using System.Collections.Concurrent;
using System.Diagnostics;
using Godot;
using Redline.Presentation.Audio;
using Redline.Presentation.Audio.Synth;

namespace ArcBox.Host.Games.Redline.Audio;

/// <summary>
/// The podium's music, synthesised on background threads ahead of the ceremony (each takes most of a second of CPU):
/// <see cref="Prepare"/> is called when the Chequered flag falls, with the anthem expected from the classification, and
/// again when the Anthem view names the real one. The victory theme is rendered once per run; anthems once per country.
/// </summary>
public static class PodiumMusicBank
{
    /// <summary>The anthem's opening phrase is cut to fit this (it fades on a phrase end).</summary>
    public const double AnthemMaxSeconds = 16;

    private static readonly Lock Gate = new();
    private static readonly ConcurrentDictionary<string, Task<Pcm>> Anthems = new(StringComparer.OrdinalIgnoreCase);
    private static Task<Pcm>? _theme;

    /// <summary>Starts rendering the theme and, when it has a melody, <paramref name="anthemId"/>'s anthem. Safe to call repeatedly.</summary>
    public static void Prepare(string? anthemId)
    {
        _ = Theme();
        if (PodiumMusic.Anthem(anthemId) is { } info)
        {
            _ = Anthem(info);
        }
    }

    public static Task<Pcm> Theme()
    {
        lock (Gate)
        {
            return _theme ??= Task.Run(() => Timed("victory theme", VictoryTheme.Render));
        }
    }

    public static Task<Pcm> Anthem(AnthemInfo info) =>
        Anthems.GetOrAdd(info.Id, _ => Task.Run(() => Timed($"anthem {info.Id}", () => AnthemSynth.Render(info, AnthemMaxSeconds))));

    private static Pcm Timed(string what, Func<Pcm> render)
    {
        var sw = Stopwatch.StartNew();
        var pcm = render();
        GD.Print($"REDLINE {what} synthesised in {sw.ElapsedMilliseconds} ms ({pcm.Seconds:0.0} s)");
        return pcm;
    }
}
