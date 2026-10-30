using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// What using an ability spends: Action, Bonus action, Reaction, Move (tiles), class resources by name, a per-battle use
/// and whether it needs the caster's concentration. Zero and absent fields cost nothing and are left out of the JSON.
/// </summary>
public sealed record CostDef(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Action = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Bonus = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Reaction = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Move = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault), JsonConverter(typeof(OrdinalJsonConverter))]
    ImmutableSortedDictionary<string, int>? Resource = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int? Uses = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Concentration = false);

/// <summary>Who or what an ability may be aimed at. JSON: <c>"self"</c>, <c>"ally"</c>, <c>"enemy"</c>, <c>"any_unit"</c>, ….</summary>
public enum TargetKind
{
    /// <summary>The user.</summary>
    [JsonStringEnumMemberName("self")] Self,
    /// <summary>A unit on the user's side still in the battle (Up or Downed: healing and Help revive).</summary>
    [JsonStringEnumMemberName("ally")] Ally,
    /// <summary>A unit on another side still in the battle (Up or Downed: a hit on a Downed unit costs it countdown).</summary>
    [JsonStringEnumMemberName("enemy")] Enemy,
    /// <summary>Any unit still in the battle (Up or Downed).</summary>
    [JsonStringEnumMemberName("any_unit")] AnyUnit,
    /// <summary>A Downed unit on the user's side.</summary>
    [JsonStringEnumMemberName("downed_ally")] DownedAlly,
    /// <summary>A tile on the map.</summary>
    [JsonStringEnumMemberName("tile")] Tile,
    /// <summary>A walkable tile with no unit on it.</summary>
    [JsonStringEnumMemberName("empty_tile")] EmptyTile,
}

/// <summary>
/// How an ability is aimed: the kind of target, the range in tiles (plus the high-ground bonus), whether line of sight is
/// needed, the area it affects, a minimum range and, for melee, the largest height difference it reaches across.
/// </summary>
public sealed record TargetDef(TargetKind Kind, int Range, bool Los, AreaDef Area,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int MinRange = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int? HeightTolerance = null);

/// <summary>Keys into the art manifests for showing an ability: the rig animation, the effect and the projectile.</summary>
public sealed record PresentationDef(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Anim,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Fx,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Projectile);

/// <summary>Hints for the AI planner, such as <c>"damage"</c>, <c>"single"</c> or <c>"heal"</c>.</summary>
public sealed record AiHints(ImmutableArray<string> Tags);

/// <summary>
/// An ability from the content: what it costs, how it is aimed and the effect blocks it runs on each target.
/// <see cref="IsWeaponAttack"/> marks the basic Attack action, which uses the sheet's weapon for range and damage and
/// repeats its effects <see cref="UnitSheet.Attacks"/> times. <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record AbilityDef(AbilityId Id, string Name, int Tier, CostDef Cost, TargetDef Target,
    ImmutableArray<EffectDef> Effects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] PresentationDef? Presentation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] AiHints? Ai = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Flavor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] AbilityId? Replaces = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsWeaponAttack = false);

