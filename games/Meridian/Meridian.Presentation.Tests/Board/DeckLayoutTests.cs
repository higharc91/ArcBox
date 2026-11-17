using Meridian.Presentation.Board;

namespace Meridian.Presentation.Tests.Board;

public sealed class DeckLayoutTests
{
    private static readonly TvSize Hd = new(1920, 1080);
    private static readonly TvSize Sd = new(1280, 720);

    private static CompartmentCard Room(string id, DeckId deck, int x, int y, params Side[] hatches)
        => new(id, "storage_bay", null, new Cell(deck, x, y), 0, [.. hatches], null, null);

    private static MeridianHostView View(EqArray<CompartmentCard> rooms, EqArray<CrewMarker> crew, Presented? now = null,
                                         string? active = null)
        => new("explore", "", [new SeatCard("p1", "Ann", "Ines Okafor", 0, true, false, active == "p1"),
                               new SeatCard("p2", "Bo", "Tobe Raske", 1, true, false, active == "p2")],
               rooms, crew, 40, 0, 1, now, []);

    [Theory]
    [InlineData(1920, 1080, 128)]
    [InlineData(1280, 720, 64)]
    [InlineData(3840, 2160, 256)]
    public void Tiles_are_128_px_at_1080p_and_scale_by_whole_steps(int w, int h, int cell)
        => Assert.Equal(cell, DeckLayout.MaxCell(new TvSize(w, h)));

    [Fact]
    public void The_main_area_and_side_strip_share_the_screen_below_the_hud()
    {
        var a = DeckLayout.Areas(Hd);
        Assert.True(a.Main.W + a.Side.W <= Hd.W && a.Main.Y >= a.Hud.H && a.Side.Y >= a.Hud.H);
        Assert.Equal(a.Main.H, a.Side.H);
    }

    [Fact]
    public void The_deck_being_presented_is_shown_first()
    {
        var rooms = new EqArray<CompartmentCard>([Room("c1", DeckId.Habitat, 0, 0), Room("c9", DeckId.Engineering, 2, 1)]);
        var crew = new EqArray<CrewMarker>([new CrewMarker("p1", "Ann", "c1", 0)]);
        var now = new Presented(4, "reveal", new SeatId("p1"), "Engine Room", "", 1.2, Compartment: "c9");
        Assert.Equal(DeckId.Engineering, DeckLayout.ActiveDeck(View(rooms, crew, now, active: "p1"), DeckId.Habitat));
        Assert.Equal(DeckId.Habitat, DeckLayout.ActiveDeck(View(rooms, crew, active: "p1"), DeckId.Command));
        Assert.Equal(DeckId.Command, DeckLayout.ActiveDeck(View(rooms, crew), DeckId.Command));   // nobody active: stay
    }

    [Fact]
    public void A_small_ship_is_framed_at_the_largest_size_and_never_closer()
    {
        var rooms = new EqArray<CompartmentCard>([Room("c1", DeckId.Habitat, 0, 2), Room("c2", DeckId.Habitat, 0, 1)]);
        var cam = DeckLayout.Frame(rooms, DeckId.Habitat, Hd);
        Assert.Equal(128, cam.Cell);
        Assert.Equal((0.0, 1.5), (cam.CenterX, cam.CenterY));
    }

    [Fact]
    public void A_deck_holding_only_its_landing_is_still_framed_around_it()
    {
        // Review focus 5: someone takes the lift to a deck with nothing new on it.
        var rooms = new EqArray<CompartmentCard>([Room("c1", DeckId.Habitat, 0, 0), Room("c4", DeckId.Command, 0, 0)]);
        var cam = DeckLayout.Frame(rooms, DeckId.Command, Hd);
        Assert.Equal(DeckLayout.MaxCell(Hd), cam.Cell);
        Assert.Equal((0.0, 0.0), (cam.CenterX, cam.CenterY));
        var main = DeckLayout.Areas(Hd).Main;
        var at = DeckLayout.ToScreen(new Cell(DeckId.Command, 0, 0), cam, main);
        Assert.True(main.Contains(at));
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    public void A_full_deck_always_fits(int w, int h)
    {
        var size = new TvSize(w, h);
        var r = 6;   // the rules' deck radius
        var rooms = new EqArray<CompartmentCard>([Room("a", DeckId.Habitat, -r, -r), Room("b", DeckId.Habitat, r, r)]);
        var cam = DeckLayout.Frame(rooms, DeckId.Habitat, size, radius: r);
        var main = DeckLayout.Areas(size).Main;
        foreach (var cell in new[] { new Cell(DeckId.Habitat, -r, -r), new Cell(DeckId.Habitat, r, r) })
        {
            var p = DeckLayout.ToScreen(cell, cam, main);
            Assert.True(main.Contains(p), $"{cell} at {p} outside {main} with cell {cam.Cell}");
        }
        Assert.Contains(cam.Cell, DeckLayout.CellSizes);   // whole steps of the art's 64 px only
    }

    [Fact]
    public void Framing_grows_in_steps_as_the_ship_does()
    {
        var rooms = new List<CompartmentCard> { Room("c0", DeckId.Habitat, 0, 0) };
        var sizes = new List<int>();
        for (var i = 1; i <= 6; i++)
        {
            rooms.Add(Room($"c{i}", DeckId.Habitat, i, 0));
            rooms.Add(Room($"d{i}", DeckId.Habitat, -i, 0));
            sizes.Add(DeckLayout.Frame([.. rooms], DeckId.Habitat, Hd).Cell);
        }
        Assert.Equal(sizes.OrderDescending(), sizes);   // only ever pulls back
        Assert.Equal(128, sizes[0]);
        Assert.True(sizes[^1] < 128);
    }
}
