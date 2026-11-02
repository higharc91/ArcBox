using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>The checks of things to interact with (interactables design §5).</summary>
internal sealed partial class Validation
{
    private static readonly string[] Facings = ["ne", "se", "sw", "nw"];

    /// <summary>
    /// An interactables file (<c>interactables/&lt;region&gt;.json</c>): each thing's shape on its own, what its kind allows, and its item and
    /// art references. Where it stands and what it opens is checked against its region by <see cref="ThingCheck"/>, once everything is loaded.
    /// </summary>
    public InteractablesFile? ThingsFile(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
        {
            var list = Deserialize<InteractablesFile>(e, file, ptr);
            if (list is null)
                return null;
            var errors = Errors.Count;
            if (string.IsNullOrWhiteSpace(list.Region))
                Error(file, Pointer.Append(ptr, "region"), "The region is empty.");
            for (var i = 0; i < list.Interactables.Length; i++)
                Thing(list.Interactables[i], file, Pointer.Append(Pointer.Append(ptr, "interactables"), i));
            return Errors.Count == errors ? list : null;
        });

    private void Thing(InteractableDef def, string file, string ptr)
    {
        void At(string field, string message) => Error(file, field.Split('/').Aggregate(ptr, Pointer.Append), message);
        void Dc(int? dc, string field, string what)
        {
            if (dc is { } value and (< 1 or > Interactables.MaxDc))
                At(field, $"{what} DC {value} must be 1 to {Interactables.MaxDc}.");
        }

        if (InteractableKinds.Of(def.Kind) is not { } kind)
        {
            At("kind", $"Unknown kind '{def.Kind}' (expected one of {string.Join(", ", InteractableKinds.All.Keys)}).");
            return;
        }
        var family = kind.Family;
        if (def.Facing is { } facing && !Facings.Contains(facing))
            At("facing", $"The facing '{facing}' must be ne, se, sw or nw.");
        if (def.Art is { } art)
            Ref(RefKind.Art, art, file, Pointer.Append(ptr, "art"));
        else if (family == InteractableFamily.Ambient)
            At("art", "An ambient thing has no art of its kind, so it needs an art key of the pack's, such as \"props.cart\".");

        if (def.Hidden is { } hidden)
        {
            Dc(hidden.Dc, "hidden", "The hidden");
            if (!HiddenDef.Skills.Contains(hidden.Skill))
                At("hidden/skill", $"A thing is hidden by a passive skill: '{hidden.Skill}' isn't one of {string.Join(", ", HiddenDef.Skills)}.");
        }
        if (kind.MustHide && def.Hidden is null)
            At("hidden", $"A {kind.Id} is always hidden: give the passive Perception DC that finds it, or {{ \"dc\", \"skill\" }} for another passive skill.");
        if (def.Range is { } range)
        {
            if (range is < 1 or > Interactables.MaxRange)
                At("range", $"The range {range} must be 1 to {Interactables.MaxRange}.");
            if (def.Hidden is null && def.Trap is null)
                At("range", "A range is how far away the party's passive skills find a thing or see its trap: this one is neither hidden nor trapped.");
        }
        if (!def.Reveals.IsEmpty && family != InteractableFamily.Notice)
            At("reveals", $"Only a notice reveals other things, not a {kind.Id}.");
        if (def.Reveals.Any(string.IsNullOrWhiteSpace))
            At("reveals", "An id is empty.");
        if (def.Reveals.Contains(def.Id))
            At("reveals", $"'{def.Id}' reveals itself.");
        if (family == InteractableFamily.Notice && def.Text.IsEmpty)
            At("text", $"A {kind.Id} needs its words: what the party notices (\"Fresh wolf tracks lead north, into the trees.\").");
        if (family == InteractableFamily.Path && def.Facing is null)
            At("facing", "A hidden path needs its facing: which way the hedge, wall or water it hides in runs (sw or ne along x, se or nw along y), so the TV draws it as that until it's found.");
        if (family == InteractableFamily.Path && (!def.Requires.IsEmpty || !def.Unless.IsEmpty))
            At(def.Requires.IsEmpty ? "unless" : "requires", "A hidden path is always there: one that came and went would open and wall the ground under the party's feet. Gate what it leads to instead.");
        if (def.Text.Any(string.IsNullOrWhiteSpace))
            At("text", "A line is empty.");
        if (family is InteractableFamily.Readable or InteractableFamily.Shrine && def.Text.IsEmpty)
            At("text", $"A {kind.Id} needs words to show.");

        if (def.Lock is { } lk)
        {
            if (family is not (InteractableFamily.Container or InteractableFamily.Passage))
                At("lock", $"A {kind.Id} can't be locked: only containers and passages can.");
            if (!lk.Approaches.Any())
                At("lock", "A lock needs a way through it: a key, or a pick, force or seal DC.");
            if (lk.Key is not null && string.IsNullOrWhiteSpace(lk.Key))
                At("lock/key", "The key's flag is empty.");
            if (lk.Key is null && lk.KeyName is not null)
                At("lock/keyName", "A key name with no key.");
            Dc(lk.Pick, "lock/pick", "The pick");
            Dc(lk.Force, "lock/force", "The force");
            Dc(lk.Seal, "lock/seal", "The seal");
        }

        if (def.Trap is { } trap)
        {
            if (family is not (InteractableFamily.Container or InteractableFamily.Passage or InteractableFamily.FloorTrap))
                At("trap", $"A {kind.Id} can't be trapped: only containers, passages and the ground can.");
            Dc(trap.Spot, "trap/spot", "The spot");
            Dc(trap.Disarm, "trap/disarm", "The disarm");
            if (family == InteractableFamily.FloorTrap && trap.Disarm is null)
                At("trap/disarm", "A trap on the ground needs a disarm DC: once it's seen the party walks round it, so it must be something they can clear.");
            if (trap.Text.Any(string.IsNullOrWhiteSpace))
                At("trap/text", "A line is empty.");
            if (trap.Lose is { } lose)
                Ref(RefKind.Item, lose.Value, file, Pointer.Append(ptr, "trap/lose"));
        }
        else if (family == InteractableFamily.FloorTrap)
            At("trap", "A trap needs its trap: at least the passive Perception DC that spots it and the DC that disarms it.");

        if (!def.Loot.IsEmpty && family is not (InteractableFamily.Container or InteractableFamily.Pickup))
            At("loot", $"A {kind.Id} holds nothing: only containers and pickups have loot.");
        for (var i = 0; i < def.Loot.Length; i++)
        {
            var at = Pointer.Append(Pointer.Append(ptr, "loot"), i);
            Ref(RefKind.Item, def.Loot[i].Item.Value, file, Pointer.Append(at, "item"));
            if (def.Loot[i].Count is < 1 or > 99)
                Error(file, Pointer.Append(at, "count"), $"The count {def.Loot[i].Count} must be 1 to 99.");
        }
        if (family == InteractableFamily.Pickup && def.Loot.IsEmpty && def.Sets.IsEmpty)
            At("loot", "A pickup needs something to pick up: loot, or a flag it gives, such as a key.");

        if (!def.Targets.IsEmpty && family != InteractableFamily.Switch)
            At("targets", $"Only a lever works other things, not a {kind.Id}.");
        if (family == InteractableFamily.Switch && def.Targets.IsEmpty && def.Sets.IsEmpty)
            At("targets", "A lever that works nothing and sets no flag does nothing.");
        if (def.Open && family is not (InteractableFamily.Passage or InteractableFamily.Switch))
            At("open", $"Only a passage can start open, or a lever on; not a {kind.Id}.");
        if (def.Blocks is not null && family is InteractableFamily.Passage or InteractableFamily.FloorTrap or InteractableFamily.Path)
            At("blocks", $"Whether a {kind.Id} is in the way depends on its state, so it can't be set.");
        if (def.Blocks == true && family == InteractableFamily.Notice)
            At("blocks", $"A {kind.Id} is only ever noticed: it never stands in the way.");
        if (!def.Sets.IsEmpty && family is InteractableFamily.Ambient or InteractableFamily.FloorTrap)
            At("sets", family == InteractableFamily.Ambient
                ? "An ambient thing never changes, so it sets nothing."
                : "A trap's flags are its trap's: put them in trap/sets, which are set when it goes off.");
        foreach (var (list, name) in new[] { (def.Requires, "requires"), (def.Unless, "unless"), (def.Sets, "sets") })
            if (list.Any(string.IsNullOrWhiteSpace))
                At(name, "A flag is empty.");
    }

