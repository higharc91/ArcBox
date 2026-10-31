using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Hud;

/// <summary>One unit on the turn order strip.</summary>
/// <param name="Id">The unit.</param>
/// <param name="Name">A character's whole name, which the strip cuts if it must, or a monster's short one (<see cref="Words.Short"/>).</param>
/// <param name="Sprite">Its sprite key, for <see cref="HudArt.Portrait"/>.</param>
/// <param name="Tone">Its side.</param>
/// <param name="Active">It's this unit's turn.</param>
/// <param name="Ai">A character whose player is away, so the AI is playing it.</param>
/// <param name="Downed">It's on the ground.</param>
/// <param name="Stripe">Its stripe in ui/manifest.json's <c>turnOrder</c>: its player's colour, or its side's.</param>
public sealed record HudSlot(UnitId Id, string Name, string? Sprite, UnitTone Tone, bool Active, bool Ai, bool Downed, string Stripe);

/// <summary>
/// What the battle HUD says, worded and ready to draw. Everything comes from the battle as the player has seen it, so the HUD
/// can't give away a beat that hasn't played. <see cref="Prompt"/> takes both: it needs the offer on screen, as the beat that
/// raises it plays, and still waiting in the live view, so one answered before its beat played never shows.
/// </summary>
/// <param name="Title">The encounter.</param>
/// <param name="Round">"Round 2".</param>
/// <param name="Turn">Whose turn it is, or null between turns.</param>
/// <param name="Objective">What wins the battle, or null if it doesn't say.</param>
/// <param name="Order">The next units to act, the current one first.</param>
/// <param name="More">How many more there are than <see cref="Slots"/> shows.</param>
/// <param name="Prompt">The game's banner while a reaction shown on screen waits on a player's phone, else null.</param>
/// <param name="Lead">The unit the strip starts from, which outlasts the gap between two turns.</param>
public sealed record BattleHud(string Title, string Round, string? Turn, string? Objective, ImmutableArray<HudSlot> Order, int More, string? Prompt, UnitId? Lead)
{
    /// <summary>The most units on the strip: as many as the mockup's fit in the corner the board leaves free.</summary>
    public const int Slots = 9;

    /// <summary>The label over the strip.</summary>
    public const string OrderLabel = "Turn order";

    /// <summary>The small line over a reaction prompt.</summary>
    public const string PromptLabel = "Reaction";

    /// <summary>The line under a reaction prompt.</summary>
    public const string PromptNote = "Choosing on their phone";

    /// <summary>The badge on a unit whose turn the AI finished.</summary>
    public const string AiFinishedBadge = "AI finished";

    /// <summary>How near the layout's edge a badge may go.</summary>
    public const float ScreenGutter = 16;

    private const string Hero = "hero";
    private const string Ally = "ally";
    private const int Players = 6;       // ui/manifest.json has stripes p1 to p6
    private const string NeutralStripe = "stripe_neutral";
    private const string EnemyStripe = "stripe_enemy";
    private const float LayoutWidth = 1920;
    private const float LayoutHeight = 1080;
    private const float BadgeGap = 4;
    private const int ArrowHeight = 16;  // ui/manifest.json: cursor.arrow

    /// <summary>The HUD for this moment.</summary>
    /// <param name="shown">The battle as the player has seen it.</param>
    /// <param name="live">The newest view, which says whether a reaction is still waiting.</param>
    /// <param name="banner">The game's banner in that view.</param>
    /// <param name="previous">The HUD before this one, which says where the strip was.</param>
    /// <param name="aiFinished">The unit whose turn the AI is finishing on screen, if any.</param>
    public static BattleHud Of(BattleView shown, BattleView live, string banner, BattleHud? previous, UnitId? aiFinished)
    {
        var lead = shown.Active ?? previous?.Lead;
        var from = lead is { } unit ? Math.Max(0, shown.Order.IndexOf(unit)) : 0;
        var rotated = shown.Order.Skip(from).Concat(shown.Order.Take(from));
        var slots = rotated
            .Select(id => shown.Units.FirstOrDefault(u => u.Id == id))
            .OfType<UnitView>()
            .Where(u => u.Life is "Up" or "Downed")
            .ToArray();

        return new BattleHud(Words.Encounter(shown), $"Round {shown.Round.ToString(CultureInfo.InvariantCulture)}", TurnOf(shown, aiFinished),
            ObjectiveWords.Of(shown.Objectives, shown.Units), [.. slots.Take(Slots).Select(u => Slot(u, shown.Active))], Math.Max(0, slots.Length - Slots),
            live.Reacting is { } reacting && reacting == shown.Reacting ? banner : null, lead);
    }

