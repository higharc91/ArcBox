using ArcBox.Host.Shell;
using Godot;
using Tactics.Presentation.Board;
using Tactics.Presentation.Story;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// The question open, at the bottom right (mockup 02): its heading and countdown, a row for each option with its letter, its words, a
/// check's DC and who voted for it, and under them the tally and who's away. It grows upward with its options; <see cref="VoteLayout"/>
/// says how big its rows are, so eleven fit. The clock changes every second and nothing else is rebuilt for it.
/// </summary>
public sealed partial class VoteBoard : Control
{
    private const int LetterBox = 44;
    private const int Pip = 30;

    private readonly StoryArt _art;
    private readonly Control _panel;
    private readonly Label _heading;
    private readonly Label _clock;
    private readonly HBoxContainer _rows;
    private readonly Label _tally;
    private readonly Label _away;
    private VotePanel? _shown;

    public VoteBoard(StoryArt art)
    {
        _art = art;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _heading = StoryArt.Text("", ArcTheme.Spaced("Bold", 4), 24, HudLayer.Gold);
        _clock = StoryArt.Text("", ArcTheme.Display("Black", 2, 6, 6), 44, HudLayer.Ink);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        _clock.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rows = ArcTheme.HBox(24);
        _tally = StoryArt.Text("", ArcTheme.SemiBold, 20, HudLayer.Muted);
        _away = StoryArt.Text("", ArcTheme.SemiBold, 20, HudLayer.Muted);
        var top = ArcTheme.HBox(12, _heading, _clock);
        _away.HorizontalAlignment = HorizontalAlignment.Left;
        var foot = ArcTheme.VBox(0, _tally, _away);
        _panel = ArcTheme.Panel(art.Box("main"), ArcTheme.Margin(8, 6, 8, 6, ArcTheme.VBox(10, top, _rows, foot)));
        AddChild(_panel);
        Visible = false;
    }

    /// <summary>Shows <paramref name="vote"/>, or hides the panel for null.</summary>
    public void Show(VotePanel? vote)
    {
        if (vote == _shown)
        {
            return;
        }
        var rebuild = vote is null || _shown is null || !SameRows(vote, _shown);
        _shown = vote;
        Visible = vote is not null;
        if (vote is null)
        {
            return;
        }
        _heading.Text = vote.Heading.ToUpperInvariant();
        _clock.Text = vote.Clock ?? "";
        _tally.Text = vote.Tally;
        _away.Text = vote.Away ?? "";
        _away.Visible = vote.Away is not null;
        if (rebuild)
        {
            Fill(vote);
        }
        var width = VoteLayout.PanelWidth(vote.Rows);
        _panel.Size = new Vector2(width, _panel.GetCombinedMinimumSize().Y);
        _panel.Position = new Vector2(StoryArt.Layout.X - StoryArt.Margin - width, StoryArt.Layout.Y - StoryArt.Margin - _panel.Size.Y);
    }

    /// <summary>A view that changes only the clock keeps its rows (a <see cref="VoteRow"/> compares by what it shows).</summary>
    private static bool SameRows(VotePanel a, VotePanel b) => a.Scale == b.Scale && a.Rows.SequenceEqual(b.Rows);

    private void Fill(VotePanel vote)
    {
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        var s = vote.Scale;
        _rows.AddThemeConstantOverride("separation", 24);
        var columns = VoteLayout.ColumnCount(vote.Rows);
        var perColumn = (int)Math.Ceiling((double)vote.Rows.Length / columns);
        VBoxContainer? column = null;
        for (var index = 0; index < vote.Rows.Length; index++)
        {
            var row = vote.Rows[index];
            if (index % perColumn == 0)
            {
                column = ArcTheme.VBox(VoteLayout.RowGap > 0 ? (int)VoteLayout.RowGap : 0);
                column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                _rows.AddChild(column);
            }
            var words = ArcTheme.VBox(0);
            words.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            words.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            foreach (var line in row.Label)
            {
                words.AddChild(StoryArt.Text(line, ArcTheme.SemiBold, Px(24, s), HudLayer.Ink));
            }
            if (row.Detail is { } detail)
            {
                words.AddChild(StoryArt.Text(detail, ArcTheme.Bold, Px(20, s), StoryArt.Sky));
            }
            var letter = StoryArt.Text(row.Letter, ArcTheme.Black, Px(24, s), new Color("2a1b04"));
            letter.HorizontalAlignment = HorizontalAlignment.Center;
            letter.VerticalAlignment = VerticalAlignment.Center;
            var box = ArcTheme.Panel(ArcTheme.Flat(HudLayer.Gold), letter);
            box.CustomMinimumSize = new Vector2(LetterBox, LetterBox) * s;
            box.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            if (!row.Voters.IsEmpty)
            {
                var voters = ArcTheme.HBox((int)(4 * s));
                voters.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                foreach (var voter in row.Voters) voters.AddChild(Voter(voter, s));
                words.AddChild(voters);
            }
            var inside = ArcTheme.HBox((int)(14 * s), box, words);
            var card = ArcTheme.Panel(_art.Box("card"), inside);
            card.CustomMinimumSize = new Vector2(0, (VoteLayout.RowHeight(row, s) - VoteLayout.RowGap * s));
            column!.AddChild(card);
        }
    }

    /// <summary>A voter as a pip in their colour, with their initial in the ink that reads on it (<see cref="VoterPip"/>).</summary>
    private static Control Voter(VoterPip voter, float s)
    {
        var initial = StoryArt.Text(voter.Initial, ArcTheme.Black, Px(20, s), Colour(voter.Ink));
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        initial.VerticalAlignment = VerticalAlignment.Center;
        var box = new StyleBoxFlat { BgColor = Colour(voter.Fill) };
        box.SetCornerRadiusAll(Pip);
        var pip = ArcTheme.Panel(box, initial);
        pip.CustomMinimumSize = new Vector2(Pip, Pip) * s;
        return pip;
    }

    private static Color Colour(Rgb c) => new(c.R, c.G, c.B);

    private static int Px(int size, float scale) => Math.Max(TvText.Minimum, (int)MathF.Round(size * scale));
}
