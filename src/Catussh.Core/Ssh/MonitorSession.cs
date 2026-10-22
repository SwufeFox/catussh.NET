using Catussh.Core.Machine;
using Catussh.Core.Store;
using Catussh.Core.Utils;

namespace Catussh.Core.Ssh;

/// <summary>Where a monitor session stands, mirroring <c>MonitorSession.Phase</c>.</summary>
public enum MonitorPhase
{
    Connecting,
    Connected,
    /// <summary>Connected once, then lost — the last reading stays on screen.</summary>
    Lost,
    Failed,
    Closed,
}

/// <summary>One refresh's outcome, so the UI can tell "no data" from "no answer".</summary>
public sealed record MonitorReading(ServerStatus Status, DateTime At, string? Warning);

/// <summary>
/// Drives a server's monitor — the port of <c>MonitorSession</c> from
/// <c>MachineStatusView/MonitorSession.swift</c>.
///
/// This is where the SSH layer and the parsers meet: connect, run
/// <see cref="MonitorScript.Build"/> over the exec channel, hand the output to
/// <see cref="ServerStatus.Apply"/>, keep the history for the sparklines. The Swift
/// ran this on a timer with an <c>NSRemoteShell</c>; here it is a background loop
/// with a cancellation token, which is the same contract without the run loop.
/// </summary>
public sealed class MonitorSession : IDisposable
{
    private readonly RDMachine _machine;
    private readonly IReadOnlyList<SshCredential> _credentials;
    private readonly KnownHosts? _knownHosts;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private SshConnection? _connection;
    private ServerStatus _status = new();
    private bool _needsStatics = true;
    private DateTime _lastReading = DateTime.MinValue;
    private Task? _loop;

    public MonitorSession(
        RDMachine machine,
        IReadOnlyList<SshCredential> credentials,
        KnownHosts? knownHosts = null,
        TimeSpan? refreshInterval = null,
        TimeSpan? timeout = null)
    {
        _machine = machine;
        _credentials = credentials;
        _knownHosts = knownHosts;
        RefreshInterval = refreshInterval ?? TimeSpan.FromSeconds(2);
        Timeout = timeout ?? TimeSpan.FromSeconds(20);
    }

    public RDMachine Machine => _machine;

    public TimeSpan RefreshInterval { get; }

    public TimeSpan Timeout { get; }

    public MonitorPhase Phase { get; private set; } = MonitorPhase.Connecting;

    /// <summary>Last failure text, shown by the placeholder.</summary>
    public string? Error { get; private set; }

    /// <summary>Each credential attempt of the last connect, for the "tried everything" screen.</summary>
    public IReadOnlyList<SshAttempt> Attempts { get; private set; } = [];

    public MonitorHistory History { get; } = new();

    /// <summary>Raised after each reading (or failure) with the current phase.</summary>
    public event Action<MonitorSession>? Updated;

    public ServerStatus Status
    {
        get { lock (_gate) return _status; }
    }

    /// <summary>True once a reading arrived — the dashboard shows, not the placeholder.</summary>
    public bool HasData
    {
        get { lock (_gate) return _status.HasData(); }
    }

    /// <summary>Starts the connect-and-refresh loop. Safe to call once.</summary>
    public void Start()
    {
        if (_loop is not null) return;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            if (_credentials.Count == 0)
            {
                Fail("No identity to authenticate with. Add one on the Identities page.");
                return;
            }

            _connection = new SshConnection(_machine.RemoteAddress, _machine.Port, Timeout, _knownHosts);
            var (outcome, attempts) = await Task.Run(() => _connection.Connect(_credentials), token);
            Attempts = attempts;

            if (outcome != SshConnectOutcome.Success)
            {
                var detail = attempts.LastOrDefault()?.Error ?? _connection.LastError;
                Fail(outcome == SshConnectOutcome.ConnectFailed
                    ? $"Can't connect: {detail}"
                    : $"Authentication failed: {detail}");
                return;
            }

            Phase = MonitorPhase.Connected;
            Error = null;

            while (!token.IsCancellationRequested)
            {
                if (!await RefreshOnceAsync(token)) break;
                await Task.Delay(RefreshInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            Fail(SshConnection.Describe(ex));
        }
        finally
        {
            if (Phase != MonitorPhase.Failed) Phase = MonitorPhase.Closed;
            _connection?.Dispose();
            _connection = null;
            Updated?.Invoke(this);
        }
    }

    /// <summary>Runs one probe round. Returns false when the connection is gone for good.</summary>
    private async Task<bool> RefreshOnceAsync(CancellationToken token)
    {
        var connection = _connection;
        if (connection is null || !connection.IsConnected) return false;

        // Hostname and os-release cannot change while a connection stays up: read
        // them on the first round only, then reuse (resetSessionStatics).
        var includeStatic = _needsStatics;
        var script = MonitorScript.Build(includeStatic);

        string merged;
        try
        {
            // The merged script samples CPU/network twice a second apart, so it needs
            // a timeout longer than the sleep plus a slow nvidia-smi.
            merged = await Task.Run(
                () => connection.Execute(script, TimeSpan.FromSeconds(Math.Max(60, Timeout.TotalSeconds * 2))),
                token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Lost(SshConnection.Describe(ex));
            return false;
        }

        if (string.IsNullOrEmpty(merged))
        {
            var error = connection.LastError;
            if (!string.IsNullOrEmpty(error))
            {
                // A command that fails but keeps the connection is a reading with no
                // data; only a dead transport ends the loop.
                if (!connection.IsConnected)
                {
                    Lost(error);
                    return false;
                }
                lock (_gate) { }
                Publish(Status, error);
                return true;
            }
            Publish(Status, null);
            return true;
        }

        var status = Status.Apply(merged, graphics: "", includeStatic);
        if (includeStatic) _needsStatics = false;

        // The GPU probe is its own command: nvidia-smi is slow and missing on most
        // machines, and its failure must not cost the rest of the reading.
        var gpu = await Task.Run(() => connection.TryExecute(
            ServerStatusMarkers.GraphicsScript,
            TimeSpan.FromSeconds(Math.Max(30, Timeout.TotalSeconds))), token);
        if (gpu.Ok && gpu.Output.Contains("<nvidia_smi_log", StringComparison.Ordinal))
        {
            var media = GraphicsParser.FromNvidiaSmiXml(gpu.Output);
            if (media is not null) status = status with { Graphics = media };
        }

        lock (_gate) _status = status;
        History.Record(status);
        _lastReading = DateTime.UtcNow;
        Phase = MonitorPhase.Connected;

        var warning = connection.LastError;
        Publish(status, warning);
        return true;
    }

    /// <summary>Reconnects from scratch, dropping the cached statics — the Retry button.</summary>
    public void Restart()
    {
        // A fresh session is simpler and safer than reopening a dead transport; the
        // host replaces this instance (see MonitorCenter).
        _cts.Cancel();
        Start();
    }

    public void Close()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { /* already closed */ }
        Phase = MonitorPhase.Closed;
        _connection?.Disconnect();
        Updated?.Invoke(this);
    }

    private void Publish(ServerStatus status, string? warning)
    {
        lock (_gate) { }
        Updated?.Invoke(this);
    }

    private void Fail(string message)
    {
        Phase = MonitorPhase.Failed;
        Error = message;
        Updated?.Invoke(this);
    }

    private void Lost(string message)
    {
        Phase = MonitorPhase.Lost;
        Error = message;
        Updated?.Invoke(this);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _connection?.Dispose();
    }
}
