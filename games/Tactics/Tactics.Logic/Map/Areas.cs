using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>The footprint of an ability's area. JSON: <c>"single"</c>, <c>"burst"</c>, …, <c>"all_enemies"</c>.</summary>
public enum AreaShape
{
    /// <summary>The aimed tile.</summary>
    [JsonStringEnumMemberName("single")] Single,
    /// <summary>The caster's own tile.</summary>
    [JsonStringEnumMemberName("self")] Self,
    /// <summary>Every tile within a Manhattan radius of the aimed tile.</summary>
    [JsonStringEnumMemberName("burst")] Burst,
    /// <summary>A straight run from the caster toward the aim.</summary>
    [JsonStringEnumMemberName("line")] Line,
    /// <summary>A widening wedge from the caster toward the aim.</summary>
    [JsonStringEnumMemberName("cone")] Cone,
    /// <summary>The aimed tile and arms of the given length in the four directions.</summary>
    [JsonStringEnumMemberName("cross")] Cross,
    /// <summary>Tiles exactly a Manhattan radius from the aimed tile.</summary>
    [JsonStringEnumMemberName("ring")] Ring,
    /// <summary>Every ally of the caster; not a tile footprint.</summary>
    [JsonStringEnumMemberName("all_allies")] AllAllies,
    /// <summary>Every enemy of the caster; not a tile footprint.</summary>
    [JsonStringEnumMemberName("all_enemies")] AllEnemies,
}

/// <summary>An area: its shape, size (radius, length or step count) and who it affects.</summary>
public sealed record AreaDef(AreaShape Shape,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Size = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IncludeSelf = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool FriendlyFire = false);

/// <summary>Computes which tiles an area covers.</summary>
public static class Areas
{
    /// <summary>
    /// Tiles covered, sorted and inside the map. Burst, Cross and Ring are centred on <paramref name="aim"/> (aim at the caster
    /// for a self-centred area). Line and Cone extend from the caster toward the aim along the dominant axis (a tie goes to X);
    /// a Cone widens by 1 each side per step (step 1 = 1 tile, step 2 = 3, ...). Aimed at the caster, Line and Cone cover nothing.
    /// <see cref="AreaShape.AllAllies"/> and <see cref="AreaShape.AllEnemies"/> have no tile footprint and return nothing.
    /// <see cref="AreaDef.IncludeSelf"/> and <see cref="AreaDef.FriendlyFire"/> decide who is affected, not which tiles.
    /// </summary>
    public static ImmutableArray<Tile> Tiles(AreaDef area, Tile caster, Tile aim, Terrain terrain)
    {
        var tiles = new SortedSet<Tile>();
        var size = Math.Max(0, area.Size);

        switch (area.Shape)
        {
            case AreaShape.Single:
                tiles.Add(aim);
                break;
            case AreaShape.Self:
                tiles.Add(caster);
                break;
            case AreaShape.Burst:
                for (var dx = -size; dx <= size; dx++)
                for (var dy = -size; dy <= size; dy++)
                    if (Math.Abs(dx) + Math.Abs(dy) <= size)
                        tiles.Add(new Tile(aim.X + dx, aim.Y + dy));
                break;
            case AreaShape.Ring:
                for (var dx = -size; dx <= size; dx++)
                for (var dy = -size; dy <= size; dy++)
                    if (Math.Abs(dx) + Math.Abs(dy) == size)
                        tiles.Add(new Tile(aim.X + dx, aim.Y + dy));
                break;
            case AreaShape.Cross:
                tiles.Add(aim);
                for (var k = 1; k <= size; k++)
                {
                    tiles.Add(new Tile(aim.X + k, aim.Y));
                    tiles.Add(new Tile(aim.X - k, aim.Y));
                    tiles.Add(new Tile(aim.X, aim.Y + k));
                    tiles.Add(new Tile(aim.X, aim.Y - k));
                }
                break;
            case AreaShape.Line:
            case AreaShape.Cone:
                AddRay(tiles, area.Shape == AreaShape.Cone, size, caster, aim);
                break;
        }

        return [.. tiles.Where(terrain.Map.InBounds)];
    }

    private static void AddRay(SortedSet<Tile> tiles, bool widen, int size, Tile caster, Tile aim)
    {
        var dx = aim.X - caster.X;
        var dy = aim.Y - caster.Y;
        if (dx == 0 && dy == 0)
            return;

        var alongX = Math.Abs(dx) >= Math.Abs(dy);
        var sign = Math.Sign(alongX ? dx : dy);

        for (var step = 1; step <= size; step++)
        {
            var reach = widen ? step - 1 : 0;
            for (var side = -reach; side <= reach; side++)
                tiles.Add(alongX
                    ? new Tile(caster.X + sign * step, caster.Y + side)
                    : new Tile(caster.X + side, caster.Y + sign * step));
        }
    }
}
