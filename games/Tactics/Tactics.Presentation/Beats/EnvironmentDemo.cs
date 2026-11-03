using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Beats;

/// <summary>Dev-only reaction reel for <c>--tactics-demo=environment</c>, through the ordinary TV beat path.</summary>
public static class EnvironmentDemo
{
    /// <summary>Ford conduction, frost and steam; a burning bridge, spread and scorch; oil and burning webs.</summary>
    public static IReadOnlyList<ThingsDemoStep> Steps()
    {
        var steps = new List<ThingsDemoStep>();
        var mage = new UnitId("wizard");
        var scout = new UnitId("scout");
        var fighter = new UnitId("fighter");
        static UnitView Unit(UnitId id, string side, string sprite, Tile at) => new(id, id.Value, side, sprite, at, 24, 24, 0, "Up", 0, [], null);
        BattleView battle = new("warlord_bridge", "warlord_bridge", 1, [mage, scout], mage,
            [Unit(mage, "Party", "characters.class_wizard", new(4, 10)), Unit(scout, "Enemy", "characters.goblin_archer", new(7, 10)),
             Unit(fighter, "Party", "characters.class_fighter", new(9, 10))], [], null, BoardChanges.None);
        var seq = 0;
        void Add(double at, string label, params BattleEvent[] events)
        {
            if (events.Length > 0)
            {
                var beat = new Beat(++seq, mage, [.. events]);
                foreach (var e in events)
                    battle = DisplayedBattle.Apply(battle, e, battle);
                battle = battle with { Beats = [beat] };
            }
            var view = new TacticsHostView("Battle", label, null, 1, [], null, battle, null, null);
            steps.Add(new ThingsDemoStep(label, at, view, [(0.35, "environment-" + label)], false));
        }
        static Zone Zone(int serial, string kind, ImmutableArray<Tile> tiles) => new(serial, null, kind, tiles, 3, false, false, [], Surface: true);
        ImmutableArray<Tile> water = [new(6, 7), new(7, 10), new(6, 8), new(7, 8)];
        Add(0, "The ford");
        Add(2, "Lightning through water", new TileReacted("conduct", mage, water),
            new Rolled(scout, RollKind.Save, new RollResult([8], 8, 2, 10, 12, false, false, false)), new Damaged(scout, 6, "lightning", 18));
        Add(5, "Freeze the ford", new TileReacted("freeze", mage, water), new ZoneCreated(Zone(1, "ice_sheet", water)));
        Add(8, "Thaw to steam", new TileReacted("thaw", mage, water), new ZoneEnded(1));
        battle = battle with { Map = "warlord_bridge", Encounter = "warlord_bridge", Board = BoardChanges.None, Beats = [],
            Units = [Unit(mage, "Party", "characters.class_wizard", new(4, 5)), Unit(fighter, "Party", "characters.class_fighter", new(7, 6)),
                Unit(scout, "Enemy", "characters.goblin_archer", new(9, 5))] };
        seq = 0;
        Add(11, "Timber bridge");
        ImmutableArray<Tile> fire = [new(5, 5), new(5, 6)];
        ImmutableArray<Tile> spread = [new(6, 5), new(6, 6), new(7, 5), new(7, 6)];
        Add(13, "Ignite the deck", new TileReacted("ignite", mage, fire), new ZoneCreated(Zone(2, "burning", fire)));
        Add(16, "Fire spreads", new RoundStarted(2, [mage, fighter, scout]), new TileReacted("spread", mage, spread), new ZoneCreated(Zone(3, "burning", spread)));
        Add(19, "Douse the flames", new TileReacted("douse", mage, fire.AddRange(spread)), new ZoneEnded(2), new ZoneEnded(3),
            new ZoneCreated(Zone(4, "scorched", fire.AddRange(spread))));
        ImmutableArray<Tile> oil = [new(8, 5), new(8, 6)];
        Add(22, "Oil on the deck", new ZoneCreated(Zone(5, "oil", oil)));
        Add(25, "Oil catches fire", new TileReacted("ignite", mage, oil), new ZoneEnded(5), new ZoneCreated(Zone(6, "burning", oil)));
        ImmutableArray<Tile> web = [new(3, 5), new(3, 6)];
        Add(28, "Webs across the path", new ZoneCreated(Zone(7, "web", web)));
        Add(31, "Burn away the webs", new TileReacted("burn_web", mage, web), new ZoneEnded(7), new ZoneCreated(Zone(8, "burning", web)));
        return steps;
    }
}
