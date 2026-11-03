using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

internal sealed partial class Validation
{
    public ShopsFile? ShopsFile(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
        {
            var def = Deserialize<ShopsFile>(e, file, ptr);
            if (def is null) return null;
            if (def.SellRatio is < 0 or > 1) Error(file, "/sellRatio", "The sell ratio must be between 0 and 1.");
            for (var i = 0; i < def.Shops.Length; i++)
            {
                var shop = def.Shops[i];
                var at = $"/shops/{i}";
                if (string.IsNullOrWhiteSpace(shop.Id) || string.IsNullOrWhiteSpace(shop.Name)) Error(file, at, "A shop needs an id and name.");
                if ((shop.Keeper.Person is null) == (shop.Keeper.Thing is null)) Error(file, at + "/keeper", "Choose exactly one person or thing.");
                if (shop.PriceMod is <= 0 or > 4) Error(file, at + "/priceMod", "Price modifier must be in (0, 4].");
                if (shop.Haggle is < 1 or > 30) Error(file, at + "/haggle", "Haggle DC must be 1 to 30.");
                var services = new HashSet<string>(StringComparer.Ordinal);
                for (var j = 0; j < shop.Services.Length; j++)
                {
                    var service = shop.Services[j];
                    var p = at + $"/services/{j}";
                    if (string.IsNullOrWhiteSpace(service.Id) || !services.Add(service.Id)) Error(file, p + "/id", "Services need distinct nonempty ids.");
                    if (string.IsNullOrWhiteSpace(service.Name)) Error(file, p + "/name", "A service needs a name.");
                    if (service.Kind is not ("room" or "cure")) Error(file, p + "/kind", "A service must be room or cure.");
                    if (service.Price <= 0) Error(file, p + "/price", "A paid service needs a positive price.");
                }
                var seen = new HashSet<ItemId>();
                for (var j = 0; j < shop.Stock.Length; j++)
                {
                    var stock = shop.Stock[j];
                    if (!seen.Add(stock.Item)) Error(file, at + $"/stock/{j}/item", "An item is listed twice.");
                    if (stock.Restock is not ("never" or "chapter" or "rest")) Error(file, at + $"/stock/{j}/restock", "Restock must be never, chapter or rest.");
                }
            }
            return def;
        });

    public void CheckShops(IEnumerable<Loaded<RegionShop>> shops, IEnumerable<Loaded<RegionDef>> regions,
        ImmutableSortedDictionary<string, BattleMap> maps, IEnumerable<Loaded<RegionThing>> things, IEnumerable<Loaded<ItemDef>> items)
    {
        var allRegions = regions.ToDictionary(r => r.Id, r => WorldRegion.From(r.Def));
        var allItems = items.ToDictionary(i => i.Id, i => i.Def);
        var allThings = things.ToDictionary(t => t.Id);
        var keepers = new HashSet<(string, int)>();
        var stalls = new HashSet<string>();
        foreach (var loaded in shops)
        {
            var shop = loaded.Def.Def;
            void ErrorAt(string field, string message) => Error(loaded.File, loaded.Pointer + "/" + field, message);
            if (!allRegions.TryGetValue(loaded.Def.Region, out var region)) { ErrorAt("keeper", $"Unknown region '{loaded.Def.Region}'."); continue; }
            Tile? tile = null;
            if (shop.Keeper.Person is { } person)
            {
                if (region.Def.People.IsDefault || person < 0 || person >= region.Def.People.Length) ErrorAt("keeper/person", $"Unknown person index {person}.");
                else
                {
                    tile = region.Def.People[person].At;
                    if (!keepers.Add((loaded.Def.Region, person))) ErrorAt("keeper/person", "A person can keep only one shop.");
                    if (!region.Def.People[person].Walk.IsDefaultOrEmpty) ErrorAt("keeper/person", "A shop keeper cannot wander.");
                }
            }
            if (shop.Keeper.Thing is { } thing)
            {
                if (!allThings.TryGetValue(thing, out var t) || t.Def.Region != loaded.Def.Region) ErrorAt("keeper/thing", $"Unknown thing '{thing}' in this region.");
                else if (t.Def.Def is not { Kind: "stall", Hidden: null, Lock: null, Trap: null }) ErrorAt("keeper/thing", "A shop thing must be a visible, unlocked, untrapped stall.");
                else tile = t.Def.Def.At;
                if (!stalls.Add(thing)) ErrorAt("keeper/thing", "A stall can keep only one shop.");
            }
            if (tile is { } at && (Art is not null || !WorldGround.NeedsArt(region.Def)))
            {
                var world = WorldGround.Of(region, maps, Art ?? ArtManifest.None);
                var solid = things.Where(t => t.Def.Region == region.Def.Id && Interactables.Solid(t.Def.Def)).Select(t => t.Def.Def.At);
                var ground = world.With(solid.ToArray());
                var reached = WorldWalk.Reach(ground, region.Def.Spawns.Start, ClassDef.DefaultJump);
                var target = new InteractableDef(shop.Id, "stall", at);
                if (!world.Terrain.Walkable(at) || !reached.Any(n => Interactables.InReach(ground, n, target))) ErrorAt("keeper", "The keeper must stand on walkable ground reachable from the start.");
                if (region.Def.Stamps.Any(s => maps.TryGetValue(s.Map, out var map) && at.X >= s.At.X && at.Y >= s.At.Y && at.X < s.At.X + map.Width && at.Y < s.At.Y + map.Height)) ErrorAt("keeper", "A shop cannot be on a stamp tile.");
            }
            for (var i = 0; i < shop.Stock.Length; i++)
            {
                var stock = shop.Stock[i];
                if (!allItems.TryGetValue(stock.Item.Value, out var item)) ErrorAt($"stock/{i}/item", $"Unknown item '{stock.Item}'.");
                else if (item.Price is null) ErrorAt($"stock/{i}/item", $"Item '{stock.Item}' has no price.");
                else if (item.Price.Value * shop.PriceMod > int.MaxValue) ErrorAt($"stock/{i}/item", "The modified price exceeds the gold limit.");
            }
            for (var i = 0; i < shop.Buys.Length; i++)
                if (!allItems.Values.Any(item => item.Tags.Contains(shop.Buys[i]))) ErrorAt($"buys/{i}", $"Unknown item tag '{shop.Buys[i]}'.");
        }
    }
}
