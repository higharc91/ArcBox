using Redline.Logic;

namespace Redline.Presentation.Hud;

/// <summary>The five start lights: one more every 0.85 s from 0.4 s into the Grid phase; dark in every other phase.</summary>
public static class StartLights
{
    public const int Count = 5;

    /// <summary>Lights lit <paramref name="phaseSeconds"/> into <paramref name="phase"/>; the gantry and the HUD both use it.</summary>
    public static int Lit(RacePhase phase, double phaseSeconds) =>
        phase == RacePhase.Grid ? Math.Clamp((int)Math.Floor((phaseSeconds - 0.4) / 0.85) + 1, 0, Count) : 0;
}