/// <summary>
/// When a passive's effects run (spec §4.4). JSON: <c>"onBattleStart"</c>, <c>"onAttackHit"</c>, …. Each hook fires for one
/// owner (<see cref="HookArgs.Owner"/>); the battle, round and turn hooks fire for every unit, in sorted
/// <see cref="UnitId"/> order. <see cref="OnDowned"/>, <see cref="OnKill"/> and <see cref="OnAllyDowned"/> are fired by
/// <see cref="DownedRules.Drop"/>.
/// </summary>
public enum HookKind
{
    /// <summary>The battle has started (every unit, before the first turn's effects).</summary>
    [JsonStringEnumMemberName("onBattleStart")] OnBattleStart,
    /// <summary>A round has started (every unit).</summary>
    [JsonStringEnumMemberName("onRoundStart")] OnRoundStart,
    /// <summary>The owner's turn has started (not when a delayed turn resumes).</summary>
    [JsonStringEnumMemberName("onTurnStart")] OnTurnStart,
    /// <summary>The owner's turn is ending, before its until-save rolls and duration ticks.</summary>
    [JsonStringEnumMemberName("onTurnEnd")] OnTurnEnd,
    /// <summary>The owner is about to make an attack roll against <see cref="HookArgs.Other"/>.</summary>
    [JsonStringEnumMemberName("beforeAttackRoll")] BeforeAttackRoll,
    /// <summary>The owner's attack hit <see cref="HookArgs.Other"/>.</summary>
    [JsonStringEnumMemberName("onAttackHit")] OnAttackHit,
    /// <summary>The owner's attack missed <see cref="HookArgs.Other"/>.</summary>
    [JsonStringEnumMemberName("onAttackMiss")] OnAttackMiss,
    /// <summary>The owner took damage (more than 0) from <see cref="HookArgs.Other"/>, if anyone.</summary>
    [JsonStringEnumMemberName("onDamaged")] OnDamaged,
    /// <summary>The owner rolled a saving throw.</summary>
    [JsonStringEnumMemberName("onSave")] OnSave,
    /// <summary>The owner walked one step (or dashed).</summary>
    [JsonStringEnumMemberName("onMove")] OnMove,
    /// <summary>The owner arrived on a tile by any means.</summary>
    [JsonStringEnumMemberName("onEnterTile")] OnEnterTile,
    /// <summary>The owner was Downed (by <see cref="HookArgs.Other"/>, if a unit did it).</summary>
    [JsonStringEnumMemberName("onDowned")] OnDowned,
    /// <summary>The owner killed <see cref="HookArgs.Other"/> (one that dies at 0 HP rather than going Downed).</summary>
    [JsonStringEnumMemberName("onKill")] OnKill,
    /// <summary>An ally of the owner (<see cref="HookArgs.Other"/>) was Downed; fires for standing allies.</summary>
    [JsonStringEnumMemberName("onAllyDowned")] OnAllyDowned,
}

/// <summary>
/// Modifiers that hold for as long as their passive or condition does (spec §4.4 <c>"static"</c>). Numbers add up across
/// sources; flags and sets combine; each edge is one source of advantage or disadvantage (see <see cref="Rolls.Combine"/>).
/// <list type="bullet">
/// <item><see cref="Ac"/>, <see cref="Attack"/> (attack rolls), <see cref="Damage"/> (weapon damage), <see cref="Move"/>,
/// <see cref="Initiative"/> and <see cref="Saves"/> (by stat) add to the unit's numbers.</item>
/// <item><see cref="Incapacitated"/>: no actions, bonus actions or reactions, and no flanking. <see cref="CantMove"/>: no
/// walking or dashing.</item>
/// <item><see cref="OwnAttacks"/>: the edge on the unit's attack rolls; <see cref="AttackedMelee"/> and
/// <see cref="AttackedRanged"/>: the edge on attack rolls against it. <see cref="Invisible"/> gives its attacks advantage
/// and attacks against it disadvantage.</item>
/// <item><see cref="AutoFailSaves"/>: saves of these stats fail whatever the roll. <see cref="CritWhenAdjacent"/>: a hit
/// from an adjacent attacker is a critical hit.</item>
/// <item><see cref="Resist"/> and <see cref="Immune"/> add damage types to the sheet's; <see cref="ExtraActions"/> adds
/// actions to each turn's economy.</item>
/// <item><see cref="MaxHp"/> adds to the sheet's maximum HP. It is applied once, by <see cref="SheetBuilder"/>, from the
/// passives and injuries a character's sheet is built with (Cracked ribs: −5); a battle never re-reads it, so a condition
/// can't carry it (the loader rejects that).</item>
/// </list>
/// </summary>
public sealed record StaticMods(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Ac = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Attack = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Damage = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Move = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Initiative = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableSortedDictionary<Stat, int>? Saves = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Incapacitated = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CantMove = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Edge OwnAttacks = Edge.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Edge AttackedMelee = Edge.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Edge AttackedRanged = Edge.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableSortedSet<Stat>? AutoFailSaves = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CritWhenAdjacent = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Invisible = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault), JsonConverter(typeof(OrdinalJsonConverter))]
    ImmutableSortedSet<string>? Resist = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault), JsonConverter(typeof(OrdinalJsonConverter))]
    ImmutableSortedSet<string>? Immune = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ExtraActions = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int MaxHp = 0)
{
    /// <summary>No modifiers.</summary>
    public static StaticMods None { get; } = new();
}

