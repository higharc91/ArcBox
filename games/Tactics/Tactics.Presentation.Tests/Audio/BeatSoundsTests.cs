using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Audio;

/// <summary>Sounds as beat cues: chosen from the beat's own events, timed with the picture, and gone with a beat that's skipped.</summary>
public sealed class BeatSoundsTests
{
    private const double Frame = 1 / 60.0;

    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));
    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));
    private static readonly Lazy<SoundBank> Bank = new(() => SoundBank.Load(Pack.Root));
    private static readonly UnitId Hero = new("hero1");
    private static readonly UnitId Foe = new("goblin.1");
    private static readonly AbilityId Attack = new("common.attack");

    private static UnitView Unit(UnitId id, string side, int x, int y, string sprite, string? player = null)
        => new(id, id.Value, side, sprite, new Tile(x, y), 10, 10, 0, "Up", 0, [], player);

    private static BattleView View(int heroX = 4, string foe = "characters.goblin_cutthroat", string? player = null)
        => new("e", "river_crossing", 1, [], null, [Unit(Hero, "Party", heroX, 3, "characters.class_fighter", player), Unit(Foe, "Enemy", 5, 3, foe)], [], null, BoardChanges.None);

    private static BeatScript Script(BattleView view, params BattleEvent[] events)
        => BeatScript.Of(new Beat(1, Hero, [.. events]), view, view, Fx.Value, Bank.Value);

    private static IEnumerable<SoundCue> Sounds(BeatScript script) => script.Cues.OfType<SoundCue>();

    private static string[] Ids(BeatScript script) => [.. Sounds(script).Select(s => s.Id)];

    private static AbilityUsed Swing(UnitId source, UnitId target) => new(source, Attack, new Target(Unit: target), new PresentationDef("characters.attack", null, null));

    private static BattleEvent[] Blow(UnitId by, UnitId at, string type = "slashing", bool hit = true, bool crit = false) =>
        [Swing(by, at), new AttackResolved(by, at, hit, crit), .. hit ? new BattleEvent[] { new Damaged(at, 4, type, 6) } : []];

    [Fact]
    public void A_melee_blow_swings_as_it_lunges_and_lands_as_the_target_recoils()
    {
        var script = Script(View(), Blow(Hero, Foe));

        var lunge = script.Cues.OfType<LungeCue>().Single();
        var recoil = script.Cues.OfType<PoseCue>().Single(p => p.Anim == "hurt");
        var swing = Sounds(script).Single(s => s.Id.StartsWith("swing_", StringComparison.Ordinal));
        var land = Sounds(script).Single(s => s.Id == "blade_armor" || s.Id == "blade_flesh");

        Assert.Equal(lunge.At, swing.At);
        Assert.Equal(recoil.At, land.At);
    }

    [Fact]
    public void A_goblin_barks_as_it_attacks_and_cries_when_hit()
    {
        Assert.Contains("goblin_attack", Ids(Script(View(), Blow(Foe, Hero))));
        Assert.Contains("goblin_hurt", Ids(Script(View(), Blow(Hero, Foe))));
    }

    [Fact]
    public void A_hit_on_armour_and_a_hit_on_flesh_differ()
    {
        Assert.Contains("blade_armor", Ids(Script(View(), Blow(Foe, Hero))));      // the fighter is in plate or chain
        Assert.Contains("blade_flesh", Ids(Script(View(), Blow(Hero, Foe))));
    }

    [Fact]
    public void A_miss_and_a_crit_have_their_sounds()
    {
        Assert.Contains("attack_miss", Ids(Script(View(), Blow(Hero, Foe, hit: false))));
        Assert.Contains("attack_crit", Ids(Script(View(), Blow(Hero, Foe, crit: true))));
    }

    [Fact]
    public void A_bow_shot_is_released_as_the_arrow_leaves_and_lands_as_one()
    {
        var view = View(2, "characters.goblin_archer");
        var script = Script(view, Blow(Foe, Hero, "piercing"));

        var flight = script.Cues.OfType<FlightCue>().Single();
        Assert.Equal(flight.At, Sounds(script).Single(s => s.Id == "bow_release").At);
        Assert.Contains("arrow_armor", Ids(script));
    }

    [Fact]
    public void A_spell_gathers_then_sounds_with_its_effect()
    {
        var script = Script(View(1), new AbilityUsed(Hero, new AbilityId("cleric.heal"), new Target(Unit: Foe), new PresentationDef("characters.pray", "fx.heal.holy", null)),
            new Healed(Foe, 5, 10));
        var burst = script.Cues.OfType<BurstCue>().First();

        Assert.Equal(0, Sounds(script).Single(s => s.Id == "spell_cast").At);
        Assert.Equal(burst.At, Sounds(script).Single(s => s.Id == "spell_holy_heal").At);
    }

    [Theory]
    [InlineData("characters.walk", "fx.dust.earth")]
    [InlineData("characters.sneak", "fx.smoke.grey")]
    [InlineData("characters.buff", "fx.poof.white")]
    public void Kicking_up_dust_is_not_casting_a_spell(string anim, string fx)
    {
        var self = Script(View(), new AbilityUsed(Hero, new AbilityId("common.dash"), new Target(), new PresentationDef(anim, fx, null)));
        var aimed = Script(View(1), new AbilityUsed(Hero, new AbilityId("common.dash"), new Target(Unit: Foe), new PresentationDef(anim, fx, null)));

        Assert.DoesNotContain("spell_cast", Ids(self));
        Assert.DoesNotContain("spell_cast", Ids(aimed));
    }

    [Fact]
    public void Two_lines_in_one_beat_caption_one_at_a_time_as_they_are_read()
    {
        var player = Player();
        const string first = "One two three four five six.";
        player.Receive(View() with { Beats = [new Beat(1, Hero, [new Said("narrator", first), new Said("warlord", "Second.")])] });

        var captions = new List<(double At, string Text)>();
        for (var t = 0.0; t < 6; t += Frame)
        {
            player.Tick(Frame);
            if (player.Caption is { } line && (captions.Count == 0 || captions[^1].Text != line.Text))
                captions.Add((t, line.Text));
        }

        Assert.Equal([first, "Second."], captions.Select(c => c.Text));
        Assert.InRange(captions[1].At - captions[0].At, 6 * 0.36 - 0.1, 6 * 0.36 + 0.3);     // once the first has been read
    }

    [Fact]
    public void A_heal_with_an_effect_of_its_own_doesnt_also_chime_and_one_without_does()
    {
        var plain = Script(View(), new AbilityUsed(Hero, new AbilityId("a"), new Target(Unit: Foe), null), new Healed(Foe, 5, 10));
        var spell = Script(View(), new AbilityUsed(Hero, new AbilityId("a"), new Target(Unit: Foe), new PresentationDef("characters.pray", "fx.heal.holy", null)), new Healed(Foe, 5, 10));

        Assert.Contains("healed", Ids(plain));
        Assert.DoesNotContain("healed", Ids(spell));
    }

    [Fact]
    public void A_walk_steps_once_per_tile_with_the_tile()
    {
        var path = new[] { new Tile(3, 3), new Tile(4, 3), new Tile(5, 3) };
        var script = Script(View(3), new Moved(Hero, [.. path]));

        var walks = script.Cues.OfType<WalkCue>().ToArray();
        var steps = Sounds(script).Where(s => s.Id.StartsWith("step_", StringComparison.Ordinal) && s.Id != "step_armor").ToArray();

        Assert.Equal(walks.Select(w => w.At), steps.Select(s => s.At));
    }

    [Fact]
    public void Armour_clanks_over_its_footsteps_and_a_troll_shakes_the_ground()
    {
        Assert.Contains("step_armor", Ids(Script(View(3), new Moved(Hero, [new Tile(3, 3), new Tile(4, 3)]))));
        Assert.Equal(["step_heavy"], Ids(Script(View(foe: "bosses.troll.troll"), new Moved(Foe, [new Tile(5, 3), new Tile(6, 3)]))));
    }

    [Fact]
    public void A_bat_flaps_rather_than_steps()
        => Assert.Equal(["flap_wings"], Ids(Script(View(foe: "beasts.bat.dusk"), new Moved(Foe, [new Tile(5, 3), new Tile(6, 3)]))));

    [Fact]
    public void A_turn_is_a_players_or_anyones()
    {
        Assert.Equal(["ui_your_turn"], Ids(Script(View(player: "Ana"), new TurnStarted(Hero, 1))));
        Assert.Equal(["ui_turn_start"], Ids(Script(View(), new TurnStarted(Foe, 1))));
    }

    [Fact]
    public void A_battle_opens_and_each_later_round_is_marked()
    {
        Assert.Equal(["battle_start"], Ids(Script(View(), new RoundStarted(1, []))));
        Assert.Equal(["round_start"], Ids(Script(View(), new RoundStarted(2, []))));
    }

    [Fact]
    public void A_death_cries_out_as_it_falls_and_the_body_goes_at_the_end()
    {
        var script = Script(View(), new Died(Foe));
        var fall = script.Cues.OfType<PoseCue>().Single(p => p.Anim == "down");

        Assert.Equal(fall.At, Sounds(script).Single(s => s.Id == "goblin_death").At);
        Assert.Equal(fall.At + fall.Length, Sounds(script).Single(s => s.Id == "died").At);
    }

    [Fact]
    public void A_hero_going_down_has_its_sound_and_no_creature_cry()
        => Assert.Equal(["downed"], Ids(Script(View(), new Downed(Hero))));

    [Fact]
    public void A_save_rolls_its_die_with_the_badge()
    {
        var roll = new RollResult([14], 14, 0, 14, 12, true, false, false);
        var script = Script(View(), new Rolled(Foe, RollKind.Save, roll));

        Assert.Equal(script.Cues.OfType<BadgeCue>().Single().At, Sounds(script).Single(s => s.Id == "ui_dice_roll").At);
    }

    public static TheoryData<BattleEvent, string> OneSound() => new()
    {
        { new Pushed(Foe, new Tile(5, 3), new Tile(6, 3)), "pushed" },
        { new Collided(Foe, null, 2), "collided" },
        { new Fell(Foe, 2, 3, false), "fell" },
        { new Fell(Foe, 2, 0, true), "fell_deep" },
        { new Teleported(Foe, new Tile(5, 3), new Tile(6, 3)), "spell_misty_step" },
        { new ConcentrationBroken(Foe, new AbilityId("a")), "concentration_broken" },
        { new Countered(Foe, new AbilityId("a"), Hero), "countered" },
        { new Revived(Foe, 1), "revived" },
        { new CountdownTicked(Foe, 2), "ui_countdown_tick" },
        { new Spawned(new UnitId("s"), new Tile(6, 3), null), "spawned" },
        { new TerrainAltered([new Tile(5, 3)], -1), "terrain_shift" },
        { new ConditionApplied(Foe, new ConditionId("poisoned")), "debuff_applied" },
        { new ConditionApplied(Foe, new ConditionId("hasted")), "buff_applied" },
    };

    [Theory]
    [MemberData(nameof(OneSound))]
    public void An_event_has_its_sound(BattleEvent e, string sound) => Assert.Contains(sound, Ids(Script(View(), e)));

    [Fact]
    public void A_collapse_rumbles_once_not_once_a_tile()
    {
        BattleEvent[] tiles = [.. Enumerable.Range(1, 6).Select(x => new TerrainAltered([new Tile(x, 3)], -2))];

        Assert.Single(Ids(Script(View(), tiles)), "terrain_shift");
    }

    [Fact]
    public void Five_targets_hit_at_once_dont_rattle_like_a_machine_gun()
    {
        var targets = Enumerable.Range(0, 5).Select(i => new UnitId($"goblin.{i}")).ToArray();
        var view = View() with { Units = [Unit(Hero, "Party", 1, 1, "characters.class_wizard"), .. targets.Select((t, i) => Unit(t, "Enemy", 5 + i, 5, "characters.goblin_cutthroat"))] };
        var roll = new RollResult([3], 3, 0, 3, 12, false, false, false);
        BattleEvent[] events = [new AbilityUsed(Hero, new AbilityId("wizard.fireball"), new Target(Tile: new Tile(5, 5)), new PresentationDef("characters.summon", null, null)),
            .. targets.SelectMany(t => new BattleEvent[] { new Rolled(t, RollKind.Save, roll), new Damaged(t, 8, "fire", 2) })];

        var fire = Sounds(Script(view, events)).Where(s => s.Id == "dmg_fire").Select(s => s.At).ToArray();

        Assert.InRange(fire.Length, 1, 3);
        Assert.All(fire.Zip(fire.Skip(1)), pair => Assert.True(pair.Second - pair.First >= SoundChoice.SameSoundGap - 1e-9));
    }

    [Fact]
    public void No_sound_bank_no_sounds()
        => Assert.Empty(BeatScript.Of(new Beat(1, Hero, [.. Blow(Hero, Foe)]), View(), View(), Fx.Value).Cues.OfType<SoundCue>());

    public static TheoryData<BattleEvent[]> Beats() => new()
    {
        { Blow(Hero, Foe) },
        { Blow(Foe, Hero, "piercing", crit: true) },
        { [new Moved(Hero, [new Tile(4, 3), new Tile(4, 4), new Tile(4, 5)]), new TurnStarted(Foe, 1)] },
        { [new Died(Foe), new Downed(Hero), new RoundStarted(2, [])] },
    };

    [Theory]
    [MemberData(nameof(Beats))]
    public void Sounds_change_nothing_about_a_beats_timing(BattleEvent[] events)
    {
        var beat = new Beat(1, Hero, [.. events]);
        var (quiet, loud) = (BeatScript.Of(beat, View(), View(), Fx.Value), BeatScript.Of(beat, View(), View(), Fx.Value, Bank.Value));

        Assert.Equal(quiet.Length, loud.Length);
        Assert.Equal(quiet.Cues.Select(c => (c.GetType(), c.At, c.Length)), loud.Cues.Where(c => c is not SoundCue).Select(c => (c.GetType(), c.At, c.Length)));
    }

    private static BeatPlayer Player() => new(Fx.Value, Art.Value, Bank.Value);

    private static List<(double At, string Id)> Heard(BeatPlayer player, double seconds)
    {
        var heard = new List<(double, string)>();
        for (var t = 0.0; t < seconds; t += Frame)
            heard.AddRange(player.Tick(Frame).Sounds.Select(s => (t, s)));
        return heard;
    }

    [Fact]
    public void A_sound_comes_out_of_the_tick_that_reaches_it_and_not_before()
    {
        var player = Player();
        player.Receive(View() with { Beats = [new Beat(1, Hero, [.. Blow(Hero, Foe)])] });

        var heard = Heard(player, 1.5);
        var land = Assert.Single(heard, h => h.Id == "blade_flesh");

        Assert.InRange(land.At, BeatTiming.Windup - 0.05, BeatTiming.Windup + 0.1);
    }

    [Fact]
    public void A_beat_a_catch_up_skipped_makes_no_sound()
    {
        var player = Player();
        Beat[] beats = [.. Enumerable.Range(10, 8).Select(i => i == 17 ? new Beat(i, Hero, [new TurnEnded(Hero)]) : new Beat(i, Hero, [.. Blow(Hero, Foe)]))];

        player.Receive(View() with { Beats = [.. beats] });     // a scene joining a fight: only the newest plays

        Assert.Empty(Heard(player, 3));
    }

    [Fact]
    public void A_catch_up_still_sounds_the_beat_it_lands_on()
    {
        var player = Player();
        player.Receive(View() with { Beats = [new Beat(9, Hero, [new TurnEnded(Hero)]), new Beat(10, Hero, [.. Blow(Hero, Foe)])] });

        Assert.Contains(Heard(player, 2), h => h.Id == "blade_flesh");
    }

    [Fact]
    public void A_broken_beat_dropped_by_the_loop_says_nothing_more()
    {
        var player = Player();
        player.Receive(View() with { Beats = [new Beat(1, Hero, [new Said(Hero.Value, null!)])] });

        Assert.Throws<NullReferenceException>(() => Heard(player, 1));
        player.Skip();

        Assert.Empty(Heard(player, 1));
    }

    [Fact]
    public void A_line_with_a_clip_carries_it_and_its_caption()
    {
        var clips = VoiceClips.Load(Pack.Root);
        const string line = "Archers, to the ridge!";
        var player = new BeatPlayer(Fx.Value, Art.Value, Bank.Value, clips);
        var view = View() with { Encounter = "warlord_bridge", Beats = [new Beat(1, Hero, [new Said("warlord", line)])] };
        player.Receive(view);

        SpokenLine? said = null;
        for (var t = 0.0; t < 0.5 && said is null; t += Frame)
            said = player.Tick(Frame).Lines.FirstOrDefault();

        Assert.NotNull(said);
        Assert.Equal(line, said.Text);
        Assert.NotNull(said.Clip);
        Assert.StartsWith("lore/audio/say_warlord_bridge_", said.Clip);
        Assert.Equal(said, player.Caption);
    }

    [Fact]
    public void A_line_with_no_clip_is_still_said_for_the_voice_chain_to_read()
    {
        var player = new BeatPlayer(Fx.Value, Art.Value, Bank.Value, VoiceClips.Load(Pack.Root));
        player.Receive(View() with { Encounter = "river_crossing", Beats = [new Beat(1, Hero, [new Said("narrator", "Nobody recorded this.")])] });

        SpokenLine? said = null;
        for (var t = 0.0; t < 0.5 && said is null; t += Frame)
            said = player.Tick(Frame).Lines.FirstOrDefault();

        Assert.NotNull(said);
        Assert.Null(said.Clip);
        Assert.Equal(TacticsVoices.Narrator, said.Theme);
    }

    [Fact]
    public void A_long_line_holds_a_beat_no_longer_than_a_short_one()
    {
        var beat = new Beat(1, Hero, [new Said("warlord", new string('x', 500))]);
        var script = BeatScript.Of(beat, View(), View(), Fx.Value, Bank.Value);

        Assert.Equal(BeatTiming.Said, script.Length, 3);
    }
}
