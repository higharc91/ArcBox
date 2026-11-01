using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>A beat as a branch of the campaign reaches it: the flags set before it starts, and the choice the branch takes there.</summary>
internal sealed record BeatStep(Arc Arc, ArcBeat Beat, ImmutableHashSet<string> Before, ArcChoice? Chosen)
{
    /// <summary>The flags a scene of the beat can see as it starts: those before, and the branch's choice, which a vote in it sets.</summary>
    public ImmutableHashSet<string> During => Chosen is { } c ? Before.Union(c.Sets) : Before;
}

/// <summary>The base pack's whole campaign, played chapter after chapter by <see cref="StoryRules.Available"/>.</summary>
internal static class Campaign
{
    public static readonly Lazy<LoreBook> Book = new(() => LoreBook.Load(ContentPaths.ContentBase));

    /// <summary>
    /// Every branch through every chapter, in order, one per combination of choices: each choice beat forks, and a branch carries its
    /// flags into the next chapter. Flags come from beat and choice <c>sets</c> only, which is what <c>StoryOrder</c> reads too.
    /// </summary>
    public static IEnumerable<ImmutableList<BeatStep>> Branches(LoreBook book)
        => Play([.. book.Arcs.OrderBy(a => a.Chapter)], 0, [], [], []);

    private static IEnumerable<ImmutableList<BeatStep>> Play(ImmutableArray<Arc> arcs, int index, ImmutableHashSet<string> flags,
        ImmutableHashSet<string> done, ImmutableList<BeatStep> path)
    {
        if (index == arcs.Length)
        {
            yield return path;
            yield break;
        }
        var arc = arcs[index];
        if (StoryRules.Available(arc, flags, done) is not { } beat)
        {
            foreach (var rest in Play(arcs, index + 1, flags, done, path))
                yield return rest;
            yield break;
        }
        Assert.True(path.Count < arcs.Sum(a => a.Beats.Length), "The campaign offered more beats than it has.");
        var after = flags.Union(beat.Sets);
        var nowDone = done.Add(beat.Id);
        ImmutableArray<ArcChoice?> choices = beat.Choices.IsEmpty ? [null] : [.. beat.Choices];
        foreach (var choice in choices)
            foreach (var rest in Play(arcs, index, choice is null ? after : after.Union(choice.Sets), nowDone, path.Add(new BeatStep(arc, beat, flags, choice))))
                yield return rest;
    }
}
