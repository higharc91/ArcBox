using System.Text.Json;

namespace Tactics.Presentation.Audio;

/// <summary>An animation strip that plays once and holds its last frame.</summary>
/// <param name="Path">Its image, relative to the pack's root.</param>
/// <param name="Width">One frame's width.</param>
/// <param name="Height">One frame's height.</param>
/// <param name="Frames">How many frames.</param>
/// <param name="Fps">Frames a second.</param>
public sealed record TitleStrip(string Path, int Width, int Height, int Frames, double Fps)
{
    /// <summary>The frame to show <paramref name="seconds"/> in.</summary>
    public int FrameAt(double seconds) => Math.Min(Frames - 1, (int)(seconds * Fps));
}

/// <summary>The name and moments of the game, from <c>title/manifest.json</c>.</summary>
public sealed class TitleArt
{
    private readonly Dictionary<string, TitleStrip> _strips = new(StringComparer.Ordinal);

    private TitleArt(string logo) => Logo = logo;

    /// <summary>The logo's image, relative to the pack's root.</summary>
    public string Logo { get; }

    /// <summary>Reads the manifest under the pack's root. Throws if it's missing or malformed.</summary>
    public static TitleArt Load(string packRoot)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(packRoot, "title", "manifest.json")));
        var art = new TitleArt($"title/{doc.RootElement.GetProperty("logo").GetString()}");
        foreach (var anim in doc.RootElement.GetProperty("anims").EnumerateObject())
        {
            var (frame, v) = (anim.Value.GetProperty("frame"), anim.Value);
            art._strips[anim.Name] = new TitleStrip($"title/{v.GetProperty("file").GetString()}", frame[0].GetInt32(), frame[1].GetInt32(), v.GetProperty("frames").GetInt32(), v.GetProperty("fps").GetDouble());
        }
        return art;
    }

    /// <summary>The strip with this name in the manifest's <c>anims</c>, or null.</summary>
    public TitleStrip? Strip(string name) => _strips.GetValueOrDefault(name);
}
