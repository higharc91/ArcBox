using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// One effect block's data, as written in content: <c>{"&lt;key&gt;": {...}}</c> with any branches beside the key (see
/// <see cref="EffectJsonConverter"/>). <see cref="BlockRegistry"/> maps each record type to the block that runs it.
/// </summary>
[JsonConverter(typeof(EffectJsonConverter))]
public abstract record EffectDef
{
    /// <summary>
    /// True for a block that acts on the aimed tile rather than on a unit (a zone, terrain, a summon, the user's own
    /// movement): at the top level of an ability's effects, a run of consecutive tile blocks runs once, at the aim, with no
    /// target unit, even when the area holds no units, and a run of other blocks once for each unit the ability affects,
    /// the runs in authored order (<see cref="Battle"/>). A tile block nested in another block (a branch of <c>attack</c>,
    /// <c>save</c>, <c>if</c>, <c>random</c>, <c>repeat</c>, …) runs wherever that block runs: once per affected unit.
    /// </summary>
    public virtual bool OnTile => false;
}

/// <summary>
/// <c>"attack"</c>: an attack roll against the target's AC. <see cref="Kind"/> is <c>"weapon"</c> (the sheet's weapon stat,
/// the better of STR and DEX for a finesse weapon) or <c>"spell"</c> (the caster's <see cref="UnitSheet.CastingStat"/>);
/// <see cref="Stat"/> overrides either. A hit runs <see cref="OnHit"/>, a critical hit also <see cref="OnCrit"/>, and a miss
/// <see cref="OnMiss"/>.
/// </summary>
public sealed record AttackEffect(string Kind, Stat? Stat, ImmutableArray<EffectDef> OnHit,
    ImmutableArray<EffectDef> OnMiss, ImmutableArray<EffectDef> OnCrit, int Bonus = 0) : EffectDef;

/// <summary>
/// <c>"save"</c>: the target rolls a saving throw against <see cref="Dc"/>, or 8 + proficiency + the modifier of
/// <see cref="DcStat"/> (a shove's STR, a dragonborn's breath's CON), or of the caster's casting stat. A failure runs <see cref="OnFail"/>; a success runs <see cref="OnSuccess"/>, after the damage blocks of
/// <see cref="OnFail"/> at half (rounded down) when <see cref="HalfOnSuccess"/> is set.
/// </summary>
public sealed record SaveEffect(Stat Stat, int? Dc, ImmutableArray<EffectDef> OnFail,
    ImmutableArray<EffectDef> OnSuccess, bool HalfOnSuccess = false, Stat? DcStat = null) : EffectDef;

/// <summary>
/// <c>"damage"</c>: <see cref="Dice"/> at the user's level plus the named <see cref="Bonus"/> addends, of
/// <see cref="Type"/>. With <see cref="Weapon"/>, the sheet's weapon dice and attack stat are added, and the type defaults
/// to the weapon's. A critical hit rolls the dice twice.
/// </summary>
public sealed record DamageEffect(ByLevel<Dice>? Dice, string? Type, ImmutableArray<string> Bonus,
    bool Weapon = false) : EffectDef;

/// <summary><c>"heal"</c>: restores <see cref="Dice"/> at the user's level plus the named <see cref="Bonus"/> addends, up to max HP.</summary>
public sealed record HealEffect(ByLevel<Dice> Dice, ImmutableArray<string> Bonus) : EffectDef;

/// <summary>
/// <c>"apply"</c>: gives the target a condition for <see cref="Rounds"/> rounds (null: until removed), or until it saves
/// at the end of its turn (<see cref="UntilSave"/>, which needs an enclosing <c>"save"</c> for its stat and DC).
/// JSON shorthand: <c>{"apply": "lit", "rounds": 1}</c>.
/// </summary>
[EffectShorthand(nameof(Condition))]
public sealed record ApplyEffect(ConditionId Condition, int? Rounds, bool UntilSave = false) : EffectDef;

