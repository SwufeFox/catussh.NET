using System.Text.Json.Serialization;

namespace Catussh.Core.Store;

/// <summary>
/// A saved server — the port of <c>RDMachine</c> from
/// <c>CatusshModule/Machine/RDMachine.swift</c>.
///
/// Field names and defaults follow the Swift struct so an export from the macOS app
/// reads back here. <see cref="Id"/> is a string rather than a <c>Guid</c> because
/// the Swift stores <c>UUID.uuidString</c> and identities reference it as text.
/// </summary>
public sealed record RDMachine
{
    public string Id { get; init; } = Guid.NewGuid().ToString().ToUpperInvariant();

    /// <summary>Hostname or IP, without a port.</summary>
    public string RemoteAddress { get; init; } = "";

    /// <summary>Kept as a string for parity with the Swift; an unparsable port fails to connect.</summary>
    public string RemotePort { get; init; } = "22";

    public string Name { get; init; } = "";

    /// <summary>Sidebar/table grouping, e.g. "Production".</summary>
    public string Group { get; init; } = "";

    /// <summary>Sort key for "last used" and the home page's recent list.</summary>
    public DateTime LastConnection { get; init; } = DateTime.UtcNow;

    /// <summary>The SSH identification string seen on the last successful connect.</summary>
    public string LastBanner { get; init; } = "";

    /// <summary>Free-form note shown under the name in the Servers table.</summary>
    public string Comment { get; init; } = "";

    /// <summary>Identity id, or null for "Automatic" (try the auto identities in order).</summary>
    public string? AssociatedIdentity { get; init; }

    /// <summary>Reserved, mirrors the Swift dictionary (e.g. <c>sftp.login.path</c>).</summary>
    private Dictionary<string, string> _attachment = [];

    public Dictionary<string, string> Attachment
    {
        get => _attachment;
        init
        {
            _attachment = new Dictionary<string, string>(value);
            foreach (var key in new[] { "cloudflare.serviceToken.secret", "remoteDesktop.vnc.password" })
            {
                if (_attachment.TryGetValue(key, out var secret))
                    _attachment[key] = SecretProtector.Protect(secret);
            }
        }
    }
    /// <summary>How SSH reaches the server; absent attachment data means direct TCP.</summary>
    [JsonIgnore]
    public Transport TransportMode
    {
        get => Attachment.GetValueOrDefault("transport") switch
        {
            "cloudflare-access" => Transport.CloudflareAccess,
            "uu-remote" => Transport.UURemote,
            _ => Transport.Direct,
        };
        init => Attachment = WithAttachment("transport", value switch
        {
            Transport.Direct => null,
            Transport.CloudflareAccess => "cloudflare-access",
            Transport.UURemote => "uu-remote",
            _ => null,
        });
    }

    /// <summary>Cloudflare Access service-token client ID.</summary>
    [JsonIgnore]
    public string CloudflareServiceTokenId
    {
        get => Attachment.GetValueOrDefault("cloudflare.serviceToken.id", "");
        init => Attachment = WithAttachment("cloudflare.serviceToken.id", value);
    }

    /// <summary>Cloudflare Access service-token client secret.</summary>
    [JsonIgnore]
    public string CloudflareServiceTokenSecret
    {
        get => SecretProtector.Unprotect(Attachment.GetValueOrDefault("cloudflare.serviceToken.secret", ""));
        init => Attachment = WithAttachment("cloudflare.serviceToken.secret", SecretProtector.Protect(value));
    }

    public enum Transport
    {
        Direct,
        CloudflareAccess,
        UURemote,
    }

    private Dictionary<string, string> WithAttachment(string key, string? value)
    {
        var attachment = new Dictionary<string, string>(Attachment);
        if (string.IsNullOrEmpty(value)) attachment.Remove(key);
        else attachment[key] = value;
        return attachment;
    }

    public enum RemoteDesktopProtocolType
    {
        None,
        Vnc,
        UuRemote,
    }

    [JsonIgnore]
    public RemoteDesktopProtocolType RemoteDesktopProtocol
    {
        get => Attachment.GetValueOrDefault("remoteDesktop.protocol") switch
        {
            "vnc" => RemoteDesktopProtocolType.Vnc,
            "uu-remote" => RemoteDesktopProtocolType.UuRemote,
            _ => RemoteDesktopProtocolType.None,
        };
        init => Attachment = WithAttachment("remoteDesktop.protocol", value switch
        {
            RemoteDesktopProtocolType.Vnc => "vnc",
            RemoteDesktopProtocolType.UuRemote => "uu-remote",
            _ => null,
        });
    }

