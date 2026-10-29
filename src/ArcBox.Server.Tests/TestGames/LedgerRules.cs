using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Server.Tests.TestGames;

/// <summary>
/// Campaign rules for the <c>ledger</c> test game. The document is <c>{"notes":[...]}</c>; the portal command
/// <c>note</c> with <c>{"text": s}</c> appends to it, and an empty text is refused with "Write something.".
/// </summary>
public sealed class LedgerRules : ICampaignRules
{
    public string GameId => "ledger";

    public JsonElement NewDocument(CampaignInfo info) => JsonSerializer.SerializeToElement(new { notes = Array.Empty<string>() });

    public SeatCard CardFor(CampaignInfo info, JsonElement document, ProfileId member)
    {
        var name = info.Members.FirstOrDefault(m => m.Profile == member)?.DisplayName ?? "Nobody";
        return new SeatCard("Hero of " + name, "Lv 1");
    }

    public JsonElement PortalView(CampaignInfo info, JsonElement document, ProfileId member) => document;

    public CommandResult Handle(CampaignInfo info, JsonElement document, ProfileId member, string type, JsonElement data)
    {
        if (type != "note") return new CommandResult(null, $"Unknown command {type}.");

        var text = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(text)) return new CommandResult(null, "Write something.");

        var root = JsonNode.Parse(document.GetRawText())!.AsObject();
        var notes = root["notes"] as JsonArray ?? [];
        notes.Add(text);
        root["notes"] = notes;
        return new CommandResult(JsonSerializer.SerializeToElement(root), null);
    }
}
