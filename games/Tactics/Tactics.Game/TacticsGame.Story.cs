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
    /// <summary>Older saves predate the bounty flag; a completed main-story battle is already proof of victory.</summary>
    private void RestoreFordSettlement()
    {
        const string settled = "ch1_ford_settled";
        if (_doc.World is not { } world || world.Flags.Contains(settled)) return;
        var ford = Lore.ArcFor("ashen_ford");
        if (ford is null || !ford.Beats.Any(b => b.Kind == ArcBeat.Battle && b.Sets.Contains(settled) && world.Beats.Contains(b.Id))) return;
        _doc = _doc with { World = world with { Flags = world.Flags.Add(settled) } };
    }

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

        /// <summary>The chapter's card, at its end (5d default 13): <see cref="TacticsHostView.Chapter"/> says which.</summary>
        public const string Card = "card";
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
    private ImmutableArray<SceneOption> _shopOptions = [];

    /// <summary>When the open question closes, or null while it waits for a phone (nobody here can answer it). The TV's countdown.</summary>
    private DateTimeOffset? _closeAt;

    /// <summary>
    /// When the open question's own clock runs out, never before its line is heard, or null while it holds. The phones' countdown: a settle
    /// can close it sooner, but doesn't change their views, so nobody's tap is dropped for a view that changed under it.
    /// </summary>
    private DateTimeOffset? _due;

    /// <summary>
    /// How long the open question's clock ran when it started, in seconds: its length, or longer when the line asking it outlasts that; null
    /// while it holds. The TV reads its countdown by it (<see cref="StoryAskView.Span"/>), so it never has to guess the length.
    /// </summary>
    private double? _span;

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

    /// <summary>The arc of the region the party is in, or null.</summary>
    private Arc? StoryArc => _region is null ? null : Lore.ArcFor(_region.Def.Id);

    /// <summary>The beat on offer (<see cref="StoryRules.Available"/>), or null with no region, no arc or nothing left.</summary>
    private ArcBeat? OnOffer()
    {
        if (_doc.World is not { } world || StoryArc is not { } arc)
            return null;
        return StoryRules.Available(arc, world.Flags.ToHashSet(StringComparer.Ordinal), world.Beats.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>True once the arc's last beat is done: its chapter is over (spec §2).</summary>
    private bool ChapterOver => StoryArc is { } arc && _doc.World is { } world && StoryRules.ChapterDone(arc, world.Beats.ToHashSet(StringComparer.Ordinal));

    /// <summary>The spot of <paramref name="beat"/> in the region, or null if it has none.</summary>
    private Tile? SpotOf(ArcBeat beat) => _region?.Def.Beats.Where(b => b.Beat == beat.Id).Select(b => (Tile?)b.At).FirstOrDefault();

    /// <summary>The beat of the arc with this id, or null.</summary>
    private ArcBeat? BeatNamed(string? id) => StoryArc?.Beats.FirstOrDefault(b => b.Id == id);

    /// <summary>True while <paramref name="beat"/> is a battle waiting at its fight: its lead-in played, and it was lost, fled or never begun.</summary>
    private bool AtTheFight(ArcBeat beat) => Cursor is { Stage: StorySave.Fight } c && c.Beat == beat.Id;

    /// <summary>
    /// The beat on offer's marker (5d §2, default 12): at its spot, with the first of its opening scene's cast who isn't the narrator, and the
    /// "!" while it waits, which includes a fight to try again. Empty with nothing on offer or no spot for it, and during its scenes the
    /// figure stands with no "!".
    /// </summary>
    private ImmutableArray<StoryMarker> Markers()
    {
        if (OnOffer() is not { } beat || SpotOf(beat) is not { } spot)
            return [];
        var scenes = Lore.ScenesOf(beat.Id);
        var opening = SceneRun.Next(scenes, StoryFlags(), null, aftermath: false) ?? scenes.FirstOrDefault();
        var figure = opening?.Cast.Where(c => c != SceneDef.Narrator).Select(Lore.Character).FirstOrDefault(c => c is not null);
        var waiting = Cursor?.Beat != beat.Id || AtTheFight(beat) && _phase == TacticsPhase.Exploration;
        // While one of its scenes plays, the scene's cast stands in the figure's place (CastOf), so nobody is shown twice.
        var playing = _phase == TacticsPhase.Story && Cursor?.Beat == beat.Id && CurrentScene() is not null;
        return [new StoryMarker(beat.Id, spot, figure is null || playing ? null : figure.SpriteKey, waiting ? "alert" : null)];
    }

    /// <summary>
    /// Who stands in the world for the scene under way (the user's answer of 2026-11-01, item 21): the speakers who aren't the narrator and are
    /// in the lore, of the node playing and every node that can lead to it (<see cref="SceneCast.SpeakersBy"/>), so nobody is shown before a
    /// line could reveal them. They stand at <see cref="SceneSpot(SceneDef, ArcBeat, Terrain)"/>, facing the leader, on tiles
    /// <see cref="SceneCast.Place"/> chooses for the whole scene's cast, so each keeps their tile as the cast grows. The tiles depend only on
    /// what's saved (the scene, the party's tile and facing), so a restart puts everyone where they were. Empty outside the Story phase, in
    /// the gap before a scene, and in the chapter's card; so they leave as the scene ends.
    /// </summary>
    private ImmutableArray<CastView> CastOf()
    {
        if (_phase != TacticsPhase.Story || _telling is StoryStates.Card or StoryStates.Gap || _region is not { } region
            || CurrentScene() is not { } scene || Cursor?.Node is not { } node || (Cursor.Person is not null ? CursorBeat() : BeatNamed(scene.Beat)) is not { } beat)
            return [];
        var cast = SceneCast.Speakers(scene).Select(Lore.Character).OfType<LoreCharacter>().ToList();
        var by = SceneCast.SpeakersBy(scene, node).ToHashSet(StringComparer.Ordinal);
        if (!cast.Any(c => by.Contains(c.Id)))
            return [];
        var ground = WalkGround();
        var reached = WalkReach();      // on the ground as it is, things and painted props in the way
        var tiles = SceneCast.Place(ground, SceneSpot(scene, beat, ground), _at, _facing, [_at], cast.Count, t => ground.Map.InBounds(t) && reached.Contains(t));
        return [.. tiles.Select((t, i) => (Tile: t, Who: cast[i])).Where(c => by.Contains(c.Who.Id))
            .Select(c => new CastView(c.Who.Id, c.Who.Name, c.Who.SpriteKey, c.Tile, _at))];
    }

    /// <summary>
    /// Where <paramref name="scene"/> of <paramref name="beat"/> plays in the region: for a scene set in another place, that place's spot
    /// (<see cref="StoryRules.SpotFor"/>, where <see cref="TravelFor"/> takes the party), else its beat's. Where the party stands when
    /// there's no spot, or the leader isn't within <see cref="StoryRules.Reach"/> steps of it: a battle's aftermath, which plays where the
    /// fight ended, or a lead-in begun by stepping into the stamp, away from the "!".
    /// </summary>
    private Tile SceneSpot(SceneDef scene, ArcBeat beat, Terrain ground)
    {
        var spot = scene.Place is { } place && place != beat.Place
            ? StoryArc is { } arc ? StoryRules.SpotFor(arc, _region!.Def, place) : null
            : SpotOf(beat);
        return spot is { } at && StoryRules.InReach(ground, _at, at) ? at : _at;
    }

    /// <summary>
    /// What the party is out to do (5d §2): "Go to" the place of the beat on offer, by name, as its summary would be a spoiler. After the
    /// chapter's last beat, that the next chapter isn't here yet (5d default 13).
    /// </summary>
    private string Objective() => OnOffer() is { Place: { } place } ? $"Go to {Lore.PlaceName(place) ?? place}"
        : ChapterOver && StoryArc is { SideAdventure: false } ? $"Chapter {Numeral(_region!.Def.Chapter + 1)} isn't here yet"
        : "Explore the region";

    /// <summary>A chapter's number as the cards write it.</summary>
    internal static string Numeral(int n) => n switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", 10 => "X",
        _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// The leader's Interact button (5d §2): the word for the beat on offer, while the leader is within <see cref="StoryRules.Reach"/> legal
    /// steps of its spot; else null. A battle beat has "Begin", as stepping into its stamp does the same.
    /// </summary>
    private string? InteractLabel()
    {
        if (_phase != TacticsPhase.Exploration || OnOffer() is not { } beat || SpotOf(beat) is not { } spot
            || !StoryRules.InReach(WalkGround(), _at, spot))
            return null;
        return beat.Kind switch
        {
            ArcBeat.Battle => "Begin",
            "discovery" => "Look around",
            "camp" => "Sit by the fire",
            _ => "Talk",
        };
    }

    /// <summary>The leader tapped Interact for the beat: checked again here, since the phone's button can be a step behind.</summary>
    private void InteractBeat(Player player)
    {
        if (player.Profile?.Value != _lead || InteractLabel() is null)
            return;
        var beat = OnOffer()!;
        if (beat.Kind == ArcBeat.Battle)
        {
            EnterBattleBeat(beat);
            return;
        }
        LeaveExploration();      // the party stops, every walking timer goes, and the place is saved
        _toasts.Clear();
        BeginBeat(beat);
    }

    /// <summary>
    /// The party reached a battle beat, by its marker or by stepping into its stamp: its lead-in plays first, and then the fight. A lead-in
    /// plays once (5d default 10): a beat waiting at its fight, after a loss, goes straight to it.
    /// </summary>
    private void EnterBattleBeat(ArcBeat beat)
    {
        _toasts.Clear();
        if (AtTheFight(beat))
        {
            StartFight(beat.Encounter!, beat.Id);
            return;
        }
        LeaveExploration();
        BeginBeat(beat);
    }

    // ---- Playing a beat ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts <paramref name="beat"/> at its first scene that plays. A scene set in another place moves the party there first, behind the
    /// gap. A beat with no scene goes on to what follows it: its fight, or for any other, it's done.
    /// </summary>
    private void BeginBeat(ArcBeat beat, bool gap = false)
    {
        _phase = TacticsPhase.Story;
        var first = SceneRun.Next(Lore.ScenesOf(beat.Id), StoryFlags(), null, aftermath: false);
        if (first is null)
        {
            if (beat.Kind != ArcBeat.Battle)
                StoryLogged?.Invoke($"The beat '{beat.Id}' has no scene to play, so it's done at once.");
            SetCursor(new StorySave(beat.Id, StorySave.Lead, null, null, 0, _storySeq));
            StageDone();
            return;
        }
        SetCursor(new StorySave(beat.Id, StorySave.Lead, first.Id, null, 0, _storySeq));
        if (gap || TravelFor(first) is not null)
            Between(first);
        else
            BeginScene(first);
    }

    /// <summary>Starts the scene the cursor names, after a gap or on a restart in one: travelling to its place if it's set somewhere else.</summary>
    private void BeginScene(SceneDef scene)
    {
        Travel(scene);
        EnterNode(scene, scene.Start);
    }

    /// <summary>
    /// Plays the node <paramref name="id"/> of <paramref name="scene"/> from its first line: its flags are set as it plays, and it's
    /// marked played, so no question leads back to it. Saved with the first line, in the same save as whatever chose this node.
    /// </summary>
    private void EnterNode(SceneDef scene, string id)
    {
        var node = scene.Node(id)!;
        if (!Cursor!.Played.Contains(node.Id) && !CanPayGold(node.Gold)) { Note($"Needs {-(long)node.Gold} gold."); StageDone(); return; }
        if (!Cursor!.Played.Contains(node.Id)) ChangePartyGold(node.Gold);
        AddFlags(node.Sets);
        var c = Cursor!;
        SetCursor(c with { Scene = scene.Id, Node = node.Id, Line = 0, Played = c.Played.Contains(node.Id) ? c.Played : c.Played.Add(node.Id), Roll = null, Checking = null, CheckSeed = null, Helper = null });
        ThingNodeEntered(node, c.Roll);     // an interaction's outcome, saved with this node's first line; the roll that led here, for a search
        if (node.End && node.Lines.IsEmpty)
        { KeepWorld(); Save(); StageDone(); return; }
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
        if (_telling == StoryStates.Card)
        {
            (_card, _telling) = (null, StoryStates.Lines);
            ToExploration();
            return;
        }
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
                    // A node whose question offers nothing (5d plan §8): its last line has played, and the scene's stage ends there.
                    StoryLogged?.Invoke($"The node '{node.Id}' of '{scene.Id}' offered nothing, so the beat '{c.Beat}' goes on from there.");
                    StageDone();
                }
                return;
        }
        // Asking and the rest: the question has been heard, and its own clock closes it.
    }

    /// <summary>The scene ended: the next of its stage that plays, after a gap, or else what follows the stage.</summary>
    private void SceneDone(SceneDef scene)
    {
        if (CampTalk) { BeginSleep(); return; }
        var c = Cursor!;
        var next = c.Thing is not null || c.Person is not null ? null : SceneRun.Next(Lore.ScenesOf(c.Beat), StoryFlags(), scene.Id, aftermath: c.Stage == StorySave.After);
        if (next is null)
        {
            StageDone();
            return;
        }
        Between(next);
    }

    /// <summary>The gap before <paramref name="next"/>, saved with the cursor on it, so a restart starts it; the party travels as it begins.</summary>
    private void Between(SceneDef next)
    {
        SetCursor(Cursor! with { Scene = next.Id, Node = null, Line = 0, Played = [], Roll = null, Checking = null, CheckSeed = null, Helper = null });
        Travel(next);
        KeepWorld();
        Save();
        CloseQuestion();
        _telling = StoryStates.Gap;
        _lineDue = _ctx.Now + Tell.SceneGap;
        _ctx.SetTimer(Timers.Line, Tell.SceneGap);
    }

    /// <summary>
    /// A stage of the beat has no more scenes: a battle beat's lead-in goes to its fight, and anything else, a story beat or an aftermath,
    /// ends the beat. A battle beat is only ever done after its fight was won (review finding 1).
    /// </summary>
    private void StageDone()
    {
        if (CampTalk) { BeginSleep(); return; }
        if (Haggling) { FinishHaggle(); return; }
        var c = Cursor!;
        if (c.Person is not null) { FinishConversation(); return; }
        if (c.Thing is not null)
        {
            FinishInteraction();
            return;
        }
        var beat = BeatNamed(c.Beat)!;
        if (beat.Kind == ArcBeat.Battle && c.Stage != StorySave.After)
            ToTheFight(beat);
        else
            FinishBeat(beat);
    }

    /// <summary>
    /// The lead-in is over: the fight (5c), saved at stage <see cref="StorySave.Fight"/> first, so a loss, a flight or a fight that won't
    /// start leaves the beat waiting there, its stamp armed and its "!" back, and a retry skips the lead-in.
    /// </summary>
    private void ToTheFight(ArcBeat beat)
    {
        EndTelling();
        SetCursor(new StorySave(beat.Id, StorySave.Fight, null, null, 0, _storySeq));
        KeepWorld();
        Save();
        StartFight(beat.Encounter!, beat.Id);
    }

    /// <summary>
    /// The beat is done (5d §2): its own flags are set, it joins the beats done, the cursor goes, and a checkpoint is kept. Then a battle
    /// in the place the last scene was set starts at once (5d §3), the chapter ends if that was its last beat, or the party walks again.
    /// </summary>
    private void FinishBeat(ArcBeat beat)
    {
        if (beat.Kind == ArcBeat.Battle && Cursor?.Stage != StorySave.After)
        {
            ToTheFight(beat);      // never done without its fight won
            return;
        }
        // Where the party is as the beat ends: the place of the scene the saved cursor names, so a restart or a new session can't lose it
        // (review). A beat that ends with no scene of its own is in its own place.
        var here = CurrentScene() is { } last ? StoryRules.PlaceOf(last, beat) : beat.Place;
        EndTelling();
        _doc = CampaignExperience.CompleteBeat(_doc, beat, Rules);
        var world = _doc.World!;
        _doc = _doc with
        {
            World = world with
            {
                Flags = [.. world.Flags.Concat(beat.Sets).Distinct(StringComparer.Ordinal)],
                Beats = world.Beats.Contains(beat.Id) ? world.Beats : world.Beats.Add(beat.Id),
                Story = null,
                RestReady = true,
            },
        };
        PrepareQuestSave();
        KeepWorld();
        if (ChapterOver && StoryArc is { SideAdventure: false })
        {
            _lease.Checkpoint(_doc.ToJson(), $"End of Chapter {Numeral(StoryArc!.Chapter)}");
            ShowCard();
            return;
        }
        _lease.Checkpoint(_doc.ToJson(), $"After {BeatTitle(beat)}");
        if (OnOffer() is { } next && StoryRules.Chains(next, here))
        {
            BeginBeat(next, gap: true);      // "Dawn, then": straight into the assault
            return;
        }
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

    // ---- Another place, and the end of a chapter --------------------------------------------------------------------------------

    /// <summary>What <see cref="ExploreView.Arrived"/> says after the party travels to a scene set somewhere else: the TV fades.</summary>
    internal const string Travelled = "travel";

    /// <summary>
    /// Where the party goes for <paramref name="scene"/> (5d §3, Another place): a scene set in a place other than its beat's moves the party
    /// to the spot of the arc's first beat there (<see cref="StoryRules.SpotFor"/>), or the nearest tile to it the party can stand on and
    /// could have walked to. Null when it plays where the party is: its beat's own place, no spot there, or the party already on it.
    /// </summary>
    private Tile? TravelFor(SceneDef scene)
    {
        if (_region is not { } region || scene.Place is not { } place || BeatNamed(scene.Beat) is not { } beat || place == beat.Place
            || StoryArc is not { } arc || StoryRules.SpotFor(arc, region.Def, place) is not { } spot)
            return null;
        var ground = WalkGround();
        var reached = WalkReach();      // with the doors and gates as they are now (interactables design §2.4)
        // Clear of every stamp a battle still to come could arm: mid-beat nothing is armed yet, and the beat's outcome can arm one (review).
        var stamps = PendingStamps();
        var to = BattleStage.Standable(ground, spot, [spot, _at], t => ground.Map.InBounds(t) && reached.Contains(t) && !stamps.Any(st => st.Holds(t)));
        return to is { } tile && tile != _at ? tile : null;
    }

    /// <summary>Where the arc's battle beats not yet done stand in the region: any of them may arm when the beat under way ends.</summary>
    private List<BattleStage> PendingStamps()
    {
        var done = _doc.World?.Beats ?? [];
        return [.. (StoryArc?.Beats ?? []).Where(b => b.Kind == ArcBeat.Battle && !done.Contains(b.Id) && b.Encounter is not null)
            .Select(b => Rules.Encounters.TryGetValue(b.Encounter!, out var def) ? StageFor(def.Map) : null)
            .OfType<BattleStage>()];
    }

    /// <summary>Moves the party for <paramref name="scene"/>, if it's set somewhere else: a teleport, so a new epoch, and the TV fades.</summary>
    private void Travel(SceneDef scene)
    {
        if (TravelFor(scene) is not { } to)
            return;
        (_at, _facing, _arrived) = (to, new Tile(to.X + 1, to.Y), Travelled);
        _epoch++;
        _legs = [];
    }

    /// <summary>The chapter that just ended, while its card shows; null otherwise.</summary>
    private string? _card;

    /// <summary>The chapter's last beat is done (5d default 13): its card shows for a while, once, and then the party walks free.</summary>
    private void ShowCard()
    {
        _phase = TacticsPhase.Story;
        var arc = StoryArc!;
        _card = $"Chapter {Numeral(arc.Chapter)} · {arc.Title}";
        _telling = StoryStates.Card;
        _lineDue = _ctx.Now + Tell.Card;
        _ctx.SetTimer(Timers.Line, Tell.Card);
    }

    // ---- Coming back --------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Back into the beat the cursor names, after Muster, Results or a restart (5d §8): on the saved line, said again with a new
    /// <see cref="StoryView.Seq"/>; a question reopens, since votes aren't saved, unless it was decided for a check, which goes back to the
    /// pick of who rolls (<see cref="StorySave.Checking"/>); a roll shows its result again, and is never made again. A
    /// beat at its fight waits there, in the world. A beat won goes on to its aftermath. A beat that's gone, a scene or node that's gone, or a
    /// scene that no longer plays with the flags as they are restarts its stage from the beginning with a notice (or is dropped, for a
    /// beat that's gone); a battle beat is never marked done by any of it.
    /// </summary>
    private void ResumeStory()
    {
        if (Camp is not null) { ResumeCamp(); return; }
        if (_doc.World?.Shop is not null) { ResumeShop(); return; }
        // Found items the party was voting on, or that wait with nothing else under way: the vote, asked again.
        if (_doc.World?.Loot is { } loot && (loot.Voting || Cursor is null or { Stage: StorySave.Fight })) { ResumeLoot(); return; }
        var c = Cursor!;
        _storySeq = Math.Max(_storySeq, c.Seq);
        if (c.Person is not null) { ResumeConversation(c); return; }
        if (c.Thing is { } thing)
        {
            ResumeInteraction(c, thing);
            return;
        }
        var beat = BeatNamed(c.Beat);
        var wrongStage = beat is not null && c.Stage != StorySave.Lead && beat.Kind != ArcBeat.Battle;
        if (beat is null || wrongStage || _doc.World!.Beats.Contains(beat.Id))
        {
            if (beat is null || wrongStage)
                Note(StoryChanged);
            _doc = _doc with { World = _doc.World! with { Story = null } };
            Save();
            ToExploration();
            return;
        }
        if (c.Stage == StorySave.Fight)
        {
            ToExploration();        // its stamp is armed and its "!" up: the party walks back to it
            return;
        }
        _phase = TacticsPhase.Story;
        var after = c.Stage == StorySave.After;
        var scene = Lore.ScenesOf(beat.Id).FirstOrDefault(s => s.Id == c.Scene && s.After == after);
        var node = c.Node is null ? null : scene?.Node(c.Node);
        var gone = c.Scene is not null && (scene is null || (c.Node is not null && (node is null || c.Line < 0 || c.Line >= node.Lines.Length))
            || (c.Node is null && !SceneRun.Plays(scene!, StoryFlags())));
        if (gone)
            Note(StoryChanged);
        if (gone || c.Scene is null)
        {
            if (!after)
            {
                BeginBeat(beat);
                return;
            }
            // The aftermath, from its first scene that plays; with none, the beat is done.
            SetCursor(c with { Scene = null, Node = null, Line = 0, Played = [], Roll = null, Checking = null, CheckSeed = null, Helper = null });
            if (SceneRun.Next(Lore.ScenesOf(beat.Id), StoryFlags(), null, aftermath: true) is { } first)
                Between(first);
            else
                FinishBeat(beat);
            return;
        }
        if (node is null)
            BeginScene(scene!);
        else if (c.Roll is not null)
            ShowRoll();
        else
            PlayLine(scene!, node, c.Line);
    }

    // ---- Questions ------------------------------------------------------------------------------------------------------------

    /// <summary>True while a question is open: asked, tied, or who rolls.</summary>
    private bool Questioning => _telling is StoryStates.Asking or StoryStates.TieBreak or StoryStates.PickRoller;

    /// <summary>
    /// Opens <paramref name="node"/>'s question as its last line starts: what's offered to this party now, given the flags, the party's
    /// tags and the nodes already played. With nothing offered, the line plays out and the beat ends (<see cref="LineDone"/>). A question
    /// already decided for a check (<see cref="StorySave.Checking"/>, on a restart) goes straight back to the pick of who rolls; if that
    /// check is no longer on offer, the question is asked again.
    /// </summary>
    private void OpenQuestion(SceneDef scene, SceneNode node)
    {
        var c = Cursor!;
        _offered = SceneRun.Offered(node, StoryFlags(), PartyTags.Of(Party().Select(m => m.Character)), c.Played.ToHashSet(StringComparer.Ordinal));
        _shopOptions = _offered;
        _offered = [.. _offered.Where(o => CanPayGold(o.Gold))];
        if (c.Checking is { } decided)
        {
            SetCursor(c with { Checking = null });
            if (_offered.FirstOrDefault(o => o.Id == decided && o.Check is not null) is { } option)
            {
                Decide(option);
                return;
            }
        }
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
    private TimeSpan QuestionLength() => CampVote || EntranceVote ? TimeSpan.FromSeconds(20) : LootVote && _telling == StoryStates.Asking ? LootVoteLength : _telling switch
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
        _span = (_due.Value - _ctx.Now).TotalSeconds;
        Reclose();
    }

    private void Hold()
    {
        (_due, _closeAt, _allInAt) = (null, null, null);
        _span = null;
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
        ResetQuestion();
    }

    private void ResetQuestion()
    {
        _shopOptions = [];
        (_offered, _closeAt, _due, _allInAt, _checking, _tied) = ([], null, null, null, null, []);
        _span = null;
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
        if (Trading && Camp is null) return;
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
        if (CampVote) { RestVoteDue(); return; }
        if (EntranceVote) { EntranceVoteDue(); return; }
        if (LootVote) { LootVoteDue(); return; }      // only the players here count (USER DECISION 1)
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
    /// the party first, to pick who rolls, saved as decided (<see cref="StorySave.Checking"/>), so a restart comes back to the pick and
    /// never reopens the question (review); its flags are set with the roll.
    /// </summary>
    private void Decide(SceneOption option)
    {
        if (LootVote) { TakeLoot(option.Id); return; }
        var (scene, beat) = (CurrentScene()!, CursorBeat());
        if (!CanPayGold(option.Gold)) { CloseQuestion(); OpenQuestion(scene, CurrentNode()!); return; }
        if (option.Check is not null)
        {
            var offered = _offered;
            CloseQuestion();
            (_offered, _checking) = (offered, option);
            SetCursor(Cursor! with { Checking = option.Id, CheckSeed = Cursor.CheckSeed ?? _ctx.Random.Next() });
            KeepWorld();
            Save();
            Ask(StoryStates.PickRoller);
            return;
        }
        CloseQuestion();
        ChangePartyGold(option.Gold);
        AddFlags(SceneRun.Sets(option, beat));
        ConversationAction(option);
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
        if (!CanPayGold(option.Gold)) { CloseQuestion(); OpenQuestion(CurrentScene()!, CurrentNode()!); return; }
        var check = option.Check!;
        var seed = Cursor!.CheckSeed ?? 0;
        var helper = !check.Group && Cursor.Helper != member && EligibleHelper(Cursor.Helper, check.Skill) ? Cursor.Helper : null;
        var random = CheckRandom(seed, member);
        var first = random.Next(1, 21);
        var second = helper is null ? first : random.Next(1, 21);
        var bonus = SkillCheck.Bonus(_doc.Members[member].Character, check.Skill, Rules);
        var die = Math.Max(first, second);
        ImmutableArray<StoryGroupRoll> group = default;
        if (check.Group)
        {
            group = [.. Cards(check.Skill).Where(c => c.Here).Select(c =>
            {
                var d = CheckRandom(seed, c.Member).Next(1, 21);
                return new StoryGroupRoll(c.Member, d, c.Bonus, d + c.Bonus >= check.Dc);
            })];
            if (group.IsEmpty) return;
            die = group.Count(r => r.Passed);
            bonus = 0;
        }
        var result = SkillCheck.Roll(die, bonus, check.Group ? (group.Length + 1) / 2 : check.Dc);
        ChangePartyGold(option.Gold);
        AddFlags(SceneRun.Sets(option, CursorBeat()));
        ConversationAction(option);
        SetCursor(Cursor! with
        {
            Node = SceneRun.Target(option, result.Success), Line = 0,
            Roll = new StoryRoll(check.Group ? "party" : member, option.Id, check.Skill, check.Dc, die, bonus, result.Success, helper, helper is null ? default : [first, second], group), Checking = null,
        });
        KeepWorld();
        Save();
        ShowRoll();
    }

    private static Random CheckRandom(int seed, string member)
    {
        // Stable across processes; string.GetHashCode is deliberately randomized by the runtime.
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{seed}:{member}"));
        return new Random(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(hash));
    }

    private bool EligibleHelper(string? member, string skill)
        => member is not null && Party().Any(m => m.Member == member && m.Here
            && SkillCheck.Proficient(SheetBuilder.ForCharacter(m.Character, Rules), skill));

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
            case "help" when _telling == StoryStates.PickRoller && _checking?.Check is { Group: false } check:
                if (EligibleHelper(profile.Value, check.Skill))
                {
                    SetCursor(Cursor! with { Helper = profile.Value });
                    KeepWorld(); Save();
                }
                break;
            case "groupRoll" when _telling == StoryStates.PickRoller && _checking?.Check is { Group: true }:
                if (Party().Any(m => m.Member == profile.Value && m.Here)) Roll(_checking, profile.Value);
                break;
            case "pickRoller" when _checking?.Check?.Group != true:
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
        {
            _doc = _doc with { World = world with { Flags = all } };
            if (_phase == TacticsPhase.Exploration) ArmMobs();
        }
    }

    private SceneDef? CurrentScene() => Camp is { Stage: "vote" or "talk" } ? CampScene() : LootVote ? LootScene() : Haggling ? HaggleScene() : Cursor is { Scene: { } id } c
        ? c.Person is not null ? Lore.Scene(id) : c.Thing is { } thing ? ThingPlan(thing)?.Scene is { } own && own.Id == id ? own : null : Lore.ScenesOf(c.Beat).FirstOrDefault(s => s.Id == id)
        : null;

    /// <summary>The beat the cursor plays, or for an interaction, a beat of its own with no choices and no flags.</summary>
    private ArcBeat CursorBeat() => Camp is not null || LootVote || Haggling || Cursor!.Thing is not null || Cursor.Person is not null ? new ArcBeat("", "interaction", null, null, [], [], []) : BeatNamed(Cursor.Beat)!;

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
        return new StoryLineView(line.Id, line.Speaker, who?.Name, who?.PortraitKey, line.Mood, line.Text, who?.Role);
    }

    /// <summary>The scene for the TV, in the Story phase, with the live tally: the TV's alone (5d flag 7).</summary>
    private StoryView? StoryViewOf()
    {
        if (_phase != TacticsPhase.Story || CurrentScene() is not { } scene)
            return null;
        var line = _telling is StoryStates.Gap or StoryStates.Rolled || CurrentLine() is not { } showing ? null : LineView(showing);
        var asking = Questioning && _checking is null;
        return new StoryView(_storySeq, scene.Id, scene.Title, _telling, line, asking ? AskView() : null,
            _checking is not null || _telling == StoryStates.Rolled ? CheckView(scene) : null, _lineDue, _lease.Info.Id);
    }

    private string PlayerName(string member) => _players.FirstOrDefault(p => p.Profile?.Value == member)?.Name ?? member;

    /// <summary>The players whose characters are in the party and whose phones aren't here, by name.</summary>
    private ImmutableArray<string> Away(IEnumerable<string>? members = null)
        => [.. (members ?? Party().Where(m => !m.Here).Select(m => m.Member)).Select(PlayerName)];

    /// <summary>What's on offer now: everything, or only the tied during a tie-break.</summary>
    private IEnumerable<SceneOption> OptionsShown() => _telling == StoryStates.TieBreak ? _offered.Where(o => _tied.Contains(o.Id)) : _shopOptions.IsEmpty ? _offered : _shopOptions;

    private object OptionPhone(SceneOption option)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(new { id = option.Id, label = option.Label, skill = option.Check?.Skill, dc = option.Check?.Dc }, ArcJson.Options)!.AsObject();
        if (option.Gold < 0)
        {
            node["can"] = CanPayGold(option.Gold);
            node["needs"] = $"needs {-(long)option.Gold} gold";
        }
        return node;
    }

    private StoryAskView AskView()
    {
        var votes = LiveVotes().ToList();
        var of = PartyQuestion ? Voters().Select(p => p.Profile!.Value.Value).Union(votes.Select(v => v.Key)).Count() : 1;
        return new StoryAskView(PartyQuestion, votes.Count, of, Away(), _telling == StoryStates.TieBreak, Decider() is { } d ? PlayerName(d) : null,
            _closeAt, [.. OptionsShown().Select(o => new StoryOptionView(o.Id, !CanPayGold(o.Gold) ? $"{o.Label} (needs {-(long)o.Gold} gold)" : o.Label, o.Check?.Skill, o.Check?.Dc, PartyQuestion
                ? [.. _players.Where(p => votes.Any(v => v.Key == p.Profile?.Value && v.Value == o.Id)).Select(p => p.Name)]
                : votes.Any(v => v.Value == o.Id) && Leader is { } leader ? [leader.Name] : []))], _closeAt is null ? null : _span);
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

    /// <summary>True while the check is an active Search's: its DC is the lowest of what it looks for, and shows nowhere (interactables design §7.4).</summary>
    private bool SecretDc => Cursor?.Thing is { Searching: true };

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
        return new StoryCheckView(skill, SecretDc ? null : dc, title,
            [.. CardsWithNotes(skill).Select(c => new StoryCardView(c.Member, c.Character.Name, c.Player.Name,
                HostSprite.Of(null, [$"{SheetBuilder.ClassTag}{c.Character.Class}"]), c.Bonus, c.Note, c.Here, votes.Count(v => v.Value == c.Member), ClassName(c.Character)))],
            roll?.Member, roll is null ? null : SecretDc ? Rolls.Flat(roll.Die, roll.Bonus, roll.Dc) with { Vs = 0 } : Rolls.Flat(roll.Die, roll.Bonus, roll.Group.IsDefaultOrEmpty ? roll.Dc : (roll.Group.Length + 1) / 2), tie, tie && Decider() is { } d ? PlayerName(d) : null,
            roll is null ? _closeAt : null, roll is null && _closeAt is not null ? _span : null,
            Helper: (roll is null ? Cursor?.Helper : roll.Helper) is { } h ? _doc.Members.GetValueOrDefault(h)?.Character.Name : null,
            GroupCheck: roll is null ? _checking!.Check!.Group : !roll.Group.IsDefaultOrEmpty,
            Group: roll?.Group ?? default);
    }

    /// <summary>
    /// A phone in a scene: the line showing, or the question, or the check. None of it changes with what anyone else does, except when the
    /// question itself does: no tally, the countdown of the question's own clock and not of a settle, and who's away as of when it was asked.
    /// Each says <c>vip</c>, so the VIP's phone has its End session there too (the server takes it in a scene, c23a42f).
    /// </summary>
    private CustomView StoryScreen(Player player, ProfileId profile)
    {
        var member = profile.Value;
        var scene = CurrentScene();
        if (Questioning && _checking is null)
        {
            var vote = JsonSerializer.SerializeToNode(new
            {
                party = PartyQuestion,
                canChoose = Decider() is { } decider ? decider == member : true,
                options = OptionsShown().Select(OptionPhone),
                mine = MineOf(member),
                chosen = PartyChoice(),
                tie = _telling == StoryStates.TieBreak,
                decider = Decider() is { } d ? PlayerName(d) : null,
                away = Away(_awayAtOpen),
                vip = IsVip(player),
                difficulty = CampaignDifficulty.ToString(),
            }, ArcJson.Options)!.AsObject();
            // The loot vote says what it asks, and which of the votes it is, so each item's is a new question (USER DECISION 1).
            if (LootVote && scene is not null)
            {
                vote["question"] = scene.Title;
                vote["vote"] = _doc.World!.Loot!.Seq;
            }
            return Custom(Screens.Vote, vote) with { DeadlineUtc = _due };
        }
        if (scene is not null && (_checking is not null || _telling == StoryStates.Rolled))
        {
            var check = CheckView(scene);
            var roll = _telling == StoryStates.Rolled ? Cursor!.Roll : null;
            return Custom(Screens.Check, new
            {
                skill = check.Skill,
                dc = check.Dc,      // null for a search
                title = check.Title,
                cards = CardsWithNotes(check.Skill).Select(c => new
                {
                    id = c.Member, name = c.Character.Name, player = c.Player.Name, cls = ClassName(c.Character), bonus = c.Bonus, note = c.Note,
                    here = !_awayAtOpen.Contains(c.Member),
                }),
                help = new { can = roll is null && _checking?.Check is { Group: false } ck && EligibleHelper(member, ck.Skill),
                    member = roll is null ? Cursor?.Helper : roll.Helper, name = check.Helper },
                group = check.GroupCheck,
                mine = MineOf(member),
                canChoose = _telling == StoryStates.PickRoller || (_telling == StoryStates.TieBreak && Decider() == member),
                tie = _telling == StoryStates.TieBreak,
                decider = _telling == StoryStates.TieBreak && Decider() is { } d2 ? PlayerName(d2) : null,
                tied = _telling == StoryStates.TieBreak ? _tied : [],
                roll = roll is null ? null : new { member = roll.Member, die = roll.Die, bonus = roll.Bonus, total = roll.Die + roll.Bonus, dc = SecretDc ? (int?)null : roll.Dc, success = roll.Passed, dice = roll.Dice.IsDefault ? null : (int[]?)[.. roll.Dice], group = roll.Group.IsDefault ? null : roll.Group.Select(r => new { member = r.Member, name = _doc.Members[r.Member].Character.Name, die = r.Die, bonus = r.Bonus, success = r.Passed }) },
                vip = IsVip(player),
                difficulty = CampaignDifficulty.ToString(),
            }) with { DeadlineUtc = roll is null ? _due : null };
        }
        var line = _telling is StoryStates.Gap or StoryStates.Rolled ? null : CurrentLine();
        var who = line is null ? null : Lore.Character(line.Speaker);
        return Custom(Screens.Dialogue, new
        {
            title = scene?.Title ?? _card,
            line = line?.Id,
            speaker = who?.Name,
            portrait = who?.PortraitKey,
            mood = line?.Mood,
            text = line?.Text,
            canSkip = member == _lead && _telling == StoryStates.Lines && line is not null,
            vip = IsVip(player),
            difficulty = CampaignDifficulty.ToString(),
        });
    }
}
