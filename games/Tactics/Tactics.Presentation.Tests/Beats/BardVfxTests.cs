using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;
namespace Tactics.Presentation.Tests.Beats;
public sealed class BardVfxTests
{
 [Fact]
 public void Every_bard_ability_proc_and_reaction_has_a_real_fixture_with_actual_prerequisites()
 {
  var rules=BaseContent.LogicRules;var samples=ShowcaseFixtures.ForClass("bard",rules);Assert.Equal(35,samples.Length);
  foreach(var feature in FxShowcase.Inventory("bard",rules).Where(f=>f.Kind!="static"))
  {
   var sample=Assert.Single(samples,s=>s.Id==feature.Id);var events=sample.After.Beats.Single().Events;
   if(feature.Kind=="proc")Assert.Contains(events.OfType<PassiveTriggered>(),e=>e.Passive==feature.Id);
   else Assert.Contains(events.OfType<AbilityUsed>(),e=>e.Ability.Value==feature.Id);
  }
  foreach(var id in new[]{"bard.song_of_rest","bard.healing_word","bard.cure_wounds","bard.mass_healing_word"})
   Assert.Contains(samples.Single(s=>s.Id==id).After.Beats.Single().Events.OfType<Healed>(),h=>h.Amount>0);
  foreach(var id in new[]{"bard.countercharm","bard.heroism","bard.lesser_restoration","bard.greater_restoration"})
   Assert.Contains(samples.Single(s=>s.Id==id).After.Beats.Single().Events,e=>e is ConditionRemoved);
  Assert.Contains(samples.Single(s=>s.Id=="bard.dispel_magic").After.Beats.Single().Events,e=>e is ZoneEnded);
  Assert.Contains(samples.Single(s=>s.Id=="bard.silence.turn").After.Beats.Single().Events,e=>e is Damaged);
  Assert.Contains(samples.Single(s=>s.Id=="lore.counterspell").After.Beats.Single().Events,e=>e is Countered);
 }
 [Fact]
 public void Every_bard_script_fits_its_budget_with_its_own_resolved_effect_and_appropriate_light()
 {
  var rules=BaseContent.LogicRules;var fx=FxArt.Load(Pack.Root);var art=Tactics.Presentation.Board.UnitArt.Load(Pack.Root);var failures=new List<string>();var keys=new HashSet<string>();
  foreach(var s in ShowcaseFixtures.ForClass("bard",rules))
  {
   var a=rules.Abilities.GetValueOrDefault(new(s.Id));var cap=s.Id=="common.attack" ? .8 : a is null ? 1.5 : BattleCutIn.Tier(a)==CutInTier.Full ? 2.5 : a.Tier>0||a.Cost.Uses is not null||a.Cost.Resource?.Any(p=>p.Value>0)==true ? 1.5 : .8;
   var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);if(script.Length>cap+1e-6)failures.Add($"{s.Id}: {script.Length:F3}s > {cap:F1}s");
   if(s.Id=="common.attack")continue;var key=a?.Presentation?.Fx??fx.Passive(s.Id);Assert.NotNull(key);Assert.True(keys.Add(key!),s.Id);var clip=fx.Clip(key!)!;Assert.NotNull(clip);Assert.InRange(clip.Seconds,0,cap);
   if(s.Id is "bard.minor_illusion" or "bard.silence" or "bard.greater_invisibility")Assert.Null(clip.Emission);else Assert.NotNull(clip.Emission);
   if(a is not null)Assert.All(art.Sprite("characters.class_bard")!.Anim(a.Presentation!.Anim!).Frames,f=>Assert.InRange(f,189,213));
  }
  Assert.Equal(34,keys.Count);Assert.True(failures.Count==0,string.Join("\n",failures));
 }
 [Fact]
 public void Battle_magic_keeps_the_weapon_and_thunder_damage_numbers_separate()
 {
  var rules=BaseContent.LogicRules;var fx=FxArt.Load(Pack.Root);var s=FxShowcase.Specific("bard","valor.battle_magic",rules);var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);
  foreach(var g in script.Cues.OfType<PopCue>().Where(c=>c.Style==PopStyle.Damage).GroupBy(c=>c.Unit).Where(g=>g.Count()>1))Assert.Equal(g.Count(),g.Select(p=>p.Row).Distinct().Count());
  Assert.True(script.Cues.OfType<PopCue>().Count(c=>c.Style==PopStyle.Damage)>=2);
 }
 [Fact]
 public void All_bard_abilities_keep_their_existing_sounds()
 {
  var rules=BaseContent.LogicRules;var sounds=Tactics.Presentation.Audio.SoundBank.Load(Pack.Root);
  using var old=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Pack.Root,"../../../tools/tactics-assets/baseline/bard-vfx.json"))));
  foreach(var a in old.RootElement.EnumerateObject())Assert.Equal(sounds.ForEffect(a.Value.GetProperty("fx").GetString()!),sounds.ForEffect(rules.Abilities[new(a.Name)].Presentation!.Fx!));
 }
}
