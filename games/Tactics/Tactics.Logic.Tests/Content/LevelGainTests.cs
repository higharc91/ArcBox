namespace Tactics.Logic.Tests;

public sealed class LevelGainTests
{
    [Fact]
    public void Every_class_has_a_non_pick_gain_or_pool_growth_at_every_non_increase_level()
    {
        var rules = BasePackTests.Rules;
        foreach (var cls in rules.Classes.Values)
        foreach (var level in new[] { 1, 2, 3, 5, 6, 7, 9, 10 })
        {
            static bool Gain(FeatureRef f) => f.Ability is not null || f.Passive is not null || f.Attacks is not null || f.RangedAttacks is not null;
            var feature = cls.Features.GetValueOrDefault(level, []).Any(Gain);
            var subclass = !cls.Subclasses.IsEmpty && cls.Subclasses.All(s => s.Features.GetValueOrDefault(level, []).Any(Gain));
            var pool = level > 1 && cls.Resources.Values.Any(r => r.At(level) > r.At(level - 1));
            Assert.True(feature || subclass || pool, $"{cls.Id} level {level} has only picks and no pool gain");
        }
        foreach (var id in new[] { "monk", "barbarian" })
        {
            var cls = rules.Classes[id];
            Assert.Equal(3, cls.Features.Values.SelectMany(f => f).Count(f => f.Choose is not null));
            foreach (var level in new[] { 2, 6, 10 })
            {
                var c = SheetBuilder.DefaultCharacter("Kara", "human", id, null, level, rules);
                Assert.NotEmpty(c.Picks); Assert.NotNull(SheetBuilder.ForCharacter(c, rules));
            }
        }
        var druid = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Kara", "human", "druid", null, 1, rules), rules);
        Assert.Contains(new AbilityId("druid.wild_shape_wolf"), druid.Abilities);
        Assert.True(druid.Resources["wild"] > 0);
    }
}
