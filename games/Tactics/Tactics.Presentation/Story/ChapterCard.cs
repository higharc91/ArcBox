namespace Tactics.Presentation.Story;

/// <summary>
/// The card at the end of a chapter (overworld 5d default 13), worded from <see cref="Tactics.Game.TacticsHostView.Chapter"/>
/// ("Chapter I · The Bridge at the Ford"), for the TV to draw full screen over the world while it shows.
/// </summary>
/// <param name="Kicker">The small line over it: "The end of".</param>
/// <param name="Chapter">"Chapter I".</param>
/// <param name="Title">The arc's title, or empty if the server sent none.</param>
public sealed record ChapterCard(string Kicker, string Chapter, string Title)
{
    /// <summary>The card for the server's <paramref name="chapter"/>, or null with none showing.</summary>
    public static ChapterCard? Of(string? chapter)
    {
        if (string.IsNullOrWhiteSpace(chapter))
            return null;
        var parts = chapter.Split(" · ", 2, StringSplitOptions.TrimEntries);
        return new ChapterCard("The end of", parts[0], parts.Length > 1 ? parts[1] : "");
    }
}
