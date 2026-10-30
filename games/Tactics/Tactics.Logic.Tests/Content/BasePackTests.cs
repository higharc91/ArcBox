using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>The shipped base pack (<c>content/tactics/base/rules</c>): it loads, and every piece of it builds and describes itself.</summary>
public sealed class BasePackTests
{
    private static readonly Lazy<RulesPack> Pack = new(() => RulesPack.Load([ContentPaths.BaseRules], ArtManifests.Load(ContentPaths.ContentBase)));

    /// <summary>The base pack, loaded once with the art manifests.</summary>
    internal static RulesPack Rules => Pack.Value;

    private static readonly string[] Classes = ["cleric", "fighter", "rogue", "wizard"];

    private static readonly string[] AiProfiles = ["archer", "boss", "brute", "caster", "coward", "healer", "skirmisher"];

    private static UnitId U(string id) => new(id);

    [Fact]
    public void Base_pack_loads_with_zero_errors_including_art_keys()
    {
        RulesPack pack;
        try
        {
            pack = RulesPack.Load([ContentPaths.BaseRules], ArtManifests.Load(ContentPaths.ContentBase));
        }
        catch (ContentException ex)
        {
            Assert.Fail("The base pack has errors:\n" + string.Join("\n", ex.Errors));
            return;
        }

        Assert.Equal(Classes, pack.Classes.Keys);
        Assert.Equal(["aasimar", "dragonborn", "dwarf", "elf", "gnome", "goliath", "half_elf", "half_orc", "halfling", "human", "tiefling"],
            pack.Races.Keys);
        Assert.Equal(12, pack.Backgrounds.Count);
        Assert.Equal(["black_eye", "bruised_pride", "concussion", "cracked_ribs", "deep_cut", "shaken", "sprained_ankle", "wrenched_shoulder"],
            pack.Injuries.Keys.Select(i => i.Value));
        Assert.Equal(["alchemists_fire", "greater_healing", "potion_of_healing", "smoke_bomb"], pack.Items.Keys.Select(i => i.Value));
        Assert.Equal(["dire_wolf", "giant_spider", "goblin", "goblin_archer", "goblin_boss", "hobgoblin", "kobold", "skeleton", "wolf", "zombie"],
            pack.Monsters.Keys);
        Assert.Equal(XpTable.Default.Thresholds, pack.Xp.Thresholds);
        Assert.Equal(["river_crossing"], pack.Maps.Keys);
        Assert.Equal(["river_crossing"], pack.Encounters.Keys);

        string[] conditions =
        [
            "blinded", "charmed", "frightened", "grappled", "incapacitated", "invisible", "paralysed", "poisoned", "prone", "restrained",
            "stunned", "unconscious", "burning", "bleeding", "slowed", "hasted", "marked", "lit", "taunted", "helped", "dodging", "disengaged",
        ];
        Assert.All(conditions, c => Assert.True(pack.Conditions.ContainsKey(new ConditionId(c)), $"No condition '{c}'."));
        string[] common = ["attack", "dash", "disengage", "dodge", "help", "shove", "use_item"];
        Assert.All(common, a => Assert.True(pack.Abilities.ContainsKey(new AbilityId("common." + a)), $"No ability 'common.{a}'."));

        foreach (var cls in pack.Classes.Values)
        {
            Assert.False(cls.SubclassesPending, cls.Id);
            Assert.Single(cls.Subclasses);
            Assert.All(Enumerable.Range(1, 10), level => Assert.True(cls.DefaultPicks.ContainsKey(level), $"{cls.Id} has no default picks at {level}."));
            Assert.Contains(cls.PrimaryResource!, cls.Resources.Keys);
        }
    }

    [Fact]
    public void Every_ability_passive_and_condition_has_a_tooltip_at_levels_1_to_10()
    {
        var rules = Rules;
        for (var level = 1; level <= 10; level++)
        {
            foreach (var ability in rules.Abilities.Values)
                Assert.False(string.IsNullOrWhiteSpace(Tooltips.For(ability, level, rules)), ability.Id.Value);
            foreach (var passive in rules.Passives.Values)
                Assert.False(string.IsNullOrWhiteSpace(Tooltips.For(passive, level, rules)), passive.Id);
        }
        foreach (var condition in rules.Conditions.Values)
            Assert.False(string.IsNullOrWhiteSpace(Tooltips.For(condition, rules)), condition.Id.Value);
    }

