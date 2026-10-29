namespace Redline.Presentation.Models;

/// <summary>
/// The naming contracts of the Blender trackside props (<c>tools/blender/trackside.py</c> writes them into
/// <c>models/trackside/</c>; the TV's <c>World/Trackside</c> places them from <see cref="Course.Trackside.Plan"/> as
/// MultiMeshes, one node = one mesh per prop). Every prop's origin is on the ground; a barrier's face is the plane
/// z = 0 and it reaches back to +Z, everything else looks along -Z (towards the track).
/// </summary>
public static class TracksideModels
{
    public const string Dir = "trackside/";

    public static readonly IReadOnlyList<string> BarrierNodes =
        ["tecpro_block", "tecpro_block_lod", "tyre_module", "tyre_module_lod", "armco_post", "fence_post", "fence_post_overhang"];

    /// <summary>
    /// The barriers: <c>tecpro</c> takes the per-instance colour; <c>belt</c> is the tyre wall's conveyor-belt face (UVs in
    /// metres); <c>galvanised</c> is the armco and fence steel.
    /// </summary>
    public static readonly ModelContract Barriers = new(Dir + "barriers.glb", 1_200, BarrierNodes,
        ["tecpro", "strap", "belt", "tyre", "tyre_inner", "galvanised"]);

    public static readonly IReadOnlyList<string> FurnitureNodes = ["marshal_post", "corner_board", "brake_board", "camera_platform"];

    /// <summary>
    /// The furniture: <c>number</c> is a board's face (UVs 0..1, top-left at (0, 0), aspect 1.6) on which the TV draws the
    /// instance's <see cref="Course.TracksideAtlas"/> cell; <c>led_panel</c> the marshal post's light panel (UVs 0..1).
    /// </summary>
    public static readonly ModelContract Furniture = new(Dir + "furniture.glb", 4_000, FurnitureNodes,
        ["hut", "hut_dark", "hut_roof", "concrete", "number", "board_back", "steel_dark", "galvanised", "led_panel", "red",
         "camera", "operator", "canvas", "deck"]);

    /// <summary>The big screen: <c>screen</c> is its face (UVs 0..1, top-left at (0, 0)), <see cref="ScreenSize"/> metres.</summary>
    public static readonly ModelContract BigScreen = new(Dir + "big_screen.glb", 12_000, ["big_screen"],
        ["screen", "screen_frame", "steel", "brand_white", "led_white"]);

    public static readonly (float Width, float Height) ScreenSize = (12f, 6.75f);

    public static IReadOnlyList<ModelContract> All { get; } = [Barriers, Furniture, BigScreen];

    /// <summary>The trackside's share of the course additions' 1.5M triangles (spec §3), as drawn on Ultra and High.</summary>
    public const int DrawnBudget = 700_000;
}
