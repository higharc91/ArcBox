using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

public sealed class CounterspellChainTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_wizard_can_counter_a_counter_to_their_own_spell_without_releasing_either_pending_cast(bool counterAgain)
    {
        var rules = BaseContent.LogicRules;
        var a = new UnitId("a"); var b = new UnitId("b");
        var fireball = new AbilityId("wizard.fireball"); var counterspell = new AbilityId("wizard.counterspell");
        var sheet = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Caster", "human", "wizard", null, 10, rules), rules)
            with { Side = Side.Party, InitiativeBonus = 100, Abilities = [fireball, counterspell] };
        var other = sheet with { Side = Side.Enemy, InitiativeBonus = 0 };
        var map = rules.Maps["crypt_hall"];
        var initial = Battle.Start(new BattleSetup(map,
            [new(a, sheet, new PlayerControlled(new("a")), new(3, 5), []),
             new(b, other, new PlayerControlled(new("b")), new(7, 5), [])], ObjectivesDef.Default, [], Difficulty.Normal, 0, []), rules, 7).State;
        var cast = Battle.Apply(initial, new Use(a, fireball, new Target(Tile: new(7, 5))), rules);
        Assert.Null(cast.Rejected);
        var firstOffer = Assert.IsType<ReactionOffer>(cast.State.Pending);
        Assert.Equal(b, firstOffer.Reactor);
        var first = Battle.Apply(cast.State, new React(b, firstOffer.Options.First(o => o.Ability == counterspell)), rules);
        Assert.Null(first.Rejected);
        var secondOffer = Assert.IsType<ReactionOffer>(first.State.Pending);
        Assert.Equal(a, secondOffer.Reactor);
        var choice = secondOffer.Options.First(o => counterAgain ? o.Ability == counterspell : o.Id == "pass");
        var last = Battle.Apply(first.State, new React(a, choice), rules);
        Assert.Null(last.Rejected);
        Assert.Null(last.State.Pending);
        Assert.Contains(last.Events, e => e is Countered c && c.Caster == (counterAgain ? b : a));
        Assert.Equal(counterAgain, last.Events.OfType<Damaged>().Any(d => d.Unit == b));

        var terrain = new BoardTerrain(map, null);
        var player = new BeatPlayer(FxArt.Load(Pack.Root), UnitArt.Load(Pack.Root));
        player.CutIn = (beat, before, script) => CutInPlayback.For(beat, before, script, rules, terrain, BattleCinematics.Full, player.CarriedCamera);
        player.Receive(View(initial, []));
        Play(cast.State, cast.Events, 1, false);
        Assert.NotNull(player.Camera);
        Play(first.State, first.Events, 2, false);
        Assert.NotNull(player.Camera);
        Play(last.State, last.Events, 3, counterAgain);
        Assert.Null(player.Camera);
        Assert.Equal(View(last.State, []).Units.Select(u => (u.Id, u.Hp, u.Life)), player.Shown!.Units.Select(u => (u.Id, u.Hp, u.Life)));

        void Play(BattleState state, ImmutableArray<BattleEvent> events, int seq, bool released)
        {
            player.Receive(View(state, [new(seq, a, events)]));
            var shown = false; var started = false;
            for (var frame = 0; frame < 1000 && !shown; frame++)
            {
                var tick = player.Tick(1.0 / 60);
                if (tick.Started is { } script)
                {
                    started = true;
                    Assert.Equal(released, script.Cues.OfType<FlightCue>().Any());
                    Assert.Equal(released, script.Cues.OfType<BurstCue>().Any(c => c.Clip.Path.Contains("wizard_fireball", StringComparison.Ordinal)));
                    if (seq == 2)
                        Assert.DoesNotContain(script.Cues.OfType<BurstCue>(), c => c.Clip.Path.Contains("wizard_counterspell", StringComparison.Ordinal));
                    if (released)
                    {
                        var original = script.Cues.OfType<AbilityCue>().Single(c => c.Used.Ability == fireball);
                        var response = script.Cues.OfType<AbilityCue>().Single(c => c.Used.Ability == counterspell);
                        Assert.True(original.At > response.At);
                    }
                }
                shown |= tick.Shown.Contains(seq);
            }
            Assert.True(started);
            Assert.True(shown);
        }
        BattleView View(BattleState state, ImmutableArray<Beat> beats) => new("chain", map.Id, state.Round, state.Order, state.Active,
            [.. state.Units.Values.Select(u => new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), "characters.class_wizard", u.Pos,
                u.Hp, u.Sheet.MaxHp, u.TempHp, u.Life.ToString(), u.Countdown, [], null))], beats, null, BoardChanges.Of(state),
            Reacting: (state.Pending as ReactionOffer)?.Reactor);
    }
}
