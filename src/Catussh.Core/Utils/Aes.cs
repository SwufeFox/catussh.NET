// Port of _src/Foundation/CatusshModule/Sources/CatusshModule/Utils/AES.swift
// (CommonCrypto / CryptoKit replaced by System.Security.Cryptography;
//  the Apple login keychain is behind ILegacyKeyStore).
using System.Security.Cryptography;
using System.Text;

namespace Catussh.Core.Utils;

/// <summary>
/// Port of <c>AES.swift</c> — a keychain the app cannot reach through <c>SecItem</c>.
/// A Mac Catalyst process only sees the data-protection keychain, while the AppKit
/// builds of catussh kept the master key in the login keychain; the app supplies
/// this to carry the key over. Set it before <see cref="Aes.Shared"/> (and so the
/// store) is first used.
/// </summary>
/// <remarks>
/// Platform-specific abstraction (the Swift file is only ever fed by the
/// AppKit bridge, <c>MacBridge.swift</c>). Reading the login keychain needs the
/// Security framework, so this port keeps the shape and leaves the storage to
/// whatever implements the interface.
/// </remarks>
public sealed class LegacyKeyStore
{
    /// <summary>Outcome of a read.</summary>
    public enum ReadOutcome
    {
        /// <summary>The key was read.</summary>
        Found,

        /// <summary>There is no key: nothing was ever stored.</summary>
        Absent,

        /// <summary>There may be a key, but it could not be read (access was denied).</summary>
        Unavailable,
    }

    /// <summary>The outcome plus the key, when there is one.</summary>
    public readonly record struct ReadResult(ReadOutcome Outcome, string? Value = null)
    {
        public static ReadResult Found(string value) => new(ReadOutcome.Found, value);

        public static readonly ReadResult Absent = new(ReadOutcome.Absent);

        public static readonly ReadResult Unavailable = new(ReadOutcome.Unavailable);
    }

    public Func<ReadResult> Read { get; }

    public Func<string, bool> Write { get; }

    public LegacyKeyStore(Func<ReadResult> read, Func<string, bool> write)
    {
        Read = read ?? throw new ArgumentNullException(nameof(read));
        Write = write ?? throw new ArgumentNullException(nameof(write));
    }
}

/// <summary>
/// Port of <c>AES.swift</c> — AES-256-GCM under a random nonce for everything it
/// writes, plus a reader for what older builds wrote under the constant DEBUG key
/// or in the legacy CBC format.
/// </summary>
public sealed class Aes
{
    private const int KeySizeInBytes = 32;
    private const int BlockSizeInBytes = 16;

    private static readonly byte[] CurrentFormatPrefix = Encoding.UTF8.GetBytes("RGCM1");

    private static readonly object LegacyKeyStoreLock = new();

    private static LegacyKeyStore? _legacyKeyStore;

    private static readonly Lazy<Aes> SharedEngine = new(CreateShared, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly byte[] _key;
    private readonly byte[] _iv;
    private readonly byte[]? _keyFallback;

    /// <summary>
    /// Key material for an engine tried when this one cannot open the data, so what
    /// older DEBUG builds stored under the constant key stays readable. It never
    /// writes: the next save moves the data to this engine's key.
    /// </summary>
    private readonly Aes? _fallback;

    /// <summary>
    /// The key could not be read or saved, so it lasts for this run only. Nothing
    /// is written with such a key: it would replace data that the real key can
    /// still read.
    /// </summary>
    public bool IsEphemeral { get; }

    /// <summary>
    /// Set before <see cref="Shared"/> is first used; the lock keeps a late read
    /// from racing the one-time registration.
    /// </summary>
    public static LegacyKeyStore? LegacyKeyStore
    {
        get
        {
            lock (LegacyKeyStoreLock) return _legacyKeyStore;
        }
        set
        {
            lock (LegacyKeyStoreLock) _legacyKeyStore = value;
        }
    }

    /// <summary>
    /// The engine every caller shares: the stored master key, or a key that lasts
    /// for this run when none can be read or saved.
    /// </summary>
    public static Aes Shared => SharedEngine.Value;

    /// <summary>
    /// The master key DEBUG builds used before device-specific derivation: a
    /// constant anyone reading the source knows. Only kept to read what those
    /// builds stored; nothing is written with it any more.
    /// </summary>
    internal const string LegacyDebugKey = "fdaisohfiuhfq34hifgraskhfiarhfgui34hibrfiuef";

    /// <summary>
    /// Builds an engine whose key doubles as IV. The initializer pads inputs to the
    /// required sizes, so any string of 16+ characters succeeds.
    /// </summary>
    public static Aes? CreateEngine(string key, bool ephemeral = false) =>
        Create(key, key, ephemeral);

    /// <summary>
    /// An engine that also reads data stored under <see cref="LegacyDebugKey"/>.
    /// Writes still go to this engine's own key.
    /// </summary>
    public Aes WithLegacyDebugKeyFallback()
    {
        var fallback = Create(LegacyDebugKey + LegacyDebugKey + LegacyDebugKey, LegacyDebugKey + LegacyDebugKey + LegacyDebugKey);
        var engine = new Aes(_key, _iv, fallback, IsEphemeral);
        return engine;
    }

    /// <summary>
    /// Builds the shared engine from a random master key persisted in the keychain,
    /// with bridges for the keys earlier builds kept elsewhere.
    /// </summary>
    private static Aes CreateShared()
    {
        // A read that fails is not a missing key: the key may be there and refused
        // (the login keychain asks before handing it to a new build). Swift retries
        // such a read three times and probes once more, which is what the loop
        // below does while it remembers that a read failed.
        string? stored = null;
        var readFailed = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                stored = TryReadStoredKey();
                readFailed = false;
                break;
            }
            catch (Exception error)
            {
                readFailed = true;
                Console.Error.WriteLine($"AES: keychain access failed: {error.Message}");
            }
        }

