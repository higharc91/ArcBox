namespace Meridian.Presentation.Board;

/// <summary>The TV's resolution in pixels.</summary>
public readonly record struct TvSize(int W, int H);

/// <summary>A point on the TV, in pixels from the top left.</summary>
public readonly record struct Px(double X, double Y);

/// <summary>A rectangle on the TV, in pixels.</summary>
public readonly record struct Box(double X, double Y, double W, double H)
{
    public bool Contains(Px p) => p.X >= X && p.X <= X + W && p.Y >= Y && p.Y <= Y + H;
}

/// <summary>Where the camera looks on a deck: a centre in cells and how big a cell is drawn, in pixels.</summary>
public readonly record struct Camera(double CenterX, double CenterY, int Cell);

/// <summary>The TV's three regions: the HUD across the top, the active deck, and the strip showing the other two decks.</summary>
public readonly record struct Areas(Box Hud, Box Main, Box Side)
{
    /// <summary>The side strip's two slots, top and bottom, for the two decks not on screen.</summary>
    public (Box Top, Box Bottom) SideSlots => (Side with { H = Side.H / 2 }, Side with { Y = Side.Y + Side.H / 2, H = Side.H / 2 });
}

/// <summary>
/// The TV's deck layout and framing (spec §5.5; M4 plan, Task 3). Pure arithmetic over the host view, so the Godot scene
/// only places what this returns.
/// </summary>
/// <remarks>
/// The art contract (#331) draws 64 px tiles at whole multiples only, so the camera never zooms smoothly: a cell is drawn
/// at one of <see cref="CellSizes"/> (native × 2ᵏ), 128 px at 1080p at most. Framing picks the largest size at which the
/// explored part of the deck fits, and never one smaller than fits the whole deck. As the ship grows, the camera only
/// pulls back, in steps, easing over <see cref="EaseSeconds"/>.
/// </remarks>
public static class DeckLayout
{
    /// <summary>The cell sizes a tile may be drawn at: the 64 px art at ½×, 1×, 2× and 4×.</summary>
    public static readonly IReadOnlyList<int> CellSizes = [32, 64, 128, 256];

    /// <summary>How long the camera takes to move or pull back.</summary>
    public const double EaseSeconds = 0.6;

    /// <summary>Cells of margin around the explored part of a deck.</summary>
    public const int Margin = 1;

    /// <summary>The rules' default deck radius: a deck runs from -6 to 6 on each axis.</summary>
    public const int DefaultRadius = 6;

    /// <summary>The largest cell size for a screen: 128 px at 1080p, scaled with the screen's height, in whole steps.</summary>
    public static int MaxCell(TvSize size)
    {
        var ideal = 128.0 * size.H / 1080;
        return CellSizes.LastOrDefault(s => s <= ideal + 0.5, CellSizes[0]);
    }

    public static Areas Areas(TvSize size)
    {
        var hud = Math.Round(size.H * 0.14);
        var side = Math.Round(size.W * 0.22);
        return new Areas(new Box(0, 0, size.W, hud), new Box(0, hud, size.W - side, size.H - hud),
                         new Box(size.W - side, hud, side, size.H - hud));
    }

    /// <summary>
    /// The deck on screen: the deck of the compartment being presented, else the active seat's deck, else the deck already
    /// shown. The director only applies a change between beats, never in the middle of one.
    /// </summary>
    public static DeckId ActiveDeck(MeridianHostView view, DeckId shown)
    {
        if (view.Now?.Compartment is { } lit && DeckOf(view, lit) is { } litDeck)
        {
            return litDeck;
        }
        if (view.Seats.FirstOrDefault(s => s.Active) is { } active
            && view.Crew.FirstOrDefault(c => c.Id == active.Id) is { } marker
            && DeckOf(view, marker.At) is { } crewDeck)
        {
            return crewDeck;
        }
        return shown;
    }

    private static DeckId? DeckOf(MeridianHostView view, string compartment)
        => view.Compartments.FirstOrDefault(c => c.Id == compartment) is { } room ? room.At.Deck : null;

    /// <summary>Frames the explored part of <paramref name="deck"/> in the main area.</summary>
    public static Camera Frame(EqArray<CompartmentCard> rooms, DeckId deck, TvSize size, int radius = DefaultRadius)
    {
        var main = Areas(size).Main;
        var (minX, maxX, minY, maxY) = Bounds(rooms, deck);
        var spanX = maxX - minX + 1 + 2 * Margin;
        var spanY = maxY - minY + 1 + 2 * Margin;
        var full = 2 * radius + 1;
        var floor = CellSizes.LastOrDefault(s => full * s <= main.W && full * s <= main.H, CellSizes[0]);
        var cell = CellSizes.Where(s => s >= floor && s <= MaxCell(size))
                            .LastOrDefault(s => spanX * s <= main.W && spanY * s <= main.H, floor);
        return new Camera((minX + maxX) / 2.0, (minY + maxY) / 2.0, cell);
    }

    /// <summary>Frames a deck in a side-strip slot: the whole deck at the largest size that fits, on its explored centre.</summary>
    public static Camera FrameSide(EqArray<CompartmentCard> rooms, DeckId deck, Box slot, int radius = DefaultRadius)
    {
        var full = 2 * radius + 1;
        var cell = CellSizes.LastOrDefault(s => full * s <= slot.W && full * s <= slot.H, CellSizes[0] / 2);
        var (minX, maxX, minY, maxY) = Bounds(rooms, deck);
        return new Camera((minX + maxX) / 2.0, (minY + maxY) / 2.0, cell);
    }

    /// <summary>The centre of <paramref name="cell"/> on screen, for a camera over <paramref name="area"/>.</summary>
    public static Px ToScreen(Cell cell, Camera camera, Box area)
        => new(area.X + area.W / 2 + (cell.X - camera.CenterX) * camera.Cell,
               area.Y + area.H / 2 + (cell.Y - camera.CenterY) * camera.Cell);

    /// <summary>The explored bounds of a deck, in cells; the deck's centre if nothing is placed there.</summary>
    private static (int MinX, int MaxX, int MinY, int MaxY) Bounds(EqArray<CompartmentCard> rooms, DeckId deck)
    {
        var on = rooms.Where(r => r.At.Deck == deck).Select(r => r.At).ToList();
        return on.Count == 0
            ? (0, 0, 0, 0)
            : (on.Min(c => c.X), on.Max(c => c.X), on.Min(c => c.Y), on.Max(c => c.Y));
    }
}
