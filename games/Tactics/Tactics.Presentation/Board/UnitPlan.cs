using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>Whose side a unit is on, as the TV tells them apart.</summary>
public enum UnitTone { Party, Enemy, Neutral }

/// <summary>
/// Where a unit stands, which needn't be a whole tile, so a unit can be drawn on its way between two.
/// </summary>
/// <param name="Map">The point of the map, in tiles: a tile's centre is its x + 0.5, y + 0.5.</param>
/// <param name="Height">The height in steps.</param>
public readonly record struct UnitPlace(Vector2 Map, float Height)
{
    /// <summary>The middle of <paramref name="tile"/> as the board draws it.</summary>
    public static UnitPlace On(BoardTerrain terrain, Tile tile) => new(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), terrain.Top(tile));

    /// <summary>
    /// The tile whose slot it sorts in. Between two tiles that's the nearer one: otherwise that tile's top, painted later,
    /// would cover the unit's feet.
    /// </summary>
    public Tile Over => new((int)MathF.Ceiling(Map.X - 0.5f), (int)MathF.Ceiling(Map.Y - 0.5f));

    /// <summary>Where it lands, in art pixels.</summary>
    public Vector2 Feet => BoardProjection.Corner(Map.X, Map.Y, Height);
}

/// <summary>An HP bar: <paramref name="Fill"/> of <paramref name="Width"/>'s inner width is lit, in art pixels.</summary>
public sealed record UnitBar(Vector2 TopLeft, int Width, int Fill);

/// <summary>A Downed unit's turns left, in the pack's big digits.</summary>
public sealed record UnitCountdown(Vector2 Centre, string Text);

/// <summary>A unit, ready to draw, in art pixels.</summary>
/// <param name="Order">Its place in the painter's order (<see cref="BoardOrder.Key"/>).</param>
/// <param name="Id">The unit.</param>
/// <param name="Tone">Its side.</param>
/// <param name="Feet">Where its anchor lands.</param>
/// <param name="Sprite">Its sheet.</param>
/// <param name="Anim">The animation it's playing.</param>
/// <param name="Phase">Seconds added to the clock, so units don't all bob in step.</param>
/// <param name="Row">The sheet's row: 0 facing the viewer, 1 away.</param>
/// <param name="Mirror">Drawn flipped.</param>
/// <param name="Ring">The radius across of the ring at its feet; half that down, on the tile's own slant.</param>
/// <param name="Active">It's this unit's turn.</param>
/// <param name="Under">Status effects drawn before the unit.</param>
/// <param name="Over">Status effects drawn after it.</param>
/// <param name="Bar">Its HP bar, if it has one.</param>
/// <param name="Countdown">Its Downed countdown, if it has one.</param>
/// <param name="Icons">Status icons, in <see cref="UnitArt.IconsPath"/>.</param>
/// <param name="IconsAt">The first icon's top left.</param>
/// <param name="ArrowAt">Where the point of the turn arrow goes, on the active unit.</param>
public sealed record BoardUnit(long Order, UnitId Id, UnitTone Tone, Vector2 Feet, UnitSprite Sprite, UnitAnim Anim, double Phase, int Row, bool Mirror,
    float Ring, bool Active, ImmutableArray<StatusEffect> Under, ImmutableArray<StatusEffect> Over, UnitBar? Bar, UnitCountdown? Countdown,
    ImmutableArray<ArtRect> Icons, Vector2 IconsAt, Vector2? ArrowAt);

/// <summary>
/// The units of a battle, ready to draw: what stands where, sorted into the painter's order, with what marks each one.
/// Pure, and separate from <see cref="BoardPlan"/>: moving a unit doesn't touch the board's plan.
/// </summary>
public static class UnitPlan
{
    /// <summary>An HP bar's width.</summary>
    public const int BarWidth = 30;

    /// <summary>An HP bar's height (ui/manifest.json: a frame's height is fixed).</summary>
    public const int BarHeight = 9;

    /// <summary>The most icons a unit shows. More would run into its neighbours' and be unreadable.</summary>
    public const int MaxIcons = 4;

    /// <summary>The distance from one icon to the next.</summary>
    public const int IconStep = 17;

    /// <summary>The digits' size over a Downed unit, as a multiple of the art.</summary>
    public const int CountdownScale = 2;

    private const string Prone = "prone";     // a Downed unit is always prone, and already lying down
    private const int DigitHeight = 12;
    private const int Gap = 2;
    private const float LyingHeight = 14;     // how tall a figure lying down is
    private const float ArrowLift = 3;
    private const float RingShare = 0.42f;    // of the frame's width, kept inside a tile for anything up to a tile wide
    private const double PhaseStep = 0.37;

    /// <summary>
    /// The drawn units of <paramref name="battle"/> (Up and Downed), back to front. A unit in <paramref name="places"/> is
    /// drawn there and not on its tile.
    /// </summary>
    public static ImmutableArray<BoardUnit> Build(BattleView battle, BoardTerrain terrain, UnitArt art, BoardFit fit,
        IReadOnlyDictionary<UnitId, UnitPlace>? places = null)
    {
        var drawn = battle.Units.Where(u => u.Life is "Up" or "Downed" && terrain.Map.InBounds(u.At)).ToArray();
        var top = (BoardFit.Area.Y - fit.Origin.Y) / fit.Scale;
        return [.. drawn
            .Select((unit, i) => Unit(unit, i, drawn, battle.Active, art, top,
                places is not null && places.TryGetValue(unit.Id, out var place) ? place : UnitPlace.On(terrain, unit.At)))
            .OrderBy(u => u.Order)];
    }

