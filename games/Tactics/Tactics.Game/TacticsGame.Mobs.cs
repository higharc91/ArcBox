using Tactics.Logic;
using System.Collections.Immutable;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    private bool _mobDirty;
    private const string MobAmbushTimer = "mobAmbush";
    private const string MobTimer = "worldMobs";
    private readonly Dictionary<string, (Tile At, long Step, int Seed, DateTimeOffset Due)> _mobActors = new(StringComparer.Ordinal);

    private IEnumerable<MobDef> PresentMobs => _region is null ? [] : Rules.MobsIn(_region.Def.Id).Where(m =>
        !(_doc.World is { MobsBeaten.IsDefaultOrEmpty: false } world && world.MobsBeaten.Contains(m.Id))
        && (m.Requires.IsDefaultOrEmpty || m.Requires.All(StoryFlags().Contains))
        && (m.Unless.IsDefaultOrEmpty || !m.Unless.Any(StoryFlags().Contains)));

    private void ArmMobs()
    {
        if (_phase != TacticsPhase.Exploration || _online.Count == 0)
        { _ctx.CancelTimer(MobTimer); _ctx.CancelTimer(MobAmbushTimer); return; }
        if (Rules.Mobs.Count != 0 && _doc.World is { } world && _region is not null && _region.Def.Chapter > world.MobChapter)
        {
            _doc = _doc with { World = world with { MobsBeaten = default, MobActors = null, MobChapter = _region.Def.Chapter } };
            _mobActors.Clear();
            _mobDirty = true;
            SaveSoon();
        }
        if (_doc.World?.MobAmbush is { } pending)
        {
            var group = Rules.Mobs.Values.SelectMany(m => m).FirstOrDefault(m => m.Id == pending);
            if (group is not null) Note(Rules.Encounters[group.Encounter].Name + " attack!");
            _ctx.SetTimer(MobAmbushTimer, TimeSpan.FromSeconds(1));
            return;
        }
        if (!PresentMobs.Any()) { _ctx.CancelTimer(MobTimer); return; }
        foreach (var mob in PresentMobs)
            for (var i = 0; i < mob.Show.Length; i++)
            {
                var key = _region!.Def.Id + ":" + mob.Id + ":" + i;
                if (!_mobActors.ContainsKey(key))
                {
                    var saved = _doc.World?.MobActors?.GetValueOrDefault(key);
                    _mobActors[key] = (saved?.At ?? mob.Patch[i % mob.Patch.Length], saved?.Step ?? 0, saved?.Seed ?? _epoch, _ctx.Now.AddSeconds(mob.Pace));
                }
            }
        _ctx.SetTimer(MobTimer, TimeSpan.FromMilliseconds(250));
    }

    private void StepMobs()
    {
        if (_phase != TacticsPhase.Exploration || _online.Count == 0) return;
        var changed = false;
        var roaming = !QuestLogOpen && _doc.World?.Shop is null && Camp is null;
        if (roaming)
            foreach (var mob in PresentMobs)
                for (var i = 0; i < mob.Show.Length; i++)
                {
                    var key = _region!.Def.Id + ":" + mob.Id + ":" + i;
                    if (!_mobActors.TryGetValue(key, out var actor) || actor.Due > _ctx.Now) continue;
                    var occupied = _mobActors.Where(p => p.Key != key && p.Key.StartsWith(_region!.Def.Id + ":", StringComparison.Ordinal)).Select(p => p.Value.At).ToHashSet();
                    var at = MobMotion.Step(_region!.Def.Id, key, actor.Seed, actor.Step, actor.At, mob.Patch.ToHashSet(),
                        tile => WalkGround().Walkable(tile) && !occupied.Contains(tile), _at);
                    _mobActors[key] = (at, actor.Step + 1, actor.Seed, _ctx.Now.AddSeconds(mob.Pace));
                    changed = true;
                }
        if (changed && _doc.World is { } world && _mobActors.Count != 0)
        {
            _doc = _doc with { World = world with { MobActors = _mobActors.ToImmutableSortedDictionary(a => a.Key, a => new MobActorSave(a.Value.At, a.Value.Step, a.Value.Seed), StringComparer.Ordinal) } };
            _mobDirty = true;
            SaveSoon();
        }
        if (roaming && ContactMob()) return;
        ArmMobs();
    }

    private bool ContactMob()
    {
        foreach (var mob in PresentMobs)
            for (var i = 0; i < mob.Show.Length; i++)
                if (_mobActors.TryGetValue(_region!.Def.Id + ":" + mob.Id + ":" + i, out var actor) && MobMotion.Contact(_at, actor.At))
                {
                    Halt();
                    _doc = _doc with { World = _doc.World! with { MobAmbush = mob.Id } };
                    _mobDirty = true;
                    Note(Rules.Encounters[mob.Encounter].Name + " attack!");
                    SaveWorld();
                    _ctx.CancelTimer(MobTimer);
                    _ctx.SetTimer(MobAmbushTimer, TimeSpan.FromSeconds(1));
                    return true;
                }
        return false;
    }
    private void BeginMobBattle()
    {
        if (_phase != TacticsPhase.Exploration || _doc.World?.MobAmbush is not { } id) return;
        var mob = PresentMobs.FirstOrDefault(m => m.Id == id);
        _doc = _doc with { World = _doc.World with { MobAmbush = null } };
        _mobDirty = true;
        if (mob is not null) StartFight(mob.Encounter, null, mob.Id);
        else { SaveWorld(); ArmMobs(); }
    }

    private ImmutableArray<MobView>? VisibleMobs()
    {
        var views = ImmutableArray.CreateBuilder<MobView>();
        foreach (var mob in PresentMobs.Where(m => _doc.Battle?.Mob != m.Id))
            for (var i = 0; i < mob.Show.Length; i++)
            {
                var id = _region!.Def.Id + ":" + mob.Id + ":" + i;
                if (_mobActors.TryGetValue(id, out var actor) && Math.Max(Math.Abs(actor.At.X - _at.X), Math.Abs(actor.At.Y - _at.Y)) <= 20)
                    views.Add(new(id, mob.Id, mob.Show[i], actor.At, actor.Step, actor.Step == 0 ? default : [new WalkLeg((int)Math.Min(actor.Step, int.MaxValue), actor.At, (int)Walk.Step.TotalMilliseconds)]));
            }
        return views.Count == 0 ? null : views.ToImmutable();
    }

    private string? NearbyMob()
    {
        var visible = VisibleMobs();
        if (visible is null) return null;
        var nearby = visible.Value.FirstOrDefault(m => Math.Abs(m.At.X - _at.X) + Math.Abs(m.At.Y - _at.Y) <= 4);
        if (nearby is null) return null;
        var group = PresentMobs.First(m => m.Id == nearby.Group);
        return Rules.Encounters[group.Encounter].Name;
    }

}
