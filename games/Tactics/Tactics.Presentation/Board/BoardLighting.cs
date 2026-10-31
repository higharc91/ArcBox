using System.Collections.Immutable;
using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Beats;

namespace Tactics.Presentation.Board;

/// <summary>One of the pack's soft round glows (<c>dungeon/manifest.json</c>'s <c>light.halos</c>).</summary>
/// <param name="Path">Relative to the pack's root.</param>
/// <param name="Size">Its size in pixels.</param>
/// <param name="Colour">The colour it's drawn in.</param>
public sealed record LightHalo(string Path, Vector2 Size, Rgb Colour);

/// <summary>What the pack says about lighting a room (<c>dungeon/manifest.json</c>'s <c>lighting</c> and <c>light</c>).</summary>
/// <param name="Ambient">The light of a room by its kind.</param>
/// <param name="Flicker">The curves by name.</param>
/// <param name="Halos">The soft glows it has.</param>
public sealed record LightRecipe(ImmutableSortedDictionary<string, Rgb> Ambient, ImmutableSortedDictionary<string, ImmutableArray<float>> Flicker, ImmutableArray<LightHalo> Halos);

/// <summary>A prop's <c>light</c>, in the variant it's drawn in.</summary>
/// <param name="Frames">Each frame's light points in the image; a still prop has one frame. A frame may hold more than one.</param>
/// <param name="Anchor">The prop's ground contact pixel.</param>
/// <param name="Radius">In tiles.</param>
/// <param name="Colour">The variant's colour.</param>
/// <param name="Flicker">A curve's name.</param>
/// <param name="Mask">Relative to the pack's root: the strip of pixels that glow by themselves, or null where the pack has none.</param>
public sealed record PropLight(ImmutableArray<ImmutableArray<Vector2>> Frames, Vector2 Anchor, float Radius, Rgb Colour, string Flicker, string? Mask);

/// <summary>A texture's <c>emissive</c> rule, in the variant it's drawn in.</summary>
/// <param name="Colour">The variant's colour.</param>
/// <param name="Every">One light per this many tiles, in a staggered grid.</param>
/// <param name="Radius">In tiles.</param>
/// <param name="Lift">How far above the surface, in pixels.</param>
/// <param name="Flicker">A curve's name.</param>
/// <param name="Power">How strong.</param>
public sealed record TextureGlow(Rgb Colour, int Every, float Radius, float Lift, string Flicker, float Power);

/// <summary>A light on the board, in art pixels, with everything that doesn't change while it burns.</summary>
/// <param name="Tile">The tile it belongs to.</param>
/// <param name="Ground">The middle of its pool on the floor.</param>
/// <param name="Sources">Where it burns, per frame of its prop; one place if it doesn't sway.</param>
/// <param name="Fps">How fast those frames go.</param>
/// <param name="Radius">In tiles.</param>
/// <param name="Colour">Its colour.</param>
/// <param name="Power">How strong.</param>
/// <param name="Curve">Its flicker curve.</param>
/// <param name="Phase">Where in the curve it starts, in steps, so lights don't pulse in step.</param>
/// <param name="Halo">The glow around it, or null for what only lights its surroundings.</param>
public sealed record BoardLight(Tile Tile, Vector2 Ground, ImmutableArray<Vector2> Sources, double Fps, float Radius, Rgb Colour, float Power,
    ImmutableArray<float> Curve, int Phase, LightHalo? Halo)
{
    /// <summary>A tile of radius, in pixels across the screen (the art tool's <c>radius * 32</c>).</summary>
    public const float PixelsPerTile = 32;

    /// <summary>How fast a curve runs. The recipe steps it by the frame, and a still prop has no frames.</summary>
    public const double CurveStepsPerSecond = 8;

    private const float FalloffPower = 1.6f;   // the recipe's (1 - d)^1.6
    private const float SphereReach = 0.8f;    // the art tool's: the round glow is a little smaller and fainter than the pool
    private const float SphereShare = 0.85f;

    /// <summary>Where it burns in the first frame.</summary>
    public Vector2 Source => Sources[0];

    /// <summary>The flicker curve at a moment, blended between its steps, as these run slower than the board redraws.</summary>
    public float Flicker(double seconds)
    {
        var at = seconds * CurveStepsPerSecond + Phase;
        var step = (long)Math.Floor(at);
        var (a, b) = (Curve[(int)(step % Curve.Length)], Curve[(int)((step + 1) % Curve.Length)]);
        return a + (b - a) * (float)(at - step);
    }

    /// <summary>How much of it reaches a point: the larger of the 2:1 pool on the floor under it and the round glow about the source itself.</summary>
    public float Falloff(Vector2 point)
    {
        var reach = Radius * PixelsPerTile;
        var pool = (point - Ground) / new Vector2(reach, reach / 2);
        return Math.Max(Curved(pool.Length()), Curved((point - Source).Length() / (reach * SphereReach)) * SphereShare);
    }

    private static float Curved(float distance) => distance >= 1 ? 0 : MathF.Pow(1 - distance, FalloffPower);
}

