using System.Numerics;

namespace Redline.Presentation.Models;

/// <summary>A grandstand export: its file, its length along the track and its number of seat rows.</summary>
public sealed record GrandstandSpec(string File, float Length, int Rows)
{
    public ModelContract Contract { get; } = new(File, LandmarkModels.StandBudget(File), LandmarkModels.StandNodes, LandmarkModels.StandSlots);
}

/// <summary>
/// The naming contracts of the Blender circuit landmarks (<c>tools/blender/landmarks.py</c> writes them into
/// <c>models/landmarks/</c>, the TV's <c>Landmarks</c>, <c>StartGantry</c> and <c>Trees</c> read them). Each model is
/// authored in the local frame of the procedural landmark it replaces, so placement and footprints are unchanged.
/// </summary>
public static class LandmarkModels
{
    public const string Dir = "landmarks/";

    /// <summary>Landmarks in total (spec §1), counting each file once.</summary>
    public const int TotalBudget = 600_000;

    public static readonly IReadOnlyList<string> StandNodes = ["grandstand", GrandstandModel.TierFirst, GrandstandModel.TierLast];

    /// <summary><c>roof</c>, <c>trim</c> and <c>brand</c> take the stand's colour; <c>seat</c> is the bucket seats' plastic.</summary>
    public static readonly IReadOnlyList<string> StandSlots =
        ["concrete", "concrete_dark", "seat", "safety", "glass_clear", "metal", "steel", "roof", "trim", "brand"];

    public static readonly GrandstandSpec MainStand = new(Dir + "grandstand_main.glb", 300, 22);
    public static readonly GrandstandSpec TurnOneStand = new(Dir + "grandstand_t1.glb", 130, 16);
    public static readonly GrandstandSpec CornerStand = new(Dir + "grandstand_corner.glb", 90, 10);

    public static int StandBudget(string file) => file switch
    {
        Dir + "grandstand_main.glb" => 90_000,
        Dir + "grandstand_t1.glb" => 40_000,
        _ => 25_000,
    };

    public static readonly ModelContract PitBuilding = new(Dir + "pit_building.glb", 80_000, ["pit_building"],
        ["concrete", "shutter", "glass", "steel", "red", "panel", "led_white", "epoxy", "wall_white"]);

    public static readonly ModelContract PitStation = new(Dir + "pit_station.glb", 8_000, ["pit_station"],
        ["steel_dark", "panel", "monitor", "seat", "steel", "red", "led_white", "glass_clear"]);

    public static readonly ModelContract StartGantry = new(Dir + "start_gantry.glb", 30_000, ["gantry", .. StartGantryModel.Lights],
        ["steel_dark", "panel", "red", "board", "lamp", "led_white", "concrete"]);

    public static readonly ModelContract Tower = new(Dir + "tower.glb", 40_000, ["tower"], ["tower_white", "glass", "red", "steel", "concrete"]);

    public static readonly ModelContract Trees = new(Dir + "trees.glb", 6_000, TreeModel.Variants, [TreeModel.Bark, TreeModel.Leaves]);

    public static IReadOnlyList<ModelContract> All { get; } =
        [MainStand.Contract, TurnOneStand.Contract, CornerStand.Contract, PitBuilding, PitStation, StartGantry, Tower, Trees];
}

/// <summary>
/// The pit straight's layout across the track (metres left of the centre line on the pit side) and along the pit
/// building: the concrete pit wall under the catch fence, the pit-wall stations on its pit-lane side (one
/// <see cref="LandmarkModels.PitStation"/> each, placed at its own point on the track so they follow its rise), and
/// the pit lane's painted lines. Along-building offsets are in the pit building's frame (+X against the direction of
/// travel, origin midway along it).
/// </summary>
public static class PitLaneModel
{
    public const float WallInner = 19.0f;
    public const float WallOuter = 19.9f;
    public const float WallHeight = 1.1f;

    public const float StationLength = 16f;
    public const float StationDepth = 2.3f;
    public const float StationSpacing = 26f;
    public const int StationCount = 11;

    public const float LaneInner = 20f;
    public const float LaneOuter = 31f;
    public const float BuildingLength = 330f;
    public const float GaragePitch = 12f;

    /// <summary>The fast lane's inner line (just clear of the stations) and the fast/working lane divider.</summary>
    public static readonly IReadOnlyList<(float From, float To)> LaneLines = [(22.3f, 22.45f), (25.9f, 26.05f)];

    /// <summary>Station centres along the building, one per team.</summary>
    public static IReadOnlyList<float> StationOffsets { get; } =
        [.. Enumerable.Range(0, StationCount).Select(k => (-(StationCount - 1) / 2f * StationSpacing) + (k * StationSpacing))];

