using System.Text.Json;
using ArcBox.Sdk;
using Meridian.Logic;

namespace Meridian.Game;

/// <summary>
/// Builds the TV's view and each phone's from the session (spec §5.3). In M2 nothing is secret; stage M3 adds audiences,
/// so views are already built from one seat's perspective.
/// </summary>
internal static class Projection
{
    public const string Module = MeridianGame.Id;

    public static MeridianHostView Host(MeridianGame game)
    {
        var s = game.Session;
        var banner = s.Phase switch
        {
            SessionPhase.Boarding => "Choose your crew",
            SessionPhase.Over => s.Ending ?? "",
            SessionPhase.Briefing => "Read your briefing",
            SessionPhase.Debrief => "Debrief",
            _ => $"{s.SeatOf(s.Game!.ActiveSeat).Name}'s turn",
        };
        // The TV is a viewer with no seat: everything it shows comes from what everyone may see.
        var seen = s.Game is { } real ? Visibility.For(real, null) : null;
        var map = seen is { } g ? Map(game, g, names: false) : new MapData([], []);
        return new MeridianHostView(Name(s.Phase), banner, Seats(game), map.Compartments, map.Crew,
                                    seen?.TilePile.Count ?? game.Pack.Tiles.Sum(t => t.Copies),
                                    seen?.AnomaliesDrawn ?? 0, seen?.Round ?? 0,
                                    s.Queue.IsEmpty ? null : s.Queue[0], s.Log,
                                    seen is { Board: not null } ? Scenario(game, seen) : null);
    }

    private static ScenarioPanel Scenario(MeridianGame game, GameState seen)
    {
        var board = seen.Board!;
        var def = game.Pack.ScenarioDef(board.Scenario);
        var left = Math.Max(0, board.RoundLimit - Math.Max(0, seen.Round - board.StartRound));
        var tokens = board.Tokens.Select(t => new TokenCard(t.Id, t.Kind, t.At?.Value, t.Carrier?.Value, t.Stunned, t.State)).ToEqArray();
        var counters = board.Counters.Select(c => new CounterCard(c.Id, c.Value)).ToEqArray();
        var objectives = board.Objectives.Select(o => new ObjectiveCard(o.Id, o.Text, o.State)).ToEqArray();
        // Only once the game is decided does the TV name anyone's role (Visibility reveals everything at Phase.Over).
        if (board.Outcome is { } outcome && seen.Phase == Phase.Over)
        {
            var reveal = board.Roles.Select(r => new RevealCard(r.Seat.Value, def.Role(r.Role).Name, outcome.Winners.Contains(r.Seat))).ToEqArray();
            return new ScenarioPanel(def.Title, left, tokens, counters, objectives, reveal, outcome.Message);
        }
        return new ScenarioPanel(def.Title, left, tokens, counters, objectives);
    }

    public static ControllerView Player(MeridianGame game, PlayerId id)
    {
        var s = game.Session;
        if (s.Find(id) is not { } seat)
        {
            return new WaitView("You're watching this game.");
        }
        switch (s.Phase)
        {
            case SessionPhase.Boarding:
                return Screen("crewPick", CrewPick(game, seat), game.Deadline);
            case SessionPhase.Over:
                return Screen("over", new OverScreen(s.Ending ?? ""), null);
            case SessionPhase.Debrief:
                return Screen("debrief", Debrief(game, seat), game.Deadline);
        }

        // Everything below is built from what this seat may see, never the raw state.
        var me = seat.Seat;
        var real = s.Game!;
        var state = Visibility.For(real, me);
        if (s.Phase == SessionPhase.Briefing)
        {
            return Screen("briefing", Briefing(game, state, me), game.Deadline);
        }
        if (!s.Queue.IsEmpty)
        {
            return Screen("watch", new WatchScreen(s.Queue[0]), null);
        }
        return state.Waiting switch
        {
            OrientPending o when o.From == me => Screen("orient", Orient(game, state, o), game.Deadline),
            RollPending r when r.From == me => Screen("roll", Roll(game, r), game.Deadline),
            ChoicePending c when c.From == me => Screen("card", Card(game, c.Card), game.Deadline),
            TradePending t when t.From == me => Screen("trade", Trade(game, state, t), game.Deadline),
            AllocatePending a when a.From == me => Screen("allocate", Allocate(game, state, a), game.Deadline),
            VotePending v when v.Owed.Contains(me) || v.Cast.Any(b => b.Voter == me) => Screen("vote", Vote(game, state, v, me), game.Deadline),
            null when state.ActiveSeat == me && !state.ActiveCrew.Alive => Screen("echo", Echo(game, real, me), game.Deadline),
            null when state.ActiveSeat == me => Screen("turn", Turn(game, real, state, me), game.Deadline),
            _ => Screen("sheet", Sheet(game, state, me), null),
        };
    }

