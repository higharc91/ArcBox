using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class EntranceTravelTests
{
    private static readonly Lazy<RulesPack> Pack = new(Load);
    internal static RulesPack TestRules => Pack.Value;

    private static RulesPack Load()
    {
        var dir = Directory.CreateTempSubdirectory("tactics-entrance-travel-");
        try
        {
            var files = new List<string>();
            foreach (var id in new[] { "outside", "hollow" })
            {
                var region = new RegionDef(id, id, 1, [8, 8], ["grass/lush"],
                    [new RegionChunk(0, 0, Convert.ToBase64String(new byte[32 * 32 * 3]))], [], [],
                    new RegionSpawns(new Tile(1, 1), new Tile(2, 1), Ordinal.Map(new[] { ("entrance", new Tile(2, 2)) })), []);
                var thing = new InteractableDef(id == "outside" ? "mouth" : "exit", "cave_mouth", new Tile(3, 2), Name: id == "outside" ? "the Hollow" : "the Ford",
                    Text: [], Unless: [], Loot: [], Targets: [], Reveals: [],
                    Requires: id == "outside" ? ["bounty_accepted"] : [], Sets: id == "outside" ? ["hollow_entered"] : [],
                    To: new EntranceDestination(id == "outside" ? "hollow" : "outside", "entrance"));
                Write("world/" + id + ".json", region);
                Write("interactables/" + id + ".json", new InteractablesFile(id, [thing]));
            }
            File.WriteAllText(Path.Combine(dir.FullName, "manifest.json"), JsonSerializer.Serialize(new { id = "entrance_test", rulesVersion = 1, requires = new[] { "base" }, files }, ArcJson.Options));
            return RulesPack.Load([Path.Combine(ContentBase(), "rules"), dir.FullName], ArtManifests.Load(ContentBase()));

            void Write(string relative, object value)
            {
                files.Add(relative);
                var path = Path.Combine(dir.FullName, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(value, ArcJson.Options));
            }
        }
        finally { dir.Delete(recursive: true); }
    }

    private static Session Outside(bool accepted = true) => Vault.At(new Tile(2, 2), Pack.Value,
        w => w with { Region = "outside", Flags = accepted ? ["bounty_accepted"] : [], RestReady = true });

    private static void Vote(Session s, int enters = 4)
    {
        for (var i = 0; i < 4; i++) s.Send(i, "vote", new { option = i < enters ? "enter" : "later" });
        StoryVoteTests.Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "the travel decision");
    }

    [Fact]
    public void A_tied_vote_stays_put_without_spending_rest_or_setting_arrival_flags()
    {
        var s = Outside();
        s.Send(0, "interact", new { target = "mouth" });
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Vote(s, 2);
        Assert.Equal("outside", s.Game.Document.World!.Region);
        Assert.DoesNotContain("hollow_entered", s.Game.Document.World.Flags);
        Assert.True(s.Game.Document.World.RestReady);
    }

    [Fact]
    public void Travel_saves_the_named_destination_survives_restart_and_end_session_and_returns()
    {
        var s = Outside();
        var epoch = s.Host.Explore!.Epoch;
        s.Send(0, "interact", new { target = "mouth" });
        Vote(s);
        Assert.Equal("hollow", s.Game.Document.World!.Region);
        Assert.Equal(new Tile(2, 2), s.Game.Document.World.At);
        Assert.Contains("hollow_entered", s.Game.Document.World.Flags);
        Assert.True(s.Host.Explore!.Epoch > epoch);
        Assert.Equal("hollow", s.Host.Explore.Region);
        Assert.Equal(["exit"], s.Host.Explore.Things!.Value.Select(t => t.Id));
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Pack.Value));
        again.Harness.Resume(again.Lease);
        StoryResumeTests.AllBack(again);
        Assert.Equal("hollow", again.Game.Document.World!.Region);
        again.Send(0, "endSession");
        var next = new Session(document: again.Lease.Document, game: new TacticsGame(Pack.Value));
        next.Harness.StartCampaign(next.Lease);
        Assert.Equal("hollow", next.Game.Document.World!.Region);
        next.Send(StageBattleTests.Lead(next), "interact", new { target = "exit" });
        Assert.Equal(TacticsPhase.Story, next.Game.Phase);
        Vote(next);
        Assert.Equal("outside", next.Game.Document.World!.Region);
        Assert.Equal(new Tile(2, 2), next.Game.Document.World.At);
    }

    [Fact]
    public void A_nonleader_or_a_party_without_the_gate_cannot_propose_travel()
    {
        var s = Outside();
        s.Send(1, "interact", new { target = "mouth" });
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        var locked = Outside(false);
        locked.Send(0, "interact", new { target = "mouth" });
        Assert.Equal(TacticsPhase.Exploration, locked.Game.Phase);
        Assert.Equal("outside", locked.Game.Document.World!.Region);
    }

    [Fact]
    public void A_restart_after_the_vote_was_saved_finishes_the_transfer_once()
    {
        var s = Outside();
        s.Send(0, "interact", new { target = "mouth" });
        var doc = s.Game.Document;
        var confirmed = doc with { World = doc.World! with { Story = doc.World.Story! with { Node = "enter", Line = 0, Played = ["ask", "enter"] } } };
        var again = new Session(document: confirmed.ToJson(), game: new TacticsGame(Pack.Value));
        again.Harness.Resume(again.Lease);
        StoryResumeTests.AllBack(again);
        Assert.Equal("hollow", again.Game.Document.World!.Region);
        Assert.Single(again.Game.Document.World.Flags, f => f == "hollow_entered");
        Assert.True(again.Game.Document.World.RestReady);
        Assert.Equal(doc.Members.Values.Select(m => m.Xp), again.Game.Document.Members.Values.Select(m => m.Xp));
    }

    [Fact]
    public void A_travel_question_holds_while_every_phone_is_away()
    {
        var s = Outside();
        s.Send(0, "interact", new { target = "mouth" });
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, false);
        s.Harness.Advance(TimeSpan.FromHours(1));
        Assert.Equal("outside", s.Game.Document.World!.Region);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true);
        Vote(s);
        Assert.Equal("hollow", s.Game.Document.World.Region);
    }
}
