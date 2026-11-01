using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="GroundGrade"/> and what a world's ground wears: its colour at the ford, and decals that belong on it.</summary>
public sealed class GroundGradeTests
{
    private static readonly string[] Stone = ["cobble/warm", "cobble/grey", "flagstone/warm", "flagstone/grey", "pavers/warm"];

    private static IEnumerable<string> Decals(string material) => Enumerable.Range(0, 40).SelectMany(y => Enumerable.Range(0, 40)
        .Select(x => BoardPlan.DecalFor(new Tile(x, y), material, Pack.Art)?.Path)).OfType<string>();

    [Fact]
    public void Chimneys_dont_all_puff_in_step()
    {
        var phases = Enumerable.Range(0, 8).Select(x => BuildingPlan.SmokePhase(new Tile(x * 3, 40))).Distinct().Count();
        Assert.True(phases > 1);
        var (still, ahead) = (new ArtImage("smoke.png", 8, 8), new ArtImage("smoke.png", 8, 8, Phase: 4));
        Assert.NotEqual(still.Key, ahead.Key);
        Assert.Equal((still.FrameAt(0.5) + 4) % 8, ahead.FrameAt(0.5));
    }

    [Fact]
    public void A_place_with_no_grade_has_its_art_as_drawn() => Assert.Equal(new Rgb(1, 1, 1), GroundGrade.Of("mirewood", "grass/autumn"));

    [Fact]
    public void A_material_the_ford_doesnt_grade_is_as_drawn() => Assert.Equal(new Rgb(1, 1, 1), GroundGrade.Of("ashen_ford", "cobble/warm"));

    [Fact]
    public void The_fords_autumn_loses_red_and_its_stamped_grass_loses_green()
    {
        var autumn = GroundGrade.Of("ashen_ford", "grass/autumn");
        Assert.True(autumn.R < autumn.G);
        var lush = GroundGrade.Of("ashen_ford", "grass/lush");
        Assert.True(lush.G < lush.R && lush.G < lush.B);
    }

    /// <summary>The frost's own pale blue, in the ford's light: it has to come out a dull cold grey, not lavender, which is what the dusk's purple makes of it unaided.</summary>
    [Fact]
    public void Frost_at_the_ford_is_dull_and_cold()
    {
        var art = new Rgb(0.75f, 0.84f, 0.92f);
        var lit = art.Times(GroundGrade.Of("ashen_ford", "grass/frost")).Times(BoardLighting.Dusk);

        Assert.True(lit.B >= lit.R, "It's cold: blue stays over red.");
        Assert.True(lit.G >= lit.R * 0.9f, "It isn't purple.");
        Assert.True(Math.Max(lit.R, Math.Max(lit.G, lit.B)) - Math.Min(lit.R, Math.Min(lit.G, lit.B)) < 0.05f, "It's dull.");
    }

    [Fact]
    public void Frost_hangs_frost_over_its_edge_not_green_grass() => Assert.Equal(new ArtRef("snow_lip", "fresh"), MaterialLook.Of("grass/frost").Lip);

    [Fact]
    public void A_forest_floors_green_is_taken_out_at_the_ford()
    {
        var floor = GroundGrade.Of("ashen_ford", "forest_needles/pine");
        Assert.True(floor.G < floor.R);
        Assert.True(floor.G < 0.75f);
    }

    [Theory]
    [InlineData("cobble/warm")]
    [InlineData("cobble/grey")]
    [InlineData("flagstone/warm")]
    [InlineData("grass/dry")]
    [InlineData("dirt/brown")]
    [InlineData("path/packed")]
    [InlineData("ash/grey")]
    [InlineData("rock/grey")]
    public void Nothing_dry_or_laid_has_a_puddle(string material)
        => Assert.DoesNotContain(Decals(material), p => p.Contains("puddle", StringComparison.Ordinal));

    [Fact]
    public void Stone_stays_clean_but_for_a_stray_pebble()
    {
        foreach (var material in Stone)
        {
            var decals = Decals(material).ToList();
            Assert.All(decals, p => Assert.Contains("pebbles", p, StringComparison.Ordinal));
            Assert.True(decals.Count < 40 * 40 / 24, $"{material} has {decals.Count}.");
        }
    }

    [Fact]
    public void Tired_ground_has_no_flowers_and_no_green_leaves()
    {
        foreach (var material in new[] { "grass/dry", "grass/autumn", "dirt/brown" })
            Assert.DoesNotContain(Decals(material), p => p.Contains("flowers", StringComparison.Ordinal) || p.Contains("_green", StringComparison.Ordinal));
    }

    [Fact]
    public void Ash_has_scorch_and_no_blood_or_bones()
    {
        var decals = Decals("ash/grey").ToList();
        Assert.Contains(decals, p => p.Contains("scorch", StringComparison.Ordinal));
        Assert.DoesNotContain(decals, p => p.Contains("blood", StringComparison.Ordinal) || p.Contains("bones", StringComparison.Ordinal));
    }
}
