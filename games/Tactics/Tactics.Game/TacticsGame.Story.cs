using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// The story in the world (overworld 5d): the beat on offer and its marker, Interact, and the Story phase, which plays a beat's scenes
/// line by line on the server's clock. The TV only shows and speaks what it's told and sends nothing back, so a slow or missing TV never
/// holds a scene up. Walking can't run in a scene: <see cref="LeaveExploration"/> ends all of it on the way in. The cursor
/// (<see cref="StorySave"/>) is saved on every line, so a restart resumes on the line it left.
/// </summary>
public sealed partial class TacticsGame
{
    /// <summary>What a scene is doing: the values of <see cref="StoryView.State"/>.</summary>
    public static class StoryStates
    {
        /// <summary>A line is showing, and the next follows when it ends.</summary>
        public const string Lines = "lines";

        /// <summary>The node's last line is a question, and the party (or the leader) is answering it.</summary>
        public const string Asking = "asking";

        /// <summary>A vote tied, and its decider is breaking the tie.</summary>
        public const string TieBreak = "tieBreak";

        /// <summary>A skill check won, and the party is picking who rolls.</summary>
        public const string PickRoller = "pickRoller";

        /// <summary>The roll is made, and its result shows.</summary>
        public const string Rolled = "rolled";

        /// <summary>The pause between two scenes of a beat, with the next one's title.</summary>
        public const string Gap = "gap";
    }

    /// <summary>What everyone is told when a saved scene is gone after a content update: the beat starts again from its first scene.</summary>
    public const string StoryChanged = "The story has changed since you were last here, so this part starts again.";

    /// <summary>
    /// Told of anything in a scene the game can't play as written (5d plan §8, a dead end): a node with nothing to offer, a beat with no
    /// scene. The story carries on regardless; the server logs these.
    /// </summary>
    public Action<string>? StoryLogged { get; init; }

    /// <summary>What the scene is doing, in the Story phase: one of <see cref="StoryStates"/>.</summary>
    private string _telling = StoryStates.Lines;

    /// <summary>The number of the last line shown, which only goes up, across restarts too (<see cref="WorldSave.StorySeq"/>): <see cref="StoryView.Seq"/>.</summary>
    private int _storySeq;

    /// <summary>When the line showing has been heard: a question's close never comes before it.</summary>
    private DateTimeOffset _lineDue;

    /// <summary>What the open question offers, in the node's order.</summary>
    private ImmutableArray<SceneOption> _offered = [];

    /// <summary>When the open question closes, or null while it waits for a phone (nobody here can answer it). The TV's countdown.</summary>
    private DateTimeOffset? _closeAt;

    /// <summary>
    /// When the open question's own clock runs out, never before its line is heard, or null while it holds. The phones' countdown: a settle
    /// can close it sooner, but doesn't change their views, so nobody's tap is dropped for a view that changed under it.
    /// </summary>
    private DateTimeOffset? _due;

    /// <summary>When everyone here had answered, which starts the settle (5d default 5); null while someone hasn't.</summary>
    private DateTimeOffset? _allInAt;

    /// <summary>
    /// The answers to the open question, by member: an option's id, or for who rolls, a member. An answer stands if its phone goes; one for
    /// something not on offer counts for nothing.
    /// </summary>
    private readonly Dictionary<string, string> _votes = new(StringComparer.Ordinal);

    /// <summary>
    /// The key of a choice's pick in <see cref="_votes"/>: it's the leader's, whoever leads now, so a pick stands if the lead changes hands,
    /// and the new leader can change it. Not a member id.
    /// </summary>
    private const string LeaderPick = "\u0001leader";

    /// <summary>Who made the choice's pick in <see cref="_votes"/>: their phone shows it as theirs, and a new leader's as the party's.</summary>
    private string? _pickedBy;

    /// <summary>The check that won and is being rolled for, while the party picks who rolls (or breaks a tie over it).</summary>
    private SceneOption? _checking;

    /// <summary>What a tie-break is between, in order: options' ids, or members.</summary>
    private ImmutableArray<string> _tied = [];

    /// <summary>Who was away when the question opened, as the phones show it: the TV's list is live.</summary>
    private ImmutableHashSet<string> _awayAtOpen = [];

    private StoryTiming Tell => _timing.Story;

    /// <summary>The beat under way, as saved.</summary>
    private StorySave? Cursor => _doc.World?.Story;

    // ---- The beat on offer ----------------------------------------------------------------------------------------------

    /// <summary>The story's flags, as a set.</summary>
    private HashSet<string> StoryFlags() => _doc.World?.Flags.ToHashSet(StringComparer.Ordinal) ?? [];