    private static BoardUnit Unit(UnitView unit, int index, UnitView[] all, UnitId? active, UnitArt art, float top, UnitPlace place)
    {
        var tone = ToneOf(unit.Side);
        var sprite = art.Sprite(unit.Sprite) ?? art.StandIn(tone);
        var downed = unit.Life == "Downed";
        var (row, mirror) = Facing(unit, tone, all);
        var looks = unit.Conditions.Where(c => !(downed && c == Prone)).Select(art.Condition).OfType<ConditionLook>().ToArray();
        var effects = looks.Select(l => l.Effect).OfType<StatusEffect>().DistinctBy(e => e.Path).ToArray();
        ImmutableArray<ArtRect> icons = [.. looks.Select(l => l.Icon).OfType<ArtRect>().Distinct().Take(MaxIcons)];
        var isActive = unit.Id == active;

        // The marks stack up from the head. If the stack would reach the top strip, all of it comes down onto the unit.
        var feet = place.Feet;
        var head = downed ? feet.Y - LyingHeight : feet.Y - sprite.AnchorY + sprite.HeadTop;
        var y = head - Gap;
        var mark = y;
        y -= downed ? DigitHeight * CountdownScale : BarHeight;
        var markTop = y;
        var iconsTop = y;
        if (!icons.IsEmpty)
            y = iconsTop = y - IconStep;
        var arrow = y - ArrowLift;
        if (isActive)
            y = arrow - art.ArrowAnchorY;
        var shift = Math.Max(0, top - y);

        return new BoardUnit(OrderOf(place.Over, sprite), unit.Id, tone, feet, sprite,
            downed ? new UnitAnim([sprite.Down.Frames[^1]], 1, false) : sprite.Idle, index * PhaseStep, row, mirror,
            sprite.Width * RingShare, isActive,
            [.. effects.Where(e => e.Under)], [.. effects.Where(e => !e.Under)],
            downed ? null : new UnitBar(new Vector2(feet.X - BarWidth / 2f, markTop + shift), BarWidth, Fill(unit.Hp, unit.MaxHp, art.Bar(tone))),
            downed ? new UnitCountdown(new Vector2(feet.X, (markTop + mark) / 2 + shift), unit.Countdown.ToString(CultureInfo.InvariantCulture)) : null,
            icons, new Vector2(feet.X - icons.Length * IconStep / 2f, iconsTop + shift),
            isActive ? new Vector2(feet.X, arrow + shift) : null);
    }

    /// <summary>
    /// A unit's slot. A sprite wider than a tile reaches over the next tile along its diagonal, which paints later and would
    /// cut it, so it goes after its whole diagonal. It's still before the diagonal in front, so what's in front hides it.
    /// </summary>
    private static long OrderOf(Tile tile, UnitSprite sprite)
        => BoardOrder.Key(sprite.Width > BoardProjection.HalfWidth * 2 ? new Tile(tile.X + tile.Y, 0) : tile, BoardLayer.Unit);

    private static UnitTone ToneOf(string side) => side switch
    {
        "Party" => UnitTone.Party,
        "Enemy" => UnitTone.Enemy,
        _ => UnitTone.Neutral,
    };

    /// <summary>ui/manifest.json: <c>fillW = round(innerW * value / max)</c>, at least 3 when value > 0.</summary>
    private static int Fill(int hp, int max, BarArt bar)
    {
        var inner = BarWidth - 2 * bar.FillX;
        if (hp <= 0 || max <= 0)
            return 0;
        return Math.Clamp((int)MathF.Round(inner * (float)hp / max, MidpointRounding.AwayFromZero), Math.Min(3, inner), inner);
    }

    /// <summary>
    /// The view has no facing, so a unit looks at its nearest foe. The art's row 0 looks to the lower right and row 1 to the
    /// upper left (characters/manifest.json), and the others are those, mirrored. With no foe a unit looks at the viewer.
    /// </summary>
    private static (int Row, bool Mirror) Facing(UnitView unit, UnitTone tone, UnitView[] all)
    {
        var foes = all.Where(u => tone switch
        {
            UnitTone.Party => ToneOf(u.Side) == UnitTone.Enemy,
            UnitTone.Enemy => ToneOf(u.Side) == UnitTone.Party,
            _ => false,
        });
        if (foes.OrderBy(u => Math.Abs(u.At.X - unit.At.X) + Math.Abs(u.At.Y - unit.At.Y)).ThenBy(u => u.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault() is not { } foe)
            return (0, false);

        var (du, dv) = (foe.At.X - unit.At.X, foe.At.Y - unit.At.Y);
        var (across, down) = (du - dv, du + dv);
        return down >= 0 ? (0, across < 0) : (1, across > 0);
    }
}