/// <summary>
/// A test on a hook's situation (see <see cref="Hooks.Holds"/>). JSON: an object with one key, either a combinator
/// (<c>{"all": [...]}</c>, <c>{"any": [...]}</c>, <c>{"not": {...}}</c>) or a leaf. "The target" is
/// <see cref="HookArgs.Other"/>, or the owner when there is none. Leaves:
/// <list type="bullet">
/// <item><see cref="Weapon"/>: a weapon attack whose weapon is any of these (<c>"finesse"</c>, <c>"ranged"</c>,
/// <c>"melee"</c>, <c>"reach"</c>, its id or one of its tags).</item>
/// <item><see cref="Advantage"/>: whether the roll had advantage. <see cref="Flanked"/>: whether the owner flanks the target
/// (an ally that can act stands directly opposite).</item>
/// <item><see cref="TargetHasCondition"/>, <see cref="TargetTag"/>: the target has the condition (implied ones included) or
/// the sheet tag. <see cref="HpBelow"/>: the target's HP is below this percentage of its max.</item>
/// <item><see cref="HeightAbove"/>: the owner stands at least this many levels above the target.</item>
/// <item><see cref="IsSpell"/>: whether the ability is a spell (tier 1 or more) or the attack a spell attack.</item>
/// <item><see cref="DamageType"/>: the damage taken was of this type.</item>
/// <item><see cref="Success"/>: whether the roll the effect follows succeeded (an attack hit, a save was made).</item>
/// </list>
/// Every key present must hold.
/// </summary>
public sealed record WhenDef(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<WhenDef> All = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<WhenDef> Any = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] WhenDef? Not = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> Weapon = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Advantage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Flanked = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConditionId? TargetHasCondition = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? HpBelow = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? HeightAbove = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsSpell = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DamageType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetTag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Success = null);

/// <summary>Whom a passive's effects land on. JSON: <c>"other"</c> (the default) or <c>"self"</c>.</summary>
public enum PassiveTarget
{
    /// <summary>The hook's other unit (<see cref="HookArgs.Other"/>): the attack's target, whoever dealt the damage, ….
    /// The passive doesn't fire when there is none, or when it is the owner.</summary>
    [JsonStringEnumMemberName("other")] Other,
    /// <summary>The owner.</summary>
    [JsonStringEnumMemberName("self")] Self,
}

/// <summary>
/// A passive feature (spec §4.4): <see cref="Static"/> modifiers that always hold, and <see cref="Effects"/> that run on
/// <see cref="Hook"/> when <see cref="When"/> holds, on <see cref="Target"/>, at most once per turn (anyone's), once per
/// round or <see cref="UsesPerBattle"/> times. The effects come from the owner, or, for a condition's passive, from the
/// unit that applied the condition. <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record PassiveDef(string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HookKind? Hook,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WhenDef? When,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<EffectDef> Effects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StaticMods? Static,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool OncePerTurn = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool OncePerRound = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UsesPerBattle = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] PassiveTarget Target = PassiveTarget.Other);

/// <summary>
/// A condition (spec §3.7): its <see cref="Static"/> modifiers and <see cref="Passives"/> hold while a unit has it.
/// <see cref="Tags"/> (<c>"magical"</c>, <c>"physical"</c>, <c>"fear"</c>, …) are what <c>cleanse</c> matches;
/// <see cref="Implies"/> names a condition that comes with this one (stunned implies incapacitated).
/// <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record ConditionDef(ConditionId Id, string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StaticMods? Static,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<PassiveDef> Passives,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Tags,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConditionId? Implies = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConditionId? Replaces = null);

/// <summary>
/// An encounter trigger (spec §4.8). Kept raw until Task 22 gives it typed When/Do records; Task 4–21 battles carry none.
/// The loader checks the zones and unit tags it refers to, and sets <see cref="Once"/> when the content leaves it out.
/// </summary>
public sealed record TriggerDef(string Id, JsonElement When, bool Once, JsonElement Do);

