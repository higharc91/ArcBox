using System.Numerics;
using Redline.Logic;
using Redline.Presentation.Meshes;
using Redline.Presentation.Models;

namespace Redline.Presentation.Tests.Models;

/// <summary>
/// Checks the committed Blender exports (src/ArcBox.Host/games/redline/models) without Blender or Godot: a broken or
/// renamed export fails here before the TV quietly falls back to the procedural car.
/// </summary>
public class BlenderModelTests
{
    private static readonly string ModelsDir = Path.Combine(RepoRoot(), "src", "ArcBox.Host", "games", "redline", "models");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ArcBox.slnx not found above the test output.");
    }

    private static GlbFile Load(string file) => GlbFile.Load(Path.Combine(ModelsDir, file));

    [Theory]
    [InlineData(CarModel.Lod0File, false, CarModel.Lod0Budget)]
    [InlineData(CarModel.Lod1File, true, CarModel.Lod1Budget)]
    public void Car_glb_has_every_node_and_material_slot_within_budget(string file, bool lod1, int budget)
    {
        var glb = Load(file);

        Assert.Empty(CarModel.Missing(glb.Nodes.Keys, glb.UsedMaterials, lod1));
        Assert.InRange(glb.Triangles, budget / 10, budget);
        Assert.Empty(glb.ExtensionsRequired);
    }

    [Fact]
    public void Lod1_is_much_lighter_than_lod0()
    {
        Assert.True(Load(CarModel.Lod1File).Triangles * 3 < Load(CarModel.Lod0File).Triangles);
    }

    [Fact]
    public void Car_glbs_fit_the_repo_size_budget()
    {
        var bytes = new[] { CarModel.Lod0File, CarModel.Lod1File }.Sum(f => new FileInfo(Path.Combine(ModelsDir, f)).Length);
        Assert.InRange(bytes, 1, 8L * 1024 * 1024);
    }

    [Theory]
    [InlineData(CarModel.Lod0File)]
    [InlineData(CarModel.Lod1File)]
    public void Wheels_sit_where_the_procedural_car_has_them(string file)
    {
        var glb = Load(file);
        var centres = CarMeshBuilder.Build(Grid2026.Load().ByCode("NOR")).WheelCentres;

        for (var w = 0; w < CarModel.Wheels.Count; w++)
        {
            var node = glb.Nodes[CarModel.Wheels[w]];
            Assert.True(Vector3.Distance(node.Translation, centres[w]) < 0.001f, $"{node.Name} at {node.Translation}, want {centres[w]}");
            Assert.True(Quaternion.Dot(node.Rotation, Quaternion.Identity) > 0.9999f, $"{node.Name} must not be rotated");
        }
    }

    [Fact]
    public void Number_empties_face_outwards_with_text_upright()
    {
        var glb = Load(CarModel.Lod0File);
        var nose = glb.Nodes["number_nose"];
        var left = glb.Nodes["number_endplate_l"];
        var right = glb.Nodes["number_endplate_r"];

        Assert.True(nose.Axis(Vector3.UnitZ).Y > 0.9f, "nose number faces up");
        Assert.True(nose.Axis(Vector3.UnitY).Z < -0.9f, "nose number reads from in front (its up points to the cockpit)");
        Assert.InRange(nose.Translation.Z, 1.8f, 2.4f);
        Assert.True(left.Axis(Vector3.UnitZ).X > 0.99f && left.Axis(Vector3.UnitY).Y > 0.99f);
        Assert.True(right.Axis(Vector3.UnitZ).X < -0.99f && right.Axis(Vector3.UnitY).Y > 0.99f);
        Assert.True(left.Translation.X > 0.45f && right.Translation.X < -0.45f && left.Translation.Z < -2f);
        Assert.True(Vector3.Distance(glb.Nodes[CarModel.Helmet].Translation, CarMeshBuilder.HelmetCentre) < 0.001f);
    }

    [Fact]
    public void Exported_paint_is_linear_papaya_not_srgb()
    {
        // #ff8000 is sRGB; glTF stores linear, so green 0x80 = 0.502 sRGB must be ~0.216, not 0.502.
        var c = Load(CarModel.Lod0File).BaseColours[CarModel.PaintTop];

        Assert.Equal(1f, c.X, 0.01f);
        Assert.Equal(0.2159f, c.Y, 0.01f);
        Assert.Equal(0f, c.Z, 0.01f);
    }

    [Fact]
    public void Godot_import_files_are_committed_next_to_the_glbs()
    {
        foreach (var file in new[] { CarModel.Lod0File, CarModel.Lod1File })
        {
            var import = File.ReadAllText(Path.Combine(ModelsDir, file + ".import"));
            Assert.Contains($"source_file=\"res://games/redline/models/{file}\"", import, StringComparison.Ordinal);
            Assert.StartsWith($"res://.godot/imported/{file}-", ModelChoice.ImportedPath(import), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Parse_rejects_something_that_is_not_a_glb()
    {
        Assert.Throws<FormatException>(() => GlbFile.Parse("{\"asset\":{}}"u8));
        Assert.Throws<FormatException>(() => GlbFile.Parse([]));
    }
}
