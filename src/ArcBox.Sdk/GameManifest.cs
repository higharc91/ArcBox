namespace ArcBox.Sdk;

/// <param name="UsesCampaigns">The game implements <see cref="Campaigns.ICampaignGame"/> and the lobby gets a campaign step.</param>
/// <param name="ResumesAfterRestart">The game implements <see cref="IResumableGame"/> and is resumed, not restarted, when its room is rebuilt.</param>
public sealed record GameManifest(string Id, string Name, string Description, int MinPlayers, int MaxPlayers,
                                  bool UsesCampaigns = false, bool ResumesAfterRestart = false);
