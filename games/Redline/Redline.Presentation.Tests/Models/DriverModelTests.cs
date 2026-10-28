using System.Numerics;
using Redline.Logic;
using Redline.Presentation.Ceremony;
using Redline.Presentation.Models;

namespace Redline.Presentation.Tests.Models;

/// <summary>
/// The committed driver figures and helmets (src/ArcBox.Host/games/redline/models/{drivers,helmets}), checked without
/// Blender or Godot: every grid driver has both, with their naming contracts, budgets and rig.
/// </summary>
public class DriverModelTests
{
    private static readonly string ModelsDir = Path.Combine(RepoRoot(), "src", "ArcBox.Host", "games", "redline", "models");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ArcBox.slnx not found above the test output.");
    }

    public static TheoryData<string> Codes()
    {
        var data = new TheoryData<string>();
        foreach (var seat in Grid2026.Load().Seats)
            data.Add(seat.Driver.Code);
        return data;
    }

    private static GlbFile Load(string file) => GlbFile.Load(Path.Combine(ModelsDir, file));

    [Theory]
    [MemberData(nameof(Codes))]
    public void Driver_glb_has_every_node_and_slot_within_budget(string code)
    {
        var glb = Load(DriverModel.File(code));

        Assert.Empty(DriverModel.Contract(code).Missing(glb.Nodes.Keys, glb.UsedMaterials));
        Assert.InRange(glb.Triangles, DriverModel.Budget / 4, DriverModel.Budget);
        Assert.Empty(glb.ExtensionsRequired);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void Helmet_glb_has_every_slot_within_budget(string code)
    {
        var glb = Load(HelmetModel.File(code));

        Assert.Empty(HelmetModel.Contract(code).Missing(glb.Nodes.Keys, glb.UsedMaterials));
        Assert.InRange(glb.Triangles, HelmetModel.Budget / 4, HelmetModel.Budget);
        Assert.Empty(glb.ExtensionsRequired);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void The_arm_chain_carries_the_bottle_in_the_right_hand(string code)
    {
        var n = Load(DriverModel.File(code)).Nodes;

        Assert.Contains(DriverModel.ArmR, n[DriverModel.Spine].Children);
        Assert.Contains(DriverModel.ForearmR, n[DriverModel.ArmR].Children);
        Assert.Contains(DriverModel.HandR, n[DriverModel.ForearmR].Children);
        Assert.Contains(DriverModel.Bottle, n[DriverModel.HandR].Children);
        Assert.Contains(DriverModel.ForearmL, n[DriverModel.ArmL].Children);
        Assert.Contains(DriverModel.HandL, n[DriverModel.ForearmL].Children);
        Assert.Contains(DriverModel.NumberCap, n[DriverModel.Cap].Children);
        // Rest rotations are the identity (Godot poses the arm by rotating about each joint).
        foreach (var name in new[] { DriverModel.ArmR, DriverModel.ForearmR, DriverModel.HandR, DriverModel.Bottle })
            Assert.True(Quaternion.Dot(n[name].Rotation, Quaternion.Identity) > 0.9999f, $"{name} must not be rotated");
        // The right arm is on the figure's right (-X); the elbow is bent; the bottle is in front of the body.
        Assert.True(n[DriverModel.ArmR].Translation.X < -0.1f);
        Assert.True(n[DriverModel.ArmL].Translation.X > 0.1f);
        var upper = n[DriverModel.ForearmR].Translation.Length();
        var fore = n[DriverModel.HandR].Translation.Length();
        Assert.InRange(upper, 0.25f, 0.4f);
        Assert.InRange(fore, 0.2f, 0.35f);
        var bottleZ = n[DriverModel.Spine].Translation.Z + n[DriverModel.ArmR].Translation.Z + n[DriverModel.ForearmR].Translation.Z
                      + n[DriverModel.HandR].Translation.Z + n[DriverModel.Bottle].Translation.Z;
        Assert.InRange(bottleZ, 0.12f, 0.35f);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void Label_empties_face_out_of_the_suit_and_cap(string code)
    {
        var n = Load(DriverModel.File(code)).Nodes;
        foreach (var name in new[] { DriverModel.NumberSuit, DriverModel.NameSuit })
        {
            Assert.True(n[name].Axis(Vector3.UnitZ).Z > 0.7f, $"{name} faces forward");
            Assert.True(n[name].Axis(Vector3.UnitY).Y > 0.7f, $"{name} reads upright");
        }
        Assert.True(n[DriverModel.NumberSuit].Translation.X < 0 && n[DriverModel.NameSuit].Translation.X > 0,
            "number on the right chest, surname on the left");
    }

    [Fact]
    public void Driver_and_helmet_glbs_fit_the_repo_size_budget()
    {
        var codes = Grid2026.Load().Seats.Select(s => s.Driver.Code).ToList();
        var bytes = codes.Sum(c => new FileInfo(Path.Combine(ModelsDir, DriverModel.File(c))).Length
                                   + new FileInfo(Path.Combine(ModelsDir, HelmetModel.File(c))).Length);
        Assert.InRange(bytes, 1, 24L * 1024 * 1024);
        var all = Directory.EnumerateFiles(ModelsDir, "*.glb", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        Assert.InRange(all, 1, 40L * 1024 * 1024);
    }

    [Fact]
    public void Godot_import_files_are_committed_next_to_the_figures_and_helmets()
    {
        foreach (var seat in Grid2026.Load().Seats)
        {
            foreach (var file in new[] { DriverModel.File(seat.Driver.Code), HelmetModel.File(seat.Driver.Code) })
            {
                var import = File.ReadAllText(Path.Combine(ModelsDir, file + ".import"));
                Assert.Contains($"source_file=\"res://games/redline/models/{file}\"", import, StringComparison.Ordinal);
                Assert.NotNull(ModelChoice.ImportedPath(import));
            }
        }
    }

    [Fact]
    public void Bottle_mouth_matches_the_procedural_bottle()
    {
        Assert.Equal(BottleMesh.Height, DriverModel.BottleMouth.Y);
    }

    [Fact]
    public void Every_team_has_a_two_tone_suit()
    {
        foreach (var team in Grid2026.Load().Teams)
        {
            var secondary = DriverModel.Secondary(team.Livery);
            Assert.NotEqual(team.Livery.Top, secondary, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(team.Livery.Top, DriverModel.TeamColour(DriverModel.SuitPrimary, team.Livery, "#123456"));
            Assert.Equal(secondary, DriverModel.TeamColour(DriverModel.Belt, team.Livery, "#123456"));
            Assert.Equal("#123456", DriverModel.TeamColour(DriverModel.BottleFoil, team.Livery, "#123456"));
            Assert.Null(DriverModel.TeamColour(DriverModel.Skin, team.Livery, "#123456"));
        }
    }

    [Fact]
    public void A_figure_missing_its_spray_arm_goes_procedural()
    {
        var nodes = DriverModel.Nodes.Where(n => n != DriverModel.ArmR).ToList();
        Assert.Equal(["arm_r"], DriverModel.Contract("NOR").Missing(nodes, DriverModel.Slots));
        Assert.Equal(["material visor"], HelmetModel.Contract("NOR").Missing([HelmetModel.Mesh], ["helmet_base", "helmet_a", "helmet_b"]));
    }

    [Theory]
    [InlineData("Lando Norris", "NORRIS")]
    [InlineData("Nico Hülkenberg", "HÜLKENBERG")]
    [InlineData("Sergio Pérez", "PÉREZ")]
    public void Surname_is_the_last_name_in_capitals(string name, string want) => Assert.Equal(want, DriverModel.Surname(name));
}
