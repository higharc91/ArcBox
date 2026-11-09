using System.Collections.Immutable;

namespace Tactics.Presentation.Story;

/// <summary>
/// The vote panel's measures, in the 1920×1080 layout's pixels (mockup 02): a panel at the bottom right, its rows in up to three columns, that grows
/// upwards with its options. A question can offer up to eleven (Chapter III's <c>ask_me</c>), so when the rows at full size would make it
/// taller than <see cref="MaxHeight"/>, additional columns keep the lettering at full size.
/// The Godot panel draws at these numbers; nothing here measures a font, so the columns keep a margin for wide letters.
/// </summary>
public static class VoteLayout
{
    /// <summary>The panel's width.</summary>
    public const float Width = 600;

    /// <summary>The tallest the panel goes: from its foot at the bottom margin up to under the leader's panel at the top right.</summary>
    public const float MaxHeight = 840;

    /// <summary>The heading and clock over the rows, the tally under them, and the panel's padding.</summary>
    public const float Chrome = 150;

    /// <summary>A label row's height at full size: the line of its 24 px type.</summary>
    public const float LabelLine = 36;

    /// <summary>A check's detail line ("Persuasion check · DC 15") at full size.</summary>
    public const float DetailLine = 30;

    /// <summary>A row's padding, top and bottom together, at full size.</summary>
    public const float RowPadding = 16;

    /// <summary>The gap between two rows, at full size.</summary>
    public const float RowGap = 10;

    /// <summary>Votes keep their full text size; columns handle crowded questions.</summary>
    public static readonly ImmutableArray<float> Scales = [1f];

    /// <summary>How many characters a label's row holds at <paramref name="scale"/>: <see cref="StoryScreen.LabelColumns"/> at full size.</summary>
    public static int Columns(float scale) => (int)MathF.Floor(StoryScreen.LabelColumns / scale);

    /// <summary>A row's height at <paramref name="scale"/>, gap included.</summary>
    public static float RowHeight(VoteRow row, float scale)
        => (row.Label.Length * LabelLine + (row.Detail is null ? 0 : DetailLine) + (row.Voters.IsEmpty ? 0 : 34) + RowPadding + RowGap) * scale;

    /// <summary>The panel's height with <paramref name="rows"/> at <paramref name="scale"/>.</summary>
    public static float Height(ImmutableArray<VoteRow> rows, float scale) => Chrome + ColumnHeights(rows, scale, ColumnCount(rows)).Max();

    /// <summary>Keep all choices visible, adding columns instead of shrinking decision text.</summary>
    public static int ColumnCount(ImmutableArray<VoteRow> rows)
    {
        for (var columns = 1; columns < 3; columns++)
            if (Chrome + ColumnHeights(rows, 1, columns).Max() <= MaxHeight) return columns;
        return 3;
    }

    public static float PanelWidth(ImmutableArray<VoteRow> rows) => Width * ColumnCount(rows) + 24 * (ColumnCount(rows) - 1);

    private static IEnumerable<float> ColumnHeights(ImmutableArray<VoteRow> rows, float scale, int columns)
    {
        var count = Math.Max(1, (int)Math.Ceiling((double)rows.Length / columns));
        return Enumerable.Range(0, columns).Select(c => rows.Skip(c * count).Take(count).Sum(r => RowHeight(r, scale)));
    }
}
