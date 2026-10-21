// Port of _src/Foundation/CatusshModule/Sources/CatusshModule/Utils/KnownHosts.swift
// (CryptoKit SHA-256 replaced by System.Security.Cryptography; UserDefaults replaced
//  by IKeyValueStore, and the NSRemoteShell / RDMachine extensions became the callers'
//  job — this project has neither type).
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Catussh.Core.Utils;

/// <summary>
/// Port of <c>KnownHosts.swift</c> — <c>[host]:port</c> → stored line, exactly what
/// Swift's <c>UserDefaults.dictionary(forKey:)</c> gives back: <c>String</c> values only.
/// </summary>
public interface IKeyValueStore
{
    /// <summary>The dictionary under the key, or nil/empty when there is none.</summary>
    IReadOnlyDictionary<string, string>? GetDictionary(string key);

    /// <summary>Stores the dictionary under the key.</summary>
    void SetDictionary(string key, IReadOnlyDictionary<string, string> value);

    /// <summary>Removes the dictionary under the key.</summary>
    void RemoveObject(string key);
}

/// <summary>
/// <see cref="IKeyValueStore"/> in memory, the counterpart of a fresh
/// <c>UserDefaults(suiteName:)</c>.
/// </summary>
public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _values = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string>? GetDictionary(string key)
    {
        lock (_values)
        {
            return _values.TryGetValue(key, out var value)
                ? new Dictionary<string, string>(value, StringComparer.Ordinal)
                : null;
        }
    }

    public void SetDictionary(string key, IReadOnlyDictionary<string, string> value)
    {
        lock (_values) _values[key] = new Dictionary<string, string>(value, StringComparer.Ordinal);
    }

    public void RemoveObject(string key)
    {
        lock (_values) _values.Remove(key);
    }
}

/// <summary>
/// Port of <c>KnownHosts.swift</c> — the host keys of the servers connected to so
/// far. A server's key is saved the first time it is seen; after that a connection
/// to the same address is only made when the server presents the same key, so
/// someone answering in the server's place is refused instead of being handed the
/// password.
/// </summary>
public sealed class KnownHosts
{
    /// <summary>A host key, equatable by type and bytes.</summary>
    public sealed record HostKey
    {
        /// <summary>The algorithm as SSH names it, "ssh-ed25519" for one.</summary>
        public string Type { get; }

        public byte[] Key { get; }

        public HostKey(string type, byte[] key)
        {
            Type = type;
            Key = key;
        }

        /// <summary>What <c>ssh-keygen -l</c> prints for the key: SHA-256, base64, no padding.</summary>
        public string Fingerprint
        {
            get
            {
                var digest = Convert.ToBase64String(SHA256.HashData(Key));
                return "SHA256:" + digest.Replace("=", string.Empty, StringComparison.Ordinal);
            }
        }