    /// <summary>
    /// The things of one region against the region (design §5): each stands on a walkable tile of the region, outside every stamp, on a tile of
    /// its own that's no spawn and no beat's spot; a lever works passages of the same region that start as it does, and no passage has two
    /// levers or a lock as well; every thing can be reached from the start with every passage open; every lock has a way through it that
    /// can't be lost for good, or nothing the story needs behind it; and a key a thing of the region gives can be had before its lock.
    /// </summary>
    private sealed class ThingCheck(Validation v, WorldRegion region, IReadOnlyDictionary<string, BattleMap> maps, ImmutableArray<BattleStage> stamps,
        List<Loaded<RegionThing>> things, IReadOnlySet<string> injuries)
    {
        private readonly RegionDef _def = region.Def;
        /// <summary>The ground, or null for a pack loaded with no art and a region that needs some: what it blocks can't be said, so walking isn't judged.</summary>
        private readonly WorldGround? _world = v.Art is null && WorldGround.NeedsArt(region.Def) ? null : WorldGround.Of(region, maps, v.Art ?? ArtManifest.None);
        /// <summary>The ground, once it's known there is one: only the reach checks read it.</summary>
        private WorldGround World => _world ?? throw new InvalidOperationException("The reach of things is judged only on a ground.");

        private readonly List<(InteractableDef Def, string File, string Ptr)> _things = [.. things.Select(t => (t.Def.Def, t.File, t.Pointer))];

        private static InteractableFamily Family(InteractableDef d) => Interactables.KindOf(d).Family;

        public void Run()
        {
            var placed = Placement();
            Levers();
            Reveals();
            Injuries();
            if (placed && _world is not null)
                Reach();
        }

        private void Error((InteractableDef Def, string File, string Ptr) t, string field, string message)
            => v.Error(t.File, field.Split('/', StringSplitOptions.RemoveEmptyEntries).Aggregate(t.Ptr, Pointer.Append), message);

        /// <summary>Where each stands. False when one stands where reach can't be judged: off the region or on void.</summary>
        private bool Placement()
        {
            var ok = true;
            var byTile = new Dictionary<Tile, string>();
            var spots = _def.Beats.ToDictionary(b => b.At, b => b.Beat);
            var props = new Dictionary<Tile, string>();
            foreach (var group in _def.Props.IsDefault ? [] : _def.Props)
                foreach (var tile in group.At)
                    props.TryAdd(tile, group.Id);
            foreach (var t in _things)
            {
                var (def, at) = (t.Def, t.Def.At);
                if (region.TileAt(at) is null)
                {
                    Error(t, "at", $"'{def.Id}' at {at} is outside the region or on void.");
                    ok = false;
                    continue;
                }
                if (stamps.Where(s => s.Holds(at)).Select(s => s.Map).FirstOrDefault() is { } map)
                    Error(t, "at", $"'{def.Id}' at {at} is inside the stamp '{map}': a fight there would play over it.");
                if (!byTile.TryAdd(at, def.Id))
                    Error(t, "at", $"'{def.Id}' at {at} shares its tile with '{byTile[at]}'.");
                if (at == _def.Spawns.Start || at == _def.Spawns.Recovery)
                    Error(t, "at", $"'{def.Id}' at {at} is on a spawn, where the party stands.");
                if (spots.TryGetValue(at, out var beat))
                    Error(t, "at", $"'{def.Id}' at {at} is on the spot of the beat '{beat}', where its figure stands.");
                if (props.TryGetValue(at, out var prop))
                    Error(t, "at", $"'{def.Id}' at {at} is on the painted prop '{prop}': a thing stands on a tile of its own, as a prop can't be on a stamp. Move the thing, or paint the prop off its tile.");
                else if (_world is not null && !_world.Terrain.Walkable(at))
                    Error(t, "at", $"'{def.Id}' at {at} isn't on ground that can be walked: a thing stands on open ground, and a passage is open ground while it's open.");
            }
            return ok;
        }

        private void Levers()
        {
            var byId = _things.ToDictionary(t => t.Def.Id, StringComparer.Ordinal);
            var workedBy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var lever in _things.Where(t => Family(t.Def) == InteractableFamily.Switch))
                for (var i = 0; i < lever.Def.Targets.Length; i++)
                {
                    var field = $"targets/{i}";
                    var id = lever.Def.Targets[i];
                    if (!byId.TryGetValue(id, out var target))
                    {
                        Error(lever, field, $"The lever '{lever.Def.Id}' works '{id}', which isn't in this region.");
                        continue;
                    }
                    if (Family(target.Def) != InteractableFamily.Passage)
                    {
                        Error(lever, field, $"The lever '{lever.Def.Id}' works '{id}', which isn't a passage.");
                        continue;
                    }
                    if (!workedBy.TryAdd(id, lever.Def.Id))
                        Error(lever, field, $"'{id}' is worked by '{workedBy[id]}' already: two levers would fight over it.");
                    if (target.Def.Open != lever.Def.Open)
                        Error(lever, field, $"'{id}' starts {(target.Def.Open ? "open" : "shut")}, but the lever '{lever.Def.Id}' starts {(lever.Def.Open ? "on" : "off")}: a lever's passages are open while it's on.");
                    if (target.Def.Lock is not null)
                        Error(target, "lock", $"'{id}' is worked by the lever '{lever.Def.Id}', so it can't have a lock as well.");
                    if (target.Def.Hidden is not null)
                        Error(target, "hidden", $"'{id}' is worked by the lever '{lever.Def.Id}', so it can't be hidden as well.");
                }
        }

