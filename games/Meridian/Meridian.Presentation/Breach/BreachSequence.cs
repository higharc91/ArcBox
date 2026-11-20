using Meridian.Presentation.Beats;

namespace Meridian.Presentation.Breach;

/// <summary>The Breach sequence's screens, in order (M4 plan, Task 8).</summary>
public enum BreachStage
{
    /// <summary>The lights fail to emergency red.</summary>
    Lights,

    /// <summary>"BREACH", where it fired, and Tiller's line.</summary>
    Breach,

    /// <summary>The scenario's title and its public announcement, read in full.</summary>
    Announcement,

    /// <summary>"Read your phone", with how many are ready, until the endgame begins.</summary>
    Phones,
}

/// <summary>
/// One frame of the Breach sequence: what the full-screen overlay shows. <see cref="Red"/> is how far the lights have
/// failed (0 to 1). <see cref="Compartment"/> is where the Breach fired, for the scene to name from the content pack.
/// </summary>
public sealed record BreachFrame(BreachStage Stage, string Title, string Text, string? Compartment, int Ready, int Seats, double Red);

/// <summary>
/// The Breach sequence (spec §3.1; M4 plan, Task 8): about twelve seconds from the lights failing to "Read your phone",
/// which holds until the endgame begins. It is driven by the same beats as everything else, so the <c>breach</c> and
/// <c>scenario</c> presentations are signalled when their steps end, and a TV that joins mid-sequence shows where it is.
/// </summary>
public static class BreachSequence
{
    /// <summary>The frame for this moment, or null when the Breach sequence isn't on screen.</summary>
    public static BreachFrame? Of(MeridianHostView view, Beat? beat, (BeatStep Step, double Progress)? at)
    {
        var seats = view.Seats.Count;
        var ready = view.Ready ?? 0;
        if (beat is { Kind: "breach" } b)
        {
            var lights = at is { Step.Name: "lights" } p ? p.Progress : 1;
            var stage = at is { Step.Name: "lights" } ? BreachStage.Lights : BreachStage.Breach;
            return new BreachFrame(stage, "BREACH", b.Now.Text, b.Now.Compartment, ready, seats, lights);
        }
        if (beat is { Kind: "scenario" } s)
        {
            return new BreachFrame(BreachStage.Announcement, s.Now.Title, s.Now.Text, null, ready, seats, 1);
        }
        if (view.Phase == "briefing")
        {
            var title = view.Scenario?.Title ?? "";
            return new BreachFrame(BreachStage.Phones, title, "Read your phone", null, ready, seats, 1);
        }
        return null;
    }
}
