using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class MapBlockTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static readonly UnitSheet Goblin = TestSheets.Goblin with { MaxHp = 30 };

    private static readonly PassiveDef FireEnter = new("zone.fire.enter", HookKind.OnEnterTile, null,
        [new DamageEffect(TestAbilities.Flat("1d6"), "fire", [])], null, Target: PassiveTarget.Self);

    private static readonly PassiveDef FireStart = new("zone.fire.start", HookKind.OnTurnStart, null,
        [new DamageEffect(TestAbilities.Flat("1d6"), "fire", [])], null, Target: PassiveTarget.Self);

    private static AbilityDef OnTile(string id, TargetKind kind, AreaDef area, params EffectDef[] effects)
        => new(new AbilityId(id), id, Tier: 0, new CostDef(Action: 1), new TargetDef(kind, Range: 6, Los: false, area), [.. effects]);

    private static readonly AbilityDef FireWall = OnTile("test.fire_wall", TargetKind.Tile, new AreaDef(AreaShape.Single),
        new ZoneEffect("fire", new AreaDef(AreaShape.Single), 3, [FireEnter, FireStart]));

    /// <summary>A caster who acts first (initiative 15) and knows <paramref name="abilities"/>.</summary>
    private static UnitSheet Mage(params AbilityDef[] abilities) => (TestSheets.Cleric with { InitiativeBonus = 5 }).Knowing(abilities);

    [Fact]
    public void Zone_fire_damages_on_enter_and_turn_start()
    {
        var battle = TestBattle.On(Field)
            .Party("mage", Mage(FireWall), T(0, 0))
            .Enemy("gob", Goblin, T(4, 1))
            .With(FireWall);
        var (state, _) = battle.Start();
        Assert.Equal(U("mage"), state.Active);

        // The zone goes down on an empty tile: the tile run happens though no unit is in the area.
        var (after, events) = battle.Apply(state, new Use(U("mage"), FireWall.Id, new Target(Tile: T(3, 1))));
        var created = Assert.IsType<ZoneCreated>(events[^1]);
        var zone = Assert.Single(after.Zones);
        Assert.Same(zone, created.Zone);
        Assert.Equal((0, U("mage"), "fire", 3, false, false), (zone.Serial, zone.Owner, zone.Kind, zone.RoundsLeft, zone.Concentration, zone.Difficult));
        Assert.Equal([T(3, 1)], zone.Tiles);
        Assert.Equal(["zone.fire.enter", "zone.fire.start"], zone.Passives.Select(p => p.Id));
        Assert.Equal(1, after.NextSerial);
        Assert.DoesNotContain(events, e => e is Damaged);

        (state, _) = battle.Apply(after, new EndTurn(U("mage")));
        Assert.Equal(U("gob"), state.Active);

        // Walking in: the zone's enter passive runs on the walker, from the zone's owner.
        var die = TestAbilities.Next(state.Rng, 6)[0];
        (after, events) = battle.Apply(state, new Move(U("gob"), [T(4, 1), T(3, 1)]));
        Assert.Equal<BattleEvent>(
        [
            new Moved(U("gob"), [T(4, 1), T(3, 1)]),
            new PassiveTriggered(U("gob"), "zone.fire.enter"),
            new Damaged(U("gob"), die, "fire", 30 - die),
        ], events, new JsonEquality<BattleEvent>());

        // Starting a turn inside it: the turn-start passive.
        (state, _) = battle.Apply(after, new EndTurn(U("gob")));
        Assert.Equal(U("mage"), state.Active);
        var second = TestAbilities.Next(state.Rng, 6)[0];
        (after, events) = battle.Apply(state, new EndTurn(U("mage")));
        Assert.Equal<BattleEvent>(
        [
            new TurnEnded(U("mage")),
            new TurnStarted(U("gob"), 2),
            new PassiveTriggered(U("gob"), "zone.fire.start"),
            new Damaged(U("gob"), second, "fire", 30 - die - second),
        ], events);
        Assert.Equal(30 - die - second, after.Units[U("gob")].Hp);
    }

    [Fact]
    public void Zone_pushed_through_still_burns()
    {
        var battle = TestBattle.On(Field)
            .Party("mage", Mage(), T(0, 1))
            .Enemy("gob", Goblin, T(1, 1));
        var (state, _) = battle.Start();
        state = state with { Zones = [new Zone(0, U("mage"), "fire", [T(2, 1)], 3, false, false, [FireEnter])], NextSerial = 1 };

        // The push carries the goblin past the zone before its passive runs; it still fires (the zone is still there).
        var die = TestAbilities.Next(state.Rng, 6)[0];
        var (after, events) = battle.Run(state, "mage", "gob", T(1, 1), new PushEffect(2));
        Assert.Equal(T(3, 1), after.Units[U("gob")].Pos);
        Assert.Contains(new PassiveTriggered(U("gob"), "zone.fire.enter"), events);
        Assert.Equal(30 - die, after.Units[U("gob")].Hp);
    }

    [Fact]
    public void Difficult_zone_costs_two_move_per_tile()
    {
        var grease = OnTile("test.grease", TargetKind.Tile, new AreaDef(AreaShape.Single),
            new ZoneEffect("grease", new AreaDef(AreaShape.Single), 2, [], Difficult: true));
        var battle = TestBattle.On(Field)
            .Party("mage", Mage(grease), T(0, 0))
            .Enemy("gob", Goblin, T(5, 2))
            .With(grease);
        var (state, _) = battle.Start();

        (state, _) = battle.Apply(state, new Use(U("mage"), grease.Id, new Target(Tile: T(1, 0))));
        Assert.True(new BattleWriter(state, battle.Rules).Terrain.Difficult(T(1, 0)));

        var tooFar = Battle.Apply(state, new Move(U("mage"), [T(0, 0), T(1, 0), T(2, 0), T(3, 0), T(4, 0)]), battle.Rules);
        Assert.Equal(RejectCode.NoMove, tooFar.Rejected!.Code);
        var (after, _) = battle.Apply(state, new Move(U("mage"), [T(0, 0), T(1, 0), T(2, 0)]));
        Assert.Equal(1, after.Units[U("mage")].Economy.MoveLeft);
    }

    [Fact]
    public void Zone_rounds_tick_at_the_owner_turn_end()
    {
        var shortZone = OnTile("test.flash", TargetKind.Tile, new AreaDef(AreaShape.Single),
            new ZoneEffect("flash", new AreaDef(AreaShape.Single), 1, []));
        var instant = OnTile("test.spark", TargetKind.Tile, new AreaDef(AreaShape.Single),
            new ZoneEffect("spark", new AreaDef(AreaShape.Single), 0, []));
        var battle = TestBattle.On(Field)
            .Party("mage", Mage(shortZone, instant), T(0, 0))
            .Enemy("gob", Goblin, T(5, 2))
            .With(shortZone, instant);
        var (state, _) = battle.Start();
        Assert.Equal(U("mage"), state.Active);

        // rounds: 1 lasts until the end of the owner's next turn; rounds: 0 until the end of this one.
        (state, _) = battle.Apply(state, new Use(U("mage"), shortZone.Id, new Target(Tile: T(2, 0))));
        state = state.Change("mage", u => u with { Economy = Economy.Fresh(u.Sheet) });
        (state, _) = battle.Apply(state, new Use(U("mage"), instant.Id, new Target(Tile: T(3, 0))));
        Assert.Equal([0, 1], state.Zones.Select(z => z.Serial));

        var (after, events) = battle.Apply(state, new EndTurn(U("mage")));
        Assert.Contains(new ZoneEnded(1), events);
        Assert.DoesNotContain(new ZoneEnded(0), events);
        Assert.Equal([0], after.Zones.Select(z => z.Serial));
        Assert.Equal(1, after.Zones[0].RoundsLeft);

        (state, _) = battle.Apply(after, new EndTurn(U("gob")));
        Assert.Single(state.Zones);
        (after, events) = battle.Apply(state, new EndTurn(U("mage")));
        Assert.Contains(new ZoneEnded(0), events);
        Assert.Empty(after.Zones);
    }

    [Fact]
    public void Zone_whose_owner_left_ticks_at_round_start()
    {
        var battle = TestBattle.On(Field)
            .Party("mage", Mage(), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(4, 1))
            .Enemy("hob", TestSheets.Goblin, T(5, 2));
        var (state, _) = battle.Start();
        state = state with { Zones = [new Zone(0, U("gob"), "web", [T(2, 1)], 2, false, true, [])], NextSerial = 1 };

        // The owner dies: no turn end of its ticks the zone any more, so each new round's start does.
        (state, _) = battle.Run(state, "mage", "gob", null, new DamageEffect(TestAbilities.Flat("30"), "fire", []));
        Assert.Equal([U("mage"), U("hob")], state.Order);
        (state, _) = battle.Apply(state, new EndTurn(U("mage")));
        (state, _) = battle.Apply(state, new EndTurn(U("hob")));
        Assert.Equal(1, Assert.Single(state.Zones).RoundsLeft);

        (state, _) = battle.Apply(state, new EndTurn(U("mage")));
        var (after, events) = battle.Apply(state, new EndTurn(U("hob")));
        Assert.Contains(new ZoneEnded(0), events);
        Assert.Empty(after.Zones);
    }

    [Fact]
    public void Concentration_zone_ends_when_concentration_breaks()
    {
        var guardians = new AbilityDef(new AbilityId("test.guardians"), "Guardians", Tier: 0, new CostDef(Action: 1, Concentration: true),
            new TargetDef(TargetKind.Self, Range: 0, Los: false, new AreaDef(AreaShape.Self)),
            [new ZoneEffect("guardians", new AreaDef(AreaShape.Burst, 1), 10, [], Concentration: true)]);
        var battle = TestBattle.On(Field)
            .Party("mage", Mage(guardians), T(1, 1))
            .Enemy("gob", Goblin, T(5, 2))
            .With(guardians);
        var (state, _) = battle.Start();

        var (after, events) = battle.Apply(state, new Use(U("mage"), guardians.Id, new Target()));
        var zone = Assert.Single(after.Zones);
        Assert.True(zone.Concentration);
        Assert.Equal([T(0, 1), T(1, 0), T(1, 1), T(1, 2), T(2, 1)], zone.Tiles);
        Assert.Single(events.OfType<ZoneCreated>());

        (after, events) = battle.Run(after, "gob", "mage", T(1, 1), new DispelEffect(Concentration: true));
        Assert.Equal<BattleEvent>([new ConcentrationBroken(U("mage"), guardians.Id), new ZoneEnded(0)], events);
        Assert.Empty(after.Zones);
    }

    [Fact]
    public void Hazard_hurts_units_that_enter()
    {
        var spikes = OnTile("test.spikes", TargetKind.Tile, new AreaDef(AreaShape.Single),
            new HazardEffect("hazards.spikes", new AreaDef(AreaShape.Single), 2, [new DamageEffect(TestAbilities.Flat("1d4"), "piercing", [])]));
        var battle = TestBattle.On(Field)
            .Party("mage", Mage(spikes), T(0, 0))
            .Enemy("gob", Goblin, T(4, 1))
            .With(spikes);
        var (state, _) = battle.Start();

        var (after, events) = battle.Apply(state, new Use(U("mage"), spikes.Id, new Target(Tile: T(3, 1))));
        var hazard = Assert.Single(after.Zones);
        Assert.Equal(("hazards.spikes", 2), (hazard.Kind, hazard.RoundsLeft));
        Assert.Single(events.OfType<ZoneCreated>());

        (state, _) = battle.Apply(after, new EndTurn(U("mage")));
        var die = TestAbilities.Next(state.Rng, 4)[0];
        (after, events) = battle.Apply(state, new Move(U("gob"), [T(4, 1), T(3, 1)]));
        Assert.Equal<BattleEvent>(
        [
            new Moved(U("gob"), [T(4, 1), T(3, 1)]),
            new PassiveTriggered(U("gob"), "hazard.hazards.spikes"),
            new Damaged(U("gob"), die, "piercing", 30 - die),
        ], events, new JsonEquality<BattleEvent>());
    }

    [Fact]
    public void Alter_terrain_raises_and_reprojects_units_on_top()
    {
        const string hills = """
            0. 0. 0. 0. 0.
            0. 0. 0. 8. 0.
            0. 0. 0. 0. 1.
            """;
        var battle = TestBattle.On(hills)
            .Party("geo", Mage(), T(0, 0))
            .Enemy("gob", Goblin, T(2, 1));
        var (state, _) = battle.Start();

        var (after, events) = battle.Run(state, "geo", null, T(2, 1), new AlterTerrainEffect(new AreaDef(AreaShape.Burst, 1), 2));

        // Every tile of the burst rises 2, the 8 only to the cap of 9.
        var altered = Assert.IsType<TerrainAltered>(Assert.Single(events));
        Assert.Equal([T(1, 1), T(2, 0), T(2, 1), T(2, 2), T(3, 1)], altered.Tiles);
        Assert.Equal(2, altered.Delta);
        Assert.Equal([(T(1, 1), 2), (T(2, 0), 2), (T(2, 1), 2), (T(2, 2), 2), (T(3, 1), 1)],
            after.HeightDelta.Select(kv => (kv.Key, kv.Value)));

        // The goblin stands on top of its raised tile: it now has the high ground over the caster.
        var terrain = new BattleWriter(after, battle.Rules).Terrain;
        Assert.Equal(T(2, 1), after.Units[U("gob")].Pos);
        Assert.Equal(2, terrain.HeightAt(after.Units[U("gob")].Pos));
        Assert.Equal(9, terrain.HeightAt(T(3, 1)));
        Assert.Equal(2, Reach.HighGroundBonus(terrain, T(2, 1), T(0, 0)));

        // Sinking stops at 0; a change that moves nothing emits nothing.
        (after, events) = battle.Run(after, "geo", null, T(4, 2), new AlterTerrainEffect(new AreaDef(AreaShape.Single), -3));
        Assert.Equal<BattleEvent>([new TerrainAltered([T(4, 2)], -3)], events, new JsonEquality<BattleEvent>());
        Assert.Equal(-1, after.HeightDelta[T(4, 2)]);
        (after, events) = battle.Run(after, "geo", null, T(3, 1), new AlterTerrainEffect(new AreaDef(AreaShape.Single), 2));
        Assert.Empty(events);

        // Lowering a raised tile back to its base height leaves no delta behind.
        (after, _) = battle.Run(after, "geo", null, T(2, 1), new AlterTerrainEffect(new AreaDef(AreaShape.Single), -2));
        Assert.False(after.HeightDelta.ContainsKey(T(2, 1)));
    }

    [Fact]
    public void Tile_blocks_run_once_at_the_aim_and_unit_blocks_per_unit_in_authored_order()
    {
        var misty = OnTile("test.misty_step", TargetKind.EmptyTile, new AreaDef(AreaShape.Single), new TeleportEffect(6));
        var burning = OnTile("test.burning_ground", TargetKind.Tile, new AreaDef(AreaShape.Burst, 1),
            new DamageEffect(TestAbilities.Flat("1d4"), "fire", []),
            new ZoneEffect("embers", new AreaDef(AreaShape.Burst, 1), 2, []));
        var battle = TestBattle.On(Field)
            .Party("mage", Mage(misty, burning), T(0, 0))
            .Enemy("gob", Goblin, T(3, 1))
            .Enemy("hob", Goblin, T(4, 1))
            .With(misty, burning);
        var (state, _) = battle.Start();

        // A teleport onto an empty tile: nobody is in the area, the tile run moves the caster.
        var (after, events) = battle.Apply(state, new Use(U("mage"), misty.Id, new Target(Tile: T(2, 2))));
        Assert.Equal(new Teleported(U("mage"), T(0, 0), T(2, 2)), events[^1]);

        // Damage once for each goblin (authored first), then one zone for the whole ability.
        after = after.Change("mage", u => u with { Economy = Economy.Fresh(u.Sheet) });
        (after, events) = battle.Apply(after, new Use(U("mage"), burning.Id, new Target(Tile: T(3, 1))));
        Assert.Single(events.OfType<ZoneCreated>());
        Assert.Equal([U("gob"), U("hob")], events.OfType<Damaged>().Select(d => d.Unit));
        Assert.IsType<ZoneCreated>(events[^1]);
    }

    [Fact]
    public void Attack_then_teleport_attacks_before_teleporting()
    {
        var strikeAndBlink = OnTile("test.strike_and_blink", TargetKind.Tile, new AreaDef(AreaShape.Burst, 1),
            new AttackEffect("spell", Stat.Wis, [new DamageEffect(TestAbilities.Flat("1d4"), "force", [])], [], []),
            new TeleportEffect(6),
            new DamageEffect(TestAbilities.Flat("1"), "force", []));
        var battle = TestBattle.On(Field)
            .Party("mage", Mage(strikeAndBlink), T(0, 0))
            .Enemy("gob", Goblin, T(3, 1))
            .With(strikeAndBlink);
        var (state, _) = battle.Start();

        var (after, events) = battle.Apply(state, new Use(U("mage"), strikeAndBlink.Id, new Target(Tile: T(3, 2))));
        var attacked = events.IndexOf(events.OfType<AttackResolved>().Single());
        var teleported = events.IndexOf(new Teleported(U("mage"), T(0, 0), T(3, 2)));
        var lastDamage = events.IndexOf(new Damaged(U("gob"), 1, "force", after.Units[U("gob")].Hp));
        Assert.True(attacked > 0 && attacked < teleported && teleported < lastDamage,
            $"attack at {attacked}, teleport at {teleported}, final damage at {lastDamage}");
        Assert.Equal(T(3, 2), after.Units[U("mage")].Pos);
    }
}
