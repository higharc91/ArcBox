using Redline.Logic;

namespace Redline.Presentation;

/// <summary>A TV camera shot. The Godot layer maps it onto a camera rig and a cut.</summary>
public abstract record Shot;

/// <summary>Driver Select: orbiting line-up camera. <paramref name="Subject"/> is the newly claimed driver code, if any.</summary>
public sealed record LineupShot(string? Subject) : Shot;

public sealed record GridShot : Shot;

/// <summary>Helicopter shot framing one or two driver codes.</summary>
public sealed record HeliShot(IReadOnlyList<string> Subjects) : Shot;

public sealed record TracksideShot(string Subject) : Shot;

public sealed record FinishShot : Shot;

/// <summary>
/// Anthem and Champagne: in front of the podium (podium spec §5), pushing in during the anthem and wider for the
/// <paramref name="Champagne"/>. Geometry: <see cref="Ceremony.PodiumFraming"/>.
/// </summary>
public sealed record PodiumShot(bool Champagne = false) : Shot;

/// <summary>
/// §4.9: picks the TV camera shot from the host view. Players first: a candidate must involve a human,
/// and AI-only candidates are used only when no human candidate exists.
/// </summary>
public static class CameraDirector
{
    public static Shot Pick(RedlineHostView view, RedlineHostView? previous) => view.Phase switch
    {
        RacePhase.DriverSelect => new LineupShot(NewlyClaimed(view, previous)),
        RacePhase.Grid => new GridShot(),
        RacePhase.Throttle => new HeliShot([(BestPlaced(view.Drivers, humanOnly: true) ?? BestPlaced(view.Drivers, humanOnly: false))!.Code]),
        RacePhase.Reading or RacePhase.Question => ClosestBattle(view),     // Reading keeps the Question shot (audio spec §2)
        RacePhase.Reveal => RevealShot(view),
        RacePhase.Anthem => new PodiumShot(),
        RacePhase.Champagne => new PodiumShot(Champagne: true),
        _ => new FinishShot(),
    };

    /// <summary>Heli on the overall leader, cut to 3.5 s into Reveal.</summary>
    public static Shot RevealFollowUp(RedlineHostView view) =>
        new HeliShot([BestPlaced(view.Drivers, humanOnly: false)!.Code]);

    /// <summary>The first seat (view order) claimed in <paramref name="view"/> but not in <paramref name="previous"/>.</summary>
    public static string? NewlyClaimed(RedlineHostView view, RedlineHostView? previous)
    {
        var already = previous is null
            ? []
            : previous.Drivers.Where(d => d.Player is not null).Select(d => d.Code).ToHashSet();
        return view.Drivers.FirstOrDefault(d => d.Player is not null && !already.Contains(d.Code))?.Code;
    }

    /// <summary>Smallest Position wins; among equal positions the earlier entry in the ranking order.</summary>
    private static DriverRow? BestPlaced(IReadOnlyList<DriverRow> drivers, bool humanOnly) =>
        drivers.Where(d => !humanOnly || d.Player is not null).MinBy(d => d.Position);

    private static HeliShot ClosestBattle(RedlineHostView view)
    {
        var drivers = view.Drivers;
        // Strict "<" keeps the earlier (higher-placed) pair on a tie.
        (int Index, int Gap)? best = null;
        (int Index, int Gap)? bestAny = null;
        for (var i = 0; i + 1 < drivers.Count; i++)
        {
            var gap = drivers[i].Distance - drivers[i + 1].Distance;
            if (bestAny is null || gap < bestAny.Value.Gap) bestAny = (i, gap);
            var involvesHuman = drivers[i].Player is not null || drivers[i + 1].Player is not null;
            if (involvesHuman && (best is null || gap < best.Value.Gap)) best = (i, gap);
        }

        var pick = best ?? bestAny;
        return pick is { } p
            ? new HeliShot([drivers[p.Index].Code, drivers[p.Index + 1].Code])
            : new HeliShot([.. drivers.Take(1).Select(d => d.Code)]);
    }

    private static Shot RevealShot(RedlineHostView view)
    {
        foreach (var humanOnly in new[] { true, false })
        {
            var candidates = view.Drivers.Where(d => !humanOnly || d.Player is not null).ToList();
            var spun = candidates.Where(d => d.Outcome == RoundOutcome.SpunOut).MinBy(d => d.Position);
            if (spun is not null) return new TracksideShot(spun.Code);
            var gainer = candidates.MinBy(d => (-(d.Distance - d.PreviousDistance), d.Position));
            if (gainer is not null) return new TracksideShot(gainer.Code);
        }

        return new FinishShot();
    }
}
