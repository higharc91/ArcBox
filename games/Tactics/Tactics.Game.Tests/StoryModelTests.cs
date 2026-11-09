using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Scenes against their invariants (overworld 5d): fixed seeds play Chapter 1's story beats under long, ugly runs of votes, picks, skips,
/// junk, phones coming and going, the leader leaving, restarts mid-line, mid-vote and mid-roll, and timers that fire late. After every event:
/// <list type="bullet">
/// <item>no walking state is active in a scene: no step, lead or save timer, and the party isn't held;</item>
/// <item>the cursor in the save always names a real line, and the line on the TV is the one saved;</item>
/// <item>a question closes on time while anyone can answer it: 45 s for a vote, 30 for a choice, 10 for a tie, 20 for who rolls;</item>
/// <item>a question asked once is never offered again, across a restart too, and the nodes played are never forgotten;</item>
/// <item>a restart resumes on the saved line, and a roll made is never made again;</item>
/// <item>the TV's line number only goes up;</item>
/// </list>
/// and at the end of each run, with everyone back, every scene ends. A failure names the seed and the events. <c>STORY_MODEL_SEEDS</c>
/// raises the number of runs.
/// </summary>
public sealed class StoryModelTests
{
    private const int EventsPerSeed = 250;
    // Current voiced line timings need more runs to cover a restart during the rare tie-break state.
    private const int DefaultSeeds = 64;
    private static readonly TimeSpan MaxLate = TimeSpan.FromMilliseconds(700);

    private static readonly string[] Backgrounds =
        ["soldier", "folk_hero", "noble", "urchin", "hermit", "sage", "entertainer", "outlander", "acolyte", "sailor", "guild_artisan", "criminal"];

    private static readonly Lazy<JsonElement> BaseDocument = new(() => ExplorationTests.DocumentAt(null));

    private static LoreBook Lore => StoryTests.Lore;

    private static Arc Arc => StoryTests.Arc;

    private sealed class ModelBroke(string message) : Exception(message);

    /// <summary>The server's clock and timers, where a timer can fire up to <see cref="MaxLate"/> late.</summary>
    private sealed class Context(int seed, bool late) : IGameContext
    {
        private readonly Random _lateness = new(seed * 17 + 3);

        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public Dictionary<string, (DateTimeOffset Due, DateTimeOffset Fires)> Timers { get; } = [];
        public Random Random { get; } = new(seed);

        public void SetTimer(string key, TimeSpan after)
        {
            if (after < TimeSpan.Zero)
                throw new ModelBroke($"negative timer {key}");
            var due = Now + after;
            Timers[key] = (due, late ? due + TimeSpan.FromMilliseconds(_lateness.Next((int)MaxLate.TotalMilliseconds)) : due);
        }

        public void CancelTimer(string key) => Timers.Remove(key);

        public DateTimeOffset? TimerDeadline(string key) => Timers.TryGetValue(key, out var t) ? t.Due : null;

        public void EndGame(GameResults results)
        {
            Timers.Clear();
            Over = true;
        }

        /// <summary>True once the game has ended the session: the VIP's endSession, from the walk or from inside a scene.</summary>
        public bool Over { get; private set; }

        /// <summary>Fires every timer due by <paramref name="target"/>, in the order they fire, then moves the clock there.</summary>
        public void Advance(TimeSpan by, Action<string> fire)
        {
            var target = Now + by;
            for (var guard = 0; guard < 10_000; guard++)
            {
                var next = Timers.Where(t => t.Value.Fires <= target).OrderBy(t => t.Value.Fires).ThenBy(t => t.Key, StringComparer.Ordinal)
                    .Select(t => (KeyValuePair<string, (DateTimeOffset Due, DateTimeOffset Fires)>?)t).FirstOrDefault();
                if (next is not { } timer)
                    break;
                Now = timer.Value.Fires > Now ? timer.Value.Fires : Now;
                Timers.Remove(timer.Key);
                fire(timer.Key);
            }
            Now = target;
        }
    }

    private sealed class Lease(CampaignInfo info, JsonElement document) : ICampaignLease
    {
        public CampaignInfo Info => info;
        public JsonElement Document { get; set; } = document.Clone();
        public TacticsDocument Read() => TacticsDocument.Read(Document);
        public void Save(JsonElement document) => Document = document.Clone();
        public void Checkpoint(JsonElement document, string label) => Save(document);
    }

    private sealed record Event(string Kind, int Seat, int N);

    /// <summary>The story so far before each beat a run can be sent to, on either branch.</summary>
    private static readonly (string Beat, TacticsSeed Seed)[] Starts =
    [
        (StoryTests.Mayor, new TacticsSeed([], [])),
        (StoryTests.FordChoice, new TacticsSeed([], [StoryTests.Mayor])),
        (StoryTests.OldMaud, new TacticsSeed(["ch1_skarrow_slain"], [StoryTests.Mayor, StoryTests.FordChoice, "ch1_ford_assault"])),
        (StoryTests.OldMaud, new TacticsSeed(["ch1_skarrow_spared"], [StoryTests.Mayor, StoryTests.FordChoice, "ch1_river_crossing"])),
    ];