        if (stored is not null) return CreateEngine(stored);

        // The key of an earlier build, kept where this one cannot look.
        var legacyKeyStore = LegacyKeyStore;
        if (legacyKeyStore is not null)
        {
            switch (legacyKeyStore.Read())
            {
                case { Outcome: LegacyKeyStore.ReadOutcome.Found, Value: { Length: > 2 } master }:
                    // Copied so later launches need not ask for it again. If the
                    // copy fails the key is still good, and is fetched again.
                    if (!readFailed) TryStoreKey(master);
                    return CreateEngine(master);
                case { Outcome: LegacyKeyStore.ReadOutcome.Unavailable }:
                    // A key exists and was not handed over. Making a new one would
                    // orphan everything saved under the old one.
                    Console.Error.WriteLine("AES: the stored master key is unavailable, using an ephemeral key");
                    return CreateEngine(Guid.NewGuid().ToString(), ephemeral: true);
                default:
                    break;
            }
        }
        else if (readFailed)
        {
            Console.Error.WriteLine("AES: the master key could not be read, using an ephemeral key");
            return CreateEngine(Guid.NewGuid().ToString(), ephemeral: true);
        }

        // Persist before activating: a key that was never written is lost on the
        // next launch, with the data encrypted under it.
        var newKey = Guid.NewGuid().ToString();
        if ((!readFailed && TryStoreKey(newKey)) || LegacyKeyStore?.Write(newKey) == true)
        {
            return CreateEngine(newKey);
        }

