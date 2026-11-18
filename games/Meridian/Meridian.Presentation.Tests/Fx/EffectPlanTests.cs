using System.Buffers.Binary;
using Meridian.Presentation.Art;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Fx;

namespace Meridian.Presentation.Tests.Fx;

public sealed class EffectPlanTests : IDisposable
{
    private static readonly MeridianRules Rules = BasePack.Pack.Rules;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-fx-art-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Presented Now(string kind, double seconds = 1.5, string? room = null, CardKind? deck = null, string? card = null,
                                 string? target = null, string? attacker = null, string? outcome = null, string text = "")
        => new(1, kind, new SeatId("s1"), "Title", text, seconds, Compartment: room, Deck: deck, Target: target, Attacker: attacker,
               Outcome: outcome, Card: card);

    private static EqArray<Cue> Cues(Presented now) => EffectPlan.For(Beat.For(now, Rules));

    [Fact]
    public void A_reveal_lights_the_compartment_then_opens_its_hatches()
    {
        var cues = Cues(Now("reveal", 1.2, room: "c7"));
        Assert.Equal(["reveal", "hatchOpen"], cues.Select(c => c.Fx));
        Assert.All(cues, c => Assert.Equal("c7", c.Where));
        Assert.Equal(FxPlace.Hatches, cues[1].Place);
        Assert.True(cues[1].Start >= cues[0].End - 1e-9);   // lights first, then the doors
    }

    [Fact]
    public void A_weld_plays_on_the_sealed_compartments_hatches()
        => Assert.Equal(("hatchSeal", FxPlace.Hatches, "c3"), Cues(Now("sealed", 0.8, room: "c3")).Select(c => (c.Fx, c.Place, c.Where)).Single());

    [Theory]
    [InlineData("hit", "hit,traitLoss", "s2")]
    [InlineData("stunned", "hit,stun", "brood-1")]
    [InlineData("staggered", "stagger", "s1")]
    [InlineData("tie", "miss", "s2")]
    public void A_fight_plays_its_outcome_on_whoever_it_landed_on(string outcome, string fx, string on)
    {
        var target = outcome == "stunned" ? "brood-1" : "s2";
        var cues = Cues(Now("fight", target: target, attacker: "s1", outcome: outcome));
        Assert.Equal(fx, string.Join(",", cues.Select(c => c.Fx)));
        Assert.All(cues, c => Assert.Equal(on, c.Where));
        Assert.All(cues, c => Assert.Equal(FxPlace.Actor, c.Place));
        // After the dice: on the tick, not over the flash and the roll.
        var beat = Beat.For(Now("fight"), Rules);
        Assert.All(cues, c => Assert.Equal(beat.Steps[0].Seconds + beat.Steps[1].Seconds, c.Start, 6));
        Assert.Equal(outcome == "stunned", cues.Any(c => c.Loop));
    }

    [Fact]
    public void The_breach_washes_the_screen_and_distorts_as_tiller_speaks()
    {
        var cues = Cues(Now("breach", 6, text: "Something aboard has woken."));
        Assert.Equal(["breachAlarm", "breachDistortion"], cues.Select(c => c.Fx));
        Assert.All(cues, c => Assert.Equal(FxPlace.Screen, c.Place));
        Assert.True(cues[0].Loop);
    }

    [Fact]
    public void A_death_leaves_an_echo_and_an_incident_plays_its_hazard()
    {
        Assert.Equal(("echo", "s1"), Cues(Now("death", 3, text: "They stay as an echo.")).Select(c => (c.Fx, c.Where)).Single());
        Assert.Equal("sparks", Cues(Now("card", 4, deck: CardKind.Incident, card: "sparking_conduit", text: "Wiring arcs.")).Single().Fx);
        Assert.Empty(Cues(Now("card", 4, deck: CardKind.Incident, card: "old_photo", text: "A photo.")));
    }

    [Fact]
    public void Every_incident_effect_is_a_real_card_and_a_contract_effect()
    {
        var incidents = BasePack.Pack.Cards.Where(c => c.Kind == CardKind.Incident).Select(c => c.Id.Value).ToHashSet();
        foreach (var (card, fx) in EffectPlan.IncidentFx)
        {
            Assert.Contains(card, incidents);
            Assert.Contains(fx, ArtContract.Effects);
        }
    }

