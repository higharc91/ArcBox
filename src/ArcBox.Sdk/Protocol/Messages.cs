using System.Text.Json.Serialization;
using ArcBox.Sdk.Campaigns;

namespace ArcBox.Sdk.Protocol;

public sealed record CreateRoomResult(string Code, string HostToken);
public sealed record JoinResult(string Code, PlayerId PlayerId, string Secret,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProfileKey? Profile = null);
public sealed record LobbyPlayer(PlayerId Id, string Name, bool IsVip, bool Connected,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProfileId? Profile = null);
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
