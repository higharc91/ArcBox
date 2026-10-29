using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ArcBox.Server.Rooms;

public static class Secrets
{
    public static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>SHA-256 of <paramref name="value"/>, base64url-encoded (43 characters).</summary>
    public static string Hash(string value)
    {
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>
    /// Hashes <paramref name="provided"/> and compares it, in constant time, with a stored hash.
    /// A plaintext passed as <paramref name="expectedHash"/> never matches.
    /// </summary>
    public static bool Matches(string expectedHash, string provided)
    {
        if (expectedHash == null || provided == null)
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);
        var providedBytes = Encoding.UTF8.GetBytes(Hash(provided));

        if (expectedBytes.Length != providedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
