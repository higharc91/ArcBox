using Tactics.Logic;

namespace Tactics.Logic.Tests;

/// <summary>
/// The owner's decision 2 (2026-11-03, issue #83): a failed pick, force or seal on a lock, and a failed search, are spent until the party's
/// next long rest. <see cref="InteractableSave.Rest"/> names the rest that reopens them; that's the one sanctioned way back
/// <see cref="Interactables.Regressed"/> allows.
/// </summary>
public sealed class RestReopensTests
{
    private static readonly InteractableDef Chest = new("c", "chest", new Tile(1, 1), Lock: new LockDef(Pick: 12, Force: 14, Seal: 15));

    [Fact]
    public void A_way_spent_until_a_rest_reopens_when_that_rest_comes_and_the_save_moves_on()
    {
        var spent = new InteractableSave("closed", Spent: ["pick", "force", "seal", "disarm"], Searched: true, Rest: 1);  // failed at rest 0
        Assert.Equal(spent, Interactables.Rested(spent, 0));                // no rest since: still spent
        var rested = Interactables.Rested(spent, 1);
        Assert.Equal(["disarm"], rested.Spent);                             // a failed disarm sprang its trap: never reopened
        Assert.False(rested.Searched);
        Assert.Equal(2, rested.Rest);
        Assert.Equal(rested, Interactables.Rested(rested, 1));
        var again = rested with { Spent = ["disarm", "pick"] };            // failed again at rest 1: spent until rest 2
        Assert.Equal(again, Interactables.Rested(again, 1));
        Assert.Equal(["disarm"], Interactables.Rested(again, 2).Spent);
    }

    [Fact]
    public void A_save_from_before_names_no_rest_and_is_stamped_with_the_next_one()
    {
        var old = new InteractableSave("closed", Spent: ["pick"]);
        Assert.Equal(old, Interactables.Rested(old, 5));                    // unstamped: never reopened as it stands
        Assert.Equal(6, Interactables.Stamped(old, 5).Rest);
        Assert.Equal(["pick"], Interactables.Rested(Interactables.Stamped(old, 5), 5).Spent);
        Assert.Empty(Interactables.Rested(Interactables.Stamped(old, 5), 6).Spent);
        var stamped = old with { Rest = 2 };
        Assert.Equal(stamped, Interactables.Stamped(stamped, 5));           // a stamped save is never stamped again
        Assert.Equal(new InteractableSave("open"), Interactables.Stamped(new InteractableSave("open"), 5));
    }

    [Fact]
    public void Regressed_allows_a_failed_way_or_search_back_only_across_a_rest()
    {
        var spent = new InteractableSave("closed", Spent: ["pick"], Searched: true, Rest: 1);
        Assert.True(Interactables.Regressed(Chest, spent, spent with { Spent = [] }, false));
        Assert.True(Interactables.Regressed(Chest, spent, spent with { Searched = false }, false));
        Assert.False(Interactables.Regressed(Chest, spent, Interactables.Rested(spent, 1), false));
        Assert.True(Interactables.Regressed(Chest, spent with { Spent = ["disarm"] }, spent with { Spent = [], Rest = 2 }, false));
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
