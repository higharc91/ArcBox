using ArcBox.Host.Shell;
using Godot;
using Meridian.Game;
using Meridian.Logic;
using Meridian.Presentation.Art;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Board;
using Meridian.Presentation.Debrief;
using Side = Meridian.Logic.Side;

namespace ArcBox.Host.Games.Meridian;

/// <summary>
/// The ship on the TV (spec §5.5; M4 plan, Task 6): the active deck in the main area and the other two in the side strip,
/// back to front: deck plating, compartments, tokens, crew. Placeholders and art share the art contract's metrics, so
/// art drops in without moving anything. Every position and size comes from <c>DeckLayout</c> and <c>TileDraw</c>.
/// </summary>
public partial class BoardCanvas : Node2D
{
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);
    private Director? _director;
    private Func<string, string> _tileName = id => id;
    private Func<string, string?> _crewId = _ => null;
    private MeridianArt _art = MeridianArt.None;
    private TvSize _size = new(1920, 1080);

    // The camera eases from where it was to where framing wants it (DeckLayout.EaseSeconds).
    private (double X, double Y, double Cell) _from, _to, _cam;
    private double _ease = 1;
    private DeckId? _deck;

    public void Use(Director director) => _director = director;

    /// <summary>The content pack's names and the art it has; until the pack arrives, ids and placeholders.</summary>
    public void Use(Func<string, string> tileName, Func<string, string?> crewIdByName, MeridianArt art)
    {
        (_tileName, _crewId, _art) = (tileName, crewIdByName, art);
        _textures.Clear();
    }

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;   // pixel art at whole multiples only

    public override void _Process(double delta)
    {
        var rect = GetViewportRect().Size;
        _size = new TvSize((int)rect.X, (int)rect.Y);
        if (_director?.Shown is { } view)
        {
            var cam = DeckLayout.Frame(view.Compartments, _director.Deck, _size);
            var target = (cam.CenterX, cam.CenterY, (double)cam.Cell);
            if (_deck != _director.Deck)
            {
                (_deck, _from, _to, _cam, _ease) = (_director.Deck, target, target, target, 1);   // a new deck: cut, don't pan
            }
            else if (target != _to)
            {
                (_from, _to, _ease) = (_cam, target, 0);
            }
            var easing = _ease < 1;
            _ease = Math.Min(1, _ease + delta / DeckLayout.EaseSeconds);
            var t = _ease * _ease * (3 - 2 * _ease);   // smoothstep
            _cam = (Lerp(_from.X, _to.X, t), Lerp(_from.Y, _to.Y, t), Lerp(_from.Cell, _to.Cell, t));
            // Redraw only when something moves: a new view, the camera easing, or a reveal dropping in. A software
            // renderer (and a cheap TV box) can't afford hundreds of shapes and labels every frame for nothing.
            if (!ReferenceEquals(view, _drawn) || easing || _director.Current is { Kind: "reveal" } || _size != _drawnSize)
            {
                (_drawn, _drawnSize) = (view, _size);
                QueueRedraw();
            }
        }
    }

    private MeridianHostView? _drawn;
    private TvSize _drawnSize;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public override void _Draw()
    {
        if (_director?.Shown is not { } view)
        {
            return;
        }
        if (DebriefModel.Of(view) is not null)
        {
            // The Debrief: the whole ship, all three decks side by side, because nothing is secret any more.
            var (_, command, habitat, engineering) = DebriefModel.Layout(_size);
            foreach (var (d, box) in new[] { (DeckId.Command, command), (DeckId.Habitat, habitat), (DeckId.Engineering, engineering) })
            {
                var inner = box with { X = box.X + 12, W = box.W - 24, Y = box.Y + 50, H = box.H - 60 };
                DrawString(ArcTheme.Bold, new Vector2((float)box.X + 16, (float)box.Y + 36), BoardStyle.DeckName(d).ToUpperInvariant(),
                           HorizontalAlignment.Left, -1, 24, new Color(BoardStyle.DeckAccent(d)));
                DrawDeck(view, d, inner, DeckLayout.FrameSide(view.Compartments, d, inner), null, null, names: false);
            }
            return;
        }
        var areas = DeckLayout.Areas(_size);
        var deck = _director.Deck;
        var beat = _director.Current;
        var lit = beat?.Now.Compartment ?? view.Now?.Compartment;
        DrawDeck(view, deck, areas.Main, new Camera(_cam.X, _cam.Y, (int)Math.Round(_cam.Cell)), lit, beat, names: true);

        var others = new[] { DeckId.Command, DeckId.Habitat, DeckId.Engineering }.Where(d => d != deck).ToArray();
        var (top, bottom) = areas.SideSlots;
        foreach (var (other, slot) in others.Zip(new[] { top, bottom }))
        {
            var inner = slot with { Y = slot.Y + 34, H = slot.H - 40, X = slot.X + 8, W = slot.W - 16 };
            DrawRect(R(slot with { X = slot.X + 4, W = slot.W - 8, Y = slot.Y + 4, H = slot.H - 8 }), new Color(0.05f, 0.05f, 0.08f, 0.82f));
            DrawString(ArcTheme.Bold, new Vector2((float)slot.X + 14, (float)slot.Y + 28), BoardStyle.DeckName(other).ToUpperInvariant(),
                       HorizontalAlignment.Left, -1, 22, new Color(BoardStyle.DeckAccent(other)));
            DrawDeck(view, other, inner, DeckLayout.FrameSide(view.Compartments, other, inner), null, null, names: false);
        }

        // Below the label minimum, names don't go in tiles: the presented compartment and the active seat's get a callout.
        var cell = (int)Math.Round(_cam.Cell);
        if (!DeckLayout.NamesInside(cell))
        {
            var active = view.Seats.FirstOrDefault(s => s.Active) is { } seat ? view.Crew.FirstOrDefault(c => c.Id == seat.Id)?.At : null;
            foreach (var id in new[] { lit, active }.OfType<string>().Distinct())
            {
                if (view.Compartments.FirstOrDefault(c => c.Id == id) is { } room && room.At.Deck == deck)
                {
                    Callout(V(DeckLayout.ToScreen(room.At, new Camera(_cam.X, _cam.Y, cell), areas.Main)), cell, _tileName(room.Tile), id == lit);
                }
            }
        }
    }

    /// <summary>A compartment's name above it, at the label minimum, on a dark tab.</summary>
    private void Callout(Vector2 c, float cell, string name, bool lit)
    {
        var size = DeckLayout.LabelPx + 2;
        var width = ArcTheme.SemiBold.GetStringSize(name, HorizontalAlignment.Left, -1, size).X + 20;
        var at = c - new Vector2(width / 2, cell / 2 + size + 14);
        DrawRect(new Rect2(at, new Vector2(width, size + 12)), new Color(0.04f, 0.04f, 0.07f, 0.92f));
        DrawRect(new Rect2(at, new Vector2(width, size + 12)), lit ? Colors.White : new Color(1, 1, 1, 0.35f), false, 2);
        DrawString(ArcTheme.SemiBold, at + new Vector2(10, size + 2), name, HorizontalAlignment.Left, -1, size, Colors.White);
    }

    private void DrawDeck(MeridianHostView view, DeckId deck, Box area, Camera cam, string? lit, Beat? beat, bool names)
    {
        var accent = new Color(BoardStyle.DeckAccent(deck));
        if (names)
        {
            DrawRect(R(area), new Color(accent, 0.04f));   // the deck's plating
        }
        var cell = (float)cam.Cell;
        var tiles = TileDraw.Of(view.Compartments, deck, lit);
        foreach (var tile in tiles)
        {
            var c = V(DeckLayout.ToScreen(tile.At, cam, area));
            if (!area.Contains(new Px(c.X, c.Y)))
            {
                continue;
            }
            var (alpha, scale) = Arrival(tile, beat);
            if (alpha <= 0)
            {
                continue;
            }
            DrawTile(tile, c, cell * scale, accent, alpha, names);
        }

        // Tokens in compartments, then crew, so people are always on top.
        var byRoom = tiles.ToDictionary(t => t.Id);
        if (view.Scenario is { } panel)
        {
            foreach (var group in panel.Tokens.Where(t => t.At is not null && byRoom.ContainsKey(t.At)).GroupBy(t => t.At!))
            {
                var c = V(DeckLayout.ToScreen(byRoom[group.Key].At, cam, area));
                var i = 0;
                foreach (var token in group)
                {
                    var glyph = BoardStyle.Token(token.Kind, token.State);
                    var size = names ? Math.Max(18f, cell * 0.22f) : Math.Max(4f, cell * 0.2f);
                    var at = c + new Vector2(-cell * 0.36f + i++ * (size + 3), cell * 0.18f);
                    var colour = new Color(glyph.Colour, token.Stunned ? 0.4f : 1f);
                    DrawRect(new Rect2(at, new Vector2(size, size)), colour);
                    if (names)
                    {
                        DrawString(ArcTheme.Black, at + new Vector2(0, size * 0.82f), glyph.Label, HorizontalAlignment.Center, size, (int)(size * 0.8f), new Color("101018"));
                    }
                }
            }
        }
        foreach (var group in view.Crew.Where(m => byRoom.ContainsKey(m.At)).GroupBy(m => m.At))
        {
            var c = V(DeckLayout.ToScreen(byRoom[group.Key].At, cam, area));
            var members = group.ToList();
            for (var i = 0; i < members.Count; i++)
            {
                var m = members[i];
                // Big enough for the initial at a readable size; up to six share a compartment in two rows of three.
                var r = names ? Math.Max(13f, cell * 0.15f) : Math.Max(3f, cell * 0.14f);
                if (members.Count > 3)
                {
                    r *= 0.8f;   // two rows must still fit the tile
                }
                // With the name in the tile (two lines at most), crew stand in its lower third so they don't cover it.
                var below = names && DeckLayout.NamesInside((int)Math.Round(cell)) ? cell * 0.3f : 0;
                var at = c + new Vector2((i % 3 - 1) * r * 1.9f, (i / 3) * r * 1.9f - (members.Count > 3 ? r : 0) + below);
                DrawCrew(view, m, at, r, cell, names);
            }
        }
    }

    /// <summary>How a compartment arrives during its reveal beat: hidden while the camera eases, then dropped in.</summary>
    private (float Alpha, float Scale) Arrival(TileDraw tile, Beat? beat)
    {
        if (!tile.Lit || beat is not { Kind: "reveal", Short: false } || _director?.Position is not { } at)
        {
            return (1, 1);
        }
        return at.Step.Name switch
        {
            "ease" => (0, 1),
            "drop" => ((float)at.Progress, 1.25f - 0.25f * (float)at.Progress),
            _ => (1, 1),
        };
    }

    private void DrawTile(TileDraw tile, Vector2 c, float cell, Color accent, float alpha, bool names)
    {
        var half = cell / 2;
        var box = new Rect2(c - new Vector2(half, half), new Vector2(cell, cell));
        if (_art.Tile(tile.Tile) is { } art && Texture(art.Path) is { } tex)
        {
            DrawSetTransform(c, Mathf.DegToRad(tile.Degrees), Vector2.One);
            DrawTextureRect(tex, new Rect2(-half, -half, cell, cell), false, new Color(1, 1, 1, alpha));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else
        {
            var inset = Math.Max(1f, cell * 0.04f);
            DrawRect(box.Grow(-inset), new Color(accent.Darkened(0.72f), 0.95f * alpha));
            DrawRect(box.Grow(-inset), new Color(accent, 0.55f * alpha), false, Math.Max(1f, cell * 0.02f));
        }

        // Hatches: a doorway is the middle quarter of the edge (#331's 16 px of 64).
        foreach (var side in Sides.All)
        {
            var state = tile.Hatch(side);
            if (_art.Hatch(state) is { } overlay && Texture(overlay) is { } hatchTex)
            {
                DrawSetTransform(c, Mathf.DegToRad(TileDraw.OverlayDegrees(side)), Vector2.One);
                DrawTextureRect(hatchTex, new Rect2(-half, -half, cell, cell), false, new Color(1, 1, 1, alpha));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                continue;
            }
            DrawHatch(c, cell, side, state, accent, alpha);
        }

        if (tile.Symbol is { } symbol)
        {
            var glyph = BoardStyle.Symbol(symbol);
            var r = Math.Max(3f, cell * 0.09f);
            var at = c + new Vector2(half - r * 1.6f, half - r * 1.6f);   // bottom right, clear of the name
            DrawCircle(at, r, new Color(glyph.Colour, alpha));
            if (names)
            {
                DrawString(ArcTheme.Black, at + new Vector2(-r, r * 0.6f), glyph.Label, HorizontalAlignment.Center, r * 2, (int)(r * 1.5f), new Color("101018"));
            }
        }
        if (names && DeckLayout.NamesInside((int)Math.Round(cell)))
        {
            // The label minimum, unless a single word is wider than the tile (Containment): then a little smaller, never
            // below 18 px, rather than cut letters off.
            var name = _tileName(tile.Tile);
            var room = cell - 10;
            var size = (int)Math.Max(DeckLayout.LabelPx, cell * 0.17f);
            var widest = name.Split(' ').Max(word => ArcTheme.SemiBold.GetStringSize(word, HorizontalAlignment.Left, -1, size).X);
            if (widest > room)
            {
                size = Math.Max(18, (int)(size * room / widest));
            }
            DrawMultilineString(ArcTheme.SemiBold, c + new Vector2(-room / 2, -half * 0.36f), name,
                                HorizontalAlignment.Center, room, size, 2, new Color(1, 1, 1, 0.92f * alpha));
        }
        if (tile.Lit)
        {
            DrawRect(box.Grow(2), new Color(1, 1, 1, 0.9f * alpha), false, Math.Max(2f, cell * 0.03f));
        }
    }

    /// <summary>A hatch placeholder, drawn for the north edge and turned to its side.</summary>
    private void DrawHatch(Vector2 c, float cell, Side side, HatchState state, Color accent, float alpha)
    {
        var half = cell / 2;
        var wall = Math.Max(1.5f, cell * 0.05f);
        var door = cell * 0.25f;
        DrawSetTransform(c, Mathf.DegToRad(TileDraw.OverlayDegrees(side)), Vector2.One);
        var wallColour = new Color(accent.Lightened(0.2f), 0.9f * alpha);
        if (state == HatchState.None)
        {
            DrawRect(new Rect2(-half, -half, cell, wall), wallColour);
        }
        else
        {
            DrawRect(new Rect2(-half, -half, half - door / 2, wall), wallColour);
            DrawRect(new Rect2(door / 2, -half, half - door / 2, wall), wallColour);
            switch (state)
            {
                case HatchState.Closed:
                    DrawRect(new Rect2(-door / 2, -half + wall * 0.25f, door, wall * 0.5f), new Color(0.75f, 0.75f, 0.8f, 0.9f * alpha));
                    break;
                case HatchState.Sealed:
                    var red = new Color("ff3b3b", alpha);
                    DrawRect(new Rect2(-door / 2, -half, door, wall), red);
                    DrawLine(new Vector2(-door / 2, -half - wall), new Vector2(door / 2, -half + wall * 2), red, Math.Max(1, wall * 0.4f));
                    DrawLine(new Vector2(door / 2, -half - wall), new Vector2(-door / 2, -half + wall * 2), red, Math.Max(1, wall * 0.4f));
                    break;
            }
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawCrew(MeridianHostView view, CrewMarker marker, Vector2 at, float r, float cell, bool names)
    {
        var colour = ArcTheme.PlayerColour(marker.Colour);
        var seat = view.Seats.FirstOrDefault(s => s.Id == marker.Id);
        if (names && seat?.Crew is { } crewName && _crewId(crewName) is { } crewId && _art.Crew(crewId) is { } art
            && Texture(art.Idle) is { } sprite)
        {
            var scale = cell / 64f;
            var size = new Vector2(24, 24) * scale;
            DrawTextureRect(sprite, new Rect2(at - new Vector2(art.AnchorX, art.AnchorY) * scale, size), false);
            DrawCircle(at + new Vector2(0, r * 0.6f), r * 0.45f, colour);
            return;
        }
        DrawCircle(at, r + Math.Max(1, r * 0.15f), new Color("101018"));
        DrawCircle(at, r, colour);
        if (names)
        {
            var mark = Mark(view, marker.Id);
            DrawString(ArcTheme.Black, at + new Vector2(-r, r * 0.5f), mark, HorizontalAlignment.Center, r * 2,
                       (int)(r * (mark.Length > 1 ? 1.1f : 1.5f)), new Color("101018"));
        }
    }

    private static string Mark(MeridianHostView view, string seat)
    {
        var marks = BoardStyle.Marks([.. view.Seats.Select(s => s.Name)]);
        for (var i = 0; i < view.Seats.Count; i++)
        {
            if (view.Seats[i].Id == seat)
            {
                return marks[i];
            }
        }
        return "?";
    }

    /// <summary>A compartment's name, from the content pack, for the Breach sequence.</summary>
    public string RoomName(string compartment)
        => _director?.Shown?.Compartments.FirstOrDefault(c => c.Id == compartment) is { } room ? _tileName(room.Tile) : compartment;

    private Texture2D? Texture(string path)
    {
        if (!_textures.TryGetValue(path, out var tex))
        {
            var image = Image.LoadFromFile(path);
            tex = image is null ? null : ImageTexture.CreateFromImage(image);
            _textures[path] = tex;
        }
        return tex;
    }

    private static Rect2 R(Box b) => new((float)b.X, (float)b.Y, (float)b.W, (float)b.H);
    private static Vector2 V(Px p) => new((float)p.X, (float)p.Y);
}
