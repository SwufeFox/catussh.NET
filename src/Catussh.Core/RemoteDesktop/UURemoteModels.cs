using System.Text.Json.Serialization;

namespace Catussh.Core.RemoteDesktop;

public static class UUConstants
{
    public const string SigningKey = "alWiSzXZTLu3WfFnw13uBru3";
    public const string PrimaryApi = "https://api.nrd.nie.163.com";
    public const string SecondaryApi = "https://api-dcdn.nrd.nie.163.com";
    public const string Ipv4Api = "https://api-ipv4.nrd.nie.163.com";
    public const string VersionName = "4.42.0.2770";
    public const string VersionCode = "2770";
    public const string PackageName = "com.netease.gameviewer";
    public const string Channel = "gwqd";
    public const int InvalidTokenCode = 1120;
}

/// <summary>Persisted identity for the UU Remote PC client.</summary>
public sealed record UURemoteCredentials
{
    public string UserID { get; init; } = "";
    public string DeviceID { get; init; } = "";
    public string ClientID { get; init; } = "";
    public string SystemID { get; init; } = "";
    public string Token { get; init; } = "";
    public string Csrf { get; init; } = "";
    public string? GuestID { get; init; }
    public string SignKey { get; init; } = UUConstants.SigningKey;

    [JsonIgnore]
    public bool IsComplete =>
        UserID.Length > 0 && DeviceID.Length > 0 && ClientID.Length > 0 &&
        SystemID.Length > 0 && Token.Length > 0 && Csrf.Length > 0;
}

public sealed record UUDeviceParticipant(
    string ClientID,
    string DeviceID,
    string Alias,
    int Platform,
    string UserJoinType,
    string ControlledTime);

public sealed record UUDeviceInfo
{
    public string DeviceID { get; init; } = "";
    public string Alias { get; init; } = "";
    public string Status { get; init; } = "";
    public int Platform { get; init; }
    public bool Controllable { get; init; }
    public bool SupportWol { get; init; }
    public string? WallpaperUrl { get; init; }
    public string? VersionName { get; init; }
    public IReadOnlyList<UUDeviceParticipant> Participants { get; init; } = [];

    [JsonIgnore]
    public bool IsOnline => Status is "CONNECTED" or "READY";

    [JsonIgnore]
    public bool IsDesktop => (Platform is 1 or 4) && !IsCatusshRegistration;

    [JsonIgnore]
    public bool IsCatusshRegistration =>
        Platform == 1 && VersionName == UUConstants.VersionName &&
        Alias.StartsWith("catussh", StringComparison.OrdinalIgnoreCase);

    public static string PlatformName(int platform) => platform switch
    {
        1 => "Windows",
        2 => "Android",
        3 => "iOS",
        4 => "macOS",
        5 => "TV",
        _ => "Unknown",
    };
}

public sealed record UURoomConfig
{
    public string Token { get; init; } = "";
    public IReadOnlyList<string> SignalServers { get; init; } = [];
    public int TimeoutMilliseconds { get; init; } = 16_000;
    public int SignalReconnectDelayMilliseconds { get; init; } = 1_000;
    public string? ReportToken { get; init; }
    public string? ReportUrl { get; init; }
    public string? AppDataJson { get; init; }
}

public sealed record UUQrLoginSession(string JumpUrl, string Token, string StatusQueryTicket);
public sealed record UUUserLogin(string UserID, string Nickname, string Token);

public sealed class UUApiException : Exception
{
    public UUApiException(string message, int code, int httpStatus = 0, bool tokenExpired = false, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        HttpStatus = httpStatus;
        TokenExpired = tokenExpired;
    }

    public int Code { get; }
    public int HttpStatus { get; }
    public bool TokenExpired { get; }
}
