using System.Collections.Concurrent;
using System.Diagnostics;
using Redline.Logic;
using Redline.Presentation;
using Redline.Presentation.Meshes;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>A car's meshes with the wheels split off so they can spin.</summary>
public sealed record CarParts(
    Seat Seat,
    CarMeshes Meshes,
    IReadOnlyList<MeshData> TyresPerWheel,
    IReadOnlyList<MeshData> RimsPerWheel,
    MeshData RimsOther);

/// <summary>Everything about one circuit that is built from the pure-C# builders.</summary>
public sealed record CircuitAssets(
    CircuitData Circuit,
    IReadOnlyList<TrackSample> Samples,
    TrackMeshes Track,
    MeshData Terrain,
    TerrainSampler Ground);

/// <summary>
/// Static cache of the pure-C# geometry (track, terrain, 22 cars). The spec's <c>@tool</c> bake is replaced by building at
/// scene load from the same builders, in parallel on the thread pool, once per process: reloading the scene (the next race)
/// reuses everything. <see cref="Prewarm"/> can start the work early, off the main thread.
/// </summary>
public static class RedlineAssets
{
    private static readonly ConcurrentDictionary<string, Lazy<CircuitAssets>> Circuits = new(StringComparer.Ordinal);
    private static readonly Lazy<IReadOnlyDictionary<string, CarParts>> CarCache = new(BuildCars);

    /// <summary>Milliseconds the last cold build of each part took, for the load-time log line.</summary>
    public static ConcurrentDictionary<string, long> Timings { get; } = new(StringComparer.Ordinal);

    public static CircuitAssets Circuit(string id) =>
        Circuits.GetOrAdd(id, key => new Lazy<CircuitAssets>(() => BuildCircuit(key))).Value;

    public static IReadOnlyDictionary<string, CarParts> Cars => CarCache.Value;

    private static readonly ConcurrentDictionary<string, Task> Prewarms = new(StringComparer.Ordinal);

    /// <summary>
    /// Starts building the circuit on the thread pool and loading the Blender car in the background (once per circuit);
    /// returns at once. The procedural cars are only built ahead when the Blender car isn't there (otherwise
    /// <see cref="Cars"/> still builds them on first use, should the model fail to load).
    /// </summary>
    public static Task Prewarm(string circuitId) =>
        Prewarms.GetOrAdd(circuitId, id =>
        {
            ArcBox.Host.Games.Redline.Cars.CarFactory.Prewarm();
            SetPieces.Prewarm();
            var procedural = !ArcBox.Host.Games.Redline.Cars.CarFactory.ModelLikelyAvailable;
            return Task.WhenAll(Task.Run(() => Circuit(id)), procedural ? Task.Run(() => Cars) : Task.CompletedTask);
        });

    /// <summary>Every driver code on the grid, in grid order.</summary>
    public static IReadOnlyList<string> Codes { get; } = [.. Grid2026.Load().Seats.Select(s => s.Driver.Code)];

    private static CircuitAssets BuildCircuit(string id)
    {
        var sw = Stopwatch.StartNew();
        var circuit = CircuitData.Load(id);
        var samples = TrackGeometry.Resample(circuit);
        var track = Task.Run(() => TrackMeshBuilder.Build(circuit));
        var terrain = Task.Run(() => TerrainBuilder.Build(circuit, samples));
        Task.WaitAll(track, terrain);
        var result = new CircuitAssets(circuit, samples, track.Result, terrain.Result, TerrainSampler.FromGrid(terrain.Result));
        Timings[$"circuit:{id}"] = sw.ElapsedMilliseconds;
        return result;
    }

    private static IReadOnlyDictionary<string, CarParts> BuildCars()
    {
        var sw = Stopwatch.StartNew();
        var seats = Grid2026.Load().Seats;
        var parts = new CarParts[seats.Count];
        Parallel.For(0, seats.Count, i =>
        {
            var meshes = CarMeshBuilder.Build(seats[i]);
            var (tyres, _) = WheelSplit.Split(meshes.Tyres, meshes.WheelCentres);
            var (rims, other) = WheelSplit.Split(meshes.Rims, meshes.WheelCentres);
            parts[i] = new CarParts(seats[i], meshes, tyres, rims, other);
        });
        Timings["cars"] = sw.ElapsedMilliseconds;
        return parts.ToDictionary(p => p.Seat.Driver.Code, StringComparer.Ordinal);
    }
}
