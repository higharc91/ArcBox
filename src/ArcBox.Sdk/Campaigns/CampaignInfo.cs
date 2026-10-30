namespace ArcBox.Sdk.Campaigns;

/// <summary>
/// A campaign as the platform keeps it: who owns it and who is in it. The game's own state is the separate document
/// (<see cref="ICampaignLease.Document"/>), which the platform never reads.
/// </summary>
/// <param name="Id">Stable and opaque; what <c>SelectCampaign</c> takes.</param>
/// <param name="Code">Four letters from the room-code alphabet, unique among campaigns; what the portal URL uses.</param>
/// <param name="GameId">The game whose campaign this is (its manifest sets <see cref="GameManifest.UsesCampaigns"/>).</param>
/// <param name="Name">Trimmed, 1–32 characters.</param>
/// <param name="Owner">The member who created it; the only one who can manage members and snapshots.</param>
/// <param name="Members">Everyone in it, the owner included, in the order they joined (at most 8).</param>
/// <param name="Version">Goes up by one with every change to the document, so a stale portal command can be refused.</param>
public sealed record CampaignInfo(
    string Id, string Code, string GameId, string Name,
    ProfileId Owner, IReadOnlyList<CampaignMember> Members, long Version);

/// <summary>A person in a campaign, under the name they go by there.</summary>
/// <param name="DisplayName">1–12 characters, unique within the campaign; the seat takes this name in the room.</param>
public sealed record CampaignMember(ProfileId Profile, string DisplayName, CampaignRole Role, DateTimeOffset JoinedUtc);

/// <summary>What a member may do in the portal: the owner manages members and snapshots; every player sees their own view.</summary>
public enum CampaignRole { Owner, Player }

/// <summary>A campaign as listed for picking: in the lobby's campaign step (<c>LobbyState.Campaigns</c>) and on <c>/api/me</c>.</summary>
/// <param name="Members">How many members it has.</param>
/// <param name="UpdatedUtc">When it last changed, for ordering the list.</param>
public sealed record CampaignSummary(string Id, string Code, string Name, int Members, DateTimeOffset UpdatedUtc);
