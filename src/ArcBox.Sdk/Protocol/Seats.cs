namespace ArcBox.Sdk.Protocol;

public sealed record PeekRequest(string Code, IReadOnlyList<string>? ProfileKeys = null);

public sealed record RoomPeek(
    string Code,
    string? GameId,
    string? CampaignName,                      // set in campaign rooms
    bool InGame,
    IReadOnlyList<SeatOffer> Offers,           // what this phone may take, in display order
    SeatOffer? Suggested,                      // the offer matching one of this phone's profile keys, if any
    bool CanJoinNew);                          // the "I'm new" / name box is allowed

public sealed record SeatOffer(SeatRef Ref, SeatCard Card, bool Absent);
/// <summary>Exactly one is set: an existing (offline) seat, or a campaign member who has no seat yet.</summary>
public sealed record SeatRef(PlayerId? Slot = null, ProfileId? Member = null);
/// <summary>How a seat is shown. The game supplies it in campaign rooms ("Aldric", "Dwarf Cleric · Lv 3").</summary>
public sealed record SeatCard(string Title, string? Subtitle = null, string? Color = null, string? Portrait = null);

public sealed record TakeSeatRequest(string Code, SeatRef? Seat = null, string? NewName = null,
                                     IReadOnlyList<string>? ProfileKeys = null);
public sealed record ProfileKey(ProfileId Id, string Key, string Name);   // only on a profile-backed seat

/// <summary>The campaign a lobby has selected, with who is here and who is away.</summary>
public sealed record LobbyCampaign(string Id, string Code, string Name, IReadOnlyList<RosterEntry> Roster);
public sealed record RosterEntry(ProfileId Profile, SeatCard Card, PlayerId? SeatedAs, bool Connected);