    private sealed class Run
    {
        private readonly int _seed;
        private readonly Random _r;
        private readonly bool _late;

        /// <summary>The leader never votes or picks in this run, so ties go to tie-breaks.</summary>
        private readonly bool _quietLeader;
        private readonly List<Player> _players;
        private readonly Lease _lease;
        private readonly HashSet<int> _online = [];
        private Context _context;
        private TacticsGame _game = null!;
        private readonly List<string> _log = [];

        // What the model remembers.
        private int _lastSeq;
        private (string Beat, string Scene)? _sceneOf;
        private ImmutableHashSet<string> _played = [];
        private (string State, int Seq)? _question;

        /// <summary>The question being asked, by its line's number, and when that line ends: it mustn't close before.</summary>
        private (int Seq, DateTimeOffset Heard)? _asking;
        private DateTimeOffset _askedAt;
        private bool _wasAnswerable;
        private int _beatsDone;

        public Run(int seed)
        {
            (_seed, _r) = (seed, new Random(seed));
            _late = _r.Next(2) == 0;
            _quietLeader = _r.Next(2) == 0;
            var seats = _r.Next(1, 5);
            if (_quietLeader)
                seats = 4;      // three to split a vote while the leader says nothing
            var doc = TacticsDocument.Read(BaseDocument.Value);
            var members = doc.Members.Keys.OrderBy(_ => _r.Next()).ToArray();
            foreach (var member in members)
                doc = doc with
                {
                    Members = doc.Members.SetItem(member, doc.Members[member] with
                    {
                        Character = doc.Members[member].Character with
                        {
                            Background = Backgrounds[_r.Next(Backgrounds.Length)], Race = _r.Next(4) == 0 ? "half_orc" : "human",
                        },
                    }),
                };
            if (seats > 1 && _r.Next(4) == 0)
                doc = doc with { Members = doc.Members.Remove(members[seats - 1]) };      // the last seat has no character yet
            _players = [.. members.Take(seats).Select((m, i) => new Player(new PlayerId($"p{i}"), $"Player{i}", i == 0, new ProfileId(m)))];
            _lease = new Lease(new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", _players[0].Profile!.Value, [], 1), doc.ToJson());
            _context = new Context(seed, _late);
            Teleport(Starts[_quietLeader ? 1 : _r.Next(Starts.Length)]);     // a quiet leader starts at the ford's vote, for its ties
        }

        private void Must(bool ok, string what)
        {
            if (!ok)
                throw new ModelBroke($"seed {_seed}: {what}\n  after: {string.Join(" | ", _log.TakeLast(25))}");
        }

        private TacticsHostView Host => (TacticsHostView)_game.HostView();

        private void Send(int seat, object payload)
            => _game.OnInput(_players[seat].Id, new PlayerInput(JsonSerializer.SerializeToElement(payload, ArcJson.Options)));

        private int LeaderSeat() => _game.Leader is { } leader ? _players.FindIndex(p => p.Id == leader.Id) : -1;

        private bool HasCharacter(int seat) => _game.Document.For(_players[seat].Profile!.Value) is not null;

        private bool Answerable => _online.Any(HasCharacter);

        /// <summary>A new game on the saved document, as a server restart: nobody is connected at first, then the phones come back.</summary>
        private void Restart(int back)
        {
            var before = _lease.Read().World?.Story;
            var state = _game.Phase == TacticsPhase.Story ? Host.Story?.State : null;
            if (state is not null)
                RestartsIn.Add(state);
            _context = new Context(_seed * 7 + _log.Count, _late) { Now = _context.Now + TimeSpan.FromSeconds(3) };
            _game = new TacticsGame(Rules, null, Lore);
            _online.Clear();
            _game.Resume(_context, _players, _lease);
            (_question, _asking) = (null, null);
            // A scene waits for the party (5d §8, review finding 2): nothing plays and no clock runs until they're back.
            if (before is { Stage: StorySave.Lead or StorySave.After } && _game.Document.Battle is null)
            {
                Must(_game.Phase == TacticsPhase.Resuming, $"a restart in a scene went to {_game.Phase}, not Resuming");
                Must(_context.Timers.Count == 0, $"a scene's clock ran while it waited for the party: {string.Join(",", _context.Timers.Keys)}");
                _resumeFrom = before;
            }
            else
                CheckResumed(before);
            foreach (var seat in Enumerable.Range(0, _players.Count).Where(s => (back >> s & 1) == 1))
                Connect(seat, true);
        }

        /// <summary>The cursor a restart is coming back to, checked when the scene starts again.</summary>
        private StorySave? _resumeFrom;

        /// <summary>The states a scene was in when the server restarted, so the test can tell the runs restarted where it matters.</summary>
        public HashSet<string> RestartsIn { get; } = [];

        /// <summary>A new session on the saved document, as the next night: Muster, then the scene on its saved line.</summary>
        private void NewSession()
        {
            var before = _lease.Read().World?.Story;
            var resumable = before is { Stage: StorySave.Lead or StorySave.After, Scene: not null } && _lease.Read().Battle is null;
            _context = new Context(_seed * 11 + _log.Count, _late) { Now = _context.Now + TimeSpan.FromSeconds(3) };
            _game = new TacticsGame(Rules, null, Lore);
            _online.Clear();
            _online.UnionWith(Enumerable.Range(0, _players.Count));
            (_question, _asking) = (null, null);
            _game.Start(_context, _players, _lease);
            foreach (var seat in Enumerable.Range(0, _players.Count).Where(s => !HasCharacter(s)))
                Connect(seat, false);
            if (resumable)
            {
                _resumeFrom = before;
                Sessions++;
            }
        }

        public int Sessions { get; private set; }

        /// <summary>How many times the VIP ended the session mid-run.</summary>
        public int Ended { get; private set; }

        /// <summary>Moves the party next to <paramref name="start"/>'s beat with its story so far, as a fresh session, and starts it.</summary>
        private void Teleport((string Beat, TacticsSeed Seed) start)
        {
            var doc = _lease.Read();
            var at = StoryTests.NearSpot(start.Beat);
            doc = doc with
            {
                World = new WorldSave(StageBattleTests.Ford, at, new Tile(at.X + 1, at.Y), doc.World?.LastLeader, start.Seed.Flags, start.Seed.Beats,
                    StorySeq: doc.World?.StorySeq ?? 0),     // the campaign goes on: only its story is set back
            };
            _lease.Document = doc.ToJson();
            StartSession();
        }

        private void StartSession()
        {
            _context = new Context(_seed * 13 + _log.Count, _late) { Now = _context.Now + TimeSpan.FromSeconds(3) };
            _game = new TacticsGame(Rules, null, Lore);
            _online.Clear();
            _online.UnionWith(Enumerable.Range(0, _players.Count));
            _game.Start(_context, _players, _lease);
            foreach (var seat in Enumerable.Range(0, _players.Count).Where(s => !HasCharacter(s) || _r.Next(5) == 0))
                Connect(seat, false);
            (_sceneOf, _played, _question) = (null, [], null);
            if (_game.Phase == TacticsPhase.Exploration && LeaderSeat() is >= 0 and var lead)
                Send(lead, new { action = "interact" });
            if (_game.Phase == TacticsPhase.Exploration)
                for (var seat = 0; seat < _players.Count && _game.Phase == TacticsPhase.Exploration; seat++)
                    if (!_online.Contains(seat) && HasCharacter(seat))
                    {
                        Connect(seat, true);
                        if (LeaderSeat() is >= 0 and var led)
                            Send(led, new { action = "interact" });
                    }
            Check("start");
        }

        private void Connect(int seat, bool connected)
        {
            if (connected ? !_online.Add(seat) : !_online.Remove(seat))
                return;
            _game.OnPlayerConnectionChanged(_players[seat].Id, connected);
        }

        /// <summary>Between beats: walk on to the next one on offer, battles too, by a restart that puts the party beside it.</summary>
        private void NextBeat()
        {
            var doc = _lease.Read();
            var world = doc.World!;
            if (StoryRules.Available(Arc, world.Flags.ToHashSet(), world.Beats.ToHashSet()) is not { } beat)
            {
                Teleport(Starts[_r.Next(Starts.Length)]);
                return;
            }
            var at = StoryTests.NearSpot(beat.Id);
            _lease.Document = (doc with { World = world with { At = at, Facing = new Tile(at.X + 1, at.Y) } }).ToJson();
            StartSession();
        }

        /// <summary>
        /// A fight started from a battle beat: the model ends it as 5c would, won or lost, by a restart on the document a fight's end leaves
        /// (the fights themselves are <see cref="StageEndTests"/>'): won, its flags set and the beat at its aftermath; lost, still at its fight.
        /// </summary>
        private void EndFight()
        {
            var doc = _lease.Read();
            var world = doc.World!;
            Must(world.Story is { Stage: StorySave.Fight } c && c.Beat == doc.Battle?.Beat, "a story fight with no cursor at its fight");
            var beat = Arc.Beats.Single(b => b.Id == world.Story!.Beat);
            var won = _r.Next(4) > 0;
            if (_r.Next(2) == 0)
            {
                FightThrough(won);
                return;
            }
            if (won)
                world = world with { Flags = [.. world.Flags.Concat(beat.Sets).Distinct()], Story = world.Story! with { Stage = StorySave.After } };
            else
                _fightsLost++;
            _lease.Document = (doc with { Battle = null, World = world }).ToJson();
            _fights++;
            Restart((1 << _players.Count) - 1);
        }

        private int _fights, _fightsLost;

        /// <summary>How many fights went the real way: the engine's end, Results, and the VIP carrying on.</summary>
        public int RealFights { get; private set; }

        public int FightsLost => _fightsLost;

        /// <summary>
        /// The fight the real way (review finding 3): the engine ends it on its next command, as <see cref="StageEndTests"/> do, every player
        /// ending their turns as it plays on; then Results, and the VIP carries on into the aftermath or back to the world.
        /// </summary>
        private void FightThrough(bool won)
        {
            var engine = _game.ApplyEngine;
            var game = _game;
            game.ApplyEngine = (state, command, rules, ai) =>
            {
                game.ApplyEngine = engine;
                var r = engine(state, command, rules, ai);
                return r with { State = r.State with { Outcome = won ? BattleOutcome.Won : BattleOutcome.Lost } };
            };
            for (var i = 0; i < 2_000 && _game.Phase is TacticsPhase.Battle or TacticsPhase.Resuming; i++)
            {
                if (_game.Phase == TacticsPhase.Resuming)
                    foreach (var seat in Enumerable.Range(0, _players.Count))
                        Connect(seat, true);
                foreach (var seat in _online.ToArray())
                    if (_game.PlayerView(_players[seat].Id) is CustomView { Screen: TacticsGame.Screens.Turn })
                        Send(seat, new { action = "endTurn" });
                    else if (_game.PlayerView(_players[seat].Id) is CustomView { Screen: TacticsGame.Screens.React })
                        Send(seat, new { action = "react", data = new { choice = "pass" } });
                _context.Advance(TimeSpan.FromSeconds(7), Fire);
            }
            Must(_game.Phase == TacticsPhase.Results, $"a fight that should have ended is at {_game.Phase}");
            var world = _lease.Read().World!;
            Must(won ? world.Story is { Stage: StorySave.After } : world.Story is { Stage: StorySave.Fight }, $"a fight {(won ? "won" : "lost")} left the cursor at {world.Story?.Stage}");
            (_fights, _fightsLost, RealFights) = (_fights + 1, _fightsLost + (won ? 0 : 1), RealFights + 1);
            if (_online.Count == 0)
                Connect(0, true);
            // The VIP: the platform's, if here, else the first here (TacticsGame.Vip).
            var vip = _players.FindIndex(p => p.IsVip) is var v and >= 0 && _online.Contains(v) ? v : _online.Order().First();
            Send(vip, new { action = "next" });
            Check("carry on");
        }

        public void Play(int events)
        {
            for (var i = 0; i < events; i++)
                Step(Next());
            Drain();
        }

        private Event Next()
        {
            var roll = _r.Next(100);
            var seat = _r.Next(_players.Count);
            if (_game.Phase == TacticsPhase.Resuming && _r.Next(3) == 0)
                return new Event(_r.Next(2) == 0 ? "continue" : "conn", seat, 0);
            // The rare moments to restart in: a roll's reveal, the gap between scenes, a tie-break.
            if (Host.Story?.State is TacticsGame.StoryStates.Rolled or TacticsGame.StoryStates.Gap or TacticsGame.StoryStates.TieBreak
                && _r.Next(Host.Story.State == TacticsGame.StoryStates.TieBreak ? 2 : 4) == 0)
                return new Event(_r.Next(3) == 0 ? "session" : "restart", seat, _r.Next(1 << _players.Count));
            // A quiet leader's party vote: two others split it, one each way, and nobody else votes, so it ties.
            if (_quietLeader && Host.Story is { Ask: { Party: true, Tie: false } ask } && _r.Next(2) == 0)
            {
                var votedFor = ask.Options.SelectMany(o => o.Voters).ToHashSet();
                var splitter = Enumerable.Range(0, _players.Count)
                    .FirstOrDefault(p => p != LeaderSeat() && _online.Contains(p) && HasCharacter(p) && !votedFor.Contains(_players[p].Name), -1);
                return ask.Voted < 2 && splitter >= 0 && ask.Options.Length >= 2
                    ? new Event("split", splitter, ask.Voted)
                    : new Event("wait", seat, _r.Next(1, 6_000));
            }
            if (Host.Story is { Ask: not null } or { Check: not null } && _r.Next(2) == 0)      // a question open: answers come thick and fast
                return new Event(Host.Story.Ask is not null ? "vote" : "pick", seat, _r.Next(8));
            return roll switch
            {
                < 30 => new Event("wait", seat, _r.Next(4) == 0 ? _r.Next(10_000, 40_000) : _r.Next(1, 6_000)),
                < 45 => new Event("vote", seat, _r.Next(8)),
                < 53 => new Event("pick", seat, _r.Next(8)),
                < 61 => new Event("skip", seat, _r.Next(3)),
                < 68 => new Event("disc", seat, 0),
                < 76 => new Event("conn", seat, 0),
                < 79 => new Event("leaderLeaves", seat, 0),
                < 83 => new Event("restart", seat, _r.Next(1 << _players.Count)),
                < 84 => new Event("session", seat, 0),
                < 88 => new Event("walk", seat, _r.Next(9)),
                < 91 => new Event("pickClass", seat, 0),
                < 94 => new Event("interact", seat, 0),
                _ => new Event("junk", seat, _r.Next(6)),
            };
        }

        private void Step(Event e)
        {
            _log.Add($"{e.Kind}:{e.Seat}:{e.N}@{_context.Now:mm:ss.fff}");
            var telling = _game.Phase == TacticsPhase.Story;
            switch (e.Kind)
            {
                case "wait":
                    _context.Advance(TimeSpan.FromMilliseconds(e.N), Fire);
                    break;
                case "vote" or "pick" when _quietLeader && e.Seat == LeaderSeat():
                    break;
                case "split":
                    Send(e.Seat, new { action = "vote", data = new { option = Host.Story!.Ask!.Options[e.N].Id } });
                    break;
                case "vote" when _online.Contains(e.Seat):
                {
                    var options = Host.Story?.Ask?.Options.Select(o => o.Id).ToArray() ?? [];
                    var option = options.Length > 0 && e.N < 6 ? options[_quietLeader ? e.Seat % Math.Min(2, options.Length) : e.N % options.Length] : "stale_" + e.N;
                    Send(e.Seat, new { action = "vote", data = new { option } });
                    break;
                }
                case "pick" when _online.Contains(e.Seat):
                {
                    var cards = Host.Story?.Check?.Cards.Select(c => c.Member).ToArray() ?? [];
                    var member = cards.Length > 0 && e.N < 6 ? cards[e.N % (_quietLeader ? Math.Min(2, cards.Length) : cards.Length)] : "pr_nobody";
                    Send(e.Seat, new { action = "pickRoller", data = new { member } });
                    break;
                }
                case "skip" when _online.Contains(e.Seat):
                {
                    var line = e.N switch { 0 => Host.Story?.Line?.Id, 1 => "an_old_line", _ => null };
                    Send(e.Seat, line is null ? new { action = "skip" } : new { action = "skip", data = (object)new { line } });
                    break;
                }
                case "disc":
                    Connect(e.Seat, false);
                    break;
                case "conn":
                    Connect(e.Seat, true);
                    break;
                case "leaderLeaves" when LeaderSeat() is >= 0 and var lead:
                    Connect(lead, false);
                    break;
                case "restart":
                    Restart(e.N);
                    break;
                case "session":
                    NewSession();
                    break;
                case "continue" when _online.Contains(e.Seat):
                    Send(e.Seat, new { action = "continue" });
                    break;
                case "walk" when _online.Contains(e.Seat):
                {
                    var before = Host.Explore;
                    string?[] dirs = ["n", "ne", "e", "se", "s", "sw", "w", "nw", null];
                    Send(e.Seat, new { action = "walk", data = new { dir = dirs[e.N] } });
                    if (_game.Phase == TacticsPhase.Story)
                        Must((before!.Seq, before.At, before.Held) == (Host.Explore!.Seq, Host.Explore.At, Host.Explore.Held), "the party walked in a scene");
                    break;
                }
                case "pickClass" when _online.Contains(e.Seat):
                    Send(e.Seat, new { action = "pickClass", data = new { @class = "rogue" } });
                    break;
                case "interact" when _online.Contains(e.Seat):
                    Send(e.Seat, new { action = "interact" });
                    break;
                case "junk" when _online.Contains(e.Seat):
                {
                    object[] junk =
                    [
                        new { action = "vote" }, new { action = "vote", data = new { option = 3 } }, new { action = "pickRoller", data = 7 },
                        new { action = "skip", data = new { line = 4 } }, new { action = "next" }, new { action = "endSession" },
                    ];
                    Send(e.Seat, junk[e.N]);
                    if (_context.Over)
                    {
                        // The VIP ended the session, in a scene too (review): the next night picks up on the saved line.
                        Must(e.N == 5, $"the session ended on {e.Kind}:{e.Seat}:{e.N}");
                        Ended++;
                        NewSession();
                    }
                    break;
                }
            }
            Check(e.Kind);
            if (_game.Phase == TacticsPhase.Battle && _game.Document.Battle?.Beat is not null)
                EndFight();
            if (_game.Phase != TacticsPhase.Exploration)
                return;
            if (telling)
                _beatsDone++;
            NextBeat();
        }

        private void Fire(string key)
        {
            _game.OnTimer(key);
            Check($"timer {key}");
        }

        /// <summary>Everyone comes back and nobody touches a thing: the scene, and its beat, have to end.</summary>
        private void Drain()
        {
            for (var seat = 0; seat < _players.Count; seat++)
                if (HasCharacter(seat))
                    Connect(seat, true);
            _log.Add("everyone back");
            Check("everyone back");
            var end = _context.Now + TimeSpan.FromMinutes(30);
            while (_game.Phase == TacticsPhase.Story && _context.Now < end)
            {
                _context.Advance(TimeSpan.FromSeconds(5), Fire);
                Check("drain");
            }
            Must(_game.Phase != TacticsPhase.Story, "a scene didn't end in half an hour with everyone here and nobody answering");
            _beatsDone++;
        }

        // ---- The invariants ---------------------------------------------------------------------------------------------

        private void Check(string what)
        {
            var host = Host;
            var saved = _lease.Read().World;
            var cursor = saved?.Story;
            if (_log.Count == 0)
                _log.Add(what);
            _log[^1] += $" [{_game.Phase} {host.Story?.State} {cursor?.Scene}/{cursor?.Node}/{cursor?.Line} {string.Join(",", _context.Timers.Keys)}]";

            if (_game.Phase == TacticsPhase.Story)
            {
                Must(!_context.Timers.Keys.Intersect(StageBattleTests.WalkingTimers).Any(), $"a walking timer in a scene ({what})");
                Must(!host.Explore!.Held && !host.Explore.Blocked, "the party is held in a scene");
                if (host.Chapter is null)
                {
                    Must(cursor is { Stage: StorySave.Lead or StorySave.After }, "a scene with no lead-in or aftermath cursor saved");
                    Must(host.Story is not null, "a scene with no story view");
                }
                else
                    Must(cursor is null && host.Story is null, "a chapter card over a scene");
            }
            else
                Must(_game.Phase switch
                {
                    TacticsPhase.Muster => true,
                    TacticsPhase.Resuming => _context.Timers.Keys.All(k => k is not (TacticsGame.Timers.Line or TacticsGame.Timers.Vote)),
                    TacticsPhase.Exploration or TacticsPhase.Battle => cursor is null or { Stage: StorySave.Fight },
                    TacticsPhase.Results => cursor is null or { Stage: StorySave.Fight or StorySave.After },
                    _ => cursor is null,
                }, $"a {cursor?.Stage} cursor saved in {_game.Phase}");
            if (_game.Phase == TacticsPhase.Battle && cursor is not null)
                Must(_game.Document.Battle?.Beat == cursor.Beat, "a fight that isn't the cursor's beat");

            // A question never closes before the line that asks it has been heard, and a question that holds runs no clock.
            var story0 = _game.Phase == TacticsPhase.Story ? host.Story : null;
            if (_asking is { } asked && !(story0 is { State: TacticsGame.StoryStates.Asking } a && a.Seq == asked.Seq))
            {
                Must(_context.Now >= asked.Heard, $"a question closed at {_context.Now:mm:ss.fff}, before its line ended at {asked.Heard:mm:ss.fff}");
                _asking = null;
            }
            if (story0 is { State: TacticsGame.StoryStates.Asking, Until: { } heard } asking)
                _asking = (asking.Seq, heard);
            if (story0 is { State: TacticsGame.StoryStates.Asking or TacticsGame.StoryStates.TieBreak or TacticsGame.StoryStates.PickRoller } open
                && (open.Ask?.Deadline ?? open.Check?.Deadline) is null)
                Must(!_context.Timers.ContainsKey(TacticsGame.Timers.Vote), "a question holds with its clock still running");
            if (host.Chapter is not null)
                States.Add("card");
            if (_resumeFrom is { } from && _game.Phase == TacticsPhase.Story)
            {
                _resumeFrom = null;
                CheckResumed(from);
            }
            if (cursor is not null)
                CheckCursor(cursor, saved!);
            if (host.Story is { } story)
                CheckView(story, cursor!);
            else
                _question = null;
        }

        /// <summary>The cursor names a real line of a beat that isn't done, and the nodes played are never forgotten.</summary>
        private void CheckCursor(StorySave c, WorldSave world)
        {
            if (c.Scene == TacticsGame.LootVoteId)
            {
                Must(world.Loot is { Voting: true, Items.IsEmpty: false }, "a loot cursor without pending rewards");
                Must(c.Beat == "" && c.Stage == StorySave.Lead && c.Node == "ask" && c.Line == 0 && c.Roll is null,
                    "the reward vote has an invalid cursor");
                Must(c.Played.SequenceEqual(["ask"]), "the reward question isn't the only played node");
                Must(world.Loot!.Items.All(i => i.Count > 0 && Rules.Items.ContainsKey(new ItemId(i.Item))), "invalid items in the vote queue");
                (_sceneOf, _played) = (null, []);
                return;
            }
            var beat = Arc.Beats.FirstOrDefault(b => b.Id == c.Beat);
            Must(beat is not null && !world.Beats.Contains(c.Beat), $"the cursor's beat {c.Beat} isn't one on offer");
            if (c.Stage == StorySave.Fight || c.Scene is null)
            {
                Must(beat!.Kind == ArcBeat.Battle && c.Stage != StorySave.Lead && c.Node is null && c.Played.IsEmpty,
                    $"a {c.Stage} cursor with no scene for {c.Beat}");
                (_sceneOf, _played) = (null, []);
                return;
            }
            Must(c.Stage == StorySave.Lead || beat!.Kind == ArcBeat.Battle, $"a {c.Stage} cursor on {c.Beat}");
            var scene = Lore.ScenesOf(c.Beat).FirstOrDefault(s => s.Id == c.Scene && s.After == (c.Stage == StorySave.After));
            Must(scene is not null, $"the cursor's scene {c.Scene} isn't one of {c.Beat}'s {c.Stage}");
            if (c.Node is { } id)
            {
                var node = scene!.Node(id);
                Must(node is not null && c.Line >= 0 && c.Line < node.Lines.Length, $"the cursor {id}/{c.Line} isn't a line");
                Must(c.Played.Contains(id) || c.Roll is not null, $"the cursor's node {id} isn't played");
            }
            else
                Must(c.Played.IsEmpty && c.Line == 0, "a cursor between scenes with something played");
            Must(c.Roll is null || c.Line == 0, "a roll saved away from its node's first line");
            Must(c.Played.All(p => scene!.Node(p) is not null), "a played node that isn't the scene's");

            if (_sceneOf == (c.Beat, c.Scene!))
                Must(_played.All(c.Played.Contains), $"played nodes forgotten: had {string.Join(",", _played)}, now {string.Join(",", c.Played)}");
            (_sceneOf, _played) = ((c.Beat, c.Scene!), [.. c.Played]);
        }

        private void CheckView(StoryView story, StorySave c)
        {
            States.Add(story.State);
            Must(story.Seq >= _lastSeq, $"the line number went back from {_lastSeq} to {story.Seq}");
            _lastSeq = story.Seq;
            Must(story.Scene == c.Scene, $"the TV shows {story.Scene}, the save says {c.Scene}");
            var reward = c.Scene == TacticsGame.LootVoteId ? _lease.Read().World!.Loot : null;
            var node = c.Node is null || reward is not null ? null : Lore.ScenesOf(c.Beat).Single(s => s.Id == c.Scene).Node(c.Node);
            switch (story.State)
            {
                case TacticsGame.StoryStates.Lines or TacticsGame.StoryStates.Asking:
                    if (reward is not null)
                        Must(c.Roll is null && story.Line?.Id == $"{TacticsGame.LootVoteId}.{reward.Seq}" && story.Seq == c.Seq,
                            "the reward line does not match its saved queue position");
                    else
                        Must(c.Roll is null && node is not null && story.Line?.Id == node.Lines[c.Line].Id && story.Seq == c.Seq,
                            $"the TV's line {story.Line?.Id} #{story.Seq} isn't the saved {c.Node}/{c.Line} #{c.Seq}");
                    break;
                case TacticsGame.StoryStates.Rolled:
                    Must(c.Roll is not null, "a roll shown that isn't saved");
                    break;
                case TacticsGame.StoryStates.Gap:
                    Must(c.Node is null, "a gap with a node saved");
                    break;
            }

            if (reward is not null && story.Ask is { } allocation)
            {
                Must(allocation.Party && allocation.Options.Select(o => o.Id).SequenceEqual(reward.Choices), "the reward question differs from saved recipients");
                Must(reward.Choices.All(_lease.Read().Members.ContainsKey), "an unknown member can take a reward");
                Must(reward.Choices.Distinct(StringComparer.Ordinal).Count() == reward.Choices.Length, "a reward recipient is offered twice");
            }

            // A question offers nothing that leads back to a node already played.
            if (story.Ask is { } ask && node is not null)
            {
                foreach (var option in ask.Options)
                {
                    var def = node.Options.SingleOrDefault(o => o.Id == option.Id);
                    Must(def is not null, $"the option {option.Id} isn't {node.Id}'s");
                    Must(!def!.Targets.Any(c.Played.Contains), $"{option.Id} is offered again, though it leads back to a node played");
                }
            }

            // A question closes on time while anyone can answer it.
            if (story.State is TacticsGame.StoryStates.Asking or TacticsGame.StoryStates.TieBreak or TacticsGame.StoryStates.PickRoller)
            {
                var asked = (story.State, story.Seq);
                var answerable = Answerable;
                if (_question != asked || (answerable && !_wasAnswerable))
                    (_question, _askedAt) = (asked, _context.Now);
                _wasAnswerable = answerable;
                var limit = story.State switch
                {
                    TacticsGame.StoryStates.TieBreak => StoryTiming.Default.TieBreak,
                    TacticsGame.StoryStates.PickRoller => StoryTiming.Default.Roller,
                    _ when reward is not null => TacticsGame.LootVoteLength,
                    _ when story.Ask is { Party: false } => StoryTiming.Default.Choice,
                    _ => StoryTiming.Default.Vote,
                };
                if (story.Line is { } line && story.State == TacticsGame.StoryStates.Asking)
                {
                    var heard = StoryTiming.Default.Line(Lore.ClipSeconds(line.Id), line.Text);
                    limit = heard > limit ? heard : limit;
                }
                if (answerable)
                    Must(_context.Now - _askedAt <= limit + MaxLate + TimeSpan.FromMilliseconds(5),
                        $"a question ({story.State}) has been open {(_context.Now - _askedAt).TotalSeconds:0.0} s, past {limit.TotalSeconds} s");
            }
            else
                _question = null;
        }

        /// <summary>Right after a restart: the scene is back on the line saved, and a roll made isn't made again.</summary>
        private void CheckResumed(StorySave? before)
        {
            var after = _lease.Read().World?.Story;
            if (before is null)
            {
                Must(_game.Phase != TacticsPhase.Story, "a restart with no cursor went into a scene");
                return;
            }
            if (before.Stage == StorySave.Fight || before.Scene is null)
            {
                Must(before.Stage != StorySave.Fight || (_game.Phase != TacticsPhase.Story && after == before),
                    $"a restart at a fight went to {_game.Phase} with {after?.Stage}");
                return;
            }
            Must(_game.Phase == TacticsPhase.Story, $"a restart with a cursor went to {_game.Phase}");
            var story = Host.Story!;
            Must(after is not null && after.Scene == before.Scene && after.Beat == before.Beat, "a restart moved the scene");
            Must(after!.Roll == before.Roll, "a restart changed the roll");
            Must(after.Played.SequenceEqual(before.Played) || before.Node is null, "a restart changed what's played");
            if (before.Scene == TacticsGame.LootVoteId)
            {
                var reward = _lease.Read().World!.Loot!;
                Must(story.Line?.Id == $"{TacticsGame.LootVoteId}.{reward.Seq}" && after.Node == before.Node && after.Line == before.Line,
                    "a restart moved the pending reward");
                Must(story.Seq == before.Seq + 1, "the resumed reward question wasn't repeated");
            }
            else if (before.Roll is not null)
                Must(story.State == TacticsGame.StoryStates.Rolled && after.Node == before.Node, "a restart after a roll didn't show it again");
            else if (before.Node is null)
            {
                var scene = Lore.ScenesOf(before.Beat).Single(s => s.Id == before.Scene);
                Must(story.Line?.Id == scene.Node(scene.Start)!.Lines[0].Id, "a restart between scenes didn't start the next one");
            }
            else
            {
                var node = Lore.ScenesOf(before.Beat).Single(s => s.Id == before.Scene).Node(before.Node)!;
                Must(story.Line?.Id == node.Lines[before.Line].Id && (after.Node, after.Line) == (before.Node, before.Line),
                    $"a restart on {before.Node}/{before.Line} resumed on {after.Node}/{after.Line}");
                Must(story.Seq == before.Seq + 1, "a resumed line isn't said again");
            }
        }

        public int BeatsDone => _beatsDone;

        public int Fights => _fights;

        /// <summary>Every state a scene was seen in, so the test can tell the runs reached ties, picks and rolls.</summary>
        public HashSet<string> States { get; } = [];
    }

