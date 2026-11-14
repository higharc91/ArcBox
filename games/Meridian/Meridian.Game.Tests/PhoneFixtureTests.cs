using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meridian.Game.Tests;

/// <summary>
/// Pins every phone screen state to a checked-in fixture under <c>web/play/test/fixtures/meridian/</c>, which the phone
/// module's Vitest tests render (M2 plan, Task 6). Each fixture is a real view from a real session. Run with
/// <c>UPDATE_FIXTURES=1</c> to rewrite them after a deliberate change, and check the diff.
/// </summary>
public sealed class PhoneFixtureTests
{
    public static readonly string[] Names =
    [
        "crewPick", "crewPick-taken", "turn", "turn-no-moves", "turn-with-items", "orient", "card", "roll-check",
        "roll-breach", "trade", "sheet", "watch-reveal", "watch-card", "watch-roll", "over",
        // The endgame (M3 plan, Task 12).
        "briefing", "briefing-host", "briefing-mimic", "turn-endgame", "turn-monsters", "allocate", "vote", "vote-cast",
        "echo", "sheet-endgame", "debrief-won", "debrief-lost",
    ];

    private static readonly string[] Endgame = Names[15..];

    [Fact]
    public void Every_screen_state_matches_its_fixture()
    {
        var screens = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        void Keep(string name, ControllerView view) => screens.TryAdd(name, Node(view));

        var boarding = new Harness();
        Keep("crewPick", boarding.H.PlayerView(0));
        boarding.Send(0, "pick", new { crew = boarding.Pack.Crew[0].Id.Value });
        Keep("crewPick-taken", boarding.H.PlayerView(1));

        // Play seeded games until every Explore state that play reaches has been seen.
        for (var seed = 1; seed <= 40 && !Names.Except(Endgame).Where(n => n is not ("turn-with-items" or "trade")).All(screens.ContainsKey); seed++)
        {
            var game = new Harness(players: 3, seed: seed).Board();
            var rng = Rng.Seeded((ulong)seed, 99);
            for (var i = 0; i < 3000; i++)
            {
                for (var p = 0; p < 3; p++)
                {
                    if (Classify(game, p) is { } name)
                    {
                        Keep(name, game.H.PlayerView(p));
                    }
                }
                if (game.Session.Phase != SessionPhase.Explore)
                {
                    game.PlayBots();   // play on through the endgame to the end screen
                    Keep("over", game.H.PlayerView(0));
                    break;
                }
                if (!game.Session.Queue.IsEmpty)
                {
                    game.Presented(game.Session.Queue[0].Seq);
                    continue;
                }
                var seat = BotRunner.Actor(game.State);
                game.Do(ExploreBot.Instance.Choose(game.Pack, game.State, seat, Rules.Legal(game.Pack, game.State, seat), ref rng));
            }
        }

        // The endgame's states, from whole games of the revealed and the hidden traitor.
        foreach (var scenario in new[] { ThePassenger.Id, OneOfUsIsnt.Id })
        {
            for (var seed = 1; seed <= 40 && !Endgame.All(screens.ContainsKey); seed++)
            {
                var game = new Harness(players: 4, seed: seed, scenario: scenario).Board();
                game.PlayUntil(() =>
                {
                    for (var p = 0; p < 4; p++)
                    {
                        if (ClassifyEndgame(game, p) is { } name)
                        {
                            Keep(name, game.H.PlayerView(p));
                        }
                    }
                    return false;
                }, steps: 6000, seed: (ulong)seed);
            }
        }

        // Bots never trade and start empty-handed, so those two are set up directly.
        var held = new Harness().Board();
        var actor = held.Actor;
        var other = (actor + 1) % 3;
        var state = held.State;
        state = state.WithCrew(state.Crew[actor] with { Items = [new CardId("med_kit"), new CardId("crowbar")] })
                     .WithCrew(state.Crew[other] with { Items = [new CardId("hand_lamp")] }) with
                     {
                         Salvage = state.Salvage with { Draw = state.Salvage.Draw.Where(c => c.Value is not ("med_kit" or "crowbar" or "hand_lamp")).ToEqArray() },
                     };
        held.Game.Load(held.Session with { Game = state });
        Keep("turn-with-items", held.H.PlayerView(actor));
        held.Send(actor, "offerTrade", new { to = held.Seat(other).Value, give = new[] { "crowbar" }, take = new[] { "hand_lamp" } });
        Keep("trade", held.H.PlayerView(other));

        Assert.Equal(Names.Order(), screens.Keys);
        Check(screens, "meridian");
    }