/// <summary><c>"remove"</c>: ends every instance of a condition on the target. JSON shorthand: <c>{"remove": "prone"}</c>.</summary>
[EffectShorthand(nameof(Condition))]
public sealed record RemoveEffect(ConditionId Condition) : EffectDef;

/// <summary>
/// <c>"push"</c>: moves the target <see cref="Distance"/> tiles, one at a time, straight away from the source (or, without
/// <see cref="AwayFromSource"/>, away from the aimed tile) along the dominant axis. See <see cref="Motion.Push"/>.
/// </summary>
public sealed record PushEffect(int Distance, bool AwayFromSource = true) : EffectDef;

/// <summary><c>"pull"</c>: moves the target up to <see cref="Distance"/> tiles toward the source, stopping beside it.</summary>
public sealed record PullEffect(int Distance) : EffectDef;

/// <summary>
/// <c>"teleport"</c>: moves the source (or, without <see cref="Self"/>, the target) to the aimed tile, which must be
/// walkable, empty and within <see cref="Range"/> tiles of the unit; otherwise nothing happens.
/// </summary>
public sealed record TeleportEffect(int Range, bool Self = true) : EffectDef
{
    /// <inheritdoc />
    public override bool OnTile => Self;
}

/// <summary><c>"swap"</c>: the source and the target exchange tiles.</summary>
public sealed record SwapEffect : EffectDef;

/// <summary>
/// <c>"dash"</c>: the source walks up to <see cref="Tiles"/> tiles toward the aimed tile, stopping before anything it can't
/// step onto or any unit. It spends no Move and provokes nothing.
/// </summary>
public sealed record DashEffect(int Tiles) : EffectDef
{
    /// <inheritdoc />
    public override bool OnTile => true;
}

/// <summary>
/// <c>"jumpTo"</c>: the source leaps to the aimed tile, ignoring its Jump limit, if the tile is walkable, empty and within
/// <see cref="Range"/> tiles; otherwise nothing happens.
/// </summary>
public sealed record JumpToEffect(int Range) : EffectDef
{
    /// <inheritdoc />
    public override bool OnTile => true;
}

/// <summary>
/// <c>"help"</c>: the Help action on the target ally. A Downed ally gets up at 1 HP; a standing one gets
/// <see cref="RulesPack.Helped"/> (see <see cref="HelpBlock"/>).
/// </summary>
public sealed record HelpEffect : EffectDef;

// ---- HP and conditions (Task 10) ------------------------------------------------------------------------------------

/// <summary>
/// <c>"tempHp"</c>: the target gains temporary HP: <see cref="Dice"/> at the user's level plus the named
/// <see cref="Bonus"/> addends. Temporary HP don't stack: the target keeps the higher of what it has and the new amount.
/// </summary>
public sealed record TempHpEffect(ByLevel<Dice> Dice, ImmutableArray<string> Bonus) : EffectDef;

/// <summary><c>"revive"</c>: a Downed target gets up with <see cref="Hp"/> HP (see <see cref="DownedRules.Revive"/>).</summary>
public sealed record ReviveEffect(int Hp) : EffectDef;

