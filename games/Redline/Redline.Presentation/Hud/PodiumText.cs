using Redline.Logic;

namespace Redline.Presentation.Hud;

/// <summary>The podium lower-third: a big title and the line under it.</summary>
public sealed record PodiumCaption(string Kicker, string Title, string Subtitle);

/// <summary>One podium place on the HUD: the label over the step and the bottle's pressure gauge.</summary>
/// <param name="Name">The player's name, upper-cased.</param>
/// <param name="Accent">The team colour, <c>#rrggbb</c>.</param>
/// <param name="Charge">The bottle's pressure, 0..1.</param>
/// <param name="Sprays">Sprays so far.</param>
public sealed record PodiumLabel(int Step, string Name, string Code, int Number, string Accent, double Charge, int Sprays);

/// <summary>Podium spec §5 HUD wording, derived from the view and kept out of Godot so it can be tested.</summary>
public static class PodiumText
{
    /// <summary>
    /// Anthem: "NATIONAL ANTHEM · GREAT BRITAIN" over "for JO · Lando Norris, McLaren" (the country is named even when only
    /// the REDLINE theme plays). Champagne: "CHAMPAGNE!". Null outside the ceremony.
    /// </summary>
    public static PodiumCaption? Caption(RedlineHostView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Podium is not { Steps: [var top, ..] } podium) return null;
        var seat = HudText.Seat(top.Code);
        var driver = seat?.Driver.Name ?? top.Code;
        var team = seat?.Team.Name ?? top.TeamId;
        var name = top.Name is { Length: > 0 } n ? n.ToUpperInvariant() : top.Code;
        return view.Phase switch
        {
            RacePhase.Anthem => new PodiumCaption("PODIUM CEREMONY", $"NATIONAL ANTHEM · {podium.AnthemCountry.ToUpperInvariant()}",
                $"for {name} · {driver}, {team}"),
            RacePhase.Champagne => new PodiumCaption("PODIUM CEREMONY", "CHAMPAGNE!", "Shake the bottle · pop the cork · spray!"),
            _ => null,
        };
    }

    /// <summary>The podium places in step order (1, 2, 3).</summary>
    public static IReadOnlyList<PodiumLabel> Labels(RedlineHostView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Podium is not { } podium) return [];
        return [.. podium.Steps.OrderBy(s => s.Step).Select(s => new PodiumLabel(
            s.Step,
            s.Name is { Length: > 0 } name ? name.ToUpperInvariant() : s.Code,
            s.Code,
            HudText.Seat(s.Code)?.Driver.Number ?? 0,
            TeamAccent.ById(s.TeamId),
            Math.Clamp(double.IsFinite(s.Charge) ? s.Charge : 0, 0, 1),
            s.Sprays.Count))];
    }

    /// <summary>"P1", "P2", "P3" for the step: the podium place, not the car's race position.</summary>
    public static string Place(int step) => $"P{step}";

    /// <summary>The gauge's colour band: green while building, amber, red when the bottle is about to blow.</summary>
    public static string GaugeColour(double charge) => charge switch
    {
        >= 0.8 => "#ff3b3b",
        >= 0.45 => "#ffb000",
        _ => "#1ed760",
    };
}

/// <summary>Lays the podium labels out side by side over their steps without overlapping.</summary>
public static class PodiumLabelLayout
{
    /// <summary>
    /// Label centres for anchors at <paramref name="xs"/> (any order): as close to each anchor as possible, at least
    /// <paramref name="width"/> + <paramref name="gap"/> apart, and inside <paramref name="min"/>..<paramref name="max"/>
    /// when the row fits. The result is in the input order.
    /// </summary>
    public static IReadOnlyList<double> Spread(IReadOnlyList<double> xs, double width, double gap, double min, double max)
    {
        ArgumentNullException.ThrowIfNull(xs);
        var n = xs.Count;
        if (n == 0) return [];
        var order = Enumerable.Range(0, n).OrderBy(i => xs[i]).ToArray();
        var pitch = width + gap;
        // Overlapping neighbours merge into clusters centred on their anchors' mean (pool-adjacent-violators).
        var clusters = new List<(double Sum, int Count, int First)>();
        for (var k = 0; k < n; k++)
        {
            clusters.Add((xs[order[k]] - (k * pitch), 1, k));
            while (clusters.Count > 1 && clusters[^1].Sum / clusters[^1].Count < clusters[^2].Sum / clusters[^2].Count)
            {
                var (s, c, _) = clusters[^1];
                clusters.RemoveAt(clusters.Count - 1);
                clusters[^1] = (clusters[^1].Sum + s, clusters[^1].Count + c, clusters[^1].First);
            }
        }
        var placed = new double[n];
        foreach (var (sum, count, first) in clusters)
        {
            for (var k = first; k < first + count; k++) placed[k] = (sum / count) + (k * pitch);
        }
        // Slide the row back inside the bounds (centred when it cannot fit).
        var lo = placed[0] - (width / 2);
        var hi = placed[^1] + (width / 2);
        var shift = hi - lo > max - min ? ((min + max) / 2) - ((lo + hi) / 2) : lo < min ? min - lo : hi > max ? max - hi : 0;
        var result = new double[n];
        for (var k = 0; k < n; k++) result[order[k]] = placed[k] + shift;
        return result;
    }
}
