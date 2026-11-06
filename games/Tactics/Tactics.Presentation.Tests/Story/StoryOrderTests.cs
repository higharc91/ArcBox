using Tactics.Presentation.Story;

namespace Tactics.Presentation.Tests.Story;

/// <summary><see cref="StoryOrder"/> against Godot's <c>move_child</c>, modelled on a list: once in place, the story stays put.</summary>
public sealed class StoryOrderTests
{
    /// <summary>Godot's <c>move_child</c>: the node comes out, and goes back in at <paramref name="to"/>.</summary>
    private static void MoveChild(List<string> children, string node, int to)
    {
        children.Remove(node);
        children.Insert(to, node);
    }

    private static bool Place(List<string> children)
    {
        if (StoryOrder.Move(children.IndexOf("story"), children.IndexOf("caption")) is not { } to)
            return false;
        MoveChild(children, "story", to);
        return true;
    }

    [Theory]
    [InlineData("world panels caption audio story")]      // just added, at the end
    [InlineData("story world panels caption audio")]      // far under
    [InlineData("world story panels caption")]
    [InlineData("world caption story")]                   // just over
    public void The_story_goes_just_under_the_caption_and_stays_there_view_after_view(string start)
    {
        var children = start.Split(' ').ToList();
        Assert.True(Place(children));
        var placed = children.ToList();

        Assert.Equal(children.IndexOf("caption") - 1, children.IndexOf("story"));
        for (var view = 0; view < 5; view++)
            Assert.False(Place(children));                                     // no move at all
        Assert.Equal(placed, children);
    }

    [Fact]
    public void Moving_to_the_captions_index_every_view_flips_the_two()
    {
        var children = new List<string> { "world", "story", "caption" };
        MoveChild(children, "story", children.IndexOf("caption"));
        Assert.Equal(["world", "caption", "story"], children);                  // why the scene asks StoryOrder: the old code's flip
    }
}
