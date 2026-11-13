namespace Meridian.Game;

/// <summary>One seat on the TV's seat strip.</summary>
public sealed record SeatCard(string Id, string Name, string? Crew, int Colour, bool Connected, bool Auto, bool Active);

/// <summary>What the TV shows: the public projection of the game (spec §5.3).</summary>
public sealed record MeridianHostView(string Phase, string Banner, EqArray<SeatCard> Seats);
