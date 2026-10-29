using System.Numerics;
using Redline.Logic;
using Redline.Presentation.Ceremony;

namespace Redline.Presentation.Models;

/// <summary>
/// The naming contract of a Blender driver figure, <c>drivers/&lt;CODE&gt;.glb</c> (<c>tools/blender/driver.py</c>
/// writes it from <c>data/drivers.json</c>, the TV's <c>PodiumDriver</c> reads it). Figure frame: metres, +Y up, feet
/// on y = 0, facing +Z, +X to the figure's left (the right hand is on -X).
/// <para>
/// The rest pose holds the champagne bottle upright in front of the stomach, the right hand round its body and the left
/// hand under its base. Every node's rest rotation is the identity, so Godot poses the arms by rotating
/// <see cref="ArmR"/>/<see cref="ForearmR"/>/<see cref="HandR"/> (and the left chain) about their origins: shoulder,
/// elbow and wrist. <see cref="Bottle"/> is a child of <see cref="HandR"/> with its origin at the bottle's base and +Y up
/// its axis; the mouth is at <see cref="BottleMouth"/>, as on <see cref="BottleMesh"/>, so the spray rides on it.
/// </para>
/// <para>
/// <see cref="HairTop"/> is the hair the cap covers (hidden while the cap is on). <see cref="NumberSuit"/>,
/// <see cref="NameSuit"/> and <see cref="NumberCap"/> are empties where Godot draws the number and surname (local +Z out
/// of the surface, +Y up). Team slots (<see cref="TeamSlots"/>) take the team's colours from grid2026.json; the likeness
/// slots (skin, hair, eyes ...) keep the colours the glb was exported with.
/// </para>
/// </summary>
public static class DriverModel
{
    public const int Budget = 40_000;

    public const string Legs = "legs";
    public const string Spine = "spine";
    public const string Torso = "torso";
    public const string Head = "head";
    public const string Face = "face";
    public const string Hair = "hair";
    public const string HairTop = "hair_top";
    public const string Cap = "cap";
    public const string ArmR = "arm_r";
    public const string ForearmR = "forearm_r";
    public const string HandR = "hand_r";
    public const string ArmL = "arm_l";
    public const string ForearmL = "forearm_l";
    public const string HandL = "hand_l";
    public const string Bottle = "bottle";
    public const string NumberSuit = "number_suit";
    public const string NameSuit = "name_suit";
    public const string NumberCap = "number_cap";

    public const string SuitPrimary = "suit_primary";
    public const string SuitSecondary = "suit_secondary";
    public const string Belt = "belt";
    public const string Gloves = "gloves";
    public const string CapSlot = "cap";
    public const string CapBrim = "cap_brim";
    public const string BottleFoil = "bottle_foil";
    public const string BottleGlass = "bottle_glass";
    public const string Skin = "skin";

    /// <summary>The bottle's mouth in the <see cref="Bottle"/> node's frame.</summary>
    public static readonly Vector3 BottleMouth = new(0, BottleMesh.Height, 0);

    public static readonly IReadOnlyList<string> Nodes =
    [
        Legs, Spine, Torso, Head, Face, Hair, Cap, ArmR, ForearmR, HandR, Bottle, ArmL, ForearmL, HandL,
        NumberSuit, NameSuit, NumberCap,
    ];

    public static readonly IReadOnlyList<string> Slots =
    [
        Skin, "hair", SuitPrimary, SuitSecondary, CapSlot, "eyes", "iris", Gloves, "boots", Belt, CapBrim,
        BottleGlass, BottleFoil, "flag_1",
    ];

    /// <summary>Slots Godot binds to the team's colours.</summary>
    public static readonly IReadOnlyList<string> TeamSlots = [SuitPrimary, SuitSecondary, Belt, Gloves, CapSlot, CapBrim, BottleFoil];

    public static string File(string code) => $"drivers/{code}.glb";

    public static ModelContract Contract(string code) => new(File(code), Budget, Nodes, Slots);

    /// <summary>
    /// The suit's second colour: the livery's side colour, or (for one-colour cars) the endplate, else the stripe; never the
    /// same as the main colour (<see cref="Livery.Top"/>).
    /// </summary>
    public static string Secondary(Livery livery)
    {
        ArgumentNullException.ThrowIfNull(livery);
        foreach (var c in new[] { livery.Side, livery.Endplate, livery.Stripe, livery.Number })
        {
            if (!string.Equals(c, livery.Top, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }
        }
        return livery.Top;
    }

    /// <summary>The <c>#rrggbb</c> a team slot takes (<paramref name="accent"/> is the team accent for the foil), or null.</summary>
    public static string? TeamColour(string slot, Livery livery, string accent)
    {
        ArgumentNullException.ThrowIfNull(livery);
        return slot switch
        {
            SuitPrimary or CapSlot => livery.Top,
            SuitSecondary or Belt or Gloves or CapBrim => Secondary(livery),
            BottleFoil => accent,
            _ => null,
        };
    }

    /// <summary>The surname printed on the suit: the last word of the driver's name, upper case.</summary>
    public static string Surname(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : parts[^1].ToUpperInvariant();
    }
}
