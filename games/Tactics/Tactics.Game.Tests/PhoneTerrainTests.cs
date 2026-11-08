using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class PhoneTerrainTests
{
    [Fact]
    public void Snapshot_matches_live_heights_difficult_ground_deep_tiles_and_blocking_props()
    {
        var s = new Session(); s.ToBattle();
        var state = s.Game.Document.Battle!.State;
        var map = Rules.MapFor(state.MapId)!;
        var changed = new Tile(1, 1);
        var sunk = new Tile(2, 1);
        var difficult = new Tile(3, 1);
        state = state with { HeightDelta = state.HeightDelta.SetItem(changed, 4), Sunk = [sunk],
            Zones = [new Zone(1, new UnitId("hero1"), "web", [difficult], 2, false, true, [])] };
        var before = JsonSerializer.Serialize(state, ArcJson.Options);
        var snapshot = PhoneTerrain.Of(state, Rules);
        var live = new BattleWriter(state, Rules).Terrain;
        Assert.Equal(map.Width * map.Height, snapshot.Heights.Length);
        for (var i = 0; i < snapshot.Heights.Length; i++)
        {
            var tile = new Tile(i % map.Width, i / map.Width);
            Assert.Equal(live.HeightAt(tile), snapshot.Heights[i]);
            Assert.Equal(live.Deep(tile), (snapshot.Flags[i] & (int)TileFlags.Deep) != 0);
            Assert.Equal(live.Difficult(tile), (snapshot.Flags[i] & (int)TileFlags.Difficult) != 0);
            Assert.Equal(!live.Walkable(tile), (snapshot.Flags[i] & (int)TileFlags.Impassable) != 0);
        }
        Assert.Contains("Difficult ground", Assert.Single(snapshot.Zones).Detail);
        Assert.Equal(before, JsonSerializer.Serialize(state, ArcJson.Options));
    }

    [Fact]
    public void Nested_damage_is_marked_as_hazard_but_a_healing_zone_is_not()
    {
        var s = new Session(); s.ToBattle();
        var state = s.Game.Document.Battle!.State;
        var tile = state.Units[new UnitId("hero1")].Pos;
        var heal = new PassiveDef("healing", HookKind.OnTurnStart, null, [new HealEffect(new Dice(1, 4, 0), [])], null);
        var damage = new PassiveDef("fire", HookKind.OnTurnStart, null,
            [new SaveEffect(Stat.Dex, 12, [new DamageEffect(new Dice(1, 6, 0), Type: "fire", Bonus: [])], [], true)], null);
        state = state with { Zones = [new Zone(1, new UnitId("hero1"), "healing_grove", [tile], 2, false, false, [heal], Affects: AffectedSide.Allies),
            new Zone(2, new UnitId("hero1"), "fire_trap", [new Tile(1, 1)], 2, false, false, [damage])] };
        var zones = PhoneTerrain.Of(state, Rules).Zones;
        Assert.False(zones[0].Hazard);
        Assert.Contains("Affects allies", zones[0].Detail);
        Assert.True(zones[1].Hazard);
        Assert.Contains("fire", zones[1].Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Turn_view_contains_live_terrain_without_waiting_for_a_pack_fetch()
    {
        var s = new Session(); s.ToBattle();
        for (var step = 0; step < 500; step++)
        {
            for (var p = 0; p < 4; p++)
                if (s.Screen(p) is { Screen: TacticsGame.Screens.Turn } turn)
                {
                    var terrain = turn.Data.GetProperty("terrain");
                    Assert.True(terrain.GetProperty("w").GetInt32() > 0);
                    Assert.Equal(terrain.GetProperty("w").GetInt32() * terrain.GetProperty("h").GetInt32(), terrain.GetProperty("heights").GetArrayLength());
                    Assert.True(JsonSerializer.SerializeToUtf8Bytes(turn, ArcJson.Options).Length < 64 * 1024);
                    return;
                }
            s.Harness.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Fail("No turn view was available.");
    }
}
