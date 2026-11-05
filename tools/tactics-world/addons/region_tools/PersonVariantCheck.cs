using System.Text.Json;
using ArcBox.Sdk;
using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>Native editor save/load/export check, using a temporary scene rather than painted production work.</summary>
public static class PersonVariantCheck
{
    public static int Run()
    {
        var folder = Directory.CreateTempSubdirectory("arcbox-person-variant-");
        var path = Path.Combine(folder.FullName, "variant_check.tscn");
        var source = new RegionSource("variant_check", "Variant check", 1, 4, 4);
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                source.Set(new Tile(x, y), 0, "grass/lush", TileFlags.None);
        foreach (var variant in new string?[] { null, "a", "b" })
            source.People.Add(new RegionPerson("farmer", new Tile(1, 1), "se", Variant: variant));
        var root = RegionScene.Build(source);
        try
        {
            RegionScene.Save(root, path);
            using var packed = ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore);
            var reloaded = packed.Instantiate<Region>();
            try
            {
                var (back, problems) = RegionScene.Read(reloaded).ToSource();
                if (problems.Length != 0)
                    throw new InvalidDataException(string.Join('\n', problems));
                var written = RegionWriter.Write(back, new HashSet<string> { "grass/lush" });
                if (written.Region is null || written.Problems.Length != 0)
                    throw new InvalidDataException(string.Join('\n', written.Problems));
                var json = JsonSerializer.Serialize(written.Region, ArcJson.Options);
                var read = JsonSerializer.Deserialize<RegionDef>(json, ArcJson.Options)!;
                if (!read.People.Select(p => p.Variant).Order().SequenceEqual(new string?[] { null, "a", "b" }))
                    throw new InvalidDataException("Person variants were lost or collapsed during editor save/load/export.");
                GD.Print("PERSON VARIANT CHECK PASS: native scene save/load, painted export, JSON round-trip; 3 distinct variants.");
                return 0;
            }
            finally { reloaded.Free(); }
        }
        finally
        {
            root.Free();
            folder.Delete(recursive: true);
        }
    }
}
