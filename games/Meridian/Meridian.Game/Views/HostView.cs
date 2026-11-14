using System.Text.Json.Serialization;

namespace Meridian.Game;

/// <summary>One seat on the TV's seat strip. <see cref="Colour"/> indexes the platform's six player colours.</summary>
public sealed record SeatCard(string Id, string Name, string? Crew, int Colour, bool Connected, bool Auto, bool Active);

/// <summary>
/// A placed compartment, for the TV and the phones' maps. <see cref="Id"/> is its compartment id. Empty fields are left
/// out of the JSON to keep a full ship under the TV view's 8 KB budget: <see cref="Name"/> is null on the TV, which looks
/// it up in the content pack by <see cref="Tile"/>; phones have no pack, so their maps carry it.
/// </summary>
public sealed record CompartmentCard(
    string Id,
    string Tile,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    Cell At,
    int Rotation,
    EqArray<Side> Hatches,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EqArray<Side>? Sealed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CardKind? Symbol);

/// <summary>Where a crew member is. <see cref="Id"/> is the seat.</summary>
public sealed record CrewMarker(string Id, string Name, string At, int Colour);

/// <summary>A scenario token everyone may see. A carried token has <see cref="Carrier"/> (a seat) and no <see cref="At"/>.</summary>
public sealed record TokenCard(
    string Id,
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? At,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Carrier,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Stunned = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? State = null);

public sealed record CounterCard(string Id, int Value);
public sealed record ObjectiveCard(string Id, string Text, ObjectiveState State);

/// <summary>A role revealed at the Debrief. <see cref="Id"/> is the seat (keyed for patches).</summary>
public sealed record RevealCard(string Id, string Role, bool Won);

/// <summary>
/// The running scenario as the TV may show it: built from <c>Visibility.For(state, null)</c>, so it holds only what
/// everyone may see. <see cref="Reveal"/> and <see cref="Outcome"/> appear at the Debrief, when nothing is secret.
/// </summary>
public sealed record ScenarioPanel(
    string Title,
    int RoundsLeft,
    EqArray<TokenCard> Tokens,
    EqArray<CounterCard> Counters,
    EqArray<ObjectiveCard> Objectives,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EqArray<RevealCard>? Reveal = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Outcome = null);

/// <summary>
/// What the TV shows: the public projection of the game (spec §5.3). Every array of objects has a string <c>id</c>, so the
/// platform's host patches update it element by element; a new compartment is one added element (budget: one Explore
/// step under one network packet).
/// </summary>
/// <param name="Now">The event the TV should be presenting, or null.</param>
public sealed record MeridianHostView(
    string Phase,
    string Banner,
    EqArray<SeatCard> Seats,
    EqArray<CompartmentCard> Compartments,
    EqArray<CrewMarker> Crew,
    int TilesLeft,
    int AnomaliesDrawn,
    int Round,
    Presented? Now,
    EqArray<string> Log,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ScenarioPanel? Scenario = null);
