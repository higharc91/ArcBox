using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    internal Func<int>? CampDie { get; set; }
    private int RollCampDie() => CampDie?.Invoke() ?? _ctx.Random.Next(1, 21);
    private const string CampTimerKey = "camp";
    private CampSave? Camp => _doc.World?.Camp;
    private bool CampVote => Camp?.Stage == "vote";
    private bool CampTalk => Camp?.Stage == "talk";
    private TimeSpan CampPause => Camp?.Stage switch { "jobs" or "rooms" => TimeSpan.FromSeconds(30), "sleep" => Tell.CampSleep, "dawn" => Tell.CampDawn, _ => Tell.Reveal };
    private bool HasCampfires => AllThings.Any(t => t.Kind == "campfire");

    private SceneDef? CampScene()
    {
        if (CampVote)
            return new SceneDef("rest_vote", "", null, Camp!.Mode == "inn" ? "Take rooms for the night?" : "Make camp?", [], [], [SceneDef.Narrator], "ask",
                [new SceneNode("ask", [new SceneLine("rest_vote_ask", SceneDef.Narrator, "Rest for the night, or carry on?", "idle")], [],
                    [new SceneOption("rest", "Rest", "yes", null, [], [], OptionTags.None, null),
                     new SceneOption("later", "Not yet", "no", null, [], [], OptionTags.None, null)], true, null, false)], false);
        if (!CampTalk || Lore.Scenes.FirstOrDefault(s => s.Id == Camp!.Talk && s.Camp) is not { } talk) return null;
        if (Camp!.Watcher is not { } watcher) return talk;
        var first = talk.Nodes.First(n => n.Id == talk.Start);
        return talk with { Nodes = talk.Nodes.Replace(first, first with { Lines = first.Lines.SetItem(0,
            first.Lines[0] with { Text = $"{_doc.Members[watcher].Character.Name} takes the first watch. " + first.Lines[0].Text }) }) };
    }

    private void ProposeRest(string mode, string source, Tile at)
    {
        if (_doc.World?.RestReady != true) { Say("You're not tired yet."); return; }
        LeaveExploration();
        EndTelling();
        var under = _doc.World.Story;
        _doc = _doc with { World = _doc.World with
        {
            Camp = new CampSave(mode, source, at, "vote", Ordinal.Map<CampJob>(), [], [], [], under),
            Story = new StorySave("", StorySave.Lead, "rest_vote", null, 0, _storySeq),
        } };
        _phase = TacticsPhase.Story;
        BeginScene(CampScene()!);
    }

    private void RestVoteDue()
    {
        var voters = Voters().Select(p => p.Profile!.Value.Value).ToArray();
        if (voters.Length == 0) { Hold(); return; }
        var yes = voters.Count(v => !_votes.TryGetValue(v, out var pick) || pick == "rest");
        CloseQuestion();
        EndTelling();
        if (yes * 2 <= voters.Length) { FinishCamp(); return; }
        var camp = Camp!;
        _doc = _doc with { World = _doc.World! with
        {
            Camp = camp with { Stage = camp.Mode == "inn" ? "rooms" : "jobs", Actors = [.. GoldParticipants.Order(StringComparer.Ordinal)] },
            Story = null,
        } };
        if (camp.Mode == "camp" && ThingNamed(camp.Source) is { } fire) SetThing(fire, ThingState(fire) with { State = "lit" });
        ArmCamp(TimeSpan.FromSeconds(30));
    }

    private void ArmCamp(TimeSpan span)
    {
        _ctx.CancelTimer(CampTimerKey);
        _doc = _doc with { World = _doc.World! with { Camp = Camp! with { Due = Voters().Any() ? _ctx.Now + span : null } } };
        Save();
        if (Camp!.Due is not null) _ctx.SetTimer(CampTimerKey, span);
    }

    private void ResumeCamp()
    {
        _phase = TacticsPhase.Story;
        if (Camp is { Mode: "inn" } && OpenShop is null)
        { if (CampVote) FinishCamp(); else Dawn(); return; }
        if (CampTalk && CampScene() is null) { Dawn(); return; }
        if (CampVote) { BeginScene(CampScene()!); return; }
        if (CampTalk && CampScene() is { } scene && Cursor is { Node: { } node } cursor)
        { if (cursor.Roll is not null) ShowRoll(); else PlayLine(scene, scene.Node(node)!, cursor.Line); return; }
        ArmCamp(CampPause);
    }

    private void CampPresenceChanged()
    {
        if (CampVote || CampTalk) { StoryPresenceChanged(); return; }
        if (Camp is null) return;
        if (!Voters().Any())
        {
            _ctx.CancelTimer(CampTimerKey);
            _doc = _doc with { World = _doc.World! with { Camp = Camp with { Due = null } } };
            Save();
        }
        else if (Camp.Due is null)
            ArmCamp(CampPause);
    }

    private IEnumerable<(string Member, InjuryDef Injury)> CampTargets(string job)
        => _doc.Members.SelectMany(m => m.Value.Character.Injuries.Select(i => Rules.Injuries.GetValueOrDefault(i))
            .OfType<InjuryDef>().Where(i => i.Mends == RecoveryRules.Mends(job)).DistinctBy(i => i.Id)
            .Select(i => (m.Key, i)));

    private void CampInput(Player player, ProfileId profile, CustomInput input)
    {
        if (Camp is null || !_online.Contains(player.Id) || _doc.For(profile) is null) return;
        if (CampVote || CampTalk) { StoryInput(player, profile, input); return; }
        if (Camp.Stage == "rooms") { ShopInput(player, profile, input); return; }
        if (input.Action is "skip" or "campNext")
        { if (profile.Value == _lead && Camp.Stage is "results" or "sleep" or "dawn") CampAdvance(); return; }
        if (Camp.Stage != "jobs" || input.Action != "campJob") return;
        var job = Str(input.Data, "job");
        if (job is null || !RecoveryRules.Jobs.Contains(job)) return;
        var target = Str(input.Data, "target");
        var injury = Str(input.Data, "injury");
        if (RecoveryRules.Mends(job) is not null && target is not null)
        {
            if (!CampTargets(job).Any(t => t.Member == target && t.Injury.Id.Value == injury)) return;
            if (Camp.Jobs.Any(j => j.Key != profile.Value && j.Value.Target == target && j.Value.Injury == injury))
            { Toast(player.Id, "That injury is already being tended. Choose another."); return; }
        }
        var actors = Camp.Actors.Contains(profile.Value) ? Camp.Actors : Camp.Actors.Add(profile.Value).Sort(StringComparer.Ordinal);
        _doc = _doc with { World = _doc.World! with { Camp = Camp with { Actors = actors, Jobs = Camp.Jobs.SetItem(profile.Value, new CampJob(job, target, injury)) } } };
        Save();
        if (GoldParticipants.All(m => Camp.Jobs.TryGetValue(m, out var pick) && (RecoveryRules.Mends(pick.Job) is null || pick.Target is not null)))
            ResolveCampJob();
    }

    private void CampAdvance()
    {
        if (Camp is null || !Voters().Any()) return;
        switch (Camp.Stage)
        {
            case "jobs": case "results": ResolveCampJob(); break;
            case "rooms": BeginSleep(); break;
            case "sleep": Dawn(); break;
            case "dawn": FinishCamp(); break;
        }
    }

    private void ResolveCampJob()
    {
        var camp = Camp!;
        var done = camp.Results.Select(r => r.Member).ToHashSet(StringComparer.Ordinal);
        var who = camp.Actors.FirstOrDefault(m => !done.Contains(m));
        if (who is null) { CampTalkOrDawn(); return; }
        var job = camp.Jobs.GetValueOrDefault(who) ?? new CampJob("rest");
        if (RecoveryRules.Mends(job.Job) is not null && job.Target is null) job = new CampJob("rest");
        var character = _doc.Members[who].Character;
        var skill = RecoveryRules.Skill(job.Job, s => SkillCheck.Bonus(character, s, Rules));
        var bonus = SkillCheck.Bonus(character, skill, Rules);
        var die = job.Job == "rest" ? 0 : RollCampDie();
        int? second = job.Job == "tend" && RecoveryRules.HealingSpell(character, Rules) ? RollCampDie() : null;
        var chosen = Math.Max(die, second ?? die);
        var passed = job.Job != "rest" && RecoveryRules.Passed(chosen, bonus, RecoveryRules.Dc(job.Job));
        var text = $"{character.Name} rests easy.";
        switch (job.Job)
        {
            case "tend": case "rally":
                if (job.Target is { } target && job.Injury is { } injury && _doc.Members.TryGetValue(target, out var patient))
                {
                    var id = new InjuryId(injury);
                    if (!patient.Character.Injuries.Contains(id)) passed = false;
                    if (passed) _doc = _doc with { Members = _doc.Members.SetItem(target, patient with { Character = patient.Character with { Injuries = patient.Character.Injuries.Remove(id) } }) };
                    text = passed ? $"{character.Name} {(job.Job == "tend" ? "tends" : "rallies")} {patient.Character.Name}: {Rules.Injuries[id].Name} mends."
                        : $"{character.Name} helps {patient.Character.Name}, but the injury needs more time.";
                }
                break;
            case "cook":
                if (passed) _doc = _doc with { Members = Ordinal.Map(_doc.Members.Select(m => (m.Key, m.Value with { WellFed = true }))) };
                text = passed ? $"{character.Name} cooks a warming meal. Everyone is Well fed." : $"{character.Name}'s stew is mostly smoke.";
                break;
            case "watch":
                var lines = new List<string>();
                foreach (var def in AllThings.Where(t => t.Hidden is not null && !ThingState(t).Found
                    && RecoveryRules.InWatchRange(camp.At, t.At) && chosen + bonus >= t.Hidden.Dc).OrderBy(t => t.Id, StringComparer.Ordinal))
                    Find(def, Interactables.FindingOf(def), who, lines, FindHow.Search);
                text = lines.Count > 0 ? string.Join(" ", lines) : $"{character.Name} keeps watch. The night is quiet.";
                camp = camp with { Watcher = camp.Watcher ?? who };
                break;
        }
        var result = new CampResult(who, job.Job, skill, die, second, bonus, RecoveryRules.Dc(job.Job), passed, text, job.Target, job.Injury);
        _doc = _doc with { World = _doc.World! with { Camp = camp with { Stage = "results", Results = camp.Results.Add(result) } } };
        // The die, the effect and the result line are persisted by this one save.
        ArmCamp(Tell.Reveal);
    }

    private void CampTalkOrDawn()
    {
        var seen = _doc.World!.CampScenes.IsDefault ? [] : _doc.World.CampScenes;
        var scene = Lore.Scenes.FirstOrDefault(s => s.Camp && !seen.Contains(s.Id) && SceneRun.Plays(s, StoryFlags())
            && StoryArc?.Beats.Any(b => b.Id == s.Beat) == true && s.Beat is not null && _doc.World.Beats.Contains(s.Beat));
        if (scene is null) { BeginSleep(); return; }
        EndTelling();
        _doc = _doc with { World = _doc.World with
        {
            Camp = Camp! with { Stage = "talk", Talk = scene.Id, Due = null }, CampScenes = seen.Add(scene.Id),
            Story = new StorySave(scene.Beat!, StorySave.Lead, scene.Id, null, 0, _storySeq),
        } };
        BeginScene(CampScene()!);
    }

    private void BeginSleep()
    {
        EndTelling();
        _doc = _doc with { World = _doc.World! with { Story = null, Camp = Camp! with { Stage = "sleep", Due = null } } };
        ArmCamp(Tell.CampSleep);
    }

    private void Dawn()
    {
        if (Camp?.Stage == "dawn") { ArmCamp(Tell.CampDawn); return; }
        var camp = Camp!;
        var members = Ordinal.Map(_doc.Members.Select(m =>
        {
            var room = camp.Rooms.Contains(m.Key);
            return (m.Key, m.Value with { WellFed = m.Value.WellFed || room,
                Character = m.Value.Character with { Injuries = room ? [] : [.. m.Value.Character.Injuries.Where(i => Rules.Injuries.GetValueOrDefault(i)?.Mends != "rest")] } });
        }));
        _doc = _doc with { Members = members, World = _doc.World! with
        { RestReady = false, RestSeq = checked(_doc.World.RestSeq + 1), Camp = camp with { Stage = "dawn" }, Story = null } };
        if (camp.Mode == "camp" && ThingNamed(camp.Source) is { } fire) SetThing(fire, ThingState(fire) with { State = "unlit" });
        ArmCamp(Tell.CampDawn);
    }

    private void FinishCamp()
    {
        var camp = Camp!;
        _ctx.CancelTimer(CampTimerKey);
        EndTelling();
        _doc = _doc with { World = _doc.World! with { Camp = null, Story = camp.Under, Shop = camp.Mode == "inn" ? null : _doc.World.Shop } };
        Save();
        ToExploration();
    }

    private CustomView CampScreen(Player player, ProfileId profile)
    {
        var camp = Camp!;
        if (CampVote) return StoryScreen(player, profile);
        if (CampTalk)
        {
            // Keep the little camp on phones during the existing story/vote, without changing ordinary story packets.
            var story = StoryScreen(player, profile);
            var data = JsonNode.Parse(story.Data.GetRawText())!.AsObject();
            data["campScene"] = JsonSerializer.SerializeToNode(new
            {
                stage = "talk", mode = camp.Mode, you = profile.Value,
                members = RecoveryMembers().Select(m => new { m.Member, m.Class, m.Here }),
                party = camp.Actors.Select(m => new { member = m, job = camp.Jobs.GetValueOrDefault(m)?.Job }),
            }, ArcJson.Options);
            return story with { Data = JsonSerializer.SerializeToElement(data, ArcJson.Options) };
        }
        if (camp.Stage == "rooms") return ShopScreen(player, profile);
        var pick = camp.Jobs.GetValueOrDefault(profile.Value);
        var targeting = camp.Stage == "jobs" && pick is not null && RecoveryRules.Mends(pick.Job) is not null;
        var screen = camp.Stage == "jobs" ? targeting ? "camp-target" : "camp-job" : "camp-roll";
        return Custom(screen, new
        {
            stage = camp.Stage, mode = camp.Mode, job = pick?.Job, you = profile.Value,
            canPick = _doc.For(profile) is not null && _online.Contains(player.Id) && camp.Stage == "jobs",
            jobs = RecoveryRules.Jobs.Select(j => new { id = j, name = JobName(j), skill = RecoveryRules.Skill(j, s => _doc.For(profile) is { } m ? SkillCheck.Bonus(m.Character, s, Rules) : 0), dc = RecoveryRules.Dc(j), can = RecoveryRules.Mends(j) is null || CampTargets(j).Any() }),
            targets = targeting ? CampTargets(pick!.Job).Select(t => new { member = t.Member, name = _doc.Members[t.Member].Character.Name,
                injury = t.Injury.Id.Value, label = t.Injury.Name, kind = t.Injury.Mends, detail = InjuryDetail(t.Injury),
                can = !camp.Jobs.Any(j => j.Key != profile.Value && j.Value.Target == t.Member && j.Value.Injury == t.Injury.Id.Value),
                selected = pick.Target == t.Member && pick.Injury == t.Injury.Id.Value }) : [],
            members = RecoveryMembers(),
            picked = pick, result = camp.Results.IsEmpty ? null : camp.Results[^1],
            party = camp.Actors.Select(m => new { member = m, name = _doc.Members[m].Character.Name, job = camp.Jobs.GetValueOrDefault(m)?.Job }),
            leader = profile.Value == _lead, vip = IsVip(player),
            difficulty = CampaignDifficulty.ToString(),
        }) with { DeadlineUtc = camp.Due };
    }

    private CampView? CampViewOf()
    {
        if (Camp is not { } camp || _region is null) return null;
        var actors = camp.Actors.IsEmpty ? GoldParticipants.Order(StringComparer.Ordinal).ToImmutableArray() : camp.Actors;
        var reached = WalkReach();
        var ground = WalkGround();
        var seats = RecoveryRules.Seats(ground, camp.At, reached, actors.Length);
        return new CampView(camp.Mode, camp.Source, camp.Stage, camp.At, _doc.World!.RestSeq,
            [.. actors.Select((m, i) => new CampPersonView(m, _doc.Members[m].Character.Name, camp.Jobs.GetValueOrDefault(m)?.Job,
                i < seats.Length ? seats[i] : _at, _doc.Members[m].WellFed, camp.Jobs.GetValueOrDefault(m)?.Target,
                camp.Results.FirstOrDefault(r => r.Member == m) is { } result ? result.Job == "rest" ? "rested" : result.Passed ? "success" : "failed" : null))], camp.Results.IsEmpty ? null : camp.Results[^1], camp.Results.Length, camp.Due, RecoveryMembers());
    }

    private static string JobName(string job) => job switch { "tend" => "Tend wounds", "rally" => "Rally", "cook" => "Cook", "watch" => "Keep watch", _ => "Rest easy" };
    private static string InjuryDetail(InjuryDef injury)
        => injury.Name + " · " + injury.Mends + " recovery" + (injury.Static.MaxHp != 0 ? $" · max HP {injury.Static.MaxHp}" : "")
            + (injury.Static.MaxHpPercent != 0 ? $" · max HP {injury.Static.MaxHpPercent}%" : "")
            + (injury.Static.Move != 0 ? $" · movement {injury.Static.Move}" : "") + (injury.Static.Attack != 0 ? $" · attack {injury.Static.Attack}" : "")
            + (injury.Static.Initiative != 0 ? $" · initiative {injury.Static.Initiative}" : "")
            + (injury.Id.Value == "concussion" ? " · INT/WIS saves −1" : injury.Id.Value == "deep_cut" ? " · Bleeding at battle start" : injury.Id.Value == "shaken" ? " · Frightened at battle start" : "");
}