        /// <summary>A notice reveals hidden things of its own region (design §7.2).</summary>
        private void Reveals()
        {
            var byId = _things.ToDictionary(t => t.Def.Id, StringComparer.Ordinal);
            foreach (var notice in _things.Where(t => !t.Def.Reveals.IsDefaultOrEmpty))
                for (var i = 0; i < notice.Def.Reveals.Length; i++)
                {
                    var id = notice.Def.Reveals[i];
                    if (!byId.TryGetValue(id, out var target))
                        Error(notice, $"reveals/{i}", $"'{notice.Def.Id}' reveals '{id}', which isn't in this region.");
                    else if (target.Def.Hidden is null)
                        Error(notice, $"reveals/{i}", $"'{notice.Def.Id}' reveals '{id}', which isn't hidden: there's nothing to find.");
                }
        }

        private void Injuries()
        {
            foreach (var t in _things)
                if (t.Def.Trap?.Injury is { } injury && !injuries.Contains(injury))
                    Error(t, "trap/injury", $"Unknown injury '{injury}'.");
        }

        /// <summary>The flags a thing is sure to give once the party reaches it: anything with no check and no lock it could fail.</summary>
        private static bool SureToGive(InteractableDef def, IReadOnlySet<string> keys)
            => def.Hidden is null && def.Requires.IsEmpty && def.Unless.IsEmpty
               && (def.Lock is null || def.Lock.Key is { } key && keys.Contains(key))
               && Family(def) is not InteractableFamily.FloorTrap;

