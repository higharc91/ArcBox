using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using Tactics.Logic;
using static Tactics.Game.Tests.ExplorationTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Walking against a small model of itself: fixed seeds generate long, ugly runs of presses, pings, drops, restarts, battles and late
/// timers, and after every event the game has to agree with what the rules say it may do. A failure names the seed and a shrunken list
/// of events. <c>WALK_MODEL_EVENTS</c> raises the count for long runs.
/// </summary>
public sealed class WalkModelTests
{
    private const int EventsPerSeed = 300;
    private const int DefaultEvents = 6_000;

    private static readonly string[] Directions = ["n", "ne", "e", "se", "s", "sw", "w", "nw"];
    private static readonly string?[] LastLeaders = [null, "pr_member1", "pr_member2", "pr_member3", "pr_member4"];
    private static readonly TacticsTiming Timing = new(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50));

    private static readonly Lazy<Tile[]> Tiles = new(() => [.. WorldWalk.Reach(Ground, Rules.RegionFor("ashen_ford")!.Def.Spawns.Start, ClassDef.DefaultJump).Order()]);
    private static readonly Lazy<JsonElement> BaseDocument = new(() => DocumentAt(null));

    private static Terrain Ground => Rules.GroundFor("ashen_ford")!.Terrain;

    /// <summary>One thing that happens. <paramref name="Player"/> -1 means whoever leads.</summary>
    private sealed record Event(string Kind, int Player = 0, string? Dir = null, int Ms = 0);

    /// <param name="Seed">Everything random in the run.</param>
    /// <param name="Seats">How many members are in the session.</param>
    /// <param name="Clock">0 for timers on the dot, 1 for late timers, 2 for a clock that moves between reads, as the server's does.</param>
    /// <param name="Lacking">The last seat has no character.</param>
    /// <param name="Others">Members who aren't in the session stay in the campaign.</param>
    /// <param name="Tile">Where the party starts, as an index into the reachable tiles.</param>
    /// <param name="LastLeader">Who led the session before.</param>
    /// <param name="AwayAtStart">The seats with no phone at Start, as a bit mask: the room says so only after Start returns.</param>
    /// <param name="Armed">
    /// 0 for no story; 1 or 2 for the ford chosen by force or by parley, so River Crossing's stamp is armed (overworld 5c), with the party
    /// starting near it: the walks then step into it, start its fight from a press or a timer, and come back from it.
    /// </param>
    private sealed record Setup(int Seed, int Seats, int Clock, bool Lacking, bool Others, int Tile, string? LastLeader, int AwayAtStart, int Armed = 0)
    {
        public static Setup Of(int seed)
        {
            var r = new Random(seed);
            return new Setup(seed, r.Next(1, 5), r.Next(3), r.Next(4) == 0, r.Next(2) == 0, r.Next(100_000), LastLeaders[r.Next(5)], r.Next(3) == 0 ? r.Next(16) : 0,
                r.Next(3));
        }
    }

    /// <summary>The reachable tiles within 4 steps of River Crossing's stamp, inside it or out, where an armed run starts.</summary>
    private static readonly Lazy<Tile[]> NearTheStamp = new(() =>
    {
        var stage = StageBattleTests.Stage;
        return [.. Tiles.Value.Where(t => t.X >= stage.Origin.X - 4 && t.Y >= stage.Origin.Y - 4 && t.X < stage.Origin.X + stage.Width + 4
            && t.Y < stage.Origin.Y + stage.Depth + 4)];
    });

    private sealed class ModelBroke(string message) : Exception(message);

    /// <summary>A run that stops early without a verdict: a battle that wouldn't end, or the battle bug named in <see cref="Applied"/>.</summary>
    private sealed class Inconclusive : Exception;

    private sealed class Context(int seed, bool drifts) : IGameContext
    {
        public DateTimeOffset Clock { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public Dictionary<string, DateTimeOffset> Timers { get; } = [];
        public bool Over { get; private set; }
        public Random Random { get; } = new(seed);

        public DateTimeOffset Now
        {
            get
            {
                if (drifts)
                    Clock = Clock.AddTicks(30);
                return Clock;
            }
        }

        public void SetTimer(string key, TimeSpan after)
        {
            if (Over)
                return;
            if (after < TimeSpan.Zero)
                throw new ModelBroke($"negative timer {key}");
            Timers[key] = Now + after;
        }

        public void CancelTimer(string key) => Timers.Remove(key);

        public DateTimeOffset? TimerDeadline(string key) => Timers.TryGetValue(key, out var at) ? at : null;

        public void EndGame(GameResults results)
        {
            Over = true;
            Timers.Clear();
        }
    }

    private sealed class Lease(CampaignInfo info, JsonElement document) : ICampaignLease
    {
        public CampaignInfo Info => info;
        public JsonElement Document { get; private set; } = document.Clone();
        public WorldSave? World => TacticsDocument.Read(Document).World;

        public void Save(JsonElement document) => Document = document.Clone();

        public void Checkpoint(JsonElement document, string label) => Save(document);
    }

    /// <summary>The game as seen from outside, before and after an event.</summary>
    private sealed record Seen(TacticsPhase Phase, string Host, string[] Phones, ExploreView Party, HashSet<string> Timers, string? Notice);

    private sealed class Run
    {
        private const int MaxLateMs = 200;

        private readonly Setup _setup;
        private readonly bool _late;
        private readonly Random _lateness;
        private readonly List<Player> _players;
        private readonly Lease _lease;
        private readonly HashSet<string> _silent = [];
        private readonly Dictionary<long, int> _firesEachSecond = [];
        private HashSet<int> _online = [];
        private Context _context = null!;
        private TacticsGame _game = null!;
        private double _slackMs;
        private bool _over;

        // What the model expects.
        private DateTimeOffset? _heldUntil;
        private string? _dir;
        private bool? _lastAlongX;
        private DateTimeOffset _lastCommit, _commitBefore;
        private int _lastMs, _msBefore, _commits, _epoch;
        private DateTimeOffset? _behindSince;

        // Quick taps: a press during a step that hasn't stepped yet is owed; let go while owed, it's latched, and takes one step.
        private bool _owed, _latched;

        // The takeback: when the party last stopped or the lead last changed hands, and when whoever leads was last heard from.
        private DateTimeOffset _settledAt, _leaderHeard;
        private int _seat = -1;

        public Run(Setup setup)
        {
            (_setup, _late) = (setup, setup.Clock == 1);
            _lateness = new Random(setup.Seed * 31 + 7);
            var r = new Random(setup.Seed);
            var document = TacticsDocument.Read(BaseDocument.Value);
            var pool = document.Members.Keys.OrderBy(_ => r.Next()).ToArray();     // seats shuffled
            var seated = pool.Take(setup.Seats).ToArray();
            if (setup.Lacking)
                document = document with { Members = document.Members.Remove(seated[^1]) };
            if (!setup.Others)
                foreach (var member in pool.Skip(setup.Seats))
                    document = document with { Members = document.Members.Remove(member) };
            document = document with { World = Saved(Tiles.Value[setup.Tile % Tiles.Value.Length], lastLeader: setup.LastLeader) };
            if (setup.Armed > 0)
            {
                var story = setup.Armed == 1 ? StageBattleTests.Force : StageBattleTests.Parley;
                document = document with
                {
                    World = document.World! with { At = NearTheStamp.Value[setup.Tile % NearTheStamp.Value.Length], Flags = story.Flags, Beats = story.Beats },
                };
            }
            _players = [.. seated.Select((p, i) => new Player(new PlayerId($"p{i}"), $"Player{i}", i == 0, new ProfileId(p)))];
            _lease = new Lease(new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", _players[0].Profile!.Value, [], 1), document.ToJson());
            _context = new Context(setup.Seed, setup.Clock == 2);
            NewGame(resume: false);
        }

        private void NewGame(bool resume)
        {
            var last = _lease.World?.LastLeader;
            _context = new Context(_setup.Seed + _commits, _setup.Clock == 2) { Clock = _context.Clock.AddSeconds(2) };
            _over = false;
            _game = new TacticsGame(Rules, Timing, _setup.Armed > 0 ? StageBattleTests.Lore.Value : null, battleList: true);
            (_heldUntil, _dir, _lastAlongX, _commits, _behindSince) = (null, null, null, 0, null);
            (_owed, _latched, _settledAt, _leaderHeard) = (false, false, DateTimeOffset.MinValue, _context.Clock);
            _silent.Clear();
            _firesEachSecond.Clear();
            if (resume)
            {
                _online.Clear();
                _game.Resume(_context, _players, _lease);
                Must(_game.Leader is null, "a resume with nobody online has a leader");
                Must(_lease.World?.LastLeader == last || last is null, "a resume changed LastLeader");
            }
            else
            {
                _online = [.. Enumerable.Range(0, _players.Count)];
                _game.Start(_context, _players, _lease);
                // As the room does: every seat is online for Start, and the absent ones drop straight after it.
                foreach (var seat in Enumerable.Range(0, _players.Count).Where(p => (_setup.AwayAtStart >> p & 1) == 1))
                {
                    _online.Remove(seat);
                    _game.OnPlayerConnectionChanged(_players[seat].Id, false);
                }
                CheckRotation(last);
            }
            _epoch = ((TacticsHostView)_game.HostView()).Explore!.Epoch;
            Must(_epoch != 0, "epoch 0");
        }

        /// <summary>The next member after the last leader, of those really here; with nobody here, the rotation hasn't happened.</summary>
        private void CheckRotation(string? last)
        {
            var members = TacticsDocument.Read(_lease.Document).Members.Keys;
            var here = members.Where(m => _players.Any(p => p.Profile!.Value.Value == m && _online.Contains(_players.IndexOf(p)))).ToArray();
            var want = here.FirstOrDefault(m => string.CompareOrdinal(m, last) > 0) ?? here.FirstOrDefault();
            Must(_lease.World?.LastLeader == (want ?? last), $"rotation: after {last} wanted {want ?? last}, saved {_lease.World?.LastLeader}");
            Must(_game.Leader?.Profile?.Value == want, $"rotation: after {last} wanted {want}, got {_game.Leader?.Profile?.Value}");
        }

        private static void Must(bool ok, string what)
        {
            if (!ok)
                throw new ModelBroke(what);
        }

        private Seen Look()
        {
            var host = (TacticsHostView)_game.HostView();
            return new Seen(_game.Phase, JsonSerializer.Serialize(host, ArcJson.Options),
                [.. _players.Select(p => JsonSerializer.Serialize(_game.PlayerView(p.Id), ArcJson.Options))], host.Explore!, [.. _context.Timers.Keys], host.Notice);
        }

        private void Send(int seat, object payload) => _game.OnInput(_players[seat].Id, new PlayerInput(JsonSerializer.SerializeToElement(payload, ArcJson.Options)));

        private int LeaderSeat() => _game.Leader is { } leader ? _players.FindIndex(p => p.Id == leader.Id) : 0;

        private void Forget() => (_heldUntil, _dir, _owed, _latched) = (null, null, false, false);

        public void Play(IEnumerable<Event> events)
        {
            foreach (var e in events)
                if (!_over || e.Kind == "start")
                    Apply(e, _over ? null : Look());
        }

        private int Seat(Event e) => e.Player < 0 ? 0 : Math.Min(e.Player, _players.Count - 1);

        private void Apply(Event e, Seen? before)
        {
            switch (e.Kind)
            {
                case "walk":
                {
                    var seat = e.Player < 0 ? LeaderSeat() : Seat(e);
                    if (!_online.Contains(seat))
                        return;
                    var led = _game.Leader?.Id == _players[seat].Id;
                    _silent.Remove(_players[seat].Profile!.Value.Value);
                    _seat = seat;
                    Send(seat, new { action = "walk", data = new { dir = e.Dir } });
                    if (_game.Phase == TacticsPhase.Exploration && (led || _game.Leader?.Id == _players[seat].Id))
                    {
                        var after = Look();
                        if (e.Dir is null && _owed && _dir is not null)
                        {
                            // Let go before the press could step: the tap is latched, and goes as the step under way ends.
                            Must(after.Timers.Contains(TacticsGame.Timers.Step), "a quick tap was let go and dropped");
                            (_latched, _heldUntil) = (true, null);
                        }
                        else if (e.Dir is null)
                        {
                            if (!_latched)
                                (_dir, _heldUntil) = (null, null);
                        }
                        else
                        {
                            var changed = e.Dir != _dir;
                            (_dir, _heldUntil, _latched) = (e.Dir, _context.Clock + TimeSpan.FromMilliseconds(750), false);
                            if (changed && after.Party.Seq == before!.Party.Seq && after.Party.Held)
                                _owed = true;       // a press during a step: it hasn't gone yet, but it's owed
                        }
                    }
                    Check(e, before!);
                    break;
                }
                case "junk":
                {
                    var seat = Seat(e);
                    if (!_online.Contains(seat))
                        return;
                    object[] junk =
                    [
                        new { action = "walk" }, new { action = "walk", data = 3 }, new { action = "walk", data = new { dir = 5 } },
                        new { action = "walk", data = new { dir = "NORTH" } }, new { action = "walk", data = new { dir = "N" } },
                        new { action = "walk", data = new { dir = new string('x', 100_000) } }, new { action = "walk", data = new { dir = new[] { "n" } } },
                        new { action = "battles", data = 1 }, new { action = 3 }, new { foo = 1 }, new[] { 1, 2 },
                    ];
                    var which = e.Ms % junk.Length;
                    if (which < 8)      // the rest don't parse, so they aren't heard
                        _silent.Remove(_players[seat].Profile!.Value.Value);
                    if (which < 8)
                        _seat = seat;
                    var phase = _game.Phase;
                    Send(seat, junk[which]);
                    if (_game.Phase != phase)
                        Forget();
                    Check(e, before!);
                    break;
                }
                case "conn" or "disc":
                {
                    var seat = Seat(e);
                    var connected = e.Kind == "conn";
                    if (connected == _online.Contains(seat))
                        return;
                    if (connected)
                        _online.Add(seat);
                    else
                        _online.Remove(seat);
                    _silent.Remove(_players[seat].Profile!.Value.Value);
                    var led = _game.Leader?.Id == _players[seat].Id;
                    _game.OnPlayerConnectionChanged(_players[seat].Id, connected);
                    if (!connected && led)
                        Forget();
                    Check(e, before!);
                    break;
                }
                case "blip":    // a phone drops and is back Ms later; -1 is the session's own leader
                {
                    var own = _players.FindIndex(p => p.Profile!.Value.Value == _lease.World?.LastLeader);
                    var seat = e.Player < 0 ? Math.Max(own, 0) : Seat(e);
                    Apply(new Event("disc", seat), before);
                    if (!_over && e.Ms > 0)
                        Advance(e.Ms);
                    if (!_over)
                        Apply(new Event("conn", seat), Look());
                    break;
                }
                case "tap":     // a press, held for Ms, and let go
                {
                    var seat = e.Player < 0 ? LeaderSeat() : Seat(e);
                    Apply(new Event("walk", seat, e.Dir), before);
                    if (!_over && e.Ms > 0)
                        Advance(e.Ms);
                    if (!_over)
                        Apply(new Event("walk", seat), Look());
                    break;
                }
                case "adv":
                    Advance(e.Ms);
                    break;
                case "battles":
                {
                    var seat = Seat(e);
                    if (!_online.Contains(seat))
                        return;
                    _seat = seat;
                    Send(seat, new { action = "battles" });
                    if (_game.Phase != TacticsPhase.Exploration)
                        Forget();
                    Check(e, before!);
                    break;
                }
                case "back":
                {
                    var seat = Seat(e);
                    if (!_online.Contains(seat))
                        return;
                    _seat = seat;
                    Send(seat, new { action = "backToWalk" });
                    Check(e, before!);
                    break;
                }
                case "pick":
                {
                    var seat = Seat(e);
                    if (!_online.Contains(seat))
                        return;
                    _seat = seat;
                    Send(seat, new { action = "pickClass", data = new { @class = "fighter" } });
                    Check(e, before!);
                    break;
                }
                case "fight":
                    Fight(e, before!);
                    break;
                case "end":
                {
                    var seat = Seat(e);
                    if (!_online.Contains(seat))
                        return;
                    _seat = seat;
                    Send(seat, new { action = "endSession" });
                    if (_context.Over)
                    {
                        _over = true;
                        Must(_lease.World!.At == before!.Party.At, "End session didn't keep the tile");
                        Must(_lease.World!.Facing == before.Party.Facing, "End session didn't keep the facing");
                        Must(_context.Timers.Count == 0, "timers after the end");
                    }
                    else
                        Check(e, before!);
                    break;
                }
                case "start":
                    NewGame(resume: false);
                    Check(e, Look(), fresh: true);
                    break;
                case "restart":
                    NewGame(resume: true);
                    Must(((TacticsHostView)_game.HostView()).Explore!.At == _lease.World!.At, "a resume isn't at the saved tile");
                    Check(e, Look(), fresh: true);
                    break;
                default:
                    throw new InvalidOperationException(e.Kind);
            }
        }

        /// <summary>Picks a fight from the list, or plays on the one the party walked into, to its end, and carries on from Results.</summary>
        private void Fight(Event e, Seen before)
        {
            if (_game.Phase != TacticsPhase.Battle)
            {
                foreach (var seat in _online)
                    Applied(() => Send(seat, new { action = "pickEncounter", data = new { encounter = "river_crossing" } }));
                Check(e, before);
            }
            if (_game.Phase == TacticsPhase.Results)
            {
                CarryOn();
                return;
            }
            if (_game.Phase != TacticsPhase.Battle)
                return;
            if (e.Player % 3 == 0)
            {
                NewGame(resume: true);
                foreach (var seat in Enumerable.Range(0, _players.Count))
                    Applied(() => Apply(new Event("conn", seat), Look()));
            }
            for (var i = 0; i < 150 && _game.Phase == TacticsPhase.Battle && !_over; i++)
            {
                foreach (var seat in _online.ToArray())
                    Applied(() => Send(seat, new { action = "endTurn" }));
                Applied(() => Advance(500));
            }
            if (_game.Phase == TacticsPhase.Battle)
                throw new Inconclusive();
            CarryOn();
        }

        private void CarryOn()
        {
            if (_game.Phase == TacticsPhase.Results)
                foreach (var seat in _online.ToArray())
                {
                    var seen = Look();
                    _seat = seat;
                    Send(seat, new { action = "next" });
                    Check(new Event("next", seat), seen);
                }
        }

        /// <summary>
        /// The stamp the story arms, from the document: River Crossing's while the run's ford fight is on offer, and none once it's won or
        /// in a run with no story.
        /// </summary>
        private BattleStage? ArmedStage()
        {
            if (_setup.Armed == 0 || _lease.World is not { } world)
                return null;
            var beat = _setup.Armed == 1 ? "ch1_ford_assault" : "ch1_river_crossing";
            return world.Beats.Contains(beat) ? null : StageBattleTests.Stage;
        }

        /// <summary>Runs battle code, setting aside "hero1 and hero2 share the tile", a battle bug of its own with nothing to do with walking.</summary>
        private static void Applied(Action battle)
        {
            try
            {
                battle();
            }
            catch (RulesInvariantException e) when (e.Message.Contains("share the tile", StringComparison.Ordinal))
            {
                throw new Inconclusive();
            }
        }

        private void Advance(int ms)
        {
            var target = _context.Clock.AddMilliseconds(ms);
            for (var fired = 0; !_over; fired++)
            {
                Must(fired < 3000, $"3000 timers in one advance in {_game.Phase}");
                var due = _context.Timers.Where(t => t.Value <= target).OrderBy(t => t.Value).ThenBy(t => t.Key, StringComparer.Ordinal).ToArray();
                if (due.Length == 0)
                    break;
                var (key, at) = (due[0].Key, due[0].Value);
                var before = _game.Phase == TacticsPhase.Battle ? null : Look();
                var late = !_late ? 0 : _lateness.Next(20) == 0 ? _lateness.Next(1500) : _lateness.Next(MaxLateMs);
                var fire = at.AddMilliseconds(late);
                if (fire > _context.Clock)
                    _context.Clock = fire;
                if (fire > target)
                    target = fire;
                _slackMs = Math.Max(_slackMs * 0.5, late);
                _context.Timers.Remove(key);
                var second = _context.Clock.ToUnixTimeSeconds();
                _firesEachSecond[second] = _firesEachSecond.GetValueOrDefault(second) + 1;
                Must(_firesEachSecond[second] <= 60 || _late || _game.Phase != TacticsPhase.Exploration, $"{_firesEachSecond[second]} timers in a second");
                _game.OnTimer(key);
                if (_context.Over)
                {
                    _over = true;
                    return;
                }
                if (before is not null && _game.Phase != TacticsPhase.Battle)
                    Check(new Event("fire:" + key), before);
            }
            if (_over)
                return;
            var seen = _game.Phase == TacticsPhase.Battle ? null : Look();
            if (target > _context.Clock)
                _context.Clock = target;
            if (seen is not null)
                Check(new Event("adv-end"), seen);
        }

        private void Check(Event e, Seen before, bool fresh = false)
        {
            var after = Look();
            var now = _context.Clock;
            var party = after.Party;
            if (party.Epoch != _epoch && (before.Phase == TacticsPhase.Results || after.Phase == TacticsPhase.Results))
            {
                // A fight in the world ended: the party was put back, as a teleport, somewhere it could have walked to, outside the stamp if
                // the fight is still on offer.
                Must(party.Epoch == _epoch + 1, $"the epoch went {_epoch} to {party.Epoch} after a fight");
                Must(party.Legs.IsEmpty, "legs across a teleport");
                Must(Tiles.Value.Contains(party.At), $"the party was put back where it can't walk from the start, {party.At}");
                Must(ArmedStage()?.Holds(party.At) != true, $"the party was put back inside the armed stamp, at {party.At}");
                Must(party.Arrived is TacticsGame.FellIn or TacticsGame.Woke, $"put back with Arrived {party.Arrived}");
                Must(party.Arrived != TacticsGame.Woke || party.At == Rules.RegionFor("ashen_ford")!.Def.Spawns.Recovery, "woke away from the recovery spawn");
                _epoch = party.Epoch;
            }
            Must(party.Epoch == _epoch, "the epoch changed");
            Must(Ground.Map.InBounds(party.At) && Ground.Walkable(party.At), $"the party is on a bad tile, {party.At}");
            Must(party.Legs.Length <= 12, "more than 12 legs");
            for (var i = 1; i < party.Legs.Length; i++)
                Must(party.Legs[i].Seq == party.Legs[i - 1].Seq + 1, "the legs have a gap");
            if (party.Legs.Length > 0)
                Must(party.Legs[^1].Seq == party.Seq && party.Legs[^1].To == party.At, "the last leg isn't the party");
            if (before.Timers.Contains(TacticsGame.Timers.Step) && !after.Timers.Contains(TacticsGame.Timers.Step))
                _settledAt = now;
            if (!fresh)
                CheckChange(e, before, after, now);
            if (_latched && !after.Timers.Contains(TacticsGame.Timers.Step) && after.Phase == TacticsPhase.Exploration)
            {
                // The latched tap has gone: anything else that stops the party (the leader going, the phase changing) forgot it already.
                if (after.Party.Seq == before.Party.Seq)
                {
                    var (dx, dy) = WorldWalk.Delta(Enum.Parse<WalkDir>(_dir!, ignoreCase: true));
                    Must(WorldWalk.Next(Ground, before.Party.At, Enum.Parse<WalkDir>(_dir!, ignoreCase: true), ClassDef.DefaultJump, _lastAlongX) is null,
                        $"a quick tap was let go and dropped, after {e}");
                    Must(after.Party.Facing == new Tile(before.Party.At.X + dx, before.Party.At.Y + dy), "a quick tap into a wall didn't turn the party");
                }
                Forget();
            }
            if (_seat >= 0 && before.Party.Leader == _players[_seat].Name || before.Phase != TacticsPhase.Exploration && after.Phase == TacticsPhase.Exploration)
                _leaderHeard = now;
            _seat = -1;
            if (after.Party.Seq != before.Party.Seq || after.Party.Blocked)
                _owed = false;      // the press was tried: stepped, or turned against a wall
            CheckLeaders(after);
            if (after.Phase == TacticsPhase.Exploration)
                CheckTimers(e, after, now);
            else
            {
                Must(!after.Timers.Overlaps([TacticsGame.Timers.Step, TacticsGame.Timers.Lead, TacticsGame.Timers.Save]), $"a walking timer in {after.Phase}");
                Must(!party.Held, "held out of exploration");
                if (before.Phase == TacticsPhase.Exploration && after.Phase == TacticsPhase.Battle)
                {
                    // Walked into the stamp: stopped one tile short of it, and saved there at once.
                    Must(ArmedStage() is { } armed && armed.Holds(party.Facing) && !armed.Holds(party.At) && party.At == before.Party.At && party.Seq == before.Party.Seq,
                        $"a fight started from {before.Party.At} at {party.At} on {e}");
                    Must(_lease.World!.At == party.At, "the place wasn't saved as the fight started");
                }
                Forget();       // whatever was held went with the walk
            }
        }

        private void CheckChange(Event e, Seen before, Seen after, DateTimeOffset now)
        {
            var (was, party) = (before.Party, after.Party);
            var moved = party.Seq - was.Seq;
            Must(moved is 0 or 1, $"Seq went {was.Seq} to {party.Seq}");
            if (moved == 0)
                Must(party.At == was.At, "moved with no Seq");
            else
                CheckStep(e, before, after, now);
            if (was.Leader != party.Leader)
            {
                Must(!(was.Held && party.Held), "the leader changed with the party held");
                var old = _players.FirstOrDefault(p => p.Name == was.Leader);
                var oldCanLead = old is not null && _online.Contains(_players.IndexOf(old)) && party.Party.Any(w => w.Name == old.Name && w.Here);
                var silence = e.Kind == "fire:lead" && now - _leaderHeard >= TimeSpan.FromMilliseconds(7999);
                if (oldCanLead && silence)
                    _silent.Add(old!.Profile!.Value.Value);
                // The takeback: the session's own leader takes it from a stand-in who could keep it only 3 s after the party stopped and 3 s
                // after the stand-in got it, so that the stand-in's pad doesn't go the moment a thumb lifts or reaches for it.
                var own = _players.FirstOrDefault(p => p.Profile!.Value.Value == _lease.World?.LastLeader);
                if (oldCanLead && !silence && own is not null && party.Leader == own.Name
                    && before.Phase == TacticsPhase.Exploration && after.Phase == TacticsPhase.Exploration)
                    Must(now - _settledAt >= TimeSpan.FromMilliseconds(2999), $"the lead went back {(now - _settledAt).TotalMilliseconds} ms after the party stopped or the stand-in got it, on {e}");
                (_leaderHeard, _settledAt) = (now, now);       // a new leader is heard, and has the pad for at least 3 s
                if (!after.Timers.Contains(TacticsGame.Timers.Step))
                    Forget();
            }
            if (e.Kind is "walk" or "junk" or "fire:save" && was.Held == party.Held && was.Blocked == party.Blocked && was.Seq == party.Seq
                && was.Facing == party.Facing && was.Leader == party.Leader && before.Phase == after.Phase && before.Notice == after.Notice)
                Must(before.Host == after.Host, "the host view changed with no step and no change in Held or Blocked");
            Must(!party.Blocked || !party.Held, "blocked and held at once");
            if (e.Kind == "walk" && before.Phase == after.Phase && was.Leader == party.Leader)
                Must(before.Phones.SequenceEqual(after.Phones), "a walk input changed a phone");
        }

        private void CheckStep(Event e, Seen before, Seen after, DateTimeOffset now)
        {
            var (from, leg) = (before.Party.At, after.Party.Legs[^1]);
            Must(_dir is not null, "stepped with nothing held");
            var step = WorldWalk.Next(Ground, from, Enum.Parse<WalkDir>(_dir!, ignoreCase: true), ClassDef.DefaultJump, _lastAlongX);
            Must(step?.To == leg.To, $"the leg {from} to {leg.To} isn't Next, {step?.To}");
            var move = step!.Value;
            Must(Ground.Walkable(move.To) && (move.Diagonal || WorldWalk.Legal(Ground, from, move.To, ClassDef.DefaultJump)), $"a step onto a blocked tile, {move.To}");
            if (_latched)
                Must(!after.Timers.Contains(TacticsGame.Timers.Step), "a quick tap took more than one step");
            var drop = Ground.HeightAt(from) - Ground.HeightAt(move.To);
            var ms = (int)Math.Round(300.0 * move.Cost * (move.Diagonal ? Math.Sqrt(2) : 1) + 60.0 * Math.Max(0, drop - 2));
            Must(leg.Ms == ms, $"the leg took {leg.Ms} ms, not {ms}");
            if (!move.Diagonal)
                _lastAlongX = move.To.Y == from.Y;
            if (_commits > 0 && !_late)
                Must((now - _lastCommit).TotalMilliseconds >= _lastMs - 0.5, $"a step came {(now - _lastCommit).TotalMilliseconds} ms after a {_lastMs} ms leg");
            if (_commits > 1 && _late)
                Must((now - _commitBefore).TotalMilliseconds >= _msBefore + _lastMs - 300.5, $"a burst: two steps in {(now - _commitBefore).TotalMilliseconds} ms");
            (_commitBefore, _msBefore) = (_lastCommit, _lastMs);
            (_lastCommit, _lastMs) = (now, leg.Ms);
            _commits++;
            if (before.Phase == after.Phase && before.Party.Leader == after.Party.Leader && e.Kind is not ("disc" or "conn"))
                Must(before.Phones.SequenceEqual(after.Phones), "a phone changed on a step");
        }

        private void CheckLeaders(Seen after)
        {
            if (after.Phase != TacticsPhase.Exploration)
                return;
            var here = after.Party.Party.Count(w => w.Here);
            var leads = after.Party.Party.Where(w => w.Leads).ToArray();
            Must(leads.Length == Math.Min(here, 1), $"{leads.Length} leaders with {here} here");
            Must(leads.All(w => w.Here), "the leader isn't here");
            // The leader is always a seated, connected player with a character, whenever anyone is.
            var leader = _game.Leader;
            Must((leader is not null) == (here > 0), $"a leader {leader?.Name} with {here} here");
            if (leader is not null)
                Must(_players.Contains(leader) && _online.Contains(_players.IndexOf(leader)) && after.Party.Party.Any(w => w.Name == leader.Name),
                    $"the leader {leader.Name} isn't a seated, connected player with a character");
        }

        private void CheckTimers(Event e, Seen after, DateTimeOffset now)
        {
            var party = after.Party;
            var stepping = after.Timers.Contains(TacticsGame.Timers.Step);    // a party against a wall stands, with its hold still running
            Must(after.Timers.Contains(TacticsGame.Timers.Lead) == (_game.Leader is not null), "the lead timer doesn't match the leader");
            Must(!party.Held || stepping, "held with no step timer");
            Must(!stepping || _heldUntil is not null || _latched, "a step timer with nothing held");
            if (_latched && !_late && _commits > 0)
                Must(now <= _lastCommit.AddMilliseconds(_lastMs + 1), $"a latched tap still waiting {(now - _lastCommit).TotalMilliseconds} ms after a {_lastMs} ms step");
            var slack = (_late ? _slackMs + 1 : 0) + (_setup.Clock == 2 ? 1 : 0);
            if (_heldUntil is { } until && now > until.AddMilliseconds(slack))
                Must(!stepping, $"still held {(now - until).TotalMilliseconds} ms past the lease");
            if (!stepping)
                Forget();
            if (_online.Count == 0)
                Must(after.Timers.Count == 0, $"timers with nobody online: {string.Join(",", after.Timers)}");

            var saved = _lease.World;
            var behind = saved is null || saved.At != party.At || saved.Facing != party.Facing;
            Must(!behind || after.Timers.Contains(TacticsGame.Timers.Save), "unsaved with no save coming");
            _behindSince = !behind ? null : _behindSince ?? now;
            if (_behindSince is { } since && !_late)
                Must((now - since).TotalSeconds <= 15.01, $"unsaved for {(now - since).TotalSeconds} s");

            // The session's leader has it back once the party has been stopped for 3 s, give or take a late timer.
            var takeback = _settledAt == DateTimeOffset.MinValue ? _settledAt : _settledAt.AddMilliseconds(3000 + (_late ? 1600 : 1));
            if (!stepping && now >= takeback && saved?.LastLeader is { } own && !_silent.Contains(own) && party.Party.FirstOrDefault(w => w.Id == own) is { Here: true } walker)
                Must(walker.Leads, $"the session's leader {own} is here and the party's been stopped since {_settledAt:HH:mm:ss.fff}, but {party.Leader} leads, after {e}");
        }
    }

    /// <summary>Even seeds are long walks with a phone pinging, odd ones a storm of everything.</summary>
    private static List<Event> Generate(int seed, int count)
    {
        var r = new Random(seed ^ 0x5bd1e995);
        return seed % 2 == 0 ? LongWalks(r, count) : Storm(r, count);
    }

    private static List<Event> LongWalks(Random r, int count)
    {
        var events = new List<Event>(count);
        while (events.Count < count)
        {
            var dir = Directions[r.Next(8)];
            var pings = r.Next(1, 90);
            for (var k = 0; k < pings && events.Count < count; k++)
            {
                events.Add(new Event("walk", -1, dir));
                events.Add(new Event("adv", Ms: r.Next(10) == 0 ? r.Next(1, 700) : 250));
                if (r.Next(25) == 0)
                    dir = Directions[r.Next(8)];
                if (r.Next(200) == 0)
                    events.Add(new Event(r.Next(2) == 0 ? "disc" : "conn", r.Next(4)));
                if (r.Next(60) == 0)
                {
                    events.Add(new Event("walk", -1));
                    events.Add(new Event("adv", Ms: r.Next(1, 1400)));
                }
            }
            events.Add(new Event("walk", -1));
            events.Add(new Event("adv", Ms: r.Next(1, 1500)));
            if (r.Next(3) == 0)     // a run of quick taps, some faster than the walk
                for (var k = r.Next(1, 10); k > 0; k--)
                {
                    events.Add(new Event("tap", -1, Directions[r.Next(8)], Ms: r.Next(1, 200)));
                    events.Add(new Event("adv", Ms: r.Next(1, 400)));
                }
            foreach (var (kind, odds) in new[] { ("end", 12), ("start", 12), ("battles", 12), ("back", 12), ("restart", 20), ("conn", 10) })
                if (r.Next(odds) == 0)
                    events.Add(new Event(kind, kind == "conn" ? r.Next(4) : 0));
        }
        return events;
    }

    private static List<Event> Storm(Random r, int count)
    {
        var events = new List<Event>(count);
        string? dir = null;
        var rhythm = r.Next(3) == 2 ? 100 : 250;
        for (var i = 0; i < count; i++)
        {
            var roll = r.Next(1000);
            if (roll < 330)
            {
                if (dir is null || r.Next(4) == 0)
                    dir = Directions[r.Next(8)];
                events.Add(new Event("walk", r.Next(12) == 0 ? r.Next(4) : -1, dir));
            }
            else if (roll < 370)
            {
                events.Add(new Event("walk", -1));
                dir = null;
            }
            else if (roll < 400)
            {
                events.Add(new Event("tap", r.Next(12) == 0 ? r.Next(4) : -1, Directions[r.Next(8)], Ms: r.Next(6) == 0 ? 0 : r.Next(1, 300)));
                dir = null;
            }
            else if (roll < 740)
                events.Add(new Event("adv", Ms: r.Next(6) switch { 0 => r.Next(1, 30), 1 => 66, 2 => 250, 3 => 300, 4 => r.Next(1, 800), _ => rhythm }));
            else if (roll < 765)
                events.Add(new Event("adv", Ms: r.Next(3) == 0 ? r.Next(8000, 20_000) : r.Next(1000, 3000)));
            else if (roll < 800)
                events.Add(new Event("disc", r.Next(4)));
            else if (roll < 820)      // a blip: a phone drops and comes back, often before a stand-in has had the pad long
                events.Add(new Event("blip", r.Next(5) == 0 ? -1 : r.Next(4), Ms: r.Next(3) == 0 ? 0 : r.Next(1, 4000)));
            else if (roll < 880)
                events.Add(new Event("conn", r.Next(4)));
            else if (roll < 900)
                events.Add(new Event("junk", r.Next(4), Ms: r.Next(100)));
            else if (roll < 925)
                events.Add(new Event("pick", r.Next(4)));
            else if (roll < 945)
                events.Add(new Event("battles", r.Next(4)));
            else if (roll < 965)
                events.Add(new Event("back", r.Next(4)));
            else if (roll < 972)
                events.Add(new Event("fight", r.Next(3)));
            else if (roll < 985)
                events.Add(new Event("restart"));
            else if (roll < 993)
            {
                events.Add(new Event("end", r.Next(4)));
                events.Add(new Event("start"));
            }
            else
                events.Add(new Event("walk", -1, "se"));
        }
        return events;
    }

    /// <summary>The failure, or null if the run held or stopped without a verdict.</summary>
    private static string? Failure(Setup setup, IReadOnlyList<Event> events)
    {
        try
        {
            new Run(setup).Play(events);
            return null;
        }
        catch (Inconclusive)
        {
            return null;
        }
        catch (Exception e)
        {
            return e is ModelBroke ? e.Message : e.ToString();
        }
    }

    private static string Kind(string failure) => new([.. failure.TakeWhile(c => !char.IsDigit(c) && c != '\n')]);

    /// <summary>Throws events away for as long as the same kind of failure still happens.</summary>
    private static List<Event> Shrink(Setup setup, List<Event> events, string failure)
    {
        for (var size = events.Count / 2; size >= 1; size /= 2)
            for (var i = 0; i + size <= events.Count;)
            {
                var fewer = events.Take(i).Concat(events.Skip(i + size)).ToList();
                if (Failure(setup, fewer) is { } again && Kind(again) == Kind(failure))
                    events = fewer;
                else
                    i += size;
            }
        return events;
    }

    [Fact]
    public void Walking_holds_up_against_its_model()
    {
        var total = int.TryParse(Environment.GetEnvironmentVariable("WALK_MODEL_EVENTS"), out var asked) ? asked : DefaultEvents;

        for (var seed = 1; seed <= Math.Max(1, total / EventsPerSeed); seed++)
        {
            var (setup, events) = (Setup.Of(seed), Generate(seed, EventsPerSeed));
            if (Failure(setup, events) is { } failure)
                Assert.Fail($"{setup}\n{failure}\nafter: {string.Join("; ", Shrink(setup, events, failure))}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_seat_that_drops_right_after_start_is_passed_over(int absent)
    {
        // Seats 1 and 2 of 4 away: the model's own check, on a walk that exercises what follows.
        var setup = new Setup(4, 4, 0, false, true, 0, "pr_member1", absent == 1 ? 0b0010 : 0b0110);
        Assert.Null(Failure(setup, Generate(4, EventsPerSeed)));
    }
}
