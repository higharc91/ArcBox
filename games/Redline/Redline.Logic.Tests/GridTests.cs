namespace Redline.Logic.Tests;

public class GridTests
{
    [Fact]
    public void Load_has_11_teams_and_22_unique_seats()
    {
        var g = Grid2026.Load();
        Assert.Equal(11, g.Teams.Count);
        Assert.Equal(22, g.Seats.Select(s => s.Driver.Code).Distinct().Count());
        Assert.Equal(22, g.Seats.Select(s => s.Driver.Number).Distinct().Count());
    }

    [Fact]
    public void Champion_and_verstappen_numbers_match_2026()
    {
        var g = Grid2026.Load();
        Assert.Equal(1, g.ByCode("NOR").Driver.Number);
        Assert.Equal(3, g.ByCode("VER").Driver.Number);
        Assert.Equal("red-bull", g.ByCode("VER").Team.Id);
    }

    [Fact]
    public void AiSkill_comes_from_the_standings_snapshot()
    {
        var g = Grid2026.Load();
        Assert.Equal(1.15, g.ByCode("ANT").Driver.AiSkill, 3);
        Assert.Equal(0.85, g.ByCode("BOT").Driver.AiSkill, 3);
    }

    [Fact]
    public void Every_driver_has_a_nationality_from_the_countries_table()
    {
        var expected = new Dictionary<string, string>
        {
            ["NOR"] = "GB", ["HAM"] = "GB", ["RUS"] = "GB", ["LIN"] = "GB", ["BEA"] = "GB",
            ["PIA"] = "AU", ["LEC"] = "MC", ["VER"] = "NL", ["HAD"] = "FR", ["GAS"] = "FR", ["OCO"] = "FR",
            ["ANT"] = "IT", ["ALO"] = "ES", ["SAI"] = "ES", ["STR"] = "CA", ["COL"] = "AR", ["ALB"] = "TH",
            ["LAW"] = "NZ", ["HUL"] = "DE", ["BOR"] = "BR", ["PER"] = "MX", ["BOT"] = "FI",
        };
        var g = Grid2026.Load();
        Assert.Equal(22, g.Seats.Count);
        Assert.All(g.Seats, s => Assert.Equal(expected[s.Driver.Code], s.Driver.Nationality));
        Assert.All(g.Seats, s => Assert.NotNull(Countries.Find(s.Driver.Nationality)));
    }

    [Fact]
    public void Validate_rejects_a_driver_without_a_nationality()
    {
        var g = Grid2026.Load();
        var t = g.Teams[0];
        var stateless = t with { Drivers = [t.Drivers[0] with { Nationality = "" }, t.Drivers[1]] };
        Assert.Throws<InvalidDataException>(() => Grid2026.Validate(g with { Teams = [stateless, .. g.Teams.Skip(1)] }));
    }

    [Fact]
    public void Validate_rejects_duplicate_codes_and_numbers()
    {
        var g = Grid2026.Load();
        var t = g.Teams[0];
        var dup = t with { Drivers = [t.Drivers[0], t.Drivers[0]] };
        Assert.Throws<InvalidDataException>(() => Grid2026.Validate(g with { Teams = [dup, .. g.Teams.Skip(1)] }));
    }
}
