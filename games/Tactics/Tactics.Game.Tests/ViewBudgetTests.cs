using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Logic;
using static Tactics.Game.Tests.ExplorationTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>Wire-size ceilings for a full party in Ashen Ford.</summary>
public sealed class ViewBudgetTests
{
    private const int ExploreBudget = 6 * 1024;
    private const int PhoneBudget = 1280;
    private const int SixPhoneBudget = 6400;

    [Fact]
    public void Ashen_ford_views_fit_six_player_byte_budgets()
    {
        var doc = TacticsDocument.Read(DocumentAt(Saved(NearStart())));
        var character = doc.Members.Values.First();
        ProfileId?[] profiles = [.. Enumerable.Range(1, 6).Select(i => (ProfileId?)new ProfileId($"pr_member{i}"))];
        var full = doc with { Members = Ordinal.Map(profiles.Select(p => (p!.Value.Value, character))) };
        var harness = new GameHarness<TacticsGame>(new TacticsGame(Rules), playerCount: 6, profiles: profiles);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value, [], 1);
        harness.StartCampaign(new FakeCampaignLease(info, full.ToJson()));

        var explore = harness.HostView<TacticsHostView>().Explore;
        Assert.NotNull(explore);
        Assert.Equal("ashen_ford", explore.Region);
        Assert.Equal(6, explore.Party.Length);
        var exploreBytes = JsonSerializer.SerializeToUtf8Bytes(explore, ArcJson.Options).Length;
        var phoneBytes = Enumerable.Range(0, 6)
            .Select(i =>
            {
                var view = harness.PlayerView(i);
                Assert.NotNull(view);
                return JsonSerializer.SerializeToUtf8Bytes(view, ArcJson.Options).Length;
            })
            .ToArray();

        Assert.True(exploreBytes <= ExploreBudget, $"explore={exploreBytes}; phones=[{string.Join(",", phoneBytes)}]; total={phoneBytes.Sum()}");
        Assert.All(phoneBytes, bytes => Assert.InRange(bytes, 1, PhoneBudget));
        Assert.InRange(phoneBytes.Sum(), 1, SixPhoneBudget);
    }
}