    /// <summary>
    /// Where a badge of <paramref name="size"/> goes, its top left on the layout: centred above whatever's highest over
    /// <paramref name="unit"/>, on the screen.
    /// </summary>
    public static Vector2 BadgeAt(BoardUnit unit, BoardFit fit, Vector2 size)
    {
        var head = fit.ToScreen(new Vector2(unit.Feet.X, Top(unit)));
        return Clamp(new Vector2(head.X - size.X / 2, head.Y - size.Y - BadgeGap), size);
    }

    /// <summary><paramref name="at"/>, moved as little as it takes for <paramref name="size"/> to stay inside the layout.</summary>
    public static Vector2 Clamp(Vector2 at, Vector2 size) => new(
        Math.Clamp(at.X, ScreenGutter, LayoutWidth - size.X - ScreenGutter),
        Math.Clamp(at.Y, ScreenGutter, LayoutHeight - size.Y - ScreenGutter));

    /// <summary>True when <paramref name="panel"/>, on the layout, lies over any of <paramref name="units"/>, from its feet to its marks.</summary>
    public static bool Covers(BoardArea panel, IEnumerable<BoardUnit> units, BoardFit fit)
        => units.Any(unit =>
        {
            var topLeft = fit.ToScreen(new Vector2(unit.Feet.X - unit.Ring, Top(unit)));
            var bottomRight = fit.ToScreen(new Vector2(unit.Feet.X + unit.Ring, unit.Feet.Y));
            return topLeft.X < panel.X + panel.Width && bottomRight.X > panel.X && topLeft.Y < panel.Y + panel.Height && bottomRight.Y > panel.Y;
        });

    private static string? TurnOf(BattleView shown, UnitId? aiFinished)
    {
        if (shown.Units.FirstOrDefault(u => u.Id == shown.Active) is not { } unit)
            return null;
        return unit.Player is { } player && unit.Id != aiFinished ? $"{player}'s turn" : $"{unit.Name} is acting";
    }

    private static HudSlot Slot(UnitView unit, UnitId? active)
        => new(unit.Id, IsCharacter(unit) ? unit.Name : Words.Short(unit.Name), unit.Sprite, UnitPlan.ToneOf(unit.Side), unit.Id == active, IsHero(unit, out _) && unit.Player is null, unit.Life == "Downed", Stripe(unit));

    private static string Stripe(UnitView unit)
    {
        if (UnitPlan.ToneOf(unit.Side) == UnitTone.Enemy)
            return EnemyStripe;
        return IsHero(unit, out var seat) && seat <= Players ? $"stripe_p{seat.ToString(CultureInfo.InvariantCulture)}" : NeutralStripe;
    }

    // The game seats members as hero1, hero2, … (a summon or a companion has its own kind of id).
    private static bool IsHero(UnitView unit, out int seat) => Numbered(unit, Hero, out seat);

    // Members and their companions have names of their own. Anything else is called what its kind is called.
    private static bool IsCharacter(UnitView unit) => IsHero(unit, out _) || Numbered(unit, Ally, out _);

    private static bool Numbered(UnitView unit, string prefix, out int number)
    {
        number = 0;
        return unit.Id.Value.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(unit.Id.Value.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static float Top(BoardUnit unit)
    {
        var top = unit.Feet.Y - unit.Sprite.AnchorY + unit.Sprite.HeadTop;
        if (unit.Bar is { } bar)
            top = MathF.Min(top, bar.TopLeft.Y);
        if (unit.Countdown is { } countdown)
            top = MathF.Min(top, countdown.Centre.Y - ArrowHeight);
        if (!unit.Icons.IsDefaultOrEmpty)
            top = MathF.Min(top, unit.IconsAt.Y);
        if (unit.ArrowAt is { } arrow)
            top = MathF.Min(top, arrow.Y - ArrowHeight);
        return top;
    }
}