/// <summary>
/// A lasting area effect on the map (e.g. a wall of fire or a difficult-terrain spell), numbered by
/// <see cref="BattleState.NextSerial"/> (see <see cref="ZoneRules"/>). <see cref="RoundsLeft"/> (null: the whole battle)
/// counts its <see cref="Owner"/>'s turn ends like a condition's rounds, <see cref="AppliedThisTurn"/> marking one made
/// during the owner's turn; a <see cref="Concentration"/> zone ends with its owner's concentration. A
/// <see cref="Difficult"/> zone costs 2 Move per tile. Its <see cref="Passives"/> belong to every unit standing in it.
/// </summary>
public sealed record Zone(int Serial, UnitId? Owner, string Kind, ImmutableArray<Tile> Tiles, int? RoundsLeft,
    bool Concentration, bool Difficult, ImmutableArray<PassiveDef> Passives,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool AppliedThisTurn = false);

/// <summary>
/// An injury a party member can gain when it goes Out (spec §3.8), picked from the rules pack's table by
/// <see cref="Weight"/>. Its <see cref="Static"/> modifiers hold in later battles until healed in town.
/// <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record InjuryDef(InjuryId Id, string Name, int Weight, StaticMods Static,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] InjuryId? Replaces = null);

/// <summary>A resource that refills during the battle: <see cref="Amount"/> every <see cref="EveryRounds"/> rounds.</summary>
public sealed record ResourceRegenDef(string Resource, int Amount, int EveryRounds);

/// <summary>
/// One entry in a class's or subclass's feature list. Exactly one key is set: <c>{"ability": id}</c>,
/// <c>{"passive": id}</c>, <c>{"choose": n, "from": [ids]}</c> (pick n of these abilities or passives, e.g. spells known),
/// <c>{"resourceRegen": {...}}</c> or <c>{"attacks": n}</c> (attacks per Attack action from this level).
/// </summary>
public sealed record FeatureRef(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AbilityId? Ability = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Passive = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Choose = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> From = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResourceRegenDef? ResourceRegen = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Attacks = null);

/// <summary>How many skills a new character of the class picks, and from which.</summary>
public sealed record SkillChoicesDef(int Count, ImmutableArray<string> From);

/// <summary>What a new character of the class starts with: weapon and armour ids, a shield, and items in the bag.</summary>
public sealed record StartingKitDef(string Weapon, string Armor, ImmutableArray<ItemStack> Bag, bool Shield = false);

/// <summary>
/// The class's hints for the simulator's role bands and the companion AI: free-form <see cref="Tags"/> and the order in
/// which a default character assigns the standard array to its stats (<see cref="Priority"/>).
/// </summary>
public sealed record ClassAiTags(ImmutableArray<string> Tags, ImmutableArray<Stat> Priority);

/// <summary>A subclass (spec §4.5), picked at level 3, with features at levels 3, 6 and 10 and an optional companion monster.</summary>
public sealed record SubclassDef(string Id, string Name, ImmutableSortedDictionary<int, ImmutableArray<FeatureRef>> Features,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Companion = null);

/// <summary>
/// A class (spec §4.5, <c>classes/&lt;id&gt;.json</c>): hit die, save and gear proficiencies, skill choices, starting kit,
/// per-level resource pools, the features gained at each level 1–10, its subclasses, the default picks (the whole loadout
/// of the highest listed level at or below a character's level, used when it has chosen none), AI hints, the casting stat
/// and the Jump score. <see cref="SubclassesPending"/> lets a class be loaded before its subclasses are written (a stop-gap
/// until every class has them). <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record ClassDef(string Id, string Name, string Role, int HitDie, ImmutableArray<Stat> Saves,
    ImmutableArray<string> ArmorProf, ImmutableArray<string> WeaponProf, SkillChoicesDef SkillChoices, StartingKitDef StartingKit,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, ByLevel<int>> Resources,
    ImmutableSortedDictionary<int, ImmutableArray<FeatureRef>> Features, ImmutableArray<SubclassDef> Subclasses,
    ImmutableSortedDictionary<int, ImmutableArray<AbilityId>> DefaultPicks, ClassAiTags AiTags,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Stat? CastingStat = null,
    int Jump = 2,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool SubclassesPending = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null);

