using System.Collections.Immutable;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.ConversationQuestTests;

namespace Tactics.Game.Tests;

public sealed class HollowBountyTests
{
    private static readonly Lazy<LoreBook> Book = new(() => LoreBook.Load(ContentBase()));
    private static readonly string[] Accepted = ["ch1_ford_settled", "sq_ashmaw_posted", "sq_ashmaw_accepted"];

    [Fact]
    public void Dagna_requires_the_posted_bounty_and_accepts_it_without_touching_main_flags()
    {
        var s = At("dagna_holt", "ch1_ford_settled", "sq_ashmaw_posted");
        Talk(s, "dagna_holt");
        Assert.Equal("offer", s.Game.Document.World!.Story!.Node);
        Choose(s, "accept"); WalkAgain(s);
        Assert.Equal(Accepted.Order(), s.Game.Document.World!.Flags.Order());
        Assert.Equal("accepted", Book.Value.Quests.Single(q => q.Id == "sq_ashmaw").State(s.Game.Document.World.Flags.ToHashSet())!.Current.Id);
    }

    [Fact]
    public void Dagna_keeps_the_rewards_safe_until_the_party_confirms_replacement()
    {
        var s = At("dagna_holt", [.. Accepted, "sq_ashmaw_slain", "sq_ashmaw_claw"]);
        var before = s.Game.Document.Members;
        Talk(s, "dagna_holt");
        Assert.Equal("proof", s.Game.Document.World!.Story!.Node);
        Assert.Contains("replaces", s.Host.Story!.Line!.Text, StringComparison.OrdinalIgnoreCase);
        Choose(s, "wait"); WalkAgain(s);
        Assert.DoesNotContain("sq_ashmaw_done", s.Game.Document.World!.Flags);
        Assert.All(s.Game.Document.Members, pair => Assert.Equal(before[pair.Key], pair.Value));
        Talk(s, "dagna_holt"); Choose(s, "present"); WalkAgain(s);
        var saved = TacticsDocument.Read(s.Lease.Document);
        Assert.Equal(60, saved.Members.Values.Sum(m => m.Gold) - before.Values.Sum(m => m.Gold));
        foreach (var (id, member) in saved.Members)
        {
            Assert.Equal(before[id].Xp + 300, member.Xp);
            Assert.Equal(member.Character.Class, Rules.Trinkets[member.Character.Trinket!].Class);
        }
        Assert.Contains("sq_ashmaw", saved.World!.QuestsRewarded);
        Assert.Equal(saved.Members.Count, s.Host.Explore!.QuestEvent!.Gifts!.Value.Length);
        for (var i = 0; i < 4; i++)
        {
            var trinket = Data(s, i).GetProperty("questNotice").GetProperty("trinket");
            Assert.Equal(saved.Members[s.Profiles[i]!.Value.Value].Character.Class, trinket.GetProperty("class").GetString());
            Assert.False(Data(s, i).GetProperty("questNotice").TryGetProperty("gifts", out _));
        }
    }

    [Fact]
    public void The_real_cave_link_survives_restart_and_returns_beside_the_cliff_mouth()
    {
        var world = ExplorationTests.Saved(new Tile(131, 99)) with { Flags = [.. Accepted] };
        var s = new Session(document: ExplorationTests.DocumentAt(world), game: new TacticsGame(Rules, lore: Book.Value));
        s.Harness.StartCampaign(s.Lease);
        s.Send(Lead(s), "interact", new { target = "af_cinder_hollow_mouth" });
        Choose(s, "enter"); WalkAgain(s);
        Assert.Equal("cinder_hollow", s.Game.Document.World!.Region);
        Assert.Contains("sq_ashmaw_entered", s.Game.Document.World.Flags);
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Rules, lore: Book.Value));
        again.Harness.StartCampaign(again.Lease);
        Assert.Equal("cinder_hollow", again.Host.Explore!.Region);
        again.Send(Lead(again), "interact", new { target = "hollow_exit" });
        Choose(again, "enter"); WalkAgain(again);
        Assert.Equal("ashen_ford", again.Game.Document.World!.Region);
        Assert.Equal(new Tile(131, 99), again.Game.Document.World.At);
    }

    [Fact]
    public void Completing_the_side_arc_returns_to_exploration_without_ending_chapter_one()
    {
        var flags = Accepted.Concat(new[] { "sq_ashmaw_entered", "sq_ashmaw_throat_won", "sq_ashmaw_diggings_won", "sq_ashmaw_lair", "sq_ashmaw_slain" }).ToImmutableArray();
        var world = ExplorationTests.Saved(new Tile(11, 33), "cinder_hollow") with
        { Flags = flags, Beats = ["hollow_throat", "hollow_diggings", "hollow_nest"],
            Story = new StorySave("hollow_lair", StorySave.After, "ashmaw_lair_after", "death", 0, 1) };
        var s = new Session(document: ExplorationTests.DocumentAt(world), game: new TacticsGame(Rules, lore: Book.Value));
        s.Harness.StartCampaign(s.Lease);
        WalkAgain(s);
        Assert.Contains("hollow_lair", s.Game.Document.World!.Beats);
        Assert.Null(s.Host.Chapter);
        Assert.DoesNotContain("Chapter II", Data(s, Lead(s)).GetProperty("objective").GetString()!, StringComparison.Ordinal);
        Assert.Contains(s.Host.Explore!.Things!.Value, t => t.Id == "hollow_ember_claw");
    }
}
