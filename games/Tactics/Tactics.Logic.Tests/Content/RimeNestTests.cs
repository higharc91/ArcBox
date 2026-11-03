namespace Tactics.Logic.Tests;

public sealed class RimeNestTests
{
    private static ZoneEffect Nest()
    {
        var trigger = Assert.Single(BasePackTests.Rules.Encounters["crystal_cave"].Triggers, t => t.Id == "spider_webs");
        return Assert.IsType<ZoneEffect>(Assert.Single(Assert.Single(trigger.Do).Effects));
    }

    [Fact]
    public void Cave_opens_with_the_four_native_web_tiles_and_cave_spiders_keep_their_stable_ids()
    {
        var rules = BasePackTests.Rules;
        var result = Battle.Start(Encounters.Setup("crystal_cave", BasePackTests.Party(4, rules), Difficulty.Normal, rules), rules, 7);
        Assert.True(result.State.Environment);
        Assert.Contains(result.Events, e => e is TriggerFired { Id: "spider_webs" });
        var webs = result.State.Zones.Where(z => z.Kind == "web").ToArray();
        Assert.Equal(4, webs.Length);
        Assert.Equal(rules.MapFor("crystal_cave")!.Zones["spider_nest"].Order(), webs.SelectMany(z => z.Tiles).Order());
        Assert.All(webs, z => { Assert.Null(z.RoundsLeft); Assert.True(z.Difficult); });
        for (var i = 1; i <= 4; i++)
            Assert.Contains("rime_nest_spider", result.State.Units[new UnitId($"giant_spider.{i}")].Sheet.Tags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_nest_spiders_cross_their_own_web_without_a_restraint_save(bool enabled)
    {
        var rules = BasePackTests.Rules;
        var spider = TestSheets.Goblin with { MaxHp = 100, InitiativeBonus = 30, Tags = TestSheets.Goblin.Tags.Add("rime_nest_spider") };
        var intruder = TestSheets.Goblin.WithScore(Stat.Dex, 1) with { MaxHp = 100, Weapon = TestSheets.Goblin.Weapon with { Ranged = true } };
        var battle = TestBattle.On("0. 0. 0.").WithEnvironment(rules.Environment, enabled)
            .WithConditions(rules.Conditions[new ConditionId("restrained")])
            .Party("spider", spider, new Tile(0, 0)).Enemy("intruder", intruder, new Tile(2, 0));
        var state = battle.Start().State;
        state = battle.Run(state, "spider", null, new Tile(1, 0), Nest()).State;
        var across = battle.Apply(state, new Move(new UnitId("spider"), [new Tile(0, 0), new Tile(1, 0)]));
        Assert.DoesNotContain(across.Events, e => e is Rolled { Kind: RollKind.Save });
        state = battle.Apply(across.State, new Move(new UnitId("spider"), [new Tile(1, 0), new Tile(0, 0)])).State;
        state = battle.Apply(state, new EndTurn(new UnitId("spider"))).State;
        var entered = battle.Apply(state, new Move(new UnitId("intruder"), [new Tile(2, 0), new Tile(1, 0)]));
        Assert.Contains(entered.Events, e => e is Rolled { Kind: RollKind.Save, Roller.Value: "intruder" });
        Assert.Contains(entered.State.Units[new UnitId("intruder")].Conditions, c => c.Id.Value == "restrained");
    }
}
