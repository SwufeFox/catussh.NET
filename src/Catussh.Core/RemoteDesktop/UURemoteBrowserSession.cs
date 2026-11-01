using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Catussh.Core.RemoteDesktop;

public sealed record UURemoteSignalEvent(long Sequence, string Name, JsonElement Data);

/// <summary>UU Remote room signaling for a browser-owned WebRTC peer connection.</summary>
public sealed class UURemoteBrowserSession : IAsyncDisposable
{
    private const int MaximumMessageBytes = 4 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly UURoomConfig _room;
    private readonly string _deviceID;
    private readonly string _localDeviceID;
    private readonly string _appControlID = Guid.NewGuid().ToString().ToUpperInvariant();
    private readonly string _initialIceID = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Queue<UURemoteSignalEvent> _signals = new();
    private ClientWebSocket? _socket;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private TaskCompletionSource<JsonElement>? _controlAck;
    private PendingBinaryEvent? _pendingBinary;
    private JsonElement _controlResult;
    private IReadOnlyList<object> _iceServers = [];
    private string _remoteClientID = "";
    private string _iceID = "";
    private string? _failure;
    private string _phase = "connecting";
    private long _sequence;
    private readonly bool _terminalOnly;
    private readonly int _targetPlatform;
    private bool _disposed;

    private UURemoteBrowserSession(UURoomConfig room, string deviceID, string localDeviceID, bool terminalOnly, int targetPlatform)
    {
        _room = room;
        _deviceID = deviceID;
        _localDeviceID = localDeviceID;
        _terminalOnly = terminalOnly;
        _targetPlatform = targetPlatform;
    }

    public string DeviceID => _deviceID;
    public bool TerminalOnly => _terminalOnly;
    public int TargetPlatform => _targetPlatform;
    public string Phase { get { lock (_gate) return _phase; } }
    public string? Failure { get { lock (_gate) return _failure; } }