/// <summary>The lights in reach of a point of the board, kept so a point that doesn't move needn't look for them again.</summary>
public sealed class LightProbe
{
    internal LightProbe(int[] lights, float[] shares)
    {
        Lights = lights;
        Shares = shares;
    }

    /// <summary>How many lights reach it.</summary>
    public int Count => Lights.Length;

    internal int[] Lights { get; }

    internal float[] Shares { get; }
}

/// <summary>
/// The lights of a board and what they make of a point on it, by the pack's recipe: light = ambient + every light's colour x power x
/// flicker x falloff, at most <see cref="Ceiling"/>, and what's drawn is its art x that light. Pure: built from a <see cref="BoardPlan"/>,
/// so a tile that sinks or changes takes its light with it, and rebuilt with the plan. Between those, <see cref="Step"/> moves the
/// flicker on. Figures, marks and whatever glows by itself aren't darkened (<see cref="ForFigure"/>, <see cref="GlowMask"/>).
/// </summary>
public sealed class BoardLighting
{
    /// <summary>How bright light gets (the recipe's <c>min(light, 1.3)</c>).</summary>
    public const float Ceiling = 1.3f;

    /// <summary>
    /// A figure is never drawn darker than this, in any channel, so it reads in the dark of a room. The recipe's ambient alone would leave
    /// a figure as black as the floor it stands on, and on a TV who's where comes first.
    /// </summary>
    public const float FigureFloor = 0.75f;

    /// <summary>
    /// How far a room's ambient is lifted toward the art as drawn, on every map alike. The recipe's numbers are for its dioramas, which have a
    /// dozen lights; the maps have three or so, and the same dark would leave most of a fight unseen from the sofa.
    /// </summary>
    public const float AmbientLift = 0.3f;


    private const float Strength = 1.35f;   // the art tool's: a light's power at full is this much of its colour
    private const float HaloAlpha = 0.55f;
    private const float HaloTilesToScale = 0.5f;

    private static readonly Rgb Daylight = new(1, 1, 1);

    private readonly float[] _energy;

    private BoardLighting(Rgb ambient, ImmutableArray<BoardLight> lights)
    {
        Ambient = ambient;
        Lights = lights;
        _energy = new float[lights.Length];
        Step(0);
    }

    /// <summary>The room's own light. Daylight is the art as drawn.</summary>
    public Rgb Ambient { get; }

    /// <summary>Every light, in the plan's order.</summary>
    public ImmutableArray<BoardLight> Lights { get; }

    /// <summary>False where nothing changes the art as drawn, so the board can skip all of this.</summary>
    public bool Active => !Lights.IsEmpty || Ambient != Daylight;

    /// <summary>The lights of a plan, on the map <paramref name="mapId"/>.</summary>
    public static BoardLighting Of(BoardPlan plan, string mapId, LightRecipe recipe)
    {
        var lights = new List<BoardLight>();
        foreach (var item in plan.Items)
        {
            if (item is Sprite { Light: { } light } sprite)
                AddProp(lights, sprite, light, recipe);
            else if (item is Face { Image.Glow: { } glow } face && Scatters(face, glow))
                AddGlow(lights, face, glow, recipe);
        }
        return new BoardLighting(AmbientOf(mapId, recipe), [.. lights]);
    }