    /// <summary>A real TV view mid-game, with an event being presented, for the dev board's tests.</summary>
    [Fact]
    public void The_host_view_matches_its_fixture()
    {
        var game = new Harness(players: 4, seed: 3).Board();
        var rng = Rng.Seeded(3, 99);
        for (var i = 0; i < 400 && game.Session.Phase == SessionPhase.Explore; i++)
        {
            if (!game.Session.Queue.IsEmpty)
            {
                if (game.Session.Queue[0].Kind == "reveal" && game.State.Ship.Compartments.Count >= 9)
                {
                    break;
                }
                game.Presented(game.Session.Queue[0].Seq);
                continue;
            }
            var seat = BotRunner.Actor(game.State);
            game.Do(ExploreBot.Instance.Choose(game.Pack, game.State, seat, Rules.Legal(game.Pack, game.State, seat), ref rng));
        }
        var view = (MeridianHostView)game.Game.HostView();
        Assert.NotNull(view.Now);

        // The endgame: The Passenger's brood on the board, between presentations.
        var endgame = new Harness(players: 4, seed: 3, ThePassenger.Id).Board();
        endgame.PlayUntil(() => endgame.Session is { Phase: SessionPhase.Endgame, Queue.IsEmpty: true } && endgame.State.Round > endgame.State.Board!.StartRound);
        // The Debrief: One of Us Isn't, every role revealed.
        var debrief = new Harness(players: 4, seed: 1, OneOfUsIsnt.Id).Board();
        debrief.PlayUntil(() => debrief.Session.Phase == SessionPhase.Debrief);
        Assert.Equal(SessionPhase.Endgame, endgame.Session.Phase);
        Assert.Equal(SessionPhase.Debrief, debrief.Session.Phase);

        Check(new SortedDictionary<string, JsonNode>(StringComparer.Ordinal)
        {
            ["explore"] = JsonSerializer.SerializeToNode(view, ArcJson.Options)!,
            ["endgame"] = JsonSerializer.SerializeToNode(endgame.Game.HostView(), ArcJson.Options)!,
            ["debrief"] = JsonSerializer.SerializeToNode(debrief.Game.HostView(), ArcJson.Options)!,
        }, "meridian-host");
    }

    private static string? ClassifyEndgame(Harness game, int p)
    {
        if (game.H.PlayerView(p) is not CustomView view || game.Session.Phase is not (SessionPhase.Briefing or SessionPhase.Endgame or SessionPhase.Debrief))
        {
            return null;
        }
        return view.Screen switch
        {
            "briefing" => game.Data<BriefingScreen>(p).You.Role switch
            {
                ThePassenger.Host => "briefing-host",
                OneOfUsIsnt.Mimic => "briefing-mimic",
                _ => "briefing",
            },
            "turn" when !game.Data<TurnScreen>(p).Monsters.IsEmpty => "turn-monsters",
            "turn" when !game.Data<TurnScreen>(p).Attacks.IsEmpty || !game.Data<TurnScreen>(p).Actions.IsEmpty => "turn-endgame",
            "vote" => game.Data<VoteScreen>(p).Cast ? "vote-cast" : "vote",
            "sheet" when game.Data<SheetScreen>(p).Briefing is not null => "sheet-endgame",
            "allocate" or "echo" => view.Screen,
            "debrief" => game.Data<DebriefScreen>(p).Won ? "debrief-won" : "debrief-lost",
            _ => null,
        };
    }

    private static string? Classify(Harness game, int p)
    {
        if (game.H.PlayerView(p) is not CustomView view)
        {
            return null;
        }
        return view.Screen switch
        {
            "turn" => game.Data<TurnScreen>(p).Moves == 0 ? "turn-no-moves" : "turn",
            "roll" => game.Data<RollScreen>(p).Reason == "breach" ? "roll-breach" : "roll-check",
            "watch" => game.Data<WatchScreen>(p).Now.Kind switch
            {
                "reveal" => "watch-reveal",
                "card" => "watch-card",
                "roll" => "watch-roll",
                _ => null,
            },
            "orient" or "card" or "sheet" or "over" => view.Screen,
            _ => null,
        };
    }

    private static JsonNode Node(ControllerView view)
        => JsonSerializer.SerializeToNode<ControllerView>(view with { ViewId = "v1" }, ArcJson.Options)!;

    private static void Check(SortedDictionary<string, JsonNode> screens, string folder)
    {
        var dir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(BasePack.Root)!)!, "..", "web", "play", "test", "fixtures", folder);
        var update = Environment.GetEnvironmentVariable("UPDATE_FIXTURES") == "1";
        foreach (var (name, json) in screens)
        {
            var path = Path.Combine(dir, name + ".json");
            if (update)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
                continue;
            }
            Assert.True(File.Exists(path), $"Missing fixture {path}; run with UPDATE_FIXTURES=1.");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(path)), json),
                $"{name}.json differs from what the game sends; run with UPDATE_FIXTURES=1 and check the diff.");
        }
    }
}
