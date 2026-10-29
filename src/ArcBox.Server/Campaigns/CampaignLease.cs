using System.Text.Json;
using ArcBox.Sdk.Campaigns;

namespace ArcBox.Server.Campaigns;

/// <summary>
/// A room's hold on a campaign (spec §3.4). It is a thin handle: the state lives in the <see cref="CampaignService"/>,
/// whose lock every call takes. Once the lease has been released (or replaced by another room's), <see cref="Save"/>
/// and <see cref="Checkpoint"/> do nothing, so a game that outlives its lease can't overwrite the next session's work;
/// <see cref="Info"/> and <see cref="Document"/> keep showing the campaign's current state.
/// </summary>
public sealed class CampaignLease : ICampaignLease
{
    private readonly CampaignService _service;
    private readonly string _id;

    internal CampaignLease(CampaignService service, string id, string roomCode)
    {
        _service = service;
        _id = id;
        RoomCode = roomCode;
    }

    /// <summary>The room that holds the campaign.</summary>
    public string RoomCode { get; }

    public CampaignInfo Info => _service.Find(_id) ?? throw new CampaignException("Campaign not found.");

    public JsonElement Document => _service.DocumentOf(_id);

    public void Save(JsonElement document) => _service.Save(this, _id, document, label: null);

    public void Checkpoint(JsonElement document, string label) => _service.Save(this, _id, document, label);
}
