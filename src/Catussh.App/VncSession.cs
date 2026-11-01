using Catussh.Core.RemoteDesktop;
using Catussh.Core.Ssh;
using Catussh.Core.Store;
using Renci.SshNet;

namespace Catussh.App;

internal sealed class VncSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _startTask;
    private SshConnection? _ssh;
    private ForwardedPortLocal? _forward;
    private VncClient? _client;
    private long _frameVersion;
    private VncFrame? _frame;
    private string _phase = "connecting";
    private string? _error;
    private int _disposed;

    private VncSession(
        RDMachine machine,
        IReadOnlyList<SshCredential> credentials,
        Identity? identity,
        KnownHosts knownHosts)
    {
        _startTask = StartCoreAsync(machine, credentials, identity, knownHosts, _stop.Token);
    }

    public string Phase
    {
        get { lock (_gate) return _phase; }
    }

    public string? Error
    {
        get { lock (_gate) return _error; }
    }

    public VncFrame? Frame
    {
        get { lock (_gate) return _frame; }
    }

    public static VncSession Start(
        RDMachine machine,
        IReadOnlyList<SshCredential> credentials,
        Identity? identity,
        KnownHosts knownHosts) => new(machine, credentials, identity, knownHosts);

    public long FrameVersion
    {
        get { lock (_gate) return _frameVersion; }
    }

    private async Task StartCoreAsync(
        RDMachine machine,
        IReadOnlyList<SshCredential> credentials,
        Identity? identity,
        KnownHosts knownHosts,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!int.TryParse(machine.VncPort, out int vncPort) || vncPort is < 1 or > 65535)
                throw new InvalidOperationException("The VNC port must be between 1 and 65535.");

            string host = machine.RemoteAddress;
            int port = vncPort;
            if (machine.VncOverSSH)
            {
                if (credentials.Count == 0)
                    throw new InvalidOperationException("Add an SSH identity before opening VNC over SSH.");

                _ssh = new SshConnection(machine, TimeSpan.FromSeconds(20), knownHosts);
                var connected = await Task.Run(() => _ssh.Connect(credentials, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                if (connected.Outcome != SshConnectOutcome.Success)
                    throw new InvalidOperationException(_ssh.LastError ?? "Could not open the SSH tunnel for VNC.");

                _forward = _ssh.ForwardLocal(0, "127.0.0.1", vncPort);
                port = checked((int)_forward.BoundPort);
                if (port == 0) throw new InvalidOperationException("SSH.NET did not assign a local port for VNC.");
                host = "127.0.0.1";
            }

            string password = machine.VncPassword;
            string username = machine.VncUsername;
            if (password.Length == 0 && identity is not null)
            {
                password = SecretProtector.Unprotect(identity.Password);
                if (username.Length == 0) username = identity.Username;
            }

            var client = new VncClient(host, port, password, username);
            client.FrameUpdated += OnFrameUpdated;
            client.Failed += OnFailed;
            _client = client;
            await client.StartAsync(machine.VncShared, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_phase == "connecting") _phase = "connected";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate) _phase = "closed";
            await CleanupTransportAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _phase = "failed";
                _error = ex.Message;
            }
            await CleanupTransportAsync().ConfigureAwait(false);
        }
    }

    private void OnFrameUpdated(VncFrame frame)
    {
        lock (_gate)
        {
            _frame = frame;
            _frameVersion++;
        }
    }

    private void OnFailed(Exception error)
    {
        lock (_gate)
        {
            _phase = "failed";
            _error = error.Message;
        }
    }

    public Task PointerMoveAsync(double x, double y, CancellationToken cancellationToken = default) =>
        (_client ?? throw new InvalidOperationException("VNC is not connected.")).PointerMoveAsync(x, y, cancellationToken);
    public Task PointerButtonAsync(int button, bool down, double x, double y, CancellationToken cancellationToken = default) =>
        (_client ?? throw new InvalidOperationException("VNC is not connected.")).PointerButtonAsync(button, down, x, y, cancellationToken);

    public Task SendKeyAsync(uint keySym, bool down, CancellationToken cancellationToken = default) =>
        (_client ?? throw new InvalidOperationException("VNC is not connected.")).SendKeyAsync(keySym, down, cancellationToken);

    private async Task CleanupTransportAsync()
    {
        var client = Interlocked.Exchange(ref _client, null);
        if (client is not null) await client.CloseAsync().ConfigureAwait(false);
        Interlocked.Exchange(ref _forward, null)?.Dispose();
        Interlocked.Exchange(ref _ssh, null)?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        try { await _startTask.ConfigureAwait(false); }
        catch (Exception) { }
        await CleanupTransportAsync().ConfigureAwait(false);
        lock (_gate) _phase = "closed";
        _stop.Dispose();
    }
}
