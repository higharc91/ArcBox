using System.Globalization;

namespace ArcBox.Host.Shell;

/// <summary>
/// App arguments after <c>--</c> on the Godot command line (all optional):
/// <c>--server=&lt;url&gt;</c>, <c>--new-room</c>, <c>--shots=&lt;dir&gt;</c>, <c>--quit-after=&lt;seconds&gt;</c>,
/// <c>--preset=ultra|high|low</c>, <c>--res=&lt;w&gt;x&lt;h&gt;</c>.
/// </summary>
public sealed record AppArgs
{
    public string? Server { get; init; }
    public bool NewRoom { get; init; }
    public string? ShotsDir { get; init; }
    public double? QuitAfterSeconds { get; init; }
    public string? Preset { get; init; }
    public (int Width, int Height)? Resolution { get; init; }

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
                case "--preset" when value is "ultra" or "high" or "low":
                    args = args with { Preset = value };
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
