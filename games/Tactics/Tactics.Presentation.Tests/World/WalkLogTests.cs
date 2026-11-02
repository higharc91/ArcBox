using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WalkLog"/>: the <c>WALKAT</c> lines tools/tv-e2e.sh reads to say the party really moved.</summary>
public sealed class WalkLogTests
{
    private static readonly ImmutableArray<WalkerView> Party =
    [
        new("p1", "Ana", "characters.class_fighter", true, false),
        new("p2", "Bo Peep", "characters.class_cleric", true, true),
        new("p3", "Cy", "characters.class_rogue", true, false),
    ];

    private static Dictionary<string, UnitPlace> At(float leadX, float y = 20.5f) => new()
    {
        ["p2"] = new UnitPlace(new Vector2(leadX, y), 0),
        ["p1"] = new UnitPlace(new Vector2(leadX - 0.9f, y), 0),
        ["p3"] = new UnitPlace(new Vector2(leadX - 2.4f, y), 1),
    };

    [Fact]
    public void A_line_says_the_leader_first_and_the_widest_gap()
    {
        var line = new WalkLog().Next(0, 1793500000123, 42, true, Party, At(31.5f));

        Assert.Equal("WALKAT 1793500000123 42 1 31.50 20.50 1.50 Bo_Peep 31.50 20.50 Ana 30.60 20.50 Cy 29.10 20.50", line);
    }

    [Fact]
    public void Lines_come_at_most_every_interval_while_moving_and_once_at_rest()
    {
        var log = new WalkLog();
        var said = new List<string>();
        var x = 10f;
        for (var t = 0.0; t < 2; t += 1 / 60.0)
        {
            if (t < 1)
                x += 0.05f;
            if (log.Next(t, 0, 1, t < 1, Party, At(x)) is { } line)
                said.Add(line);
        }

        Assert.InRange(said.Count, 1 / WalkLog.Interval, 1 / WalkLog.Interval + 2);
        Assert.Contains($" {x:0.00} ", said[^1]);
        Assert.Contains(" 0 ", said[^1][..30]);    // the last says it isn't held
        for (var t = 2.0; t < 60; t += 1 / 60.0)
            Assert.Null(log.Next(t, 0, 1, false, Party, At(x)));
    }

    [Fact]
    public void Nothing_to_place_is_no_line()
    {
        Assert.Null(new WalkLog().Next(0, 0, 0, false, Party, new Dictionary<string, UnitPlace>()));
        Assert.Null(new WalkLog().Next(0, 0, 0, false, [], At(3)));
    }

    [Fact]
    public void A_new_step_or_a_release_is_a_line_even_standing_still()
    {
        var log = new WalkLog();
        Assert.NotNull(log.Next(0, 0, 1, true, Party, At(5)));
        Assert.NotNull(log.Next(1, 0, 1, false, Party, At(5)));
        Assert.NotNull(log.Next(2, 0, 2, false, Party, At(5)));
        Assert.Null(log.Next(3, 0, 2, false, Party, At(5)));
    }
}
