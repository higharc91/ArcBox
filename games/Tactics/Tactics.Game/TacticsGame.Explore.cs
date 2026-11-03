using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// The party in the world (overworld 5b): which region it's in, the tile it stands on and who leads it. All of it is ready from
/// the moment a session starts, whatever phase that lands in, so a TV joining mid-fight still learns the region. The live tile
/// is kept here and reaches the document only through <see cref="SaveWorld"/>.
/// <para>
/// Input is level-triggered with a lease (overworld 5b §1, §2), because a stop can be lost in more ways than one: the phone says which way
/// it's held, and repeats that to keep it held. A timer exists only while something needs it: <see cref="Timers.Step"/> only with a
/// direction held, <see cref="Timers.Lead"/> only with the pad on someone's screen, and <see cref="Timers.Save"/> only with a change to keep.
/// </para>
/// </summary>
public sealed partial class TacticsGame
{
    /// <summary>The region the party is in; null for a pack with no region, which plays as it did before the world.</summary>
    private WorldRegion? _region;

    private Tile _at;
    private Tile _facing;

    /// <summary>
    /// What <see cref="ExploreView.Epoch"/> says. <see cref="EnterWorld"/> seeds it; after that a teleport is just <c>_epoch++</c>
    /// (with <see cref="_legs"/> cleared), as only a difference matters to the TV.
    /// </summary>
    private int _epoch;

    /// <summary>How the party came to its tile when the last fight ended, for the TV: what <see cref="ExploreView.Arrived"/> says.</summary>
    private string? _arrived;

    /// <summary>Whoever leads now, by member id; null with nobody to lead.</summary>
    private string? _lead;

    /// <summary>
    /// The member whose session this is. <see cref="WorldSave.LastLeader"/> keeps this one, not whoever holds the lead at the
    /// moment, so a restart mid-handover still hands the lead back to them and the next session's rotation doesn't skip anyone.
    /// Null until someone is elected, which is what makes a session's rotation happen once.
    /// </summary>
    private string? _sessionLead;

    /// <summary>
    /// True from an election at Start until the first input. Every seat counts as online during Start, and the room says who's
    /// absent only after it returns, so until someone has actually spoken the election can still have picked an empty seat.
    /// </summary>
    private bool _electionOpen;

    /// <summary>The <see cref="WorldSave.LastLeader"/> an election at Start followed, to go back to if it's retracted.</summary>
    private string? _electedAfter;

    /// <summary>Members who went quiet while leading. They're passed over until their phone is heard from again.</summary>
    private readonly HashSet<string> _silent = [];

    /// <summary>When the leader's phone was last heard from.</summary>
    private DateTimeOffset _heardAt;

    /// <summary>The direction held, or null when the party's stopped.</summary>
    private WalkDir? _dir;

    /// <summary>When the hold runs out unless it's repeated.</summary>
    private DateTimeOffset _heldUntil;

    /// <summary>True while <see cref="_blocked"/> is by something the TV hasn't been told of: <see cref="ExploreView.Blocked"/> stays false.</summary>
    private bool _unseenWall;

    /// <summary>When the step under way ends: the next can't begin before this, however fast a thumb taps.</summary>
    private DateTimeOffset _stepDue;

    /// <summary>The held direction is against a wall. The ground doesn't change, so nothing is tried again until the direction does.</summary>
    private bool _blocked;

    /// <summary>A press or a change of direction came during a step, and hasn't been stepped yet: it goes when that step ends.</summary>
    private bool _owed;

    /// <summary>
    /// The thumb lifted while a step was <see cref="_owed"/>: a quick tap, which still takes its one step, at <see cref="_stepDue"/>, and
    /// then the party stops. Without this a tap faster than the walk would buzz the phone and do nothing.
    /// </summary>
    private bool _latched;

    /// <summary>When the party last stopped or the lead last changed hands, whichever is later: what <see cref="WalkTiming.TakebackAfter"/> counts from.</summary>
    private DateTimeOffset _settledAt = DateTimeOffset.MinValue;

    /// <summary>The handover's note on the TV, if it's still up, so the next handover replaces it rather than adding to it.</summary>
    private string? _leadNote;

    /// <summary>Whether the party has been told that the road goes on past the region's edge: once a session is enough.</summary>
    private bool _edgeSaid;

    /// <summary>How many steps a wall has refused this session: what <see cref="ExploreView.Bumps"/> says.</summary>
    private int _bumps;

