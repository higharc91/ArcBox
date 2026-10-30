using System.Security.Cryptography;
using System.Text;

namespace Tactics.Logic;

/// <summary>
/// The content hash that pins a battle to the rules it started under (spec §2.5, §4.9): SHA-256 over every loaded pack
/// file, as lowercase hex.
/// </summary>
public static class ContentHash
{
    /// <summary>
    /// Hashes <paramref name="files"/>, each named <c>"&lt;pack&gt;/&lt;relative path&gt;"</c>. The files are sorted by name
    /// (ordinal), so the order they are listed or read in doesn't matter. Each file is hashed on its own as its UTF-8 name,
    /// a NUL byte and its bytes, and the result is SHA-256 over those digests in order, so renaming a file, changing any
    /// byte or moving bytes from one file to the next all change the hash.
    /// </summary>
    public static string Of(IEnumerable<(string RelativePath, byte[] Bytes)> files)
    {
        using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (path, bytes) in files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            using var one = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            one.AppendData(Encoding.UTF8.GetBytes(path));
            one.AppendData([0]);
            one.AppendData(bytes);
            all.AppendData(one.GetHashAndReset());
        }
        return Convert.ToHexStringLower(all.GetHashAndReset());
    }
}
