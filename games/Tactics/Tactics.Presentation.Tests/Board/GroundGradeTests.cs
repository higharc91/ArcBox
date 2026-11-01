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
