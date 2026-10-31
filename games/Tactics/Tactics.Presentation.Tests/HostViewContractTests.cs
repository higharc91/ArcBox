using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Tests;

/// <summary>
/// The TV reads real host views: every phase of a real session, written the way the server writes them and read the way
/// the scene reads them, turns into a screen.
/// </summary>
public sealed class HostViewContractTests
{
    private static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return RulesPack.Load([Path.Combine(dir.FullName, "content", "tactics", "base", "rules")]);
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    private static TacticsHostView OverTheWire(TacticsGame game)
        => JsonSerializer.SerializeToElement(game.HostView(), ArcJson.Options).Deserialize<TacticsHostView>(ArcJson.Options)!;

    [Fact]
    public void Every_phase_of_a_real_session_reaches_the_tv()
    {
        var rules = Pack.Value;
        ProfileId?[] profiles = [new ProfileId("pr_ana"), new ProfileId("pr_bo")];
        var game = new TacticsGame(rules);
        var h = new GameHarness<TacticsGame>(game, playerCount: 2, seed: 3, profiles: profiles);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
            [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
        h.StartCampaign(new FakeCampaignLease(info, new TacticsCampaignRules(rules).NewDocument(info)));

        var muster = OverTheWire(game);
        Assert.Equal("Muster · 0 ready", PhaseScreen.Of(muster).Meta);

        h.Input(0, new { action = "pickClass", data = new { @class = "fighter" } });
        Assert.Equal(["Player1"], PhaseScreen.Of(OverTheWire(game)).Rows.Select(r => r.Title));
        h.Input(1, new { action = "pickClass", data = new { @class = "cleric" } });

        var pick = PhaseScreen.Of(OverTheWire(game));
        Assert.Contains(pick.Rows, r => r is { Title: "River Crossing", Detail: "4–6 players · +2 AI companions", Value: "Lv 1" });

        h.Input(0, new { action = "pickEncounter", data = new { encounter = "river_crossing" } });
        h.SetConnected(1, false);
        h.SetConnected(0, false);
        for (var step = 0; step < 3; step++)
            h.Advance(TimeSpan.FromSeconds(7));

        var fight = OverTheWire(game);
        Assert.NotNull(fight.Battle);
        Assert.NotEmpty(fight.Battle.Beats.SelectMany(b => b.Events));   // the polymorphic events survive the trip
        Assert.StartsWith("Battle · River Crossing · Round ", PhaseScreen.Of(fight).Meta);
        Assert.NotNull(fight.Battle.Board);                              // and so do the board's changes

        for (var step = 0; step < 20_000 && game.Phase == TacticsPhase.Battle; step++)
            h.Advance(TimeSpan.FromSeconds(7));
        h.SetConnected(0, true);

        var over = PhaseScreen.Of(OverTheWire(game));
        Assert.StartsWith("Results · ", over.Meta);
        Assert.Equal(2, over.Rows.Length);
        Assert.All(over.Rows, r => Assert.EndsWith(" XP", r.Value));
    }
}
