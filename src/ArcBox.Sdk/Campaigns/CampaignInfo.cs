namespace ArcBox.Sdk.Campaigns;

public sealed record CampaignInfo(
    string Id, string Code, string GameId, string Name,
    ProfileId Owner, IReadOnlyList<CampaignMember> Members, long Version);
public sealed record CampaignMember(ProfileId Profile, string DisplayName, CampaignRole Role, DateTimeOffset JoinedUtc);
public enum CampaignRole { Owner, Player }
public sealed record CampaignSummary(string Id, string Code, string Name, int Members, DateTimeOffset UpdatedUtc);
