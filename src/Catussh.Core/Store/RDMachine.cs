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
    public Dictionary<string, string> Attachment { get; init; } = [];

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
