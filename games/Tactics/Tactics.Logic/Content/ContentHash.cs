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
    /// Hashes <paramref name="files"/>, each named <c>"&lt;pack&gt;/&lt;relative path&gt;"</c>, in two stages:
    /// <list type="number">
    /// <item>each file gets its own SHA-256 over its UTF-8 name, one NUL byte and its bytes;</item>
    /// <item>the files are sorted by name (ordinal) and the result is SHA-256 over their digests in that order, as lowercase
    /// hex.</item>
    /// </list>
    /// So the order the files are passed or read in doesn't matter, while renaming a file, changing any byte or moving bytes
    /// from one file to the next all change the hash (hashing each file separately keeps the boundaries unambiguous).
    /// <see cref="RulesPack.Load"/> passes every pack's <c>manifest.json</c> too, so editing a manifest (its requires, or
    /// even just the order of its file list) changes the hash.
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
