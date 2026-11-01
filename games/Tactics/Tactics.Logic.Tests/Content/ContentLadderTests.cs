using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>
/// Rules the shipped pack keeps at every level 1–10, not only at the levels and default picks the simulator samples
/// (Task 23 review, findings 4 and 5). Each test collects every breach before failing, so one run lists them all.
/// </summary>
public sealed class ContentLadderTests
{
    private static RulesPack Rules => BasePackTests.Rules;

    private static readonly Tile Centre = new(12, 12);

    private static readonly Terrain Open = new(
        AsciiMap.Parse(string.Join('\n', Enumerable.Range(0, 25).Select(_ => string.Join(' ', Enumerable.Repeat("0.", 25))))),
        ImmutableSortedDictionary<Tile, int>.Empty, ImmutableSortedSet<Tile>.Empty);

    [Fact]
    public void Class_pools_never_shrink_as_the_level_rises()
    {
        var problems = new List<string>();
        foreach (var cls in Rules.Classes.Values)
        foreach (var (resource, pool) in cls.Resources)
            for (var level = 2; level <= 10; level++)
                if (pool.At(level) < pool.At(level - 1))
                    problems.Add($"{cls.Id} {resource}: {pool.At(level - 1)} at level {level - 1}, {pool.At(level)} at level {level}");

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Every ability a class or subclass grants, offers in a <c>choose</c> list or puts in its default picks can be paid
    /// for from the class's pools at the level it arrives. Pools never shrink, so it stays affordable after that.
    /// </summary>
    [Fact]
    public void Every_granted_ability_can_be_paid_for_at_the_level_that_grants_it()
    {
        var problems = new List<string>();
        foreach (var cls in Rules.Classes.Values)
        {
            void Check(int level, AbilityId id, string how)
            {
                if (!Rules.Abilities.TryGetValue(id, out var ability) || ability.Cost.Resource is not { } cost)
                    return;
                foreach (var (resource, amount) in cost)
                {
                    var pool = cls.Resources.TryGetValue(resource, out var byLevel) ? byLevel.At(level) : 0;
                    if (amount > pool)
                        problems.Add($"{cls.Id} level {level}: {how} {id.Value} costs {amount} {resource}, the pool is {pool}");
                }
            }

            void CheckFeatures(ImmutableSortedDictionary<int, ImmutableArray<FeatureRef>> features, string owner)
            {
                foreach (var (level, refs) in features)
                foreach (var feature in refs)
                {
                    if (feature.Ability is { } granted)
                        Check(level, granted, $"{owner} grants");
                    if (!feature.From.IsDefault)
                        foreach (var option in feature.From)
                            Check(level, new AbilityId(option), $"{owner} offers");
                }
            }

            CheckFeatures(cls.Features, cls.Id);
            foreach (var subclass in cls.Subclasses)
                CheckFeatures(subclass.Features, subclass.Id);
            for (var level = 1; level <= 10; level++)
            {
                var picks = cls.DefaultPicks.Where(p => p.Key <= level).Select(p => p.Value).LastOrDefault();
                if (!picks.IsDefault)
                    foreach (var pick in picks)
                        Check(level, pick, "default pick");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Within one class's Mana spells, a higher-tier area spell whose footprint is at least as large never deals less
    /// direct damage per target than a lower-tier one. Equal is allowed: the larger footprint is the upgrade. Single-target
    /// spells, zone-only damage and subclass kits are their own ladders and aren't compared here.
    /// </summary>
    [Fact]
    public void Area_spell_damage_per_target_never_falls_as_the_tier_rises()
    {
        // Each breaching pair is reported once, at the first level it shows.
        var problems = new Dictionary<(AbilityId High, AbilityId Low), string>();
        for (var level = 1; level <= 10; level++)
        {
            var spells = Rules.Abilities.Values
                .Where(a => a.Tier > 0 && a.Cost.Resource?.ContainsKey("mana") == true)
                .Select(a => (Ability: a, Class: a.Id.Value.Split('.')[0], Tiles: Footprint(a.Target.Area),
                    Damage: DirectDamage(a.Effects, level)))
                .Where(s => s.Tiles > 1 && s.Damage > 0 && Rules.Classes.ContainsKey(s.Class))
                .ToList();

            foreach (var high in spells)
            foreach (var low in spells)
                if (high.Class == low.Class && high.Ability.Tier > low.Ability.Tier && high.Tiles >= low.Tiles
                    && high.Damage < low.Damage)
                    problems.TryAdd((high.Ability.Id, low.Ability.Id),
                        $"level {level}: {high.Ability.Id.Value} (tier {high.Ability.Tier}, {high.Tiles} tiles) deals " +
                        $"{high.Damage:0.#} per target, below {low.Ability.Id.Value} (tier {low.Ability.Tier}, {low.Tiles} tiles) at {low.Damage:0.#}");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Values));
    }

    [Fact]
    public void Same_tier_area_spells_with_similar_footprints_deal_similar_damage()
    {
        // Lightning Bolt kept the SRD's 8d6 after Fireball was cut to 3d6. Within a class and tier, a much smaller area may
        // hit harder, but two area spells whose footprints are within a third of each other differ by at most 1.5×.
        var problems = new List<string>();
        var spells = Rules.Abilities.Values
            .Where(a => a.Tier > 0 && a.Cost.Resource?.ContainsKey("mana") == true)
            .Select(a => (Ability: a, Class: a.Id.Value.Split('.')[0], Tiles: Footprint(a.Target.Area),
                Damage: DirectDamage(a.Effects, 10)))
            .Where(s => s.Tiles > 1 && s.Damage > 0 && Rules.Classes.ContainsKey(s.Class))
            .ToList();

        foreach (var a in spells)
        foreach (var b in spells)
            if (a.Class == b.Class && a.Ability.Tier == b.Ability.Tier && b.Tiles * 4 >= a.Tiles * 3 && a.Tiles * 4 >= b.Tiles * 3
                && a.Damage > b.Damage * 1.5)
                problems.Add($"{a.Ability.Id.Value} ({a.Tiles} tiles) deals {a.Damage:0.#} per target, over 1.5× " +
                    $"{b.Ability.Id.Value} ({b.Tiles} tiles) at {b.Damage:0.#}");

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Tiles an area covers on open ground, aimed one tile east of the caster (the direction for lines and cones).</summary>
    private static int Footprint(AreaDef area)
        => Areas.Tiles(area, Centre, new Tile(Centre.X + 1, Centre.Y), Open).Length;

    /// <summary>
    /// Average damage one target takes from a failed save or a hit, summed over the top-level blocks and their
    /// <c>repeat</c>s. Zones, conditions and branches such as <c>if</c> add nothing: they aren't damage every target takes.
    /// </summary>
    private static double DirectDamage(ImmutableArray<EffectDef> effects, int level)
        => effects.IsDefault ? 0 : effects.Sum(e => e switch
        {
            DamageEffect { Dice: { } dice } => dice.At(level).Average,
            SaveEffect save => DirectDamage(save.OnFail, level),
            AttackEffect attack => DirectDamage(attack.OnHit, level),
            RepeatEffect repeat => repeat.Times.At(level) * DirectDamage(repeat.Effects, level),
            _ => 0,
        });
}
