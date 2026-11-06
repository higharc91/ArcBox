using System.Collections.Immutable;
using System.Numerics;

namespace Tactics.Presentation.Board;

/// <summary>Shared emission defaults for luminous effect materials, separate from gameplay damage and targeting.</summary>
public sealed record SpellEmission(Rgb Colour, float Radius, float Power)
{
    public static SpellEmission? ForAsset(string key, bool additive)
    {
        var pieces = key.Split('.');
        var material = pieces[^1];
        if (pieces[0] == "projectiles")
            material = material switch
            {
                "fireball" or "fire_bolt" or "flame" => "fire",
                "missile" or "magic_missile" or "force" => "force",
                "lightning" or "spark" => "storm",
                "ice" or "frost" => "ice",
                "eldritch" => "eldritch",
                _ => material,
            };
        if (!additive && material is not ("fire" or "hellfire")) return null;
        var colour = material switch
        {
            "fire" or "ember" => "#ff8a32",
            "hellfire" or "fel" => "#8fe05a",
            "ice" or "frost" or "storm" or "thunder" or "lightning" or "azure" => "#8fcfff",
            "spark" => "#ffd080",
            "holy" or "radiant" or "gold" or "dawn" or "spirit" or "ki" => "#ffe3a0",
            "moon" => "#c3d9ff",
            "arcane" or "force" or "magic" => "#7aa8ff",
            "eldritch" or "shadow" or "violet" or "void" => "#b86ef2",
            "nature" or "fey" => "#85d878",
            "grave" or "necro" or "necrotic" => "#9cf06a",
            "psychic" or "hex" or "mind" or "charm" => "#e070e8",
            _ => null,
        };
        if (colour is null) return null;
        var small = pieces.Any(p => p is "impact" or "hit" or "crit" or "slash" or "thrust");
        var projectile = pieces[0] == "projectiles";
        return new(Rgb.Hex(colour), small ? 1.2f : projectile ? 2 : 3.2f, small ? .22f : projectile ? .8f : 1.35f);
    }

    public SpellLight At(Vector2 source, Vector2 ground, float envelope = 1)
        => new(source, ground, Radius, Colour, Power * Math.Clamp(envelope, 0, 1));
}

/// <summary>One moving spell's soft pool and raised source, in board art pixels. It owns no GPU textures.</summary>
public sealed record SpellLight(Vector2 Source, Vector2 Ground, float Radius, Rgb Colour, float Power)
{
    public Rgb At(Vector2 point)
    {
        var reach = Math.Max(1, Radius * BoardLight.PixelsPerTile);
        var pool = ((point - Ground) / new Vector2(reach, reach / 2)).Length();
        var sphere = (point - Source).Length() / (reach * .8f);
        static float Fade(float distance) { var x = Math.Max(0, 1 - distance); return x * x; }
        var share = Math.Max(Fade(pool), Fade(sphere) * .85f) * Power;
        return new(Colour.R * share, Colour.G * share, Colour.B * share);
    }
}

public static class SpellLighting
{
    /// <summary>Persistent spell fire and luminous zones stay lit between turns, and disappear with their visible board state.</summary>
    public static ImmutableArray<SpellLight> ForZones(BoardTerrain terrain, double seconds, bool reduced)
    {
        if (terrain.Changes.Zones.IsEmpty) return [];
        var lights = ImmutableArray.CreateBuilder<SpellLight>();
        foreach (var zone in terrain.Changes.Zones)
        {
            var material = zone.Kind switch
            {
                "fire" or "burning" or "ember_rune" => "fire",
                "holy" => "holy",
                "moonbeam" => "moon",
                _ => null,
            };
            if (material is null) continue;
            var emission = SpellEmission.ForAsset($"fx.zone.{material}", true)! with { Radius = 2.2f, Power = .6f };
            foreach (var tile in zone.Tiles.Where(terrain.Map.InBounds).Distinct())
            {
                var ground = terrain.Centre(tile);
                var flicker = reduced ? .8f : .9f + .1f * (float)Math.Sin(seconds * 7 + tile.X * 5 + tile.Y * 3);
                lights.Add(emission.At(ground - new Vector2(0, 14), ground, flicker));
            }
        }
        return lights.ToImmutable();
    }

    /// <summary>Add emitted light to the current static/baked illumination, retaining the board's brightness ceiling.</summary>
    public static Rgb Apply(Rgb basis, Vector2 point, ImmutableArray<SpellLight> lights, Rgb? surface = null)
    {
        var r = 0f; var g = 0f; var b = 0f;
        foreach (var light in lights.IsDefault ? [] : lights)
        {
            var emission = light.At(point);
            r += emission.R; g += emission.G; b += emission.B;
        }
        return new(Math.Min(BoardLighting.Ceiling, basis.R + r * (surface?.R ?? 1)),
            Math.Min(BoardLighting.Ceiling, basis.G + g * (surface?.G ?? 1)),
            Math.Min(BoardLighting.Ceiling, basis.B + b * (surface?.B ?? 1)));
    }
}