        /// <summary>
        /// What the party can reach from <paramref name="from"/> (the start when null): <paramref name="optimistic"/>, with every passage open;
        /// else only what's sure, whatever has happened before: every passage shut that can be (one that starts shut, and every one a lever works,
        /// whichever way its lever was left), opening only those that open with no check (unlocked, or a key the party is sure to have, or a
        /// lever it can reach), with keys from things it's sure to get into, until nothing more opens. Things that stand in the way always do.
        /// So a tile whose sure closure holds the start can always walk back to it, under every state the levers and locks can be in.
        /// </summary>
        private (HashSet<Tile> Reached, HashSet<string> Keys, List<InteractableDef> Shut) Closure(bool optimistic, Tile? from = null)
        {
            var solid = _things.Where(t => Interactables.Solid(t.Def)).Select(t => t.Def.At).ToHashSet();
            var worked = Interactables.Worked(_things.Select(t => t.Def));
            // A hidden path is shut until it's found, which may never be: it counts as a hidden door does (design §7.3).
            var shut = optimistic ? [] : _things.Where(t => Family(t.Def) == InteractableFamily.Path
                    || (Family(t.Def) == InteractableFamily.Passage && (!t.Def.Open || worked.Contains(t.Def.Id))))
                .Select(t => t.Def).ToList();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var levers = _things.Where(t => Family(t.Def) == InteractableFamily.Switch).Select(t => t.Def).ToList();
            var start = from ?? _def.Spawns.Start;
            while (true)
            {
                var ground = World.With([.. solid, .. shut.Select(d => d.At)]);
                var reached = ground.Walkable(start) ? WorldWalk.Reach(ground, start, ClassDef.DefaultJump) : [];
                bool Near(InteractableDef d) => Neighbours(d.At).Any(n => reached.Contains(n) && Interactables.InReach(ground, n, d));
                var before = keys.Count;
                foreach (var t in _things.Where(t => Near(t.Def) && SureToGive(t.Def, keys)))
                    keys.UnionWith(t.Def.Sets);
                var opened = shut.Where(d => Near(d) && d.Hidden is null && (worked.Contains(d.Id)
                        ? levers.Any(l => l.Targets.Contains(d.Id) && Near(l))
                        : d.Lock is null || d.Lock.Key is { } key && keys.Contains(key)))
                    .ToList();
                if (opened.Count == 0 && keys.Count == before)
                    return (reached, keys, shut);
                shut.RemoveAll(opened.Contains);
            }
        }

        private static IEnumerable<Tile> Neighbours(Tile at)
        {
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    yield return new Tile(at.X + dx, at.Y + dy);
        }

