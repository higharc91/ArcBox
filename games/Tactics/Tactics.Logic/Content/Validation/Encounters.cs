using System.Globalization;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic;

/// <summary>Encounters and their triggers.</summary>
internal sealed partial class Validation
{
    /// <summary>
    /// An encounter: party size, how each unit is placed (and named) and the monsters, AI profiles and map it names. Its
    /// zones and unit tags are checked against its map by <see cref="CheckEncounters"/>. Triggers without <c>"once"</c> get
    /// <c>once: true</c>, and objectives without <c>"lose"</c> get <c>party_out</c>.
    /// </summary>
    public EncounterDef? Encounter(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
        {
            if (!TriggerEffects(e, file, ptr))
                return null;
            var def = Deserialize<EncounterDef>(e, file, ptr, raw =>
            {
                var objectives = raw.Objectives is null ? ObjectivesDef.Default
                    : raw.Objectives.Lose is null ? raw.Objectives with { Lose = ObjectivesDef.Default.Lose }
                    : raw.Objectives;
                var triggers = raw.Triggers.IsDefault ? raw.Triggers
                    : [.. raw.Triggers.Select((t, i) => t is null || HasOnce(e, i) ? t! : t with { Once = true })];
                return raw with { Objectives = objectives, Triggers = triggers };
            });
            if (def is null)
                return null;
            Ref(RefKind.Map, def.Map, file, Pointer.Append(ptr, "map"));
            if (def.Level is < 1 or > 10)
                Error(file, Pointer.Append(ptr, "level"), $"The level {def.Level} must be in 1..10.");
            if (def.Party.Min < 1 || def.Party.Max < def.Party.Min)
                Error(file, Pointer.Append(ptr, "party"), "The party size needs 1 ≤ min ≤ max.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (units, name) in new[] { (def.Enemies, "enemies"), (def.Npcs, "npcs") })
                for (var i = 0; i < units.Length; i++)
                {
                    var at = Pointer.Append(Pointer.Append(ptr, name), i);
                    Ref(RefKind.Monster, units[i].Unit, file, Pointer.Append(at, "unit"));
                    if (units[i].Id is { } id)
                    {
                        if (string.IsNullOrWhiteSpace(id))
                            Error(file, Pointer.Append(at, "id"), "The id is empty.");
                        else if (!ids.Add(id))
                            Error(file, Pointer.Append(at, "id"), $"Two units in this encounter are named '{id}'.");
                        if (units[i].Count != 1)
                            Error(file, Pointer.Append(at, "id"), "A unit with an \"id\" is a single unit (\"count\" 1).");
                    }
                    Ref(RefKind.AiProfile, units[i].AiProfile, file, Pointer.Append(at, "aiProfile"));
                    if ((units[i].At is null) == (units[i].Zone is null))
                        Error(file, at, "A unit is placed with either \"at\" or \"zone\".");
                    if (units[i].Count < 1)
                        Error(file, Pointer.Append(at, "count"), "The count must be at least 1.");
                    if (units[i].Level is < 1 or > 10)
                        Error(file, Pointer.Append(at, "level"), $"The level {units[i].Level} must be in 1..10.");
                }
            var triggerIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < def.Triggers.Length; i++)
            {
                var at = Pointer.Append(Pointer.Append(ptr, "triggers"), i);
                var trigger = def.Triggers[i];
                if (string.IsNullOrWhiteSpace(trigger.Id))
                    Error(file, Pointer.Append(at, "id"), "The id is empty.");
                else if (!triggerIds.Add(trigger.Id))
                    Error(file, Pointer.Append(at, "id"), $"Two triggers in this encounter are named '{trigger.Id}'.");
                TriggerShape(trigger.When, file, Pointer.Append(at, "when"));
                for (var j = 0; j < trigger.Do.Length; j++)
                    ActionShape(trigger.Do[j], file, Pointer.Append(Pointer.Append(at, "do"), j));
            }
            return def;
        });

