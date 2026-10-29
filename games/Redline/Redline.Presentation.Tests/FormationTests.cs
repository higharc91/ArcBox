using ArcBox.Sdk;
using Redline.Logic;

namespace Redline.Presentation.Tests;

public class FormationTests
{
    private const double L = 5512;

    private static DriverRow Row(string code, int pos, int dist, int? prev = null, int change = 0) =>
        new(code, 1, "team", null, null, pos, change, dist, prev ?? dist, DriverStatus.Ready, null, null, null, false, true);

    private static RedlineHostView View(params DriverRow[] rows) =>
        new(RacePhase.Question, 1, 15, 28, "cota", null, null, rows, null, null, "attribution", Payouts.From(new RedlineRules()));

    [Fact]
    public void Distance_zero_lines_up_on_the_grid_in_ranking_order()
    {
        var slots = Formation.Current(View(Row("AAA", 1, 0), Row("BBB", 1, 0), Row("CCC", 3, 0)), L);

        Assert.Equal(new CarSlot(-Formation.PoleSetback, LapMath.Grid(0).Lateral), slots["AAA"]);
        Assert.Equal(new CarSlot(-Formation.PoleSetback - 8, LapMath.Grid(1).Lateral), slots["BBB"]);
        Assert.Equal(new CarSlot(-Formation.PoleSetback - 16, LapMath.Grid(2).Lateral), slots["CCC"]);
    }

    [Fact]
    public void Lone_car_sits_on_the_centre_line_at_its_distance()
    {
        var slots = Formation.Current(View(Row("AAA", 1, 7)), L);

        Assert.Equal(7.0 / 28 * L, slots["AAA"].RaceMeters, 9);
        Assert.Equal(0, slots["AAA"].Lateral);
    }

    [Fact]
    public void Shared_distance_forms_a_pack_ordered_by_position()
    {
        // Listed out of position order on purpose: the pack still orders by position.
        var slots = Formation.Current(View(Row("CCC", 3, 9), Row("AAA", 1, 9), Row("BBB", 2, 9), Row("DDD", 4, 9), Row("EEE", 5, 2)), L);
        var anchor = 9.0 / 28 * L;

        Assert.Equal(new CarSlot(anchor, -4.4), slots["AAA"]);
        Assert.Equal(new CarSlot(anchor, 0), slots["BBB"]);
        Assert.Equal(new CarSlot(anchor, 4.4), slots["CCC"]);
        Assert.Equal(new CarSlot(anchor - 9, -4.4), slots["DDD"]);
        Assert.Equal(0, slots["EEE"].Lateral);
    }

    [Fact]
    public void Round_start_uses_previous_distance_and_the_position_held_then()
    {
        // BBB was ahead at the start of the round (position 1 + change 0 vs AAA 2 + ... ), both at 5.
        var view = View(Row("AAA", 1, 9, prev: 5, change: 1), Row("BBB", 2, 6, prev: 5, change: -1));

        var start = Formation.RoundStart(view, L);

        Assert.Equal(5.0 / 28 * L, start["AAA"].RaceMeters, 9);
        Assert.Equal(-3.4, start["BBB"].Lateral);
        Assert.Equal(3.4, start["AAA"].Lateral);
    }

    [Fact]
    public void Race_meters_are_not_wrapped_past_the_finish_line()
    {
        var slots = Formation.Current(View(Row("AAA", 1, 30)), L);

        Assert.Equal(30.0 / 28 * L, slots["AAA"].RaceMeters, 9);
    }

    [Fact]
    public void Every_driver_gets_a_slot_and_no_two_overlap()
    {
        var rows = Enumerable.Range(0, 22).Select(i => Row($"D{i:00}", i + 1, i % 5 == 0 ? 0 : 3 + i % 4)).ToArray();

        var slots = Formation.Current(View(rows), L);

        Assert.Equal(22, slots.Count);
        var list = slots.Values.ToList();
        for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
                Assert.True(Math.Abs(list[i].RaceMeters - list[j].RaceMeters) >= 5.6 || Math.Abs(list[i].Lateral - list[j].Lateral) >= 2.5,
                    $"{list[i]} overlaps {list[j]}");
    }

    [Fact]
    public void Player_ids_do_not_change_placement()
    {
        var ai = Formation.Current(View(Row("AAA", 1, 4), Row("BBB", 2, 4)), L);
        var human = Formation.Current(View(Row("AAA", 1, 4) with { Player = new PlayerId("p"), PlayerName = "P" }, Row("BBB", 2, 4)), L);

        Assert.Equal(ai["AAA"], human["AAA"]);
    }
}