    public static async Task<UURemoteBrowserSession> ConnectAsync(
        UURoomConfig room,
        string deviceID,
        string localDeviceID,
        bool terminalOnly = false,
        int targetPlatform = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(room);
        if (string.IsNullOrWhiteSpace(deviceID)) throw new ArgumentException("UU Remote device ID is empty.", nameof(deviceID));
        if (string.IsNullOrWhiteSpace(localDeviceID)) throw new InvalidOperationException("UU Remote client device ID is missing.");
        var session = new UURemoteBrowserSession(room, deviceID, localDeviceID, terminalOnly, targetPlatform);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session._stop.Token);
        try
        {
            await session.ConnectSignalAsync(linked.Token).ConfigureAwait(false);
            await session.RequestControlAsync(terminalOnly, linked.Token).ConfigureAwait(false);
            lock (session._gate) session._phase = "connecting";
            session._heartbeatTask = Task.Run(() => session.HeartbeatAsync(session._stop.Token));
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public object Snapshot(long afterSequence)
    {
        lock (_gate)
        {
            return new
            {
                phase = _phase,
                error = _failure,
                version = _sequence,
                bootstrap = _remoteClientID.Length == 0 ? null : new
                {
                    deviceID = _deviceID,
                    appControlID = _appControlID,
                    iceID = _iceID,
                    clientID = _remoteClientID,
                    iceServers = _iceServers,
                    targetPlatform = _targetPlatform,
                    forceRelay = ReadBool(_controlResult, "force_relay"),
                },
                signals = _signals.Where(item => item.Sequence > afterSequence).ToArray(),
            };
        }
    }

    public async Task SendSOACAsync(JsonElement body, CancellationToken cancellationToken = default)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new ArgumentException("SOAC must be a JSON object.", nameof(body));
        string type = body.TryGetProperty("type", out var typeValue) ? typeValue.GetString() ?? "" : "";
        if (type is not ("offer" or "answer" or "candidate"))
            throw new ArgumentException("SOAC type must be offer, answer, or candidate.", nameof(body));
        var data = JsonNode.Parse(body.GetRawText())!.AsObject();
        data["ice_id"] = _iceID;
        data["app_control_id"] = _appControlID;
        var envelope = new Dictionary<string, object?>
        {
            ["client_id"] = _remoteClientID,
            ["data"] = data,
        };
        var packet = JsonSerializer.Serialize(new object[] { "soac", envelope });
        await SendTextAsync("42" + packet, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        var socket = _socket;
        if (socket is { State: WebSocketState.Open or WebSocketState.CloseReceived })
        {
            try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException) { }
        }
        if (_receiveTask is not null)
        {
            try { await _receiveTask.ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
        }
        if (_heartbeatTask is not null)
        {
            try { await _heartbeatTask.ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException or InvalidOperationException) { }
        }
        socket?.Dispose();
        _sendGate.Dispose();
        _stop.Dispose();
        lock (_gate) _phase = "closed";
    }

    private async Task ConnectSignalAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var server in _room.SignalServers)
        {
            var namespaceConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            socket.Options.SetRequestHeader("X-NRD-AUTH", _room.Token);
            socket.Options.SetRequestHeader("X-NRD-CONTROLLING", "0");
            socket.Options.SetRequestHeader("streamer_version", "V3.1.14");
            socket.Options.SetRequestHeader("streamer_flag", "{\"sdp_flags\":{\"gzip_sdp\":true}}");
            var endpoint = server.Contains("://", StringComparison.Ordinal) ? server.TrimEnd('/') : "wss://" + server.TrimEnd('/');
            if (endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) endpoint = "wss://" + endpoint[8..];
            if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) endpoint = "ws://" + endpoint[7..];
            if (!Uri.TryCreate(endpoint + "/socket.io/?transport=websocket&EIO=4", UriKind.Absolute, out var uri))
            {
                lastError = new InvalidOperationException("UU Remote returned an invalid signaling server address.");
                continue;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(_room.TimeoutMilliseconds, 1000, 120_000)));
                await socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
                _socket = socket;
                _receiveTask = Task.Run(() => ReceiveLoopAsync(socket, _stop.Token, namespaceConnected));
                await namespaceConnected.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                lock (_gate) _phase = "connecting";
                return;
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or InvalidOperationException)
            {
                lastError = ex;
                socket.Dispose();
                if (_receiveTask is not null)
                {
                    try { await _receiveTask.ConfigureAwait(false); }
                    catch (Exception receiveError) when (receiveError is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
                }
                if (ReferenceEquals(_socket, socket)) _socket = null;
        }
        }

        throw new InvalidOperationException($"Could not connect to a UU Remote signaling server: {lastError?.Message ?? "no signaling servers were returned"}", lastError);
    }

    private async Task RequestControlAsync(bool terminalOnly, CancellationToken cancellationToken)
    {
        _controlAck = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] options = BuildConnectOptions(terminalOnly ? 8 : 1, _localDeviceID);
        string streamerData = JsonSerializer.Serialize(new
        {
            control_id = _appControlID,
            device_capability = new
            {
                display_info = terminalOnly ? Array.Empty<object>() : new object[] { new { id = 1, fps = 30, type = 0, hdr = 0 } },
                ice_id = "",
                video_codec_capability = terminalOnly ? Array.Empty<object>() : new object[]
                {
                    new { video_codec = 1, chroma_sampling = 1, codec_impl = 34, bit_depth = 8, width = 3840, height = 2160 },
                    new { video_codec = 1, chroma_sampling = 3, codec_impl = 34, bit_depth = 8, width = 3840, height = 2160 },
                },
            },
        });
        var header = new Dictionary<string, object?>
        {
            ["app_control_id"] = _appControlID,
            ["app_data"] = new Dictionary<string, object> { ["_placeholder"] = true, ["num"] = 0 },
            ["streamer_data"] = streamerData,
        };
        string packet = "451-10" + JsonSerializer.Serialize(new object[] { "control", header });
        await SendTextAsync(packet, cancellationToken).ConfigureAwait(false);
        var binary = new byte[options.Length + 1];
        binary[0] = 0x04;
        options.CopyTo(binary.AsSpan(1));
        await SendBinaryAsync(binary, cancellationToken).ConfigureAwait(false);

        var args = await _controlAck.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() < 2 || args[0].GetString() != "success")
            throw new InvalidOperationException("UU Remote rejected the control request.");
        var result = args[1];
        int code = ReadInt(result, "code", 0);
        if (code != 0)
            throw new InvalidOperationException(ReadString(result, "msg") ?? $"UU Remote control request failed (code {code}).");
        if (ReadString(result, "app_control_id") is { Length: > 0 } returned && returned != _appControlID)
            throw new InvalidOperationException("UU Remote control acknowledgement belongs to another session.");
        _remoteClientID = ReadString(result, "client_id") ?? "";
        _iceID = ReadString(result, "ice_id") is { Length: > 0 } assigned ? assigned : _initialIceID;
        if (_remoteClientID.Length == 0) throw new InvalidOperationException("UU Remote control acknowledgement is missing its client ID.");
        _controlResult = result.Clone();
        _iceServers = ReadIceServers(result);
        lock (_gate) _phase = "connecting";
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken, TaskCompletionSource namespaceConnected)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (message.Length + result.Count > MaximumMessageBytes) throw new InvalidDataException("UU Remote signaling message exceeded 4 MiB.");
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var bytes = message.ToArray();
                message.SetLength(0);
                if (result.MessageType == WebSocketMessageType.Text)
                    await HandleEnginePacketAsync(Encoding.UTF8.GetString(bytes), cancellationToken, namespaceConnected).ConfigureAwait(false);
                else if (result.MessageType == WebSocketMessageType.Binary)
                    HandleBinaryAttachment(bytes);
            }
            if (!cancellationToken.IsCancellationRequested) throw new WebSocketException("UU Remote signaling server closed the connection.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is WebSocketException or IOException or InvalidDataException or JsonException or ObjectDisposedException)
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _failure = ex.Message;
                    _phase = "failed";
                }
            }
            namespaceConnected.TrySetException(ex);
            _controlAck?.TrySetException(ex);
        }
    }

    private async Task HandleEnginePacketAsync(string packet, CancellationToken cancellationToken, TaskCompletionSource namespaceConnected)
    {
        if (packet.Length == 0) return;
        switch (packet[0])
        {
            case '0':
                await SendTextAsync("40", cancellationToken).ConfigureAwait(false);
                return;
            case '1':
                throw new WebSocketException("UU Remote signaling closed the Engine.IO connection.");
            case '2':
                await SendTextAsync("3" + packet[1..], cancellationToken).ConfigureAwait(false);
                return;
            case '4':
                HandleSocketPacket(packet[1..], namespaceConnected);
                return;
            case '3':
            case '5':
            case '6':
                return;
            default:
                throw new InvalidDataException("UU Remote sent an unsupported Engine.IO packet.");
        }
    }

    private void HandleSocketPacket(string packet, TaskCompletionSource namespaceConnected)
    {
        if (packet.Length == 0) return;
        char kind = packet[0];
        string rest = packet[1..];
        if (rest.StartsWith("/", StringComparison.Ordinal))
        {
            int comma = rest.IndexOf(',');
            if (comma < 0) throw new InvalidDataException("UU Remote Socket.IO namespace packet is malformed.");
            rest = rest[(comma + 1)..];
        }

        if (kind == '0')
        {
            namespaceConnected.TrySetResult();
            return;
        }
        if (kind == '3' || kind == '5' && !rest.Contains('-', StringComparison.Ordinal))
        {
            int digits = 0;
            while (digits < rest.Length && char.IsAsciiDigit(rest[digits])) digits++;
            if (digits == 0 || !ulong.TryParse(rest.AsSpan(0, digits), out ulong ackID)) return;
            var json = JsonDocument.Parse(rest[digits..]).RootElement.Clone();
            if (ackID == 10) _controlAck?.TrySetResult(json);
            return;
        }
        if (kind == '2')
        {
            AddSignal(rest);
            return;
        }
        if (kind == '5')
        {
            int dash = rest.IndexOf('-');
            if (dash <= 0 || !int.TryParse(rest.AsSpan(0, dash), out int count) || count is < 1 or > 8)
                throw new InvalidDataException("UU Remote binary signaling event is malformed.");
            string eventPacket = rest[(dash + 1)..];
            int idEnd = 0;
            while (idEnd < eventPacket.Length && char.IsAsciiDigit(eventPacket[idEnd])) idEnd++;
            if (idEnd >= eventPacket.Length || eventPacket[idEnd] != '[')
                throw new InvalidDataException("UU Remote binary signaling event has no argument array.");
            var array = JsonNode.Parse(eventPacket[idEnd..])?.AsArray() ?? throw new InvalidDataException("UU Remote binary signaling args are malformed.");
            if (array.Count == 0 || array[0]?.GetValue<string>() is not { Length: > 0 } eventName)
                throw new InvalidDataException("UU Remote binary signaling event has no name.");
            _pendingBinary = new PendingBinaryEvent(eventName, array, count);
        }
    }

    private void AddSignal(string json)
    {
        using var document = JsonDocument.Parse(json);
        var array = document.RootElement;
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0 || array[0].ValueKind != JsonValueKind.String) return;
        var name = array[0].GetString()!;
        var data = array.GetArrayLength() > 1 ? array[1].Clone() : JsonDocument.Parse("{}").RootElement.Clone();
        AddSignal(name, data);
    }

    private void HandleBinaryAttachment(byte[] bytes)
    {
        var pending = _pendingBinary;
        if (pending is null) return;
        var payload = bytes.AsSpan();
        if (payload.Length > 0 && payload[0] == 0x04) payload = payload[1..];
        pending.Attachments.Add(Convert.ToBase64String(payload));
        if (pending.Attachments.Count < pending.Expected) return;
        _pendingBinary = null;
        JsonNode? data = pending.Arguments.Count > 1 ? pending.Arguments[1]?.DeepClone() : new JsonObject();
        data = Hydrate(data, pending.Attachments);
        using var document = JsonDocument.Parse(data?.ToJsonString() ?? "{}");
        AddSignal(pending.Name, document.RootElement.Clone());
    }

    private void AddSignal(string name, JsonElement data)
    {
        lock (_gate)
        {
            if (_signals.Count == 512) _signals.Dequeue();
            _signals.Enqueue(new UURemoteSignalEvent(++_sequence, name, data));
            if (name is "be-controlled" or "control") _phase = "connected";
        }
    }

    private async Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(false);
                await SendTextAsync("2", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            lock (_gate) { if (!_disposed) { _failure = ex.Message; _phase = "failed"; } }
        }
    }

    private async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("UU Remote signaling is not connected.");
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (socket.State != WebSocketState.Open) throw new WebSocketException("UU Remote signaling is not open.");
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally { _sendGate.Release(); }
    }

    private async Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("UU Remote signaling is not connected.");
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (socket.State != WebSocketState.Open) throw new WebSocketException("UU Remote signaling is not open.");
            await socket.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
        }
        finally { _sendGate.Release(); }
    }

    private static byte[] BuildConnectOptions(int captureType, string localDeviceID)
    {
        var options = new List<byte>();
        VarintField(options, 1, (ulong)captureType);
        VarintField(options, 2, ulong.MaxValue);
        if (captureType == 1)
        {
            var capture = new List<byte>();
            VarintField(capture, 1, 1); // 30 fps tier
            VarintField(capture, 2, 1); // standard quality
            VarintField(capture, 4, 2); // follow local resolution
            MessageField(capture, 5, Resolution(1920, 1080));
            MessageField(capture, 6, []); // upstream's empty choose-resolution message
            VarintField(capture, 7, 1); // 4:2:0
            VarintField(capture, 9, 1); // HDR capability
            VarintField(capture, 11, 30);
            MessageField(options, 3, capture);
            foreach (int chroma in new[] { 1, 3 })
            {
                var decoder = new List<byte>();
                VarintField(decoder, 1, 30);
                VarintField(decoder, 2, 1); // H.264
                VarintField(decoder, 3, 3840);
                VarintField(decoder, 4, 2160);
                VarintField(decoder, 5, (ulong)chroma);
                MessageField(options, 4, decoder);
            }
            MessageField(options, 6, Resolution(1920, 1080, 30));
            MessageField(options, 7, Resolution(1920, 1080));
        }
        else
        {
            var decoder = new List<byte>();
            VarintField(decoder, 1, 30);
            VarintField(decoder, 2, 1);
            VarintField(decoder, 3, 3840);
            VarintField(decoder, 4, 2160);
            MessageField(options, 4, decoder);
        }
        VarintField(options, 8, 3); // Windows client
        StringField(options, 9, localDeviceID);
        VarintField(options, 10, 1); // normal control
        if (captureType == 1)
        {
            var flags = new List<byte>();
            foreach (var (field, value) in new (int, int)[] { (1, 6), (2, 2), (3, 1), (4, 2), (6, 2), (7, 2), (8, 3), (9, 1) })
                VarintField(flags, field, (ulong)value);
            MessageField(options, 11, flags);
        }
        StringField(options, 12, "4.42.0");
        return options.ToArray();
    }

    private static List<byte> Resolution(int width, int height)
    {
        var result = new List<byte>();
        VarintField(result, 1, (ulong)width);
        VarintField(result, 2, (ulong)height);
        return result;
    }

    private static List<byte> Resolution(int width, int height, int fps)
    {
        var result = Resolution(width, height);
        VarintField(result, 3, (ulong)fps);
        return result;
    }

    private static JsonNode? Hydrate(JsonNode? node, IReadOnlyList<string> attachments)
    {
        if (node is JsonObject obj)
        {
            if (obj["_placeholder"] is JsonValue placeholder && placeholder.TryGetValue<bool>(out bool isPlaceholder) && isPlaceholder &&
                obj["num"] is JsonValue number && number.TryGetValue<int>(out int index) && index >= 0 && index < attachments.Count)
                return JsonValue.Create(attachments[index]);

            foreach (var child in obj.ToArray())
            {
                var hydrated = Hydrate(child.Value, attachments);
                if (!ReferenceEquals(child.Value, hydrated)) obj[child.Key] = hydrated;
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                var child = array[i];
                var hydrated = Hydrate(child, attachments);
                if (!ReferenceEquals(child, hydrated)) array[i] = hydrated;
            }
        }
        return node;
    }

    private static void VarintField(List<byte> output, int field, ulong value)
    {
        Varint(output, (ulong)(field << 3));
        Varint(output, value);
    }

    private static void StringField(List<byte> output, int field, string value) => MessageField(output, field, Encoding.UTF8.GetBytes(value));

    private static void MessageField(List<byte> output, int field, IEnumerable<byte> payload)
    {
        var bytes = payload.ToArray();
        Varint(output, (ulong)((field << 3) | 2));
        Varint(output, (ulong)bytes.Length);
        output.AddRange(bytes);
    }

    private static void Varint(List<byte> output, ulong value)
    {
        while (value >= 0x80)
        {
            output.Add((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }
        output.Add((byte)value);
    }


    private static IReadOnlyList<object> ReadIceServers(JsonElement result)
    {
        if (!result.TryGetProperty("iceServers", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        var servers = new List<object>();
        foreach (var server in list.EnumerateArray())
        {
            if (server.ValueKind != JsonValueKind.Object || !server.TryGetProperty("urls", out var urls)) continue;
            var item = new Dictionary<string, object?>();
            if (urls.ValueKind == JsonValueKind.String) item["urls"] = new[] { urls.GetString()! };
            else if (urls.ValueKind == JsonValueKind.Array) item["urls"] = urls.EnumerateArray().Where(static value => value.ValueKind == JsonValueKind.String).Select(static value => value.GetString()!).ToArray();
            else continue;
            if (server.TryGetProperty("username", out var username) && username.ValueKind == JsonValueKind.String) item["username"] = username.GetString();
            if (server.TryGetProperty("credential", out var credential) && credential.ValueKind == JsonValueKind.String) item["credential"] = credential.GetString();
            servers.Add(item);
        }
        return servers;
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static int ReadInt(JsonElement value, string property, int fallback) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) && field.TryGetInt32(out var number) ? number : fallback;

    private static bool ReadBool(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.True;

    private sealed class PendingBinaryEvent(string name, JsonArray arguments, int expected)
    {
        public string Name { get; } = name;
        public JsonArray Arguments { get; } = arguments;
        public int Expected { get; } = expected;
        public List<string> Attachments { get; } = [];
    }
}
