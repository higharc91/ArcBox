using Tactics.Logic;

namespace Tactics.Logic.Tests;

/// <summary>
/// The owner's decision 2 (2026-11-03, issue #83): a failed pick, force or seal on a lock, and a failed search, are spent until the party's
/// next long rest. That's the one sanctioned way back <see cref="Interactables.Regressed"/> allows.
/// </summary>
public sealed class RestReopensTests
{
    private static readonly InteractableDef Chest = new("c", "chest", new Tile(1, 1), Lock: new LockDef(Pick: 12, Force: 14, Seal: 15));

    [Fact]
    public void A_rest_after_the_one_a_way_was_spent_in_reopens_it_and_says_so()
    {
        var spent = new InteractableSave("closed", Spent: ["pick", "force", "seal", "disarm"], Searched: true);    // an old save: rest 0
        Assert.Equal(spent, Interactables.Rested(spent, 0));                // no rest since: still spent
        var rested = Interactables.Rested(spent, 1);
        Assert.Equal(["disarm"], rested.Spent);                             // a failed disarm sprang its trap: never reopened
        Assert.False(rested.Searched);
        Assert.Equal(1, rested.Rest);
        Assert.Equal(rested, Interactables.Rested(rested, 1));
        var again = rested with { Spent = ["disarm", "pick"] };            // spent again at rest 1
        Assert.Equal(again, Interactables.Rested(again, 1));
        Assert.Equal(["disarm"], Interactables.Rested(again, 2).Spent);
    }

    [Fact]
    public void Regressed_allows_a_failed_way_or_search_back_only_across_a_rest()
    {
        var spent = new InteractableSave("closed", Spent: ["pick"], Searched: true);
        Assert.True(Interactables.Regressed(Chest, spent, spent with { Spent = [] }, false));
        Assert.True(Interactables.Regressed(Chest, spent, spent with { Searched = false }, false));
        Assert.False(Interactables.Regressed(Chest, spent, Interactables.Rested(spent, 1), false));
        Assert.True(Interactables.Regressed(Chest, spent with { Spent = ["disarm"] }, spent with { Spent = [], Rest = 1 }, false));
        Assert.True(Interactables.Regressed(Chest, spent with { Rest = 2 }, spent with { Rest = 1 }, false));     // the rest never goes back
        Assert.True(Interactables.Regressed(Chest, new("open", Unlocked: true, Keyed: true), new("open", Unlocked: true), false));
        Assert.True(Interactables.Regressed(Chest, new("open", Unlocked: true), new("closed", Unlocked: true, Rest: 1), false));  // a rest reopens nothing else
    }

    [Fact]
    public void Only_lock_ways_reopen()
    {
        Assert.True(Interactables.Reopens(Interactables.PickApproach));
        Assert.True(Interactables.Reopens(Interactables.ForceApproach));
        Assert.True(Interactables.Reopens(Interactables.SealApproach));
        Assert.False(Interactables.Reopens(Interactables.DisarmApproach));
        Assert.False(Interactables.Reopens(Interactables.KeyApproach));
    }
}
