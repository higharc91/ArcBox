using System.Text.Json;
using System.Collections.Immutable;
using Tactics.Game;
using ArcBox.Sdk;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

public sealed class FxShowcaseTests
{
    [Fact]
    public void Close_up_plays_the_real_cast_frames_without_releasing_the_spell_or_its_light_early()
    {
        var sample = FxShowcase.StepZero(Pack.Root)[3];
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var terrain = new global::Tactics.Presentation.Board.BoardTerrain(rules.Maps[sample.Before.Map], sample.Before.Board);
        var player = new BeatPlayer(FxArt.Load(Pack.Root), global::Tactics.Presentation.Board.UnitArt.Load(Pack.Root)) { EnhancedEffects = true };
        player.CutIn = (beat, before, script) => CutInPlayback.For(beat, before, script, rules, terrain, BattleCinematics.Full);
        player.Receive(sample.Before); player.Receive(sample.After);
        for (var i = 0; i < 24; i++) player.Tick(1.0 / 60);
        var frame = player.Frame(terrain);
        Assert.InRange(frame.Poses[new("hero1")].Seconds, .35, .45);
        Assert.Empty(frame.Lights);
        Assert.Empty(frame.Pops);
        Assert.All(player.Shown!.Units, u => Assert.Equal(sample.Before.Units.First(old => old.Id == u.Id).Hp, u.Hp));
        Assert.Equal(2, player.Camera!.Zoom);
    }

    [Fact]
    public void A_real_counterspell_keeps_the_frame_across_event_batches_and_settles_before_final_shown()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var fireball = new AbilityId("wizard.fireball");
        var counterspell = new AbilityId("wizard.counterspell");
        var caster = new UnitId("enemycaster");
        var foe = new UnitId("hero1");
        var sheet = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Caster", "human", "wizard", null, 5, rules), rules)
            with { Side = Side.Enemy, InitiativeBonus = 100, Abilities = [fireball] };
        var enemy = sheet with { Name = "Counter", Side = Side.Party, InitiativeBonus = 0, Abilities = [counterspell] };
        var map = rules.Maps["crypt_hall"];
        var state = Battle.Start(new BattleSetup(map,
            [new(caster, sheet, new AiControlled("caster"), new(3, 5), []),
             new(foe, enemy, new PlayerControlled(new("preview")), new(7, 5), [])], ObjectivesDef.Default, [], Difficulty.Normal, 0, []), rules, 7).State;
        var cast = Battle.Apply(state, new Use(caster, fireball, new Target(Tile: new(7, 5))), rules);
        Assert.Null(cast.Rejected);
        var offer = Assert.IsType<ReactionOffer>(cast.State.Pending);
        var choice = offer.Options.First(o => o.Ability == counterspell);
        var reaction = Battle.Apply(cast.State, new React(offer.Reactor, choice), rules);
        Assert.Null(reaction.Rejected);
        Assert.Null(reaction.State.Pending);
        BattleView View(BattleState s, ImmutableArray<Beat> beats) => new("demo", map.Id, s.Round, s.Order, s.Active,
            [.. s.Units.Values.Select(u => new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), "characters.class_wizard", u.Pos,
                u.Hp, u.Sheet.MaxHp, u.TempHp, u.Life.ToString(), u.Countdown, [], null))], beats, null, BoardChanges.Of(s), Reacting: (s.Pending as ReactionOffer)?.Reactor);
        var player = new BeatPlayer(FxArt.Load(Pack.Root), global::Tactics.Presentation.Board.UnitArt.Load(Pack.Root));
        var terrain = new global::Tactics.Presentation.Board.BoardTerrain(map, null);
        player.CutIn = (beat, before, script) => CutInPlayback.For(beat, before, script, rules, terrain, BattleCinematics.Full, player.CarriedCamera);
        player.Receive(View(state, []));
        player.Receive(View(cast.State, [new(1, caster, cast.Events)]));
        var parentShown = false;
        for (var i = 0; i < 300 && !parentShown; i++) parentShown |= player.Tick(1.0 / 60).Shown.Contains(1);
        Assert.True(parentShown);
        var held = player.Camera!;
        Assert.Equal(.35f, held.Dim);
        for (var i = 0; i < 10; i++) { player.Tick(1.0 / 60); Assert.Equal(held, player.Camera); }
        player.Receive(View(reaction.State, [new(2, offer.Reactor, reaction.Events)]));
        var finished = false;
        var settling = false;
        for (var i = 0; i < 300 && !finished; i++)
        {
            var tick = player.Tick(1.0 / 60);
            settling |= player.Camera is { Dim: > 0 and < .35f };
            if (tick.Shown.Contains(2)) { finished = true; Assert.Null(player.Camera); }
        }
        Assert.True(settling);
        Assert.True(finished);
    }

    [Fact]
    public void Step_zero_uses_accepted_real_engine_actions_and_is_repeatable()
    {
        var samples = FxShowcase.StepZero(Pack.Root);
        Assert.Equal(4, samples.Length);
        foreach (var sample in samples)
        {
            var beat = Assert.Single(sample.After.Beats);
            Assert.Contains(beat.Events, e => e is AbilityUsed used && used.Ability.Value == sample.Id);
            Assert.Contains(beat.Events, e => e is AttackResolved or Damaged);
        }
        Assert.Equal(JsonSerializer.Serialize(samples, ArcJson.Options), JsonSerializer.Serialize(FxShowcase.StepZero(Pack.Root), ArcJson.Options));
    }

    [Fact]
    public void Area_telegraphs_are_the_rules_tiles_even_with_cinematics_off()
    {
        var sample = FxShowcase.StepZero(Pack.Root)[3];
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var script = BeatScript.Of(sample.After.Beats[0], sample.Before, sample.After, FxArt.Load(Pack.Root));
        var terrain = new global::Tactics.Presentation.Board.BoardTerrain(rules.Maps[sample.Before.Map], sample.Before.Board);
        var playback = CutInPlayback.For(sample.After.Beats[0], sample.Before, script, rules, terrain, BattleCinematics.Off)!;
        var marker = Assert.Single(script.Cues.OfType<AbilityCue>());
        Assert.Equal(Areas.Tiles(rules.Abilities[new(sample.Id)].Target.Area, marker.From, marker.Toward, terrain.Rules), playback.Telegraph(marker.At));
        Assert.Equal(0, playback.Extra);
        Assert.Equal(script.Length, playback.Length);
    }
}