/// <summary>
/// A race (spec §4.6): size, Move, darkvision in tiles, an optional Jump override, resistances, trait passives, the racial
/// ability and any extra skill picks. <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record RaceDef(string Id, string Name, string Size, int Move,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Resist, ImmutableArray<string> Passives,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AbilityId? Ability = null,
    int Darkvision = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Jump = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ExtraSkills = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null);

/// <summary>
/// A background (spec §4.6): its +2/+1 ability boosts, two skills and a starting trinket. <see cref="Replaces"/> lets a
/// later pack override an id.
/// </summary>
public sealed record BackgroundDef(string Id, string Name, ImmutableSortedDictionary<Stat, int> Boosts, ImmutableArray<string> Skills,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Trinket = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null);

/// <summary>A boss phase: below <see cref="HpBelow"/> percent of max HP the monster gains these abilities and passives.</summary>
public sealed record PhaseDef(int HpBelow, ImmutableArray<AbilityId> Abilities, ImmutableArray<string> Passives,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AiProfile = null);

/// <summary>
/// A monster's stat block (spec §4.7, <c>monsters/&lt;family&gt;.json</c>): the unit sheet's numbers plus its challenge
/// rating (<c>"1/4"</c>, <c>"3"</c>), the XP it is worth, its AI profile, its sprite (an art key such as
/// <c>"beasts.wolf.grey"</c>) and, for bosses, phases. Its <see cref="Weapon"/> is written inline in the stat block (a monster's
/// natural weapons are its own; <c>weapons.json</c> holds the gear characters carry). <see cref="Hp"/> is its maximum HP.
/// <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record MonsterDef(string Id, string Name, string Cr, int Xp, ImmutableSortedDictionary<Stat, int> Scores, int Hp, int Ac,
    WeaponDef Weapon, string AiProfile, string Sprite, ImmutableArray<AbilityId> Abilities, ImmutableArray<string> Passives,
    ImmutableSortedSet<Stat> Saves,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Skills,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, int> Resources,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Resist,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Immune,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Vulnerable,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Tags,
    ImmutableArray<PhaseDef> Phases,
    int Level = 1, ArmorKind Armor = ArmorKind.None, int Move = 4, int Jump = 2, int InitiativeBonus = 0, int Attacks = 1,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Stat? CastingStat = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null);

/// <summary>
/// A consumable item (<c>items.json</c>): using it runs the ability <see cref="Use"/>. <see cref="Tags"/> such as
/// <c>"healing"</c> guide the AI. <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record ItemDef(ItemId Id, string Name, AbilityId Use,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Tags,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ItemId? Replaces = null);

/// <summary>
/// A suit of armour (<c>armor.json</c>): its <see cref="Kind"/> caps the DEX bonus (light: none, medium: +2, heavy: DEX
/// doesn't count and initiative −2) and <see cref="BaseAc"/> is the AC it gives before DEX. A shield is not armour: a
/// character's <see cref="CharacterRecord.Shield"/> adds +2. <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record ArmorDef(string Id, string Name, ArmorKind Kind, int BaseAc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null);

/// <summary>
/// A prop as written in a map file. <see cref="Cover"/> is <c>"none"</c>, <c>"half"</c> or <c>"three_quarters"</c>
/// (total cover comes only from line of sight, never from content).
/// </summary>
public sealed record PropDef(string Id, Tile At, bool Blocks = false, string Cover = "none");

