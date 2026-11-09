using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

public sealed class BattleCutInTests
{
    private static CutInAction Action(CutInTier tier, double seconds = 1) => new(tier, seconds, new(12, 24), new(140, 60));

    [Theory]
    [InlineData(CutInTier.None)]
    [InlineData(CutInTier.Short)]
    [InlineData(CutInTier.Full)]
    public void Off_preserves_the_original_clock_and_never_moves_or_dims_the_board(CutInTier tier)
    {
        var plan = new BattleCutIn([Action(tier)], BattleCinematics.Off);
        Assert.Equal(0, plan.Extra);
        for (var t = 0.0; t <= 1; t += 0.01)
        {
            Assert.Equal(t, plan.ScriptTime(t));
            Assert.Equal(1, plan.At(t).Zoom);
            Assert.Equal(0, plan.At(t).Dim);
            Assert.False(plan.At(t).Banner);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Holds_use_integer_zoom_and_the_clock_finishes_only_after_settle(int zoom)
    {
        var plan = new BattleCutIn([Action(CutInTier.Full)], BattleCinematics.Full, zoom);
        Assert.Equal(zoom, plan.At(plan.Lead).Zoom);
        Assert.Equal(0, plan.ScriptTime(0.15));
        Assert.Equal(1, plan.ScriptTime(plan.Lead + 1));
        Assert.False(plan.Finished(plan.Lead + 1));
        Assert.True(plan.Finished(plan.Length));
        Assert.Equal(1, plan.At(plan.Length).Zoom);
        Assert.Equal(new Vector2(140, 60), plan.At(plan.Length - 0.1).Focus);
    }

    [Fact]
    public void Reaction_and_multiattack_links_do_not_add_repeated_pushes_or_zoom_out_between_links()
    {
        var plan = new BattleCutIn([Action(CutInTier.Short), Action(CutInTier.None), Action(CutInTier.None)], BattleCinematics.Full);
        Assert.Equal(0.6, plan.Extra, 8);
        Assert.Equal(2, plan.At(plan.Lead + 0.99).Zoom);
        Assert.Equal(2, plan.At(plan.Lead + 1.01).Zoom);
        Assert.Equal(2, plan.At(plan.Lead + 2.01).Zoom);
    }

    [Fact]
    public void Short_caps_a_full_chain_within_the_short_budget()
    {
        var full = new BattleCutIn([Action(CutInTier.Full)], BattleCinematics.Full);
        var shortPlan = new BattleCutIn([Action(CutInTier.Full)], BattleCinematics.Short);
        Assert.InRange(full.Extra, 0, 2);
        Assert.InRange(shortPlan.Extra, 0, 1);
        Assert.True(shortPlan.Length < full.Length);
        Assert.Equal(0.3, shortPlan.Lead, 8);
    }

    [Theory]
    [InlineData(4, BattleCinematics.Full)]
    [InlineData(5, BattleCinematics.Short)]
    [InlineData(6, BattleCinematics.Short)]
    public void Player_count_defaults_do_not_override_the_viewers_explicit_choice(int count, BattleCinematics expected)
    {
        Assert.Equal(expected, BattleCutIn.Preference(count));
        Assert.Equal(BattleCinematics.Full, BattleCutIn.Preference(count, BattleCinematics.Full));
        Assert.Equal(BattleCinematics.Off, BattleCutIn.Preference(count, BattleCinematics.Off));
    }

    [Fact]
    public void Every_shipped_ability_resolves_and_basic_attacks_and_cantrips_stay_unframed()
    {
        var rules = BaseContent.LogicRules;
        foreach (var ability in rules.Abilities.Values) Assert.True(Enum.IsDefined(BattleCutIn.Tier(ability)), ability.Id.Value);
        Assert.Equal(CutInTier.None, BattleCutIn.Tier(RulesPack.CommonAttack));
        Assert.Equal(CutInTier.None, BattleCutIn.Tier(rules.Abilities[new("wizard.fire_bolt")]));
        Assert.Equal(CutInTier.Short, BattleCutIn.Tier(rules.Abilities[new("wizard.magic_missile")]));
        Assert.Equal(CutInTier.Full, BattleCutIn.Tier(rules.Abilities[new("wizard.fireball")]));
        Assert.Equal(CutInTier.Full, BattleCutIn.Tier(RulesPack.CommonAttack, downingCritical: true));
    }

    [Fact]
    public void Optional_metadata_does_not_change_old_presentation_payloads()
    {
        Assert.Equal("{\"anim\":\"characters.point\"}", JsonSerializer.Serialize(new PresentationDef("characters.point", null, null), ArcJson.Options));
        var explicitOff = RulesPack.CommonAttack with { Presentation = new(null, null, null, "none") };
        Assert.Equal(CutInTier.None, BattleCutIn.Tier(explicitOff, downingCritical: true));
        Assert.Throws<ArgumentException>(() => BattleCutIn.Tier(explicitOff with { Presentation = new(null, null, null, "cinema") }));
    }

    [Fact]
    public void Invalid_clocks_and_zoom_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new BattleCutIn([], BattleCinematics.Full));
        Assert.Throws<ArgumentException>(() => new BattleCutIn([Action(CutInTier.Short, double.NaN)], BattleCinematics.Full));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleCutIn([Action(CutInTier.Short)], BattleCinematics.Full, 4));
    }
}
