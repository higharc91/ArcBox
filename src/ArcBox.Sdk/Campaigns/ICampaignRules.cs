using System.Text.Json;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Sdk.Campaigns;

/// <summary>The game's rules for its documents, used by the lobby picker and the portal.</summary>
public interface ICampaignRules
{
    string GameId { get; }
    JsonElement NewDocument(CampaignInfo info);
    /// <summary>How a member's seat looks in the picker and on the TV roster ("Aldric", "Dwarf Cleric · Lv 3").</summary>
    SeatCard CardFor(CampaignInfo info, JsonElement document, ProfileId member);
    /// <summary>The member's view of the campaign for the portal (the game decides what each member may see).</summary>
    JsonElement PortalView(CampaignInfo info, JsonElement document, ProfileId member);
    /// <summary>Applies a portal command: a new document, or an error message for the member.</summary>
    CommandResult Handle(CampaignInfo info, JsonElement document, ProfileId member, string type, JsonElement data);
}
public sealed record CommandResult(JsonElement? Document, string? Error);