        // Without a persisted key, stay usable for this run only.
        Console.Error.WriteLine("AES: no persisted master key, falling back to an ephemeral key");
        return CreateEngine(Guid.NewGuid().ToString(), ephemeral: true);
    }

    /// <summary>
    /// Reads the master key the store keeps, the way Swift's <c>stored()</c> plus
    /// <c>keychain.getString</c> do: the key counts only when it is longer than two
    /// characters, a missing one is "no key", and anything that cannot be read
    /// throws — a read that fails is not a missing key.
    /// </summary>
    private static string? TryReadStoredKey()
    {
        var store = LegacyKeyStore;
        if (store is null) throw new InvalidOperationException("no key store is registered");

        var result = store.Read();
        if (result.Outcome == LegacyKeyStore.ReadOutcome.Unavailable)
        {
            throw new InvalidOperationException("the key store is unavailable");
        }

        return result is { Outcome: LegacyKeyStore.ReadOutcome.Found, Value.Length: > 2 } ? result.Value : null;
    }

    private static bool TryStoreKey(string newKey)
    {
        var store = LegacyKeyStore;
        if (store is null) return false;
        try
        {
            return store.Write(newKey);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"AES: keychain write failed: {error.Message}");
            return false;
        }
    }

    /// <summary>
    /// Builds an engine whose key doubles as IV, exactly as
    /// <c>AES(key:iv:)</c> does; null when the string is too short to pad.
    /// </summary>
    private static Aes? Create(string key, string iv, bool ephemeral = false) =>
        key.Length >= BlockSizeInBytes && iv.Length >= BlockSizeInBytes
            ? new Aes(key, iv, null, ephemeral)
            : null;

    private Aes(byte[] key, byte[] iv, Aes? fallback, bool ephemeral)
    {
        _key = key;
        _iv = iv;
        _fallback = fallback;
        _keyFallback = fallback?._key;
        IsEphemeral = ephemeral;
    }

    /// <summary>
    /// Initializes an AES engine.
    /// </summary>
    /// <param name="initKey">Must be at least 16 characters long; padded or cut to 32.</param>
    /// <param name="initIV">Must be at least 16 characters long; padded or cut to 16.</param>
    /// <param name="fallback">The engine tried when this one cannot open the data.</param>
    /// <param name="ephemeral">Whether the key lasts for this run only.</param>
    private Aes(string initKey, string initIV, Aes? fallback, bool ephemeral)
    {
        // 初始化密钥 核查长度要求
        if (initKey.Length < BlockSizeInBytes || initIV.Length < BlockSizeInBytes)
        {
            throw new ArgumentException("the key and the IV must be at least 16 characters long");
        }

        // 修改 key 到指定长度
        var keyString = initKey;
        while (keyString.Length < KeySizeInBytes) keyString += keyString;
        while (keyString.Length > KeySizeInBytes) keyString = keyString[..^1];
        var key = Encoding.UTF8.GetBytes(keyString);

        // 修改 iv 到指定长度
        var ivString = initIV;
        while (ivString.Length < BlockSizeInBytes) ivString += ivString;
        while (ivString.Length > BlockSizeInBytes) ivString = ivString[..^1];
        var iv = Encoding.UTF8.GetBytes(ivString);

        _key = key;
        _iv = iv;
        _fallback = fallback;
        _keyFallback = fallback?._key;
        IsEphemeral = ephemeral;
    }

    // MARK: - API

    /// <summary>
    /// Encrypts under AES-256-GCM with a random nonce, prefixed with the marker of
    /// the current format (version 1). Data without it is the legacy format and is
    /// only ever read; the next write stores it in the current format.
    /// </summary>
    public byte[]? Encrypt(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var sealedBytes = Seal(data);
        if (sealedBytes is null)
        {
            Console.Error.WriteLine("Error: Failed to encrypt data.");
            return null;
        }

        var result = new byte[CurrentFormatPrefix.Length + sealedBytes.Length];
        CurrentFormatPrefix.CopyTo(result, 0);
        sealedBytes.CopyTo(result, CurrentFormatPrefix.Length);
        return result;
    }

    /// <summary>
    /// Decrypts what <see cref="Encrypt"/> writes, and the legacy CBC format.
    /// </summary>
    public byte[]? Decrypt(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (StartsWithPrefix(data))
        {
            // GCM refuses a wrong key on its own, so the new key goes first.
            if (Open(data) is { } plain) return plain;
            if (FallbackOpen(data) is { } fallbackPlain) return fallbackPlain;
        }
        else
        {
            // CBC has no integrity: a wrong key still "decrypts" when the last byte
            // happens to pad out. What DEBUG builds stored in this format is under
            // the constant key, so that one goes first.
            if (FallbackOpen(data) is { } fallbackPlain) return fallbackPlain;
            if (Open(data) is { } plain) return plain;
        }

        Console.Error.WriteLine("Error: Failed to decrypt data.");
        return null;
    }

    /// <summary>Decrypts under the fallback key material, if there is any.</summary>
    private byte[]? FallbackOpen(byte[] data) => _fallback?.Open(data);

    /// <summary>Decrypts with this engine's own key, whichever format the data is in.</summary>
    private byte[]? Open(byte[] data)
    {
        if (!StartsWithPrefix(data)) return LegacyDecrypt(data);
        return TryOpenGcm(data);
    }

    private byte[]? Seal(byte[] plain)
    {
        try
        {
            // 12-byte nonce and 16-byte tag, as CryptoKit's combined representation.
            var combined = new byte[12 + plain.Length + 16];
            var nonce = combined.AsSpan(0, 12);
            RandomNumberGenerator.Fill(nonce);
            var cipher = combined.AsSpan(12, plain.Length);
            var tag = combined.AsSpan(12 + plain.Length, 16);
            using var gcm = new AesGcm(_key, 16);
            gcm.Encrypt(nonce, plain, cipher, tag);
            return combined;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private byte[]? TryOpenGcm(byte[] data)
    {
        const int nonceSize = 12;
        const int tagSize = 16;
        var payload = data.AsSpan(CurrentFormatPrefix.Length);
        if (payload.Length < nonceSize + tagSize) return null;

        var cipher = payload[nonceSize..^tagSize];
        var plain = new byte[cipher.Length];
        try
        {
            using var gcm = new AesGcm(_key, 16);
            gcm.Decrypt(payload[..nonceSize], cipher, payload[^tagSize..], plain);
            return plain;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// The legacy format: AES-CBC with PKCS7 padding, the key doubling as IV and no
    /// integrity check. Kept to read what was stored before the GCM format; it is
    /// never written any more.
    /// </summary>
    private byte[]? LegacyDecrypt(byte[] data)
    {
        try
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor(_key, _iv);
            return decryptor.TransformFinalBlock(data, 0, data.Length);
        }
        catch (CryptographicException error)
        {
            Console.Error.WriteLine($"Error: Failed to crypt data. {error.Message}");
            return null;
        }
    }

    private static bool StartsWithPrefix(byte[] data) =>
        data.AsSpan().StartsWith(CurrentFormatPrefix);
}
