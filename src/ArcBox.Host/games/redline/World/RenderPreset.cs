using Godot;
using Environment = Godot.Environment;

namespace ArcBox.Host.Games.Redline.World;

public enum Preset { Ultra, High, Low }

/// <summary>
/// The render presets (spec §4.8, plan Global Constraints). Ultra: SDFGI with half-resolution probes, SSR, SSAO + SSIL,
/// light volumetric fog, a 4-split 8192 shadow atlas, MSAA 4× + SMAA, 144 fps cap with VSync off. High: Ultra without
/// SDFGI and SSR. Low: no screen-space effects or volumetrics, 2048 shadows, 2 splits, FXAA only, half the trees, no skid
/// marks or marbles.
/// Low keeps Forward+ (switching to the Mobile renderer needs a restart). Everything here can change at runtime.
/// </summary>
public static class RenderPreset
{
    public static Preset Parse(string? name) => name switch
    {
        "high" => Preset.High,
        "low" => Preset.Low,
        _ => Preset.Ultra,
    };

    /// <summary>Fraction of the tree instances drawn.</summary>
    public static float TreeFraction(Preset p) => p == Preset.Low ? 0.5f : 1f;

    /// <summary>Screen-space subsurface scattering on the driver figures' skin: off on Low, like the other screen-space effects.</summary>
    public static bool SkinSubsurface(Preset p) => p != Preset.Low;

    /// <summary>Skid marks, marbles and run-off tyre marks on the track surface: off on Low.</summary>
    public static bool TrackDetail(Preset p) => p != Preset.Low;

    public static void Apply(Environment env, DirectionalLight3D sun, Viewport vp, Preset p)
    {
        var ultra = p == Preset.Ultra;
        var low = p == Preset.Low;

        Engine.MaxFps = 144;
        TrackMaterials.SetDetail(TrackDetail(p));
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

        env.SdfgiEnabled = ultra;
        if (ultra)
        {
            RenderingServer.GISetUseHalfResolution(true);
            env.SdfgiUseOcclusion = true;
            env.SdfgiReadSkyLight = true;
            env.SdfgiCascades = 6;
            env.SdfgiMinCellSize = 0.4f;
            env.SdfgiEnergy = 0.9f;
            env.SdfgiBounceFeedback = 0.35f;
        }

        env.SsrEnabled = ultra;
        env.SsrMaxSteps = 48;
        env.SsrFadeIn = 0.2f;
        env.SsrFadeOut = 2f;
        env.SsrDepthTolerance = 0.3f;

        env.SsaoEnabled = !low;
        env.SsaoRadius = 1.2f;
        env.SsaoIntensity = 1.6f;
        env.SsaoPower = 1.4f;
        env.SsaoDetail = 0.6f;
        env.SsilEnabled = !low;
        env.SsilRadius = 4f;
        env.SsilIntensity = 0.6f;

        env.VolumetricFogEnabled = !low;
        env.VolumetricFogDensity = 0.0007f;
        env.VolumetricFogLength = 200;
        env.VolumetricFogAnisotropy = 0.3f;
        env.VolumetricFogGIInject = 0.3f;
        env.VolumetricFogAlbedo = new Color("d2dce6");
        env.VolumetricFogSkyAffect = 0;

        sun.ShadowEnabled = true;
        sun.DirectionalShadowMode = low ? DirectionalLight3D.ShadowMode.Parallel2Splits : DirectionalLight3D.ShadowMode.Parallel4Splits;
        RenderingServer.DirectionalShadowAtlasSetSize(low ? 2048 : 8192, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(low
            ? RenderingServer.ShadowQuality.SoftLow
            : RenderingServer.ShadowQuality.SoftHigh);

        vp.Msaa3D = low ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa4X;
        vp.ScreenSpaceAA = low ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Smaa;
        vp.UseDebanding = !low;
        vp.AnisotropicFilteringLevel = low ? Viewport.AnisotropicFiltering.Anisotropy4X : Viewport.AnisotropicFiltering.Anisotropy16X;
    }
}
