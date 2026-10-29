using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Redline.Presentation.Course;
using Redline.Presentation.Models;

namespace Redline.Presentation.Tests.Course;

/// <summary>
/// The committed surroundings exports (src/ArcBox.Host/games/redline/models/surroundings) against their contracts, and
/// the triangles the whole of the surroundings draw against the course's budget (spec §3: course additions ≤ 1.5M
/// triangles in total; the surroundings keep under <see cref="SurroundingsBudget"/> of it).
/// </summary>
public class SurroundingsModelTests
{
    /// <summary>The surroundings' share of the course additions' 1.5M triangles.</summary>
    public const int SurroundingsBudget = 750_000;

    private static readonly string ModelsDir = Path.Combine(RepoRoot(), "src", "ArcBox.Host", "games", "redline", "models");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ArcBox.slnx not found above the test output.");
    }

    private static string PathOf(string file) => Path.Combine(ModelsDir, file);

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var c in SurroundingsModels.All)
            data.Add(c.File);
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Glb_has_every_node_and_slot_within_budget(string file)
    {
        var contract = SurroundingsModels.All.Single(c => c.File == file);
        var glb = GlbFile.Load(PathOf(file));
        Assert.Empty(contract.Missing(glb.Nodes.Keys, glb.UsedMaterials));
        Assert.InRange(glb.Triangles, 1, contract.Budget);
        Assert.Empty(glb.ExtensionsRequired);
        foreach (var node in contract.Nodes)
            Assert.NotNull(glb.Nodes[node].Mesh);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Godot_import_file_is_committed_next_to_the_glb(string file)
    {
        var import = File.ReadAllText(PathOf(file) + ".import");
        Assert.Contains($"source_file=\"res://games/redline/models/{file}\"", import, StringComparison.Ordinal);
        Assert.StartsWith($"res://.godot/imported/{Path.GetFileName(file)}-", ModelChoice.ImportedPath(import), StringComparison.Ordinal);
    }

    [Fact]
    public void Variant_order_matches_the_plan()
    {
        Assert.Equal("light_pole", SurroundingsModels.PlazaProps[PlazaModelsIndex.LightPole]);
        Assert.Equal("planter", SurroundingsModels.PlazaProps[PlazaModelsIndex.Planter]);
        Assert.Equal("kiosk", SurroundingsModels.PlazaProps[PlazaModelsIndex.Kiosk]);
        Assert.Equal("flag_pole", SurroundingsModels.PlazaProps[PlazaModelsIndex.FlagPole]);
        Assert.Equal("marquee", SurroundingsModels.Tents[TentModelsIndex.Marquee]);
        Assert.Equal("pagoda", SurroundingsModels.Tents[TentModelsIndex.Pagoda]);
        Assert.Equal(["sedan", "suv", "pickup", "hatch"], SurroundingsModels.Cars);
        Assert.Equal(CarModelsIndex.Pickup, SurroundingsModels.Cars.ToList().IndexOf("pickup"));
        Assert.Equal(3, SurroundingsModels.Shrubs.Count);
        Assert.Equal(["grove_oak", "grove_cedar"], SurroundingsModels.Groves);
    }

    [Fact]
    public void Surroundings_fit_the_course_budget()
    {
        var files = SurroundingsModels.All.Sum(c => GlbFile.Load(PathOf(c.File)).Triangles);
        Assert.InRange(files, 1, SurroundingsModels.TotalBudget);

        var plan = SurroundSite.Cota.Value.Plan;
        long Instanced(ModelContract c, IEnumerable<Prop> props)
        {
            var tris = NodeTriangles(PathOf(c.File));
            return props.Sum(p => (long)tris[c.Nodes[p.Variant]]);
        }
        var props = Instanced(SurroundingsModels.ParkedCars, plan.ParkedCars) + Instanced(SurroundingsModels.Shrub, plan.Shrubs)
            + Instanced(SurroundingsModels.Tent, plan.Tents) + Instanced(SurroundingsModels.Plaza, plan.PlazaProps)
            + Instanced(SurroundingsModels.Grove, plan.Groves) + Instanced(SurroundingsModels.FarClump, plan.FarClumps);
        var site = SurroundSite.Cota.Value;
        var surfaces = SurroundMeshes.Build(plan, site.Ground);
        var draped = (surfaces.Roads.Indices.Count + surfaces.Lots.Indices.Count + surfaces.Plaza.Indices.Count) / 3;
        var far = new SurroundHorizon(site.Ground, site.Samples).Build().Indices.Count / 3;
        var single = GlbFile.Load(PathOf(SurroundingsModels.Amphitheatre.File)).Triangles + GlbFile.Load(PathOf(SurroundingsModels.Skyline.File)).Triangles;
        var total = props + draped + far + single;
        Assert.InRange(total, 1, SurroundingsBudget);
        TestContext.Current.SendDiagnosticMessage($"surroundings draw {total} triangles ({props} instanced, {draped} draped, {far} far land, {single} amphitheatre + skyline)");

        var models = Directory.EnumerateFiles(ModelsDir, "*.glb", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        Assert.InRange(models, 1, 40L * 1024 * 1024);
    }

    /// <summary>Triangles of each named node's mesh (indexed primitives).</summary>
    private static Dictionary<string, int> NodeTriangles(string path)
    {
        var data = File.ReadAllBytes(path);
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(data, 20, length));
        var root = doc.RootElement;
        var accessors = root.GetProperty("accessors");
        var meshes = root.GetProperty("meshes").EnumerateArray().Select(m => m.GetProperty("primitives").EnumerateArray()
            .Sum(p => accessors[p.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32() / 3)).ToList();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in root.GetProperty("nodes").EnumerateArray())
            if (n.TryGetProperty("mesh", out var mesh) && n.TryGetProperty("name", out var name))
                result[name.GetString()!] = meshes[mesh.GetInt32()];
        return result;
    }
}
