using System.Numerics;
using Redline.Presentation.Ceremony;
using Redline.Presentation.Models;

namespace Redline.Presentation.Tests.Models;

/// <summary>
/// Checks the committed podium and landmark exports (src/ArcBox.Host/games/redline/models) against their contracts
/// without Blender or Godot: names, budgets, and the anchors the TV places things by (crowd tiers, light pods, the
/// podium steps and confetti cannons).
/// </summary>
public class LandmarkModelTests
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

    public static TheoryData<string> Contracts()
    {
        var data = new TheoryData<string>();
        foreach (var c in LandmarkModels.All.Append(PodiumModel.Contract))
            data.Add(c.File);
        return data;
    }

    private static ModelContract ContractFor(string file) => LandmarkModels.All.Append(PodiumModel.Contract).Single(c => c.File == file);

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Glb_has_every_node_and_material_slot_within_budget(string file)
    {
        var contract = ContractFor(file);
        var glb = Load(file);

        Assert.Empty(contract.Missing(glb.Nodes.Keys, glb.UsedMaterials));
        Assert.InRange(glb.Triangles, 1, contract.Budget);
        Assert.Empty(glb.ExtensionsRequired);
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Godot_import_file_is_committed_next_to_the_glb(string file)
    {
        var import = File.ReadAllText(Path.Combine(ModelsDir, file + ".import"));
        Assert.Contains($"source_file=\"res://games/redline/models/{file}\"", import, StringComparison.Ordinal);
        Assert.StartsWith($"res://.godot/imported/{Path.GetFileName(file)}-", ModelChoice.ImportedPath(import), StringComparison.Ordinal);
    }

    [Fact]
    public void Landmarks_and_podium_fit_their_budgets()
    {
        var landmarks = LandmarkModels.All.Sum(c => Load(c.File).Triangles);
        Assert.InRange(landmarks, 1, LandmarkModels.TotalBudget);
        Assert.InRange(Load(PodiumModel.File).Triangles, 1, PodiumModel.Budget);
        var bytes = LandmarkModels.All.Append(PodiumModel.Contract).Sum(c => new FileInfo(Path.Combine(ModelsDir, c.File)).Length);
        Assert.InRange(bytes, 1, 16L * 1024 * 1024);
    }

    [Fact]
    public void Missing_names_nodes_then_materials()
    {
        var missing = LandmarkModels.Tower.Missing(["tower"], ["glass"]);
        Assert.Equal(["material tower_white", "material red", "material steel", "material concrete"], missing);
        Assert.Equal(["tower"], LandmarkModels.Tower.Missing([], LandmarkModels.Tower.Slots));
    }

    public static TheoryData<int> StandIndices() => [0, 1, 2];

    private static GrandstandSpec Stand(int i) => new[] { LandmarkModels.MainStand, LandmarkModels.TurnOneStand, LandmarkModels.CornerStand }[i];

    [Theory]
    [MemberData(nameof(StandIndices))]
    public void Grandstand_tiers_are_where_the_crowd_sits(int index)
    {
        var stand = Stand(index);
        var glb = Load(stand.File);

        AssertNear(GrandstandModel.TreadFront(0), glb.Nodes[GrandstandModel.TierFirst].Translation);
        AssertNear(GrandstandModel.TreadFront(stand.Rows - 1), glb.Nodes[GrandstandModel.TierLast].Translation);
    }

    [Fact]
    public void Tier_contract_matches_the_procedural_stand()
    {
        // The procedural stand's row r: a box whose top is at 1 + 0.9 r over 1.2 r .. 1.2 r + 1.2; crowd every 0.85 m.
        Assert.Equal(1f, GrandstandModel.TreadTop(0));
        Assert.Equal(1f + (0.9f * 21), GrandstandModel.TreadTop(21), 4);
        var xs = GrandstandModel.SeatXs(90);
        Assert.Equal(-44.4f, xs[0], 4);
        Assert.True(xs[^1] < 45 - GrandstandModel.SeatEndClearance);
        Assert.Equal(105, xs.Count);
    }

    [Fact]
    public void Aisles_are_spaced_through_the_stand_and_take_whole_seats()
    {
        Assert.Equal([-32.5f, -7.5f, 17.5f], GrandstandModel.Aisles(90));
        Assert.Equal(12, GrandstandModel.Aisles(300).Count);
        var xs = GrandstandModel.SeatXs(300);
        var inAisle = xs.Count(x => GrandstandModel.InAisle(x, 300));
        Assert.InRange(inAisle, 12, 24);
        Assert.False(GrandstandModel.InAisle(0.6f - 150, 300));
        Assert.True(GrandstandModel.InAisle(-137.5f, 300));
    }

    [Fact]
    public void Start_lights_sit_where_the_procedural_lamps_were()
    {
        var glb = Load(LandmarkModels.StartGantry.File);
        for (var k = 1; k <= 5; k++)
        {
            var node = glb.Nodes[StartGantryModel.Lights[k - 1]];
            AssertNear(StartGantryModel.LightPosition(k), node.Translation);
            Assert.NotNull(node.Mesh);
        }
        Assert.Equal(-2.3f, StartGantryModel.LightPosition(1).X, 4);
        Assert.Equal(10.1f, StartGantryModel.ColumnHalfSpan, 3);
    }

    [Fact]
    public void Podium_steps_and_cannons_match_the_layout()
    {
        var glb = Load(PodiumModel.File);
        for (var step = 1; step <= 3; step++)
            AssertNear(PodiumLayout.StepTopCentre(step), glb.Nodes[PodiumModel.Steps[step - 1]].Translation);

        var left = glb.Nodes[PodiumModel.ConfettiLeft];
        var right = glb.Nodes[PodiumModel.ConfettiRight];
        Assert.True(left.Translation.X < -6 && right.Translation.X > 6, "cannons at the wall's ends");
        Assert.True(left.Translation.Y > PodiumLayout.StageHeight + PodiumLayout.BackdropHeight, "cannons above the wall");
        Assert.True(left.Axis(Vector3.UnitZ).Y > 0.7f && left.Axis(Vector3.UnitZ).X > 0.1f, "left cannon fires up and inwards");
        Assert.True(right.Axis(Vector3.UnitZ).Y > 0.7f && right.Axis(Vector3.UnitZ).X < -0.1f, "right cannon fires up and inwards");
    }

    [Fact]
    public void Pit_wall_stations_sit_between_the_fence_and_the_fast_lane_along_the_building()
    {
        var offsets = PitLaneModel.StationOffsets;
        Assert.Equal(PitLaneModel.StationCount, offsets.Count);
        Assert.All(offsets.Zip(offsets.Skip(1)), p => Assert.True(p.Second - p.First > PitLaneModel.StationLength));
        Assert.True(offsets[0] - (PitLaneModel.StationLength / 2) > -PitLaneModel.BuildingLength / 2);
        Assert.True(offsets[^1] + (PitLaneModel.StationLength / 2) < PitLaneModel.BuildingLength / 2);
        // The wall carries the catch fence (19 m) and the stations end before the fast lane's line.
        Assert.InRange((float)global::Redline.Presentation.Meshes.TrackMeshBuilder.FenceOffset, PitLaneModel.WallInner, PitLaneModel.WallOuter);
        Assert.True(PitLaneModel.WallOuter + PitLaneModel.StationDepth < PitLaneModel.LaneLines[0].From);
        Assert.True(PitLaneModel.LaneLines[^1].To < PitLaneModel.LaneOuter);
    }

    [Fact]
    public void Tree_variants_are_round_robin()
    {
        Assert.Equal([0, 1, 2, 0], Enumerable.Range(0, 4).Select(TreeModel.VariantOf));
        var glb = Load(LandmarkModels.Trees.File);
        foreach (var v in TreeModel.Variants)
        {
            AssertNear(Vector3.Zero, glb.Nodes[v].Translation);
            Assert.NotNull(glb.Nodes[v].Mesh);
        }
    }

    private static void AssertNear(Vector3 want, Vector3 got) =>
        Assert.True(Vector3.Distance(want, got) < 0.002f, $"expected {want}, got {got}");
}
