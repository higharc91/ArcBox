using Tactics.Sim;

namespace Tactics.Logic.Tests;

public sealed class EnvironmentTallyTests
{
    private static UnitId U(string id) => new(id);
    private static (EnvironmentTally Tally, BattleState State) Fresh()
    {
        var b = TestBattle.On("0. 0~ 0~ 0~").WithEnvironment(BasePackTests.Rules.Environment)
            .Party("mage", TestSheets.Cleric, new Tile(0, 0)).Party("friend", TestSheets.Fighter, new Tile(1, 0))
            .Enemy("foe", TestSheets.Goblin, new Tile(3, 0));
        var state = b.Start().State;
        var tally = new EnvironmentTally(b.Rules); tally.Read([], state);
        return (tally, state);
    }

    [Fact]
    public void Conducted_friendly_damage_counts_soaked_damage_not_overkill_or_later_attacks()
    {
        var (t, s) = Fresh(); var hp = s.Units[U("friend")].Hp;
        t.Read([new TileReacted("conduct", U("mage"), [new Tile(1, 0), new Tile(3, 0)]),
            new TempHpGained(U("friend"), 2), new Rolled(U("friend"), RollKind.Save, new RollResult([8], 8, 1, 9, 12, false, false, false)),
            new Damaged(U("friend"), 5, "lightning", hp - 3),
            new AbilityUsed(U("foe"), new AbilityId("common.attack"), new Target(Unit: U("friend")), null),
            new Damaged(U("friend"), 100, "lightning", 0)], s);
        Assert.Equal(1, t.Snapshot().Reactions);
        Assert.Equal(5, t.Snapshot().FriendlyConductionDamage);
    }

    [Fact]
    public void Other_reaction_markers_do_not_erase_pending_conduction()
    {
        var (t, s) = Fresh(); var hp = s.Units[U("friend")].Hp;
        t.Read([new TileReacted("conduct", U("mage"), [new Tile(1, 0)]), new TileReacted("ignite", U("mage"), [new Tile(0, 0)]),
            new Rolled(U("friend"), RollKind.Save, new RollResult([8], 8, 1, 9, 12, false, false, false)),
            new Damaged(U("friend"), 4, "lightning", hp - 4)], s);
        Assert.Equal(2, t.Snapshot().Reactions);
        Assert.Equal(4, t.Snapshot().FriendlyConductionDamage);
    }

    [Fact]
    public void Burning_tile_rounds_count_unique_tiles_present_during_each_round_without_expiry_ghosts()
    {
        var (t, s) = Fresh();
        var fire = new Zone(1, U("mage"), "burning", [new Tile(0, 0), new Tile(1, 0)], 1, false, false, [], Surface: true);
        t.Read([new ZoneCreated(fire), new ZoneCreated(fire with { Serial = 2 }), new ZoneEnded(1), new ZoneEnded(2),
            new RoundStarted(2, []), new ZoneCreated(fire with { Serial = 3, Tiles = [new Tile(2, 0)] }), new TurnStarted(U("mage"), 2),
            new RoundStarted(3, []), new TurnSkipped(U("mage"), "incapacitated"),
            new RoundStarted(4, []), new ZoneEnded(3), new TurnStarted(U("mage"), 4)], s);
        Assert.Equal(4, t.Snapshot().BurningTileRounds);
    }

    [Fact]
    public void Surface_terminal_deaths_are_distinct_from_downs_and_ordinary_spell_zone_damage()
    {
        var (t, s) = Fresh();
        t.Read([new PassiveTriggered(U("foe"), "surface.burning", U("mage")), new Damaged(U("foe"), 99, "fire", 0), new Died(U("foe")),
            new PassiveTriggered(U("friend"), "surface.burning", U("mage")), new Damaged(U("friend"), 99, "fire", 0), new Downed(U("friend")),
            new TurnStarted(U("mage"), 2), new PassiveTriggered(U("friend"), "wall_of_fire.burn", U("mage")), new CountdownTicked(U("friend"), 0),
            new UnitOut(U("friend"), new InjuryId("scar"))], s);
        Assert.Equal(1, t.Snapshot().SurfaceDeaths);
        t.Read([new PassiveTriggered(U("friend"), "surface.burning", U("mage")), new CountdownTicked(U("friend"), 0),
            new UnitOut(U("friend"), new InjuryId("scar"))], s);
        Assert.Equal(2, t.Snapshot().SurfaceDeaths);
    }
}
