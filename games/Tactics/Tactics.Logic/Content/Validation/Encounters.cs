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
                }
            for (var i = 0; i < def.Triggers.Length; i++)
                if (string.IsNullOrWhiteSpace(def.Triggers[i].Id))
                    Error(file, Pointer.Append(Pointer.Append(Pointer.Append(ptr, "triggers"), i), "id"), "The id is empty.");
            return def;
        });

    private static bool HasOnce(JsonElement encounter, int trigger)
        => encounter.TryGetProperty("triggers", out var triggers) && triggers.ValueKind == JsonValueKind.Array
           && trigger < triggers.GetArrayLength() && triggers[trigger].ValueKind == JsonValueKind.Object
           && triggers[trigger].TryGetProperty("once", out _);

    /// <summary>
    /// Checks each encounter against its map: the zones and tiles its units, objectives and triggers use, its
    /// <c>party_spawn</c> zone, and that every unit tag its objectives and triggers name belongs to one of its units
    /// (placed or spawned). An encounter whose map is missing or broken skips the map checks.
    /// </summary>
    public void CheckEncounters(IEnumerable<Loaded<EncounterDef>> encounters, IReadOnlyDictionary<string, BattleMap> maps)
    {
        foreach (var (def, _, _, file, ptr) in encounters)
        {
            try
            {
                new EncounterCheck(this, def, maps.GetValueOrDefault(def.Map), file, ptr).Run();
            }
            catch (Exception ex) when (NonFatal(ex))
            {
                Error(file, ptr, $"This couldn't be checked: {ex.Message}");
            }
        }
    }

    // ---- Encounters -------------------------------------------------------------------------------------------------

    /// <summary>The map-dependent checks of one encounter; see <see cref="CheckEncounters"/>.</summary>
    private sealed class EncounterCheck(Validation v, EncounterDef def, BattleMap? map, string file, string ptr)
    {
        private readonly HashSet<string> _tags = new(StringComparer.Ordinal);

        public void Run()
        {
            foreach (var unit in def.Enemies.Concat(def.Npcs))
                _tags.Add(unit.UnitTag);
            foreach (var trigger in def.Triggers)
                foreach (var spawn in Actions(trigger.Do, "spawn"))
                    if (spawn.ValueKind == JsonValueKind.Object && Text(spawn, "unit") is { } unit)
                        _tags.Add(Text(spawn, "tag") ?? unit);

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
                Do(def.Triggers[i].Do, Pointer.Append(at, "do"));
            }
        }

        private void Objective(ObjectiveExpr expr, string at)
        {
            for (var i = 0; i < expr.Any.Length; i++)
                Objective(expr.Any[i], Pointer.Append(Pointer.Append(at, "any"), i));
            for (var i = 0; i < expr.All.Length; i++)
                Objective(expr.All[i], Pointer.Append(Pointer.Append(at, "all"), i));
            if (expr.Leaf is not { } leaf)
                return;
            var parts = leaf.Split(':');
            switch (parts)
            {
                case [Objectives.DefeatAll] or [Objectives.PartyOut]:
                case ["survive", var rounds] when int.TryParse(rounds, NumberStyles.None, CultureInfo.InvariantCulture, out var r) && r >= 1:
                case ["flag", { Length: > 0 }]:
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
        private void Condition(JsonElement when, string at)
        {
            if (when.ValueKind != JsonValueKind.Object)
            {
                v.Error(file, at, "A trigger condition must be an object.");
                return;
            }
            foreach (var p in when.EnumerateObject())
            {
                var here = Pointer.Append(at, p.Name);
                switch (p.Name)
                {
                    case "all" or "any" when p.Value.ValueKind == JsonValueKind.Array:
                        var i = 0;
                        foreach (var item in p.Value.EnumerateArray())
                            Condition(item, Pointer.Append(here, i++));
                        break;
                    case "unitEntersZone":
                        TagIn(p.Value, here);
                        if (Text(p.Value, "zone") is { } zone)
                            Zone(zone, Pointer.Append(here, "zone"));
                        break;
                    case "hpBelow" or "unitDies":
                        TagIn(p.Value, here);
                        break;
                    case "round" or "flag" or "counter" or "objective":
                        break;
                    default:
                        v.Error(file, here, $"Unknown trigger condition '{p.Name}'.");
                        break;
                }
            }
        }

        /// <summary>A trigger's <c>do</c> list (spec §4.8 actions, or any effect block).</summary>
        private void Do(JsonElement list, string at)
        {
            if (list.ValueKind != JsonValueKind.Array)
            {
                v.Error(file, at, "A trigger's \"do\" must be a list of actions.");
                return;
            }
            var i = 0;
            foreach (var action in list.EnumerateArray())
            {
                var here = Pointer.Append(at, i++);
                if (action.ValueKind != JsonValueKind.Object)
                {
                    v.Error(file, here, "A trigger action must be an object.");
                    continue;
                }
                foreach (var p in action.EnumerateObject())
                {
                    var inner = Pointer.Append(here, p.Name);
                    switch (p.Name)
                    {
                        case "spawn":
                            if (Text(p.Value, "unit") is { } unit)
                                v.Ref(RefKind.Monster, unit, file, Pointer.Append(inner, "unit"));
                            if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("at", out var spawnAt))
                                Place(spawnAt, Pointer.Append(inner, "at"));
                            break;
                        case "cameraHint":
                            if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("at", out var hintAt))
                                Place(hintAt, Pointer.Append(inner, "at"));
                            break;
                        case "setObjective":
                            foreach (var side in new[] { "win", "lose" })
                                if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty(side, out var expr))
                                    RawObjective(expr, Pointer.Append(inner, side));
                            break;
                        case "say" or "setFlag" or "addCounter" or "endBattle":
                            break;
                        default:
                            if (BlockRegistry.TryFor(p.Name) is null)
                                v.Error(file, inner, $"Unknown trigger action '{p.Name}'.");
                            break;
                    }
                }
            }
        }

        private void RawObjective(JsonElement expr, string at)
        {
            if (v.Deserialize<ObjectiveExpr>(expr, file, at) is { } objective)
                Objective(objective, at);
        }

        /// <summary>A place given as a zone name or an <c>[x, y]</c> tile.</summary>
        private void Place(JsonElement place, string at)
        {
            if (place.ValueKind == JsonValueKind.String)
                Zone(place.GetString()!, at);
            else if (place is { ValueKind: JsonValueKind.Array } && place.GetArrayLength() == 2
                     && place[0].TryGetInt32(out var x) && place[1].TryGetInt32(out var y))
                Tile(new Tile(x, y), at);
            else
                v.Error(file, at, "A place is a zone name or an [x, y] tile.");
        }

        private void TagIn(JsonElement e, string at)
        {
            if (Text(e, "tag") is { } tag)
                UnitTag(tag, Pointer.Append(at, "tag"));
            else
                v.Error(file, at, "This needs a unit \"tag\".");
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

        private static IEnumerable<JsonElement> Actions(JsonElement list, string name)
            => list.ValueKind != JsonValueKind.Array ? []
                : list.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out _))
                    .Select(a => a.GetProperty(name));

        private static string? Text(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