    [Fact]
    public void Every_class_builds_a_default_character_at_levels_1_to_10()
    {
        var rules = Rules;
        foreach (var cls in rules.Classes.Values)
            foreach (var subclass in cls.Subclasses.Select(s => s.Id).Prepend(null))
                for (var level = 1; level <= 10; level++)
                {
                    var record = SheetBuilder.DefaultCharacter($"{cls.Id} {level}", "human", cls.Id, level >= 3 ? subclass : null, level, rules);
                    var sheet = SheetBuilder.ForCharacter(record, rules);

                    Assert.True(sheet.MaxHp > 0);
                    Assert.Equal(cls.Resources.Keys, sheet.Resources.Keys);
                    Assert.All(sheet.Abilities, a => Assert.True(rules.Abilities.ContainsKey(a), $"{cls.Id} {level}: unknown ability {a}."));
                    Assert.All(sheet.Passives, p => Assert.NotNull(rules.PassiveFor(p)));
                    // A default character uses every pick its level offers.
                    var offered = Enumerable.Range(1, level).Sum(l => cls.Features.GetValueOrDefault(l, []).Sum(f => f.Choose ?? 0));
                    Assert.True(offered == record.Picks.Length, $"{cls.Id} {level}: {record.Picks.Length} default picks for {offered} choices.");
                }

        // Every race builds with every class.
        foreach (var race in rules.Races.Keys)
            foreach (var cls in Classes)
                Assert.NotNull(SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("x", race, cls, null, 1, rules), rules));
    }

    [Fact]
    public void Every_monster_builds()
    {
        var rules = Rules;
        foreach (var monster in rules.Monsters.Values)
        {
            var sheet = SheetBuilder.ForMonster(monster.Id, rules);
            Assert.True(sheet.MaxHp > 0, monster.Id);
            Assert.NotNull(sheet.Sprite);
            Assert.Contains(monster.AiProfile, AiProfiles);
            Assert.All(sheet.Abilities, a => Assert.True(rules.Abilities.ContainsKey(a), $"{monster.Id}: unknown ability {a}."));
            Assert.Same(rules.MonsterSheet(monster.Id)!.Weapon, sheet.Weapon);
        }
    }

    [Fact]
    public void River_crossing_sets_up_for_4_5_and_6_party_members()
    {
        var rules = Rules;
        foreach (var size in new[] { 4, 5, 6 })
        {
            var party = Party(size, rules);
            var setup = Encounters.Setup("river_crossing", party, Difficulty.Normal, rules);

            Assert.Equal(size, setup.Units.Count(u => u.Sheet.Side == Side.Party));
            var enemies = setup.Units.Where(u => u.Sheet.Side == Side.Enemy).ToArray();
            Assert.Equal(6, enemies.Length);
            Assert.All(enemies, e => Assert.Contains("goblinoid", e.Sheet.Tags));
            Assert.Equal(250, setup.Xp);

            var started = Battle.Start(setup, rules, seed: 7);
            Assert.Equal(1, started.State.Round);
            Assert.Equal(size + 6, started.State.Order.Length);
            Assert.NotEmpty(started.Events);
        }
        Assert.Throws<ArgumentException>(() => Encounters.Setup("river_crossing", Party(3, rules), Difficulty.Normal, rules));
        Assert.Throws<ArgumentException>(() => Encounters.Setup("river_crossing", Party(7, rules), Difficulty.Normal, rules));
    }

    /// <summary>A level-1 party of default characters: fighter, cleric, rogue, wizard, then again.</summary>
    internal static ImmutableArray<PartyMember> Party(int size, RulesPack rules)
    {
        string[] order = ["fighter", "cleric", "rogue", "wizard"];
        return [.. Enumerable.Range(0, size).Select(i =>
        {
            var cls = order[i % order.Length];
            var id = i < order.Length ? cls : $"{cls}{i / order.Length + 1}";
            return new PartyMember(U(id), SheetBuilder.DefaultCharacter(id, "human", cls, null, 1, rules),
                new PlayerControlled(new ProfileId("pr_" + id)));
        })];
    }

    [Fact]
    public void Every_ability_runs_in_a_battle_without_breaking_the_rules()
    {
        // Each ability, used by a level-10 caster of it on a small field: the user acts first, with an ally, a sturdy enemy
        // two tiles away and a free tile beside it. Every use must be accepted and keep the engine's invariants.
        const string field = """
            0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0.
            """;
        var base_ = Rules;
        var map = AsciiMap.Parse(field);
        var rules = RulesPack.InMemory("smoke", base_.Abilities.Values, base_.Passives.Values, base_.Conditions.Values, [map],
            base_.Injuries.Values, items: base_.Items.Values);
        Invariants.Enabled = true;
        var owners = base_.Classes.Values
            .SelectMany(c => c.Subclasses.Select(s => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter(c.Id, "human", c.Id, s.Id, 10, base_), base_)
                is var sheet ? sheet with { Abilities = [.. sheet.Abilities.Union(AllPicks(c, s).Where(base_.Abilities.ContainsKey))] } : null!))
            .ToArray();
        var ogre = SheetBuilder.ForMonster("hobgoblin", base_) with { MaxHp = 300, Tags = Ordinal.Set("humanoid", "undead") };
        var problems = new List<string>();

        foreach (var ability in base_.Abilities.Values.Where(a => a.Id != Battle.UseItem))
        {
            // An item's ability runs through common.use_item, with one of the item in the bag.
            var item = base_.Items.Values.FirstOrDefault(i => i.Use == ability.Id);
            var sheet = owners.FirstOrDefault(o => o.Abilities.Contains(ability.Id))
                        ?? owners[0] with { Abilities = item is null ? [ability.Id] : [] };
            var battle = new[] { ("user", sheet with { InitiativeBonus = 50, Side = Side.Party }, new Tile(1, 1)),
                ("ally", owners[0] with { Side = Side.Party }, new Tile(1, 2)), ("enemy", ogre, new Tile(3, 1)) };
            ImmutableArray<ItemStack> bag = item is null ? [] : [new ItemStack(item.Id, 1)];
            var setup = new BattleSetup(map, [.. battle.Select(b => new UnitPlacement(U(b.Item1), b.Item2,
                b.Item2.Side == Side.Party ? new PlayerControlled(new ProfileId("pr_" + b.Item1)) : new AiControlled("brute"), b.Item3,
                b.Item1 == "user" ? bag : []))],
                ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
            var state = Battle.Start(setup, rules, seed: 3).State;
            Assert.Equal(U("user"), state.Active);
            if (ability.Target.Kind == TargetKind.DownedAlly)
                state = state.Change("ally", u => u with { Life = LifeState.Downed, Hp = 0, Countdown = 3 });
            if (ability.Target.Range <= 1 && ability.Target.Kind is TargetKind.Enemy or TargetKind.AnyUnit && !ability.IsWeaponAttack
                || ability.IsWeaponAttack && !sheet.Weapon.Ranged || ability.Target.Kind == TargetKind.Self)
                state = state.Change("enemy", u => u with { Pos = new Tile(2, 1) });
            var enemy = state.Units[U("enemy")].Pos;
            var target = ability.Target.Kind switch
            {
                TargetKind.Self => ability.Target.Area.Shape is AreaShape.Cone or AreaShape.Line ? new Target(Tile: enemy) : new Target(Unit: U("user")),
                TargetKind.Ally or TargetKind.DownedAlly => new Target(Unit: ability.Target.Range == 0 ? U("user") : U("ally")),
                TargetKind.Enemy or TargetKind.AnyUnit => new Target(Unit: U("enemy")),
                TargetKind.Tile => new Target(Tile: enemy),
                _ => new Target(Tile: new Tile(2, 2)),
            };
            if (ability.Target.Kind == TargetKind.Ally && ability.Target.Range == 0 && ability.Target.Area.Shape == AreaShape.Single)
                target = new Target(Unit: U("user"));
            try
            {
                var used = Battle.Apply(state, item is null ? new Use(U("user"), ability.Id, target)
                    : new Use(U("user"), Battle.UseItem, target, item.Id), rules);
                if (used.Rejected is { } refused)
                    problems.Add($"{ability.Id}: refused ({refused.Reason})");
            }
            catch (Exception ex)
            {
                problems.Add($"{ability.Id}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Resourceful_survives_arcane_recovery()
    {
        // A human wizard starts with one Mana over her pool of 4; Arcane Recovery (+1 at level 1) mustn't take it away.
        var rules = Rules;
        var setup = Encounters.Setup("river_crossing", Party(4, rules), Difficulty.Normal, rules);
        var state = Battle.Start(setup, rules, seed: 7).State;
        Assert.Equal(5, state.Units[U("wizard")].Resources["mana"]);
        Assert.Equal(3, state.Units[U("fighter")].Resources["superiority"]);   // the declared primary, not second_wind
        Assert.Equal(2, state.Units[U("fighter")].Resources["second_wind"]);

        while (state.Active != U("wizard"))
            state = Battle.Apply(state, new EndTurn(state.Active!.Value), rules).State;
        var recovered = Battle.Apply(state, new Use(U("wizard"), new AbilityId("wizard.arcane_recovery"), new Target(Unit: U("wizard"))), rules);

        Assert.Null(recovered.Rejected);
        Assert.Equal(5, recovered.State.Units[U("wizard")].Resources["mana"]);
    }

    [Fact]
    public void Loaded_sneak_attack_fires_when_flanking()
    {
        // Content passives are read with absent lists filled in as empty ones; their "when" must still hold.
        var map = AsciiMap.Parse("""
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            """);
        var base_ = Rules;
        var rules = RulesPack.InMemory("sneak", base_.Abilities.Values, base_.Passives.Values, base_.Conditions.Values, [map],
            base_.Injuries.Values);
        UnitSheet Character(string cls) => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter(cls, "human", cls, null, 1, base_), base_);
        UnitPlacement Place(string id, UnitSheet sheet, Tile at)
            => new(U(id), sheet, sheet.Side == Side.Party ? new PlayerControlled(new ProfileId("pr_" + id)) : new AiControlled("brute"), at, []);
        var setup = new BattleSetup(map,
        [
            Place("vex", Character("rogue") with { InitiativeBonus = 50 }, new Tile(1, 0)),
            Place("gob", SheetBuilder.ForMonster("goblin", base_) with { MaxHp = 100 }, new Tile(2, 0)),
            Place("kara", Character("fighter"), new Tile(3, 0)),
        ], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var state = Battle.Start(setup, rules, seed: 3).State;

        var used = Battle.Apply(state, new Use(U("vex"), RulesPack.CommonAttack.Id, new Target(Unit: U("gob"))), rules);

        Assert.Contains(new AttackResolved(U("vex"), U("gob"), true, true), used.Events);
        Assert.Contains(new PassiveTriggered(U("vex"), "rogue.sneak_attack"), used.Events);
    }

    /// <summary>Every ability or passive a class and its subclass offer a choice of, up to level 10.</summary>
    private static ImmutableArray<AbilityId> AllPicks(ClassDef cls, SubclassDef subclass)
        => [.. cls.Features.Values.Concat(subclass.Features.Values).SelectMany(f => f).SelectMany(f => f.From.IsDefault ? [] : f.From)
            .Distinct().Select(id => new AbilityId(id))];

    [Fact]
    public void Builtins_match_common_json()
    {
        var rules = Rules;
        static string Json<T>(T value) => JsonSerializer.Serialize(value, ArcJson.Options);

        Assert.Equal(Json(RulesPack.CommonAttack), Json(rules.Abilities[RulesPack.CommonAttack.Id]));
        Assert.Equal(Json(RulesPack.CommonHelp), Json(rules.Abilities[RulesPack.CommonHelp.Id]));
        Assert.Equal(Json(RulesPack.Helped), Json(rules.Conditions[RulesPack.Helped.Id]));
    }
}
