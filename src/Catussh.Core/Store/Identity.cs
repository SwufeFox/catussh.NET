using Catussh.Core.Ssh;
using System.Text.Json.Serialization;

namespace Catussh.Core.Store;

/// <summary>
/// A saved credential — the port of <c>RDIdentity</c> from
/// <c>CatusshModule/Identity/RDIdentity.swift</c>.
///
/// The Swift keeps the password and the private key in the same struct and picks
/// whichever is filled in; <see cref="ToCredential"/> does the same. The password is
/// stored through <see cref="SecretProtector"/> rather than in plain text.
/// </summary>
public sealed record Identity
{
    public string Id { get; init; } = Guid.NewGuid().ToString().ToUpperInvariant();

    public string Username { get; init; } = "";

    /// <summary>Encrypted with <see cref="SecretProtector"/>; empty when this identity uses a key.</summary>
    public string Password { get; init; } = "";

    /// <summary>OpenSSH private key text.</summary>
    public string PrivateKey { get; init; } = "";

    public string PublicKey { get; init; } = "";

    /// <summary>Passphrase for an encrypted private key.</summary>
    public string Passphrase { get; init; } = "";

    public DateTime LastRecentUsed { get; init; } = DateTime.UtcNow;

    public string Comment { get; init; } = "";

    public string Group { get; init; } = "";

    /// <summary>When true this identity is offered to servers set to "Automatic".</summary>
    public bool AuthenticAutomatically { get; init; }

    public Dictionary<string, string> Attachment { get; init; } = [];

    [JsonIgnore]
    public bool UsesPrivateKey => !string.IsNullOrWhiteSpace(PrivateKey);

    /// <summary>Port of <c>getKeyType()</c>: what the UI labels the credential with.</summary>
    [JsonIgnore]
    public string KeyType => UsesPrivateKey ? "key" : "password";

    /// <summary>Port of <c>shortDescription()</c>, shown while connecting.</summary>
    public string ShortDescription() => $"User {Username} with {KeyType}";

    /// <summary>Turns this identity into what <see cref="SshConnection"/> expects.</summary>
    public SshCredential ToCredential() =>
        UsesPrivateKey
            ? SshCredential.FromPrivateKey(
                Username,
                PrivateKey,
                string.IsNullOrEmpty(Passphrase) ? null : SecretProtector.Unprotect(Passphrase))
            : SshCredential.FromPassword(Username, SecretProtector.Unprotect(Password));

    /// <summary>Builds an identity from the edit form, protecting the secrets.</summary>
    public static Identity Create(
        string username,
        string? password = null,
        string? privateKey = null,
        string? passphrase = null,
        string comment = "",
        string group = "",
        bool authenticAutomatically = false) =>
        new()
        {
            Username = username.Trim(),
            Password = SecretProtector.Protect(password ?? ""),
            PrivateKey = privateKey ?? "",
            Passphrase = SecretProtector.Protect(passphrase ?? ""),
            Comment = comment,
            Group = group,
            AuthenticAutomatically = authenticAutomatically,
        };
}
