namespace Redline.Presentation.Models;

/// <summary>
/// The naming contract of a Blender helmet, <c>helmets/&lt;CODE&gt;.glb</c> (<c>tools/blender/helmet.py</c> writes it
/// from the driver's scheme in <c>data/drivers.json</c>; the TV's <c>CarFactory</c> puts it at the car's
/// <see cref="CarModel.Helmet"/> empty). Origin at the head's centre, facing +Z, +Y up. One mesh, <see cref="Mesh"/>:
/// the base colour and the two accents of the driver's design keep their exported colours (they are the driver's, not
/// the team's), the visor is dark and glossy, and the flag on the back uses <c>flag_1..flag_4</c>.
/// </summary>
public static class HelmetModel
{
    public const int Budget = 12_000;

    public const string Mesh = "helmet";
    public const string Base = "helmet_base";
    public const string AccentA = "helmet_a";
    public const string AccentB = "helmet_b";
    public const string Visor = "visor";
    public const string Trim = "helmet_trim";

    public static readonly IReadOnlyList<string> Slots = [Base, AccentA, AccentB, Visor];

    /// <summary>The slots painted in the driver's design (gloss paint with clearcoat in Godot).</summary>
    public static readonly IReadOnlyList<string> PaintSlots = [Base, AccentA, AccentB];

    public static string File(string code) => $"helmets/{code}.glb";

    public static ModelContract Contract(string code) => new(File(code), Budget, [Mesh], Slots);
}
