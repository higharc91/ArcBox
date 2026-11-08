using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;
namespace Tactics.Presentation.Tests.Beats;
public sealed class WarlockVfxTests
{
    [Fact]
    public void Every_ability_and_visible_invocation_has_its_real_trigger_and_high_level_beams_are_not_omitted()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var samples = FxShowcase.ForClass("warlock", rules);
        Assert.Equal(44, samples.Length);
        foreach (var feature in FxShowcase.Inventory("warlock", rules).Where(f => f.Kind != "static"))
        {
            var sample = Assert.Single(samples.Where(s => s.Id == feature.Id));
            if (feature.Kind == "proc") Assert.Contains(sample.After.Beats.Single().Events.OfType<PassiveTriggered>(), e => e.Passive == feature.Id);
            else Assert.Contains(sample.After.Beats.Single().Events.OfType<AbilityUsed>(), e => e.Ability.Value == feature.Id);
        }
        var arms = samples.Single(s => s.Id == "warlock.arms_of_hadar");
        Assert.Equal(3, arms.After.Beats.Single().Events.OfType<Damaged>().Count());
        var hex = samples.Single(s => s.Id == "warlock.maddening_hex");
        Assert.Contains(hex.Before.Units, u => u.Conditions.Contains("hex"));
        Assert.Contains(hex.After.Beats.Single().Events.OfType<Damaged>(), e => e.Type == "psychic");
        var imp = samples.Single(s => s.Id == "fiendbound.companion");
        Assert.True(imp.After.Units.Length > imp.Before.Units.Length);
        Assert.Contains(imp.After.Beats.Single().Events, e => e is Spawned);
        var pact = samples.Single(s => s.Id == "warlock.pact_magic");
        Assert.Equal(2, pact.After.Round);
        foreach (var level in new[] {5,7})
        {
            var blast = samples.Single(s => s.Id == "warlock.eldritch_blast.level" + level);
            Assert.Equal(level == 5 ? 2 : 3, blast.After.Beats.Single().Events.OfType<AttackResolved>().Count());
        }
    }

    [Fact]
    public void Every_real_beat_fits_its_budget_and_every_ability_or_proc_has_its_own_resolved_effect()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var fx = FxArt.Load(Pack.Root);
        var units = Tactics.Presentation.Board.UnitArt.Load(Pack.Root);
        var failures = new List<string>();
        var effects = new HashSet<string>();
        foreach (var sample in FxShowcase.ForClass("warlock", rules))
        {
            var script = BeatScript.Of(sample.After.Beats.Single(), sample.Before, sample.After, fx);
            var extra = sample.Id.StartsWith("warlock.eldritch_blast.level");
            var id = extra ? "warlock.eldritch_blast" : sample.Id;
            var ability = rules.Abilities.GetValueOrDefault(new(id));
            var key = ability?.Presentation?.Fx ?? fx.Passive(id);
            var cap = id == "common.attack" ? .8 : ability is null ? id is "warlock.pact_magic" or "fiendbound.companion" or "warlock.hunger_of_hadar.turn" ? 1.5 : .8
                : BattleCutIn.Tier(ability) == CutInTier.Full ? 2.5
                : ability.Tier > 0 || ability.Cost.Uses is not null || ability.Cost.Resource?.Any(p => p.Value > 0) == true ? 1.5 : .8;
            if (script.Length > cap + 1e-6) failures.Add($"{sample.Id}: {script.Length:F3}s > {cap:F1}s");
            if (id == "common.attack") continue;
            Assert.NotNull(key);
            var clip = fx.Clip(key!)!; Assert.NotNull(clip);
            if (clip.Seconds > cap) failures.Add($"{id} effect: {clip.Seconds:F3}s > {cap:F1}s");
            if (!extra) Assert.True(effects.Add(key!), id);
            if (ability is null) Assert.Contains(script.Cues.OfType<BurstCue>(), c => c.Clip.Path == clip.Path);
            else
            {
                Assert.StartsWith("characters.warlock_", ability.Presentation!.Anim);
                Assert.All(units.Sprite("characters.class_warlock")!.Anim(ability.Presentation.Anim!).Frames, f => Assert.InRange(f,189,209));
                if (ability.Presentation.Projectile is {} shot) Assert.NotNull(fx.Shot(shot));
            }
            if (extra)
            {
                Assert.Equal(sample.Level == 5 ? 2 : 3, script.Cues.OfType<FlightCue>().Count());
                Assert.Single(script.Cues.OfType<PoseCue>(), p => p.Anim == "characters.warlock_lash");
            }
        }
        Assert.Empty(failures);
        Assert.Equal(41,effects.Count);
        foreach (var name in new[] {"darkness","hunger_of_hadar","one_with_shadows","shroud_of_shadow","shadow_of_moil"})
            Assert.Null(fx.Clip(rules.Abilities[new("warlock."+name)].Presentation!.Fx!)!.Emission);
        Assert.NotNull(fx.Clip(rules.Abilities[new("fiendbound.hellfire_surge")].Presentation!.Fx!)!.Emission);
    }

    [Fact]
    public void All_renamed_effects_retain_their_existing_sounds()
    {
        var sounds = Tactics.Presentation.Audio.SoundBank.Load(Pack.Root);
        var rules = RulesPack.Load([Path.Combine(Pack.Root,"rules")]);
        using var old = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Pack.Root,"../../../tools/tactics-assets/baseline/warlock-vfx.json"))));
        foreach (var a in old.RootElement.EnumerateObject())
            Assert.Equal(sounds.ForEffect(a.Value.GetProperty("fx").GetString()!),sounds.ForEffect(rules.Abilities[new(a.Name)].Presentation!.Fx!));
    }
}
