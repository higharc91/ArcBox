using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;

namespace Tactics.Game.Tests;

public sealed class PartyCheckTests
{
    private static readonly Lazy<LoreBook> HelpBook = new(() => Book(false));
    private static readonly Lazy<LoreBook> GroupBook = new(() => Book(true));
    private static LoreBook Book(bool group) => EditedLore(scenes =>
    {
        var scene = scenes.Single(s => s!["id"]!.GetValue<string>() == "ch1_ford")!;
        var node = scene["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "vote")!;
        var check = node["options"]!.AsArray().Single(o => o!["id"]!.GetValue<string>() == "pay")!["check"]!;
        check["skill"] = group ? "stealth" : "arcana";
        check["group"] = group;
    });
    internal static Session Waiting(bool group = false)
    {
        var s = AtTheVote();
        for (var i = 0; i < 4; i++) Vote(s, i, "pay");
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.PickRoller, "the check");
        return Again(s, group, 99);
    }
    private static Session Again(Session s, bool group, int seed, int? checkSeed = null)
    {
        var doc = TacticsDocument.Read(s.Lease.Document);
        if (checkSeed is not null) doc = doc with { World = doc.World! with { Story = doc.World.Story! with { CheckSeed = checkSeed } } };
        var next = new Session(seed: seed, document: doc.ToJson(), game: new TacticsGame(Rules, lore: group ? GroupBook.Value : HelpBook.Value));
        next.Harness.Resume(next.Lease); StoryResumeTests.AllBack(next);
        Until(next, () => next.Host.Story?.State is TacticsGame.StoryStates.PickRoller or TacticsGame.StoryStates.Rolled, "the restored check");
        return next;
    }
    internal static void Pick(Session s, int seat = 0)
    {
        for (var i = 0; i < 4; i++) s.Send(i, "pickRoller", new { member = s.Profiles[seat]!.Value.Value });
        Until(s, () => s.Game.Document.World!.Story!.Roll is not null, "the roll");
    }
    private static JsonElement Roll(Session s) => JsonSerializer.SerializeToElement(s.Game.Document.World!.Story!.Roll, ArcJson.Options);

    [Fact]
    public void A_proficient_helper_grants_advantage_and_both_pending_and_rolled_restarts_keep_the_dice()
    {
        var s = Waiting();
        Assert.True(SkillCheck.Proficient(SheetBuilder.ForCharacter(s.Game.Document.Members["pr_member4"].Character, Rules), "arcana"));
        s.Send(3, "help");
        s.Send(3, "help"); // a duplicate tap must not withdraw the offer
        Assert.Equal("pr_member4", s.Screen(0)!.Data.GetProperty("help").GetProperty("member").GetString());
        var restored = Again(s, false, 987);
        Pick(s); Pick(restored);
        Assert.Equal(Roll(s).GetRawText(), Roll(restored).GetRawText());
        var roll = Roll(s);
        Assert.Equal("pr_member4", roll.GetProperty("helper").GetString());
        var dice = roll.GetProperty("dice").EnumerateArray().Select(d => d.GetInt32()).ToArray();
        Assert.Equal(2, dice.Length);
        Assert.Equal(dice.Max(), roll.GetProperty("die").GetInt32());
        var after = Again(restored, false, 12345);
        Assert.Equal(roll.GetRawText(), Roll(after).GetRawText());
        Assert.Contains("Player4", JsonSerializer.Serialize(s.Host.Story!.Check, ArcJson.Options));
    }

    [Fact]
    public void An_untrained_or_absent_helper_cannot_help_and_a_roller_cannot_help_themselves()
    {
        var s = Waiting();
        s.Send(0, "help");
        Assert.Equal(JsonValueKind.Null, s.Screen(0)!.Data.GetProperty("help").GetProperty("member").ValueKind);
        s.Harness.SetConnected(3, false); s.Send(3, "help");
        Assert.Equal(JsonValueKind.Null, s.Screen(0)!.Data.GetProperty("help").GetProperty("member").ValueKind);
        s.Harness.SetConnected(3, true); s.Send(3, "help"); Pick(s, 3);
        Assert.False(Roll(s).TryGetProperty("helper", out _));
    }

    [Fact]
    public void Exactly_half_of_an_even_party_is_enough_and_the_TV_shows_each_hero()
    {
        var original = Waiting(true);
        for (var seed = 0; seed < 40; seed++)
        {
            var s = Again(original, true, 99, seed);
            s.Send(0, "groupRoll");
            var rolled = Roll(s);
            var group = rolled.GetProperty("group").EnumerateArray().ToArray();
            if (group.Count(r => r.GetProperty("passed").GetBoolean()) != 2) continue;
            Assert.True(rolled.GetProperty("passed").GetBoolean());
            Assert.Equal(4, s.Host.Story!.Check!.Group.Length);
            var saved = s.Lease.Document.GetRawText();
            s.Send(0, "groupRoll");
            Assert.Equal(saved, s.Lease.Document.GetRawText());
            return;
        }
        Assert.Fail("No two-of-four result in the deterministic seed sample.");
    }

    [Fact]
    public void A_group_rolls_every_present_hero_and_half_or_more_passes_with_restart_safe_results()
    {
        var s = Waiting(true);
        s.Harness.SetConnected(3, false);
        var next = Again(s, true, 777); next.Harness.SetConnected(3, false);
        s.Send(0, "groupRoll"); next.Send(0, "groupRoll");
        Until(s, () => s.Game.Document.World!.Story!.Roll is not null, "group roll");
        Until(next, () => next.Game.Document.World!.Story!.Roll is not null, "restored group roll");
        var roll = Roll(s);
        Assert.Equal(roll.GetRawText(), Roll(next).GetRawText());
        var group = roll.GetProperty("group").EnumerateArray().ToArray();
        Assert.Equal(3, group.Length);
        Assert.DoesNotContain(group, r => r.GetProperty("member").GetString() == "pr_member4");
        Assert.All(group, r => Assert.InRange(r.GetProperty("die").GetInt32(), 1, 20));
        var passed = group.Count(r => r.GetProperty("passed").GetBoolean());
        Assert.Equal(passed * 2 >= group.Length, roll.GetProperty("passed").GetBoolean());
    }
}