    /// <summary>What the TV says when the party is stopped by the region's edge. The user's to reword.</summary>
    internal const string RoadGoesOn = "The road goes on, but not today.";

    private bool? _lastAlongX;
    private int _walkSeq;
    private ImmutableArray<WalkLeg> _legs = [];

    /// <summary>When a walk under way has to be saved by, or null with nothing waiting.</summary>
    private DateTimeOffset? _saveBy;

    /// <summary>
    /// When <see cref="Timers.Save"/> is set for, or null if it isn't. Kept here because the context's own deadline comes from a
    /// later read of the clock, so it never equals a time worked out here.
    /// </summary>
    private DateTimeOffset? _saveAt;

    private WalkTiming Walk => _timing.Walk;

    /// <summary>Who has the pad, if anyone.</summary>
    public Player? Leader => _lead is { } lead ? _players.FirstOrDefault(p => p.Profile?.Value == lead) : null;

    /// <summary>
    /// The inputs of the world and its story, never a battle's: one that arrives late, from a screen the fight has replaced, is dropped
    /// rather than read as a command the battle doesn't know.
    /// </summary>
    private static bool IsWorldAction(string action)
        => action is "walk" or "battles" or "backToWalk" or "endSession" or "interact" or "search" or "skip" or "vote" or "pickRoller" or "campJob" or "campNext" or "restPropose" or "shopService";

    // ---- Fights in the world (overworld 5c) ----------------------------------------------------------------------------

    /// <summary>What everyone is told when a fight the party walked into won't start. The party stands where it stopped and can try again.</summary>
    public const string FightWontStart = "Something went wrong starting that fight.";

    /// <summary>
    /// What everyone is told when the party is too big for the fight it walked into. The checks make every arc battle take a full party, so
    /// this is a guard, not a case anyone should meet.
    /// </summary>
    internal static string FightTooBig(EncounterDef encounter) => $"{encounter.Name} takes at most {encounter.Party.Max} heroes.";

    /// <summary>Where the battle map <paramref name="map"/> stands in the region, or null with no region or no stamp of it: a plain board.</summary>
    private BattleStage? StageFor(string map) => _region is { } region && Rules.MapFor(map) is { } battleMap ? BattleStage.Of(region, battleMap) : null;

    /// <summary>
    /// Where the battle <paramref name="save"/> is drawn, and lands: the stage it was saved with, while the region still stamps its map
    /// there (origin and height); a plain board if a repaint has moved or removed that stamp since (review), so the party ends it where it
    /// stood before the fight. A fight begun on a plain board stays on one (<see cref="BattleSave.Plain"/>), even if a repaint has stamped
    /// its map since. A battle saved before stages were kept takes the region's stamp, as it always did.
    /// </summary>
    private BattleStage? StageOf(BattleSave save)
    {
        if (save.Plain)
            return null;
        var live = StageFor(save.State.MapId);
        if (save.Stage is not { } saved)
            return live;
        return live is { } stage && stage.Origin == saved.Origin && stage.Height == saved.Height ? stage : null;
    }

    /// <summary>
    /// The fight on offer and where it stands: the arc's available beat (<see cref="StoryRules.Available"/>), if it's a battle whose
    /// encounter's map the region stamps. Every other stamp is ordinary ground, and so is this one once its beat is done. One beat is on
    /// offer at a time, so at most one stamp is armed, and it never has to name its encounter (5c default 2).
    /// </summary>
    private (ArcBeat Beat, BattleStage Stage)? Armed()
    {
        if (_region is null || _doc.World is not { } world || Lore.ArcFor(_region.Def.Id) is not { } arc)
            return null;
        // A beat whose fight is won counts as done here though its aftermath is still to play (5d §3): its stamp is ordinary ground, and
        // the party lands clear of whatever stamp follows it, as 5c did when the beat was done at the win.
        var beats = world.Beats.ToHashSet(StringComparer.Ordinal);
        if (world.Story is { Stage: StorySave.After } won)
            beats.Add(won.Beat);
        if (StoryRules.Available(arc, world.Flags.ToHashSet(StringComparer.Ordinal), beats) is not { Kind: ArcBeat.Battle, Encounter: { } encounter } beat
            || !Rules.Encounters.TryGetValue(encounter, out var def) || StageFor(def.Map) is not { } stage)
            return null;
        // A beat whose lead-in is under way isn't waiting at its stamp: only one at its fight, or not yet begun, is.
        if (world.Story is { Stage: StorySave.Lead } leading && leading.Beat == beat.Id)
            return null;
        return (beat, stage);
    }

