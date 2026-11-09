using System.Text.Json.Nodes;
using Godot;
using Tactics.Logic;
using Tactics.WorldTools;
namespace Tactics.World.Editor;

/// <summary>Real Godot scene persistence and pack export against copied production content.</summary>
public static class PlacementMarkerCheck
{
    public static int Run()
    {
        var folder = Directory.CreateTempSubdirectory("arcbox-placement-check-");
        var rules = Path.Combine(folder.FullName, "rules");
        foreach (var file in Directory.EnumerateFiles(RegionPaths.Rules, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(rules, Path.GetRelativePath(RegionPaths.Rules, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(file, to);
        }
        var root = ResourceLoader.Load<PackedScene>(RegionPaths.Scene("ashen_ford")).Instantiate<Region>();
        try
        {
            if (!root.HasNode(Placements.Things)) Placements.Import(root, rules);
            var snapshot = new[] { "interactables/ashen_ford.json", "mobs/ashen_ford.json", "shops/ashen_ford.json", "world/ashen_ford.json" }
                .ToDictionary(p => p, p => File.ReadAllBytes(Path.Combine(rules, p)), StringComparer.Ordinal);
            if (Placements.Plan(root, rules).Count != 0) throw new InvalidDataException("Imported placements changed authored coordinates.");
            var once = RegionScene.Export(root, rules);
            if (once.Written is null) throw new InvalidDataException(string.Join('\n', once.Problems));
            foreach (var (file, bytes) in snapshot)
                if (!bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(rules, file)))) throw new InvalidDataException("No-op round trip changed " + file);
            var tempScene = Path.Combine(folder.FullName, "ashen_ford.tscn");
            RegionScene.Save(root, tempScene);
            var loaded = ResourceLoader.Load<PackedScene>(tempScene, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<Region>();
            try
            {
                var pack = RulesPack.Load([rules], ArtManifests.Load(RegionPaths.Content));
                var ground = pack.GroundFor("ashen_ford")!;
                var reached = pack.ReachFor("ashen_ford")!;
                Tile Next(Tile old) => new[] { new Tile(old.X + 1, old.Y), new Tile(old.X, old.Y + 1), new Tile(old.X - 1, old.Y), new Tile(old.X, old.Y - 1) }
                    .First(t => ground.Terrain.Walkable(t) && reached.Contains(t));
                var thing = loaded.GetNode(Placements.Things).GetChildren().OfType<InteractableMarker>().Single(m => m.Id == "af_square_notice");
                var thingAt = Next(thing.Tile); thing.Position = thing.PlaceOn(thingAt);
                var keeper = loaded.GetNode(RegionScene.People).GetChildren().OfType<PersonMarker>().Single(m => m.ShopId == "af_market_herbalist");
                var keeperAt = Next(keeper.Tile); keeper.Position = keeper.PlaceOn(keeperAt);
                var mob = loaded.GetNode(Placements.Mobs).GetChildren().OfType<MobPatchMarker>().First();
                var mobAt = mob.Tile;
                ExportResult? result = null;
                foreach (var delta in new[] { new Tile(1, 0), new Tile(0, 1), new Tile(-1, 0), new Tile(0, -1) })
                {
                    mob.Position = mob.PlaceOn(new Tile(mobAt.X + delta.X, mobAt.Y + delta.Y));
                    result = RegionScene.Export(loaded, rules);
                    if (result.Written is not null) break;
                }
                if (result?.Written is null) throw new InvalidDataException("Moved markers failed pack validation: " + string.Join(';', result?.Problems ?? []));
                var exported = RulesPack.Load([rules], ArtManifests.Load(RegionPaths.Content));
                if (exported.InteractablesIn("ashen_ford").Single(t => t.Id == thing.Id).At != thingAt) throw new InvalidDataException("Thing marker position was not exported.");
                var people = loaded.GetNode(RegionScene.People).GetChildren().OfType<PersonMarker>().ToArray();
                var index = PlacementEdits.Read(rules, "ashen_ford").Shops.Single(s => s.Id == keeper.ShopId).Person;
                if (exported.RegionFor("ashen_ford")!.Def.People[index].At != keeperAt) throw new InvalidDataException($"Shopkeeper marker was not exported: index {index}, expected {keeperAt}, marker {keeper.Tile}, actual {exported.RegionFor("ashen_ford")!.Def.People[index].At}.");
                var changed = snapshot.Keys.ToDictionary(p => p, p => File.ReadAllBytes(Path.Combine(rules, p)));
                var twice = RegionScene.Export(loaded, rules);
                if (twice.Written is null || changed.Any(p => !p.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(rules, p.Key)))))
                    throw new InvalidDataException("Second export was not byte-identical.");
                RegionScene.Save(loaded, tempScene, overwrite: true);
                var persisted = ResourceLoader.Load<PackedScene>(tempScene, cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<Region>();
                try { if (Placements.Plan(persisted, rules).Count != 0) throw new InvalidDataException("Moved placement markers did not survive scene save/load."); }
                finally { persisted.Free(); }
                GD.Print($"PLACEMENT CHECK PASS: Ashen Ford original bytes preserved; moved interactable, shopkeeper and mob patch exported; second export byte-identical; native scene save/load preserved positions. {people.Length} people.");
                return 0;
            }
            finally { loaded.Free(); }
        }
        finally { root.Free(); folder.Delete(true); }
    }
}
