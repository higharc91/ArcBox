using System.Globalization;
using ArcBox.Host.Core;

namespace ArcBox.Host.Shell;

/// <summary>
/// App arguments after <c>--</c> on the Godot command line (all optional):
/// <c>--server=&lt;url&gt;</c>, <c>--new-room</c>, <c>--shots=&lt;dir&gt;</c>, <c>--quit-after=&lt;seconds&gt;</c>,
/// <c>--preset=ultra|high|low</c>, <c>--res=&lt;w&gt;x&lt;h&gt;</c>, and the dev-only <c>--redline-demo[=step]</c>
/// (play a scripted REDLINE race into the TV without a server, from a step such as <c>qualifying</c> or <c>reveal</c>;
/// <c>--redline-demo-winner=PER</c> crowns another player's
/// car, e.g. one whose country plays the theme only) and <c>--autostart=&lt;gameId&gt;[:&lt;players&gt;]</c>
/// (select that game and start it once enough players are in; for scripted runs), and <c>--perf</c> (F3 overlay on
/// from the start, logged every 5 s), and <c>--audio-log</c> (print <c>AUDIO &lt;bus&gt; &lt;name&gt;</c> for every sound played),
/// and the dev-only <c>--demo=campaign-lobby</c> (show the lobby with a built-in campaign and roster, without a server).
/// </summary>
public sealed record AppArgs
{
    /// <summary><c>--demo=campaign-lobby</c>: the lobby with <see cref="Shell.CampaignLobbyDemo"/>'s sample state.</summary>
    public const string DemoCampaignLobby = "campaign-lobby";

    public string? Server { get; init; }
    public bool NewRoom { get; init; }
    public string? ShotsDir { get; init; }
    public double? QuitAfterSeconds { get; init; }
    public string? Preset { get; init; }
    public (int Width, int Height)? Resolution { get; init; }

    /// <summary>Dev only: run the scripted REDLINE race instead of connecting.</summary>
    public bool RedlineDemo { get; init; }

    /// <summary>Dev only: the demo step to start from (e.g. "reveal"), or null for the whole script.</summary>
    public string? RedlineDemoFrom { get; init; }

    /// <summary>Dev only: the demo race winner's driver code (a player's car), or null for the script's own.</summary>
    public string? RedlineDemoWinner { get; init; }

    /// <summary>Dev only: a built-in screen to show instead of connecting (<see cref="DemoCampaignLobby"/>), or null.</summary>
    public string? Demo { get; init; }

    /// <summary><c>--perf</c>: start with the F3 performance overlay on, and log it every 5 s.</summary>
    public bool Perf { get; init; }

    /// <summary><c>--audio-log</c>: print <c>AUDIO &lt;bus&gt; &lt;name&gt;</c> for every sound played (no sound card needed).</summary>
    public bool AudioLog { get; init; }

    /// <summary>Dev only: select and start this game by itself once enough players have joined.</summary>
    public AutoStartSpec? AutoStart { get; init; }

    /// <summary>Arguments that were not understood (reported once at start-up, never fatal).</summary>
    public IReadOnlyList<string> Unknown { get; init; } = [];

    /// <summary>Parses <c>OS.GetCmdlineUserArgs()</c>.</summary>
    public static AppArgs Parse(string[] userArgs)
    {
        var args = new AppArgs();
        var unknown = new List<string>();
        foreach (var raw in userArgs)
        {
            var (key, value) = Split(raw);
            switch (key)
            {
                case "--server" when !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out _):
                    args = args with { Server = value.TrimEnd('/') };
                    break;
                case "--new-room" when value is null:
                    args = args with { NewRoom = true };
                    break;
                case "--shots" when !string.IsNullOrWhiteSpace(value):
                    args = args with { ShotsDir = value };
                    break;
                case "--quit-after" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0:
                    args = args with { QuitAfterSeconds = s };
                    break;
                case "--preset" when RenderPresets.Normalise(value) is { } preset:
                    args = args with { Preset = preset };
                    break;
                case "--redline-demo":
                    args = args with { RedlineDemo = true, RedlineDemoFrom = string.IsNullOrWhiteSpace(value) ? null : value };
                    break;
                case "--redline-demo-winner" when !string.IsNullOrWhiteSpace(value):
                    args = args with { RedlineDemoWinner = value.Trim().ToUpperInvariant() };
                    break;
                case "--demo" when value == DemoCampaignLobby:
                    args = args with { Demo = value };
                    break;
                case "--perf" when value is null:
                    args = args with { Perf = true };
                    break;
                case "--audio-log" when value is null:
                    args = args with { AudioLog = true };
                    break;
                case "--autostart" when AutoStartSpec.TryParse(value, out var auto):
                    args = args with { AutoStart = auto };
                    break;
                case "--res" when TryParseResolution(value, out var res):
                    args = args with { Resolution = res };
                    break;
                default:
                    unknown.Add(raw);
                    break;
            }
        }
        return args with { Unknown = unknown };
    }

    private static (string Key, string? Value) Split(string raw)
    {
        var i = raw.IndexOf('=');
        return i < 0 ? (raw, null) : (raw[..i], raw[(i + 1)..]);
    }

    private static bool TryParseResolution(string? value, out (int, int) res)
    {
        res = default;
        var parts = value?.Split('x', 'X');
        if (parts is not { Length: 2 }
            || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var w)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var h)
            || w < 320 || h < 240)
        {
            return false;
        }
        res = (w, h);
        return true;
    }
}