        public bool Equals(HostKey? other) =>
            other is not null && Type == other.Type && Key.AsSpan().SequenceEqual(other.Key);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Type, StringComparer.Ordinal);
            foreach (var b in Key) hash.Add(b);
            return hash.ToHashCode();
        }
    }

    public static KnownHosts Shared { get; } = new();

    private readonly IKeyValueStore _defaults;
    private readonly string _storageKey;
    private readonly object _lock = new();

    /// <summary>The keys refused since launch, to tell the user what was offered.</summary>
    private readonly Dictionary<string, HostKey> _refused = new(StringComparer.Ordinal);

    public KnownHosts(IKeyValueStore? defaults = null, string storageKey = "me.mizorewww.rayon.knownHosts")
    {
        _defaults = defaults ?? new InMemoryKeyValueStore();
        _storageKey = storageKey;
    }

    /// <summary>
    /// Has every connection checked against the saved keys.
    /// </summary>
    /// <remarks>
    /// Platform-specific in the Swift file: it installs
    /// <c>NSRemoteShell.hostKeyVerifier</c>, the hook of the SSH library this port
    /// does not use. Callers ask <see cref="Accepts"/> from their own connection
    /// code; there is no global verifier to install here.
    /// </remarks>
    public static void Install()
    {
    }

    /// <summary>
    /// True for the key saved for the address, and for the first key seen there,
    /// which is saved. False for any other key.
    /// </summary>
    public bool Accepts(HostKey offered, string host, int port)
    {
        var name = Name(host, port);
        lock (_lock)
        {
            var saved = Entries();
            if (!saved.TryGetValue(name, out var line))
            {
                saved[name] = Line(offered);
                _defaults.SetDictionary(_storageKey, saved);
                _refused.Remove(name);
                return true;
            }

            if (string.Equals(line, Line(offered), StringComparison.Ordinal))
            {
                _refused.Remove(name);
                return true;
            }

            _refused[name] = offered;
            return false;
        }
    }

    public HostKey? SavedKey(string host, int port)
    {
        lock (_lock)
        {
            var name = Name(host, port);
            return Entries().TryGetValue(name, out var line) ? HostKeyFrom(line) : null;
        }
    }

    /// <summary>The key the address offered that was refused, if one was since launch.</summary>
    public HostKey? RefusedKey(string host, int port)
    {
        lock (_lock)
        {
            return _refused.TryGetValue(Name(host, port), out var key) ? key : null;
        }
    }

    /// <summary>The next key the address presents is saved as its key.</summary>
    public void Forget(string host, int port)
    {
        var name = Name(host, port);
        lock (_lock)
        {
            var saved = Entries();
            saved.Remove(name);
            _defaults.SetDictionary(_storageKey, saved);
            _refused.Remove(name);
        }
    }

    public void ForgetAll()
    {
        lock (_lock)
        {
            _defaults.RemoveObject(_storageKey);
            _refused.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock) return Entries().Count;
        }
    }

    /// <summary>
    /// Why a connection to the address was refused, when its host key was the
    /// reason; null when it was not.
    /// </summary>
    public string? RefusalMessage(string host, int port)
    {
        var offered = RefusedKey(host, port);
        if (offered is null) return null;

        var lines = new List<string>
        {
            $"The host key of {host}:{port} has changed, so the connection was refused.",
        };
        if (SavedKey(host, port) is { } saved)
        {
            lines.Add($"Saved: {saved.Type} {saved.Fingerprint}");
        }

        lines.Add($"Offered: {offered.Type} {offered.Fingerprint}");
        lines.Add("If the server was reinstalled or its keys were replaced, choose Forget Host Key for it and connect again. If not, something may be intercepting the connection.");
        return string.Join("\n", lines);
    }

    private Dictionary<string, string> Entries()
    {
        var read = _defaults.GetDictionary(_storageKey);
        return read is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(read, StringComparer.Ordinal);
    }

    /// <summary>
    /// <c>[host]:port</c>, as known_hosts writes an address with a port. The host is
    /// trimmed of whitespace and lowercased.
    /// </summary>
    internal static string Name(string host, int port) =>
        $"[{host.Trim().ToLowerInvariant()}]:{port}";

    private static string Line(HostKey key) => key.Type + " " + Convert.ToBase64String(key.Key);

    private static HostKey? HostKeyFrom(string line)
    {
        // Swift's split(separator: " ", maxSplits: 1) with its default
        // omittingEmptySubsequences: true: leading spaces are dropped and the
        // remainder keeps every later space.
        var parts = SplitFirstSpace(line);
        if (parts is null) return null;
        var (type, encoded) = parts.Value;
        try
        {
            return new HostKey(type, Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static (string Type, string Key)? SplitFirstSpace(string line)
    {
        var start = 0;
        while (start < line.Length && line[start] == ' ') start++;
        if (start >= line.Length) return null;
        var separator = line.IndexOf(' ', start);
        if (separator < 0) return null;
        var type = line[start..separator];
        var remainder = line[(separator + 1)..];
        if (remainder.Length == 0) return null;
        return (type, remainder);
    }
}