    /// <summary>A map has a room's light if a word of its id names one of the recipe's rooms: maps don't otherwise say what they are. The rest are open air.</summary>
    private static Rgb AmbientOf(string mapId, LightRecipe recipe)
    {
        foreach (var word in mapId.Split('_'))
            if (recipe.Ambient.TryGetValue(word, out var ambient))
                return Lifted(ambient);
        return Daylight;
    }

    private static Rgb Lifted(Rgb ambient)
    {
        static float Lift(float channel) => channel + (1 - channel) * AmbientLift;
        return new Rgb(Lift(ambient.R), Lift(ambient.G), Lift(ambient.B));
    }

    private static void AddProp(List<BoardLight> lights, Sprite sprite, PropLight light, LightRecipe recipe)
    {
        var ground = sprite.TopLeft + light.Anchor;
        var halo = NearestHalo(light.Colour, recipe);
        for (var i = 0; i < light.Frames[0].Length; i++)
        {
            ImmutableArray<Vector2> sources = [.. light.Frames.Select(frame => sprite.TopLeft + frame[i])];
            lights.Add(new BoardLight(sprite.Tile, ground, sources, sprite.Image.Fps, light.Radius, light.Colour, 1, Curve(light.Flicker, recipe),
                PhaseOf(sprite.Tile, lights.Count), halo));
        }
    }

    /// <summary>One light per <see cref="TextureGlow.Every"/> tiles, staggered row on row; a side's run along its edge.</summary>
    private static bool Scatters(Face face, TextureGlow glow) => face.Kind switch
    {
        FaceKind.Top => (face.Tile.X + face.Tile.Y * (glow.Every / 2)) % glow.Every == 0,
        FaceKind.Side => (face.Facing == Facing.U ? face.Tile.Y : face.Tile.X) % glow.Every == 0,
        _ => false,
    };

    private static void AddGlow(List<BoardLight> lights, Face face, TextureGlow glow, LightRecipe recipe)
    {
        var middle = face.Points.Aggregate(Vector2.Zero, Vector2.Add) / face.Points.Length;
        lights.Add(new BoardLight(face.Tile, middle, [middle - new Vector2(0, glow.Lift)], 0, glow.Radius, glow.Colour, glow.Power,
            Curve(glow.Flicker, recipe), PhaseOf(face.Tile, lights.Count), null));
    }

    private static ImmutableArray<float> Curve(string name, LightRecipe recipe) => recipe.Flicker.GetValueOrDefault(name, [1f]);

    /// <summary>The art tool's stagger, by the tile, which the count of lights before it spreads further where tiles would agree.</summary>
    private static int PhaseOf(Tile tile, int index) => tile.X * 5 + tile.Y * 3 + index * 7;

    private static LightHalo? NearestHalo(Rgb colour, LightRecipe recipe)
    {
        static float Gap(Rgb a, Rgb b) => (a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B);
        return recipe.Halos.IsEmpty ? null : recipe.Halos.MinBy(h => Gap(h.Colour, colour));
    }

    /// <summary>Moves every light's flicker on to a moment.</summary>
    public void Step(double seconds)
    {
        for (var i = 0; i < _energy.Length; i++)
            _energy[i] = Lights[i].Flicker(seconds) * Lights[i].Power * Strength;
    }

    /// <summary>The light at a point, now.</summary>
    public Rgb At(Vector2 point)
    {
        var (r, g, b) = (Ambient.R, Ambient.G, Ambient.B);
        for (var i = 0; i < _energy.Length; i++)
        {
            var share = Lights[i].Falloff(point) * _energy[i];
            r += share * Lights[i].Colour.R;
            g += share * Lights[i].Colour.G;
            b += share * Lights[i].Colour.B;
        }
        return Clamped(r, g, b);
    }

    /// <summary>The light at a point kept with <see cref="Probe"/>, now.</summary>
    public Rgb At(LightProbe probe)
    {
        var (r, g, b) = (Ambient.R, Ambient.G, Ambient.B);
        for (var i = 0; i < probe.Lights.Length; i++)
        {
            var light = probe.Lights[i];
            var share = probe.Shares[i] * _energy[light];
            var colour = Lights[light].Colour;
            r += share * colour.R;
            g += share * colour.G;
            b += share * colour.B;
        }
        return Clamped(r, g, b);
    }

