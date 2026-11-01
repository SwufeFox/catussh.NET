using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Catussh.Core.RemoteDesktop;

/// <summary>Signed REST client for the UU Remote control plane.</summary>
public sealed class UURemoteApiClient
{
    private static readonly string[] Endpoints =
    [
        UUConstants.PrimaryApi,
        UUConstants.SecondaryApi,
        UUConstants.Ipv4Api,
    ];

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _gate = new();
    private readonly HttpClient _http;
    private UURemoteCredentials _credentials;
    private int _activeEndpointIndex;
    private int _requestCounter;

    public UURemoteApiClient(UURemoteCredentials? credentials = null, HttpClient? httpClient = null)
    {
        _credentials = credentials ?? new UURemoteCredentials();
        _http = httpClient ?? SharedHttpClient;
    }

    public void UpdateCredentials(UURemoteCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        lock (_gate) _credentials = credentials;
    }

    public UURemoteCredentials CurrentCredentials
    {
        get { lock (_gate) return _credentials; }
    }

    public async Task<string> RegisterWindowsDeviceAsync(
        string name,
        string clientID,
        string systemID,
        string machineGuid,
        string os,
        string baseBoard,
        string cpu,
        IReadOnlyList<string> video,
        string mac,
        ulong memoryMiB,
        string screen,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["client_id"] = clientID,
            ["system_id"] = systemID,
            ["machine_guid"] = machineGuid,
            ["os"] = os,
            ["base_board"] = baseBoard,
            ["cpu"] = cpu,
            ["video"] = video,
            ["mac"] = mac,
            ["memory"] = memoryMiB,
            ["screen"] = screen,
            ["platform"] = 1,
            ["controllable"] = 1,
        };
        var data = await SendAsync("POST", "/api/v1/device/windows/init", body, requiresAuth: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        var id = ReadString(data, "device_id");
        return string.IsNullOrEmpty(id) ? throw MissingField("device_id") : id;
    }

    public async Task<UUQrLoginSession> GenerateQrLoginAsync(CancellationToken cancellationToken = default)
    {
        var data = await SendAsync("POST", "/api/v1/qrcode/gen/login", body: null, requiresAuth: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        var jumpUrl = ReadString(data, "qrcode_jump_url");
        var token = ReadString(data, "token");
        var ticket = ReadString(data, "status_query_ticket");
        return string.IsNullOrEmpty(jumpUrl) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(ticket)
            ? throw MissingField("qrcode_jump_url/token/status_query_ticket")
            : new UUQrLoginSession(jumpUrl, token, ticket);
    }

    public async Task<int> PollQrLoginAsync(
        string token,
        string jumpUrl,
        int lastStatus,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["token"] = token,
            ["qrcode_jump_url"] = jumpUrl,
            ["login_status"] = lastStatus,
        };
        var data = await SendAsync(
            "POST", "/api/v1/qrcode/login/status", body, requiresAuth: false,
            timeout: TimeSpan.FromSeconds(120), cancellationToken: cancellationToken).ConfigureAwait(false);
        return ReadInt(data, "status", 1);
    }

    public async Task<UUUserLogin> CompleteQrLoginAsync(string token, string jumpUrl, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["token"] = token, ["qrcode_jump_url"] = jumpUrl };
        var data = await SendAsync("POST", "/api/v1/login/by_qrcode", body, requiresAuth: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        var userID = ReadString(data, "user_id");
        var accessToken = ReadString(data, "token");
        return string.IsNullOrEmpty(userID) || string.IsNullOrEmpty(accessToken)
            ? throw MissingField("user_id/token")
            : new UUUserLogin(userID, ReadString(data, "nickname") ?? "", accessToken);
    }

