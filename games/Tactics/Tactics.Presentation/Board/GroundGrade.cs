using System.Collections.Immutable;

namespace Tactics.Presentation.Board;

/// <summary>
/// What a place's hour does to its ground, on top of its light: the pack's tops are drawn for noon, and at the ford's dusk some of them shout. A grade multiplies a material's art,
/// so it holds the art's detail and only turns its colour. It's stated by region, as <see cref="BoardLighting"/>'s dusk is. It's the world's: a fight in the world is drawn from the
/// world's tiles and shares it, so there's no seam, and a fight on a plain board stays as its map was tuned.
/// </summary>
public static class GroundGrade
{
    private static readonly Rgb Plain = new(1, 1, 1);

    /// <summary>
    /// The Ashen Ford (lore: "golden-hour autumn turning cold"). Reds are taken down, so autumn reads tired and not on fire; ash is pushed back from the dusk's purple to grey;
    /// the stamp's green is burned out of it, since a mile of ash lies round it.
    /// </summary>
    private static readonly ImmutableDictionary<string, Rgb> Ford = new Dictionary<string, Rgb>(StringComparer.Ordinal)
    {
        ["ash/grey"] = new(1.0f, 1.26f, 1.06f),
        ["dirt/dark"] = new(0.8f, 1.0f, 1.04f),
        ["dirt/brown"] = new(0.76f, 0.9f, 0.95f),
        ["path/packed"] = new(0.8f, 0.86f, 0.86f),
        ["grass/autumn"] = new(0.72f, 0.88f, 1f),
        ["grass/dry"] = new(0.84f, 0.82f, 0.76f),
        ["grass/frost"] = new(0.72f, 0.8f, 0.8f),
        ["grass/lush"] = new(1.14f, 0.68f, 1.12f),
        ["rock/grey"] = new(0.74f, 0.82f, 0.8f),
        ["rock/dark"] = new(0.74f, 0.82f, 0.8f),
        ["water/clear"] = new(0.72f, 0.82f, 0.84f),
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, ImmutableDictionary<string, Rgb>> Regions = new Dictionary<string, ImmutableDictionary<string, Rgb>>(StringComparer.Ordinal)
    {
        ["ashen_ford"] = Ford,
    }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>What to multiply <paramref name="material"/>'s art by on <paramref name="mapId"/>: white for most of it, and everywhere that isn't graded.</summary>
    public static Rgb Of(string mapId, string material) => Regions.TryGetValue(mapId, out var grades) ? grades.GetValueOrDefault(material, Plain) : Plain;
}