    private static EqArray<SeatCard> Seats(MeridianGame game)
    {
        var s = game.Session;
        var active = s.Phase is SessionPhase.Explore or SessionPhase.Endgame ? s.Game?.ActiveSeat : null;
        return s.Seats.Select((x, i) => new SeatCard(x.Player.Value, x.Name,
            x.Crew is { } c ? game.Pack.CrewMember(c).Name : null, i, x.Connected, x.Auto, x.Seat == active)).ToEqArray();
    }

    /// <param name="names">Phones need compartment names; the TV looks them up in its content pack.</param>
    public static MapData Map(MeridianGame game, GameState state, bool names = true)
    {
        var ship = state.Ship;
        var compartments = ship.Compartments.Select(c =>
        {
            var tile = game.Pack.Tile(c.Tile);
            var sealedSides = c.Hatches.Where(h => ship.IsSealed(c.Id, h)).ToEqArray();
            return new CompartmentCard(c.Id.Value, c.Tile.Value, names ? tile.Name : null, c.At, c.Rotation, c.Hatches,
                                       sealedSides.IsEmpty ? null : sealedSides, tile.Symbol);
        }).ToEqArray();
        var seats = game.Session.Seats;
        var crew = state.Crew.Select(c => new CrewMarker(c.Seat.Value, game.Session.SeatOf(c.Seat).Name, c.At.Value,
                                                         seats.IndexOf(game.Session.SeatOf(c.Seat)))).ToEqArray();
        return new MapData(compartments, crew);
    }

    private static CrewPickScreen CrewPick(MeridianGame game, SeatInfo me)
    {
        var s = game.Session;
        var crew = game.Pack.Crew.Select(c => Card(c, TraitSteps.Starting(c.Tracks),
            taken: s.Seats.Any(x => x.Crew == c.Id && x.Player != me.Player), mine: me.Crew == c.Id)).ToEqArray();
        return new CrewPickScreen(crew, s.Seats.Count(x => x.Crew is not null), s.Seats.Count);
    }