    [Fact]
    public void No_effect_outlasts_its_beat_and_every_effect_is_in_the_contract()
    {
        var samples = new[]
        {
            Now("reveal", 1.2, room: "c1"), Now("reveal", 0.4, room: "c1"), Now("sealed", 0.8, room: "c1"), Now("sealed", 0.3, room: "c1"),
            Now("fight", outcome: "hit", target: "s2"), Now("fight", 0.5, outcome: "stunned", target: "b"),
            Now("fight", outcome: "staggered", attacker: "s1"), Now("fight", outcome: "tie", target: "s2"),
            Now("breach", 6, text: "Something aboard has woken."), Now("death", 3, text: "They stay as an echo."),
            Now("card", 4, deck: CardKind.Incident, card: "hull_groan", text: "The ship lurches."),
        };
        foreach (var now in samples)
        {
            var beat = Beat.For(now, Rules);
            var cues = EffectPlan.For(beat);
            Assert.NotEmpty(cues);
            Assert.All(cues, c =>
            {
                Assert.Contains(c.Fx, ArtContract.Effects);
                Assert.InRange(c.Start, 0, beat.Seconds);
                Assert.True(c.End <= beat.Seconds + 1e-9, $"{now.Kind}: {c.Fx} ends at {c.End:0.00} s of {beat.Seconds:0.00} s");
            });
        }
    }

    [Fact]
    public void A_brief_beat_and_the_quiet_kinds_play_nothing()
    {
        Assert.Empty(EffectPlan.For(Beat.For(Now("reveal", 1.2, room: "c1"), Rules, brief: true)));
        Assert.Empty(Cues(Now("roll")));
        Assert.Empty(Cues(Now("notice", 2, text: "A notice.")));
    }

    private string Png(string name, int w, int h)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), w);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), h);
        bytes[32] = (byte)name.Length;
        Directory.CreateDirectory(Path.Combine(_root, "fx"));
        File.WriteAllBytes(Path.Combine(_root, "fx", name), bytes);
        return $"\"file\": \"{name}\", \"frames\": 4, \"fps\": 8, \"frameSize\": [32, 32]";
    }

    [Fact]
    public void Reduced_effects_play_only_reduced_art_for_every_kind()
    {
        // Every effect: hit has both, miss only its full strip, stagger a bare entry.
        var parts = ArtContract.Effects.Select(fx => fx switch
        {
            "miss" => $"\"miss\": {{ \"full\": {{ {Png("miss.png", 128, 32)} }} }}",
            "stagger" => $"\"stagger\": {{ {Png("stagger.png", 128, 32)} }}",
            _ => $"\"{fx}\": {{ \"full\": {{ {Png($"{fx}.png", 128, 32)} }}, \"reduced\": {{ {Png($"{fx}-calm.png", 128, 32)} }} }}",
        });
        File.WriteAllText(Path.Combine(_root, "fx", "manifest.json"), $$"""{ "fx": { {{string.Join(", ", parts)}} } }""");
        var art = MeridianArt.Load(_root, BasePack.Pack);
        Assert.Empty(art.Problems);
        foreach (var fx in ArtContract.Effects)
        {
            var full = EffectPlan.Art(art, fx, reduced: false);
            var reduced = EffectPlan.Art(art, fx, reduced: true);
            Assert.NotNull(full);
            if (fx is "miss" or "stagger")
            {
                Assert.Null(reduced);   // no reduced art: nothing plays, never the full strip
            }
            else
            {
                Assert.EndsWith($"{fx}-calm.png", reduced!.Path);
                Assert.NotEqual(full!.Path, reduced.Path);
            }
        }
    }

    [Fact]
    public void A_strip_plays_once_and_holds_unless_it_loops()
    {
        var strip = new ArtEntry("x.png", 32, 32, Frames: 4, Fps: 10, Loop: true, null, null);
        var once = new Cue("hit", FxPlace.Actor, "s1", 1.0, 0.4);
        var loop = once with { Fx = "stun", Loop = true };
        Assert.Equal(0, EffectPlan.Frame(strip, once, 1.0));
        Assert.Equal(3, EffectPlan.Frame(strip, once, 1.9));    // held on the last frame
        Assert.Equal(1, EffectPlan.Frame(strip, loop, 1.5));    // 5 frames in: round again
    }

    [Fact]
    public void An_unsealed_hatch_opens_and_a_scenario_notice_plays_its_hazard_where_it_says()
    {
        Assert.Equal(("hatchOpen", FxPlace.Hatches, "c3"), Cues(Now("unsealed", 0.8, room: "c3")).Select(c => (c.Fx, c.Place, c.Where)).Single());
        var vent = new Presented(1, "notice", new SeatId("s1"), "", "Tiller vents the Galley.", 2, Compartment: "c4", Fx: "vent");
        Assert.Equal(("vent", FxPlace.Compartment, "c4"), EffectPlan.For(Beat.For(vent, Rules)).Select(c => (c.Fx, c.Place, c.Where)).Single());
        Assert.Empty(EffectPlan.For(Beat.For(vent with { Compartment = null }, Rules)));   // nowhere to show it
        Assert.Empty(EffectPlan.For(Beat.For(vent with { Fx = null }, Rules)));
    }

    [Fact]
    public void Every_hazard_a_notice_can_name_is_a_contract_effect()
        => Assert.All(ScenarioNotice.NoticeEffects, fx => Assert.Contains(fx, ArtContract.Effects));
}
