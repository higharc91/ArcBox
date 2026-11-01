using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Logic;

namespace Tactics.Game.Tests;

/// <summary>AI companions fill a party smaller than the encounter needs (the user's call after the phone playtest).</summary>
public sealed class CompanionTests
{
    private static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return RulesPack.Load([Path.Combine(dir.FullName, "content", "tactics", "base", "rules")]);
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    private static RulesPack Rules => Pack.Value;

    /// <summary>A two-member campaign (a cleric and a fighter) at the battle pick.</summary>
    private static (GameHarness<TacticsGame> H, TacticsGame Game, FakeCampaignLease Lease) TwoPlayers(JsonElement? document = null)
    {
        ProfileId?[] profiles = [new ProfileId("pr_a"), new ProfileId("pr_b")];
        var game = new TacticsGame(Rules);
        var h = new GameHarness<TacticsGame>(game, playerCount: 2, profiles: profiles);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
            [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
        var lease = new FakeCampaignLease(info, document ?? new TacticsCampaignRules(Rules).NewDocument(info));
        return (h, game, lease);
    }

    private static void PickClasses(GameHarness<TacticsGame> h)
    {
        h.Input(0, new { action = "pickClass", data = new { @class = "cleric" } });
        h.Input(1, new { action = "pickClass", data = new { @class = "fighter" } });
    }

    private static void ToBattle(GameHarness<TacticsGame> h)
    {
        PickClasses(h);
        h.Input(0, new { action = "pickEncounter", data = new { encounter = "river_crossing" } });
    }

    [Fact]
    public void Two_players_get_two_companions_of_the_roles_they_lack_and_play_to_the_end()
    {
        var (h, game, lease) = TwoPlayers();
        h.StartCampaign(lease);
        PickClasses(h);
        var pick = (CustomView)h.PlayerView(0);
        var crossing = pick.Data.GetProperty("encounters").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "river_crossing");
        Assert.True(crossing.GetProperty("fits").GetBoolean());
        Assert.Equal(2, crossing.GetProperty("companions").GetInt32());

        h.Input(0, new { action = "pickEncounter", data = new { encounter = "river_crossing" } });

        Assert.Equal(TacticsPhase.Battle, game.Phase);
        var party = game.Document.Battle!.State.Units.Values.Where(u => u.Sheet.Side == Side.Party).ToList();
        Assert.Equal(["ally1", "ally2", "hero1", "hero2"], party.Select(u => u.Id.Value).Order(StringComparer.Ordinal));
        var allies = party.Where(u => u.Id.Value.StartsWith("ally", StringComparison.Ordinal)).ToList();
        Assert.All(allies, u => Assert.Equal(new AiControlled("companion"), u.Controller));
        // Healer and defender are taken, so the companions are a striker and a control class.
        Assert.Equal(["control", "striker"], allies.Select(u => Rules.Classes[Tag(u)].Role).Order(StringComparer.Ordinal));
        Assert.All(allies, u => Assert.EndsWith("companion", u.Sheet.Name, StringComparison.Ordinal));

        h.SetConnected(0, false);
        h.SetConnected(1, false);
        for (var i = 0; i < 20_000 && game.Phase == TacticsPhase.Battle; i++)
            h.Advance(TimeSpan.FromSeconds(7));

        Assert.Equal(TacticsPhase.Results, game.Phase);
        var doc = TacticsDocument.Read(lease.Document);
        Assert.Equal(["pr_a", "pr_b"], doc.Members.Keys);   // companions earn nothing and stay out of the campaign
        Assert.All(doc.Members.Values, m => Assert.True(m.Fought));
    }

    [Fact]
    public void A_battle_with_companions_restarts_with_the_same_companions()
    {
        var (h, game, lease) = TwoPlayers();
        h.StartCampaign(lease);
        ToBattle(h);
        var doc = TacticsDocument.Read(lease.Document);
        var stale = doc with { Battle = doc.Battle! with { State = doc.Battle.State with { ContentHash = "old" } } };

        var (h2, game2, lease2) = TwoPlayers(stale.ToJson());
        h2.Resume(lease2);

        var allies = game2.Document.Battle!.State.Units.Values.Where(u => u.Id.Value.StartsWith("ally", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, allies.Count);
        Assert.Equal(doc.Battle.Companions.Select(c => c.Class), allies.OrderBy(u => u.Id.Value).Select(Tag));
    }

    private static string Tag(UnitState u)
        => u.Sheet.Tags.First(t => t.StartsWith(SheetBuilder.ClassTag, StringComparison.Ordinal))[SheetBuilder.ClassTag.Length..];
}
