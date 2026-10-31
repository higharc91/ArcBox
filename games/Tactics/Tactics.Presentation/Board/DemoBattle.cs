using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>
/// Dev-only views for <c>--tactics-demo=&lt;map&gt;[:changed]</c>: a battle on a map with no server, so the board can be
/// looked at. <c>:changed</c> adds what a fight does to a map: zones, raised tiles (one on the back corner, burning, which
/// must leave the board where it was) and the bridge's tiles sunk.
/// </summary>
public static class DemoBattle
{
    private const string Changed = ":changed";

    private static BoardChanges Changes()
    {
        ImmutableArray<Tile> bridge = [.. Enumerable.Range(5, 2).SelectMany(y => Enumerable.Range(5, 4).Select(x => new Tile(x, y)))];
        return new BoardChanges(
            [
                new ZoneView(1, "fire", [new Tile(0, 0), new Tile(3, 5), new Tile(3, 6), new Tile(4, 6)]),
                new ZoneView(2, "web", [new Tile(10, 4), new Tile(10, 5)]),
                new ZoneView(3, "ice", [new Tile(11, 6), new Tile(12, 6)]),
                new ZoneView(4, "poison", [new Tile(3, 8), new Tile(4, 8)]),
                new ZoneView(5, "grand_sigil", [new Tile(10, 9), new Tile(11, 9)]),
            ],
            [.. bridge.Select(t => new HeightChange(t, -2)), new HeightChange(new Tile(11, 8), 2), new HeightChange(new Tile(0, 0), 2)],
            bridge);
    }

    /// <summary>The host view for a demo spec: a map id, with <c>:changed</c> or without.</summary>
    public static TacticsHostView View(string spec)
    {
        var changed = spec.EndsWith(Changed, StringComparison.Ordinal);
        var map = changed ? spec[..^Changed.Length] : spec;
        var battle = new BattleView(map, map, 1, [], null, [], [], null, changed ? Changes() : BoardChanges.None);
        return new TacticsHostView("Battle", "Board demo", null, 1, [], null, battle, null, null);
    }
}
