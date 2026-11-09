using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
namespace Tactics.Presentation.Tests.Beats;

public sealed class CombatReadoutTests
{
    [Theory]
    [InlineData(false, "MISS")]
    [InlineData(true, "HIT")]
    public void Roll_readout_names_actor_ability_target_modifier_and_target_number(bool hit, string word)
    {
        var view = RollPanelDemo.Steps()[0].View.Battle!;
        var cue = new CombatCue(0, new("hero1"), new("common.attack"), new("goblin.1"), RollKind.Attack,
            new([9], 9, 3, 12, hit ? 10 : 14, hit, false, false));
        var readout = CombatReadout.Of([cue], view, null)!;
        Assert.Equal("Ana · Attack", readout.Heading);
        Assert.Equal($"Goblin: 9 +3 = 12 vs AC {(hit ? 10 : 14)} {word}", Assert.Single(readout.Rolls));
    }

    [Fact]
    public void A_save_uses_its_original_spell_context_and_names_conditions_and_pushes()
    {
        var steps = RollPanelDemo.Steps(); var before = steps[1].View.Battle!; var after = steps[2].View.Battle!;
        var script = BeatScript.Of(after.Beats[0], before, after, FxArt.Load(Pack.Root));
        var cues = script.Cues.OfType<CombatCue>().ToArray();
        var save = Assert.Single(cues, c => c.Kind == RollKind.Save);
        Assert.Equal(new AbilityId("wizard.burning_hands"), save.Ability);
        Assert.Equal(new UnitId("hero1"), save.Actor);
        var readout = CombatReadout.Of(cues, after, null)!;
        Assert.Contains("Burning Hands", readout.Heading);
        Assert.Contains("10 vs DC 13 FAILED", Assert.Single(readout.Rolls));
        Assert.Contains("Prone", readout.Effect); Assert.Contains("Pushed 1 tile", readout.Effect);
    }

    [Fact]
    public void A_reaction_in_a_later_beat_keeps_the_attackers_roll_and_uses_the_final_AC()
    {
        var root = Pack.Root; var player = new BeatPlayer(FxArt.Load(root), UnitArt.Load(root));
        var view = RollPanelDemo.Steps()[0].View.Battle!;
        var hero = new UnitId("hero1"); var foe = new UnitId("goblin.1");
        var roll = new RollResult([9], 9, 5, 14, 12, true, false, false);
        var first = new Beat(1, foe, [new AbilityUsed(foe, new("common.attack"), new Target(Unit: hero), null), new Rolled(foe, RollKind.Attack, roll)]);
        player.Receive(view with { Beats = [first] });
        for (var t = 0; t < 180; t++) player.Tick(1 / 60.0);
        var second = new Beat(2, hero, [new AbilityUsed(hero, new("wizard.shield"), new Target(Unit: hero), null),
            new ConditionApplied(hero, new("shielded")), new AttackResolved(foe, hero, false, false) { FinalAc = 17 }]);
        player.Receive(view with { Beats = [first, second] });
        var terrain = new BoardTerrain(Pack.Map("river_crossing"), null);
        CombatReadout? readout = null;
        for (var t = 0; t < 180; t++)
        {
            player.Tick(1 / 60.0);
            if (player.Frame(terrain).Combat is { Rolls.Length: > 0 } row) { readout = row; break; }
        }
        Assert.NotNull(readout);
        Assert.Equal("Goblin · Attack", readout.Heading);
        Assert.Contains("9 +5 = 14 vs AC 17 MISS", Assert.Single(readout.Rolls));
        player.Reset(); Assert.Null(player.Frame(terrain).Combat);
    }
    [Theory]
    [InlineData("statMod.wizard.shield", "Shield")]
    [InlineData("prone", "Prone")]
    public void Effect_labels_use_human_names_instead_of_generated_condition_identifiers(string id, string expected)
        => Assert.Equal(expected, CombatReadout.ConditionName(new(id), null));

}
