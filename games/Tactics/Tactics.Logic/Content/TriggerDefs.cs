using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// An encounter trigger (spec §4.8): when <see cref="When"/> holds at a checkpoint (see <see cref="Triggers"/>), its
/// <see cref="Do"/> actions run, in order, as steps at the front of the queue. A <see cref="Once"/> trigger (the default:
/// the loader sets it when the content leaves it out) fires at most once per battle; any other fires each time its
/// condition becomes true again after having stopped holding.
/// </summary>
public sealed record TriggerDef(string Id, TriggerWhen When, bool Once, ImmutableArray<TriggerAction> Do);

/// <summary>
/// A trigger's condition (spec §4.8). Every key present must hold; <see cref="All"/> needs every entry and
/// <see cref="Any"/> at least one. Unit tags are sheet tags (an encounter places its tag on each unit), plus two that need
/// no placing: <c>"party"</c> (every party member that isn't a summon or a protected NPC) and <c>"enemy"</c> (every
/// enemy that isn't a summon).
/// <list type="bullet">
/// <item><see cref="Round"/>: the current round is this one.</item>
/// <item><see cref="UnitEntersZone"/>: at least <see cref="ZoneWatch.Count"/> standing units with the tag are on the
/// map zone's tiles.</item>
/// <item><see cref="HpBelow"/>: a unit with the tag, still in the battle, is below this percentage of its max HP.</item>
/// <item><see cref="UnitDies"/>: a unit with the tag is Dead or Out.</item>
/// <item><see cref="Flag"/>: the story flag is set. <see cref="Counter"/>: the counter is at least this much.</item>
/// <item><see cref="Objective"/>: the objective leaf (e.g. <c>"reach:exit:2"</c>) is met, or unmet.</item>
/// </list>
/// </summary>
public sealed record TriggerWhen(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<TriggerWhen> All = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<TriggerWhen> Any = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Round = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ZoneWatch? UnitEntersZone = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HpWatch? HpBelow = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TagRef? UnitDies = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Flag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CounterWatch? Counter = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ObjectiveWatch? Objective = null);

/// <summary><c>unitEntersZone</c>: <see cref="Count"/> (at least 1) standing units with <see cref="Tag"/> on <see cref="Zone"/>.</summary>
public sealed record ZoneWatch(string Tag, string Zone, int Count = 1);

/// <summary><c>hpBelow</c>: a unit with <see cref="Tag"/> below <see cref="Percent"/>% of its max HP.</summary>
public sealed record HpWatch(string Tag, int Percent);

/// <summary>A unit tag, as in <c>{"unitDies": {"tag": "warlord"}}</c>.</summary>
public sealed record TagRef(string Tag);

/// <summary><c>counter</c>: the counter <see cref="Name"/> is at least <see cref="AtLeast"/>.</summary>
public sealed record CounterWatch(string Name, int AtLeast);

/// <summary>Whether an objective leaf is met. JSON: <c>"met"</c> or <c>"unmet"</c>.</summary>
public enum ObjectiveStatus
{
    /// <summary>The leaf holds.</summary>
    [JsonStringEnumMemberName("met")] Met,
    /// <summary>The leaf doesn't hold.</summary>
    [JsonStringEnumMemberName("unmet")] Unmet,
}

/// <summary><c>objective</c>: the objective leaf <see cref="Name"/> (e.g. <c>"survive:8"</c>) is in <see cref="State"/>.</summary>
public sealed record ObjectiveWatch(string Name, ObjectiveStatus State = ObjectiveStatus.Met);

/// <summary>
/// One trigger action (spec §4.8). Exactly one of these keys is set:
/// <list type="bullet">
/// <item><see cref="Say"/>: a dialogue line on the TV (<see cref="Said"/>).</item>
/// <item><see cref="Spawn"/>: monsters join the battle (<see cref="SpawnAction"/>).</item>
/// <item><see cref="SetFlag"/>: sets a story flag. <see cref="AddCounter"/>: adds to a named counter.</item>
/// <item><see cref="SetObjective"/>: replaces the win and/or lose expression (<see cref="ObjectiveChanged"/>).</item>
/// <item><see cref="CameraHint"/>: asks the TV to focus a place (<see cref="Tactics.Logic.CameraHint"/>).</item>
/// <item><see cref="EndBattle"/>: ends the battle with an outcome.</item>
/// <item><see cref="Phase"/>: a boss enters one of its phases (<see cref="PhaseAction"/>).</item>
/// <item><see cref="Effects"/>: effect blocks applied to the units with <see cref="Tag"/>, or to the map zone
/// <see cref="Zone"/>, or to the tile <see cref="At"/> (see <see cref="Triggers"/>), from the first unit in the battle with the
/// tag <see cref="Source"/> when it is given.</item>
/// </list>
/// </summary>
public sealed record TriggerAction(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SayAction? Say = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SpawnAction? Spawn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SetFlag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CounterAdd? AddCounter = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ObjectivesPatch? SetObjective = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlaceRef? CameraHint = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BattleOutcome? EndBattle = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PhaseAction? Phase = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<EffectDef> Effects = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Zone = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Tile? At = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null)
{
    /// <summary>The action keys that are set (a valid action has exactly one).</summary>
    [JsonIgnore]
    public ImmutableArray<string> Keys
    {
        get
        {
            var keys = ImmutableArray.CreateBuilder<string>();
            if (Say is not null) keys.Add("say");
            if (Spawn is not null) keys.Add("spawn");
            if (SetFlag is not null) keys.Add("setFlag");
            if (AddCounter is not null) keys.Add("addCounter");
            if (SetObjective is not null) keys.Add("setObjective");
            if (CameraHint is not null) keys.Add("cameraHint");
            if (EndBattle is not null) keys.Add("endBattle");
            if (Phase is not null) keys.Add("phase");
            if (!Effects.IsDefaultOrEmpty) keys.Add("effects");
            return keys.ToImmutable();
        }
    }
}