    /// <param name="real">The true state, for <see cref="Rules.Legal"/>, which only ever reveals this seat's own options.</param>
    private static TurnScreen Turn(MeridianGame game, GameState real, GameState state, SeatId me)
    {
        var pack = game.Pack;
        var legal = Rules.Legal(pack, real, me);
        var crew = state.CrewOf(me);
        var reach = Reach.From(pack, state.Ship, crew.At, state.MovesLeft);
        var usable = legal.OfType<UseItem>().Select(u => u.Item).ToHashSet();
        var neighbours = state.Turn.Traded
            ? EqArray<Neighbour>.Empty
            : state.Crew.Where(c => c.Seat != me && c.At == crew.At)
                .Select(c => new Neighbour(c.Seat.Value, game.Session.SeatOf(c.Seat).Name, Items(pack, c, NoItems))).ToEqArray();
        var actions = state.Board is { } board
            ? pack.Scenarios.Get(board.Scenario).ActionsFor(pack, real, me).Where(a => legal.Contains(new ScenarioAct(me, a.Id, a.Data))).ToEqArray()
            : [];
        var monsters = legal.OfType<MonsterMove>().Select(m => m.Monster).Concat(legal.OfType<MonsterAttack>().Select(m => m.Monster))
            .Distinct().Order(StringComparer.Ordinal)
            .Select(id => new MonsterCard(id, state.Board!.Tokens.First(t => t.Id == id).Kind, state.Board.Tokens.First(t => t.Id == id).At!.Value.Value,
                legal.OfType<MonsterMove>().Where(m => m.Monster == id).Select(m => m.To.Value).ToEqArray(),
                legal.OfType<MonsterAttack>().Where(m => m.Monster == id).Select(m => new AttackTarget(m.Target.Value, game.Session.SeatOf(m.Target).Name)).ToEqArray()))
            .ToEqArray();
        return new TurnScreen(
            Map(game, state), crew.At.Value, state.MovesLeft,
            legal.OfType<MoveTo>().Select(m => new Reachable(m.To.Value, reach[m.To])).ToEqArray(),
            legal.OfType<Open>().Select(o => new Openable(o.At.Value, o.Side)).ToEqArray(),
            Items(pack, crew, usable),
            legal.Contains(new UseCompartment(me)) ? pack.Tile(state.Ship.Get(crew.At).Tile).Name : null,
            neighbours,
            Card(pack.CrewMember(crew.Crew), crew.Steps, taken: false, mine: true),
            legal.OfType<Attack>().Select(a => new AttackTarget(a.Target, TargetName(game, state, a.Target))).ToEqArray(),
            actions, monsters, BriefingOf(game, state, me));
    }

    private static string TargetName(MeridianGame game, GameState state, string target)
        => state.IndexOf(new SeatId(target)) >= 0 ? game.Session.SeatOf(new SeatId(target)).Name
            : state.Board?.Tokens.FirstOrDefault(t => t.Id == target)?.Kind ?? target;

    /// <summary>This seat's role and briefing, from what it can see: its own card, which its audience always includes.</summary>
    private static BriefingCard? BriefingOf(MeridianGame game, GameState state, SeatId me)
    {
        if (state.Board is not { } board || state.RoleOf(me) is not { } role || role.Role == Visibility.UnknownRole)
        {
            return null;
        }
        var def = game.Pack.ScenarioDef(board.Scenario).Role(role.Role);
        return new BriefingCard(role.Role, def.Name, def.Briefing, def.Objectives);
    }

    private static BriefingScreen Briefing(MeridianGame game, GameState state, SeatId me)
        => new(game.Pack.ScenarioDef(state.Board!.Scenario).Title, BriefingOf(game, state, me)!,
               game.Session.Ready.Contains(me), game.Session.Ready.Count, game.Session.Seats.Count);

    private static AllocateScreen Allocate(MeridianGame game, GameState state, AllocatePending a)
    {
        var crew = state.CrewOf(a.From);
        var def = game.Pack.CrewMember(crew.Crew);
        return new AllocateScreen(a.Steps,
            a.Group.Members().Select(t => new TrackCard(t, def.Tracks[t].Values, crew.Steps[t])).ToEqArray(),
            a.By is { } by ? game.Session.SeatOf(by).Name : null);
    }

    private static VoteScreen Vote(MeridianGame game, GameState state, VotePending v, SeatId me)
        => new(v.Purpose == "mimic" ? "Who is the mimic?" : "Vote",
               v.Options.Select(o => new VoteOption(o, state.IndexOf(new SeatId(o)) >= 0 ? game.Session.SeatOf(new SeatId(o)).Name : o)).ToEqArray(),
               !v.Owed.Contains(me), v.Owed.Count);

    private static EchoScreen Echo(MeridianGame game, GameState real, SeatId me)
    {
        var lends = Rules.Legal(game.Pack, real, me).OfType<Lend>().ToList();
        return new EchoScreen(lends.Select(l => new VoteOption(l.To.Value, game.Session.SeatOf(l.To).Name)).ToEqArray(), real.Turn.Lent);
    }

