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

    /// <summary>When the open question closes, or null while it waits for a phone (nobody here can answer it).</summary>
    private DateTimeOffset? _closeAt;

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

    /// <summary>
    /// Opens <paramref name="node"/>'s question as its last line starts: what's offered to this party now, given the flags, the party's
    /// tags and the nodes already played. With nothing offered, the line plays out and the beat ends (<see cref="LineDone"/>).
    /// </summary>
    private void OpenQuestion(SceneDef scene, SceneNode node)
    {
        _offered = SceneRun.Offered(node, StoryFlags(), PartyTags.Of(Party().Select(m => m.Character)), Cursor!.Played.ToHashSet(StringComparer.Ordinal));
        if (_offered.IsEmpty)
            return;
        _telling = StoryStates.Asking;
        StartQuestionClock(node);
    }

    /// <summary>
    /// Sets the question's clock in full from now, never closing before the line that asks it has been heard. With nobody here to answer,
    /// it holds instead: no timer, until a phone comes back (5d §4).
    /// </summary>
    private void StartQuestionClock(SceneNode node)
    {
        if (!Voters().Any())
        {
            _closeAt = null;
            _ctx.CancelTimer(Timers.Vote);
            return;
        }
        var length = node.Vote ? Tell.Vote : Tell.Choice;
        var due = _ctx.Now + length;
        CloseAt(due > _lineDue ? due : _lineDue);
    }

    private void CloseAt(DateTimeOffset at)
    {
        _closeAt = at;
        _ctx.SetTimer(Timers.Vote, Longest(at - _ctx.Now, TimeSpan.FromMilliseconds(1)));
    }

    /// <summary>Drops the open question, if any, and its clock.</summary>
    private void CloseQuestion()
    {
        _ctx.CancelTimer(Timers.Vote);
        (_offered, _closeAt) = ([], null);
    }

    /// <summary>Someone came or went, or got a character: an unanswerable question holds, and the first phone back restarts it in full.</summary>
    private void StoryPresenceChanged()
    {
        if (_telling != StoryStates.Asking || CurrentNode() is not { } node)
            return;
        if (!Voters().Any())
        {
            _closeAt = null;
            _ctx.CancelTimer(Timers.Vote);
        }
        else if (_closeAt is null)
            StartQuestionClock(node);
    }

    /// <summary>The question's clock ran out: it takes what nobody answering takes (5d default 3, the user's decision 2).</summary>
    private void QuestionDue()
    {
        if (_telling != StoryStates.Asking || CurrentNode() is not { } node || _offered.IsEmpty)
            return;
        Decide(SceneRun.Unanswered(_offered, node.Vote));
    }

    /// <summary>
    /// The question settled on <paramref name="option"/>: its flags are set and the scene goes where it leads, in one save. A check is
    /// rolled first.
    /// </summary>
    private void Decide(SceneOption option)
    {
        var (scene, beat) = (CurrentScene()!, BeatNamed(Cursor!.Beat)!);
        CloseQuestion();
        if (option.Check is not null)
        {
            Roll(option, SkillCheck.Best(Cards(option.Check.Skill).Select(card => (card.Member, card.Bonus))));
            return;
        }
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

    /// <summary>The scene for the TV, in the Story phase.</summary>
    private StoryView? StoryViewOf()
    {
        if (_phase != TacticsPhase.Story || CurrentScene() is not { } scene)
            return null;
        var line = _telling is StoryStates.Gap or StoryStates.Rolled || CurrentLine() is not { } showing ? null : LineView(showing);
        DateTimeOffset? until = _telling == StoryStates.Asking ? _closeAt : _lineDue;
        return new StoryView(_storySeq, scene.Id, scene.Title, _telling, line, null, null, until);
    }

    /// <summary>
    /// A phone in a scene: the line showing, the speaker and the scene's title, and for the leader alone, Skip. It changes with each line,
    /// and with nothing anyone else does.
    /// </summary>
    private CustomView StoryScreen(Player player, ProfileId profile)
    {
        var scene = CurrentScene();
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
            canSkip = profile.Value == _lead && _telling == StoryStates.Lines && line is not null,
        });
    }
}