    /// <summary>
    /// Starts <paramref name="encounter"/> where the party is, as the beat <paramref name="beat"/>: out of the walk, everyone who has a
    /// seat in the party (an absent member is given a character, and played by the AI), and companions up to the encounter's smallest
    /// party, as the battle list does. If it won't start, the party is back to walking where it stopped, with a notice.
    /// </summary>
    private void StartFight(string encounter, string? beat)
    {
        var def = Rules.Encounters.GetValueOrDefault(encounter);
        // Characters first, while the party is still walking: giving one out can settle the lead, and anything the lead arms has to go
        // with the walk (LeaveExploration), not outlive it into the battle.
        if (def is not null && _players.Count <= def.Party.Max)
            FillAbsentCharacters();
        LeaveExploration();
        _toasts.Clear();
        if (def is not null && _players.Count <= def.Party.Max
            && StartBattle(encounter, (ulong)_ctx.Random.NextInt64(), [.. _players.Select(p => p.Profile!.Value)], Companions(def.Party.Min), beat))
            return;
        Note(def is not null && _players.Count > def.Party.Max ? FightTooBig(def) : FightWontStart);
        ToExploration();
    }

    // ---- Coming into the world ----------------------------------------------------------------------------------------

    /// <summary>
    /// Puts the party where the document says, or at the region's start. A place that no longer makes sense goes to the start
    /// with a notice: the region was removed, or a repaint left the tile unstandable or cut off from the start.
    /// </summary>
    private void EnterWorld()
    {
        WorldEntering?.Invoke();
        var saved = _doc.World;
        var region = saved?.Region is { } id && Rules.RegionFor(id) is { } named ? named : Rules.Regions.Values.MinBy(r => r.Def.Chapter);
        if (region is null)
            return;
        _region = region;
        // Not 0: a server restart restarts Seq under a TV that may have stayed up, and to it only a new Epoch means "snap".
        _epoch = (int)(_ctx.Now.ToUnixTimeSeconds() % int.MaxValue);
        _storySeq = Math.Max(saved?.StorySeq ?? 0, saved?.Story?.Seq ?? 0);     // the TV's line number goes on from where it was
        _thingSeq = Math.Max(_thingSeq, saved?.ThingSeq ?? 0);                     // and so does its thing events' number
        var start = region.Def.Spawns.Start;
        if (saved is not null && saved.Region == region.Def.Id && CanStandOn(saved.At))
            (_at, _facing) = (saved.At, saved.Facing);
        else
        {
            (_at, _facing) = (start, new Tile(start.X + 1, start.Y));
            if (saved is not null)
                Note("The land has changed since you were last here, so the party starts again from the road's beginning.");
        }
    }

    private bool CanStandOn(Tile at)
    {
        var terrain = WalkGround();
        return terrain.Map.InBounds(at) && terrain.Walkable(at) && Rules.ReachFor(_region!.Def.Id)!.Contains(at);
    }

    private void Note(string text) => _notice = _notice is null ? text : $"{_notice} {text}";

    /// <summary>
    /// Takes one note back down, leaving any others up. Notes are joined by spaces, so only a whole note counts: the newest one that
    /// starts the notice or follows a space, and ends it or is followed by one. "Bo leads now." is never cut out of "Jimbo leads now."
    /// </summary>
    private void Unnote(string? text) => _notice = Without(_notice, text);

    /// <summary><paramref name="notice"/> with the newest whole note <paramref name="text"/> taken out (see <see cref="Unnote"/>); null if nothing is left.</summary>
    internal static string? Without(string? notice, string? text)
    {
        if (string.IsNullOrEmpty(text) || notice is null)
            return notice;
        for (var at = notice.LastIndexOf(text, StringComparison.Ordinal); at >= 0;
             at = at == 0 ? -1 : notice.LastIndexOf(text, Math.Min(notice.Length - 1, at + text.Length - 2), StringComparison.Ordinal))
        {
            var end = at + text.Length;
            if ((at > 0 && notice[at - 1] != ' ') || (end < notice.Length && notice[end] != ' '))
                continue;
            var (from, to) = end < notice.Length ? (at, end + 1) : (Math.Max(0, at - 1), end);    // with one of the spaces round it
            var left = notice.Remove(from, to - from);
            return left.Length == 0 ? null : left;
        }
        return notice;
    }

    /// <summary>Says on the TV who leads now (5b review item 5). Only the TV: the phones already show it, and a note would mint them a view.</summary>
    private void SayLeader(string name)
    {
        Unnote(_leadNote);
        _leadNote = $"{name} leads now.";
        Note(_leadNote);
    }

