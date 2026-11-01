using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Catussh.Core.RemoteDesktop;

public static class UURequestSigner
{
    public static IReadOnlyDictionary<string, string> SignedHeaders(
        UURemoteCredentials credentials,
        string method,
        string pathWithQuery,
        ReadOnlySpan<byte> body,
        long? timestamp = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Param-CHN"] = UUConstants.Channel,
            ["X-Param-PKGN"] = UUConstants.PackageName,
            ["X-Param-PLAT"] = "1",
            ["X-Param-VC"] = UUConstants.VersionCode,
            ["X-Param-VN"] = UUConstants.VersionName,
            ["X-Param-TS"] = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["X-Param-CNT"] = "CN",
            ["X-Param-LANG"] = "zh-CN",
        };
        Add(headers, "X-Param-client-id", credentials.ClientID);
        Add(headers, "X-Param-device-id", credentials.DeviceID);
        Add(headers, "X-Param-system-id", credentials.SystemID);
        Add(headers, "X-Param-user-id", credentials.UserID);
        Add(headers, "X-Param-guest-id", credentials.GuestID);

        var signatureBase = SignatureBase(method, pathWithQuery, headers, body);
        var key = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(credentials.SignKey) ? UUConstants.SigningKey : credentials.SignKey);
        headers["X-Param-SIGN"] = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signatureBase)));
        if (!string.IsNullOrEmpty(credentials.Token))
        {
            headers["Authorization"] = $"Bearer {credentials.Token}";
            headers["Cookie"] = $"nrd_access_token={credentials.Token};nrd_csrf_access_token={credentials.Csrf}";
            Add(headers, "X-CSRF-TOKEN", credentials.Csrf);
        }
        return headers;
    }

    public static string SignatureBase(
        string method,
        string pathWithQuery,
        IReadOnlyDictionary<string, string> headers,
        ReadOnlySpan<byte> body)
    {
        var xParams = headers
            .Where(pair => pair.Key.StartsWith("x-param-", StringComparison.OrdinalIgnoreCase) &&
                           !pair.Key.Equals("x-param-sign", StringComparison.OrdinalIgnoreCase))
            .Select(pair => (Key: pair.Key.ToLowerInvariant(), pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}");
        return method.ToUpperInvariant() + pathWithQuery + string.Join("&", xParams) + Encoding.UTF8.GetString(body);
    }

    public static JsonDocument? JwtPayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
        try { return JsonDocument.Parse(Convert.FromBase64String(payload)); }
        catch (FormatException) { return null; }
        catch (JsonException) { return null; }
    }

    public static string? CsrfFrom(string token)
    {
        using var payload = JwtPayload(token);
        return payload is not null && payload.RootElement.TryGetProperty("csrf", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static string? UserIDFrom(string token)
    {
        using var payload = JwtPayload(token);
        if (payload is null) return null;
        foreach (var key in new[] { "uid", "user_id", "sub" })
        {
            if (!payload.RootElement.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString())) return value.GetString();
            if (value.ValueKind == JsonValueKind.Number) return value.ToString();
        }
        return null;
    }

    private static void Add(IDictionary<string, string> headers, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value)) headers[name] = value;
    }
}
