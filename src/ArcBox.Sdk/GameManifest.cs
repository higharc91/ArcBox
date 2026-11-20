namespace ArcBox.Sdk;

/// <param name="UsesCampaigns">The game implements <see cref="Campaigns.ICampaignGame"/> and the lobby gets a campaign step.</param>
/// <param name="ResumesAfterRestart">The game implements <see cref="IResumableGame"/> and is resumed, not restarted, when its room is rebuilt.</param>
/// <param name="CardArt">
/// A title image for the game's card in the TV lobby (#405): a file's path inside the game's <c>base</c> content pack, such
/// as <c>ui/logo.4f2a…png</c>. The server turns it into the file's URL before the TV sees it, or drops it if the pack has
/// no such file; the TV draws the text card whenever there's no image to show. Null for a text card.
/// </param>
public sealed record GameManifest(string Id, string Name, string Description, int MinPlayers, int MaxPlayers,
                                  bool UsesCampaigns = false, bool ResumesAfterRestart = false, string? CardArt = null);
