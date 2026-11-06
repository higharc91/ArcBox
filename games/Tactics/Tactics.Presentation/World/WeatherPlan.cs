namespace Tactics.Presentation.World;

/// <summary>A frame of the pack's rain sheet, tiled in screen space below the HUD.</summary>
public sealed record WeatherSheet(string Path, int Frame, int Width, int Height);

/// <summary>The visible rain variants follow the same authored weather as the ambience. No random weather changes.</summary>
public static class WeatherPlan
{
    public static WeatherSheet? Of(string? weather, AmbientManifest? manifest, double seconds)
    {
        if (weather is not ("rain" or "storm" or "drizzle") || manifest is null
            || !manifest.Assets.TryGetValue("rain", out var rain) || !rain.Variants.TryGetValue(weather, out var path)
            || rain.Frames <= 0 || rain.FrameWidth <= 0 || rain.FrameHeight <= 0)
            return null;
        var frame = (int)(Math.Max(0, seconds) * rain.Fps % rain.Frames);
        return new(path, frame, rain.FrameWidth, rain.FrameHeight);
    }
}
