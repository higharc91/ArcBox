using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

public sealed class UnitTextureQueueTests
{
    [Fact]
    public void Shared_sheets_upload_once_and_party_changes_drop_only_unused_sheets()
    {
        var queue = new UnitTextureQueue();
        queue.Retain(["farmer", "fighter", "farmer", "wizard"]);
        Assert.Equal(3, queue.Pending);
        Assert.True(queue.TryTake(out var first));
        Assert.Equal("farmer", first);
        Assert.Empty(queue.Retain(["wizard", "farmer", "fighter"]));
        Assert.Equal(2, queue.Pending);
        Assert.Equal(["fighter"], queue.Retain(["farmer", "cleric", "wizard"]));
        var loaded = new List<string>();
        while (queue.TryTake(out var path)) loaded.Add(path);
        Assert.Equal(["wizard", "cleric"], loaded);
        Assert.Equal(0, queue.Pending);
        Assert.Equal(["cleric", "farmer", "wizard"], queue.Retain([]).Order());
    }

    [Fact]
    public void Travel_before_upload_cancels_stale_work_and_readding_does_not_duplicate_uploads()
    {
        var queue = new UnitTextureQueue();
        queue.Retain(["old-region", "shared"]);
        queue.Retain(["shared", "new-region"]);
        queue.Retain(["old-region", "shared", "new-region"]);
        var loaded = new List<string>();
        while (queue.TryTake(out var path)) loaded.Add(path);
        Assert.Equal(3, loaded.Count);
        Assert.Equal(3, loaded.Distinct().Count());
        Assert.Equal(0, queue.Pending);
        Assert.False(queue.TryTake(out _));
    }
}
