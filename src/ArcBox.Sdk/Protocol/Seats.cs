namespace ArcBox.Sdk.Protocol;

/// <summary>Asks a room what this phone may take (hub method <c>Peek</c>). Creates and changes nothing.</summary>
/// <param name="ProfileKeys">The profile keys the phone holds, most recently used first, so the room can suggest one.</param>
public sealed record PeekRequest(string Code, IReadOnlyList<string>? ProfileKeys = null);

/// <summary>The answer to <see cref="PeekRequest"/>: what the phone's picker shows.</summary>
public sealed record RoomPeek(
    string Code,
    string? GameId,
    string? CampaignName,                      // set in campaign rooms
    bool InGame,
    IReadOnlyList<SeatOffer> Offers,           // what this phone may take, in display order
    SeatOffer? Suggested,                      // the offer matching one of this phone's profile keys, if any
    bool CanJoinNew);                          // the "I'm new" / name box is allowed

/// <summary>A seat the phone may take, as the picker shows it.</summary>
/// <param name="Ref">What to send back in <see cref="TakeSeatRequest.Seat"/>.</param>
/// <param name="Absent">The seat was empty when the running game began (shown as played by the AI).</param>
public sealed record SeatOffer(SeatRef Ref, SeatCard Card, bool Absent);
/// <summary>Exactly one is set: an existing (offline) seat, or a campaign member who has no seat yet.</summary>
public sealed record SeatRef(PlayerId? Slot = null, ProfileId? Member = null);
/// <summary>How a seat is shown. The game supplies it in campaign rooms ("Aldric", "Dwarf Cleric · Lv 3").</summary>
public sealed record SeatCard(string Title, string? Subtitle = null, string? Color = null, string? Portrait = null);

/// <summary>
/// Takes a seat (hub method <c>TakeSeat</c>): exactly one of <paramref name="Seat"/> (an offered seat) or
/// <paramref name="NewName"/> (a new seat, or "I'm new here" in a campaign lobby).
/// </summary>
/// <param name="ProfileKeys">The phone's stored profile keys, so a key it already holds for the seat's profile isn't issued again.</param>
public sealed record TakeSeatRequest(string Code, SeatRef? Seat = null, string? NewName = null,
                                     IReadOnlyList<string>? ProfileKeys = null);
/// <summary>
/// A credential for a profile, issued to one phone (in <see cref="JoinResult.Profile"/>, or by <c>CreateCampaign</c> and
/// <c>ClaimMember</c>). The phone keeps it to be recognised next time and to sign in to the portal; the server keeps
/// only its hash.
/// </summary>
/// <param name="Name">The profile's display name, for the phone's "Continue as" button.</param>
public sealed record ProfileKey(ProfileId Id, string Key, string Name);   // only on a profile-backed seat

/// <summary>The campaign a lobby has selected, with who is here and who is away.</summary>
public sealed record LobbyCampaign(string Id, string Code, string Name, IReadOnlyList<RosterEntry> Roster);
/// <summary>A campaign member on the lobby roster.</summary>
/// <param name="Card">The game's card for the member (<see cref="Campaigns.ICampaignRules.CardFor"/>).</param>
/// <param name="SeatedAs">The member's seat in this room, if they have one.</param>
/// <param name="Connected">A phone is in that seat now.</param>
public sealed record RosterEntry(ProfileId Profile, SeatCard Card, PlayerId? SeatedAs, bool Connected);
