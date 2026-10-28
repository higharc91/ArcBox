using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Course;

/// <summary>
/// What the surroundings are planned round: the track (<see cref="Samples"/>), the rendered ground, the Observation
/// Tower, the landmark footprints (<c>Landmarks.Footprints</c> on the TV, the tower's included) and the trees' crowns,
/// all of which keep what they have. <see cref="PitS"/> is the pit building's middle along the lap.
/// </summary>
public sealed record SurroundingsSite(
    IReadOnlyList<TrackSample> Samples,
    TerrainSampler Ground,
    double LapLength,
    Vector2 Tower,
    IReadOnlyList<KeepOut> Landmarks,
    IReadOnlyList<KeepOut> Trees)
{
    /// <summary>The pit building's middle: <c>LapLength - 175</c>, as the TV's <c>Landmarks.PitS</c>.</summary>
    public double PitS => LapLength - Surroundings.PitFromLapEnd;
}

/// <summary>A car park: grass (worn aisles) or paved (painted bays). Cars park in rows along <see cref="Obb.Axis"/>.</summary>
public enum LotSurface { Grass, Paved }

public sealed record CarPark(Obb Area, LotSurface Surface, bool Infield, Vector2 Entrance);

/// <summary>A hospitality compound: a gravel pad with marquees and pagoda tents.</summary>
public sealed record Compound(Obb Area, bool Infield, Vector2 Entrance);

/// <summary>A service road's centre line (metres, ground plane) and its width.</summary>
public sealed record Road(IReadOnlyList<Vector2> Points, float Width);

/// <summary>
/// One instanced prop: its model variant, position on the ground, yaw (radians about +Y, Godot's sense), scale, a 0..1
/// shade for colour variety and its footprint on the ground (what the overlap rules check).
/// </summary>
public readonly record struct Prop(int Variant, Vector3 Position, float Yaw, float Scale, float Shade, Obb Footprint);

/// <summary>
/// The Grand Plaza beside the Observation Tower: a paved apron round the tower (<see cref="ApronRadius"/>), the
/// promenade leading to it (<see cref="Promenade"/>, its axis pointing at the tower) and the amphitheatre beside the
/// tower (<see cref="Amphitheatre"/>: its stage nearest the tower, the lawn bowl rising along
/// <see cref="AmphitheatreAxis"/>). The amphitheatre model's origin (stage front, centre, on the highest ground under
/// it) is <see cref="AmphitheatreOrigin"/>, its local +Z = AmphitheatreAxis. Roads arrive at <see cref="Entrance"/>.
/// </summary>
public sealed record GrandPlaza(
    Vector2 Tower,
    Vector2 AmphitheatreAxis,
    float ApronRadius,
    Obb Promenade,
    Obb Amphitheatre,
    Vector3 AmphitheatreOrigin,
    Vector2 Entrance);

/// <summary>The distant Austin skyline: where its model stands (ground), which way it faces and its scale.</summary>
public sealed record SkylineSite(Vector3 Position, Vector2 Facing, float Scale);

/// <summary>
/// Everything <see cref="Surroundings.Plan"/> places. <see cref="Clearings"/> is the ground the plaza complex takes from
/// the trees (circles no tree's trunk may stand in, grown by the largest crown): the TV scatters its trees again with
/// these kept clear, which drops the trees there and keeps every other tree exactly where it was.
/// <see cref="Trees"/> are the site's trees that remain.
/// </summary>
public sealed record SurroundingsPlan(
    GrandPlaza Plaza,
    IReadOnlyList<CarPark> CarParks,
    IReadOnlyList<Compound> Compounds,
    IReadOnlyList<Road> Roads,
    IReadOnlyList<Prop> ParkedCars,
    IReadOnlyList<Prop> Tents,
    IReadOnlyList<Prop> Shrubs,
    IReadOnlyList<Prop> PlazaProps,
    IReadOnlyList<Prop> Groves,
    IReadOnlyList<Prop> FarClumps,
    SkylineSite Skyline,
    IReadOnlyList<KeepOut> Clearings,
    IReadOnlyList<KeepOut> Trees);

/// <summary>
/// Plans COTA's surroundings (spec §2.4): the Grand Plaza and amphitheatre beside the Observation Tower, car parks with
/// parked cars, hospitality compounds with tents, service roads joining them, scattered shrubs and the distant skyline.
/// Nothing overlaps the track and its run-off (<see cref="TrackClearance"/> from the centre line), the pit lane and
/// building, the landmarks or the trees' crowns, and nothing overlaps anything else placed here. The tower stands in
/// tree clusters, so the plaza complex alone clears its own ground (<see cref="SurroundingsPlan.Clearings"/>, the
/// layout that clears fewest trees); everything else fits round the trees. Deterministic per seed. The instanced lists are in random order, so any prefix is spread over the whole circuit: the Low preset
/// draws the first half.
/// </summary>
public static class Surroundings
{
    /// <summary>Run-off allowance beyond the track width (spec: "the track width plus 12 m").</summary>
    public const double RunoffAllowance = 12;

    /// <summary>The pit building's middle is this far before the end of the lap (<c>Landmarks.PitS</c>).</summary>
    public const double PitFromLapEnd = 175;

    /// <summary>The pit lane and building, laterally from the centre line on the pit side (left), with room behind.</summary>
    public const float PitZoneInner = 16, PitZoneOuter = 56;

    /// <summary>The pit zone's reach along the lap, either side of <see cref="SurroundingsSite.PitS"/>.</summary>
    public const float PitZoneBefore = 200, PitZoneAfter = 195;

    /// <summary>The tower's own structure (its veil lands on a 26 m ring): the plaza may pave round it, nothing stands inside.</summary>
    public const float TowerStructure = 28;

    public const float RoadWidth = 7;
    public const float PromenadeHalfWidth = 15;
    public const float ApronRadius = 38;

    /// <summary>The amphitheatre model's footprint in its own frame (x across, z from the stage back to the rim).</summary>
    public const float AmphiHalfWidth = 42, AmphiBack = -16, AmphiRim = 48;