    /// <summary>
    /// Walks the effect blocks of every trigger action (<c>triggers/i/do/j/effects</c>) as an ability's are walked, so they
    /// get the same checks and pointers. False when one couldn't be read.
    /// </summary>
    private bool TriggerEffects(JsonElement e, string file, string ptr)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("triggers", out var triggers)
            || triggers.ValueKind != JsonValueKind.Array)
            return true;
        var ok = true;
        var i = 0;
        foreach (var trigger in triggers.EnumerateArray())
        {
            var at = Pointer.Append(Pointer.Append(ptr, "triggers"), i++);
            if (trigger.ValueKind != JsonValueKind.Object || !trigger.TryGetProperty("do", out var actions)
                || actions.ValueKind != JsonValueKind.Array)
                continue;
            var j = 0;
            foreach (var action in actions.EnumerateArray())
                ok &= EffectsIn(action, "effects", file, Pointer.Append(Pointer.Append(at, "do"), j++), inSave: false);
        }
        return ok;
    }

    /// <summary>A trigger condition's own values: at least one key, a round from 1, a count from 1, a percentage in 1..100.</summary>
    private void TriggerShape(TriggerWhen when, string file, string ptr)
    {
        var keys = 0;
        for (var i = 0; i < when.All.Length; i++)
            TriggerShape(when.All[i], file, Pointer.Append(Pointer.Append(ptr, "all"), i));
        for (var i = 0; i < when.Any.Length; i++)
            TriggerShape(when.Any[i], file, Pointer.Append(Pointer.Append(ptr, "any"), i));
        keys += when.All.IsEmpty ? 0 : 1;
        keys += when.Any.IsEmpty ? 0 : 1;
        if (when.Round is { } round)
        {
            keys++;
            AtLeast(round, 1, file, Pointer.Append(ptr, "round"));
        }
        if (when.UnitEntersZone is { } enters)
        {
            keys++;
            AtLeast(enters.Count, 1, file, Pointer.Append(Pointer.Append(ptr, "unitEntersZone"), "count"));
        }
        if (when.HpBelow is { } hp)
        {
            keys++;
            if (hp.Percent is < 1 or > 100)
                Error(file, Pointer.Append(Pointer.Append(ptr, "hpBelow"), "percent"), $"The percentage {hp.Percent} must be in 1..100.");
        }
        keys += when.UnitDies is null ? 0 : 1;
        keys += when.Flag is null ? 0 : 1;
        keys += when.Counter is null ? 0 : 1;
        keys += when.Objective is null ? 0 : 1;
        if (keys == 0)
            Error(file, ptr, "A trigger condition needs at least one key.");
    }

    /// <summary>
    /// A trigger action's own values: exactly one action key; effect blocks with exactly one of <c>tag</c>, <c>zone</c> or
    /// <c>at</c> (and those only with effects); a <c>setObjective</c> that sets something; spawn counts from 1; a phase
    /// number from 1 with no <c>def</c> written (it comes from the monster).
    /// </summary>
    private void ActionShape(TriggerAction action, string file, string ptr)
    {
        var keys = action.Keys;
        if (keys.Length != 1)
        {
            Error(file, ptr, keys.Length == 0
                ? "A trigger action needs one of say, spawn, setFlag, addCounter, setObjective, cameraHint, endBattle, phase or effects."
                : $"A trigger action does one thing, but this one has {string.Join(" and ", keys.Select(k => $"'{k}'"))}.");
            return;
        }
        var selectors = (action.Tag is null ? 0 : 1) + (action.Zone is null ? 0 : 1) + (action.At is null ? 0 : 1);
        if (keys[0] == "effects")
        {
            if (selectors != 1)
                Error(file, ptr, "Trigger effects need exactly one of \"tag\", \"zone\" or \"at\".");
        }
        else if (selectors > 0 || action.Source is not null)
            Error(file, ptr, "\"tag\", \"zone\", \"at\" and \"source\" go only with \"effects\".");

        if (action.SetObjective is { Win: null, Lose: null })
            Error(file, Pointer.Append(ptr, "setObjective"), "setObjective needs \"win\" or \"lose\".");
        if (action.Spawn is { } spawn)
        {
            AtLeast(spawn.Count, 1, file, Pointer.Append(Pointer.Append(ptr, "spawn"), "count"));
            if (spawn.Level is < 1 or > 10)
                Error(file, Pointer.Append(Pointer.Append(ptr, "spawn"), "level"), $"The level {spawn.Level} must be in 1..10.");
        }
        if (action.SetFlag is { } flag && string.IsNullOrWhiteSpace(flag))
            Error(file, Pointer.Append(ptr, "setFlag"), "The flag name is empty.");
        if (action.Phase is { } phase)
        {
            AtLeast(phase.Number, 1, file, Pointer.Append(Pointer.Append(ptr, "phase"), "number"));
            if (phase.Def is not null)
                Error(file, Pointer.Append(Pointer.Append(ptr, "phase"), "def"),
                    "A phase comes from the monster's \"phases\"; write only its \"tag\" and \"number\".");
        }
    }

    private static bool HasOnce(JsonElement encounter, int trigger)
        => encounter.TryGetProperty("triggers", out var triggers) && triggers.ValueKind == JsonValueKind.Array
           && trigger < triggers.GetArrayLength() && triggers[trigger].ValueKind == JsonValueKind.Object
           && triggers[trigger].TryGetProperty("once", out _);

    /// <summary>
    /// Checks each encounter against its map: the zones and tiles its units, objectives and triggers use, its
    /// <c>party_spawn</c> zone, and that every unit tag its objectives and triggers name belongs to one of its units
    /// (placed or spawned) or is <c>party</c> or <c>enemy</c>. Its triggers' references must resolve too: every flag
    /// read is set by some trigger, every counter read is added to by one, every objective leaf watched appears in the
    /// encounter's objectives (as written, or as a trigger sets them), every spawned monster and AI profile exists, and
    /// every phase is one the tagged monsters have. An encounter whose map is missing or broken skips the map checks.
    /// </summary>
    public void CheckEncounters(IEnumerable<Loaded<EncounterDef>> encounters, IReadOnlyDictionary<string, BattleMap> maps,
        IReadOnlyDictionary<string, MonsterDef> monsters)
    {
        foreach (var (def, _, _, file, ptr) in encounters)
        {
            try
            {
                new EncounterCheck(this, def, maps.GetValueOrDefault(def.Map), monsters, file, ptr).Run();
            }
            catch (Exception ex) when (NonFatal(ex))
            {
                Error(file, ptr, $"This couldn't be checked: {ex.Message}");
            }
        }
    }

    // ---- Encounters -------------------------------------------------------------------------------------------------

    /// <summary>The map-dependent checks of one encounter; see <see cref="CheckEncounters"/>.</summary>
    private sealed class EncounterCheck(Validation v, EncounterDef def, BattleMap? map,
        IReadOnlyDictionary<string, MonsterDef> monsters, string file, string ptr)
    {
        private readonly HashSet<string> _tags = new(StringComparer.Ordinal) { Objectives.PartyTag, Objectives.EnemyTag };
        private readonly Dictionary<string, HashSet<string>> _monstersByTag = new(StringComparer.Ordinal);
        private readonly HashSet<string> _flagsSet = new(StringComparer.Ordinal);
        private readonly HashSet<string> _countersAdded = new(StringComparer.Ordinal);
        private readonly HashSet<string> _leaves = new(StringComparer.Ordinal);

        public void Run()
        {
            foreach (var unit in def.Enemies.Concat(def.Npcs))
                Tagged(unit.UnitTag, unit.Unit);
            foreach (var trigger in def.Triggers)
                foreach (var action in trigger.Do)
                {
                    if (action.Spawn is { } spawn)
                        Tagged(spawn.UnitTag, spawn.Unit);
                    if (action.SetFlag is { } flag)
                        _flagsSet.Add(flag);
                    if (action.AddCounter is { } counter)
                        _countersAdded.Add(counter.Name);
                    if (action.SetObjective is { } patch)
                    {
                        Leaves(patch.Win);
                        Leaves(patch.Lose);
                    }
                }
            Leaves(def.Objectives.Win);
            Leaves(def.Objectives.Lose);

            foreach (var (units, name) in new[] { (def.Enemies, "enemies"), (def.Npcs, "npcs") })
                for (var i = 0; i < units.Length; i++)
                {
                    var at = Pointer.Append(Pointer.Append(ptr, name), i);
                    if (units[i].Zone is { } zone)
                        Zone(zone, Pointer.Append(at, "zone"));
                    if (units[i].At is { } tile)
                        Tile(tile, Pointer.Append(at, "at"));
                }
            if (map is not null)
            {
                if (!map.Zones.TryGetValue("party_spawn", out var spawn))
                    v.Error(file, Pointer.Append(ptr, "map"), $"The map '{map.Id}' has no \"party_spawn\" zone.");
                else if (spawn.Length < def.Party.Max)
                    v.Error(file, Pointer.Append(Pointer.Append(ptr, "party"), "max"),
                        $"The map '{map.Id}' has {spawn.Length} party_spawn tiles, fewer than the party's {def.Party.Max}.");
            }

            var objectives = Pointer.Append(ptr, "objectives");
            Objective(def.Objectives.Win, Pointer.Append(objectives, "win"));
            Objective(def.Objectives.Lose, Pointer.Append(objectives, "lose"));

            for (var i = 0; i < def.Triggers.Length; i++)
            {
                var at = Pointer.Append(Pointer.Append(ptr, "triggers"), i);
                Condition(def.Triggers[i].When, Pointer.Append(at, "when"));
                for (var j = 0; j < def.Triggers[i].Do.Length; j++)
                    Action(def.Triggers[i].Do[j], Pointer.Append(Pointer.Append(at, "do"), j), def.Triggers[i].When, Pointer.Append(at, "when"));
            }
        }

        private void Tagged(string tag, string monster)
        {
            _tags.Add(tag);
            if (!_monstersByTag.TryGetValue(tag, out var set))
                _monstersByTag[tag] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(monster);
        }

        private void Leaves(ObjectiveExpr? expr)
        {
            if (expr is null)
                return;
            if (expr.Leaf is { } leaf)
                _leaves.Add(leaf);
            foreach (var e in expr.Any.IsDefault ? [] : expr.Any)
                Leaves(e);
            foreach (var e in expr.All.IsDefault ? [] : expr.All)
                Leaves(e);
        }

        private void Objective(ObjectiveExpr expr, string at)
        {
            for (var i = 0; i < expr.Any.Length; i++)
                Objective(expr.Any[i], Pointer.Append(Pointer.Append(at, "any"), i));
            for (var i = 0; i < expr.All.Length; i++)
                Objective(expr.All[i], Pointer.Append(Pointer.Append(at, "all"), i));
            if (expr.Leaf is { } leaf)
                ObjectiveLeaf(leaf, at);
        }

        private void ObjectiveLeaf(string leaf, string at)
        {
            var parts = leaf.Split(':');
            switch (parts)
            {
                case [Objectives.DefeatAll] or [Objectives.PartyOut]:
                case ["survive", var rounds] when int.TryParse(rounds, NumberStyles.None, CultureInfo.InvariantCulture, out var r) && r >= 1:
                    break;
                case ["flag", { Length: > 0 } flag]:
                    if (!_flagsSet.Contains(flag))
                        v.Error(file, at, $"No trigger in this encounter sets the flag '{flag}'.");
                    break;
                case ["defeat" or "protect", { Length: > 0 } tag]:
                    UnitTag(tag, at);
                    break;
                case ["reach", { Length: > 0 } zone, var count] when int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var c) && c >= 1:
                    Zone(zone, at);
                    break;
                default:
                    v.Error(file, at, $"'{leaf}' isn't an objective (expected defeat_all, party_out, defeat:<tag>, survive:<rounds>, "
                                      + "reach:<zone>:<count>, protect:<tag> or flag:<name>).");
                    break;
            }
        }

        /// <summary>A trigger's <c>when</c> (spec §4.8 conditions, combined with <c>all</c>/<c>any</c>).</summary>
        private void Condition(TriggerWhen when, string at)
        {
            for (var i = 0; i < when.All.Length; i++)
                Condition(when.All[i], Pointer.Append(Pointer.Append(at, "all"), i));
            for (var i = 0; i < when.Any.Length; i++)
                Condition(when.Any[i], Pointer.Append(Pointer.Append(at, "any"), i));
            if (when.UnitEntersZone is { } enters)
            {
                var here = Pointer.Append(at, "unitEntersZone");
                UnitTag(enters.Tag, Pointer.Append(here, "tag"));
                Zone(enters.Zone, Pointer.Append(here, "zone"));
            }
            if (when.HpBelow is { } hp)
                UnitTag(hp.Tag, Pointer.Append(Pointer.Append(at, "hpBelow"), "tag"));
            if (when.UnitDies is { } dies)
                UnitTag(dies.Tag, Pointer.Append(Pointer.Append(at, "unitDies"), "tag"));
            if (when.Flag is { } flag && !_flagsSet.Contains(flag))
                v.Error(file, Pointer.Append(at, "flag"), $"No trigger in this encounter sets the flag '{flag}'.");
            if (when.Counter is { } counter && !_countersAdded.Contains(counter.Name))
                v.Error(file, Pointer.Append(Pointer.Append(at, "counter"), "name"),
                    $"No trigger in this encounter adds to the counter '{counter.Name}'.");
            if (when.Objective is { } objective)
            {
                var here = Pointer.Append(Pointer.Append(at, "objective"), "name");
                if (!_leaves.Contains(objective.Name))
                    v.Error(file, here, $"The objective '{objective.Name}' isn't one of this encounter's objectives.");
                else
                    ObjectiveLeaf(objective.Name, here);
            }
        }

        /// <summary>A trigger action's references (spec §4.8).</summary>
        private void Action(TriggerAction action, string at, TriggerWhen when, string whenAt)
        {
            if (action.Spawn is { } spawn)
            {
                var here = Pointer.Append(at, "spawn");
                v.Ref(RefKind.Monster, spawn.Unit, file, Pointer.Append(here, "unit"));
                v.Ref(RefKind.AiProfile, spawn.AiProfile, file, Pointer.Append(here, "aiProfile"));
                Place(spawn.At, Pointer.Append(here, "at"));
            }
            if (action.CameraHint is { } hint)
                Place(hint, Pointer.Append(at, "cameraHint"));
            if (action.SetObjective is { } patch)
            {
                var here = Pointer.Append(at, "setObjective");
                if (patch.Win is { } win)
                    Objective(win, Pointer.Append(here, "win"));
                if (patch.Lose is { } lose)
                    Objective(lose, Pointer.Append(here, "lose"));
            }
            if (action.Phase is { } phase)
                Phase(phase, Pointer.Append(at, "phase"), when, whenAt);
            if (action.Tag is { } tag)
                UnitTag(tag, Pointer.Append(at, "tag"));
            if (action.Source is { } source)
                UnitTag(source, Pointer.Append(at, "source"));
            if (action.Zone is { } zone)
                Zone(zone, Pointer.Append(at, "zone"));
            if (action.At is { } tile)
                Tile(tile, Pointer.Append(at, "at"));
        }

        /// <summary>
        /// The phase's tag names one monster, placed or spawned, that has a phase of that number; a trigger watching
        /// <c>hpBelow</c> on the same tag uses that phase's percentage.
        /// </summary>
        private void Phase(PhaseAction phase, string at, TriggerWhen when, string whenAt)
        {
            var tagAt = Pointer.Append(at, "tag");
            if (!_monstersByTag.TryGetValue(phase.Tag, out var tagged))
            {
                if (_tags.Contains(phase.Tag))
                    v.Error(file, tagAt, $"A phase needs the tag of a placed or spawned monster, not '{phase.Tag}'.");
                else
                    UnitTag(phase.Tag, tagAt);
                return;
            }
            if (tagged.Count > 1)
            {
                var names = string.Join(" and ", tagged.Order(StringComparer.Ordinal).Select(m => $"'{m}'"));
                v.Error(file, tagAt, $"The tag '{phase.Tag}' covers the monsters {names}, but a phase comes from one monster.");
                return;
            }
            var monster = tagged.Single();
            if (!monsters.TryGetValue(monster, out var def))
                return;
            if (def.Phases.Length < phase.Number)
                v.Error(file, Pointer.Append(at, "number"),
                    $"The monster '{monster}' has {def.Phases.Length} phases, so it has no phase {phase.Number}.");
            else if (phase.Number >= 1 && when.HpBelow is { } hp && hp.Tag == phase.Tag && hp.Percent != def.Phases[phase.Number - 1].HpBelow)
                v.Error(file, Pointer.Append(Pointer.Append(whenAt, "hpBelow"), "percent"),
                    $"This trigger watches {hp.Percent}%, but phase {phase.Number} of '{monster}' is at {def.Phases[phase.Number - 1].HpBelow}%.");
        }

        private void Place(PlaceRef place, string at)
        {
            if (place.Zone is { } zone)
                Zone(zone, at);
            else if (place.Tile is { } tile)
                Tile(tile, at);
        }

        private void UnitTag(string tag, string at)
        {
            if (!_tags.Contains(tag))
                v.Error(file, at, $"No unit in this encounter has the tag '{tag}'.");
        }

        private void Zone(string zone, string at)
        {
            if (map is not null && !map.Zones.ContainsKey(zone))
                v.Error(file, at, $"The map '{map.Id}' has no zone '{zone}'.");
        }

        private void Tile(Tile tile, string at)
        {
            if (map is not null && !map.InBounds(tile))
                v.Error(file, at, $"Tile {tile} is outside the {map.Width}x{map.Height} map.");
        }
    }
}
