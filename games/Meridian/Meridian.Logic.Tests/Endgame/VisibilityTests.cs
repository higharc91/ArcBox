using System.Reflection;
using System.Text.Json;

namespace Meridian.Logic.Tests.Endgame;

public sealed class VisibilityTests
{
    // The test board: s2 is the secret mimic with a hidden "sabotage" counter; "repairs" is public; the crew objective is
    // for the crew side.
    private static GameState Board() => EndgameStateTests.WithBoard(Play.Game());

    private static string Json(GameState state) => Logic.Tests.Json.Write(state);

    [Fact]
    public void The_tv_sees_nothing_secret()
    {
        var tv = Visibility.For(Board(), null);
        Assert.DoesNotContain("mimic", Json(tv));
        Assert.DoesNotContain("sabotage", Json(tv));
        Assert.DoesNotContain("Repair four points", Json(tv));
        Assert.Equal(Visibility.UnknownRole, tv.RoleOf(Play.S2)!.Role);
        Assert.Equal("crew", tv.RoleOf(Play.S1)!.Role);   // a public role stays public
        Assert.NotNull(tv.Board!.CounterOf("repairs"));
    }

    [Fact]
    public void A_seat_sees_its_own_secrets_and_no_one_elses()
    {
        var mimic = Visibility.For(Board(), Play.S2);
        Assert.Equal("mimic", mimic.RoleOf(Play.S2)!.Role);
        Assert.NotNull(mimic.Board!.CounterOf("sabotage"));
        Assert.Empty(mimic.Board.Objectives);   // the crew's objective isn't the mimic's

        var crew = Visibility.For(Board(), Play.S1);
        Assert.DoesNotContain("mimic", Json(crew));
        Assert.Single(crew.Board!.Objectives);
    }

    [Fact]
    public void Only_your_own_ballot_is_visible_before_a_vote_resolves()
    {
        var state = Board() with
        {
            Waiting = new VotePending(Play.S3, "accuse", [Play.S3], ["s1", "s2"], [new Ballot(Play.S1, "s2"), new Ballot(Play.S2, "s1")]),
        };
        Assert.Equal([new Ballot(Play.S1, "s2")], ((VotePending)Visibility.For(state, Play.S1).Waiting!).Cast);
        Assert.Empty(((VotePending)Visibility.For(state, null).Waiting!).Cast);
    }

    [Fact]
    public void The_future_is_hidden_from_everyone()
    {
        var state = Play.Game().State;
        var seen = Visibility.For(state, Play.S1);
        Assert.NotEqual(state.Rng, seen.Rng);
        Assert.Equal(state.TilePile.Order(), seen.TilePile);
        Assert.Equal(state.TilePile.Order(), seen.TilePile.Items.Order());   // same tiles, no order
    }

    [Fact]
    public void Bots_are_handed_only_their_seats_view()
    {
        var spy = new SpyBot();
        var pack = BasePack.Pack;
        var start = Rules.NewGame(pack, BotRunner.Seats(pack, 3), 1);
        _ = BotRunner.Play(pack, start, spy, 1, 20).ToList();
        Assert.All(spy.Seen, s => Assert.Equal(Rng.Seeded(0), s.Rng));
    }

    /// <summary>
    /// Structural (M3 review focus 1): every record type reachable from GameState is classified, as public or as filtered
    /// by Visibility. A new type fails here until someone decides which it is, so a secret can't slip in unfiltered.
    /// </summary>
    [Fact]
    public void Every_type_in_the_state_is_classified()
    {
        Type[] filtered = [typeof(ScenarioBoard), typeof(RoleCard), typeof(Token), typeof(Counter), typeof(Objective),
                           typeof(VotePending), typeof(Ballot), typeof(CardPile), typeof(Rng)];
        Type[] public_ = [typeof(GameState), typeof(ShipState), typeof(Compartment), typeof(HatchRef), typeof(Link),
                          typeof(CrewState), typeof(TraitSteps), typeof(CrewStats), typeof(TurnFlags), typeof(BreachInfo),
                          typeof(Pending), typeof(OrientPending), typeof(RollPending), typeof(ChoicePending), typeof(TradePending),
                          typeof(TradeOffer), typeof(AllocatePending), typeof(Audience), typeof(MonsterStats), typeof(Outcome),
                          typeof(Cell)];
        var reached = new HashSet<Type>();
        void Walk(Type t)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(EqArray<>))
            {
                Walk(t.GetGenericArguments()[0]);
                return;
            }
            if (Nullable.GetUnderlyingType(t) is { } inner)
            {
                Walk(inner);
                return;
            }
            if (t.Namespace != "Meridian.Logic" || t.IsEnum || t.GetInterfaces().Any(i => i.Name.StartsWith("IStringId")) || !reached.Add(t))
            {
                return;
            }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
            {
                Walk(p.PropertyType);
            }
            foreach (var derived in typeof(GameState).Assembly.GetTypes().Where(d => d.BaseType == t))
            {
                Walk(derived);
            }
        }
        Walk(typeof(GameState));
        var unclassified = reached.Except(filtered).Except(public_).Select(t => t.Name).Order().ToList();
        Assert.Empty(unclassified);
    }

    private sealed class SpyBot : IBotPolicy
    {
        public List<GameState> Seen { get; } = [];

        public Command Choose(RulesPack pack, GameState state, SeatId seat, EqArray<Command> legal, ref Rng rng)
        {
            Seen.Add(state);
            return ExploreBot.Instance.Choose(pack, state, seat, legal, ref rng);
        }
    }
}
