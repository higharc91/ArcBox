using System.Collections.Immutable;
using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Beats;

namespace Tactics.Presentation.Tests.Beats;

public sealed class CinematicBudgetTests
{
    private static CutInPlayback Short()
        => new(new BattleCutIn([new(CutInTier.Short, 0.8, Vector2.Zero, Vector2.One)], BattleCinematics.Short), 0, "Spell",
            [new TelegraphWindow(0, 0.2, [new Tile(1, 1)])]);

    [Fact]
    public void Large_party_overhead_is_bounded_without_dropping_the_spell_or_telegraph()
    {
        var budget = new CinematicBudget();
        var total = 0.0;
        for (var i = 0; i < 12; i++)
        {
            var play = budget.Apply(1, 6, Short())!;
            total += play.Extra;
            Assert.True(play.Length >= 0.8);
            Assert.Equal([new Tile(1, 1)], play.Telegraph(play.Extra == 0 ? 0 : BattleCutIn.PushSeconds));
        }
        Assert.Equal(3, total, 6);
        Assert.Equal(0.6, budget.Apply(2, 6, Short())!.Extra, 6);
    }

    [Fact]
    public void Small_party_playback_keeps_its_existing_cinematics()
    {
        var budget = new CinematicBudget();
        for (var i = 0; i < 12; i++) Assert.Equal(0.6, budget.Apply(1, 4, Short())!.Extra, 6);
    }

    [Fact]
    public void Reset_starts_a_new_battles_budget()
    {
        var budget = new CinematicBudget();
        for (var i = 0; i < 10; i++) budget.Apply(1, 6, Short());
        budget.Reset();
        Assert.Equal(0.6, budget.Apply(1, 6, Short())!.Extra, 6);
    }
}
