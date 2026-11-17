namespace Meridian.Presentation.Board;

/// <summary>A glyph for a token or symbol: a short label and a colour, until the art sets replace it.</summary>
public sealed record Glyph(string Label, string Colour);

/// <summary>
/// The placeholder look (M4 plan, Task 6): one accent per deck so a glance tells you which deck is on screen (#331), and a
/// glyph for every token kind and card symbol. Colours are hex, for the scene to parse.
/// </summary>
public static class BoardStyle
{
    public static string DeckAccent(DeckId deck) => deck switch
    {
        DeckId.Command => "#3ec7ff",       // cyan: the bridge's screens
        DeckId.Habitat => "#d8b25a",       // warm brass: where people lived
        DeckId.Engineering => "#ff7a3d",   // hazard orange
        _ => "#9a9aa8",
    };

    public static string DeckName(DeckId deck) => deck switch
    {
        DeckId.Command => "Command",
        DeckId.Habitat => "Habitat",
        DeckId.Engineering => "Engineering",
        _ => deck.ToString(),
    };

    public static Glyph Symbol(CardKind kind) => kind switch
    {
        CardKind.Incident => new Glyph("!", "#ff3b3b"),
        CardKind.Salvage => new Glyph("+", "#5fe08a"),
        CardKind.Anomaly => new Glyph("◆", "#c08cff"),
        _ => new Glyph("?", "#9a9aa8"),
    };

    /// <summary>A token's glyph. A kind no scenario of M4 knows still gets a labelled square: its first letter.</summary>
    public static Glyph Token(string kind, string? state = null) => kind switch
    {
        "brood" => new Glyph("B", "#ff3b3b"),
        "repair" => state == "fixed" ? new Glyph("✓", "#5fe08a") : new Glyph("R", "#ffd447"),
        "core" => new Glyph("◆", "#3ec7ff"),
        "engine" => new Glyph("E", "#ff7a3d"),
        "pods" => new Glyph("P", "#5fe08a"),
        "prize" => new Glyph("★", "#ffd447"),
        _ => new Glyph(kind.Length > 0 ? kind[..1].ToUpperInvariant() : "?", "#9a9aa8"),
    };

    /// <summary>A seat's initial, drawn on its colour so nobody relies on colour alone (M4 plan, Global constraints).</summary>
    public static string Initial(string name) => name.Trim() is { Length: > 0 } n ? n[..1].ToUpperInvariant() : "?";

    /// <summary>
    /// Each seat's mark: its initial, unless another seat shares it. Then the initial and the name's last digit (Bot1 →
    /// B1), or its first two letters (Sam, Sue → Sa, Su), so no two seats ever show the same mark.
    /// </summary>
    public static IReadOnlyList<string> Marks(IReadOnlyList<string> names)
    {
        var marks = names.Select(Initial).ToArray();
        foreach (var clash in marks.Select((m, i) => (m, i)).GroupBy(x => x.m).Where(g => g.Count() > 1))
        {
            foreach (var (_, i) in clash)
            {
                var n = names[i].Trim();
                marks[i] = n.Length > 1 && char.IsDigit(n[^1]) ? $"{marks[i]}{n[^1]}" : n.Length > 1 ? $"{marks[i]}{char.ToLowerInvariant(n[1])}" : marks[i];
            }
        }
        // Still a clash (Sam, Sammy): number them in seat order.
        foreach (var clash in marks.Select((m, i) => (m, i)).GroupBy(x => x.m).Where(g => g.Count() > 1).ToList())
        {
            var k = 1;
            foreach (var (_, i) in clash)
            {
                marks[i] = $"{Initial(names[i])}{k++}";
            }
        }
        return marks;
    }

    /// <summary>
    /// A north-edge rectangle in a <paramref name="size"/>-pixel frame, turned clockwise by <paramref name="degrees"/>
    /// about the frame's centre (y points down). How a hatch overlay drawn for the north edge lands on another side.
    /// </summary>
    public static Box Turn(Box box, int degrees, double size)
    {
        var (x0, y0, x1, y1) = (box.X, box.Y, box.X + box.W, box.Y + box.H);
        for (var q = 0; q < ((degrees / 90) % 4 + 4) % 4; q++)
        {
            // (x, y) → (size − y, x): a quarter turn clockwise with y down.
            (x0, y0, x1, y1) = (size - y1, x0, size - y0, x1);
        }
        return new Box(x0, y0, x1 - x0, y1 - y0);
    }
}
