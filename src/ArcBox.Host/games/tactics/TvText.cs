using ArcBox.Host.Shell;
using Godot;

namespace ArcBox.Host.Games.Tactics;

/// <summary>1080p Tactics TV typography: at least 20 px metadata; decision/body text uses 24 px or more.</summary>
internal static class TvText
{
    public const int Minimum = 20;
    public static Label Label(string text, Font font, int size, Color? colour = null)
        => ArcTheme.Label(text, font, Math.Max(Minimum, size), colour);
    public static Label Meta(string text, int size = Minimum, Color? colour = null)
        => ArcTheme.Meta(text, Math.Max(Minimum, size), colour);

    /// <summary>Measure before layout: wrapping at an initial zero width can cache an off-screen minimum height.</summary>
    public static void SetWrappedText(Label label, string text, float width)
    {
        var font = label.GetThemeFont("font");
        var size = label.GetThemeFontSize("font_size");
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (font.GetStringSize(next, fontSize: size).X <= width) { line = next; continue; }
                if (line.Length > 0) { lines.Add(line); line = ""; }
                foreach (var rune in word.EnumerateRunes())
                {
                    next = line + rune;
                    if (line.Length > 0 && font.GetStringSize(next, fontSize: size).X > width)
                    { lines.Add(line); line = rune.ToString(); }
                    else line = next;
                }
            }
            lines.Add(line);
        }
        label.Text = string.Join("\n", lines);
    }
}
