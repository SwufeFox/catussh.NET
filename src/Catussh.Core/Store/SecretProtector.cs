using System.Security.Cryptography;
using System.Text;

namespace Catussh.Core.Store;

/// <summary>
/// Keeps stored secrets out of plain text in <c>machines.json</c>.
///
/// The Swift app put these in the Keychain. Windows' equivalent is DPAPI, which
/// binds a blob to the current user account: another user on the same machine
/// cannot read it. That is the closest match to the Keychain's guarantee without
/// shipping a master-key store of our own.
///
/// Values are prefixed so a file written on one platform still reads back: an
/// unprefixed value is treated as plain text, which is what an import from the
/// macOS app contains.
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "dpapi:";

    /// <summary>True when this runtime can protect secrets (Windows only).</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        if (plainText.StartsWith(Prefix, StringComparison.Ordinal)) return plainText;
        if (!IsSupported) return plainText;

        try
        {
            var cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainText),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(cipher);
        }
        catch (CryptographicException)
        {
            // A profile without a DPAPI master key still has to work; the caller
            // gets plain text and the UI says the secret is stored unprotected.
            return plainText;
        }
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;

        try
        {
            var cipher = Convert.FromBase64String(stored[Prefix.Length..]);
            var plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (FormatException)
        {
            return "";
        }
        catch (CryptographicException)
        {
            // Copied to another user account or another machine: unreadable by design.
            return "";
        }
    }

    /// <summary>True when the stored value is still readable, for the UI to warn otherwise.</summary>
    public static bool CanUnprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return true;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        try
        {
            var cipher = Convert.FromBase64String(stored[Prefix.Length..]);
            ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
