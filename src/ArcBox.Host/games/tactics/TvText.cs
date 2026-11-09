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
}
