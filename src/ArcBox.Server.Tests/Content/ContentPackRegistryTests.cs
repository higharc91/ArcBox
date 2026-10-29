using System.Security.Cryptography;
using System.Text;
using ArcBox.Server.Content;

namespace ArcBox.Server.Tests.Content;

public sealed class ContentPackRegistryTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("arcbox-content-");

    public void Dispose()
    {
        try { _root.Delete(true); } catch (IOException) { }
    }

    private string Write(string relative, string text)
    {
        var full = Path.Combine(_root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    private static string Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void Hash_is_deterministic_and_changes_with_any_file()
    {
        var a = Write("g/base/a.txt", "alpha");
        Write("g/base/sub/b.txt", "beta");

        var first = new ContentPackRegistry(_root.FullName).For("g").Single();
        var again = new ContentPackRegistry(_root.FullName).For("g").Single();
        Assert.Equal(first.Hash, again.Hash);
        Assert.Matches("^[0-9a-f]{64}$", first.Hash);

        // The documented recipe: SHA-256 of "{path}\n{fileSha}\n" over files in ordinal path order.
        var expected = Hex($"a.txt\n{Hex("alpha")}\nsub/b.txt\n{Hex("beta")}\n");
        Assert.Equal(expected, first.Hash);
        Assert.Equal(["a.txt", "sub/b.txt"], first.Files.Select(f => f.Path));
        Assert.Equal(Hex("alpha"), first.Files[0].Sha256);
        Assert.Equal(5, first.Files[0].Bytes);

        // Changing a file's bytes changes the hash...
        File.WriteAllText(a, "alpha!");
        var edited = new ContentPackRegistry(_root.FullName).For("g").Single().Hash;
        Assert.NotEqual(first.Hash, edited);

        // ...so does adding a file...
        Write("g/base/c.txt", "gamma");
        var added = new ContentPackRegistry(_root.FullName).For("g").Single().Hash;
        Assert.NotEqual(edited, added);

        // ...and so does renaming one without touching its bytes.
        File.Move(Path.Combine(_root.FullName, "g/base/c.txt"), Path.Combine(_root.FullName, "g/base/d.txt"));
        Assert.NotEqual(added, new ContentPackRegistry(_root.FullName).For("g").Single().Hash);
    }

    [Fact]
    public void Scans_games_and_packs()
    {
        Write("g/zeta/z.txt", "z");
        Write("g/base/a.txt", "a");
        Write("other/base/a.txt", "a");
        Write("loose-file.txt", "ignored");

        var registry = new ContentPackRegistry(_root.FullName);

        Assert.Equal(["base", "zeta"], registry.For("g").Select(p => p.PackId));
        Assert.Single(registry.For("other"));
        Assert.Empty(registry.For("none"));

        var pack = registry.For("g")[0];
        Assert.Same(pack, registry.Find("g", "base", pack.Hash));
        Assert.Null(registry.Find("g", "base", new string('0', 64)));
        Assert.Null(registry.Find("g", "nope", pack.Hash));
        Assert.Null(registry.Find("none", "base", pack.Hash));
    }

    [Fact]
    public void Missing_root_is_an_empty_registry()
    {
        var registry = new ContentPackRegistry(Path.Combine(_root.FullName, "does-not-exist"));

        Assert.Empty(registry.For("g"));
        Assert.Null(registry.Find("g", "base", "x"));
    }
}
