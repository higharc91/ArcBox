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
    [Fact]
    public void Compaction_interns_repeated_previews_without_losing_roll_or_damage_information()
    {
        var id = new UnitId("hero1");
        var preview = new UnitPreview(new("foe"), 4.2, 0, new AimRoll("hit", 65, 5, 13, Edge.Advantage, "flanking"), 0, 16);
        var targets = new[] { new Tile(1, 0), new Tile(2, 0) }.Select(t => new TargetChoice(new Target(Tile: t), t, [preview])).ToImmutableArray();
        var choices = new TurnChoices(id, new TurnEconomy(4, true, true, true, 0), [],
            [new AbilityChoice(new("test"), null, "Test", "", new CostDef(), targets)], false);
        var ability = Assert.Single(PhoneChoices.Of(choices).Abilities);
        var pooled = Assert.Single(ability.Previews);
        Assert.Single(ability.Rolls);
        foreach (var target in ability.Targets)
        {
            Assert.Empty(target.Affected);
            var restored = ability.Previews[Assert.Single(target.PreviewIds)];
            Assert.Equal(preview.Unit, restored.Unit); Assert.Equal(preview.Damage, restored.Damage);
            Assert.Equal(preview.Heal, restored.Heal); Assert.Equal(preview.DamageMin, restored.DamageMin);
            Assert.Equal(preview.DamageMax, restored.DamageMax);
            Assert.Equal(preview.Roll, ability.Rolls[restored.Roll!.Value]);
        }
        Assert.Equal(0, pooled.Roll);
    }

}
