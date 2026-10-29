namespace ArcBox.Server.Campaigns;

/// <summary>A campaign request the platform refuses; the message is written for the player.</summary>
public class CampaignException(string message) : Exception(message);

/// <summary>No such campaign, or the caller isn't in it. Callers map this to "not found" by type, not by message.</summary>
public sealed class CampaignNotFoundException() : CampaignException("Campaign not found.");

/// <summary>The campaign is held by a room, so it can't be changed from outside.</summary>
public sealed class CampaignLeasedException() : CampaignException("A session is in progress. Use the in-game menu.");
