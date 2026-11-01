using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The <c>companion</c> profile (spec §5.5): absent players' characters, and timed-out turns, which play it through
/// <see cref="AiOptions.ProfileOverride"/>.
/// <list type="bullet">
/// <item>Its weights are the content's <c>companion</c> profile scaled by the roles in the class's <c>aiTags</c>
/// (<see cref="Weigh"/>), with reviving a Downed ally always the highest weight. Its hoard weight is 0: class resources refill
/// every battle, so they are spent freely.</item>
/// <item>Healers and protected allies are guarded: the planner adds to a tile's position score for standing next to one the
/// foes threaten (see <see cref="Planner"/>).</item>
/// <item>Items: only one tagged <see cref="HealingTag"/>, from its own bag, only on itself, only while it hasn't used an item
/// this battle (<see cref="UnitState.ItemsUsedByAi"/>) and only where the threat map's expected damage next round is at
/// least its HP plus temporary HP (<see cref="WouldDrop"/>). The planner applies this whenever <see cref="AiOptions.AllowItems"/>
/// lets it use items at all.</item>
/// </list>
/// </summary>
internal static class Companion
{
    /// <summary>The profile's id in <c>ai-profiles.json</c>.</summary>
    public const string ProfileId = "companion";

    /// <summary>The item tag that marks an item the companion may use.</summary>
    public const string HealingTag = "healing";

    /// <summary>The class <c>aiTags</c> role that marks an ally the companion guards.</summary>
    public const string HealerRole = "healer";

    /// <summary>How each class role scales the companion's weights; roles not listed (melee, sneak) change nothing.</summary>
    private static readonly ImmutableSortedDictionary<string, Func<AiProfile, AiProfile>> Roles = Ordinal.Map<Func<AiProfile, AiProfile>>(
        (HealerRole, p => p with { Heal = p.Heal * 1.5, Revive = p.Revive * 1.25 }),
        ("support", p => p with { Heal = p.Heal * 1.2, Control = p.Control * 1.2 }),
        ("caster", p => p with { Control = p.Control * 1.2 }),
        ("control", p => p with { Control = p.Control * 1.5 }),
        ("defender", p => p with { Caution = p.Caution * 0.5, Objective = p.Objective * 1.5 }),
        ("front", p => p with { Caution = p.Caution * 0.75 }),
        ("striker", p => p with { Damage = p.Damage * 1.25, Kill = p.Kill * 1.25, FocusFire = p.FocusFire * 1.5 }),
        ("ranged", p => p with { Caution = p.Caution * 1.5 }));

    /// <summary>True when <paramref name="profile"/> is the companion's.</summary>
    public static bool Is(AiProfile profile) => profile.Id == ProfileId;

    /// <summary>
    /// The companion weights for <paramref name="unit"/>: <paramref name="profile"/> scaled by each role of its class's
    /// <c>aiTags</c> in turn (in ordinal order), then Revive raised, if it must be, to stay a quarter above every other weight.
    /// </summary>
    public static AiProfile Weigh(AiProfile profile, UnitState unit, RulesPack rules)
    {
        foreach (var role in RoleTags(unit, rules))
            if (Roles.TryGetValue(role, out var scale))
                profile = scale(profile);
        var top = new[] { profile.Damage, profile.Kill, profile.Control, profile.Heal, profile.Caution, profile.FocusFire, profile.Objective }.Max();
        return profile with { Revive = Math.Max(profile.Revive, 1.25 * top) };
    }

    /// <summary>
    /// The roles in the <c>aiTags</c> of the unit's class or classes (its own sheet's <see cref="SheetBuilder.ClassTag"/>
    /// tags, while transformed too); none for a monster.
    /// </summary>
    public static ImmutableSortedSet<string> RoleTags(UnitState unit, RulesPack rules)
    {
        var roles = Ordinal.Set();
        foreach (var tag in (unit.TrueSheet ?? unit.Sheet).Tags)
            if (tag.StartsWith(SheetBuilder.ClassTag, StringComparison.Ordinal)
                && rules.Classes.TryGetValue(tag[SheetBuilder.ClassTag.Length..], out var cls) && !cls.AiTags.Tags.IsDefault)
                roles = roles.Union(cls.AiTags.Tags);
        return roles;
    }

    /// <summary>True when the companion may consider <paramref name="item"/> at all: a healing item, and no AI item used yet.</summary>
    public static bool MayUse(ItemDef item, UnitState unit) => item.Tags.Contains(HealingTag) && unit.ItemsUsedByAi == 0;

    /// <summary>
    /// True when the threat on <paramref name="at"/> next round (<paramref name="threat"/>, rated on the unit) is at least its
    /// HP plus temporary HP: it would be Downed there.
    /// </summary>
    public static bool WouldDrop(ThreatMap threat, UnitState unit, Tile at) => threat.At(at) >= unit.Hp + unit.TempHp;
}