    [JsonIgnore]
    public string VncPort
    {
        get => Attachment.GetValueOrDefault("remoteDesktop.vnc.port", "5900");
        init => Attachment = WithAttachment(
            "remoteDesktop.vnc.port",
            string.IsNullOrEmpty(value) || value == "5900" ? null : value);
    }

    [JsonIgnore]
    public string VncPassword
    {
        get => SecretProtector.Unprotect(Attachment.GetValueOrDefault("remoteDesktop.vnc.password", ""));
        init => Attachment = WithAttachment("remoteDesktop.vnc.password", SecretProtector.Protect(value));
    }

    [JsonIgnore]
    public string VncUsername
    {
        get => Attachment.GetValueOrDefault("remoteDesktop.vnc.username", "");
        init => Attachment = WithAttachment("remoteDesktop.vnc.username", value);
    }

    [JsonIgnore]
    public bool VncShared
    {
        get => Attachment.GetValueOrDefault("remoteDesktop.vnc.shared") != "0";
        init => Attachment = WithAttachment("remoteDesktop.vnc.shared", value ? null : "0");
    }

    [JsonIgnore]
    public bool VncOverSSH
    {
        get => Attachment.GetValueOrDefault("remoteDesktop.vnc.overSSH") == "1";
        init => Attachment = WithAttachment("remoteDesktop.vnc.overSSH", value ? "1" : null);
    }

    [JsonIgnore]
    public string UUDeviceID
    {
        get => Attachment.GetValueOrDefault("remoteDesktop.uu.deviceID", "");
        init => Attachment = WithAttachment("remoteDesktop.uu.deviceID", value);
    }

    [JsonIgnore]
    public string UUDeviceAlias
    {
        get => Attachment.GetValueOrDefault("remoteDesktop.uu.deviceAlias", "");
        init => Attachment = WithAttachment("remoteDesktop.uu.deviceAlias", value);
    }

    [JsonIgnore]
    public string UUDeviceStatus
    {
        get => Attachment.GetValueOrDefault("remoteDesktop.uu.status", "");
        init => Attachment = WithAttachment("remoteDesktop.uu.status", value);
    }

    [JsonIgnore]
    public string UUWallpaperUrl
    {
        get => Attachment.GetValueOrDefault("remoteDesktop.uu.wallpaperURL", "");
        init => Attachment = WithAttachment("remoteDesktop.uu.wallpaperURL", value);
    }

    [JsonIgnore]
    public bool UURemoteManaged
    {
        get => Attachment.GetValueOrDefault("remoteDesktop.uu.managed") == "1";
        init => Attachment = WithAttachment("remoteDesktop.uu.managed", value ? "1" : null);
    }

    [JsonIgnore]
    public bool IsUURemoteDevice =>
        RemoteDesktopProtocol == RemoteDesktopProtocolType.UuRemote &&
        TransportMode == Transport.UURemote && !string.IsNullOrEmpty(UUDeviceID);

    [JsonIgnore]
    public bool HasRemoteDesktop => RemoteDesktopProtocol != RemoteDesktopProtocolType.None;

    [JsonIgnore]
    public int Port => int.TryParse(RemotePort, out var port) && port > 0 ? port : 22;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? RemoteAddress : Name;

    /// <summary>Remote folder SFTP opens at, port of <c>fileTransferLoginPath</c>.</summary>
    public string FileTransferLoginPath
    {
        get => Attachment.GetValueOrDefault("sftp.login.path", "/");
        init => Attachment = new Dictionary<string, string>(Attachment) { ["sftp.login.path"] = value };
    }

    /// <summary>Parses <c>user@host:port</c> for Quick Connect; null when it is not that shape.</summary>
    public static RDMachine? FromQuickConnect(string input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) return null;

        // Drop a leading "ssh " so a pasted full command works.
        if (text.StartsWith("ssh ", StringComparison.OrdinalIgnoreCase)) text = text[4..].Trim();
        if (text.Length == 0 || text.Contains(' ')) return null;

        var user = "";
        var at = text.LastIndexOf('@');
        if (at > 0)
        {
            user = text[..at];
            text = text[(at + 1)..];
        }

        var port = "22";
        var colon = text.LastIndexOf(':');
        if (colon > 0 && !text.Contains(']'))
        {
            var maybePort = text[(colon + 1)..];
            if (int.TryParse(maybePort, out var parsed) && parsed > 0 && parsed <= 65535)
            {
                port = maybePort;
                text = text[..colon];
            }
        }

        if (text.Length == 0) return null;
        return new RDMachine
        {
            Name = text,
            RemoteAddress = text,
            RemotePort = port,
            Comment = user,
        };
    }
}
