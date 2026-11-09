using Godot;
using Tactics.WorldTools;
namespace Tactics.World.Editor;

/// <summary>Editor markers for the existing side-content files; shopkeepers keep their authoritative People markers.</summary>
public static class Placements
{
    public const string Things = "Interactables", Mobs = "MobPatches";

    public static void Import(Region root, string rules)
    {
        if (root.HasNode(Things) || root.HasNode(Mobs)) throw new InvalidDataException("Placement groups already exist; move their markers instead of importing over painted work.");
        var source = PlacementEdits.Read(rules, root.Id);
        var people = OrderedPeople(root);
        foreach (var shop in source.Shops)
            if (shop.Person < 0 || shop.Person >= people.Length) throw new InvalidDataException($"Shop '{shop.Id}' has no person marker {shop.Person}.");
        foreach (var name in new[] { Things, Mobs })
        {
            var group = new Node2D { Name = name }; root.AddChild(group); group.Owner = root;
        }
        foreach (var point in source.Points)
        {
            TileMarker marker = point.Kind == PlacementKind.Interactable ? new InteractableMarker { Id = point.Id }
                : new MobPatchMarker { Id = point.Id, Offsets = PersonText.Walk(point.Offsets) };
            marker.Name = point.Id; marker.Position = marker.PlaceOn(point.At);
            root.GetNode(marker.Group).AddChild(marker); marker.Owner = root;
        }
        foreach (var shop in source.Shops)
        {
            if (shop.Person < 0 || shop.Person >= people.Length) throw new InvalidDataException($"Shop '{shop.Id}' has no person marker {shop.Person}.");
            people[shop.Person].ShopId = shop.Id;
            people[shop.Person].QueueRedraw();
        }
    }

    public static IReadOnlyDictionary<string, byte[]> Plan(Region root, string rules)
    {
        if (!root.HasNode(Things) && !root.HasNode(Mobs)) return new Dictionary<string, byte[]>(); // existing scenes remain valid
        if (!root.HasNode(Things) || !root.HasNode(Mobs)) throw new InvalidDataException("Both Interactables and MobPatches groups must be present.");
        foreach (var name in new[] { Things, Mobs })
        {
            if (root.GetNode<Node2D>(name).Transform != Transform2D.Identity) throw new InvalidDataException($"Keep {name} at the origin.");
            if (root.GetNode(name).GetChildren().Any(c => c is not TileMarker)) throw new InvalidDataException($"Only placement markers belong under {name}.");
        }
        var source = PlacementEdits.Read(rules, root.Id);
        var things = root.GetNode(Things).GetChildren().OfType<InteractableMarker>().Select(m => new PlacementPoint(PlacementKind.Interactable, m.Id, m.Tile));
        var mobs = root.GetNode(Mobs).GetChildren().OfType<MobPatchMarker>().Select(m => new PlacementPoint(PlacementKind.MobPatch, m.Id, m.Tile, PersonText.Walk(m.Offsets)));
        var result = source.Plan(things.Concat(mobs)).ToDictionary(p => p.Key, p => p.Value);
        // RegionWriter sorts people by position. Keep each shop attached to its labelled person after dragging.
        var ordered = OrderedPeople(root);
        var labels = ordered.Select((p, i) => (p.ShopId, Index: i)).Where(p => p.ShopId.Length > 0)
            .ToDictionary(p => p.ShopId, p => p.Index, StringComparer.Ordinal);
        foreach (var update in source.PlanShops(labels)) result.Add(update.Key, update.Value);
        return result;
    }
    private static PersonMarker[] OrderedPeople(Region root)
    {
        var markers = root.GetNode(RegionScene.People).GetChildren().OfType<PersonMarker>().ToArray();
        var people = RegionScene.Read(root).People;
        var byKey = people.Select((p, i) => (Key: PersonText.Key(p), Marker: markers[i]))
            .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Marker);
        return PersonText.Ordered(people).Select(p => byKey[PersonText.Key(p)]).ToArray();
    }
}
