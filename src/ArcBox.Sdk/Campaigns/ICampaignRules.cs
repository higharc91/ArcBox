using System.Text.Json;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Sdk.Campaigns;

/// <summary>The game's rules for its documents, used by the lobby picker and the portal.</summary>
public interface ICampaignRules
{
    /// <summary>The game these rules belong to, as its manifest names it.</summary>
    string GameId { get; }
    /// <summary>The document a new campaign starts with.</summary>
    JsonElement NewDocument(CampaignInfo info);
    /// <summary>How a member's seat looks in the picker and on the TV roster ("Aldric", "Dwarf Cleric · Lv 3").</summary>
    SeatCard CardFor(CampaignInfo info, JsonElement document, ProfileId member);
    /// <summary>The member's view of the campaign for the portal (the game decides what each member may see).</summary>
    JsonElement PortalView(CampaignInfo info, JsonElement document, ProfileId member);
    /// <summary>Applies a portal command: a new document, or an error message for the member.</summary>
    CommandResult Handle(CampaignInfo info, JsonElement document, ProfileId member, string type, JsonElement data);
}

/// <summary>
/// What <see cref="ICampaignRules.Handle"/> decided: exactly one of a new <paramref name="Document"/> (the command is
/// applied and the version goes up) or an <paramref name="Error"/> shown to the member (nothing changes).
/// </summary>
public sealed record CommandResult(JsonElement? Document, string? Error);