    public async Task SendMobileCodeAsync(string countryCode, string mobile, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["country_code"] = countryCode,
            ["mobile"] = mobile,
            ["type"] = "login",
        };
        _ = await SendAsync("POST", "/api/v1/security/mobile/code", body, requiresAuth: false, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<UUUserLogin> CompleteMobileLoginAsync(string countryCode, string mobile, string code, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["country_code"] = countryCode,
            ["mobile"] = mobile,
            ["code"] = code,
        };
        var data = await SendAsync("POST", "/api/v1/login/by_mobile", body, requiresAuth: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        var userID = ReadString(data, "user_id");
        var accessToken = ReadString(data, "token");
        return string.IsNullOrEmpty(userID) || string.IsNullOrEmpty(accessToken)
            ? throw MissingField("user_id/token")
            : new UUUserLogin(userID, ReadString(data, "nickname") ?? "", accessToken);
    }

    public async Task<IReadOnlyList<UUDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var data = await SendAsync("GET", "/api/v1/device/groups/of/my", body: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        var devices = ReadDeviceLists(data, "desktop_devices", "mobile_devices", "tv_devices");
        if (devices.Count > 0) return devices;

        data = await SendAsync("GET", "/api/v1/device/list", body: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ReadDeviceLists(data, "current_device", "my_binded_devices", "others_shared_devices");
    }

    public async Task<UURoomConfig> JoinRoomByDeviceAsync(string deviceID, bool forceJoin = true, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["force_join"] = forceJoin };
        var data = await SendAsync(
            "POST", $"/api/v1/room/join/by_device/{Uri.EscapeDataString(deviceID)}", body,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseRoomConfig(data) ?? throw MissingField("room token and signaling servers");
    }

    public async Task ClearRoomByDeviceAsync(string deviceID, CancellationToken cancellationToken = default)
    {
        _ = await SendAsync(
            "POST", $"/api/v1/room/clear/by_device/{Uri.EscapeDataString(deviceID)}", new Dictionary<string, object?>(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> SendAsync(
        string method,
        string path,
        object? body,
        bool requiresAuth = true,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var requestBody = string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || body is null
            ? Array.Empty<byte>()
            : JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        var timeoutValue = timeout ?? TimeSpan.FromSeconds(15);
        Exception? lastTransportError = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var endpoint = NextEndpoint();
            try
            {
                var result = await PerformAsync(method, path, requestBody, requiresAuth, endpoint, timeoutValue, cancellationToken).ConfigureAwait(false);
                return Unwrap(result.Body, result.StatusCode, path);
            }
            catch (UUApiException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                lastTransportError = ex;
                if (attempt == 0)
                {
                    RotateEndpoint();
                    continue;
                }
                break;
            }
        }

        throw new UUApiException(
            $"UU Remote transport failed: {lastTransportError?.Message ?? "unknown transport failure"}",
            code: -1,
            inner: lastTransportError);
    }

    private async Task<(byte[] Body, int StatusCode)> PerformAsync(
        string method,
        string path,
        byte[] body,
        bool requiresAuth,
        string endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var credentials = CurrentCredentials;
        var headers = UURequestSigner.SignedHeaders(credentials, method, path, body);
        using var request = new HttpRequestMessage(new HttpMethod(method), endpoint + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", $"UURemote/{UUConstants.VersionName} (Windows NT; {RuntimeInformation.ProcessArchitecture})");
        foreach (var (name, value) in headers)
        {
            if (!requiresAuth && name is "Authorization" or "Cookie" or "X-CSRF-TOKEN") continue;
            if (name.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation(name, value);
            else if (!request.Headers.TryAddWithoutValidation(name, value))
                request.Content?.Headers.TryAddWithoutValidation(name, value);
        }
        if (body.Length > 0)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token).ConfigureAwait(false);
        return (await response.Content.ReadAsByteArrayAsync(timeoutSource.Token).ConfigureAwait(false), (int)response.StatusCode);
    }

    private static JsonElement Unwrap(byte[] bytes, int status, string path)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (JsonException ex)
        {
            throw new UUApiException($"UU Remote returned a non-JSON response for {path} (HTTP {status}).", -1, status, inner: ex);
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new UUApiException($"UU Remote returned a non-object response for {path} (HTTP {status}).", -1, status);

            var code = ReadInt(root, "code", -1);
            var message = ReadString(root, "msg") ?? "";
            if (code == UUConstants.InvalidTokenCode)
                throw new UUApiException("UU Remote login token has expired.", code, status, tokenExpired: true);

            if (status != (int)HttpStatusCode.OK || code != 0)
            {
                var detail = status == 422 ? ValidationDetails(root) : "";
                var summary = message.Length == 0 ? $"code {code}" : $"{message} (code {code})";
                var text = string.Join("; ", new[] { summary, detail }.Where(static value => value.Length > 0));
                throw new UUApiException(text, code, status);
            }

            return root.TryGetProperty("data", out var data) ? data.Clone() : default;
        }
    }

    private static string ValidationDetails(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return "";
        var details = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            var location = "";
            if (item.TryGetProperty("loc", out var loc) && loc.ValueKind == JsonValueKind.Array)
                location = string.Join(' ', loc.EnumerateArray().Select(static part => part.ToString()));
            var detail = ReadString(item, "msg") ?? "";
            var combined = string.Join(": ", new[] { location, detail }.Where(static value => value.Length > 0));
            if (combined.Length > 0) details.Add(combined);
        }
        return string.Join("; ", details);
    }

    private string NextEndpoint()
    {
        lock (_gate)
        {
            _requestCounter++;
            if (_requestCounter % 100 == 0 && _activeEndpointIndex != 0) _activeEndpointIndex = 0;
            return Endpoints[_activeEndpointIndex];
        }
    }

    private void RotateEndpoint()
    {
        lock (_gate) _activeEndpointIndex = (_activeEndpointIndex + 1) % Endpoints.Length;
    }

    private static IReadOnlyList<UUDeviceInfo> ReadDeviceLists(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) return [];
        var devices = new List<UUDeviceInfo>();
        foreach (var field in fields)
        {
            if (!value.TryGetProperty(field, out var list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in list.EnumerateArray())
            {
                var device = ParseDevice(item);
                if (device is not null) devices.Add(device);
            }
        }
        return devices;
    }

    private static UUDeviceInfo? ParseDevice(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var deviceID = ReadString(value, "device_id");
        if (string.IsNullOrEmpty(deviceID)) return null;
        var participants = new List<UUDeviceParticipant>();
        if (value.TryGetProperty("participants_info", out var participantArray) && participantArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in participantArray.EnumerateArray())
            {
                participants.Add(new UUDeviceParticipant(
                    ReadString(item, "client_id") ?? "",
                    ReadString(item, "device_id") ?? "",
                    ReadString(item, "alias") ?? "",
                    ReadInt(item, "platform", 0),
                    ReadString(item, "user_join_type") ?? "",
                    ReadString(item, "controlled_time") ?? ""));
            }
        }

        return new UUDeviceInfo
        {
            DeviceID = deviceID,
            Alias = ReadString(value, "alias") ?? "",
            Status = ReadString(value, "status") ?? "",
            Platform = ReadInt(value, "platform", 0),
            Controllable = ReadFlag(value, "controllable"),
            SupportWol = ReadFlag(value, "support_wol"),
            WallpaperUrl = ReadString(value, "wallpaper_url"),
            VersionName = ReadString(value, "version_name"),
            Participants = participants,
        };
    }

    private static UURoomConfig? ParseRoomConfig(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var config = value.TryGetProperty("room_config", out var nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : value;
        var token = ReadString(config, "token");
        if (string.IsNullOrEmpty(token)) return null;

        var servers = new List<string>();
        AddStrings(config, "signal_servers", servers);
        AddString(config, "signaling_server", servers);
        AddStrings(config, "signaling_list", servers);
        servers = servers.Where(static item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToList();
        if (servers.Count == 0) return null;

        var appData = config.TryGetProperty("app_data", out var appDataElement) && appDataElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? appDataElement.GetRawText()
            : null;
        return new UURoomConfig
        {
            Token = token,
            SignalServers = servers,
            TimeoutMilliseconds = ReadInt(config, "timeout", ReadInt(config, "ws_connect_timeout_ms", 16_000)),
            SignalReconnectDelayMilliseconds = ReadInt(config, "signal_reconnect_delay", ReadInt(config, "streamer_retry_delta_ms", 1_000)),
            ReportToken = ReadString(config, "report_token"),
            ReportUrl = ReadString(config, "report_url"),
            AppDataJson = appData,
        };
    }

    private static void AddStrings(JsonElement value, string property, ICollection<string> destination)
    {
        if (!value.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array) return;
        foreach (var item in items.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text) destination.Add(text);
    }

    private static void AddString(JsonElement value, string property, ICollection<string> destination)
    {
        if (ReadString(value, property) is { Length: > 0 } text) destination.Add(text);
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString()
            : null;

    private static int ReadInt(JsonElement value, string property, int fallback) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field)
            ? ReadInt(field, fallback)
            : fallback;

    private static int ReadInt(JsonElement value, int fallback) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out var number) => number,
        JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
        _ => fallback,
    };

    private static bool ReadFlag(JsonElement value, string property)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out var field)) return false;
        return field.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number when field.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String when int.TryParse(field.GetString(), out var number) => number != 0,
            _ => false,
        };
    }

    private static UUApiException MissingField(string field) => new($"UU Remote response is missing {field}.", -1);

    private static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };
}