    /// <summary>The garage dividers along the building (where the working lane's box lines are painted).</summary>
    public static IReadOnlyList<float> GarageDividers { get; } =
        [.. Enumerable.Range(0, 28).Select(g => (-BuildingLength / 2) + (GaragePitch * g))];
}

/// <summary>
/// The tier geometry every grandstand export keeps and the instanced crowd sits on. Local frame: origin on the track
/// surface at the stand's front edge, +X along the track, +Y up, +Z away from the track. Row r's tread is the strip
/// <see cref="RowDepth"/>·r ≤ z ≤ <see cref="RowDepth"/>·(r+1) at y = <see cref="TreadTop"/>(r); a seat (and a crowd
/// place) every <see cref="SeatPitch"/> m along it (<see cref="SeatXs"/>), except in the aisles (<see cref="InAisle"/>),
/// which carry half-height steps. Empties <see cref="TierFirst"/> and <see cref="TierLast"/> mark the front-middle of the
/// first and the last tread.
/// </summary>
public static class GrandstandModel
{
    public const float RowRise = 0.9f;
    public const float RowDepth = 1.2f;
    public const float FirstTreadTop = 1f;
    public const float SeatPitch = 0.85f;
    public const float SeatInset = 0.6f;
    public const float SeatEndClearance = 0.4f;
    public const float AisleSpacing = 25f;
    public const float AisleOffset = 12.5f;
    public const float AisleHalfWidth = 0.75f;
    public const float AisleEndMargin = 5f;

    /// <summary>The roof's underside sits this far above the top row's tread line (at the back).</summary>
    public const float RoofClearance = 4.5f;

    public const string TierFirst = "tier_first";
    public const string TierLast = "tier_last";

    public static float TreadTop(int row) => FirstTreadTop + (RowRise * row);

    /// <summary>The front-middle of row <paramref name="row"/>'s tread (where <see cref="TierFirst"/>/<see cref="TierLast"/> sit).</summary>
    public static Vector3 TreadFront(int row) => new(0, TreadTop(row), RowDepth * row);

    /// <summary>Seat (crowd place) centres along a stand of <paramref name="length"/> metres, aisles included.</summary>
    public static IReadOnlyList<float> SeatXs(float length)
    {
        var xs = new List<float>();
        for (var k = 0; ; k++)
        {
            var x = (-length / 2) + SeatInset + (SeatPitch * k);
            if (x >= (length / 2) - SeatEndClearance)
                return xs;
            xs.Add(x);
        }
    }

    /// <summary>Aisle centres: every <see cref="AisleSpacing"/> m from <see cref="AisleOffset"/> m in, clear of the far end.</summary>
    public static IReadOnlyList<float> Aisles(float length)
    {
        var a = new List<float>();
        for (var k = 0; ; k++)
        {
            var x = (-length / 2) + AisleOffset + (AisleSpacing * k);
            if (x > (length / 2) - AisleEndMargin)
                return a;
            a.Add(x);
        }
    }

    public static bool InAisle(float x, float length) => Aisles(length).Any(a => MathF.Abs(x - a) < AisleHalfWidth);
}

/// <summary>
/// The start gantry export (local frame: X across the track to the left of travel, +Y up, +Z along travel). The five
/// light pods are the nodes <see cref="Lights"/> (each pod's lenses, one mesh, origin at the pod) at
/// <see cref="LightPosition"/>; the TV switches their material. <see cref="ColumnHalfSpan"/> must equal the procedural
/// gantry's (track half width at s = 0 plus 2.6 m).
/// </summary>
public static class StartGantryModel
{
    public static readonly IReadOnlyList<string> Lights = ["light_1", "light_2", "light_3", "light_4", "light_5"];

    public const float LightSpacing = 1.15f;
    public const float LightY = 7.75f;
    public const float LightZ = -0.4f;

    /// <summary>Half the distance between the columns' centres.</summary>
    public static float ColumnHalfSpan => (float)(TrackGeometry.Width(0) / 2) + 2.6f;

    /// <summary>Pod <paramref name="light"/> (1..5), lit in this order: the same place as the procedural lamp.</summary>
    public static Vector3 LightPosition(int light) => new((light - 3) * LightSpacing, LightY, LightZ);
}

/// <summary>
/// The live-oak variants for the tree MultiMesh: nodes <see cref="Variants"/>, each one mesh at unit scale (crown
/// radius about 1 m, so the scatter's scale is the crown radius in metres) with a bark and a leaves surface; the leaves
/// take the per-instance colour.
/// </summary>
public static class TreeModel
{
    public static readonly IReadOnlyList<string> Variants = ["oak_a", "oak_b", "oak_c"];
    public const string Bark = "bark";
    public const string Leaves = "leaves";

    /// <summary>The variant tree <paramref name="index"/> of the scatter draws (round robin, so any prefix mixes all three).</summary>
    public static int VariantOf(int index) => index % Variants.Count;
}