    /// <summary>Distance from the tower centre to the amphitheatre's origin (the stage front).</summary>
    public const float AmphiFromTower = 46;

    /// <summary>The largest tree crown radius (<c>TreeScatter</c>): clearings grow by it so no crown reaches in.</summary>
    public const float MaxCrown = 7;

    public const int MaxParkedCars = 1800;
    public const int ShrubCount = 1300;
    public const int GroveCount = 750;
    public const int FarClumpCount = 3600;

    /// <summary>Parked car bay pitch and row depth; an aisle between double rows.</summary>
    public const float BayPitch = 2.7f, RowDepth = 5.2f, Aisle = 7.5f, LotMargin = 3f;

    /// <summary>Downtown Austin from COTA: about 18 km away, NNW (x east, y south).</summary>
    public static readonly Vector2 AustinDirection = Vector2.Normalize(new Vector2(-0.53f, -0.85f));

    public const float AustinDistance = 18_000;
    public const float SkylineDistance = 6_500;

    /// <summary>Distance from the centre line inside which nothing new stands: the track's width plus the run-off.</summary>
    public static double TrackClearance(double s) => TrackGeometry.Width(s) + RunoffAllowance;

    public static SurroundingsPlan Plan(SurroundingsSite site, int seed = 2026)
    {
        ArgumentNullException.ThrowIfNull(site);
        var world = new World(site);
        var (plaza, clearings) = PlacePlaza(world);
        world.Clear(clearings);
        world.AddPlaza(plaza);

        var plazaProps = PlazaProps(world, plaza);
        var compounds = PlaceCompounds(world, new Random(seed + 1));
        var tents = Tents(world, compounds);
        var lots = PlaceCarParks(world, plaza, new Random(seed + 2));
        var roads = new RoadRouter(world).Route(plaza, lots, compounds);
        foreach (var r in roads)
            world.AddRoad(r);
        var cars = ParkCars(world, lots, new Random(seed + 3));
        var groves = PlaceGroves(world, new Random(seed + 5));
        var standing = world.Trees;
        foreach (var g in groves)
            world.AddTree(new KeepOut(g.Footprint.Centre, g.Footprint.HalfLength));
        var shrubs = PlaceShrubs(world, new Random(seed + 4), ShrubCount);
        var far = PlaceFarClumps(world, new Random(seed + 6));
        return new SurroundingsPlan(plaza, lots, compounds, roads, cars, tents, shrubs, plazaProps, groves, far, Skyline(site),
            clearings, standing);
    }

    // ---- the Grand Plaza ------------------------------------------------------------------------------------------

