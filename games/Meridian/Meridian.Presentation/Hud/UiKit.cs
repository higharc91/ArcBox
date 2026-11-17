using Meridian.Presentation.Art;

namespace Meridian.Presentation.Hud;

/// <summary>The TV elements that wear a UI-kit piece (#335).</summary>
public enum UiElement
{
    SeatSlot,
    Figures,
    Log,
    CardPanel,
    DiceTray,
    BreachPlate,
    DebriefPlate,
}

/// <summary>
/// Which art each TV element wears (M5 plan, Task 5): its own piece, else the kit's generic <c>panel</c>, else none (the
/// M4 flat panel). A card panel wears its deck's card frame first (#334). The scene draws any piece with insets as a
/// 9-slice (Godot's StyleBoxTexture), so pieces stretch to any size.
/// </summary>
public static class UiKit
{
    /// <summary>
    /// Screen pixels per art pixel for UI-kit pieces at 1080p: whole steps only, like the tiles, so a 1 px edge stays
    /// 2 px and crisp rather than a hairline. Corners (the insets) come out at twice their size in the art.
    /// </summary>
    public const int PixelScale = 2;

    public static string Piece(UiElement element) => element switch
    {
        UiElement.SeatSlot => "seatSlot",
        UiElement.Figures => "panel",
        UiElement.Log => "logPanel",
        UiElement.CardPanel => "cardPanel",
        UiElement.DiceTray => "diceTray",
        UiElement.BreachPlate => "breachPlate",
        UiElement.DebriefPlate => "debriefPlate",
        _ => "panel",
    };

    /// <summary>The art for an element, or null. <paramref name="deck"/>: a card's deck, for a card panel.</summary>
    public static ArtEntry? For(MeridianArt art, UiElement element, CardKind? deck = null)
    {
        if (element == UiElement.CardPanel && deck is { } kind && art.Entry($"cards/{kind.ToString().ToLowerInvariant()}") is { } frame)
        {
            return frame;
        }
        var own = art.Entry($"ui/{Piece(element)}");
        // Plates are pictures in their own right; only panels fall back to the generic panel.
        return own ?? (element is UiElement.BreachPlate or UiElement.DebriefPlate ? null : art.Entry("ui/panel"));
    }

    /// <summary>
    /// The die (#335): <c>ui.die.roll</c>, a tumbling strip, and <c>ui.die.face0</c>, <c>face1</c> and <c>face2</c>, a settled
    /// still per face value (the faces read 0, 0, 1, 1, 1, 2). Null where the art isn't there: the drawn die stands in.
    /// </summary>
    public static ArtEntry? DieFace(MeridianArt art, int face) => art.Entry($"ui/die/face{face}");

    public static ArtEntry? DieRoll(MeridianArt art) => art.Entry("ui/die/roll");
}
