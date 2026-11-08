using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Audio;
using System.Text.Json;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

public sealed class SorcererVfxTests
{
    [Fact]
    public void Renamed_spell_effects_retain_their_existing_sound_ids()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var sounds = SoundBank.Load(Pack.Root);
        var baseline = Path.GetFullPath(Path.Combine(Pack.Root, "../../../tools/tactics-assets/baseline/sorcerer-vfx.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(baseline));
        foreach (var ability in doc.RootElement.EnumerateObject())
            Assert.Equal(sounds.ForEffect(ability.Value.GetProperty("fx").GetString()!),
                sounds.ForEffect(rules.Abilities[new(ability.Name)].Presentation!.Fx!));
    }

    [Fact]
    public void Every_random_outcome_uses_its_actual_damage_material_and_existing_sound()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var art = FxArt.Load(Pack.Root);
        var sounds = SoundBank.Load(Pack.Root);
        var samples = FxShowcase.ForClass("sorcerer", rules);
        Assert.Equal(50, samples.Length);
        var outcomes = new HashSet<string>();
        foreach (var sample in samples.Where(s => s.Id.StartsWith("chaos.wild_surge") || s.Id.StartsWith("chaos.controlled_surge")))
        {
            var beat = sample.After.Beats.Single();
            var used = beat.Events.OfType<AbilityUsed>().Single();
            var script = BeatScript.Of(beat, sample.Before, sample.After, art);
            var hits = beat.Events.OfType<Damaged>().ToArray();
            foreach (var hit in hits)
            {
                var resolved = art.ForDamage(used.Presentation!.Fx, hit.Type)!;
                var material = hit.Type == "lightning" ? "storm" : hit.Type;
                Assert.EndsWith("." + material, resolved);
                outcomes.Add(used.Ability.Value + ":" + hit.Type);
                var clip = art.Clip(resolved)!;
                Assert.EndsWith("_" + material + ".png", clip.Path);
                var apply = script.Cues.OfType<ApplyCue>().Single(c => c.Event == hit);
                var tile = sample.Before.Units.Single(u => u.Id == hit.Unit).At;
                Assert.Contains(script.Cues.OfType<BurstCue>(), c => c.Clip.Path == clip.Path && c.Tile == tile
                    && c.At == apply.At && c.Scale == .5f && c.Alpha == .4f);
                Assert.Equal(sounds.ForEffect(used.Presentation.Fx!), sounds.ForEffect(resolved));
                if (hit.Type is "fire" or "lightning") Assert.NotNull(clip.Emission);
            }
            if (hits.Select(h => h.Type).Distinct().Count() > 1)
                Assert.EndsWith("_arcane.png", script.Cues.OfType<BurstCue>().First().Clip.Path);
        }
        Assert.Equal(5, outcomes.Count);
        Assert.Contains(samples, s => s.Id.StartsWith("chaos.controlled_surge")
            && s.After.Beats.Single().Events.OfType<Damaged>().Select(h => h.Type).Distinct().Count() > 1);
        Assert.Equal("fx.sorcerer_fireball.fire", art.ForDamage("fx.sorcerer_fireball.fire", "cold"));
    }

    [Fact]
    public void Every_real_sorcerer_beat_and_effect_fits_its_budget_and_uses_resolved_distinct_art()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var art = FxArt.Load(Pack.Root);
        var units = UnitArt.Load(Pack.Root);
        var keys = new HashSet<string>();
        var failures = new List<string>();
        foreach (var sample in FxShowcase.ForClass("sorcerer", rules))
        {
            var script = BeatScript.Of(sample.After.Beats.Single(), sample.Before, sample.After, art);
            if (sample.Id == "common.attack") continue;
            if (sample.Id.Count(c => c == '.') == 2) continue;
            var proc = sample.Id == "draconic.elemental_affinity";
            var ability = proc ? null : rules.Abilities[new(sample.Id)];
            var key = proc ? art.Passive(sample.Id) : ability!.Presentation!.Fx;
            Assert.NotNull(key);
            Assert.True(keys.Add(key!), $"Shared Sorcerer effect: {sample.Id}");
            var clip = art.Clip(key!)!;
            Assert.NotNull(clip);
            var cap = proc ? .8 : BattleCutIn.Tier(ability!) == CutInTier.Full ? 2.5 : ability!.Tier == 0 && ability.Cost.Resource?.Any(p => p.Value > 0) != true && ability.Cost.Uses is null && BattleCutIn.Tier(ability) == CutInTier.None ? .8 : 1.5;
            if (clip.Seconds > cap || script.Length > cap + 1e-6)
                failures.Add($"{sample.Id}: beat {script.Length:F3}s, effect {clip.Seconds:F3}s, cap {cap:F1}s");
            if (proc)
                Assert.Contains(script.Cues.OfType<BurstCue>(), c => c.Clip.Path == clip.Path);
            else
            {
                var anim = ability!.Presentation!.Anim!;
                Assert.Contains("sorcerer_", anim);
                Assert.All(units.Sprite("characters.class_sorcerer")!.Anim(anim).Frames, f => Assert.InRange(f, 189, 208));
                if (ability.Presentation.Projectile is { } shot) Assert.NotNull(art.Shot(shot));
            }
        }
        Assert.Empty(failures);
        Assert.Equal(46, keys.Count);
        Assert.Equal(CutInTier.Full, BattleCutIn.Tier(rules.Abilities[new("sorcerer.fire_bolt")], downingCritical: true));
    }
}
