using Redline.Logic;
using Redline.Presentation.Audio.Synth;

namespace Redline.Presentation.Audio;

/// <summary>
/// Podium spec §5 music: the anthem of the best player's driver's country when we have its melody, else the REDLINE
/// victory theme; the theme always plays for the champagne. Kept out of Godot so the choice and the timing can be tested.
/// </summary>
public static class PodiumMusic
{
    /// <summary>Gap between the commentator's anthem line and the band.</summary>
    public const double AfterLine = 0.35;

    /// <summary>The anthem may run on into the Champagne by at most this long (the champagne waits for the last chord).</summary>
    public const double MaxOverrun = 1.0;

    /// <summary>The narrator's assumed pace for a line it has not spoken yet (about 2.7 words a second).</summary>
    public const double SecondsPerWord = 0.36;

    /// <summary>The anthem to play for <paramref name="anthemId"/>, or null when the victory theme plays instead.</summary>
    /// <remarks>
    /// Null for a fallback country (no id), an unknown id, and a public-domain anthem whose melody is not in yet
    /// (<see cref="AnthemInfo.HasMelody"/> false: Monaco, Argentina and New Zealand for now).
    /// </remarks>
    public static AnthemInfo? Anthem(string? anthemId) =>
        anthemId is not null && Anthems.Find(anthemId) is { HasMelody: true } anthem ? anthem : null;

    /// <summary>
    /// The anthem we expect before the podium view arrives: from the Chequered classification's best-placed player's
    /// driver nationality (the logic picks the anthem the same way). Null for a theme-only country or no players.
    /// </summary>
    public static string? PredictAnthemId(RedlineHostView view, Grid grid)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(grid);
        var best = view.Drivers.Where(d => d.Player is not null).MinBy(d => d.Position);
        if (best is null) return null;
        var nationality = grid.Seats.FirstOrDefault(s => s.Driver.Code == best.Code)?.Driver.Nationality;
        return nationality is null ? null : Countries.For(nationality).AnthemId;
    }

    /// <summary>Seconds the narrator will take over <paramref name="line"/> (an estimate; the pace it assumes too).</summary>
    public static double LineSeconds(string? line) =>
        string.IsNullOrWhiteSpace(line) ? 0 : (line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * SecondsPerWord) + 0.3;

    /// <summary>
    /// When the music starts, in seconds into the Anthem phase: after the commentator's line, but early enough that an
    /// anthem of <paramref name="musicSeconds"/> ends no later than <see cref="MaxOverrun"/> after the phase. If the two
    /// clash, the band starts under the end of the line (the timpani roll lead-in, ducked under the voice) rather than
    /// cut the anthem short. The looping theme has no end to fit: it simply starts after the line.
    /// </summary>
    public static double Start(double lineSeconds, double musicSeconds, double phaseSeconds, bool loops)
    {
        var afterLine = lineSeconds > 0 ? lineSeconds + AfterLine : 0;
        if (loops) return afterLine;
        var latest = phaseSeconds + MaxOverrun - musicSeconds;
        return Math.Max(0, Math.Min(afterLine, latest));
    }
}