/// <summary>
/// A battle map as written in <c>maps/&lt;id&gt;.json</c> (spec §4.8): <see cref="Size"/> is <c>[w, h]</c> and
/// <see cref="Tiles"/> is base64 of three bytes per tile, row-major (height, material index, flags); see
/// <see cref="BattleMap.FromBytes"/>. <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record MapDef(string Id, ImmutableArray<int> Size, string Tiles, ImmutableArray<string> Materials,
    ImmutableArray<PropDef> Props,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, ImmutableArray<Tile>> Zones,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null)
{
    /// <summary>The cover names content may use, by name.</summary>
    public static ImmutableSortedDictionary<string, Cover> CoverNames { get; } =
        Ordinal.Map(("none", Cover.None), ("half", Cover.Half), ("three_quarters", Cover.ThreeQuarters));

    /// <summary>
    /// The engine's map. Throws <see cref="FormatException"/> or <see cref="ArgumentException"/> on bad tiles, size or
    /// cover; the loader reports those as content errors first.
    /// </summary>
    public BattleMap ToBattleMap()
    {
        if (Size.Length != 2)
            throw new ArgumentException($"Map '{Id}' needs a size of [w, h].");
        var props = Props.Select(p => new PropPlacement(p.Id, p.At, p.Blocks,
                CoverNames.TryGetValue(p.Cover, out var cover) ? cover : throw new ArgumentException($"Unknown cover '{p.Cover}'.")))
            .ToImmutableArray();
        return BattleMap.FromBytes(Id, Size[0], Size[1], Convert.FromBase64String(Tiles), Materials, props, Zones);
    }
}

/// <summary>How many party members an encounter takes.</summary>
public sealed record PartySizeDef(int Min, int Max);

/// <summary>
/// A unit an encounter places: a monster id, either on a tile (<see cref="At"/>) or on the first free tiles of a map zone
/// (<see cref="Zone"/>), <see cref="Count"/> times, with an optional level, variant and AI profile override.
/// <see cref="Tag"/> (the monster id when left out) is what objectives and triggers refer to. <see cref="Id"/> names the
/// unit in the battle (a single unit only); without it units are numbered <c>"&lt;monster&gt;.&lt;n&gt;"</c> in placement
/// order (see <see cref="Encounters.Setup"/>).
/// </summary>
public sealed record EncounterUnitDef(string Unit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Tile? At = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Zone = null,
    int Count = 1,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Level = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Variant = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AiProfile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null)
{
    /// <summary>The tag objectives and triggers use: <see cref="Tag"/>, or the monster id.</summary>
    [JsonIgnore]
    public string UnitTag => Tag ?? Unit;
}

/// <summary>
/// An encounter (spec §4.8, <c>encounters/&lt;id&gt;.json</c>): its map, party size, enemies and protected NPCs
/// (<see cref="Npcs"/>), XP and loot tables, objectives (the default lose is <c>party_out</c>) and triggers.
/// <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record EncounterDef(string Id, string Name, string Map, PartySizeDef Party, ImmutableArray<EncounterUnitDef> Enemies,
    ImmutableArray<EncounterUnitDef> Npcs, ImmutableArray<LootTableId> Loot, ObjectivesDef Objectives,
    ImmutableArray<TriggerDef> Triggers, int Xp = 250,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null);

/// <summary>
/// An AI weight set (spec §5.3, <c>ai-profiles.json</c>); Task 14's planner reads it. <see cref="Cruel"/> lets it value
/// Downed targets; <see cref="FleeBelowHp"/> is a fraction of max HP. <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record AiProfileDef(string Id, double Damage = 0, double Kill = 0, double Control = 0, double Heal = 0,
    double Revive = 0, double Caution = 0, double FocusFire = 0, double Objective = 0, double Hoard = 0, bool Cruel = false,
    double FleeBelowHp = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null);

/// <summary>
/// The XP needed to reach each level (<c>xp.json</c>): <see cref="Thresholds"/>[n] is the cumulative XP for level n + 1,
/// starting at 0. A later pack overrides it with <c>"replaces": "xp"</c>.
/// </summary>
public sealed record XpTable(ImmutableArray<int> Thresholds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null)
{
    /// <summary>The id a later pack's <c>"replaces"</c> uses for the XP table.</summary>
    public const string Id = "xp";

    /// <summary>1000 XP per level: 0, 1000, …, 9000 for levels 1–10.</summary>
    public static XpTable Default { get; } = new([.. Enumerable.Range(0, 10).Select(i => i * 1000)]);
}
