using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// The tactics campaign's rules outside a session (adapter spec §6): new documents, seat cards and a read-only portal view
/// of the party. Sub-project 4 adds the portal's commands (character creation and level-up).
/// </summary>
public sealed class TacticsCampaignRules(RulesPack rules) : ICampaignRules
{
    /// <inheritdoc />
    public string GameId => TacticsGame.Id;

    /// <inheritdoc />
    public JsonElement NewDocument(CampaignInfo info) => TacticsDocument.New.ToJson();

    /// <inheritdoc />
    public SeatCard CardFor(CampaignInfo info, JsonElement document, ProfileId member)
    {
        var name = info.Members.FirstOrDefault(m => m.Profile == member)?.DisplayName ?? "Adventurer";
        var doc = TacticsDocument.Read(document);
        return doc.For(member) is { } record
            ? new SeatCard(name, $"{Describe(record.Character)} · Lv {doc.PartyLevel}")
            : new SeatCard(name, "No character yet");
    }

    /// <inheritdoc />
    public JsonElement PortalView(CampaignInfo info, JsonElement document, ProfileId member)
    {
        var doc = TacticsDocument.Read(document);
        var party = info.Members.Select(m => doc.For(m.Profile) is { } r
            ? new { name = m.DisplayName, character = Describe(r.Character), xp = r.Xp, you = m.Profile == member }
            : new { name = m.DisplayName, character = "No character yet", xp = 0, you = m.Profile == member });
        return JsonSerializer.SerializeToElement(new { level = doc.PartyLevel, completed = doc.Completed, party }, ArcJson.Options);
    }

    /// <inheritdoc />
    public CommandResult Handle(CampaignInfo info, JsonElement document, ProfileId member, string type, JsonElement data)
        => new(null, "The portal can't change a tactics campaign yet.");

    /// <summary>"Human Fighter", from the pack's race and class names.</summary>
    public string Describe(CharacterRecord character)
        => $"{rules.Races.GetValueOrDefault(character.Race)?.Name ?? character.Race} {rules.Classes.GetValueOrDefault(character.Class)?.Name ?? character.Class}";
}
