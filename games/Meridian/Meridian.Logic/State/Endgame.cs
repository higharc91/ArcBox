using System.Text.Json.Serialization;

namespace Meridian.Logic;

// The endgame's state (spec §4.3, §4.4; M3 plan, Task 1). Everything here carries an Audience, so Visibility can filter it.

[JsonConverter(typeof(JsonStringEnumConverter<AudienceKind>))]
public enum AudienceKind
{
    [JsonStringEnumMemberName("everyone")] Everyone,
    [JsonStringEnumMemberName("side")] Side,
    [JsonStringEnumMemberName("role")] Role,
    [JsonStringEnumMemberName("seat")] Seat,
}

/// <summary>Who may see something (spec §4.4). The TV is a viewer with no seat, so it sees only <see cref="Everyone"/>.</summary>
public sealed record Audience(AudienceKind Kind, string? Value = null)
{
    public static Audience Everyone { get; } = new(AudienceKind.Everyone);
    public static Audience OfSeat(SeatId seat) => new(AudienceKind.Seat, seat.Value);
    public static Audience OfSide(string side) => new(AudienceKind.Side, side);
    public static Audience OfRole(string role) => new(AudienceKind.Role, role);

    /// <param name="viewer">A seat, or null for the TV.</param>
    public bool Includes(GameState state, SeatId? viewer)
    {
        if (Kind == AudienceKind.Everyone)
        {
            return true;
        }
        if (viewer is not { } seat)
        {
            return false;
        }
        var role = state.RoleOf(seat);
        return Kind switch
        {
            AudienceKind.Seat => seat.Value == Value,
            AudienceKind.Role => role?.Role == Value,
            AudienceKind.Side => role?.Side == Value,
            _ => false,
        };
    }
}

/// <summary>A seat's part in the scenario. A secret role's <see cref="Audience"/> is that seat (or its side).</summary>
public sealed record RoleCard(SeatId Seat, string Role, string Side, Audience Audience);

public sealed record MonsterStats(int Brawn, int Agility, int Nerve);

/// <summary>
/// A thing in the scenario: a monster, an objective marker, a hazard, a dropped item, a carried object. A token is in a
/// compartment (<see cref="At"/>) or carried by a seat (<see cref="Carrier"/>). <see cref="Card"/> keeps a dropped item's
/// card, so every card still exists exactly once.
/// </summary>
public sealed record Token(
    string Id,
    string Kind,
    CompartmentId? At,
    Audience Audience,
    SeatId? Carrier = null,
    SeatId? Controller = null,
    MonsterStats? Stats = null,
    bool Stunned = false,
    string? State = null,
    CardId? Card = null);

public sealed record Counter(string Id, int Value, Audience Audience);

[JsonConverter(typeof(JsonStringEnumConverter<ObjectiveState>))]
public enum ObjectiveState
{
    [JsonStringEnumMemberName("open")] Open,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("failed")] Failed,
}

public sealed record Objective(string Id, string Text, ObjectiveState State, Audience Audience);

/// <summary>How the game ended: who won, and the line the Debrief opens with.</summary>
public sealed record Outcome(EqArray<SeatId> Winners, string Message);

/// <summary>The running scenario's own state (spec §4.2: scenarios keep their data here, never in fields).</summary>
/// <param name="StartRound">The round the endgame began in; <see cref="RoundLimit"/> counts from it.</param>
public sealed record ScenarioBoard(
    string Scenario,
    EqArray<RoleCard> Roles,
    EqArray<Token> Tokens,
    EqArray<Counter> Counters,
    EqArray<Objective> Objectives,
    int RoundLimit,
    int StartRound,
    Outcome? Outcome = null)
{
    public Counter? CounterOf(string id) => Counters.FirstOrDefault(c => c.Id == id);
    public int Value(string id) => CounterOf(id)?.Value ?? 0;

    public ScenarioBoard WithCounter(string id, int value)
    {
        var at = Counters.Items.FindIndex(c => c.Id == id);
        return at < 0 ? this : this with { Counters = Counters.SetItem(at, Counters[at] with { Value = value }) };
    }

    public ScenarioBoard WithToken(Token token)
    {
        var at = Tokens.Items.FindIndex(t => t.Id == token.Id);
        return this with { Tokens = at < 0 ? Tokens.Add(token) : Tokens.SetItem(at, token) };
    }

    public ScenarioBoard WithoutToken(string id) => this with { Tokens = Tokens.Where(t => t.Id != id).ToEqArray() };

    public ScenarioBoard WithObjective(string id, ObjectiveState state)
    {
        var at = Objectives.Items.FindIndex(o => o.Id == id);
        return at < 0 ? this : this with { Objectives = Objectives.SetItem(at, Objectives[at] with { State = state }) };
    }
}

/// <summary>What a crew member did, for the Debrief's awards.</summary>
public sealed record CrewStats(int Revealed = 0, int Cards = 0, int Dice = 0, int Damage = 0);

internal static class ImmutableArrayFind
{
    public static int FindIndex<T>(this System.Collections.Immutable.ImmutableArray<T> items, Func<T, bool> match)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (match(items[i]))
            {
                return i;
            }
        }
        return -1;
    }
}