    /// <summary>
    /// Tries every direction round the tower (5° steps) for the amphitheatre and for the promenade (at least 100° apart),
    /// and promenade lengths from 150 m down to 40 m. Both must keep clear of the track's clearance, the pit zone and the
    /// landmarks. Of those layouts it keeps the one that clears the fewest trees, then the longest promenade, then the
    /// flattest ground under the amphitheatre.
    /// </summary>
    private static (GrandPlaza Plaza, List<KeepOut> Clearings) PlacePlaza(World w)
    {
        var tower = w.Site.Tower;
        var amphis = new List<(Vector2 Axis, Obb Box, HashSet<int> Trees, float Rough)>();
        var proms = new List<(Vector2 Axis, Obb Box, HashSet<int> Trees, float Length)>();
        for (var deg = 0; deg < 360; deg += 5)
        {
            var axis = Shapes.Dir(deg * Math.PI / 180);
            var amphi = AmphiBox(tower, axis);
            if (w.Free(amphi, track: 6, other: 4, tree: float.NaN, ignoreTower: true))
            {
                var (lo, hi) = w.HeightRange(amphi);
                amphis.Add((axis, amphi, w.TreesIn(amphi.Grown(MaxCrown)), hi - lo));
            }
            for (var len = 150f; len >= 40f; len -= 10f)
            {
                var prom = PromenadeBox(tower, -axis, len);
                if (w.Free(prom, track: 6, other: 4, tree: float.NaN, ignoreTower: true))
                {
                    proms.Add((axis, prom, w.TreesIn(prom.Grown(MaxCrown)), len));
                    break;
                }
            }
        }
        (int A, int P)? best = null;
        double bestScore = double.NegativeInfinity;
        for (var a = 0; a < amphis.Count; a++)
            for (var p = 0; p < proms.Count; p++)
            {
                if (Vector2.Dot(amphis[a].Axis, proms[p].Axis) > MathF.Cos(100 * MathF.PI / 180))
                    continue;
                var cleared = amphis[a].Trees.Union(proms[p].Trees).Count();
                var score = (-40.0 * cleared) + proms[p].Length - (6 * amphis[a].Rough);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (a, p);
                }
            }
        if (best is not { } pick)
            throw new InvalidOperationException("No room for the Grand Plaza beside the tower.");
        var (amphiAxis, amphiBox, _, _) = amphis[pick.A];
        var promBox = proms[pick.P].Box;
        var (_, top) = w.HeightRange(amphiBox);
        var origin2 = tower + (amphiAxis * AmphiFromTower);
        var plaza = new GrandPlaza(tower, amphiAxis, ApronRadius, promBox, amphiBox, new Vector3(origin2.X, top, origin2.Y),
            promBox.At(-promBox.HalfLength - 6, 0));
        return (plaza, [.. Cover(amphiBox.Grown(MaxCrown)), .. Cover(promBox.Grown(MaxCrown))]);
    }

    /// <summary>Circles that together cover <paramref name="box"/> (a grid of 7.5 m circles at most 10 m apart).</summary>
    public static IEnumerable<KeepOut> Cover(Obb box)
    {
        const float spacing = 10, radius = 7.5f;
        var nu = Math.Max(1, (int)MathF.Ceiling(2 * box.HalfLength / spacing));
        var nv = Math.Max(1, (int)MathF.Ceiling(2 * box.HalfWidth / spacing));
        for (var i = 0; i <= nu; i++)
            for (var j = 0; j <= nv; j++)
                yield return new KeepOut(box.At(-box.HalfLength + (2 * box.HalfLength * i / nu), -box.HalfWidth + (2 * box.HalfWidth * j / nv)), radius);
    }

    public static Obb AmphiBox(Vector2 tower, Vector2 axis)
    {
        var origin = tower + (axis * AmphiFromTower);
        var mid = (AmphiBack + AmphiRim) / 2;
        return new Obb(origin + (axis * mid), axis, (AmphiRim - AmphiBack) / 2, AmphiHalfWidth);
    }

    /// <summary>
    /// The promenade: from inside the apron (where its corners meet the apron's edge) out along -<paramref name="axis"/>;
    /// its own axis points at the tower.
    /// </summary>
    public static Obb PromenadeBox(Vector2 tower, Vector2 axis, float length)
    {
        var start = MathF.Sqrt((ApronRadius * ApronRadius) - (PromenadeHalfWidth * PromenadeHalfWidth));
        return new Obb(tower - (axis * (start + (length / 2))), axis, length / 2, PromenadeHalfWidth);
    }

    /// <summary>Light poles and flag poles along the promenade, kiosks between them, flags round the apron.</summary>
    private static List<Prop> PlazaProps(World w, GrandPlaza p)
    {
        var props = new List<Prop>();
        var prom = p.Promenade;
        var yaw = prom.Yaw;
        void Add(int variant, Vector2 at, float facingYaw, float halfL, float halfW)
        {
            var fp = new Obb(at, Shapes.Dir(-facingYaw), halfL, halfW);
            if (Vector2.Distance(at, p.Tower) - fp.Reach < TowerStructure || p.Amphitheatre.Overlaps(fp, 1)
                || !w.Free(fp, track: 4, other: 1, tree: 1, ignoreTower: true, ignorePlaza: true))
                return;
            props.Add(new Prop(variant, new Vector3(at.X, w.Site.Ground.Height(at.X, at.Y), at.Y), facingYaw, 1, 0.5f, fp));
        }
        for (var a = -prom.HalfLength + 6; a <= prom.HalfLength - 2; a += 16)
            foreach (var side in new[] { -1f, 1f })
            {
                Add(PlazaModelsIndex.LightPole, prom.At(a, side * (PromenadeHalfWidth - 1.5f)), yaw, 0.4f, 0.4f);
                Add(PlazaModelsIndex.Planter, prom.At(a + 8, side * (PromenadeHalfWidth - 1.5f)), yaw, 1.3f, 1.3f);
            }
        for (var a = -prom.HalfLength + 14; a <= prom.HalfLength - 10; a += 32)
            foreach (var side in new[] { -1f, 1f })
            {
                // Kiosks face across the promenade: their counters (local +Z) towards its middle.
                var facing = prom.Across * -side;
                Add(PlazaModelsIndex.Kiosk, prom.At(a + (side > 0 ? 16 : 0), side * (PromenadeHalfWidth - 5)), MathF.Atan2(facing.X, facing.Y), 2.4f, 1.8f);
            }
        for (var deg = 0; deg < 360; deg += 15)
        {
            var d = Shapes.Dir(deg * Math.PI / 180);
            // Leave the promenade and the amphitheatre side open.
            if (Vector2.Dot(d, -prom.Axis) > 0.8f || Vector2.Dot(d, p.AmphitheatreAxis) > 0.45f)
                continue;
            Add(PlazaModelsIndex.FlagPole, p.Tower + (d * (ApronRadius - 3)), 0, 0.3f, 0.3f);
        }
        return props;
    }

    // ---- hospitality compounds and tents ------------------------------------------------------------------------

    private static List<Compound> PlaceCompounds(World w, Random rng)
    {
        var q = w.Site.Samples;
        var result = new List<Compound>();
        var picked = new List<double>();
        for (var tries = 0; tries < 3000 && result.Count < 6; tries++)
        {
            var i = rng.Next(q.Count);
            var t = q[i];
            if (picked.Any(s => LapGap(s, t.S, w.Site.LapLength) < 550))
                continue;
            var side = rng.Next(2) == 0 ? -1 : 1;
            var halfL = 30 + (rng.Next(3) * 8);
            const float halfW = 15;
            var lateral = (float)(TrackClearance(t.S) + 6 + halfW + (rng.NextDouble() * 40));
            var centre = t.Position + (t.Left * (side * lateral));
            var box = new Obb(centre, t.Tangent, halfL, halfW);
            if (!w.Free(box, track: 6, other: 8, tree: 3))
                continue;
            var entrance = box.At(0, side * (halfW + 6));
            var c = new Compound(box, w.Infield(centre), entrance);
            result.Add(c);
            picked.Add(t.S);
            w.AddBox(box);
        }
        return result;
    }

    /// <summary>In each compound: one or two marquees along the back, a row of pagodas along the front.</summary>
    private static List<Prop> Tents(World w, IReadOnlyList<Compound> compounds)
    {
        var tents = new List<Prop>();
        foreach (var c in compounds)
        {
            var a = c.Area;
            // The side away from the track is the back (the entrance side).
            var back = Vector2.Dot(c.Entrance - a.Centre, a.Across) > 0 ? 1f : -1f;
            var yaw = a.Yaw;
            var marquees = a.HalfLength >= 38 ? 2 : 1;
            for (var m = 0; m < marquees; m++)
            {
                var along = marquees == 1 ? 0 : (m == 0 ? -11.5f : 11.5f);
                var at = a.At(along, back * (a.HalfWidth - 6.5f));
                tents.Add(Tent(w, TentModelsIndex.Marquee, at, yaw, 10.5f, 5.5f));
            }
            for (var x = -a.HalfLength + 5; x <= a.HalfLength - 5; x += 9.5f)
            {
                var at = a.At(x, -back * (a.HalfWidth - 5));
                tents.Add(Tent(w, TentModelsIndex.Pagoda, at, yaw, 3.2f, 3.2f));
            }
        }
        return tents;
    }

    private static Prop Tent(World w, int variant, Vector2 at, float yaw, float halfL, float halfW) =>
        new(variant, new Vector3(at.X, w.Site.Ground.Height(at.X, at.Y), at.Y), yaw, 1, 0.5f, new Obb(at, Shapes.Dir(-yaw), halfL, halfW));

    // ---- car parks and parked cars ------------------------------------------------------------------------------

    private static readonly (float L, float W)[] LotSizes = [(70, 30), (60, 26), (55, 24), (80, 34), (45, 22)];

    private static List<CarPark> PlaceCarParks(World w, GrandPlaza plaza, Random rng)
    {
        var q = w.Site.Samples;
        var result = new List<CarPark>();
        int infield = 0, outside = 0;
        for (var tries = 0; tries < 6000 && (infield < 4 || outside < 7); tries++)
        {
            var t = q[rng.Next(q.Count)];
            var side = rng.Next(2) == 0 ? -1 : 1;
            var (halfL, halfW) = LotSizes[rng.Next(LotSizes.Length)];
            var lateral = (float)(TrackClearance(t.S) + 25 + halfW + (Math.Pow(rng.NextDouble(), 1.5) * 260));
            var centre = t.Position + (t.Left * (side * lateral));
            var axis = Vector2.Normalize(t.Tangent + (t.Left * (float)((rng.NextDouble() - 0.5) * 0.2)));
            var box = new Obb(centre, axis, halfL, halfW);
            var inside = w.Infield(centre);
            if ((inside && infield >= 4) || (!inside && outside >= 7))
                continue;
            if (!w.Site.Ground.Contains(centre) || !w.Free(box, track: 10, other: 18, tree: 4))
                continue;
            // Roads come in on the side away from the track.
            var away = Vector2.Dot(t.Left * side, box.Across) > 0 ? 1f : -1f;
            var entrance = box.At(0, away * (halfW + 6));
            var paved = Vector2.Distance(centre, plaza.Tower) < 420 || rng.NextDouble() < 0.3;
            result.Add(new CarPark(box, paved ? LotSurface.Paved : LotSurface.Grass, inside, entrance));
            w.AddBox(box);
            if (inside) infield++; else outside++;
        }
        return result;
    }

    /// <summary>
    /// Bays in double rows along each lot (noses to the aisle), filled nearest the entrance first with a random gap
    /// here and there; every lot about the same fullness so the total stays within <see cref="MaxParkedCars"/>.
    /// </summary>
    private static List<Prop> ParkCars(World w, IReadOnlyList<CarPark> lots, Random rng)
    {
        var bays = new List<(CarPark Lot, Vector2 At, float Yaw, float Rank)>();
        foreach (var lot in lots)
        {
            var a = lot.Area;
            var width = 2 * a.HalfWidth;
            var entranceSide = Vector2.Dot(lot.Entrance - a.Centre, a.Across) > 0 ? 1f : -1f;
            for (var v = LotMargin; v + (2 * RowDepth) <= width - LotMargin + 0.01f; v += (2 * RowDepth) + Aisle)
                for (var row = 0; row < 2; row++)
                {
                    var across = -a.HalfWidth + v + (RowDepth * (row + 0.5f));
                    // Row 0 faces -Across (towards the aisle before it), row 1 faces +Across.
                    var facing = row == 0 ? -a.Across : a.Across;
                    var yaw = MathF.Atan2(facing.X, facing.Y);
                    for (var u = -a.HalfLength + LotMargin + (BayPitch / 2); u <= a.HalfLength - LotMargin - (BayPitch / 2); u += BayPitch)
                    {
                        var rank = (entranceSide * across / a.HalfWidth) + (0.35f * MathF.Abs(u) / a.HalfLength);
                        bays.Add((lot, a.At(u, across), yaw, -rank));
                    }
                }
        }
        // Fill the best-ranked bays (nearest the entrance) of each lot: some lots fuller than others, all within the cap.
        var fraction = Math.Min(0.85, MaxParkedCars / Math.Max(1.0, bays.Count));
        var cars = new List<Prop>();
        foreach (var group in bays.GroupBy(b => b.Lot))
        {
            var ordered = group.OrderBy(b => b.Rank).ToList();
            var take = (int)Math.Round(ordered.Count * fraction * (0.65 + (0.35 * rng.NextDouble())));
            // Runs of empty bays here and there (someone left early), as well as single gaps.
            var runAt = rng.Next(Math.Max(1, take));
            var runLength = rng.Next(3, 9);
            for (var k = 0; k < ordered.Count; k++)
            {
                var b = ordered[k];
                var p = rng.NextDouble();
                var variant = PickCar(rng.NextDouble());
                var colour = (float)rng.NextDouble();
                var jitter = new Vector2((float)(rng.NextDouble() - 0.5) * 0.5f, (float)(rng.NextDouble() - 0.5) * 0.35f);
                var yaw = b.Yaw + (float)((rng.NextDouble() - 0.5) * 0.12);
                // Mostly the first `take`, with gaps and an empty run among them and a few strays beyond.
                var park = k < take ? p > 0.13 && (k < runAt || k >= runAt + runLength) : p < 0.05;
                if (!park)
                    continue;
                var at = b.At + jitter;
                var fwd = new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
                var fp = new Obb(at, fwd, 2.5f, 1.05f);
                cars.Add(new Prop(variant, new Vector3(at.X, w.Site.Ground.Height(at.X, at.Y), at.Y), yaw, 1, colour, fp));
            }
        }
        Shuffle(cars, rng);
        return cars.Count > MaxParkedCars ? cars.GetRange(0, MaxParkedCars) : cars;
    }

    /// <summary>Texas mix: pickups and SUVs first, then sedans and hatchbacks.</summary>
    private static int PickCar(double r) => r switch
    {
        < 0.3 => CarModelsIndex.Pickup,
        < 0.6 => CarModelsIndex.Suv,
        < 0.86 => CarModelsIndex.Sedan,
        _ => CarModelsIndex.Hatch,
    };

    // ---- groves and treelines -------------------------------------------------------------------------------------

    /// <summary>
    /// Dense live-oak and cedar groves (each one MultiMesh instance: a clump of a few crowns, radius 4-8 m): along the
    /// draws and creek lines (where the ground is hollow), as treelines on straight property boundaries, and along the
    /// county roads outside the track. Clear of everything placed before and of the trees; in random order.
    /// </summary>
    private static List<Prop> PlaceGroves(World w, Random rng)
    {
        var g = w.Site.Ground;
        var groves = new List<Prop>();
        void Try(Vector2 at, int variant)
        {
            var scale = (float)(4 + (rng.NextDouble() * 4));
            var yaw = (float)(rng.NextDouble() * Math.PI * 2);
            var shade = (float)rng.NextDouble();
            var fp = new Obb(at, Shapes.Dir(-yaw), scale, scale);
            if (groves.Count >= GroveCount || !g.Contains(at) || !w.Free(fp, track: 10, other: 3, tree: 0))
                return;
            if (groves.Any(o => Vector2.Distance(o.Footprint.Centre, at) < 0.7f * (o.Scale + scale)))
                return;
            groves.Add(new Prop(variant, new Vector3(at.X, g.Height(at.X, at.Y), at.Y), yaw, scale, shade, fp));
        }

        // Creek lines: candidates anywhere, kept where the ground lies well below its surroundings.
        for (var k = 0; k < 40_000 && groves.Count < GroveCount * 0.45; k++)
        {
            var at = new Vector2(g.X0 + 20 + (float)(rng.NextDouble() * (g.X1 - g.X0 - 40)), g.Z0 + 20 + (float)(rng.NextDouble() * (g.Z1 - g.Z0 - 40)));
            var hollow = Hollow(g, at);
            if (rng.NextDouble() < Math.Clamp((hollow - 0.8) / 2.5, 0, 1))
                Try(at, rng.NextDouble() < 0.7 ? 0 : 1);
        }
        // Property boundaries: straight treelines running roughly north-south or east-west.
        for (var line = 0; line < 22; line++)
        {
            var centre = new Vector2(g.X0 + (float)(rng.NextDouble() * (g.X1 - g.X0)), g.Z0 + (float)(rng.NextDouble() * (g.Z1 - g.Z0)));
            var dir = Shapes.Dir((rng.Next(2) * Math.PI / 2) + ((rng.NextDouble() - 0.5) * 0.12));
            var half = 120 + (float)(rng.NextDouble() * 330);
            var variant = rng.NextDouble() < 0.5 ? 0 : 1;
            for (var t = -half; t <= half; t += 9 + (float)(rng.NextDouble() * 5))
                Try(centre + (dir * t) + (new Vector2(-dir.Y, dir.X) * (float)((rng.NextDouble() - 0.5) * 5)), variant);
        }
        // Along the county roads, on one side, away from the track.
        foreach (var run in w.RoadsOutside(80))
        {
            var (a, b) = (run[0], run[1]);
            var len = Vector2.Distance(a, b);
            if (len < 1)
                continue;
            var dir = (b - a) / len;
            for (var u = 0f; u < len; u += 12 + (float)(rng.NextDouble() * 5))
                Try(a + (dir * u) + (new Vector2(-dir.Y, dir.X) * (12 + (float)(rng.NextDouble() * 4))), 0);
        }
        Shuffle(groves, rng);
        return groves;
    }

    /// <summary>How far (metres) the ground at <paramref name="p"/> lies below the mean of a 90 m ring round it.</summary>
    public static float Hollow(TerrainSampler g, Vector2 p)
    {
        float sum = 0;
        for (var k = 0; k < 8; k++)
        {
            var q = p + (Shapes.Dir(k * Math.PI / 4) * 90);
            sum += g.Height(q.X, q.Y);
        }
        return (sum / 8) - g.Height(p.X, p.Y);
    }

    /// <summary>
    /// Distant tree clumps out to the horizon: clusters on the far land (and on the terrain's outer reaches, clear of
    /// everything there), none in the line of sight to the skyline. Each is one low-poly dome, 8-16 m across.
    /// </summary>
    private static List<Prop> PlaceFarClumps(World w, Random rng)
    {
        var g = w.Site.Ground;
        var horizon = new SurroundHorizon(g, w.Site.Samples);
        var centre = new Vector2((g.X0 + g.X1) / 2, (g.Z0 + g.Z1) / 2);
        var clumps = new List<Prop>();
        for (var tries = 0; tries < FarClumpCount * 4 && clumps.Count < FarClumpCount; tries++)
        {
            // Cluster centres anywhere past 450 m from the track, denser nearer (where they are seen), out to 5.5 km
            // past the terrain's edge: woods of 4-20 clumps.
            var c = new Vector2(g.X0 + (float)(rng.NextDouble() * (g.X1 - g.X0)), g.Z0 + (float)(rng.NextDouble() * (g.Z1 - g.Z0)));
            c += Shapes.Dir(rng.NextDouble() * Math.PI * 2) * (float)(5500 * Math.Pow(rng.NextDouble(), 1.8));
            var beyond = horizon.Beyond(c);
            if (beyond > 5500 || (beyond <= 0 && w.Track(c, 470).Distance < 450))
                continue;
            var rel = c - centre;
            if (beyond > 1500 && Vector2.Dot(Vector2.Normalize(rel), Surroundings.AustinDirection) > 0.985f)
                continue;
            var size = 4 + rng.Next(17);
            var spread = 40 + (float)(rng.NextDouble() * 110);
            for (var j = 0; j < size && clumps.Count < FarClumpCount; j++)
            {
                var at = c + new Vector2((float)((rng.NextDouble() - 0.5) * spread), (float)((rng.NextDouble() - 0.5) * spread));
                var scale = (float)(4 + (rng.NextDouble() * 4));
                var yaw = (float)(rng.NextDouble() * Math.PI * 2);
                var fp = new Obb(at, Shapes.Dir(-yaw), scale, scale);
                float y;
                if (horizon.Beyond(at) <= 0)
                {
                    if (w.Track(at, 470).Distance < 450 || !w.Free(fp, track: 20, other: 3, tree: 0))
                        continue;
                    y = g.Height(at.X, at.Y);
                }
                else
                {
                    y = horizon.Height(at);
                }
                clumps.Add(new Prop(0, new Vector3(at.X, y, at.Y), yaw, scale, (float)rng.NextDouble(), fp));
            }
        }
        Shuffle(clumps, rng);
        return clumps;
    }

    // ---- shrubs ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Shrub clumps: most at the edges of the tree clusters (where scrub grows in the Texas fields), the rest in loose
    /// clumps out in the grass; none on anything placed before, none inside a tree's crown, none on a road.
    /// </summary>
    private static List<Prop> PlaceShrubs(World w, Random rng, int count)
    {
        var q = w.Site.Samples;
        var trees = w.Site.Trees;
        var shrubs = new List<Prop>(count);
        var spacing = new Dictionary<(int, int), List<(Vector2 P, float R)>>();
        for (var tries = 0; tries < count * 30 && shrubs.Count < count; tries++)
        {
            Vector2 centre;
            if (trees.Count > 0 && rng.NextDouble() < 0.6)
            {
                var t = trees[rng.Next(trees.Count)];
                centre = t.Centre + (Shapes.Dir(rng.NextDouble() * Math.PI * 2) * (t.Radius + 2 + (float)(rng.NextDouble() * 6)));
            }
            else
            {
                var s = q[rng.Next(q.Count)];
                var side = rng.Next(2) == 0 ? -1 : 1;
                var reach = TrackClearance(s.S) + 6 + (420 * Math.Pow(rng.NextDouble(), 1.6));
                centre = s.Position + (s.Left * (float)(side * reach));
            }
            var size = 2 + rng.Next(7);
            for (var j = 0; j < size && shrubs.Count < count; j++)
            {
                var p = centre + new Vector2((float)((rng.NextDouble() - 0.5) * 14), (float)((rng.NextDouble() - 0.5) * 14));
                var variant = rng.NextDouble() switch { < 0.5 => 0, < 0.8 => 1, _ => 2 };
                var scale = (float)(0.7 + (rng.NextDouble() * 1.1));
                var yaw = (float)(rng.NextDouble() * Math.PI * 2);
                var shade = (float)rng.NextDouble();
                var r = ShrubRadius(variant) * scale;
                var fp = new Obb(p, Shapes.Dir(-yaw), r, r);
                if (!w.Site.Ground.Contains(p) || !w.Free(fp, track: 2, other: 1.5f, tree: 0.3f))
                    continue;
                var key = ((int)MathF.Floor(p.X / 8), (int)MathF.Floor(p.Y / 8));
                var close = false;
                for (var dx = -1; dx <= 1 && !close; dx++)
                    for (var dz = -1; dz <= 1 && !close; dz++)
                        if (spacing.TryGetValue((key.Item1 + dx, key.Item2 + dz), out var near))
                            close = near.Any(n => Vector2.Distance(n.P, p) < 0.8f * (n.R + r));
                if (close)
                    continue;
                if (!spacing.TryGetValue(key, out var list))
                    spacing[key] = list = [];
                list.Add((p, r));
                shrubs.Add(new Prop(variant, new Vector3(p.X, w.Site.Ground.Height(p.X, p.Y), p.Y), yaw, scale, shade, fp));
            }
        }
        return shrubs;
    }

    /// <summary>Unit-scale footprint radius of each shrub variant (round bush, juniper, low spreading clump).</summary>
    public static float ShrubRadius(int variant) => variant switch { 0 => 1.0f, 1 => 0.8f, _ => 1.3f };

    // ---- skyline --------------------------------------------------------------------------------------------------

    private static SkylineSite Skyline(SurroundingsSite site)
    {
        var q = site.Samples;
        var centre = new Vector2(q.Average(t => t.Position.X), q.Average(t => t.Position.Y));
        var at = centre + (AustinDirection * SkylineDistance);
        var y = new SurroundHorizon(site.Ground, q).Height(at) - 3;
        return new SkylineSite(new Vector3(at.X, y, at.Y), -AustinDirection, SkylineDistance / AustinDistance);
    }

    // ---- helpers --------------------------------------------------------------------------------------------------

    private static double LapGap(double a, double b, double lap)
    {
        var d = Math.Abs(a - b) % lap;
        return Math.Min(d, lap - d);
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Everything already standing, with the overlap test: the track's clearance band, the pit zone, landmarks, trees,
    /// the plaza complex, boxes placed so far and roads.
    /// </summary>
    internal sealed class World
    {
        private const float Bucket = 32;
        private readonly Dictionary<(int, int), List<int>> _samples = new();
        private readonly Dictionary<(int, int), List<KeepOut>> _trees = new();
        private readonly List<KeepOut> _landmarks = [];
        private readonly List<Obb> _pit = [];
        private readonly List<Obb> _boxes = [];
        private readonly Dictionary<(int, int), List<(Vector2 A, Vector2 B, float Half)>> _roads = new();
        private readonly Vector2[] _polygon;
        private KeepOut? _tower;
        private readonly List<Obb> _plaza = [];

        public World(SurroundingsSite site)
        {
            Site = site;
            var q = site.Samples;
            for (var i = 0; i < q.Count; i++)
                Bin(_samples, q[i].Position).Add(i);
            foreach (var t in site.Trees)
                Bin(_trees, t.Centre).Add(t);
            foreach (var l in site.Landmarks)
            {
                if (Vector2.Distance(l.Centre, site.Tower) < 2)
                    _tower = l;
                else
                    _landmarks.Add(l);
            }
            _tower ??= new KeepOut(site.Tower, TowerStructure);
            for (var s = site.PitS - PitZoneBefore; s < site.PitS + PitZoneAfter; s += 20)
            {
                var a = TrackMeshBuilder.TrackPoint(q, s, (PitZoneInner + PitZoneOuter) / 2);
                var b = TrackMeshBuilder.TrackPoint(q, s + 20, (PitZoneInner + PitZoneOuter) / 2);
                var a2 = new Vector2(a.X, a.Z);
                var b2 = new Vector2(b.X, b.Z);
                _pit.Add(new Obb((a2 + b2) / 2, Vector2.Normalize(b2 - a2), 11, (PitZoneOuter - PitZoneInner) / 2));
            }
            _polygon = [.. q.Select(t => t.Position)];
        }

        public SurroundingsSite Site { get; }

        public bool Infield(Vector2 p) => Shapes.Inside(_polygon, p);

        public void AddBox(Obb b) => _boxes.Add(b);

        public void AddTree(KeepOut t) => Bin(_trees, t.Centre).Add(t);

        /// <summary>The roads' centre lines outside the infield, the parts at least <paramref name="clear"/> m beyond the track's clearance.</summary>
        public IEnumerable<List<Vector2>> RoadsOutside(float clear)
        {
            var seen = new HashSet<(Vector2, Vector2)>();
            var runs = new List<List<Vector2>>();
            foreach (var (a, b, _) in _roads.Values.SelectMany(l => l))
            {
                if (!seen.Add((a, b)) || Infield(a))
                    continue;
                var (d, c) = Track(a, clear + 60);
                if (d < c + clear)
                    continue;
                runs.Add([a, b]);
            }
            return runs;
        }

        /// <summary>The trees still standing.</summary>
        public IReadOnlyList<KeepOut> Trees => [.. _trees.Values.SelectMany(t => t)];

        /// <summary>Indices (in the site's list) of the trees whose trunk stands in <paramref name="box"/>.</summary>
        public HashSet<int> TreesIn(Obb box)
        {
            var hits = new HashSet<int>();
            for (var i = 0; i < Site.Trees.Count; i++)
                if (box.Contains(Site.Trees[i].Centre))
                    hits.Add(i);
            return hits;
        }

        /// <summary>Removes the trees whose trunk stands in any of <paramref name="clearings"/>.</summary>
        public void Clear(IReadOnlyList<KeepOut> clearings)
        {
            foreach (var key in _trees.Keys.ToList())
                _trees[key] = _trees[key].Where(t => !clearings.Any(c => Vector2.Distance(c.Centre, t.Centre) < c.Radius)).ToList();
        }

        public void AddRoad(Road r)
        {
            for (var i = 0; i + 1 < r.Points.Count; i++)
            {
                var (a, b, half) = (r.Points[i], r.Points[i + 1], r.Width / 2);
                // In every bucket the segment's box touches.
                for (var x = (int)MathF.Floor((MathF.Min(a.X, b.X) - half) / Bucket); x <= (int)MathF.Floor((MathF.Max(a.X, b.X) + half) / Bucket); x++)
                    for (var z = (int)MathF.Floor((MathF.Min(a.Y, b.Y) - half) / Bucket); z <= (int)MathF.Floor((MathF.Max(a.Y, b.Y) + half) / Bucket); z++)
                    {
                        if (!_roads.TryGetValue((x, z), out var list))
                            _roads[(x, z)] = list = [];
                        list.Add((a, b, half));
                    }
            }
        }

        public void AddPlaza(GrandPlaza p)
        {
            _plaza.Add(p.Promenade);
            _plaza.Add(p.Amphitheatre);
            _plaza.Add(new Obb(p.Tower, p.AmphitheatreAxis, p.ApronRadius, p.ApronRadius));
        }

        /// <summary>Lowest and highest ground under a box (4 m samples).</summary>
        public (float Lo, float Hi) HeightRange(Obb b)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (var u = -b.HalfLength; u <= b.HalfLength + 0.01f; u += 4)
                for (var v = -b.HalfWidth; v <= b.HalfWidth + 0.01f; v += 4)
                {
                    var p = b.At(u, v);
                    var h = Site.Ground.Height(p.X, p.Y);
                    lo = MathF.Min(lo, h);
                    hi = MathF.Max(hi, h);
                }
            return (lo, hi);
        }

        /// <summary>Distance from <paramref name="p"/> to the centre line and the clearance there.</summary>
        public (float Distance, float Clearance) Track(Vector2 p, float reach)
        {
            var best = float.MaxValue;
            var clearance = 0f;
            foreach (var i in Near(_samples, p, reach))
            {
                var t = Site.Samples[i];
                var d = Vector2.Distance(t.Position, p) - (float)TrackClearance(t.S);
                if (d < best)
                {
                    best = d;
                    clearance = (float)TrackClearance(t.S);
                }
            }
            return (best == float.MaxValue ? float.MaxValue : best + clearance, clearance);
        }

        /// <summary>
        /// True when <paramref name="b"/> keeps <paramref name="track"/> metres outside the track's clearance band (plus
        /// one for the 2 m sampling), <paramref name="tree"/> from every crown and <paramref name="other"/> from everything
        /// else. The plaza complex may stand inside the tower's footprint (<paramref name="ignoreTower"/>: only its
        /// structure counts); props on the plaza ignore the plaza itself (<paramref name="ignorePlaza"/>).
        /// </summary>
        public bool Free(Obb b, float track, float other, float tree, bool ignoreTower = false, bool ignorePlaza = false)
        {
            var reach = b.Reach + 60 + track;
            foreach (var i in Near(_samples, b.Centre, reach))
            {
                var t = Site.Samples[i];
                if (b.Distance(t.Position) < TrackClearance(t.S) + track + 1)
                    return false;
            }
            foreach (var p in _pit)
                if (Vector2.Distance(p.Centre, b.Centre) < p.Reach + b.Reach + other && p.Overlaps(b, other))
                    return false;
            foreach (var l in _landmarks)
                if (b.Distance(l.Centre) < l.Radius + other)
                    return false;
            var towerRadius = ignoreTower ? TowerStructure : _tower!.Value.Radius;
            if (b.Distance(Site.Tower) < towerRadius + (ignoreTower ? 0 : other))
                return false;
            if (!float.IsNaN(tree))
                foreach (var t in Near(_trees, b.Centre, b.Reach + 12 + tree))
                    if (b.Distance(t.Centre) < t.Radius + tree)
                        return false;
            if (!ignorePlaza)
                foreach (var p in _plaza)
                    if (p.Overlaps(b, other))
                        return false;
            foreach (var x in _boxes)
                if ((x.Centre - b.Centre).Length() < x.Reach + b.Reach + other && x.Overlaps(b, other))
                    return false;
            foreach (var (a, c, half) in Near(_roads, b.Centre, b.Reach + other + 1))
                if (b.Distance(a, c) < half + other)
                    return false;
            return true;
        }

        /// <summary>The same test for a road segment of half width <paramref name="half"/> (lots and the plaza are its ends).</summary>
        public bool SegmentFree(Vector2 a, Vector2 c, float half)
        {
            var len = Vector2.Distance(a, c);
            var steps = Math.Max(1, (int)MathF.Ceiling(len / 2));
            for (var k = 0; k <= steps; k++)
            {
                var p = Vector2.Lerp(a, c, k / (float)steps);
                if (!PointFree(p, half))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Marks every cell of a grid (cell centres at x0 + c·cell, z0 + r·cell) that lies within <paramref name="pad"/>
        /// of anything <see cref="PointFree"/> checks: the same test, rasterised shape by shape.
        /// </summary>
        public void Stamp(bool[] blocked, int cols, int rows, float x0, float z0, float cell, float pad)
        {
            void Shape(Vector2 centre, float reach, Func<Vector2, bool> hit)
            {
                var c0 = Math.Max(0, (int)MathF.Floor((centre.X - reach - x0) / cell));
                var c1 = Math.Min(cols - 1, (int)MathF.Ceiling((centre.X + reach - x0) / cell));
                var r0 = Math.Max(0, (int)MathF.Floor((centre.Y - reach - z0) / cell));
                var r1 = Math.Min(rows - 1, (int)MathF.Ceiling((centre.Y + reach - z0) / cell));
                for (var r = r0; r <= r1; r++)
                    for (var c = c0; c <= c1; c++)
                    {
                        var i = (r * cols) + c;
                        if (!blocked[i] && hit(new Vector2(x0 + (c * cell), z0 + (r * cell))))
                            blocked[i] = true;
                    }
            }
            void Circle(Vector2 centre, float radius) => Shape(centre, radius, p => Vector2.DistanceSquared(p, centre) < radius * radius);
            void Box(Obb b, float grow) => Shape(b.Centre, b.Reach + grow, p => b.Distance(p) < grow);

            foreach (var t in Site.Samples)
                Circle(t.Position, (float)TrackClearance(t.S) + pad + 1);
            foreach (var z in _pit)
                Box(z, pad);
            foreach (var l in _landmarks)
                Circle(l.Centre, l.Radius + pad);
            Circle(Site.Tower, _tower!.Value.Radius + pad);
            foreach (var t in _trees.Values.SelectMany(t => t))
                Circle(t.Centre, t.Radius + pad);
            foreach (var x in _plaza)
                Box(x, pad);
            foreach (var x in _boxes)
                Box(x, pad);
        }

        /// <summary>Whether a disc of radius <paramref name="r"/> at <paramref name="p"/> clears everything (roads excluded).</summary>
        public bool PointFree(Vector2 p, float r)
        {
            var (d, c) = Track(p, r + 60);
            if (d < c + r + 1)
                return false;
            foreach (var z in _pit)
                if (z.Distance(p) < r)
                    return false;
            foreach (var l in _landmarks)
                if (Vector2.Distance(p, l.Centre) < l.Radius + r)
                    return false;
            if (Vector2.Distance(p, Site.Tower) < _tower!.Value.Radius + r)
                return false;
            foreach (var t in Near(_trees, p, r + 12))
                if (Vector2.Distance(p, t.Centre) < t.Radius + r)
                    return false;
            foreach (var x in _plaza)
                if (x.Distance(p) < r)
                    return false;
            foreach (var x in _boxes)
                if (x.Distance(p) < r)
                    return false;
            return true;
        }

        private static List<T> Bin<T>(Dictionary<(int, int), List<T>> bins, Vector2 p)
        {
            var key = ((int)MathF.Floor(p.X / Bucket), (int)MathF.Floor(p.Y / Bucket));
            if (!bins.TryGetValue(key, out var list))
                bins[key] = list = [];
            return list;
        }

        private static IEnumerable<T> Near<T>(Dictionary<(int, int), List<T>> bins, Vector2 p, float reach)
        {
            var r = (int)MathF.Ceiling(reach / Bucket);
            var cx = (int)MathF.Floor(p.X / Bucket);
            var cz = (int)MathF.Floor(p.Y / Bucket);
            for (var dx = -r; dx <= r; dx++)
                for (var dz = -r; dz <= r; dz++)
                    if (bins.TryGetValue((cx + dx, cz + dz), out var list))
                        foreach (var x in list)
                            yield return x;
        }
    }
}

/// <summary>Variant indices of the plaza props (<c>surroundings/plaza_props.glb</c> nodes, in this order).</summary>
public static class PlazaModelsIndex
{
    public const int LightPole = 0, Planter = 1, Kiosk = 2, FlagPole = 3;
}

/// <summary>Variant indices of the tents (<c>surroundings/tents.glb</c>).</summary>
public static class TentModelsIndex
{
    public const int Marquee = 0, Pagoda = 1;
}

/// <summary>Variant indices of the parked cars (<c>surroundings/parked_cars.glb</c>).</summary>
public static class CarModelsIndex
{
    public const int Sedan = 0, Suv = 1, Pickup = 2, Hatch = 3;
}
