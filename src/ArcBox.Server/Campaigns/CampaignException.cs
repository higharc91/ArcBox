namespace ArcBox.Server.Campaigns;

/// <summary>A campaign request the platform refuses; the message is written for the player.</summary>
public sealed class CampaignException(string message) : Exception(message);
