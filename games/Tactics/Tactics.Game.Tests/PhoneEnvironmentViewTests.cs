using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game.Tests;

public sealed class PhoneEnvironmentViewTests
{
    [Fact]
    public void Current_surface_tracks_the_actual_tile_and_ice_suppresses_water_art()
    {
        var rules = PhoneFixtureTests.Pack.Value;
        var s = new TacticsGameTests.Session(); s.ToBattle();
        var state = TacticsDocument.Read(s.Lease.Document).Battle!.State with { Environment = true };
        var me = state.Units.Values.First(u => u.Sheet.Side == Side.Party) with { Pos = new Tile(6, 7) };
        state = state with { Units = state.Units.SetItem(me.Id, me) };
        Assert.Equal(["water"], PhoneSurfaces.At(state, me, rules));
        state = state with { Zones = [new Zone(1, me.Id, "ice_sheet", [me.Pos], 3, false, true, [], Surface: true)] };
        Assert.Equal(["ice_sheet"], PhoneSurfaces.At(state, me, rules));
        Assert.Empty(PhoneSurfaces.At(state, me with { Pos = new Tile(1, 1) }, rules));
        Assert.Empty(PhoneSurfaces.At(state with { Environment = false }, me, rules));
        Assert.Empty(PhoneSurfaces.At(state, me with { Life = LifeState.Out }, rules));
    }

    [Fact]
    public void Compaction_keeps_empty_aims_with_reaction_spread_and_omits_absent_fields()
    {
        var id = new UnitId("hero1");
        var target = new TargetChoice(new Target(Tile: new Tile(1, 0)), new Tile(1, 0), [], [new Tile(2, 0)]);
        var choices = new TurnChoices(id, new TurnEconomy(4, true, true, true, 0), [],
            [new AbilityChoice(new AbilityId("test"), null, "Test", "", new CostDef(), [target])], false);
        var phone = PhoneChoices.Of(choices);
        Assert.Single(phone.Abilities[0].Targets);
        Assert.Empty(phone.Abilities[0].Tiles);
        Assert.Contains("\"spread\"", JsonSerializer.Serialize(phone, ArcJson.Options));
        var bare = PhoneChoices.Of(choices with { Abilities = [choices.Abilities[0] with { Targets = [target with { Spread = default }] }] });
        Assert.Empty(bare.Abilities[0].Targets);
        Assert.Single(bare.Abilities[0].Tiles);
        Assert.DoesNotContain("spread", JsonSerializer.Serialize(bare, ArcJson.Options));
    }
}
