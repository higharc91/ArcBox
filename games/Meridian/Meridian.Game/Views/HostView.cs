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
    EqArray<string> Log);