    private static int Seeds => int.TryParse(Environment.GetEnvironmentVariable("STORY_MODEL_SEEDS"), out var n) && n > 0 ? n : DefaultSeeds;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_short_story_trace_checks_the_model_on_every_transition(int seed)
    {
        var run = new Run(seed);
        run.Play(32);
        Assert.NotEmpty(run.States);
    }

    [Fact]
    [Trait("Category", "Sim")]
    public void Scenes_hold_up_against_their_model()
    {
        var beats = 0;
        var (fights, sessions, real, lost, ended) = (0, 0, 0, 0, 0);
        var states = new HashSet<string>();
        var restarts = new HashSet<string>();
        var only = int.TryParse(Environment.GetEnvironmentVariable("STORY_MODEL_SEED"), out var one) ? one : 0;
        for (var seed = only > 0 ? only : 1; seed <= (only > 0 ? only : Seeds); seed++)
        {
            var run = new Run(seed);
            run.Play(EventsPerSeed);
            beats += run.BeatsDone;
            states.UnionWith(run.States);
            restarts.UnionWith(run.RestartsIn);
            (fights, sessions, real, lost, ended) = (fights + run.Fights, sessions + run.Sessions, real + run.RealFights, lost + run.FightsLost, ended + run.Ended);
        }
        string[] all = [TacticsGame.StoryStates.Lines, TacticsGame.StoryStates.Asking, TacticsGame.StoryStates.TieBreak,
            TacticsGame.StoryStates.PickRoller, TacticsGame.StoryStates.Rolled, TacticsGame.StoryStates.Gap];
        Assert.Empty(all.Except(states));
        // The rare moments the runs must reach: a restart mid-roll, between scenes, on a tie-break, and in a line and a question; a new
        // session on a saved scene; a fight from the story.
        Assert.Empty(new[] { TacticsGame.StoryStates.Rolled, TacticsGame.StoryStates.Gap, TacticsGame.StoryStates.TieBreak,
            TacticsGame.StoryStates.Lines, TacticsGame.StoryStates.Asking }.Except(restarts));
        Assert.True(sessions > 0, "no new session started on a saved scene");
        Assert.True(ended > 0, "the VIP never ended a session");
        Assert.True(fights > 0, "no fight started from the story");
        Assert.True(real > 0 && lost > 0 && fights > lost, $"fights: {fights}, {real} the real way, {lost} lost: the runs need wins and losses, through Results");
        Assert.Contains("card", states);      // a run reached the end of the chapter
        Assert.True(beats >= Seeds, $"only {beats} beats were finished in {Seeds} runs: the model isn't reaching the ends of scenes");
    }
}