    /// <summary>The lights in reach of a point that won't move, for reading on every frame.</summary>
    public LightProbe Probe(Vector2 point)
    {
        var reached = Enumerable.Range(0, Lights.Length).Select(i => (i, Share: Lights[i].Falloff(point))).Where(l => l.Share > 0).ToArray();
        return new LightProbe([.. reached.Select(l => l.i)], [.. reached.Select(l => l.Share)]);
    }

    /// <summary>The light on a figure at a point, now: what's near it, and never less than <see cref="FigureFloor"/>.</summary>
    public Rgb ForFigure(Vector2 point)
    {
        if (!Active)
            return Daylight;
        var light = At(point);
        return new Rgb(Math.Max(light.R, FigureFloor), Math.Max(light.G, FigureFloor), Math.Max(light.B, FigureFloor));
    }

    /// <summary>The soft glow about each source at a moment, which stands in for the recipe's bloom.</summary>
    public ImmutableArray<FxDraw> Halos(double seconds)
    {
        var draws = ImmutableArray.CreateBuilder<FxDraw>();
        foreach (var light in Lights)
        {
            if (light.Halo is not { } halo)
                continue;
            var at = light.Sources[light.Sources.Length == 1 ? 0 : (int)(seconds * light.Fps) % light.Sources.Length];
            var size = halo.Size * (light.Radius * HaloTilesToScale);
            draws.Add(new FxDraw(halo.Path, new ArtRect(0, 0, (int)halo.Size.X, (int)halo.Size.Y), at - size / 2, size, 0, true,
                Math.Clamp(HaloAlpha * light.Flicker(seconds), 0, 1)));
        }
        return draws.ToImmutable();
    }

    private static Rgb Clamped(float r, float g, float b) => new(Math.Min(r, Ceiling), Math.Min(g, Ceiling), Math.Min(b, Ceiling));
}

/// <summary>
/// What of a strip glows by itself (the recipe's emissive pixels, which skip the light), as RGBA pixels drawn again over the lit strip.
/// Pure bytes, so the Godot side only loads and saves them.
/// </summary>
public static class GlowMask
{
    private const float BrightEnough = 0.7f;   // of the colour's own brightness
    private const float WhiteHot = 0.95f;      // a flame's heart has lost its colour
    private const float Saturated = 0.25f;     // grey stone in the light's direction isn't a glow
    private const float SameHue = 0.94f;       // the cosine between two colours as vectors

    /// <summary>The pixels of <paramref name="rgba"/> with their coverage from an L8 <paramref name="mask"/> of the same size.</summary>
    public static byte[] FromMask(byte[] rgba, byte[] mask)
    {
        var glow = (byte[])rgba.Clone();
        for (var i = 0; i < mask.Length; i++)
            glow[i * 4 + 3] = (byte)(glow[i * 4 + 3] * mask[i] / 255);
        return glow;
    }

    /// <summary>The pixels of <paramref name="rgba"/> that are the brightest shades of <paramref name="colour"/>, for art the pack has no mask for.</summary>
    public static byte[] FromColour(byte[] rgba, Rgb colour)
    {
        var glow = (byte[])rgba.Clone();
        var want = Vector3.Normalize(new Vector3(colour.R, colour.G, colour.B));
        var brightest = Math.Max(colour.R, Math.Max(colour.G, colour.B));
        for (var i = 0; i < glow.Length; i += 4)
            glow[i + 3] = glow[i + 3] > 0 && Glows(new Vector3(glow[i], glow[i + 1], glow[i + 2]) / 255, want, brightest) ? glow[i + 3] : (byte)0;
        return glow;
    }

    private static bool Glows(Vector3 pixel, Vector3 want, float brightest)
    {
        var value = Math.Max(pixel.X, Math.Max(pixel.Y, pixel.Z));
        if (value < brightest * BrightEnough)
            return false;
        if (value >= WhiteHot && Math.Min(pixel.X, Math.Min(pixel.Y, pixel.Z)) > 0.75f)
            return true;
        var saturation = (value - Math.Min(pixel.X, Math.Min(pixel.Y, pixel.Z))) / value;
        return saturation >= Saturated && Vector3.Dot(Vector3.Normalize(pixel), want) >= SameHue;
    }
}
