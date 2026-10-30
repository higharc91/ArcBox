using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>Which side a unit fights on.</summary>
public enum Side { Party, Enemy, Neutral }

/// <summary>The armour a unit wears, which caps DEX to AC and costs initiative when heavy.</summary>
public enum ArmorKind { None, Light, Medium, Heavy }

/// <summary>
/// A unit's weapon: damage, the stat it attacks with, and its reach or range in tiles. Weapons characters carry are in
/// <c>weapons.json</c> (<see cref="RulesPack.Weapons"/>); a monster writes its own inline. <see cref="Replaces"/> lets a
/// later pack override a weapon id; it is cleared when the pack is loaded, so a unit's weapon never carries it.
/// </summary>
public sealed record WeaponDef(string Id, Dice Damage, string DamageType, Stat Stat, bool Finesse, bool Ranged,
    int Range, bool Reach, [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Tags,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null);

/// <summary>
/// A unit's built character sheet, snapshotted into the battle at start. <see cref="Resources"/> holds each resource's
/// maximum per battle; the battle refills to it. <see cref="CastingStat"/> names a <see cref="Stat"/> (e.g. <c>"Wis"</c>).
/// </summary>
public sealed record UnitSheet(
    string Name, Side Side, int Level, ImmutableSortedDictionary<Stat, int> Scores, int MaxHp, int Ac, ArmorKind Armor,
    int Move, int Jump, int InitiativeBonus, ImmutableSortedSet<Stat> SaveProficiencies,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Skills,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, int> Resources,
    ImmutableArray<AbilityId> Abilities, ImmutableArray<string> Passives, WeaponDef Weapon, int Attacks,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Resist,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Immune,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Vulnerable,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Tags,
    string? Sprite, string? AiProfile, string? CastingStat, int Xp = 0)
{
    /// <summary>The score for <paramref name="stat"/>, or 10 when the sheet doesn't list it.</summary>
    public int Score(Stat stat) => Scores.TryGetValue(stat, out var score) ? score : 10;
}
