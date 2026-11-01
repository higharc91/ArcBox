using System.Collections.Immutable;

namespace Tactics.Presentation.Board;

/// <summary>
/// Each player's colour in Wardenflame: the ring at their walker's feet on the TV (interaction review 5b, item 11), and 5d's vote dots.
/// They're the six the phone's lobby already gives its player chips, <c>--p1</c> to <c>--p6</c> in <c>web/play/src/games/tactics/lobby.css</c>,
/// which are mockup 02's, so the phone and the TV agree. A seat past the sixth takes the colours round again.
/// <para>
/// Bright on their own, they're no brighter than a sunlit path, so on the ground they're drawn over <see cref="Outline"/>: every one of them is
/// at least 7.9:1 against it, which is what keeps them off grass, stone and ash alike. Sky and violet are close for someone without green
/// cones, and orange and yellow without blue ones, so a ring is never the only thing that tells two players apart: the leader has the crown.
/// </para>
/// </summary>
public static class SeatColours
{
    /// <summary>The colours, first seat first, as <c>#rrggbb</c>.</summary>
    public static readonly ImmutableArray<string> Hex = ["#ff8a3d", "#3ec7ff", "#c08cff", "#5fe08a", "#ffd447", "#ff7fb6"];

    /// <summary>What a seat's colour is drawn over, so it reads on any ground.</summary>
    public static readonly Rgb Outline = Rgb.Hex("#0b0b10");

    private static readonly ImmutableArray<Rgb> Colours = [.. Hex.Select(Rgb.Hex)];

    /// <summary>The colour of the seat at <paramref name="seat"/>, counting from 0.</summary>
    public static Rgb Of(int seat) => Colours[((seat % Colours.Length) + Colours.Length) % Colours.Length];
}
