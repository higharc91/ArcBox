using Tactics.Game;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>Absolute night/sunrise colours divided by the cached region light, so a dusk region actually becomes morning.</summary>
public static class CampLight
{
    public static readonly Rgb Night = new(.13f, .16f, .29f);
    public static readonly Rgb Morning = new(.90f, .88f, .79f);
    public static Rgb Grade(CampView? camp, double seconds, Rgb ambient, bool morning)
    {
        Rgb target;
        if (camp is { Stage: "dawn" })
        {
            var p = (float)Math.Clamp(seconds / 4.5, 0, 1);
            var sunrise = new Rgb(.76f, .49f, .35f);
            target = p < .5f ? Blend(Night, sunrise, p * 2) : Blend(sunrise, Morning, (p - .5f) * 2);
        }
        else if (camp is { Stage: not "vote" }) target = Night;
        else if (morning) target = Morning;
        else return new Rgb(1, 1, 1);
        return new Rgb(target.R / Math.Max(.35f, ambient.R), target.G / Math.Max(.35f, ambient.G), target.B / Math.Max(.35f, ambient.B));
    }
    private static Rgb Blend(Rgb a, Rgb b, float p) => new(a.R + (b.R - a.R) * p, a.G + (b.G - a.G) * p, a.B + (b.B - a.B) * p);
}
