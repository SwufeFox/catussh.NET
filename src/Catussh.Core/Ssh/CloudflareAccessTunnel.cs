using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace Catussh.Core.Ssh;

/// <summary>
/// Exposes a Cloudflare Access WebSocket as a loopback TCP endpoint for SSH.NET.
/// SSH.NET owns its TCP socket; this bridge forwards its byte stream over the
/// WebSocket, matching the socket-pair adapter used by the upstream libssh2 client.
/// </summary>
internal sealed class CloudflareAccessTunnel : IDisposable
{
    private const int BufferSize = 64 * 1024;
    private readonly string _hostname;
    private readonly string _serviceTokenId;
    private readonly string _serviceTokenSecret;
    private readonly TimeSpan _timeout;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<TcpClient, ClientWebSocket> _active = new();
    private readonly Task _acceptLoop;
    private ClientWebSocket? _initialSocket;
    private string? _lastError;
    private int _disposed;

    private CloudflareAccessTunnel(
        string hostname,
        string serviceTokenId,
        string serviceTokenSecret,
        TimeSpan timeout,
        TcpListener listener,
        ClientWebSocket initialSocket)
    {
        _hostname = hostname;
        _serviceTokenId = serviceTokenId;
        _serviceTokenSecret = serviceTokenSecret;
        _timeout = timeout;
        _listener = listener;
        _initialSocket = initialSocket;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string Host => IPAddress.Loopback.ToString();
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public string? LastError => Volatile.Read(ref _lastError);

    public static CloudflareAccessTunnel Open(
        string hostname,
        string serviceTokenId,
        string serviceTokenSecret,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var uri = BuildUri(hostname);
        bool hasTokenId = !string.IsNullOrWhiteSpace(serviceTokenId);
        bool hasTokenSecret = !string.IsNullOrWhiteSpace(serviceTokenSecret);
        bool usesServiceToken = hasTokenId || hasTokenSecret;

        var initialSocket = ConnectWebSocketAsync(
            uri,
            serviceTokenId,
            serviceTokenSecret,
            usesServiceToken,
            timeout,
            cancellationToken).GetAwaiter().GetResult();

        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return new CloudflareAccessTunnel(
                uri.Authority,
                serviceTokenId,
                serviceTokenSecret,
                timeout,
                listener,
                initialSocket);
        }
        catch
        {
            initialSocket.Dispose();
            listener.Stop();
            throw;
        }
    }

    private static Uri BuildUri(string hostname)
    {
        var address = (hostname ?? "").Trim();
        if (address.Length == 0) throw new ArgumentException("Cloudflare Access hostname is empty.", nameof(hostname));

        if (address.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var suppliedUri) ||
                suppliedUri.Scheme is not ("http" or "https" or "ws" or "wss"))
                throw new ArgumentException("Cloudflare Access requires an HTTP hostname.", nameof(hostname));
            address = suppliedUri.Authority;
        }
        else if (address.Contains('/'))
        {
            address = address[..address.IndexOf('/')];
        }

        if (address.EndsWith(":443", StringComparison.Ordinal)) address = address[..^4];
        if (!Uri.TryCreate("wss://" + address + "/", UriKind.Absolute, out var uri) ||
            uri.HostNameType == UriHostNameType.Unknown)
            throw new ArgumentException("The Cloudflare Access address is not a valid hostname.", nameof(hostname));
        return uri;
    }

    private static async Task<ClientWebSocket> ConnectWebSocketAsync(
        Uri uri,
        string serviceTokenId,
        string serviceTokenSecret,
        bool usesServiceToken,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        if (usesServiceToken)
        {
            socket.Options.SetRequestHeader("Cf-Access-Client-Id", serviceTokenId);
            socket.Options.SetRequestHeader("Cf-Access-Client-Secret", serviceTokenSecret);
        }

        try
        {
            await socket.ConnectAsync(uri, linked.Token).ConfigureAwait(false);
            return socket;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TimeoutException($"Timed out reaching {uri.Host} through Cloudflare Access.", ex);
        }
        catch (Exception ex)
        {
            socket.Dispose();
            throw new InvalidOperationException(DescribeAccessFailure(uri.Host, usesServiceToken, ex), ex);
        }
    }

    private static string DescribeAccessFailure(string hostname, bool usesServiceToken, Exception error)
    {
        var status = (error as WebSocketException)?.HttpStatusCode;
        if (status is HttpStatusCode.Found or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return usesServiceToken
                ? $"Cloudflare Access did not accept the service token for {hostname}. Check its Client ID and Secret and the application's Service Auth policy."
                : $"{hostname} is protected by Cloudflare Access and requires a service token. Add one in the server's Cloudflare Access settings.";
        }

        var detail = error.Message.TrimEnd('.');
        return status is null
            ? $"Could not open a Cloudflare Access tunnel to {hostname}: {detail}."
            : $"Could not open a Cloudflare Access tunnel to {hostname} (HTTP {(int)status}): {detail}.";
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                client.NoDelay = true;
                _ = BridgeAcceptedClientAsync(client);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task BridgeAcceptedClientAsync(TcpClient client)
    {
        ClientWebSocket? socket = null;
        try
        {
            socket = Interlocked.Exchange(ref _initialSocket, null)
                ?? await ConnectWebSocketAsync(
                    BuildUri(_hostname),
                    _serviceTokenId,
                    _serviceTokenSecret,
                    !string.IsNullOrWhiteSpace(_serviceTokenId) || !string.IsNullOrWhiteSpace(_serviceTokenSecret),
                    _timeout,
                    _stop.Token).ConfigureAwait(false);
            Volatile.Write(ref _lastError, null);
            _active.TryAdd(client, socket);
            await BridgeAsync(client, socket, _stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex.Message);
        }
        finally
        {
            _active.TryRemove(client, out _);
            client.Dispose();
            socket?.Dispose();
        }
    }

    private static async Task BridgeAsync(TcpClient client, ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var bridgeStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stream = client.GetStream();
        var toWebSocket = PumpTcpToWebSocketAsync(stream, socket, bridgeStop.Token);
        var toTcp = PumpWebSocketToTcpAsync(socket, stream, bridgeStop.Token);
        await Task.WhenAny(toWebSocket, toTcp).ConfigureAwait(false);
        bridgeStop.Cancel();
        try { client.Client.Shutdown(SocketShutdown.Both); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        try { await Task.WhenAll(toWebSocket, toTcp).ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or WebSocketException or ObjectDisposedException) { }
    }

    private static async Task PumpTcpToWebSocketAsync(NetworkStream stream, ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                int count = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false);
                if (count == 0) return;
                await socket.SendAsync(buffer.AsMemory(0, count), WebSocketMessageType.Binary, endOfMessage: true, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task PumpWebSocketToTcpAsync(ClientWebSocket socket, NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (result.Count > 0)
                    await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _listener.Stop();
        Interlocked.Exchange(ref _initialSocket, null)?.Abort();
        foreach (var (client, socket) in _active)
        {
            socket.Abort();
            client.Dispose();
        }
        try { _acceptLoop.GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { }
        _stop.Dispose();
    }
}
