using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Catussh.Core.RemoteDesktop;

public sealed record VncDiscoveredHost(string Name, string HostName, int Port)
{
    public string Id => Name;
    public string DisplayName => Name.Replace('_', ' ');
}

/// <summary>Browses DNS-SD `_rfb._tcp.local.` advertisements over IPv4 mDNS.</summary>
public sealed class VncDiscoveryService : IAsyncDisposable
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;
    private readonly object _gate = new();
    private readonly Dictionary<string, ServiceRecord> _services = new(StringComparer.OrdinalIgnoreCase);
    private UdpClient? _socket;
    private CancellationTokenSource? _stop;
    private Task? _run;
    private string? _failure;
    private bool _isSearching;

    public event Action? Changed;

    public bool IsSearching { get { lock (_gate) return _isSearching; } }
    public string? Failure { get { lock (_gate) return _failure; } }

    public IReadOnlyList<VncDiscoveredHost> Hosts
    {
        get
        {
            lock (_gate)
                return _services.Values
                    .Where(static item => item.Port is > 0 and <= 65535 && item.HostName.Length > 0)
                    .Select(static item => new VncDiscoveredHost(item.Name, item.HostName, item.Port))
                    .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_run is not null) return;
            _failure = null;
            _isSearching = true;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                var client = new UdpClient(AddressFamily.InterNetwork);
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
                client.JoinMulticastGroup(MulticastAddress);
                _socket = client;
                _run = Task.Run(() => RunAsync(client, _stop.Token));
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                _failure = $"Could not browse the local network for VNC services: {ex.Message}";
                _isSearching = false;
                _stop.Dispose();
                _stop = null;
                throw new InvalidOperationException(_failure, ex);
            }
        }

        await SendQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => SendQueryAsync(cancellationToken);

    public async ValueTask StopAsync()
    {
        Task? run;
        lock (_gate)
        {
            if (_run is null) return;
            _stop?.Cancel();
            _socket?.Dispose();
            run = _run;
            _run = null;
            _socket = null;
            _isSearching = false;
            _services.Clear();
        }
        try { await run.ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        lock (_gate)
        {
            _stop?.Dispose();
            _stop = null;
        }
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        var queryTask = QueryLoopAsync(client, cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                ParsePacket(result.Buffer);
                PruneExpired();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException ex) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException or ArgumentException)
        {
            lock (_gate)
            {
                _failure = $"Local VNC discovery stopped: {ex.Message}";
                _isSearching = false;
            }
            Changed?.Invoke();
        }
        finally
        {
            try { await queryTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task QueryLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var query = BuildQuery();
            await client.SendAsync(query, new IPEndPoint(MulticastAddress, MdnsPort), cancellationToken).ConfigureAwait(false);
            lock (_gate) _isSearching = false;
            Changed?.Invoke();
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendQueryAsync(CancellationToken cancellationToken)
    {
        UdpClient? client;
        lock (_gate) client = _socket;
        if (client is null) return;
        await client.SendAsync(BuildQuery(), new IPEndPoint(MulticastAddress, MdnsPort), cancellationToken).ConfigureAwait(false);
    }

    private void ParsePacket(byte[] packet)
    {
        if (packet.Length < 12) return;
        int questions = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4, 2));
        int records = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6, 2)) +
                     BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(8, 2)) +
                     BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(10, 2));
        int offset = 12;
        for (int i = 0; i < questions; i++)
        {
            _ = ReadName(packet, ref offset);
            offset = checked(offset + 4);
            if (offset > packet.Length) return;
        }

        bool changed = false;
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < records && offset < packet.Length; i++)
        {
            var owner = ReadName(packet, ref offset);
            if (offset + 10 > packet.Length) return;
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset, 2));
            uint ttl = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(offset + 4, 4));
            int length = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset + 8, 2));
            int dataOffset = offset + 10;
            if (dataOffset + length > packet.Length) return;
            offset = dataOffset + length;

            if (type == 12 && owner.Equals("_rfb._tcp.local", StringComparison.OrdinalIgnoreCase))
            {
                int ptrOffset = dataOffset;
                var instance = ReadName(packet, ref ptrOffset);
                if (ttl == 0) changed |= _services.Remove(instance);
                else
                {
                    var labels = instance.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (labels.Length >= 4 && labels[^2].Equals("_tcp", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = labels[0];
                        lock (_gate)
                        {
                            if (!_services.TryGetValue(instance, out var record))
                                _services[instance] = record = new ServiceRecord(name, "", 0, now.AddSeconds(ttl));
                            else record.ExpiresAt = now.AddSeconds(ttl);
                            changed = true;
                        }
                    }
                }
            }
            else if (type == 33)
            {
                var instanceName = owner;
                int srvOffset = dataOffset;
                if (length < 7) continue;
                ushort port = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(srvOffset + 4, 2));
                srvOffset += 6;
                string target = ReadName(packet, ref srvOffset).TrimEnd('.');
                lock (_gate)
                {
                    var labels = instanceName.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (labels.Length >= 4 && labels[^2].Equals("_tcp", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = labels[0];
                        if (!_services.TryGetValue(instanceName, out var record))
                            _services[instanceName] = record = new ServiceRecord(name, target, port, now.AddSeconds(ttl));
                        else
                        {
                            record.HostName = target;
                            record.Port = port;
                            record.ExpiresAt = now.AddSeconds(ttl);
                        }
                        changed = true;
                    }
                }
            }
        }
        if (changed) Changed?.Invoke();
    }

    private void PruneExpired()
    {
        bool changed;
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var expired = _services.Where(pair => pair.Value.ExpiresAt <= now).Select(static pair => pair.Key).ToArray();
            foreach (var key in expired) _services.Remove(key);
            changed = expired.Length > 0;
        }
        if (changed) Changed?.Invoke();
    }

    private static byte[] BuildQuery()
    {
        using var stream = new MemoryStream(64);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[4..6], 1);
        stream.Write(header);
        foreach (var label in new[] { "_rfb", "_tcp", "local" })
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        stream.WriteByte(0);
        Span<byte> question = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(question[..2], 12);
        BinaryPrimitives.WriteUInt16BigEndian(question[2..], 1);
        stream.Write(question);
        return stream.ToArray();
    }

    private static string ReadName(byte[] packet, ref int offset)
    {
        var labels = new List<string>();
        var visited = new HashSet<int>();
        int cursor = offset;
        bool jumped = false;
        while (cursor < packet.Length)
        {
            byte length = packet[cursor++];
            if (length == 0)
            {
                if (!jumped) offset = cursor;
                return string.Join('.', labels);
            }
            if ((length & 0xC0) == 0xC0)
            {
                if (cursor >= packet.Length) throw new InvalidDataException("Truncated mDNS name pointer.");
                int pointer = ((length & 0x3F) << 8) | packet[cursor++];
                if (pointer >= packet.Length || !visited.Add(pointer)) throw new InvalidDataException("Invalid mDNS name pointer.");
                if (!jumped) offset = cursor;
                cursor = pointer;
                jumped = true;
                continue;
            }
            if ((length & 0xC0) != 0 || cursor + length > packet.Length)
                throw new InvalidDataException("Invalid mDNS name label.");
            labels.Add(Encoding.UTF8.GetString(packet, cursor, length));
            cursor += length;
            if (!jumped) offset = cursor;
        }
        throw new InvalidDataException("Unterminated mDNS name.");
    }

    private sealed class ServiceRecord(string name, string hostName, int port, DateTimeOffset expiresAt)
    {
        public string Name { get; } = name;
        public string HostName { get; set; } = hostName;
        public int Port { get; set; } = port;
        public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
    }
}
