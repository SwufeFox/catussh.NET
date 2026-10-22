using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Catussh.App;

/// <summary>
/// Serves the Monitor page's snapshot over plain HTTP on loopback.
///
/// The first attempt answered <c>https://catussh.local/monitor-snapshot</c> through
/// <c>WebResourceRequested</c>, but requests resolved by
/// <c>SetVirtualHostNameToFolderMapping</c> never reach the network stack, so the
/// event never fires — not even for the document itself (verified with a <c>*</c>
/// filter). A loopback listener sidesteps WebView2's interception entirely and can
/// be checked independently with curl while the window is open.
/// </summary>
internal sealed class MonitorBridge : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Core.Machine.MonitorHistory _history = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private int _ticks;

    /// <summary>Ephemeral loopback port, assigned in <see cref="Start"/>.</summary>
    public int Port { get; private set; }

    /// <summary>Demo target shown in the Facts row; the parsers have no address field.</summary>
    public const string DemoAddress = "203.0.113.10";

    public void Start()
    {
        Port = FindFreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        // Pre-seed one screen of history so the sparklines draw a curve on the
        // very first paint instead of a single dot.
        for (var i = 0; i < 24; i++)
        {
            _history.Record(SnapshotAt(i));
        }
        _ticks = 24;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => ServeAsync(token), token);

        Console.Error.WriteLine($"[catussh] monitor bridge on http://127.0.0.1:{Port}/monitor-snapshot");
    }

    /// <summary>
    /// The demo fixture replayed with a slow traffic wave, so live polls keep the
    /// throughput cards moving. Shared with <c>--dump-snapshot</c> so the baked
    /// fixture and the live bridge show the same server.
    /// </summary>
    public static Core.Machine.ServerStatus SnapshotAt(int tick)
    {
        var status = MainWindow.DemoSnapshot();
        var wave = 1 + 0.45 * Math.Sin(tick / 3.0);
        return status with
        {
            Network = new Core.Machine.NetworkInfo(
                status.Network.Elements
                    .Select(e => new Core.Machine.NetworkInfo.NetworkInfoElement(
                        e.Device,
                        (int)(e.RxBytesPerSec * wave),
                        (int)(e.TxBytesPerSec * (2 - wave))))
                    .ToList()),
        };
    }

    private async Task ServeAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break; // Listener stopped.
            }
            _ = Task.Run(() => Handle(context), token);
        }
    }

    private void Handle(HttpListenerContext context)
    {
        try
        {
            if (!string.Equals(context.Request.Url?.AbsolutePath, "/monitor-snapshot", StringComparison.Ordinal))
            {
                context.Response.StatusCode = 404;
                return;
            }

            // The page runs on https://catussh.local, so cross-origin needs an
            // explicit allowance. Loopback-only listener: "*" cannot leak anywhere.
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Cache-Control", "no-store");

            var tick = Interlocked.Increment(ref _ticks);
            var status = SnapshotAt(tick);
            _history.Record(status);
            var bytes = Encoding.UTF8.GetBytes(MonitorPayload.Build(status, _history, DemoAddress));

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.StatusCode = 200;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[catussh] bridge error: {ex.Message}");
        }
        finally
        {
            try { context.Response.OutputStream.Close(); }
            catch (Exception) { /* Client already gone. */ }
        }
    }

    /// <summary>Binds port 0 for a moment to let the OS pick, then releases it.</summary>
    private static int FindFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts?.Cancel(); }
        catch (Exception) { /* Shutting down; nothing to report. */ }
        try { _listener.Stop(); }
        catch (Exception) { /* Already stopped. */ }
        _listener.Close();
        _cts?.Dispose();
    }
}