/// <summary><c>say</c>: <see cref="Speaker"/> (a unit tag, a name, or <c>"narrator"</c>) says <see cref="Line"/>.</summary>
public sealed record SayAction(string Speaker, string Line);

/// <summary>
/// <c>spawn</c>: <see cref="Count"/> of the monster <see cref="Unit"/> join the enemy side (or, with <see cref="Ally"/>, the
/// party's side as AI-controlled <see cref="UnitState.Helper"/>s) on the free tiles of <see cref="At"/> (a map zone, in its order, or a tile), at
/// <see cref="Level"/> (the monster's own when left out), tagged <see cref="Tag"/> (the monster id when left out), with an
/// optional AI profile override. They take their turns from the next round.
/// </summary>
public sealed record SpawnAction(string Unit, PlaceRef At, int Count = 1,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Level = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AiProfile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Ally = false)
{
    /// <summary>The tag the spawned units carry: <see cref="Tag"/>, or the monster id.</summary>
    [JsonIgnore]
    public string UnitTag => Tag ?? Unit;
}

/// <summary><c>addCounter</c>: adds <see cref="Amount"/> (1 when left out) to the counter <see cref="Name"/>.</summary>
public sealed record CounterAdd(string Name, int Amount = 1);

/// <summary><c>setObjective</c>: the new win and/or lose expression; one left out stays as it is.</summary>
public sealed record ObjectivesPatch(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ObjectiveExpr? Win = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ObjectiveExpr? Lose = null);

/// <summary>
/// <c>phase</c>: every unit with <see cref="Tag"/> still in the battle enters phase <see cref="Number"/> (from 1) of its
/// monster's <c>phases</c>: it gains the phase's abilities and passives and, if the phase names one, its AI profile.
/// Content writes only the tag and number; <see cref="Encounters.Setup"/> fills <see cref="Def"/> from the monster, and the
/// engine runs from <see cref="Def"/> (nothing happens without one).
/// </summary>
public sealed record PhaseAction(string Tag, int Number = 1,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PhaseDef? Def = null);

/// <summary>
/// A place on the map, written as a map zone's name (<c>"ridge_spawn"</c>) or a tile (<c>[3, 4]</c>).
/// </summary>
[JsonConverter(typeof(PlaceRefJsonConverter))]
public sealed record PlaceRef(string? Zone, Tile? Tile)
{
    /// <summary>The place's tiles: the zone's in its order (none if the map lacks it), or the tile.</summary>
    public ImmutableArray<Tile> Tiles(BattleMap map)
        => Zone is { } zone ? map.Zones.TryGetValue(zone, out var tiles) ? tiles : []
            : Tile is { } tile ? [tile] : [];
}

/// <summary>Reads and writes a <see cref="PlaceRef"/> as a zone name or an <c>[x, y]</c> tile.</summary>
public sealed class PlaceRefJsonConverter : JsonConverter<PlaceRef>
{
    /// <inheritdoc />
    public override PlaceRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var zone = reader.GetString();
            if (string.IsNullOrWhiteSpace(zone))
                throw new JsonException("A place is a zone name or an [x, y] tile.");
            return new PlaceRef(zone, null);
        }
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            try
            {
                return new PlaceRef(null, new TileJsonConverter().Read(ref reader, typeof(Tile), options));
            }
            catch (InvalidOperationException)
            {
                throw new JsonException("A place is a zone name or an [x, y] tile.");
            }
        }
        throw new JsonException("A place is a zone name or an [x, y] tile.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, PlaceRef value, JsonSerializerOptions options)
    {
        if (value.Zone is { } zone)
            writer.WriteStringValue(zone);
        else if (value.Tile is { } tile)
            new TileJsonConverter().Write(writer, tile, options);
        else
            writer.WriteNullValue();
    }
}
