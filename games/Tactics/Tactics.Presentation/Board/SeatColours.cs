using System.Collections.Immutable;

namespace Tactics.Presentation.Board;

/// <summary>
/// Wardenflame's six player colours, the ones the phone's lobby gives its player chips (<c>--p1</c> to <c>--p6</c> in
/// <c>web/play/src/games/tactics/lobby.css</c>, mockup 02's). Nothing on the TV uses them yet. The phone picks a chip's colour by its place in the
/// room's player list, which shifts when someone leaves, and the TV has no such number at all, so a walker's ring can't be their colour without
/// a seat number both share. That waits for 5d's vote dots (interaction review 5b, item 11). Until then this only pins the palette.
/// <para>
/// Every one of them stands at least 7.9:1 off <see cref="UnitPlan.RingOutline"/>, and is brighter than grass, stone and ash, so a ring in them
/// would read on any ground. Sky and violet are close without green cones, and orange and yellow without blue ones, so a colour must never be
/// the only thing that tells two players apart.
/// </para>
/// </summary>
public static class SeatColours
{
    /// <summary>The colours, the first chip's first, as <c>#rrggbb</c>.</summary>
    public static readonly ImmutableArray<string> Hex = ["#ff8a3d", "#3ec7ff", "#c08cff", "#5fe08a", "#ffd447", "#ff7fb6"];

    private static readonly ImmutableArray<Rgb> Colours = [.. Hex.Select(Rgb.Hex)];

    /// <summary>The colour at <paramref name="index"/>, counting from 0, going round again past the last.</summary>
    public static Rgb Of(int index) => Colours[((index % Colours.Length) + Colours.Length) % Colours.Length];
}
