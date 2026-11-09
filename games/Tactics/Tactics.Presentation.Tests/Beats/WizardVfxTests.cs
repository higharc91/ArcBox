using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Audio;
using System.Text.Json;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

public sealed class WizardVfxTests
{
    [Fact]
    public void Renamed_spell_effects_retain_their_existing_sound_ids()
    {
        var rules = BaseContent.LogicRules;
        var sounds = SoundBank.Load(Pack.Root);
        var baseline = Path.GetFullPath(Path.Combine(Pack.Root, "../../../tools/tactics-assets/baseline/wizard-vfx.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(baseline));
        foreach (var ability in doc.RootElement.EnumerateObject())
            Assert.Equal(sounds.ForEffect(ability.Value.GetProperty("fx").GetString()!),
                sounds.ForEffect(rules.Abilities[new(ability.Name)].Presentation!.Fx!));
    }

    [Fact]
    public void Every_real_wizard_beat_and_effect_fits_its_budget_and_uses_resolved_distinct_art()
    {
        var rules = BaseContent.LogicRules;
        var art = FxArt.Load(Pack.Root);
        var units = UnitArt.Load(Pack.Root);
        var keys = new HashSet<string>();
        var failures = new List<string>();
        foreach (var sample in ShowcaseFixtures.ForClass("wizard", rules))
        {
            var script = BeatScript.Of(sample.After.Beats.Single(), sample.Before, sample.After, art);
            if (sample.Id == "common.attack") continue;
            var proc = sample.Id == "evoker.empowered_evocation";
            var ability = proc ? null : rules.Abilities[new(sample.Id)];
            var key = proc ? art.Passive(sample.Id) : ability!.Presentation!.Fx;
            Assert.NotNull(key);
            Assert.True(keys.Add(key!), $"Shared Wizard effect: {sample.Id}");
            var clip = art.Clip(key!)!;
            Assert.NotNull(clip);
            var cap = proc ? .8 : BattleCutIn.Tier(ability!) == CutInTier.Full ? 2.5 : ability!.Tier == 0 && BattleCutIn.Tier(ability) == CutInTier.None ? .8 : 1.5;
            if (clip.Seconds > cap || script.Length > cap + 1e-6)
                failures.Add($"{sample.Id}: beat {script.Length:F3}s, effect {clip.Seconds:F3}s, cap {cap:F1}s");
            if (proc)
                Assert.Contains(script.Cues.OfType<BurstCue>(), c => c.Clip.Path == clip.Path);
            else
            {
                var anim = ability!.Presentation!.Anim!;
                Assert.Contains("wizard_", anim);
                Assert.All(units.Sprite("characters.class_wizard")!.Anim(anim).Frames, f => Assert.InRange(f, 189, 222));
                if (ability.Presentation.Projectile is { } shot) Assert.NotNull(art.Shot(shot));
            }
        }
        Assert.Empty(failures);
        Assert.Equal(48, keys.Count);
        Assert.Equal(CutInTier.Full, BattleCutIn.Tier(rules.Abilities[new("wizard.fire_bolt")], downingCritical: true));
    }
}