    private static DebriefScreen Debrief(MeridianGame game, SeatInfo me)
    {
        var state = game.Session.Game!;
        var board = state.Board!;
        var outcome = board.Outcome!;
        var roles = board.Roles.Select(r => new RoleReveal(r.Seat.Value, game.Session.SeatOf(r.Seat).Name,
            game.Pack.ScenarioDef(board.Scenario).Role(r.Role).Name, outcome.Winners.Contains(r.Seat))).ToEqArray();
        var awards = game.Awards().Select(a => new AwardCard(a.Title, a.Seat.Value, game.Session.SeatOf(a.Seat).Name)).ToEqArray();
        return new DebriefScreen(outcome.Message, outcome.Winners.Contains(me.Seat), roles, awards, me.Vip);
    }

    private static OrientScreen Orient(MeridianGame game, GameState state, OrientPending o)
    {
        var tile = game.Pack.Tile(o.Tile);
        return new OrientScreen(tile.Name, o.At, o.Toward,
            o.Rotations.Select(r => new RotationOption(r, tile.HatchesAt(r))).ToEqArray(), Map(game, state));
    }

    private static RollScreen Roll(MeridianGame game, RollPending r)
    {
        if (r.Reason == RollReason.Breach)
        {
            return new RollScreen("breach", null, r.Dice, "Breach roll",
                                  "Every Anomaly aboard makes the next one more likely to wake something.", [],
                                  game.Pack.Rules.BreachThreshold);
        }
        var card = game.Pack.Card(r.Card!.Value);
        var check = r.Option is { } o ? card.Choice!.Options.First(x => x.Id == o).Check! : card.Check!;
        return new RollScreen("check", r.Trait, r.Dice, card.Title, card.Text,
                              check.Bands.Select(b => new BandCard(b.Min, b.Max, b.Text)).ToEqArray(), null);
    }

    private static CardScreen Card(MeridianGame game, CardId id)
    {
        var card = game.Pack.Card(id);
        return new CardScreen(id, card.Kind, card.Title, card.Text,
            card.Choice!.Options.Select(o => new OptionCard(o.Id, o.Label, o.Text, o.Check?.Trait)).ToEqArray());
    }

    private static TradeScreen Trade(MeridianGame game, GameState state, TradePending t)
    {
        ItemCard Item(CardId id) => ItemOf(game.Pack, id, usable: false);
        return new TradeScreen(game.Session.SeatOf(t.Offerer).Name, t.Offer.Give.Select(Item).ToEqArray(),
                               t.Offer.Take.Select(Item).ToEqArray());
    }

    private static SheetScreen Sheet(MeridianGame game, GameState state, SeatId me)
    {
        var crew = state.CrewOf(me);
        var waiting = state.Waiting is { } w ? game.Session.SeatOf(w.From).Name : null;
        return new SheetScreen(Card(game.Pack.CrewMember(crew.Crew), crew.Steps, taken: false, mine: true), crew.At.Value,
                               Items(game.Pack, crew, NoItems), Map(game, state), game.Session.SeatOf(state.ActiveSeat).Name, waiting,
                               BriefingOf(game, state, me));
    }

    private static readonly IReadOnlySet<CardId> NoItems = new HashSet<CardId>();

    private static EqArray<ItemCard> Items(RulesPack pack, CrewState crew, IReadOnlySet<CardId> usable)
        => crew.Items.Select(i => ItemOf(pack, i, usable.Contains(i))).ToEqArray();

    private static ItemCard ItemOf(RulesPack pack, CardId id, bool usable)
    {
        var card = pack.Card(id);
        return new ItemCard(id, card.Kind, card.Title, card.Text, card.Item?.Use?.Label, usable);
    }

    public static CrewCard Card(CrewDef def, TraitSteps steps, bool taken, bool mine)
        => new(def.Id, def.Name, def.Role,
               Traits.All.Select(t => new TrackCard(t, def.Tracks[t].Values, steps[t])).ToEqArray(), taken, mine);

    public static CustomView Screen<T>(string screen, T data, DateTimeOffset? deadline)
        => new(Module, screen, JsonSerializer.SerializeToElement(data, ArcJson.Options)) { DeadlineUtc = deadline };

    private static string Name(SessionPhase phase) => JsonSerializer.Serialize(phase, ArcJson.Options).Trim('"');
}
