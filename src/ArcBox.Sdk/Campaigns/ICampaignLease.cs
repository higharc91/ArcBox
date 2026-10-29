using System.Text.Json;

namespace ArcBox.Sdk.Campaigns;

/// <summary>A room's hold on a campaign while its game runs. Every member is synchronous and does no I/O.</summary>
public interface ICampaignLease
{
    CampaignInfo Info { get; }
    JsonElement Document { get; }
    /// <summary>Replaces the document (Version + 1) and queues a background write.</summary>
    void Save(JsonElement document);
    /// <summary>Like Save, and also records a named snapshot (e.g. "End of session 7") for rollback.</summary>
    void Checkpoint(JsonElement document, string label);
}