        private void Reach()
        {
            if (!World.Terrain.Walkable(_def.Spawns.Start))
                return;     // the region check reports it, and nothing can be judged from it
            var (open, _, _) = Closure(optimistic: true);
            var ground = World.With([.. _things.Where(t => Interactables.Solid(t.Def)).Select(t => t.Def.At)]);
            foreach (var t in _things.Where(t => !Neighbours(t.Def.At).Any(n => open.Contains(n) && Interactables.InReach(ground, n, t.Def))))
                Error(t, "at", $"'{t.Def.Id}' at {t.Def.At} can't be reached from the start, even with every passage open.");

            var (sure, keys, shut) = Closure(optimistic: false);
            var required = new List<(string What, Tile At)>
            {
                ("the recovery spawn", _def.Spawns.Recovery),
            };
            required.AddRange(_def.Beats.Select(b => ($"the beat '{b.Beat}'", b.At)));
            foreach (var stamp in stamps)
                if (maps.GetValueOrDefault(stamp.Map)?.Zones.GetValueOrDefault("party_spawn") is { IsDefault: false } zone)
                    required.AddRange(zone.Select(z => ($"the party spawn of the stamp '{stamp.Map}'", stamp.ToWorld(z))));
            var solid = string.Join(", ", _things.Where(t => Interactables.Solid(t.Def)).Select(t => $"'{t.Def.Id}'"));
            var painted = WorldWalk.Reach(World.Terrain, _def.Spawns.Start, ClassDef.DefaultJump);     // what the region check judges
            foreach (var (what, at) in required.Where(r => !open.Contains(r.At) && painted.Contains(r.At)).DistinctBy(r => r.What))
                v.Error(things[0].File, Pointer.Append(things[0].Pointer, "at"),
                    $"{Interactables.Capital(what)} at {at} is walled off by things that stand in the way ({solid}).");
            var blamed = string.Join(", ", shut.Select(d => $"'{d.Id}'"));
            foreach (var (what, at) in required.Where(r => open.Contains(r.At) && !sure.Contains(r.At)).DistinctBy(r => r.What))
                v.Error(things[0].File, Pointer.Append(things[0].Pointer, "at"),
                    $"{Interactables.Capital(what)} at {at} is behind a passage that may never open ({blamed}): a check can be failed for good and a hidden door "
                    + "or path never found, so give it a key the party is sure to find first, or leave another way round.");

            var givers = _things.SelectMany(t => t.Def.Sets.Select(s => (Flag: s, t.Def))).ToLookup(g => g.Flag, g => g.Def, StringComparer.Ordinal);
            foreach (var t in _things.Where(t => t.Def.Lock?.Key is { } key && givers.Contains(key) && !keys.Contains(key)))
                Error(t, "lock/key", $"The key '{t.Def.Lock!.Key}' of '{t.Def.Id}' can't be had before it: what gives it ("
                    + string.Join(", ", givers[t.Def.Lock.Key!].Select(d => $"'{d.Id}'")) + ") is behind it, hidden, or behind a lock that can be failed.");

            // The places the party is put without walking there: it wakes at the recovery spawn, lands by a stamp after a fight, and travels to a
            // beat's spot for a scene set there. From each, whatever the levers and locks are by then, it must be sure to walk back to the start.
            var dropped = new List<(string What, Tile At)> { ("The recovery spawn", _def.Spawns.Recovery) };
            dropped.AddRange(_def.Beats.Select(b => ($"The spot of the beat '{b.Beat}'", b.At)));
            foreach (var stamp in stamps)
                dropped.Add(($"The landing by the stamp '{stamp.Map}'",
                    maps.GetValueOrDefault(stamp.Map)?.Zones.GetValueOrDefault("party_spawn") is { IsDefault: false, Length: > 0 } zone
                        ? stamp.ToWorld(zone[0]) : stamp.Origin));
            foreach (var (what, at) in dropped.Where(d => sure.Contains(d.At)))      // what isn't sure from the start is reported above
            {
                var (back, _, closed) = Closure(optimistic: false, from: at);
                if (!back.Contains(_def.Spawns.Start))
                    v.Error(things[0].File, Pointer.Append(things[0].Pointer, "at"),
                        $"{what} at {at} can't be sure of a way back to the start ({string.Join(", ", closed.Where(d => Neighbours(d.At).Any(back.Contains)).Select(d => $"'{d.Id}'"))} may be shut): "
                        + "the party is put there without walking, so give it a lever, a key or a way round on its own side.");
            }
        }
    }
}
