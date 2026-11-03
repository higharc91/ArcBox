using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>A class-bound keepsake in the character's single trinket slot, using the existing passive engine.</summary>
public sealed record TrinketDef(string Id, string Name, string Class, string Rarity, StaticMods Static, string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HookKind? Hook = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<EffectDef> Effects = default)
{
    public bool ActiveFor(CharacterRecord character) => character.Class == Class;

    internal PassiveDef Passive() => new(RulesPack.TrinketPassiveId(Id), Hook, null,
        Effects.IsDefault ? [] : Effects, Static with { MaxHp = 0 }, Target: PassiveTarget.Self);
}
