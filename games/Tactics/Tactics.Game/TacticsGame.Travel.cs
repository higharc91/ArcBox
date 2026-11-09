using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>A landmark on the leader's map of the region: only what the TV already shows (a beat's "!", a quest giver, a shop…).</summary>
/// <param name="Kind">What it is, for its icon: story, quest, turnIn, pinned, shop, inn, temple, rest, place or person.</param>
/// <param name="Label">Its name, as the phone's list and the pin's caption say it.</param>
/// <param name="At">Its tile.</param>
public sealed record MapPin(string Kind, string Label, Tile At);

/// <summary>
/// Tap to travel: the leader picks a tile on the phone's map of the region and the party walks there by itself, one step at a time
/// through the very step the pad takes (<see cref="TakeStep"/>), so its speed, saves, mobs, stamps and beats are a held walk's. A journey
/// is held as if the pad were, with no lease, and lives only in memory: anything that stops the party (<see cref="Halt"/>) ends it, and a
/// restart simply finds the party stopped where the last save put it.
/// </summary>
public sealed partial class TacticsGame
{
    /// <summary>What the leader's screen says when no walk reaches the tile they picked. The user's to reword.</summary>
    internal const string NoWayThere = "Can't find a way there.";

    /// <summary>How far, in tiles each way, a pick that isn't ground the party can reach looks for the nearest that is.</summary>
    internal const int TravelSnap = 4;

    /// <summary>A journey under way: where to, the pad's taps that get there, the next one to take, and whether it has been re-routed.</summary>
    private sealed record Journey(Tile Goal, ImmutableArray<WalkDir> Taps, int Next, bool Rerouted);

    private Journey? _travel;

    /// <summary>What the last travel request came to when it went nowhere (<see cref="NoWayThere"/>), for the leader's screen until the next walk.</summary>
    private string? _travelNote;

    /// <summary>The member whose phone has the map open: only ever the leader's, and only while they lead and the party is in the world.</summary>
    private string? _mapOpen;

    private bool MapShown(string member) => _mapOpen == member && _lead == member && _phase == TacticsPhase.Exploration && _region is not null;

    private void MapInput(Player player, bool open)
    {
        if (player.Profile?.Value is not { } member)
            return;
        if (open && member == _lead && _region is not null && !QuestLogOpen)
            _mapOpen = member;
        else if (!open && _mapOpen == member)
            _mapOpen = null;
    }

    /// <summary>
    /// The leader asks to travel to <c>{x, y}</c>. Only in the world, with the quest log shut and no fight closing in; anything else, and
    /// anyone else, is ignored. A pick the party can't stand on, or can't reach, goes to the nearest tile it can within <see cref="TravelSnap"/>.
    /// The walk keeps clear of the stamp of the fight on offer unless that's where it's going or there's no other way.
    /// </summary>
    private void TravelInput(Player player, JsonElement? data)
    {
        if (QuestLogOpen || _region is null || player.Profile?.Value is not { } member || member != _lead || _doc.World?.MobAmbush is not null)
            return;
        if (!ReadTile(data, out var target) || !WalkGround().Map.InBounds(target))
            return;
        _mapOpen = null;
        _travelNote = null;
        var plan = Plan(target);
        if (plan is not { } found)
        {
            _travelNote = NoWayThere;
            return;
        }
        if (found.Taps.IsEmpty)
            return;     // already there
        Halt();         // whatever the pad or an earlier journey was doing
        _travel = new Journey(found.Goal, found.Taps, 0, false);
        (_dir, _blocked, _owed, _latched) = (found.Taps[0], false, false, false);
        _heldUntil = DateTimeOffset.MaxValue;     // held with no lease: the journey, not a thumb, says when it stops
        if (_ctx.Now >= _stepDue)
            TravelStep(onTime: false);
        else
        {
            _owed = true;       // still on its way to a tile: the journey starts as that step ends
            ArmStep();
        }
    }

    /// <summary>Reads <c>{x, y}</c>: false for anything else.</summary>
    private static bool ReadTile(JsonElement? data, out Tile tile)
    {
        tile = default;
        if (data is not { ValueKind: JsonValueKind.Object } d || !d.TryGetProperty("x", out var x) || !d.TryGetProperty("y", out var y)
            || x.ValueKind != JsonValueKind.Number || y.ValueKind != JsonValueKind.Number || !x.TryGetInt32(out var tx) || !y.TryGetInt32(out var ty))
            return false;
        tile = new Tile(tx, ty);
        return true;
    }

    /// <summary>Where a pick of <paramref name="target"/> goes and the taps that walk there; null if nowhere near it can be reached.</summary>
    private (Tile Goal, ImmutableArray<WalkDir> Taps)? Plan(Tile target)
    {
        var ground = WalkGround();
        var reach = WorldWalk.Reach(ground, _at, ClassDef.DefaultJump);
        Tile? goal = reach.Contains(target) ? target
            : reach.Where(t => Math.Max(Math.Abs(t.X - target.X), Math.Abs(t.Y - target.Y)) <= TravelSnap)
                .OrderBy(t => (t.X - target.X) * (t.X - target.X) + (t.Y - target.Y) * (t.Y - target.Y)).ThenBy(t => t)
                .Select(t => (Tile?)t).FirstOrDefault();
        return goal is { } to && RouteTo(to) is { } taps ? (to, taps) : null;
    }

