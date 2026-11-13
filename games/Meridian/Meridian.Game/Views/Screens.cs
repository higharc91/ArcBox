using Meridian.Logic;

namespace Meridian.Game;

// Phone screens (spec §3.4). Each record is one screen's CustomView.Data.

/// <summary>A crew member as the crew-pick and sheet screens show it: values from step 1 to 8, and where they start.</summary>
public sealed record CrewCard(CrewId Id, string Name, string Role, EqArray<TrackCard> Tracks, bool Taken, bool Mine);

public sealed record TrackCard(Trait Trait, EqArray<int> Values, int Step);

/// <summary><c>crewPick</c>: choose a crew member. Sends <c>pick { crew }</c>.</summary>
public sealed record CrewPickScreen(EqArray<CrewCard> Crew, int Picked, int Seats);
