using System.Numerics;
using System.Collections.Concurrent;
using System.Globalization;
using Tactics.Logic;
namespace Tactics.Presentation.Board;

/// <summary>The physical plane of an authored road ramp, in height steps at a fractional map position.</summary>
public static class RoadSurface
{
    private static readonly Tile[] Neighbours = [new(-1,0),new(1,0),new(0,-1),new(0,1)];
    private static readonly ConcurrentDictionary<string,float[]> Corners = new(StringComparer.Ordinal);
    private static float[] Quad(string material)
    {
        if (!material.StartsWith("road_ramp/q_",StringComparison.Ordinal)) return [];
        return Corners.GetOrAdd(material, key =>
        {
            var parts=key["road_ramp/q_".Length..].Split('_');
            if(parts.Length is not (4 or 5))return [];
            var result=new float[4];
            for(var i=0;i<4;i++)
            {
                if(!int.TryParse(parts[i],NumberStyles.Integer,CultureInfo.InvariantCulture,out var q)||q is < -8 or > 8)return [];
                result[i]=q/4f;
            }
            return result;
        });
    }
    /// <summary>Samples the ramp itself, or the flat approach touching it. False preserves legacy walking elsewhere.</summary>
    public static bool TryHeight(BoardTerrain terrain, Vector2 at, out float height)
    {
        var tile=new Tile((int)MathF.Floor(at.X),(int)MathF.Floor(at.Y));
        height=0;
        if(!terrain.Map.InBounds(tile))return false;
        var material=terrain.Map.Materials[terrain.Map[tile].Material];
        height=terrain.Top(tile);
        if(Quad(material) is {Length:4} corners)
        {
            var x=at.X-tile.X;var y=at.Y-tile.Y;
            // Same diagonal and planes as the two native geometry triangles.
            height += x>=y ? corners[0]+(corners[1]-corners[0])*x+(corners[2]-corners[1])*y
                : corners[0]+(corners[2]-corners[3])*x+(corners[3]-corners[0])*y;
            return true;
        }
        if(Profile(material) is { } plane)
        {
            height+=plane.Base+plane.U*(at.X-tile.X)+plane.V*(at.Y-tile.Y);
            return true;
        }
        // At the exact seam use the adjoining flat surface, without a synthetic hop.
        foreach(var delta in Neighbours)
        {
            var near=new Tile(tile.X+delta.X,tile.Y+delta.Y);
            if(terrain.Map.InBounds(near))
            {
                var name=terrain.Map.Materials[terrain.Map[near].Material];
                if(Profile(name) is not null || Quad(name).Length==4)return true;
            }
        }
        return false;
    }

    private static (float Base,float U,float V)? Profile(string material)=>material switch
    {
        "warden_ramp/west" or "warden_ramp/u1" => (0,1,0),
        "warden_ramp/east_high" => (0,-.5f,0),
        "warden_ramp/east_low" => (.5f,-.5f,0),
        "warden_ramp/u2" => (0,2,0),
        "warden_ramp/nu1" => (1,-1,0),
        "warden_ramp/nu2" => (2,-2,0),
        "warden_ramp/v1" => (0,0,1),
        "warden_ramp/v2" => (0,0,2),
        "warden_ramp/nv1" => (1,0,-1),
        "warden_ramp/nv2" => (2,0,-2),
        _=>null,
    };
}