    /// <summary>The taps from the party's tile to <paramref name="goal"/> on the ground as it is now, round the armed stamp if there's a way round.</summary>
    private ImmutableArray<WalkDir>? RouteTo(Tile goal)
    {
        var ground = WalkGround();
        return (Armed() is { Stage: var stamp } ? WorldWalk.Route(ground, _at, t => t == goal, ClassDef.DefaultJump, stamp.Holds) : null)
            ?? WorldWalk.Route(ground, _at, t => t == goal, ClassDef.DefaultJump);
    }

    /// <summary>
    /// The journey's next step, through <see cref="TakeStep"/>. A step the ground no longer allows as planned (a door shut, a gate closed)
    /// routes again from where the party stands, once; a second, or no way at all, and the party stops. So does anything the step itself
    /// leads to that isn't the next tile of the walk: a beat, a fight, a scene, a trap that moves it.
    /// </summary>
    private void TravelStep(bool onTime)
    {
        var journey = _travel!;
        if (!Planned(journey.Taps[journey.Next]))
        {
            if (journey.Rerouted || RouteTo(journey.Goal) is not { IsEmpty: false } again)
            {
                StopWalking();
                return;
            }
            journey = new Journey(journey.Goal, again, 0, true);
        }
        var dir = journey.Taps[journey.Next];
        var (dx, dy) = WorldWalk.Delta(dir);
        var want = new Tile(_at.X + dx, _at.Y + dy);
        (_travel, _dir) = (journey, dir);
        TakeStep(onTime);
        if (!ReferenceEquals(_travel, journey))
            return;     // the step stopped the party: a fight, a beat, a scene
        if (_phase != TacticsPhase.Exploration || _at != want || _blocked || journey.Next + 1 >= journey.Taps.Length)
        {
            StopWalking();      // arrived, or moved somewhere the walk didn't mean
            return;
        }
        _travel = journey with { Next = journey.Next + 1 };
        _dir = _travel.Taps[_travel.Next];
    }

    /// <summary>Whether the pad's <paramref name="dir"/> still takes the party exactly one tile that way, as the route planned it.</summary>
    private bool Planned(WalkDir dir)
    {
        var (dx, dy) = WorldWalk.Delta(dir);
        return WorldWalk.Next(WalkGround(), _at, dir, ClassDef.DefaultJump, _lastAlongX) is { Slid: false } move && move.To == new Tile(_at.X + dx, _at.Y + dy);
    }

    /// <summary>The leader's map, while it's open on their phone: the region, the party's tile, where it's going, and the landmarks.</summary>
    private object? MapView(string member) => !MapShown(member) ? null : new
    {
        region = _region!.Def.Id,
        at = _at,
        to = _travel?.Goal,
        pins = MapPins(),
    };

    /// <summary>
    /// The landmarks the map shows, one to a tile: only what the TV already draws or says. The beat on offer (its "!"), quest givers and
    /// turn-ins, the pinned quest's target, the shops, inn and temple open now, campfires, shrines, wells and cave mouths that have been
    /// seen, and the region's named people who are about and have something to say. Never a thing that's hidden or not yet found.
    /// </summary>
    private ImmutableArray<MapPin> MapPins()
    {
        var pins = new List<MapPin>();
        foreach (var marker in Markers())
        {
            var beat = BeatNamed(marker.Beat);
            var place = beat?.Place is { } p ? Lore.PlaceName(p) : null;
            pins.Add(new MapPin("story", place ?? Lore.ScenesOf(marker.Beat).FirstOrDefault()?.Title ?? "The story", marker.At));
        }
        if (QuestTracker() is { Target: { } target } tracker)
            pins.Add(new MapPin("pinned", tracker.Name, target));
        var named = PresentPeople().ToDictionary(p => p.Person.At, p => p.Person);
        string Who(Tile at) => named.TryGetValue(at, out var person) && person.Character is { } c ? Lore.Character(c)?.Name ?? c : "Someone";
        foreach (var marker in QuestPersonMarkers() ?? [])
            pins.Add(new MapPin(marker.TurnIn ? "turnIn" : "quest", Who(marker.At), marker.At));
        foreach (var shop in RegionShops.Where(s => ShopRules.Present(s, StoryFlags())))
        {
            var kind = shop.Services.Any(s => s.Kind == "room") ? "inn" : shop.Services.IsEmpty ? "shop" : "temple";
            pins.Add(new MapPin(kind, shop.Name, KeeperTile(shop)));
        }
        foreach (var thing in ThingViews() ?? [])
            if (thing.Kind switch { "campfire" => "rest", "shrine" or "well" or "cave_mouth" or "bounty_board" => "place", _ => null } is { } kind)
                pins.Add(new MapPin(kind, thing.Name, thing.At));
        foreach (var (person, _) in PresentPeople())
            if (person.Character is { } character && (ConversationFor(person) is not null || Lore.Barks.For(person) is not null))
                pins.Add(new MapPin("person", Lore.Character(character)?.Name ?? character, person.At));
        var seen = new HashSet<Tile>();
        return [.. pins.Where(p => seen.Add(p.At))];
    }
}