    /// <summary>The beat on offer (<see cref="StoryRules.Available"/>), or null with no region, no arc or nothing left.</summary>
    private ArcBeat? OnOffer()
    {
        if (_region is null || _doc.World is not { } world || Lore.ArcFor(_region.Def.Id) is not { } arc)
            return null;
        return StoryRules.Available(arc, world.Flags.ToHashSet(StringComparer.Ordinal), world.Beats.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>The spot of <paramref name="beat"/> in the region, or null if it has none.</summary>
    private Tile? SpotOf(ArcBeat beat) => _region?.Def.Beats.Where(b => b.Beat == beat.Id).Select(b => (Tile?)b.At).FirstOrDefault();

    /// <summary>The beat of the arc with this id, or null.</summary>
    private ArcBeat? BeatNamed(string id) => _region is null ? null : Lore.ArcFor(_region.Def.Id)?.Beats.FirstOrDefault(b => b.Id == id);

    /// <summary>
    /// The beat on offer's marker (5d §2, default 12): at its spot, with the first of its opening scene's cast who isn't the narrator, and the
    /// "!" until its scene starts. Empty with nothing on offer or no spot for it; null for a pack with no region, which has no world.
    /// </summary>
    private ImmutableArray<StoryMarker> Markers()
    {
        if (OnOffer() is not { } beat || SpotOf(beat) is not { } spot)
            return [];
        var scenes = Lore.ScenesOf(beat.Id);
        var opening = SceneRun.Next(scenes, StoryFlags(), null, aftermath: false) ?? scenes.FirstOrDefault();
        var figure = opening?.Cast.Where(c => c != SceneDef.Narrator).Select(Lore.Character).FirstOrDefault(c => c is not null);
        var playing = _phase == TacticsPhase.Story && Cursor?.Beat == beat.Id;
        return [new StoryMarker(beat.Id, spot, figure is null ? null : $"characters.{figure.Preset}", playing ? null : "alert")];
    }

    /// <summary>What the party is out to do (5d §2): "Go to" the place of the beat on offer, by name. Its summary would be a spoiler.</summary>
    private string Objective() => OnOffer() is { Place: { } place } ? $"Go to {Lore.PlaceName(place) ?? place}" : "Explore the region";

    /// <summary>
    /// The leader's Interact button (5d §2): the word for the beat on offer, while the leader is within <see cref="StoryRules.Reach"/> legal
    /// steps of its spot; else null. A battle beat is still started by stepping into its stamp (overworld 5c), so it has none here.
    /// </summary>
    private string? InteractLabel()
    {
        if (_phase != TacticsPhase.Exploration || OnOffer() is not { } beat || beat.Kind == ArcBeat.Battle || SpotOf(beat) is not { } spot
            || !StoryRules.InReach(Rules.GroundFor(_region!.Def.Id)!.Terrain, _at, spot))
            return null;
        return beat.Kind switch
        {
            "discovery" => "Look around",
            "camp" => "Sit by the fire",
            _ => "Talk",
        };
    }

    /// <summary>The leader tapped Interact: checked again here, since the phone's button can be a step behind.</summary>
    private void Interact(Player player)
    {
        if (player.Profile?.Value != _lead || InteractLabel() is null)
            return;
        LeaveExploration();      // the party stops, every walking timer goes, and the place is saved
        _toasts.Clear();
        BeginBeat(OnOffer()!);
    }

    // ---- Playing a beat ---------------------------------------------------------------------------------------------------

    /// <summary>Starts <paramref name="beat"/> at its first scene that plays. A beat with none is simply done.</summary>
    private void BeginBeat(ArcBeat beat)
    {
        _phase = TacticsPhase.Story;
        var first = SceneRun.Next(Lore.ScenesOf(beat.Id), StoryFlags(), null, aftermath: false);
        if (first is null)
        {
            StoryLogged?.Invoke($"The beat '{beat.Id}' has no scene to play, so it's done at once.");
            FinishBeat(beat);
            return;
        }
        SetCursor(new StorySave(beat.Id, StorySave.Lead, first.Id, null, 0, _storySeq));
        EnterNode(first, first.Start);
    }

    /// <summary>Starts the scene the cursor names, after a gap or on a restart in one.</summary>
    private void BeginScene(SceneDef scene) => EnterNode(scene, scene.Start);

    /// <summary>
    /// Plays the node <paramref name="id"/> of <paramref name="scene"/> from its first line: its flags are set as it plays, and it's
    /// marked played, so no question leads back to it. Saved with the first line, in the same save as whatever chose this node.
    /// </summary>
    private void EnterNode(SceneDef scene, string id)
    {
        var node = scene.Node(id)!;
        AddFlags(node.Sets);
        var c = Cursor!;
        SetCursor(c with { Scene = scene.Id, Node = node.Id, Line = 0, Played = c.Played.Contains(node.Id) ? c.Played : c.Played.Add(node.Id), Roll = null });
        PlayLine(scene, node, 0);
    }

    /// <summary>
    /// Shows line <paramref name="index"/> of <paramref name="node"/>, saving the cursor on it, for its clip and a gap or a reading timer.
    /// The node's last line opens its question, if it has one, so the question shows beside the line that asks it (5d default 9).
    /// </summary>
    private void PlayLine(SceneDef scene, SceneNode node, int index)
    {
        _storySeq++;
        SetCursor(Cursor! with { Line = index, Seq = _storySeq });
        _doc = _doc with { World = _doc.World! with { StorySeq = _storySeq } };
        KeepWorld();
        Save();
        var line = node.Lines[index];
        var now = _ctx.Now;
        _lineDue = now + Tell.Line(Lore.ClipSeconds(line.Id), line.Text);
        _ctx.SetTimer(Timers.Line, Longest(_lineDue - now, TimeSpan.FromMilliseconds(1)));
        _telling = StoryStates.Lines;
        CloseQuestion();
        if (index == node.Lines.Length - 1 && !node.Options.IsEmpty)
            OpenQuestion(scene, node);
    }

    /// <summary>The line showing has run its time, or the leader skipped it: the next line, node or scene.</summary>
    private void LineDone()
    {
        if (CurrentScene() is not { } scene)
            return;
        var c = Cursor!;
        switch (_telling)
        {
            case StoryStates.Gap:
                BeginScene(scene);
                return;
            case StoryStates.Lines when scene.Node(c.Node!) is { } node:
                if (c.Line + 1 < node.Lines.Length)
                    PlayLine(scene, node, c.Line + 1);
                else if (node.Next is { } next)
                    EnterNode(scene, next);
                else if (node.End)
                    SceneDone(scene);
                else
                {
                    // A node whose question offers nothing (5d plan §8): its last line has played, and the scene ends with the beat.
                    StoryLogged?.Invoke($"The node '{node.Id}' of '{scene.Id}' offered nothing, so the beat '{c.Beat}' ends there.");
                    FinishBeat(BeatNamed(c.Beat)!);
                }
                return;
        }
        // Asking and the rest: the question has been heard, and its own clock closes it.
    }

    /// <summary>The scene ended: the next of the beat's that plays, after a gap, or else the beat is done.</summary>
    private void SceneDone(SceneDef scene)
    {
        var c = Cursor!;
        var next = SceneRun.Next(Lore.ScenesOf(c.Beat), StoryFlags(), scene.Id, aftermath: c.Stage == StorySave.After);
        if (next is null)
        {
            FinishBeat(BeatNamed(c.Beat)!);
            return;
        }
        SetCursor(c with { Scene = next.Id, Node = null, Line = 0, Played = [], Roll = null });
        KeepWorld();
        Save();
        ShowGap();
    }

    private void ShowGap()
    {
        _telling = StoryStates.Gap;
        _lineDue = _ctx.Now + Tell.SceneGap;
        _ctx.SetTimer(Timers.Line, Tell.SceneGap);
    }

    /// <summary>
    /// The beat is done (5d §2): its own flags are set, it joins the beats done, the cursor goes, and a checkpoint is kept. Then the party
    /// walks again.
    /// </summary>
    private void FinishBeat(ArcBeat beat)
    {
        EndTelling();
        var world = _doc.World!;
        _doc = _doc with
        {
            World = world with
            {
                Flags = [.. world.Flags.Concat(beat.Sets).Distinct(StringComparer.Ordinal)],
                Beats = world.Beats.Contains(beat.Id) ? world.Beats : world.Beats.Add(beat.Id),
                Story = null,
            },
        };
        KeepWorld();
        _lease.Checkpoint(_doc.ToJson(), $"After {BeatTitle(beat)}");
        ToExploration();
    }

    /// <summary>A beat's name for a checkpoint: its first scene's title, since a beat has none of its own.</summary>
    private string BeatTitle(ArcBeat beat) => Lore.ScenesOf(beat.Id).FirstOrDefault(s => s.Title.Length > 0)?.Title ?? beat.Id;

    /// <summary>Ends the scene's clocks and whatever question was open.</summary>
    private void EndTelling()
    {
        _ctx.CancelTimer(Timers.Line);
        CloseQuestion();
        _telling = StoryStates.Lines;
    }

    /// <summary>
    /// Back into the beat the cursor names, after Muster or a restart (5d §8): on the saved line, said again with a new
    /// <see cref="StoryView.Seq"/>; a question reopens, since votes aren't saved; a roll shows its result again, and is never made again.
    /// A cursor on a scene or node that's gone restarts the beat from its first scene, with a notice. A beat that's gone or done is dropped.
    /// </summary>
    private void ResumeStory()
    {
        var c = Cursor!;
        _storySeq = Math.Max(_storySeq, c.Seq);
        if (BeatNamed(c.Beat) is not { } beat || _doc.World!.Beats.Contains(beat.Id))
        {
            _doc = _doc with { World = _doc.World! with { Story = null } };
            Save();
            ToExploration();
            return;
        }
        _phase = TacticsPhase.Story;
        var scene = Lore.ScenesOf(beat.Id).FirstOrDefault(s => s.Id == c.Scene);
        var node = c.Node is null ? null : scene?.Node(c.Node);
        if (scene is null || (c.Node is not null && (node is null || c.Line < 0 || c.Line >= node.Lines.Length)))
        {
            Note(StoryChanged);
            SetCursor(null);
            BeginBeat(beat);
            return;
        }
        if (node is null)
            BeginScene(scene);
        else if (c.Roll is not null)
            ShowRoll();
        else
            PlayLine(scene, node, c.Line);
    }

    // ---- Questions ------------------------------------------------------------------------------------------------------------

    /// <summary>True while a question is open: asked, tied, or who rolls.</summary>
    private bool Questioning => _telling is StoryStates.Asking or StoryStates.TieBreak or StoryStates.PickRoller;

    /// <summary>
    /// Opens <paramref name="node"/>'s question as its last line starts: what's offered to this party now, given the flags, the party's
    /// tags and the nodes already played. With nothing offered, the line plays out and the beat ends (<see cref="LineDone"/>).
    /// </summary>
    private void OpenQuestion(SceneDef scene, SceneNode node)
    {
        _offered = SceneRun.Offered(node, StoryFlags(), PartyTags.Of(Party().Select(m => m.Character)), Cursor!.Played.ToHashSet(StringComparer.Ordinal));
        if (_offered.IsEmpty)
            return;
        Ask(StoryStates.Asking);
    }

    /// <summary>Puts a question to the party, or a tie-break or the pick of who rolls, with its clock in full from now.</summary>
    private void Ask(string state)
    {
        _telling = state;
        _awayAtOpen = [.. Party().Where(m => !m.Here).Select(m => m.Member)];
        StartQuestionClock();
    }

    /// <summary>How long the open question has (5d default 4).</summary>
    private TimeSpan QuestionLength() => _telling switch
    {
        StoryStates.TieBreak => Tell.TieBreak,
        StoryStates.PickRoller => Tell.Roller,
        _ when CurrentNode() is { Vote: true } => Tell.Vote,
        _ => Tell.Choice,
    };

    /// <summary>
    /// Sets the question's clock in full from now, never closing before the line that asks it has been heard. With nobody here to answer,
    /// it holds instead: no timer, until a phone comes back (5d §4).
    /// </summary>
    private void StartQuestionClock()
    {
        _allInAt = null;
        if (!Voters().Any())
        {
            Hold();
            return;
        }
        var due = _ctx.Now + QuestionLength();
        _due = due > _lineDue ? due : _lineDue;
        Reclose();
    }

    private void Hold()
    {
        (_due, _closeAt, _allInAt) = (null, null, null);
        _ctx.CancelTimer(Timers.Vote);
    }

    /// <summary>
    /// When the question closes now: at its own deadline, or once everyone here has answered, after the settle, but never before the line
    /// that asks it is heard. A change of answer in the settle doesn't move it; someone new who hasn't answered puts it back.
    /// </summary>
    private void Reclose()
    {
        if (_due is not { } due)
            return;
        var at = due;
        if (AllAnswered())
        {
            _allInAt ??= _ctx.Now;
            var settle = _allInAt.Value + Tell.Settle;
            settle = settle > _lineDue ? settle : _lineDue;
            at = settle < due ? settle : due;
        }
        else
            _allInAt = null;
        if (at != _closeAt)
            CloseAt(at);
    }

    private void CloseAt(DateTimeOffset at)
    {
        _closeAt = at;
        _ctx.SetTimer(Timers.Vote, Longest(at - _ctx.Now, TimeSpan.FromMilliseconds(1)));
    }

    /// <summary>Drops the open question, if any, its answers and its clock.</summary>
    private void CloseQuestion()
    {
        _ctx.CancelTimer(Timers.Vote);
        (_offered, _closeAt, _due, _allInAt, _checking, _tied) = ([], null, null, null, null, []);
        _votes.Clear();
        _pickedBy = null;
    }

    /// <summary>What can be answered: the options on offer, or who can roll.</summary>
    private ImmutableArray<string> Answers()
        => _checking is { Check: { } check } ? [.. Cards(check.Skill).Select(c => c.Member)] : [.. _offered.Select(o => o.Id)];

    private VoteTally Tally() => new(Answers(), _votes.ToImmutableDictionary(StringComparer.Ordinal));

    /// <summary>The answers that count, by member: those for something still on offer.</summary>
    private IEnumerable<KeyValuePair<string, string>> LiveVotes()
    {
        var answers = Answers();
        return _votes.Where(v => answers.Contains(v.Value));
    }

    /// <summary>True for a vote of the party's, or the pick of who rolls; false for a choice that's the leader's.</summary>
    private bool PartyQuestion => _checking is not null || CurrentNode() is { Vote: true };

    /// <summary>
    /// True when everyone who can answer has: everyone here with a character for a party's question (and at least one of them), the leader
    /// for a choice. A tie-break is decided the moment its decider answers, so it never settles.
    /// </summary>
    private bool AllAnswered()
    {
        if (_telling == StoryStates.TieBreak)
            return false;
        if (!PartyQuestion)
            return _votes.TryGetValue(LeaderPick, out var pick) && Answers().Contains(pick);
        var present = Voters().Select(p => p.Profile!.Value.Value).ToArray();
        return present.Length > 0 && VoteRules.AllIn(Tally(), present);
    }

    /// <summary>The first player here with a character, in seat order: who breaks a tie with no leader (spec §4.2).</summary>
    private string? FirstConnected() => _players.FirstOrDefault(p => _online.Contains(p.Id) && _doc.For(p.Profile!.Value) is not null)?.Profile!.Value.Value;

    /// <summary>Who decides alone now: the leader for a choice, the leader or else the first here for a tie-break; null for a party's question.</summary>
    private string? Decider() => _telling == StoryStates.TieBreak ? _lead ?? FirstConnected() : PartyQuestion ? null : _lead;

    /// <summary>
    /// Someone came or went, or got a character: an unanswerable question holds, the first phone back restarts it in full, and otherwise who
    /// has still to answer may have changed: a voter who's gone no longer holds up the close, and someone new can still vote.
    /// </summary>
    private void StoryPresenceChanged()
    {
        if (!Questioning)
            return;
        if (!Voters().Any())
        {
            if (_allInAt is null)
                Hold();         // else everyone had answered before they went: the settle closes it on what they said
        }
        else if (_due is null)
            StartQuestionClock();
        else
            Reclose();
    }

    /// <summary>
    /// An answer from a phone: a vote or the leader's choice (<c>vote {option}</c>), or a pick of who rolls (<c>pickRoller {member}</c>). Only
    /// someone here with a character answers, and only what's on offer counts. A tie-break is decided by its decider's answer at once.
    /// </summary>
    private void Answer(ProfileId profile, string? pick, bool roller)
    {
        var member = profile.Value;
        if (pick is null || !Questioning || roller != (_checking is not null) || !Voters().Any(p => p.Profile!.Value.Value == member))
            return;
        if (_telling == StoryStates.TieBreak)
        {
            if (member == Decider() && _tied.Contains(pick))
                Resolve(pick);
            return;
        }
        if ((!PartyQuestion && member != _lead) || !Answers().Contains(pick))
            return;
        _votes[PartyQuestion ? member : LeaderPick] = pick;
        if (!PartyQuestion)
            _pickedBy = member;
        Reclose();
    }

    /// <summary>
    /// The question's clock ran out, or its settle did. A party's question closes by <see cref="VoteRules.Close"/>: nobody answering takes the
    /// first option, or for who rolls the best bonus (5d default 6, the user's decision 2). A choice takes the leader's pick, or the first
    /// option with no check (5d default 3). A tie-break nobody answers takes the first tied.
    /// </summary>
    private void QuestionDue()
    {
        if (!Questioning || CurrentNode() is null)
            return;
        switch (_telling)
        {
            case StoryStates.TieBreak:
                Resolve(_tied[0]);
                return;
            case StoryStates.Asking when !PartyQuestion:
                Resolve(_votes.TryGetValue(LeaderPick, out var pick) && Answers().Contains(pick)
                    ? pick
                    : SceneRun.Unanswered(_offered, vote: false).Id);
                return;
        }
        var unanswered = _checking is { Check: { } check }
            ? (Func<ImmutableArray<string>, string>)(_ => SkillCheck.Best(Cards(check.Skill).Select(c => (c.Member, c.Bonus))))
            : _ => SceneRun.Unanswered(_offered, vote: true).Id;
        switch (VoteRules.Close(Tally(), _lead, FirstConnected(), unanswered))
        {
            case Won won:
                Resolve(won.Option);
                break;
            case Tied tied:
                _tied = tied.Options;
                Ask(StoryStates.TieBreak);
                break;
        }
    }

    /// <summary>The question is settled on <paramref name="answer"/>: an option, or who rolls.</summary>
    private void Resolve(string answer)
    {
        if (_checking is { } check)
            Roll(check, answer);
        else
            Decide(_offered.Single(o => o.Id == answer));
    }

    /// <summary>
    /// The question settled on <paramref name="option"/>: its flags are set and the scene goes where it leads, in one save. A check goes to
    /// the party first, to pick who rolls; its flags are set with the roll.
    /// </summary>
    private void Decide(SceneOption option)
    {
        var (scene, beat) = (CurrentScene()!, BeatNamed(Cursor!.Beat)!);
        if (option.Check is not null)
        {
            var offered = _offered;
            CloseQuestion();
            (_offered, _checking) = (offered, option);
            Ask(StoryStates.PickRoller);
            return;
        }
        CloseQuestion();
        AddFlags(SceneRun.Sets(option, beat));
        EnterNode(scene, SceneRun.Target(option));
    }

    // ---- Skill checks ---------------------------------------------------------------------------------------------------------

    /// <summary>Who can roll <paramref name="skill"/>, in member order: everyone in the party with a character, here or not, and their bonus.</summary>
    private IEnumerable<(string Member, Player Player, CharacterRecord Character, int Bonus, bool Here)> Cards(string skill)
        => Party().Select(m => (m.Member, m.Player, m.Character, SkillCheck.Bonus(m.Character, skill, Rules), m.Here));

    /// <summary>
    /// Rolls <paramref name="option"/>'s check for <paramref name="member"/> with the game's die (5d §6), and saves the roll with the node
    /// it leads to and the option's flags, in one save, so a restart can't roll again. Then the result shows.
    /// </summary>
    private void Roll(SceneOption option, string member)
    {
        var check = option.Check!;
        var bonus = SkillCheck.Bonus(_doc.Members[member].Character, check.Skill, Rules);
        var die = _ctx.Random.Next(1, 21);
        var result = SkillCheck.Roll(die, bonus, check.Dc);
        AddFlags(SceneRun.Sets(option, BeatNamed(Cursor!.Beat)!));
        SetCursor(Cursor! with
        {
            Node = SceneRun.Target(option, result.Success), Line = 0,
            Roll = new StoryRoll(member, option.Id, check.Skill, check.Dc, die, bonus, result.Success),
        });
        KeepWorld();
        Save();
        ShowRoll();
    }

    private void ShowRoll()
    {
        CloseQuestion();
        _telling = StoryStates.Rolled;
        _lineDue = _ctx.Now + Tell.Reveal;
        _ctx.SetTimer(Timers.Line, Tell.Reveal);
    }

    // ---- Inputs and timers ----------------------------------------------------------------------------------------------------

    private void StoryInput(Player player, ProfileId profile, CustomInput input)
    {
        switch (input.Action)
        {
            case "skip":
                Skip(profile, input.Data);
                break;
            case "vote":
                Answer(profile, Str(input.Data, "option"), roller: false);
                break;
            case "pickRoller":
                Answer(profile, Str(input.Data, "member"), roller: true);
                break;
        }
    }

    /// <summary>
    /// The leader skips the line showing (5d default 2). The phone names the line it meant, so a double tap can't skip the next one as well;
    /// the room drops a tap on the old view anyway. The question's line can't be skipped: it's the question.
    /// </summary>
    private void Skip(ProfileId profile, JsonElement? data)
    {
        if (profile.Value != _lead || _telling != StoryStates.Lines || CurrentLine() is not { } line)
            return;
        if (Str(data, "line") is { } meant && meant != line.Id)
            return;
        _ctx.CancelTimer(Timers.Line);
        LineDone();
    }

    private void StoryTimer(string key)
    {
        switch (key)
        {
            case Timers.Line when _telling == StoryStates.Rolled:
                EnterNode(CurrentScene()!, Cursor!.Node!);
                break;
            case Timers.Line:
                LineDone();
                break;
            case Timers.Vote:
                QuestionDue();
                break;
        }
    }

    // ---- State ----------------------------------------------------------------------------------------------------------------

    private void SetCursor(StorySave? cursor) => _doc = _doc with { World = _doc.World! with { Story = cursor } };

    private void AddFlags(IEnumerable<string> flags)
    {
        var world = _doc.World!;
        var all = world.Flags.Concat(flags).Distinct(StringComparer.Ordinal).ToImmutableArray();
        if (all.Length != world.Flags.Length)
            _doc = _doc with { World = world with { Flags = all } };
    }

    private SceneDef? CurrentScene() => Cursor is { Scene: { } id } c ? Lore.ScenesOf(c.Beat).FirstOrDefault(s => s.Id == id) : null;

    private SceneNode? CurrentNode() => Cursor is { Node: { } id } ? CurrentScene()?.Node(id) : null;

    private SceneLine? CurrentLine() => CurrentNode() is { } node && Cursor!.Line < node.Lines.Length ? node.Lines[Cursor.Line] : null;

    /// <summary>The session's players who have a character, in member order, here or not: the party a scene's tags and checks look at (5d default 8).</summary>
    private IEnumerable<(string Member, Player Player, CharacterRecord Character, bool Here)> Party()
        => Walkers().Select(w => (w.Player.Profile!.Value.Value, w.Player, w.Character, w.Here));

    /// <summary>Who can answer a question: the players here who have a character.</summary>
    private IEnumerable<Player> Voters() => Party().Where(m => m.Here).Select(m => m.Player);

    // ---- Views ----------------------------------------------------------------------------------------------------------------

    private string StoryBanner() => CurrentScene()?.Title is { Length: > 0 } title ? title : "The story goes on";

    private StoryLineView LineView(SceneLine line)
    {
        var who = Lore.Character(line.Speaker);
        return new StoryLineView(line.Id, line.Speaker, who?.Name, who?.Preset, line.Mood, line.Text, who?.Role);
    }

    /// <summary>The scene for the TV, in the Story phase, with the live tally: the TV's alone (5d flag 7).</summary>
    private StoryView? StoryViewOf()
    {
        if (_phase != TacticsPhase.Story || CurrentScene() is not { } scene)
            return null;
        var line = _telling is StoryStates.Gap or StoryStates.Rolled || CurrentLine() is not { } showing ? null : LineView(showing);
        var asking = Questioning && _checking is null;
        return new StoryView(_storySeq, scene.Id, scene.Title, _telling, line, asking ? AskView() : null,
            _checking is not null || _telling == StoryStates.Rolled ? CheckView(scene) : null, _lineDue);
    }

    private string PlayerName(string member) => _players.FirstOrDefault(p => p.Profile?.Value == member)?.Name ?? member;

    /// <summary>The players whose characters are in the party and whose phones aren't here, by name.</summary>
    private ImmutableArray<string> Away(IEnumerable<string>? members = null)
        => [.. (members ?? Party().Where(m => !m.Here).Select(m => m.Member)).Select(PlayerName)];

    /// <summary>What's on offer now: everything, or only the tied during a tie-break.</summary>
    private IEnumerable<SceneOption> OptionsShown() => _telling == StoryStates.TieBreak ? _offered.Where(o => _tied.Contains(o.Id)) : _offered;

    private StoryAskView AskView()
    {
        var votes = LiveVotes().ToList();
        var of = PartyQuestion ? Voters().Select(p => p.Profile!.Value.Value).Union(votes.Select(v => v.Key)).Count() : 1;
        return new StoryAskView(PartyQuestion, votes.Count, of, Away(), _telling == StoryStates.TieBreak, Decider() is { } d ? PlayerName(d) : null,
            _closeAt, [.. OptionsShown().Select(o => new StoryOptionView(o.Id, o.Label, o.Check?.Skill, o.Check?.Dc, PartyQuestion
                ? [.. _players.Where(p => votes.Any(v => v.Key == p.Profile?.Value && v.Value == o.Id)).Select(p => p.Name)]
                : votes.Any(v => v.Value == o.Id) && Leader is { } leader ? [leader.Name] : []))]);
    }

    /// <summary>
    /// What <paramref name="member"/> has answered, as their phone shows it: their vote, or the choice they made as leader. A leader who
    /// took over a choice already made sees it as the party's (<see cref="PartyChoice"/>), not as theirs: they never made it.
    /// </summary>
    private string? PartyChoice() => !PartyQuestion && _votes.TryGetValue(LeaderPick, out var pick) && Answers().Contains(pick) ? pick : null;

    private string? MineOf(string member)
    {
        var key = PartyQuestion ? member : member == _pickedBy ? LeaderPick : null;
        return key is not null && _votes.TryGetValue(key, out var pick) && Answers().Contains(pick) ? pick : null;
    }

    /// <summary>Who can roll, as cards: their bonus, and where it comes from ("Proficient · CHA 14", as mockup 03).</summary>
    private IEnumerable<(string Member, Player Player, CharacterRecord Character, int Bonus, string Note, bool Here)> CardsWithNotes(string skill)
        => Party().Select(m =>
        {
            var sheet = SheetBuilder.ForCharacter(m.Character, Rules);
            var stat = SkillCheck.StatOf(skill)!.Value;
            var score = $"{stat.ToString().ToUpperInvariant()} {sheet.Score(stat)}";
            return (m.Member, m.Player, m.Character, SkillCheck.Bonus(sheet, skill), SkillCheck.Proficient(sheet, skill) ? $"Proficient · {score}" : score, m.Here);
        });

    /// <summary>The option a saved roll was for, found by its id and skill in the scene, since option ids repeat across nodes.</summary>
    private static SceneOption? RolledFor(SceneDef scene, StoryRoll roll)
        => scene.Nodes.SelectMany(n => n.Options).FirstOrDefault(o => o.Id == roll.Option && o.Check?.Skill == roll.Skill);

    private StoryCheckView CheckView(SceneDef scene)
    {
        var roll = _telling == StoryStates.Rolled ? Cursor!.Roll : null;
        var (skill, dc, title) = roll is not null
            ? (roll.Skill, roll.Dc, RolledFor(scene, roll)?.Label ?? roll.Skill)
            : (_checking!.Check!.Skill, _checking.Check.Dc, _checking.Label);
        var votes = LiveVotes().ToList();
        var tie = _telling == StoryStates.TieBreak;
        return new StoryCheckView(skill, dc, title,
            [.. CardsWithNotes(skill).Select(c => new StoryCardView(c.Member, c.Character.Name, c.Player.Name,
                HostSprite.Of(null, [$"{SheetBuilder.ClassTag}{c.Character.Class}"]), c.Bonus, c.Note, c.Here, votes.Count(v => v.Value == c.Member)))],
            roll?.Member, roll is null ? null : Rolls.Flat(roll.Die, roll.Bonus, roll.Dc), tie, tie && Decider() is { } d ? PlayerName(d) : null,
            roll is null ? _closeAt : null);
    }

    /// <summary>
    /// A phone in a scene: the line showing, or the question, or the check. None of it changes with what anyone else does, except when the
    /// question itself does: no tally, the countdown of the question's own clock and not of a settle, and who's away as of when it was asked.
    /// </summary>
    private CustomView StoryScreen(Player player, ProfileId profile)
    {
        var member = profile.Value;
        var scene = CurrentScene();
        if (Questioning && _checking is null)
            return Custom(Screens.Vote, new
            {
                party = PartyQuestion,
                canChoose = Decider() is { } decider ? decider == member : true,
                options = OptionsShown().Select(o => new { id = o.Id, label = o.Label, skill = o.Check?.Skill, dc = o.Check?.Dc }),
                mine = MineOf(member),
                chosen = PartyChoice(),
                tie = _telling == StoryStates.TieBreak,
                decider = Decider() is { } d ? PlayerName(d) : null,
                away = Away(_awayAtOpen),
            }) with { DeadlineUtc = _due };
        if (scene is not null && (_checking is not null || _telling == StoryStates.Rolled))
        {
            var check = CheckView(scene);
            var roll = _telling == StoryStates.Rolled ? Cursor!.Roll : null;
            return Custom(Screens.Check, new
            {
                skill = check.Skill,
                dc = check.Dc,
                title = check.Title,
                cards = CardsWithNotes(check.Skill).Select(c => new
                {
                    id = c.Member, name = c.Character.Name, player = c.Player.Name, cls = ClassName(c.Character), bonus = c.Bonus, note = c.Note,
                    here = !_awayAtOpen.Contains(c.Member),
                }),
                mine = MineOf(member),
                canChoose = _telling == StoryStates.PickRoller || (_telling == StoryStates.TieBreak && Decider() == member),
                tie = _telling == StoryStates.TieBreak,
                decider = _telling == StoryStates.TieBreak && Decider() is { } d2 ? PlayerName(d2) : null,
                tied = _telling == StoryStates.TieBreak ? _tied : [],
                roll = roll is null ? null : new { member = roll.Member, die = roll.Die, bonus = roll.Bonus, total = roll.Die + roll.Bonus, dc = roll.Dc, success = roll.Passed },
            }) with { DeadlineUtc = roll is null ? _due : null };
        }
        var line = _telling is StoryStates.Gap or StoryStates.Rolled ? null : CurrentLine();
        var who = line is null ? null : Lore.Character(line.Speaker);
        return Custom(Screens.Dialogue, new
        {
            title = scene?.Title,
            line = line?.Id,
            speaker = who?.Name,
            portrait = who?.Preset,
            mood = line?.Mood,
            text = line?.Text,
            canSkip = member == _lead && _telling == StoryStates.Lines && line is not null,
        });
    }
}
