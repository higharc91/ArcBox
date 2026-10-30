using System.Text.Json.Serialization;
using ArcBox.Sdk.Campaigns;

namespace ArcBox.Sdk.Protocol;

public sealed record CreateRoomResult(string Code, string HostToken);
/// <summary>A seat this phone now holds: what it stores to <c>Rejoin</c> later.</summary>
/// <param name="Secret">The seat's secret, shown only here; taking an offline seat issues a new one.</param>
/// <param name="Profile">A new profile key for a profile-backed seat, or null (no profile, or the phone already holds one). Omitted from the JSON when null.</param>
public sealed record JoinResult(string Code, PlayerId PlayerId, string Secret,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProfileKey? Profile = null);
/// <summary>A seat in the lobby.</summary>
/// <param name="Connected">A phone is in the seat now; an offline seat can be taken or rejoined.</param>
/// <param name="Profile">The profile behind the seat in a campaign room, otherwise null. Omitted from the JSON when null.</param>
public sealed record LobbyPlayer(PlayerId Id, string Name, bool IsVip, bool Connected,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProfileId? Profile = null);
/// <summary>The room as the TV and every seated phone see it (<c>LobbyChanged</c>), in the lobby and during a game.</summary>
/// <param name="Campaign">The selected campaign and its roster, when the selected game uses campaigns and one is selected.</param>
/// <param name="Campaigns">The selected game's campaigns to pick from, when it uses campaigns.</param>
/// <remarks>Campaign and Campaigns are omitted from the JSON when null, so lobbies of games without campaigns are unchanged.</remarks>
public sealed record LobbyState(
    string Code,
    IReadOnlyList<LobbyPlayer> Players,
    string? SelectedGameId,
    IReadOnlyList<GameManifest> Games,
    bool InGame,
    bool CanStart,
    GameResults? LastResults,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LobbyCampaign? Campaign = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CampaignSummary>? Campaigns = null);

/// <summary>A phone already in the room answers "Who are you in {campaign}?": a member, or null for "I'm new here".</summary>
/// <param name="Member">The member (or that member's offline seat) this phone is; null for "I'm new here".</param>
/// <param name="ProfileKeys">The phone's stored profile keys, so a key it already holds isn't issued again.</param>
/// <param name="NewName">With a null <paramref name="Member"/>: the new member's name (1–12 characters, like a seat name), which
/// the seat takes too. Null keeps the seat's own name. Lets a phone get past a name the campaign already has.</param>
public sealed record ClaimMemberRequest(SeatRef? Member, IReadOnlyList<string>? ProfileKeys = null, string? NewName = null);
/// <summary>A content pack the TV must have cached, identified by its hash.</summary>
public sealed record PackRef(string PackId, string Hash);
