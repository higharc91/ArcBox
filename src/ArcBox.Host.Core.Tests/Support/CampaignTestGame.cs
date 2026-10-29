using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Host.Core.Tests.Support;

/// <summary>The smallest game whose manifest sets <c>UsesCampaigns</c>, so the lobby lists campaigns and the host can select one.</summary>
public sealed class CampaignTestGame : IGame, ICampaignGame
{
    public const string Id = "campaign-test";

    public GameManifest Manifest { get; } = new(Id, "Campaign test", "Test game that uses campaigns", 1, 8, UsesCampaigns: true);

    public void Start(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease campaign) { }

    public void Start(IGameContext ctx, IReadOnlyList<Player> players) { }

    public void OnInput(PlayerId from, PlayerInput input) { }

    public void OnTimer(string key) { }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected) { }

    public object HostView() => new { };

    public ControllerView PlayerView(PlayerId id) => new ButtonsView(null, [new Option("ping", "Ping")]);
}

/// <summary>Campaign rules for <see cref="CampaignTestGame"/>: an empty document, and each member's card is their display name.</summary>
public sealed class CampaignTestRules : ICampaignRules
{
    public string GameId => CampaignTestGame.Id;

    public JsonElement NewDocument(CampaignInfo info) => JsonSerializer.SerializeToElement(new { });

    public SeatCard CardFor(CampaignInfo info, JsonElement document, ProfileId member) =>
        new(info.Members.FirstOrDefault(m => m.Profile == member)?.DisplayName ?? "Nobody", "Lv 1");

    public JsonElement PortalView(CampaignInfo info, JsonElement document, ProfileId member) => document;

    public CommandResult Handle(CampaignInfo info, JsonElement document, ProfileId member, string type, JsonElement data) =>
        new(null, $"Unknown command {type}.");
}