    /// <summary>Into the world, from Muster, the battle list or Results.</summary>
    private void ToExploration()
    {
        _toasts.Clear();
        _phase = TacticsPhase.Exploration;
        ShowPendingQuestNotice();
        NoteAppeared();
        if (LookAround())       // what the party sees as it comes into the world, or back from a fight or a scene
        {
            KeepWorld();
            Save();
        }
        ReconsiderLeader();
        _heardAt = _ctx.Now;     // the pad has only just come back
        LeadClock();
    }

    /// <summary>Out of the world, to anywhere: everything walking runs on ends here, and the place is saved at once.</summary>
    private void LeaveExploration()
    {
        Halt();
        _ctx.CancelTimer(Timers.Lead);     // no pad, no idle ping: the silence rule would take the lead away during any battle
        Unnote(_leadNote);                 // the walk's own notes would mean nothing over a battle
        Unnote(RoadGoesOn);
        _leadNote = null;
        SaveWorld();
    }

    /// <summary>
    /// The VIP ends the session, from the walk or from inside a scene: the place is saved, and in a scene its clocks stop, with the cursor
    /// as saved on its line. An open question's votes go, as on any restart.
    /// </summary>
    private void EndSession()
    {
        _ctx.CancelTimer("townBark");
        _ctx.CancelTimer("questResult");
        if (_phase == TacticsPhase.Exploration)
            LeaveExploration();
        else if (_phase == TacticsPhase.Story)
        {
            EndTelling();
            KeepWorld();
            Save();
        }
        _ctx.EndGame(new GameResults([], $"The party is level {_doc.PartyLevel}; {_doc.Completed.Length} battles won."));
    }

    // ---- Walking -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The leader's phone saying what it holds. A repeat of the direction held only extends the lease, so pings can't change the
    /// speed. A press or a change steps in this very call, unless the party is still on its way to a tile, when it goes as that step ends,
    /// even if the thumb has lifted by then (<see cref="_latched"/>).
    /// </summary>
    private void WalkInput(Player player, JsonElement? data)
    {
        if (QuestLogOpen) return;
        if (!ReadDir(data, out var dir) || player.Profile?.Value != _lead)
            return;
        if (dir is null)
        {
            if (_owed && !_latched)
            {
                _latched = true;
                ArmStep();
            }
            else if (!_latched)
                StopWalking();
            return;
        }

        var now = _ctx.Now;
        _heldUntil = now + Walk.HoldLease;
        if (_latched)
        {
            _latched = false;       // pressed again before the tap's step: held once more
            ArmStep();
        }
        if (dir == _dir)
            return;
        var held = _dir is not null;
        (_dir, _blocked) = (dir, false);
        if (now >= _stepDue)
            TakeStep(onTime: false);
        else
        {
            _owed = true;
            if (!held)
                ArmStep();
        }
    }

    /// <summary>Reads <c>{dir}</c>: false for anything but a direction or an explicit null, which means stopped.</summary>
    private static bool ReadDir(JsonElement? data, out WalkDir? dir)
    {
        dir = null;
        if (data is not { ValueKind: JsonValueKind.Object } d || !d.TryGetProperty("dir", out var value))
            return false;
        if (value.ValueKind == JsonValueKind.Null)
            return true;
        if (value.ValueKind != JsonValueKind.String)
            return false;
        dir = value.GetString() switch
        {
            "n" => WalkDir.N, "ne" => WalkDir.NE, "e" => WalkDir.E, "se" => WalkDir.SE,
            "s" => WalkDir.S, "sw" => WalkDir.SW, "w" => WalkDir.W, "nw" => WalkDir.NW,
            _ => null,
        };
        return dir is not null;
    }

