using System.Collections.Immutable;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>#107: a phone's <c>use</c> may list a target for each attack, for a hero and for a summon its owner plays.</summary>
public sealed class MultiAttackPhoneTests
{
    private static readonly UnitId Hero = new("hero1");
    private static readonly UnitId Hound = new("hero1.hound.7");

    /// <summary>
    /// A saved battle resumed on <paramref name="actor"/>'s turn (hero1, or a hound of hero1's that player 0 plays), the actor
    /// holding a long bow with two attacks on a tile from which it can shoot two goblins, which it returns.
    /// </summary>
    internal static (Session Session, UnitId First, UnitId Second) Arranged(UnitId actor)
    {
        var s = new Session();
        s.ToBattle();
        var doc = TacticsDocument.Read(s.Lease.Document);
        var state = doc.Battle!.State;
        var owner = state.Units[Hero];
        var sheet = owner.Sheet with { Attacks = 2, Weapon = Rules.Weapons["shortbow"] with { Range = 30 } };
        var unit = owner with
        {
            Id = actor, Sheet = actor == Hero ? sheet : sheet with { Name = "Hound" }, SummonedBy = actor == Hero ? null : Hero,
            Economy = Economy.Fresh(sheet), Controller = new PlayerControlled(new ProfileId("pr_member1")),
        };
        var order = state.Order.Remove(actor);
        state = state with { Units = state.Units.SetItem(actor, unit), Order = [actor, .. order], TurnIndex = 0, Pending = null };

        var foes = state.Units.Values.Where(u => u.Sheet.Side == Side.Enemy && u.Life == LifeState.Up).Select(u => u.Id).ToArray();
        var taken = state.Units.Values.Where(u => u.Id != actor).Select(u => u.Pos).ToHashSet();
        var map = Rules.MapFor(state.MapId)!;
        foreach (var at in Enumerable.Range(0, map.Width).SelectMany(x => Enumerable.Range(0, map.Height).Select(y => new Tile(x, y))))
        {
            if (taken.Contains(at))
                continue;
            var placed = state with { Units = state.Units.SetItem(actor, unit with { Pos = at }) };
            foreach (var first in foes)
                foreach (var second in foes.Where(f => f != first))
                {
                    var use = new Use(actor, RulesPack.CommonAttack.Id, new Target(Unit: first)) { Targets = [first, second] };
                    if (Battle.Check(placed, use, Rules) is not null)
                        continue;
                    var saved = doc with { Battle = doc.Battle with { State = placed } };
                    var resumed = new Session(document: saved.ToJson());
                    resumed.Harness.Resume(resumed.Lease);
                    for (var p = 0; p < 4; p++)
                        resumed.Harness.SetConnected(p, true);
                    if (resumed.Game.Phase == TacticsPhase.Resuming)
                        resumed.Send(0, "continue");
                    Assert.Equal(actor, resumed.Game.Document.Battle!.State.Active);
                    return (resumed, first, second);
                }
        }
        throw new InvalidOperationException("No tile on the map lets the actor shoot two goblins.");
    }

    private static ImmutableArray<BattleEvent> Logged(Session s) => [.. s.Game.Document.Battle!.Log.SelectMany(b => b.Events)];

    [Theory]
    [InlineData("hero1")]
    [InlineData("hero1.hound.7")]
    public void A_use_with_a_target_for_each_attack_shoots_each(string id)
    {
        var actor = new UnitId(id);
        var (s, first, second) = Arranged(actor);
        Assert.Equal(2, s.Screen(0)!.Data.GetProperty("choices").GetProperty("abilities").EnumerateArray()
            .First(a => a.GetProperty("ability").GetString() == "common.attack").GetProperty("attacks").GetInt32());

        s.Send(0, "use", new { ability = "common.attack", unit = first.Value, targets = new[] { first.Value, second.Value } });

        var events = Logged(s);
        Assert.Equal([first, second], events.OfType<AttackResolved>().Where(a => a.Source == actor).Select(a => a.Target));
        Assert.Contains(new Retargeted(actor, second), events);
    }

    [Fact]
    public void A_targets_list_that_isnt_a_list_of_ids_is_refused_and_the_turn_stays()
    {
        var (s, first, _) = Arranged(Hero);

        s.Send(0, "use", new { ability = "common.attack", unit = first.Value, targets = new object[] { 3, true } });

        Assert.DoesNotContain(Logged(s), e => e is AttackResolved { Source.Value: "hero1" });
        Assert.Equal(Hero, s.Game.Document.Battle!.State.Active);
        Assert.Equal(TacticsGame.Screens.Turn, s.Screen(0)?.Screen);
    }
}
