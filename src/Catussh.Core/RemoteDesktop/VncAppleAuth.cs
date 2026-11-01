using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Catussh.Core.RemoteDesktop;

/// <summary>RFB security type 30, used by macOS Screen Sharing accounts.</summary>
internal static class VncAppleAuth
{
    private const int MaxKeyLength = 1024;

    public static byte[] CreateResponse(
        ReadOnlySpan<byte> generatorBytes,
        ReadOnlySpan<byte> primeBytes,
        ReadOnlySpan<byte> serverPublicKeyBytes,
        string username,
        string password)
    {
        if (generatorBytes.Length != 2 || primeBytes.Length is < 1 or > MaxKeyLength ||
            serverPublicKeyBytes.Length != primeBytes.Length)
            throw new IOException("The server sent an unusable Apple authentication key length.");
        if ((primeBytes[^1] & 1) == 0)
            throw new IOException("The server's Apple authentication modulus is not usable.");

        var prime = new BigInteger(primeBytes, isUnsigned: true, isBigEndian: true);
        var generator = new BigInteger(generatorBytes, isUnsigned: true, isBigEndian: true);
        if (prime <= 2 || generator <= 0 || generator >= prime)
            throw new IOException("The server sent unusable Apple authentication parameters.");

        var privateKeyBytes = RandomNumberGenerator.GetBytes(primeBytes.Length);
        var privateKey = new BigInteger(privateKeyBytes, isUnsigned: true, isBigEndian: true);
        CryptographicOperations.ZeroMemory(privateKeyBytes);
        if (privateKey.IsZero) privateKey = BigInteger.One;

        var serverPublicKey = new BigInteger(serverPublicKeyBytes, isUnsigned: true, isBigEndian: true);
        var publicKey = BigInteger.ModPow(generator, privateKey, prime);
        var sharedSecret = BigInteger.ModPow(serverPublicKey, privateKey, prime);
        var sharedBytes = ToFixedBigEndian(sharedSecret, primeBytes.Length);
        var key = MD5.HashData(sharedBytes);
        CryptographicOperations.ZeroMemory(sharedBytes);

        var credentialBlock = CreateCredentialBlock(username, password);
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        var encryptedCredentials = aes.EncryptEcb(credentialBlock, PaddingMode.None);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(credentialBlock);

        var encodedPublicKey = ToFixedBigEndian(publicKey, primeBytes.Length);
        var response = new byte[encryptedCredentials.Length + encodedPublicKey.Length];
        encryptedCredentials.CopyTo(response, 0);
        encodedPublicKey.CopyTo(response, encryptedCredentials.Length);
        CryptographicOperations.ZeroMemory(encryptedCredentials);
        CryptographicOperations.ZeroMemory(encodedPublicKey);
        return response;
    }

    private static byte[] CreateCredentialBlock(string username, string password)
    {
        var result = new byte[128];
        WriteField(username, result.AsSpan(0, 64));
        WriteField(password, result.AsSpan(64, 64));
        return result;
    }

    private static void WriteField(string value, Span<byte> destination)
    {
        RandomNumberGenerator.Fill(destination);
        var encoded = Encoding.UTF8.GetBytes(value);
        var count = Math.Min(encoded.Length, destination.Length - 1);
        encoded.AsSpan(0, count).CopyTo(destination);
        destination[count] = 0;
        CryptographicOperations.ZeroMemory(encoded);
    }

    private static byte[] ToFixedBigEndian(BigInteger value, int length)
    {
        var encoded = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (encoded.Length > length)
            throw new IOException("The server's Apple authentication key exceeds its modulus.");
        var result = new byte[length];
        encoded.CopyTo(result, length - encoded.Length);
        CryptographicOperations.ZeroMemory(encoded);
        return result;
    }
}
