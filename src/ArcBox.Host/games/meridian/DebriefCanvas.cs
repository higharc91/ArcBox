using ArcBox.Host.Shell;
using Godot;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Board;
using Meridian.Presentation.Debrief;

namespace ArcBox.Host.Games.Meridian;

/// <summary>
/// The Debrief's text column (spec §3.1; M4 plan, Task 8): the outcome, every seat's role with the winners first, and the
/// awards. The board draws all three decks beside it.
/// </summary>
public partial class DebriefCanvas : Node2D
{
    private Director? _director;

    public void Use(Director director, ArtTextures textures) => (_director, _textures) = (director, textures);

    private ArtTextures? _textures;

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    private object? _drawn;

    public override void _Process(double delta)
    {
        if (!ReferenceEquals(_director?.Shown, _drawn))
        {
            _drawn = _director?.Shown;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_director?.Shown is not { } view || DebriefModel.Of(view) is not { } debrief)
        {
            return;
        }
        var size = GetViewportRect().Size;
        var (text, _, _, _) = DebriefModel.Layout(new TvSize((int)size.X, (int)size.Y));
        var x = (float)text.X + 40;
        var w = (float)text.W - 70;
        var y = (float)text.Y + 60;
        DrawRect(new Rect2((float)text.X, (float)text.Y, (float)text.W, (float)text.H), new Color(0.03f, 0.03f, 0.05f, 0.92f));
        DrawString(ArcTheme.Bold, new Vector2(x, y), debrief.Title.ToUpperInvariant(), HorizontalAlignment.Left, w, 26, new Color("c08cff"));
        y += 16;
        DrawMultilineString(ArcTheme.Bold, new Vector2(x, y + 38), debrief.Outcome, HorizontalAlignment.Left, w, 38, 3, Colors.White);
        y += 38 * 3 + 30;
        foreach (var row in debrief.Rows)
        {
            var colour = ArcTheme.PlayerColour(row.Colour);
            DrawCircle(new Vector2(x + 18, y - 9), 18, colour);
            var crewName = view.Seats.FirstOrDefault(s => s.Id == row.Seat)?.Crew;
            if (_textures?.Portrait(crewName) is { } portrait)
            {
                DrawTextureRect(portrait, new Rect2(x + 2, y - 25, 32, 32), false);
            }
            else
            {
                DrawString(ArcTheme.Black, new Vector2(x, y), row.Mark, HorizontalAlignment.Center, 36, row.Mark.Length > 1 ? 16 : 22, new Color("101018"));
            }
            DrawString(ArcTheme.SemiBold, new Vector2(x + 50, y), $"{row.Name} was {row.Role}", HorizontalAlignment.Left, w - 150, 28, Colors.White);
            if (row.Won)
            {
                DrawString(ArcTheme.Bold, new Vector2(x + w - 90, y), "WON", HorizontalAlignment.Right, 90, 26, new Color("5fe08a"));
            }
            y += 50;
        }
        if (debrief.Awards.Count > 0)
        {
            y += 20;
            DrawString(ArcTheme.Bold, new Vector2(x, y), "AWARDS", HorizontalAlignment.Left, w, 24, ArcTheme.Dim);
            y += 40;
            foreach (var award in debrief.Awards)
            {
                DrawMultilineString(ArcTheme.SemiBold, new Vector2(x, y), $"{award.Title}: {award.Name}", HorizontalAlignment.Left, w, 24, 2,
                                    ArcTheme.PlayerColour(award.Colour));
                y += 64;
            }
        }
    }
}
