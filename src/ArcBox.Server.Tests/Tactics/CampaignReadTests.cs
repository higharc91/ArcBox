using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Games;
using ArcBox.Server.Rooms;
using ArcBox.Server.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Tactics.Game;
using Tactics.Logic;

namespace ArcBox.Server.Tests.Tactics;

public sealed class CampaignReadTests
{
    private static readonly ProfileId Profile = new("pr_ana");
    private static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
            {
                var root = Path.Combine(dir.FullName, "content/tactics/base");
                return RulesPack.Load([Path.Combine(root, "rules")], ArtManifests.Load(root));
            }
        throw new DirectoryNotFoundException();
    });

    public static IEnumerable<object[]> BadDocuments()
    {
        foreach (var resume in new[] { false, true })
            foreach (var kind in new[] { "malformed", "shape", "newer", "newer-shape", "null", "array", "class", "item", "absent-class" })
                yield return [kind, resume];
    }

    [Theory, MemberData(nameof(BadDocuments))]
    public void Bad_campaign_refuses_start_or_resume_without_a_single_write(string kind, bool resume)
    {
        var text = BadDocument(kind);
        var lease = new Lease(text);
        using var session = Session(lease);

        if (resume) session.Resume(); else session.Start();

        Assert.True(session.IsOver);
        Assert.NotNull(session.Results);
        Assert.Contains(kind.StartsWith("newer", StringComparison.Ordinal) ? "newer version" : kind is "class" or "item" or "absent-class" ? "content pack" : "needs attention", session.Results.Message);
        Assert.Contains("not been changed", session.Results.Message);
        Assert.Equal(text, lease.Text);
        Assert.Equal(0, lease.Writes);
    }

    [Fact]
    public void Only_the_empty_object_marker_creates_a_new_campaign()
    {
        var lease = new Lease("{  }");
        using var session = Session(lease);
        session.Start();
        Assert.False(session.IsOver);
        Assert.True(lease.Writes > 0);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("shape")]
    [InlineData("newer-shape")]
    [InlineData("class")]
    [InlineData("item")]
    public async Task The_actual_room_sends_the_same_refusal_to_tv_and_phone_and_keeps_the_stored_bytes(string kind)
    {
        await using var f = new RoomFixture(rules: new TacticsCampaignRules(Pack.Value),
            game: new GameRegistration(() => new TacticsGame(Pack.Value)));
        await f.HostAsync();
        await f.JoinAsync("Ana", "c1");
        await f.Room.SelectGameAsync("c1", TacticsGame.Id);
        await f.Room.CreateCampaignAsync("c1", "Embers");
        var id = f.Campaigns.ListForGame(TacticsGame.Id).Single().Id;
        var lease = f.Campaigns.CheckOut(id, "editor")!;
        lease.Save(JsonDocument.Parse(BadDocument(kind)).RootElement.Clone());
        f.Campaigns.Release(id, "editor");
        var bytes = f.Campaigns.DocumentOf(id).GetRawText();
        var version = f.Campaigns.Find(id)!.Version;

        await f.Room.StartGameAsync("c1");

        var tv = Assert.IsType<GameResults>(Assert.Single(f.Sender.Of("host", ClientMethods.GameEnded)).Args[0]);
        var phone = Assert.IsType<GameResults>(Assert.Single(f.Sender.Of("c1", ClientMethods.GameEnded)).Args[0]);
        Assert.Equal(tv.Message, phone.Message);
        Assert.Contains("not been changed", tv.Message);
        Assert.Equal(bytes, f.Campaigns.DocumentOf(id).GetRawText());
        Assert.Equal(version, f.Campaigns.Find(id)!.Version);
    }

    private static GameSession Session(Lease lease) => new(new TacticsGame(Pack.Value),
        [new Player(new PlayerId("p1"), "Ana", true, Profile)], new FakeTimeProvider(), 1,
        action => action(), NullLogger.Instance, lease);

    private static string BadDocument(string kind)
    {
        if (kind == "malformed") return "{ malformed JSON";
        if (kind == "shape") return "{ \"version\": \"not-a-number\" }";
        if (kind == "null") return " null ";
        if (kind == "array") return " [1, 2] ";
        if (kind == "newer-shape") return $$"""{ "version": {{TacticsDocument.CurrentVersion + 1}}, "members": [] }""";
        var rules = Pack.Value;
        var (character, problem) = CharacterRules.Create("Ana", "human", "fighter", rules.Backgrounds.Keys.First(),
            Enum.GetValues<Stat>().ToDictionary(s => s, _ => 10), 1, rules);
        Assert.Null(problem);
        var members = Ordinal.Map<MemberRecord>().Add(Profile.Value, new MemberRecord(character!, 0));
        if (kind == "absent-class") members = members.Add("pr_absent", new MemberRecord(character! with { Class = "retired_class" }, 0));
        var doc = TacticsDocument.New with
        {
            Members = members,
            World = new WorldSave("ashen_ford", new Tile(30, 59), new Tile(31, 59), Profile.Value, [], []),
        };
        var node = JsonNode.Parse(doc.ToJson().GetRawText())!.AsObject();
        if (kind == "newer") node["version"] = TacticsDocument.CurrentVersion + 1;
        if (kind == "class") node["members"]![Profile.Value]!["character"]!["class"] = "retired_class";
        if (kind == "item") node["members"]![Profile.Value]!["character"]!["bag"] = JsonNode.Parse("""[{"item":"retired_item","count":1}]""");
        return " \n" + node.ToJsonString() + "\n ";
    }

    private sealed class Lease(string text) : ICampaignLease
    {
        public string Text { get; private set; } = text;
        public int Writes { get; private set; }
        public CampaignInfo Info { get; } = new("c1", "ABCD", TacticsGame.Id, "Embers", Profile,
            [new CampaignMember(Profile, "Ana", CampaignRole.Owner, DateTimeOffset.UnixEpoch)], 1);
        public JsonElement Document => JsonDocument.Parse(Text).RootElement.Clone();
        public void Save(JsonElement document) { Writes++; Text = document.GetRawText(); }
        public void Checkpoint(JsonElement document, string label) => Save(document);
    }
}
