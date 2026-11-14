using System.Text.Json.Serialization;

namespace Meridian.Logic;

[JsonConverter(typeof(JsonStringEnumConverter<Phase>))]
public enum Phase
{
    /// <summary>Exploring; nobody can die (spec §2.1).</summary>
    [JsonStringEnumMemberName("explore")] Explore,

    /// <summary>The Breach fired and the scenario is set up; phones read their briefings (spec §3.1).</summary>
    [JsonStringEnumMemberName("breach")] Breach,

    /// <summary>The scenario is being played; death is possible.</summary>
    [JsonStringEnumMemberName("endgame")] Endgame,

    /// <summary>A side has won (see <see cref="ScenarioBoard.Outcome"/>).</summary>
    [JsonStringEnumMemberName("over")] Over,
}

/// <summary>A crew member's four 1-based marker positions. A record of ints, so it compares by value for free.</summary>
public sealed record TraitSteps(int Brawn, int Agility, int Tech, int Nerve)
{
    public int this[Trait trait] => trait switch
    {
        Trait.Brawn => Brawn,
        Trait.Agility => Agility,
        Trait.Tech => Tech,
        Trait.Nerve => Nerve,
        _ => throw new ArgumentOutOfRangeException(nameof(trait)),
    };

    public TraitSteps With(Trait trait, int step) => trait switch
    {
        Trait.Brawn => this with { Brawn = step },
        Trait.Agility => this with { Agility = step },
        Trait.Tech => this with { Tech = step },
        Trait.Nerve => this with { Nerve = step },
        _ => throw new ArgumentOutOfRangeException(nameof(trait)),
    };

    public static TraitSteps Starting(TrackSet tracks)
        => new(tracks.Brawn.Start, tracks.Agility.Start, tracks.Tech.Start, tracks.Nerve.Start);
}

/// <param name="UsedRooms">Compartments whose once-per-crew rule this crew member has used.</param>
/// <param name="Alive">False once dead; a dead crew member stays in the game as an echo (spec §2.8).</param>
/// <param name="Escaped">Left the ship (Decaying Orbit's pods); out of the turn order but not dead.</param>
public sealed record CrewState(SeatId Seat, CrewId Crew, CompartmentId At, TraitSteps Steps,
                               EqArray<CardId> Items = default, EqArray<CompartmentId> UsedRooms = default,
                               bool Alive = true, bool Escaped = false, CrewStats? Stats = null)
{
    public CrewStats Counted => Stats ?? new CrewStats();

    /// <summary>On the ship and alive: can be hurt, can act, counts for "everyone dead".</summary>
    public bool Aboard => Alive && !Escaped;
}

public sealed record CardPile(EqArray<CardId> Draw, EqArray<CardId> Discard = default);

/// <summary>What the active seat has already done this turn (spec §2.4: each at most once).</summary>
public sealed record TurnFlags(bool UsedItem = false, bool UsedCompartment = false, bool Traded = false);

/// <summary>
/// Who fired the Breach, with what and where. <see cref="Anomaly"/> and <see cref="Compartment"/> are null when the ship
/// ran out of places to explore (spec §2.6), in which case the scenario is chosen from every runnable one.
/// </summary>
public sealed record BreachInfo(SeatId Seat, CardId? Anomaly, CompartmentId? Compartment, bool Forced, int? Roll);

/// <summary>
/// The whole game, immutable. It holds its own <see cref="Rng"/>, so serialising it is a complete snapshot: a restored
/// state continues with the same draws (spec §5.6).
/// </summary>
/// <param name="Crew">In turn order.</param>
/// <param name="First">The index in <see cref="Crew"/> of the seat that took the first turn; a new round starts on it.</param>
/// <param name="Waiting">What the game is waiting for, and from whom. Null: the active seat's free choice.</param>
public sealed record GameState(
    Rng Rng,
    Phase Phase,
    ShipState Ship,
    EqArray<CrewState> Crew,
    int First,
    int Active,
    int Round,
    int MovesLeft,
    TurnFlags Turn,
    EqArray<TileId> TilePile,
    CardPile Incidents,
    CardPile Salvage,
    CardPile Anomalies,
    int AnomaliesDrawn,
    Pending? Waiting,
    BreachInfo? Breach,
    ScenarioBoard? Board = null)
{
    /// <summary>A seat's role in the running scenario, or null before one is dealt (or for a seat with none).</summary>
    public RoleCard? RoleOf(SeatId seat) => Board?.Roles.FirstOrDefault(r => r.Seat == seat);

    public GameState WithBoard(ScenarioBoard board) => this with { Board = board };

    public GameState CountFor(SeatId seat, Func<CrewStats, CrewStats> change)
        => WithCrew(CrewOf(seat) with { Stats = change(CrewOf(seat).Counted) });

    public CrewState ActiveCrew => Crew[Active];
    public SeatId ActiveSeat => Crew[Active].Seat;

    public int IndexOf(SeatId seat)
    {
        for (var i = 0; i < Crew.Count; i++)
        {
            if (Crew[i].Seat == seat)
            {
                return i;
            }
        }
        return -1;
    }

    public CrewState CrewOf(SeatId seat) => Crew[IndexOf(seat)];

    public GameState WithCrew(CrewState crew) => this with { Crew = Crew.SetItem(IndexOf(crew.Seat), crew) };

    public CardPile Pile(CardKind kind) => kind switch
    {
        CardKind.Incident => Incidents,
        CardKind.Salvage => Salvage,
        CardKind.Anomaly => Anomalies,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public GameState WithPile(CardKind kind, CardPile pile) => kind switch
    {
        CardKind.Incident => this with { Incidents = pile },
        CardKind.Salvage => this with { Salvage = pile },
        CardKind.Anomaly => this with { Anomalies = pile },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
