using System.Collections.Immutable;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class ScriptedBossTests
{
    private static RulesPack Rules => BasePackTests.Rules;
    private static ImmutableArray<PartyMember> Party(int count)
        => [.. Enumerable.Range(0, count).Select(i => new PartyMember(new UnitId("hero"+i),
            SheetBuilder.DefaultCharacter("Hero"+i, "human", "fighter", null, 10, Rules), new PlayerControlled(new ProfileId("pr_"+i))))];

    [Fact]
    public void Every_shipped_placed_or_spawned_monster_phase_has_a_trigger()
        => Assert.DoesNotContain(Rules.Warnings, w => w.Message.Contains("no trigger enters it", StringComparison.Ordinal));

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Hierophants_keep_the_original_party_size_counts_and_enter_phases_independently(int count)
    {
        var state = Battle.Start(Encounters.Setup("hall_of_the_flame", Party(count), Difficulty.Normal, Rules), Rules, 1).State;
        var bosses = state.Units.Values.Where(u => u.Sheet.Name == "Ember Hierophant").ToArray();
        Assert.Equal(count == 4 ? 1 : 2, bosses.Length);
        var north = bosses.Single(u => u.Sheet.Tags.Contains("hierophant_north"));
        var hurt = state with { Units = state.Units.SetItem(north.Id, north with { Hp = north.Sheet.MaxHp / 2 - 1 }) };
        var first = Battle.Apply(hurt, new EndTurn(hurt.Active!.Value), Rules);
        Assert.Contains(new PhaseEntered(north.Id, 1), first.Events);
        if (count == 4) return;
        var south = first.State.Units.Values.Single(u => u.Sheet.Tags.Contains("hierophant_south"));
        Assert.DoesNotContain(first.Events.OfType<PhaseEntered>(), e => e.Unit == south.Id);
        Assert.DoesNotContain(new AbilityId("cult_leader.fanatic_zeal"), south.Sheet.Abilities);
        var next = first.State with { Units = first.State.Units.SetItem(south.Id, south with { Hp = south.Sheet.MaxHp / 2 - 1 }) };
        var second = Battle.Apply(next, new EndTurn(next.Active!.Value), Rules);
        Assert.Contains(new PhaseEntered(south.Id, 1), second.Events);
        Assert.DoesNotContain(second.Events.OfType<PhaseEntered>(), e => e.Unit == north.Id);
    }

    [Fact]
    public void Occupying_the_whole_dragon_roost_does_not_cancel_the_dragon_or_its_fury_phase()
    {
        var state = Battle.Start(Encounters.Setup("hall_of_the_flame", Party(6), Difficulty.Normal, Rules), Rules, 1).State;
        var roost = Rules.MapFor(state.MapId)!.Zones["dragon_roost"];
        var heroes = state.Units.Values.Where(u => u.Controller is PlayerControlled).Take(roost.Length).ToArray();
        Assert.Equal(roost.Length, heroes.Length);
        for (var i = 0; i < heroes.Length; i++)
            state = state with { Units = state.Units.SetItem(heroes[i].Id, heroes[i] with { Pos = roost[i] }) };
        state = state with { Round = 3 };
        var arrived = Battle.Apply(state, new EndTurn(state.Active!.Value), Rules);
        var spawn = Assert.Single(arrived.Events.OfType<Spawned>());
        Assert.DoesNotContain(spawn.At, roost);
        var dragon = arrived.State.Units[spawn.Unit];
        Assert.Contains("dragon", dragon.Sheet.Tags);
        var hurt = arrived.State with { Units = arrived.State.Units.SetItem(dragon.Id, dragon with { Hp = dragon.Sheet.MaxHp / 2 - 1 }) };
        var fury = Battle.Apply(hurt, new EndTurn(hurt.Active!.Value), Rules);
        Assert.Contains(new PhaseEntered(dragon.Id, 1), fury.Events);
        Assert.Contains(new AbilityId("dragon.cataclysm"), fury.State.Units[dragon.Id].Sheet.Abilities);
    }

    [Fact]
    public void Captain_gains_dirty_strike_below_half_health()
    {
        var state = Battle.Start(Encounters.Setup("forest_ruins", Party(4), Difficulty.Normal, Rules), Rules, 1).State;
        var captain = state.Units.Values.Single(u => u.Sheet.Tags.Contains("ruins_captain"));
        Assert.DoesNotContain(new AbilityId("bandit.dirty_strike"), captain.Sheet.Abilities);
        state = state with { Units = state.Units.SetItem(captain.Id, captain with { Hp = captain.Sheet.MaxHp / 2 - 1 }) };
        var result = Battle.Apply(state, new EndTurn(state.Active!.Value), Rules);
        Assert.Contains(new PhaseEntered(captain.Id, 1), result.Events);
        Assert.Contains(new AbilityId("bandit.dirty_strike"), result.State.Units[captain.Id].Sheet.Abilities);
    }
}
