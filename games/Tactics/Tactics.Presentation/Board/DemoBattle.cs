using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>
/// Dev-only views for <c>--tactics-demo=&lt;map&gt;[:changed]</c>: a battle on a map with no server, so the board can be
/// looked at. <c>:changed</c> adds what a fight does to a map: zones, raised tiles (one on the back corner, burning, which
/// must leave the board where it was) and the bridge's tiles sunk. <c>:fight</c> adds one of every kind of unit: a turn in
/// progress, a Downed character, conditions, a summon, flyers, a boss wider than a tile, a sprite the pack doesn't have, and
/// Out and Dead units, which aren't drawn, and on the HUD a long turn order and a reaction prompt.
/// </summary>
public static class DemoBattle
{
    private const string Changed = ":changed";
    private const string Fight = ":fight";

    private static UnitView Unit(string id, string side, string? sprite, int x, int y, int hp, int maxHp, string life = "Up", int countdown = 0,
        params string[] conditions)
        => new(new UnitId(id), id, side, sprite, new Tile(x, y), hp, maxHp, 0, life, countdown, [.. conditions], null);

    private static ImmutableArray<UnitView> Units() =>
    [
        Unit("fighter", "Party", "characters.class_fighter", 5, 5, 28, 34),
        Unit("cleric", "Party", "characters.class_cleric", 3, 7, 0, 22, "Downed", 2, "prone"),
        Unit("wizard", "Party", "characters.class_wizard", 2, 9, 9, 18, "Up", 0, "poisoned", "hasted"),
        Unit("rogue", "Party", "characters.class_rogue", 4, 9, 14, 20, "Up", 0, "burning", "marked", "slowed", "inspired", "bleeding"),
        Unit("hound", "Party", "bosses.hound.tan", 1, 9, 11, 12),
        Unit("nobody", "Party", null, 3, 10, 10, 10),
        Unit("gone", "Party", "characters.class_monk", 2, 11, 0, 20, "Out"),
        Unit("goblin", "Enemy", "characters.goblin_cutthroat", 9, 5, 3, 7),
        Unit("scout", "Enemy", "characters.kobold", 5, 7, 5, 5),
        Unit("lookout", "Enemy", "characters.kobold", 0, 0, 5, 5),
        Unit("spider", "Enemy", "beasts.spider.black", 9, 8, 6, 11, "Up", 0, "stunned"),
        Unit("archer", "Enemy", "characters.goblin_archer", 12, 3, 7, 7),
        Unit("wolf", "Enemy", "beasts.wolf.grey", 10, 6, 11, 11),
        Unit("troll", "Enemy", "bosses.troll.troll", 11, 9, 50, 84),
        Unit("dragon", "Enemy", "bosses.dragon.red", 10, 2, 120, 200),
        Unit("wraith", "Enemy", "bosses.wraith.wraith", 12, 7, 20, 30),
        Unit("mystery", "Enemy", "nonsense.key", 11, 11, 4, 8),
        Unit("dead", "Enemy", "characters.bandit", 9, 3, 0, 8, "Dead"),
    ];

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

    /// <summary>The host view for a demo spec: a map id, with <c>:changed</c> or <c>:fight</c> or neither.</summary>
    public static TacticsHostView View(string spec)
    {
        var changed = spec.EndsWith(Changed, StringComparison.Ordinal);
        var fight = spec.EndsWith(Fight, StringComparison.Ordinal);
        var map = changed ? spec[..^Changed.Length] : fight ? spec[..^Fight.Length] : spec;
        var units = fight ? Units() : [];
        var battle = new BattleView(map, map, 2, [.. units.Select(u => u.Id)], fight ? new UnitId("fighter") : null, units, [], null,
            changed ? Changes() : BoardChanges.None, ObjectivesDef.Default, fight ? new UnitId("rogue") : null);
        return new TacticsHostView("Battle", fight ? "rogue can react" : "Board demo", null, 1, [], null, battle, null, null);
    }
}
