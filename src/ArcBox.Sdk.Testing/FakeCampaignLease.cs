using System.Text.Json;
using ArcBox.Sdk.Campaigns;

namespace ArcBox.Sdk.Testing;

/// <summary>
/// An in-memory <see cref="ICampaignLease"/> for game tests. Save and Checkpoint replace the document and bump the version,
/// as the server's lease does, and every call is recorded.
/// </summary>
public sealed class FakeCampaignLease(CampaignInfo info, JsonElement document) : ICampaignLease
{
    private readonly List<JsonElement> _saves = [];
    private readonly List<(JsonElement Doc, string Label)> _checkpoints = [];

    public CampaignInfo Info { get; private set; } = info;
    public JsonElement Document { get; private set; } = document.Clone();

    /// <summary>Every document passed to Save or Checkpoint, in order.</summary>
    public IReadOnlyList<JsonElement> Saves => _saves;

    /// <summary>Every Checkpoint call, in order.</summary>
    public IReadOnlyList<(JsonElement Doc, string Label)> Checkpoints => _checkpoints;

    public void Save(JsonElement document)
    {
        Document = document.Clone();
        Info = Info with { Version = Info.Version + 1 };
        _saves.Add(Document);
    }

    public void Checkpoint(JsonElement document, string label)
    {
        Save(document);
        _checkpoints.Add((Document, label));
    }
}
