namespace Tactics.Logic.Tests;

public sealed class UncannyHitTests
{
    [Fact]
    public void Uncanny_dodge_halves_the_whole_hit_without_temp_hp_or_rng_changes()
    {
        var dodge = BasePackTests.Rules.Abilities[new("rogue.uncanny_dodge")];
        var strike = new AbilityDef(new("test.hit"), "Hit", 0, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, 1, false, new AreaDef(AreaShape.Single)),
            [new AttackEffect("weapon", null, [new DamageEffect(TestAbilities.Flat("3"), "fire", []),
                new DamageEffect(TestAbilities.Flat("4"), "cold", [])], [], [], Bonus: 100)]);
        var battle = TestBattle.On("0. 0. 0.")
            .Party("rogue", TestSheets.Rogue with { MaxHp = 100, Abilities = [dodge.Id] }, new Tile(0, 0))
            .Enemy("attacker", TestSheets.Goblin with { InitiativeBonus = 100, Abilities = [strike.Id] }, new Tile(1, 0))
            .With(dodge, strike);
        var (state, _) = battle.Start(7);
        var (paused, _) = battle.Apply(state, new Use(new("attacker"), strike.Id, new Target(Unit: new("rogue"))));
        var offer = Assert.IsType<ReactionOffer>(paused.Pending);
        for (var hp = 1; hp <= 8; hp++)
        {
            var injured = paused.Change("rogue", u => u with { Hp = hp });
            Assert.Equal(hp is >= 4 and <= 7,
                Ai.ChangesOutcome(injured, offer, offer.Options.First(c => c.Id == dodge.Id.Value), battle.Rules));
            Assert.Equal(paused.Rng, injured.Rng);
        }
        var (ordinary, _) = battle.Apply(paused, new React(new("rogue"), offer.Options.First(c => c.Id == "pass")));
        var (dodged, _) = battle.Apply(paused, new React(new("rogue"), offer.Options.First(c => c.Id == dodge.Id.Value)));
        Assert.Equal(93, ordinary.Units[new("rogue")].Hp);
        Assert.Equal(97, dodged.Units[new("rogue")].Hp);
        Assert.Equal(0, dodged.Units[new("rogue")].TempHp);
        Assert.Equal(ordinary.Rng, dodged.Rng);
        Assert.False(dodged.Units[new("rogue")].Economy.Reaction);
        var w = new BattleWriter(dodged, battle.Rules);
        Hp.Damage(w, new("rogue"), 7, "fire", new("attacker"), resolution: 999);
        Assert.Equal(90, w.State.Units[new("rogue")].Hp);
    }
    [Fact]
    public void An_unresolved_secondary_reaction_is_not_proof_that_dodge_prevents_downing()
    {
        var rogue = new UnitId("rogue");
        var attacker = new UnitId("attacker");
        var dodge = BasePackTests.Rules.Abilities[new("rogue.uncanny_dodge")];
        var strike = new AbilityDef(new("test.scratch"), "Scratch", 0, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, 1, false, new AreaDef(AreaShape.Single)),
            [new AttackEffect("weapon", null, [new DamageEffect(TestAbilities.Flat("1"), "fire", [])], [], [], Bonus: 100)]);
        var retaliation = new PassiveDef("test.retaliation", HookKind.OnDamaged, null,
            [new AttackEffect("weapon", null, [new DamageEffect(TestAbilities.Flat("1"), "fire", [])], [], [], Bonus: 100)], null);
        var battle = TestBattle.On("0. 0. 0.")
            .Party("rogue", TestSheets.Rogue with { MaxHp = 10, Abilities = [dodge.Id], Passives = [retaliation.Id] }, new Tile(0, 0))
            .Enemy("attacker", TestSheets.Goblin with { MaxHp = 100, InitiativeBonus = 100, Abilities = [strike.Id, dodge.Id] }, new Tile(1, 0))
            .With(dodge, strike).WithPassives(retaliation);
        var (state, _) = battle.Start(7);
        state = state.Change("rogue", u => u with { Hp = 5 })
            .Change("attacker", u => u with { Controller = new PlayerControlled(new("pr_attacker")) });
        var (paused, _) = battle.Apply(state, new Use(attacker, strike.Id, new Target(Unit: rogue)));
        var offer = Assert.IsType<ReactionOffer>(paused.Pending);
        var (ordinary, _) = battle.Apply(paused, new React(rogue, offer.Options.First(c => c.Id == "pass")));
        Assert.Equal(attacker, Assert.IsType<ReactionOffer>(ordinary.Pending).Reactor);
        Assert.Equal(4, ordinary.Units[rogue].Hp);
        Assert.False(Ai.ChangesOutcome(paused, offer, offer.Options.First(c => c.Id == dodge.Id.Value), battle.Rules));
    }

}