    /// <summary>
    /// Takes the step the held direction asks for, or turns to face it if the way is blocked. A blocked step tells only the TV
    /// (<see cref="ExploreView.Blocked"/>, and the road going on at the region's edge): a toast would change the phone's view.
    /// </summary>
    private void TakeStep(bool onTime)
    {
        var (dir, from, now) = (_dir!.Value, _at, _ctx.Now);
        var terrain = WalkGround();
        _owed = false;
        if (WorldWalk.Next(terrain, from, dir, ClassDef.DefaultJump, _lastAlongX) is not { } move)
        {
            var (dx, dy) = WorldWalk.Delta(dir);
            _facing = new Tile(from.X + dx, from.Y + dy);
            _blocked = true;
            // A door nobody has found is a wall the TV doesn't draw: bumping it would give it away, so the party just stands.
            _unseenWall = WorldWalk.Next(Rules.GroundFor(_region!.Def.Id)!.Terrain, from, dir, ClassDef.DefaultJump, _lastAlongX) is { } plain
                && UnseenBlocker(plain.To);
            if (!_unseenWall)
                _bumps++;
            if (!_edgeSaid && !terrain.Map.InBounds(_facing))
            {
                _edgeSaid = true;
                Note(RoadGoesOn);
            }
        }
        else if (Armed() is { } armed && armed.Stage.Holds(move.To) && !armed.Stage.Holds(from))
        {
            // Into the stamp of the fight on offer, from outside it: the party stops one tile short, facing it, and the beat starts.
            // Nothing of the walk goes on: not this step's save or timer, nor the next.
            _facing = move.To;
            EnterBattleBeat(armed.Beat);       // its lead-in first, the first time (5d §3)
            return;
        }
        else
        {
            var ms = StepMs(from, move);
            // On time, from when the last step ended, so that a late timer doesn't drift; after a stall or a rest, from now: never a burst.
            var start = onTime && now - _stepDue < Walk.Step ? _stepDue : now;
            _stepDue = start + TimeSpan.FromMilliseconds(ms);
            _at = move.To;
            _facing = new Tile(2 * move.To.X - from.X, 2 * move.To.Y - from.Y);
            if (!move.Diagonal)
                _lastAlongX = move.To.Y == from.Y;
            _arrived = null;     // walking again: the last epoch's arrival is over
            var leg = new WalkLeg(++_walkSeq, move.To, ms);
            _legs = (_legs.Length < LegsKept ? _legs : _legs.RemoveAt(0)).Add(leg);
            (_notice, _leadNote, _thingNote) = (null, null, null);      // whatever it said, the party has moved on
            SteppedOnto();      // a trap underfoot, or something hidden near (interactables design §2.6)
        }
        SaveSoon();
        ArmStep();
    }

    /// <summary>How many steps <see cref="ExploreView.Legs"/> keeps. The TV takes only the newest view each frame, so it can miss some.</summary>
    private const int LegsKept = 12;

    /// <summary>A step's length, in milliseconds. It's here, not in <see cref="WorldWalk"/>, because the logic has no clocks.</summary>
    private int StepMs(Tile from, WalkMove move)
    {
        var terrain = Rules.GroundFor(_region!.Def.Id)!.Terrain;
        var drop = terrain.HeightAt(from) - terrain.HeightAt(move.To);
        var ms = Walk.Step.TotalMilliseconds * move.Cost * (move.Diagonal ? Math.Sqrt(2) : 1) + Walk.DropStep.TotalMilliseconds * Math.Max(0, drop - 2);
        return Math.Max(1, (int)Math.Round(ms));
    }

    /// <summary>Sets the step timer for the next thing that can happen: this step ending, or the lease running out.</summary>
    private void ArmStep()
    {
        var at = _latched ? _stepDue : _blocked || _heldUntil < _stepDue ? _heldUntil : _stepDue;
        _ctx.SetTimer(Timers.Step, Longest(at - _ctx.Now, TimeSpan.FromMilliseconds(1)));
    }

