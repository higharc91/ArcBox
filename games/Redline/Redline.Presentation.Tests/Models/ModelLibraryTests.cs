using Redline.Logic;
using Redline.Presentation.Models;

namespace Redline.Presentation.Tests.Models;

/// <summary>The TV's model fallback rules (plan Review Focus 1) and LOD choice (Review Focus 3), engine-free.</summary>
public class ModelLibraryTests
{
    [Fact]
    public void ModelLibrary_falls_back_when_missing()
    {
        Assert.Equal(ModelSource.Imported, ModelChoice.Decide(importedExists: true, rawExists: true));
        Assert.Equal(ModelSource.Runtime, ModelChoice.Decide(importedExists: false, rawExists: true));
        Assert.Equal(ModelSource.Procedural, ModelChoice.Decide(importedExists: false, rawExists: false));
    }

    [Fact]
    public void A_failed_load_falls_back_one_step_and_a_good_one_stays()
    {
        string[] none = [];
        Assert.Equal(ModelSource.Imported, ModelChoice.After(ModelSource.Imported, loaded: true, none, rawExists: true));
        Assert.Equal(ModelSource.Runtime, ModelChoice.After(ModelSource.Imported, loaded: false, none, rawExists: true));
        Assert.Equal(ModelSource.Procedural, ModelChoice.After(ModelSource.Imported, loaded: false, none, rawExists: false));
        Assert.Equal(ModelSource.Procedural, ModelChoice.After(ModelSource.Runtime, loaded: false, none, rawExists: true));
        Assert.Equal(ModelSource.Runtime, ModelChoice.After(ModelSource.Runtime, loaded: true, none, rawExists: true));
    }

    [Fact]
    public void A_model_missing_part_of_its_contract_goes_procedural()
    {
        Assert.Equal(ModelSource.Procedural, ModelChoice.After(ModelSource.Imported, loaded: true, ["wheel_fl"], rawExists: true));
        Assert.Equal(ModelSource.Procedural, ModelChoice.After(ModelSource.Runtime, loaded: true, ["material paint_top"], rawExists: true));
    }

    [Fact]
    public void Missing_lists_absent_nodes_and_slots()
    {
        var nodes = CarModel.RequiredNodes.Where(n => n != "wheel_rr").ToList();
        var mats = CarModel.Slots.Where(m => m != CarModel.Glass).ToList();

        Assert.Equal(["wheel_rr", "material glass"], CarModel.Missing(nodes, mats));
        Assert.Empty(CarModel.Missing(CarModel.RequiredNodes, CarModel.Slots));
        Assert.Empty(CarModel.Missing(CarModel.RequiredNodes, mats.Where(m => m != CarModel.TyreStripe), lod1: true));
    }

    [Fact]
    public void ImportedPath_reads_the_remap_path()
    {
        const string import = "[remap]\n\nimporter=\"scene\"\ntype=\"PackedScene\"\npath=\"res://.godot/imported/car_2026.glb-abc.scn\"\n\n[deps]\n";

        Assert.Equal("res://.godot/imported/car_2026.glb-abc.scn", ModelChoice.ImportedPath(import));
        Assert.Null(ModelChoice.ImportedPath("[remap]\nimporter=\"keep\"\n"));
    }

    [Theory]
    [InlineData(0f, false, 0)]
    [InlineData(59.9f, false, 0)]
    [InlineData(60f, false, 1)]
    [InlineData(400f, false, 1)]
    [InlineData(0f, true, 1)]
    [InlineData(5f, true, 1)]
    public void Lod1_beyond_60_m_and_always_on_Low(float distance, bool low, int lod)
    {
        Assert.Equal(lod, CarLod.Pick(distance, low));
        var (lod0, lod1) = CarLod.Bands(low);
        Assert.Equal(lod == 0, CarLod.Draws(lod0, distance));
        Assert.Equal(lod == 1, CarLod.Draws(lod1, distance));
    }

    [Fact]
    public void Paint_slots_take_the_livery_from_grid2026()
    {
        var livery = Grid2026.Load().ByCode("NOR").Team.Livery;

        Assert.Equal("#ff8000", CarModel.LiveryColour(CarModel.PaintTop, livery));
        Assert.Equal(livery.Halo, CarModel.LiveryColour(CarModel.Halo, livery));
        Assert.Null(CarModel.LiveryColour(CarModel.Carbon, livery));
        Assert.All(CarModel.PaintSlots, s => Assert.NotNull(CarModel.LiveryColour(s, livery)));
    }
}