/// <summary><c>"cleanse"</c>: ends every condition on the target whose definition has any of <see cref="Tags"/>.</summary>
public sealed record CleanseEffect([property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Tags) : EffectDef;

/// <summary>
/// <c>"dispel"</c>: with <see cref="Concentration"/>, ends the concentration of the target (or, on a tile run, of the
/// unit on the aimed tile); with <see cref="Zones"/>, ends every zone covering the aimed tile.
/// </summary>
public sealed record DispelEffect(bool Concentration = false, bool Zones = false) : EffectDef
{
    /// <inheritdoc />
    public override bool OnTile => true;
}

// ---- Map ------------------------------------------------------------------------------------------------------------

/// <summary>
/// <c>"zone"</c>: a lasting area (<see cref="Zone"/>) of <see cref="Kind"/> on the tiles of <see cref="Area"/> around the
/// aim, owned by the user, for <see cref="Rounds"/> rounds of its owner's turns (null: the whole battle), tied to the
/// user's concentration with <see cref="Concentration"/>. A <see cref="Difficult"/> zone costs 2 Move per tile; a
/// <see cref="BlocksSight"/> one blocks line of sight (smoke, darkness). Its
/// <see cref="Passives"/> belong to every unit standing in it (usually <c>onEnterTile</c> and <c>onTurnStart</c> with
/// <c>"target": "self"</c>), their effects coming from the zone's owner.
/// </summary>
public sealed record ZoneEffect(string Kind, AreaDef Area, int? Rounds, ImmutableArray<PassiveDef> Passives,
    bool Concentration = false, bool Difficult = false, bool BlocksSight = false) : EffectDef
{
    /// <inheritdoc />
    public override bool OnTile => true;
}

/// <summary>
/// <c>"alterTerrain"</c>: raises (or, with a negative <see cref="Delta"/>, sinks) the tiles of <see cref="Area"/> around
/// the aim by that many levels, each kept within heights 0–9. Units on them stay on top.
/// </summary>
public sealed record AlterTerrainEffect(AreaDef Area, int Delta) : EffectDef
{
    /// <inheritdoc />
    public override bool OnTile => true;
}

/// <summary>
/// <c>"hazard"</c>: a hazard from the hazards art set (<see cref="HazardArt"/>, an art key) on the tiles of
/// <see cref="Area"/> for <see cref="Rounds"/> rounds: a zone of that kind whose <see cref="OnEnter"/> effects run on each
/// unit that enters one of its tiles.
/// </summary>
public sealed record HazardEffect(string HazardArt, AreaDef Area, int Rounds, ImmutableArray<EffectDef> OnEnter) : EffectDef
{
    /// <inheritdoc />
    public override bool OnTile => true;
}

// ---- Units ----------------------------------------------------------------------------------------------------------

/// <summary>
/// <c>"summon"</c>: <see cref="Count"/> units of the monster <see cref="Monster"/> join the user's side under its control
/// (see <see cref="Summons"/>): on the aimed tile and the nearest free ones, or on the first free tiles of the map zone
/// <see cref="Zone"/> when the map has it. They last <see cref="Rounds"/> rounds of the user's turns (null: the whole
/// battle), or until the user's concentration ends with <see cref="Concentration"/>.
/// </summary>
public sealed record SummonEffect(string Monster, int Count = 1, int? Rounds = null, bool Concentration = false,
    string? Zone = null) : EffectDef
{
    /// <inheritdoc />
    public override bool OnTile => true;
}

/// <summary><c>"transform"</c>: runs the named C# hook <see cref="Hook"/> (see <see cref="NamedHooks"/>) with its <see cref="Args"/>.</summary>
public sealed record TransformEffect(string Hook, JsonElement Args) : EffectDef
{
    /// <inheritdoc />
    public override bool OnTile => NamedHooks.TryFor(Hook)?.OnTile == true;
}

// ---- Combat ---------------------------------------------------------------------------------------------------------

/// <summary>
/// <c>"grantResource"</c>: the target gains <see cref="Amount"/> (at the user's level) of <see cref="Resource"/> (a negative
/// amount drains it), never below 0 and, unless <see cref="OverMax"/>, never above the larger of its sheet's maximum (when
/// the sheet has one) and what it holds now, so a normal grant never takes away what an over-the-pool grant gave. The
/// resource <see cref="Primary"/> is the sheet's <see cref="UnitSheet.PrimaryResource"/>, or else its first (ordinal)
/// resource, for content that can't know the class (a race's passive); a target with no resources gains nothing.
/// </summary>
public sealed record GrantResourceEffect(string Resource, ByLevel<int> Amount, bool OverMax = false) : EffectDef
{
    /// <summary>The resource name that means the first resource on the target's sheet.</summary>
    public const string Primary = "primary";
}

/// <summary>
/// <c>"extraAction"</c>: the target gains an extra action this turn (<see cref="Kind"/> <c>"action"</c>), gets its bonus
/// action back (<c>"bonus"</c>) or gains its Move again (<c>"move"</c>, the Dash action).
/// </summary>
public sealed record ExtraActionEffect(string Kind) : EffectDef;

/// <summary><c>"refreshReaction"</c>: the target gets its Reaction back.</summary>
public sealed record RefreshReactionEffect : EffectDef;

/// <summary>
/// <c>"statMod"</c>: the target gains <see cref="Mods"/> for <see cref="Rounds"/> rounds of the user's turns (as a
/// condition, <c>statMod.&lt;ability&gt;</c>), tied to the user's concentration with <see cref="Concentration"/>.
/// </summary>
public sealed record StatModEffect(StaticMods Mods, int Rounds, bool Concentration = false) : EffectDef;

/// <summary>
/// <c>"mark"</c>: the target becomes the user's marked unit (the condition <c>marked</c>, from the user) for
/// <see cref="Rounds"/> rounds; the user's mark on any other unit ends. Content passives read it with
/// <c>targetHasCondition</c>.
/// </summary>
public sealed record MarkEffect(int Rounds) : EffectDef;

/// <summary>
/// <c>"taunt"</c>: the target is taunted by the user (the condition <c>taunted</c>) for <see cref="Rounds"/> rounds: its
/// attack rolls against anyone but the user have disadvantage.
/// </summary>
public sealed record TauntEffect(int Rounds) : EffectDef;

/// <summary><c>"reveal"</c>: units hostile to the user on the tiles of <see cref="Area"/> around the aim lose every condition that makes them invisible.</summary>
public sealed record RevealEffect(AreaDef Area) : EffectDef
{
    /// <inheritdoc />
    public override bool OnTile => true;
}

// ---- Turn order -----------------------------------------------------------------------------------------------------

/// <summary>
/// <c>"turnOrder"</c>: changes this round's order for a target whose turn is still to come (one that has had or is having
/// its turn stays put). <see cref="Op"/> <c>"delay"</c> moves it <see cref="Slots"/> places later, <c>"hasten"</c> that
/// many earlier but never ahead of the next turn, and <c>"swap"</c> trades places with the unit that many places after it.
/// The next round is sorted by initiative again.
/// </summary>
public sealed record TurnOrderEffect(string Op, int Slots = 1) : EffectDef;

// ---- Flow -----------------------------------------------------------------------------------------------------------

/// <summary>
/// <c>"chain"</c>: runs <see cref="Effects"/> on the target, then bounces up to <see cref="Times"/> times: each bounce
/// goes to the nearest unit within <see cref="Range"/> tiles of the last one that is on the first target's side, still in
/// the battle and not yet hit (ties by <see cref="UnitId"/>), and runs the effects on it as a hit of its own.
/// </summary>
public sealed record ChainEffect(int Times, int Range, ImmutableArray<EffectDef> Effects) : EffectDef;

/// <summary><c>"repeat"</c>: runs <see cref="Effects"/> on the target <see cref="Times"/> times (at the user's level), each run a hit of its own.</summary>
public sealed record RepeatEffect(ByLevel<int> Times, ImmutableArray<EffectDef> Effects) : EffectDef;

/// <summary>
/// <c>"if"</c>: runs <see cref="Then"/> when <see cref="When"/> holds (with the user as owner, the target as the other
/// unit and the roll this effect follows; see <see cref="Hooks.Holds"/>), otherwise <see cref="Else"/>.
/// </summary>
public sealed record IfEffect(WhenDef When, ImmutableArray<EffectDef> Then, ImmutableArray<EffectDef> Else) : EffectDef;

/// <summary>One entry of a <c>"random"</c> table: its weight (1 or more) and its effects.</summary>
public sealed record RandomEntry(int Weight, ImmutableArray<EffectDef> Effects);

/// <summary><c>"random"</c>: runs one entry of <see cref="Table"/>, picked by weight with one roll of the battle's RNG.</summary>
public sealed record RandomEffect(ImmutableArray<RandomEntry> Table) : EffectDef;