    private static TimeSpan Longest(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private void WalkTimer(string key)
    {
        switch (key)
        {
            case Timers.Step when _dir is not null:
                var now = _ctx.Now;
                if (_latched && now >= _stepDue)
                {
                    TakeStep(onTime: true);     // the tap's one step, then the stop it asked for
                    StopWalking();
                }
                else if (_latched)
                    ArmStep();
                else if (now >= _heldUntil)
                    StopWalking();
                else if (!_blocked && now >= _stepDue)
                    TakeStep(onTime: true);
                else
                    ArmStep();
                break;
            case Timers.Lead:
                LeadTimer();
                break;
            case Timers.Save:
                SaveWorld();
                break;
        }
    }

    /// <summary>Ends the hold and its timer. Everything that stops the party goes through here. False if it wasn't walking.</summary>
    private bool Halt()
    {
        if (_dir is null)
            return false;
        (_dir, _blocked, _owed, _latched) = (null, false, false, false);
        _settledAt = _ctx.Now;
        _ctx.CancelTimer(Timers.Step);
        return true;
    }

    /// <summary>The party stops of its own accord: a release, or the lease running out. Being stopped is also when the lead can change.</summary>
    private void StopWalking()
    {
        if (!Halt())
            return;
        SaveSoon();
        ReconsiderLeader();
    }

    // ---- The lead -----------------------------------------------------------------------------------------------------

    /// <summary>Anything at all from a phone means it's alive.</summary>
    private void HeardFrom(ProfileId profile)
    {
        _electionOpen = false;
        if (profile.Value == _lead)
            _heardAt = _ctx.Now;
        else if (_silent.Remove(profile.Value))
            ReconsiderLeader();
    }

    /// <summary>Someone's phone came or went. Only the leader going stops the party: anyone else can come and go under a walk.</summary>
    private void WalkerConnection(Player? player, bool connected)
    {
        if (_region is null)
            return;
        if (player?.Profile is { } profile)
            _silent.Remove(profile.Value);
        if (!connected && _electionOpen && player?.Profile?.Value == _sessionLead)
            RetractElection();
        if (!connected && player?.Profile?.Value == _lead)
            Halt();
        ReconsiderLeader();
        if (!connected)
            SaveWorld();
    }

    /// <summary>
    /// The silence rule: stateful reconnect can hold back the disconnect event, so a leader's phone that says nothing for long
    /// enough loses the lead. Pings only move <see cref="_heardAt"/>, so there's one timer here, not one for each ping. With nobody to
    /// hand on to, the leader keeps it.
    /// </summary>
    private void LeadTimer()
    {
        if (_lead is not { } lead)
            return;
        var now = _ctx.Now;
        if (TakebackAt <= now)
        {
            ReconsiderLeader();
            if (_lead != lead)
                return;
        }
        if (now - _heardAt < Walk.LeadSilence)
            LeadClock();
        else if (NextAfter(lead) == lead)
            LeadClock(now + Walk.LeadSilence);
        else
            HandLead(lead);
    }

    /// <summary>Takes the lead from a silent leader: the party stops first.</summary>
    private void HandLead(string from)
    {
        Halt();
        _silent.Add(from);
        ReconsiderLeader();
    }

    /// <summary>
    /// Sets the lead's one timer if there's a pad on someone's screen, and stops it if there isn't. It's for whichever comes first: the
    /// silence rule, by <paramref name="silenceDue"/> or else 8 s from when the leader was last heard, or the session's leader taking the
    /// lead back (<see cref="TakebackAt"/>).
    /// </summary>
    private void LeadClock(DateTimeOffset? silenceDue = null)
    {
        if (_phase != TacticsPhase.Exploration || _lead is null)
        {
            _ctx.CancelTimer(Timers.Lead);
            return;
        }
        var now = _ctx.Now;
        var due = silenceDue ?? _heardAt + Walk.LeadSilence;
        if (TakebackAt is { } back && back > now && back < due)
            due = back;
        _ctx.SetTimer(Timers.Lead, Longest(due - now, TimeSpan.FromMilliseconds(1)));
    }

    /// <summary>
    /// When the session's own leader can take the lead back from whoever stood in for them, or null if nobody is waiting to: only in
    /// the world, with the party stopped and both of them able to lead. It's <see cref="WalkTiming.TakebackAfter"/> from the party
    /// last stopping or the stand-in getting the lead, whichever is later, so it lands between walks and not between one press and the
    /// next, and a stand-in whose pad has just appeared isn't left reaching for nothing. In any other phase there's no pad to pull
    /// out from under a thumb, so the lead goes back at once.
    /// </summary>
    private DateTimeOffset? TakebackAt => _phase == TacticsPhase.Exploration && _dir is null && _sessionLead is { } own && _lead is { } standIn
        && standIn != own && CanLead(own) && CanLead(standIn)
            ? _settledAt + Walk.TakebackAfter
            : null;

    /// <summary>Whether a member can take the lead: here, with a character, and not silent.</summary>
    private bool CanLead(string member)
        => _doc.Members.ContainsKey(member) && !_silent.Contains(member) && _players.Any(p => p.Profile?.Value == member && _online.Contains(p.Id));

    /// <summary>
    /// The next member after <paramref name="last"/> who can lead, in member order, which is ordinal by profile because seat
    /// order changes between nights. <paramref name="last"/> needn't be a member any more.
    /// </summary>
    private string? NextAfter(string? last)
    {
        var free = _doc.Members.Keys.Where(CanLead).ToArray();
        return free.FirstOrDefault(m => string.CompareOrdinal(m, last) > 0) ?? free.FirstOrDefault();
    }

    /// <summary>The session's one rotation, when it can happen at Start: see <see cref="_electionOpen"/>.</summary>
    private void ElectAtStart()
    {
        var last = _doc.World?.LastLeader;
        ReconsiderLeader();
        (_electionOpen, _electedAfter) = (_sessionLead is not null, last);
    }

    /// <summary>The seat elected at Start was empty: the rotation hasn't happened, so the next one who's here gets it, and it's as if the empty seat was never picked.</summary>
    private void RetractElection()
    {
        _sessionLead = null;
        _doc = _doc with { World = _doc.World! with { LastLeader = _electedAfter } };
        Save();
    }

    /// <summary>
    /// Settles who leads, only ever with the party stopped: this session's leader if they're here (electing one if the session has
    /// none yet, which is its one rotation), else whoever already leads, else the next who can. Better a silent member than nobody.
    /// Taking the lead back from a stand-in who can keep it waits for <see cref="TakebackAt"/>. A change is said on the TV.
    /// </summary>
    private void ReconsiderLeader()
    {
        if (_region is null || _dir is not null)
            return;
        _sessionLead ??= NextAfter(_doc.World?.LastLeader);
        var wanted = WantedLeader();
        if (wanted is null && _silent.Count > 0)
        {
            _silent.Clear();
            wanted = WantedLeader();
        }
        if (wanted == _lead)
        {
            if (TakebackAt is not null)
                LeadClock();        // the session's leader is waiting: the timer has to come round for them
            return;
        }
        var was = _lead;
        _lead = wanted;
        if (was != wanted) _questLogLeader = null;
        (_heardAt, _settledAt) = (_ctx.Now, _ctx.Now);      // a stand-in gets the pad for at least TakebackAfter, however long the party stood before
        if (_phase == TacticsPhase.Exploration && was is not null && !_electionOpen && Leader is { } leader)     // not a seat found empty at Start
            SayLeader(leader.Name);
        LeadClock();
        SaveWorld();
    }

    // The guard against the lead changing hands mid-scene (5d flag 9) is on the first arm, not at the top of ReconsiderLeader: the
    // session's leader waits for the scene to end, so a tie-break or a choice can't change hands under the party, but a leader who drops
    // mid-scene still has to be replaced, which the other arms do. The first arm also waits for the takeback: a stand-in who can still lead
    // keeps it until the party has been stopped long enough. With nobody leading at all, mid-scene or not, the session's leader is first.
    private string? WantedLeader() => (_phase != TacticsPhase.Story || _lead is null) && _sessionLead is { } own && CanLead(own) && !(TakebackAt > _ctx.Now)
        ? own
        : _lead is { } still && CanLead(still) ? still
        : NextAfter(_lead ?? _sessionLead);

    // ---- After a fight (overworld 5c, Task 4) ---------------------------------------------------------------------------

    /// <summary>What <see cref="ExploreView.Arrived"/> says after a fight the party won or fled: it falls in behind the leader where it stands.</summary>
    internal const string FellIn = "fellIn";

    /// <summary>What <see cref="ExploreView.Arrived"/> says after a fight the party lost: it wakes at the region's recovery spawn.</summary>
    internal const string Woke = "woke";

    /// <summary>
    /// Puts the party back in the world after a fight on a stamped map, before the battle is dropped (5c §6). A loss wakes it at the
    /// recovery spawn, which the region check keeps out of every stamp. A win or a flight stands it where the fight left it
    /// (<see cref="Landing"/>). Either is a teleport: a new <see cref="_epoch"/>, with no legs to follow. A fight on a map the region doesn't
    /// stamp, or no longer stamps where the fight was saved (<see cref="StageOf"/>), leaves the party where it was, with no new epoch. Call it after the story has taken the fight's outcome, so that a stamp the
    /// outcome arms counts.
    /// </summary>
    private void Land(BattleSave save, BattleOutcome outcome)
    {
        _arrived = null;
        if (_region is not { } region || StageOf(save) is not { } stage)
            return;
        var at = outcome == BattleOutcome.Lost ? region.Def.Spawns.Recovery : Landing(save, stage);
        (_at, _facing, _arrived) = (at, new Tile(at.X + 1, at.Y), outcome == BattleOutcome.Lost ? Woke : FellIn);
        _epoch++;
        _legs = [];
    }

    /// <summary>
    /// Where walking resumes after a fight won or fled (5c default 4): the leader's battle tile in the world, or if the leader isn't Up, that
    /// of the first hero who is, in member order. The tile must be one the party could have walked to, by the rule
    /// <see cref="EnterWorld"/> holds a saved place to (walkable, and reached from the region's start), and outside any armed stamp, this
    /// one included after a flight or a fight from the list, or another the outcome has just armed: else stepping out would start a fight,
    /// or a swimmer's nearest bank could be cut off from the rest of the world. If the tile isn't one, it's the nearest that is
    /// (<see cref="BattleStage.Standable"/>), then the first of the map's <c>party_spawn</c> that is, as <c>StoryOrder</c> assumes, then the
    /// tile the party stood on when the fight began, and last the start spawn, which always is.
    /// </summary>
    private Tile Landing(BattleSave save, BattleStage stage)
    {
        var ground = WalkGround();
        var start = _region!.Def.Spawns.Start;
        var reached = WalkReach();      // with the doors and gates as they are now (interactables design §2.4)
        var armed = Armed()?.Stage;
        bool Allowed(Tile t) => ground.Map.InBounds(t) && reached.Contains(t) && armed?.Holds(t) != true;

        var spawn = Rules.MapFor(save.State.MapId)?.Zones.GetValueOrDefault("party_spawn") is { IsDefault: false } zone ? zone : [];
        ImmutableArray<Tile> fallback = [.. spawn.Select(stage.ToWorld), _at, start];
        var hero = LandingHero(save);
        var from = hero is { } unit ? stage.ToWorld(unit.Pos) : fallback[0];
        return BattleStage.Standable(ground, from, fallback, Allowed) ?? start;
    }

    /// <summary>The leader's hero if it's Up, else the first Up hero in member order, else null.</summary>
    private UnitState? LandingHero(BattleSave save)
    {
        var units = save.State.Units;
        UnitState? Up(ProfileId member)
            => save.Members.IndexOf(member) is var i and >= 0 && units.TryGetValue(UnitFor(i), out var u) && u.Life == LifeState.Up ? u : null;
        if (_lead is { } lead && Up(new ProfileId(lead)) is { } leader)
            return leader;
        return save.Members.OrderBy(m => m.Value, StringComparer.Ordinal).Select(Up).FirstOrDefault(u => u is not null);
    }

    // ---- Saving -------------------------------------------------------------------------------------------------------

    /// <summary>Whether the document is behind the party.</summary>
    private bool WorldChanged => _region is not null
        && (_doc.World is not { } saved || saved.Region != _region.Def.Id || saved.At != _at || saved.Facing != _facing
            || _sessionLead is not null && saved.LastLeader != _sessionLead);

    /// <summary>Brings the document up to the party, without saving it.</summary>
    private void KeepWorld()
    {
        if (_doc.Shops is { } shops && _region is { } shopRegion && shopRegion.Def.Chapter > shops.Chapter)
            _doc = _doc with { Shops = shops with { Chapter = shopRegion.Def.Chapter } };
        if (!WorldChanged)
            return;
        _doc = _doc with
        {
            World = (_doc.World ?? new WorldSave(_region!.Def.Id, _at, _facing, null, [], [])) with
            {
                Region = _region!.Def.Id, At = _at, Facing = _facing, LastLeader = _sessionLead ?? _doc.World?.LastLeader,
            },
        };
    }

    /// <summary>
    /// Schedules a save: soon after the party stops, and by a fixed deadline from the first unsaved step, which a stop can bring
    /// forward but never push back. A walk that picks up again before the stop's save takes it back, so rests under
    /// <see cref="WalkTiming.SaveAfterStop"/> save no more often than walking. A rest that long is a real stop, and each one
    /// saves, so the most this can ever be is one save per rest, which is one a second.
    /// </summary>
    private void SaveSoon()
    {
        if (!WorldChanged)
            return;
        var now = _ctx.Now;
        if (_dir is null)
        {
            var soon = now + Walk.SaveAfterStop;
            SaveAt(_saveBy < soon ? _saveBy.Value : soon);
        }
        else
            SaveAt(_saveBy ??= now + Walk.SaveWhileWalking);
    }

    private void SaveAt(DateTimeOffset at)
    {
        if (_saveAt == at)
            return;
        _saveAt = at;
        _ctx.SetTimer(Timers.Save, Longest(at - _ctx.Now, TimeSpan.FromMilliseconds(1)));
    }

    /// <summary>Saves the party's place now, if it has changed, and whatever was scheduled goes with it.</summary>
    private void SaveWorld()
    {
        _ctx.CancelTimer(Timers.Save);
        (_saveBy, _saveAt) = (null, null);
        if (!WorldChanged)
            return;
        KeepWorld();
        Save();
    }
}
