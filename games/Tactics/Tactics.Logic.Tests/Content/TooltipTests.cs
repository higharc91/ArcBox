using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic.Tests;

public sealed class TooltipTests
{
    private static readonly ConditionDef Lit = new(new ConditionId("lit"), "Lit", null, [], Ordinal.Set());

    private static readonly RulesPack Rules = RulesPack.InMemory("test", conditions: [Lit, .. TestConditions.All]);

    private static AbilityDef Ability(TargetKind kind, int range, AreaDef area, string? flavor = null, params EffectDef[] effects)
        => new(new AbilityId("test.ability"), "Test", Tier: 0, new CostDef(Action: 1), new TargetDef(kind, range, Los: true, area),
            [.. effects], Flavor: flavor);

    private static AbilityDef Enemy(int range, params EffectDef[] effects)
        => Ability(TargetKind.Enemy, range, new AreaDef(AreaShape.Single), null, effects);

    private static DamageEffect Dmg(string dice, string? type = null) => new(TestAbilities.Flat(dice), type, []);

    private static ApplyEffect Apply(string id, int? rounds, bool untilSave = false) => new(new ConditionId(id), rounds, untilSave);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ---- The spec's examples ----------------------------------------------------------------------------------------

    [Fact]
    public void Guiding_bolt_level_1_exact_text()
        => Assert.Equal("Spell attack, 6 tiles: 4d6 radiant; target is Lit for 1 round",
            Tooltips.For(TestAbilities.GuidingBolt, 1, Rules));

    [Fact]
    public void Guiding_bolt_level_5_shows_5d6()
    {
        Assert.Equal("Spell attack, 6 tiles: 5d6 radiant; target is Lit for 1 round", Tooltips.For(TestAbilities.GuidingBolt, 5, Rules));
        Assert.Equal("Spell attack, 6 tiles: 4d6 radiant; target is Lit for 1 round", Tooltips.For(TestAbilities.GuidingBolt, 4, Rules));
        Assert.Equal("Spell attack, 6 tiles: 6d6 radiant; target is Lit for 1 round", Tooltips.For(TestAbilities.GuidingBolt, 10, Rules));
    }

    [Fact]
    public void Fireball_save_half_burst_2()
    {
        var fireball = Ability(TargetKind.Tile, 6, new AreaDef(AreaShape.Burst, 2), null,
            new SaveEffect(Stat.Dex, null, OnFail: [Dmg("8d6", "fire")], OnSuccess: [], HalfOnSuccess: true));
        Assert.Equal("Dex save, 6 tiles, burst 2: 8d6 fire; half damage on a save", Tooltips.For(fireball, 5, Rules));
    }

    [Fact]
    public void Sneak_attack_passive_text()
    {
        var sneak = new PassiveDef("rogue.sneak_attack", HookKind.OnAttackHit,
            new WhenDef(All:
            [
                new WhenDef(Weapon: ["finesse", "ranged"]),
                new WhenDef(Any: [new WhenDef(Advantage: true), new WhenDef(Flanked: true)]),
            ]),
            [new DamageEffect(TestAbilities.Levels((1, "1d6"), (3, "2d6"), (5, "3d6")), null, [])], null, OncePerTurn: true);
        const string head = "When you hit, if you attack with a finesse or ranged weapon and "
                            + "(you have advantage or you are flanking the target): ";
        Assert.Equal(head + "1d6 damage (once per turn)", Tooltips.For(sneak, 1, Rules));
        Assert.Equal(head + "2d6 damage (once per turn)", Tooltips.For(sneak, 4, Rules));
    }

    [Fact]
    public void Missing_describe_throws_naming_block()
    {
        // A transform whose hook doesn't exist has no phrase: the block answers null and the tooltip names it.
        var broken = Enemy(4, new TransformEffect("no_such_hook", Args("{}")));
        var error = Assert.Throws<InvalidOperationException>(() => Tooltips.For(broken, 1, Rules));
        Assert.Contains("'transform'", error.Message);
        Assert.Contains("no_such_hook", error.Message);
    }

    [Fact]
    public void Missing_describe_throws_from_a_nested_block_too()
    {
        var broken = Enemy(4, new RepeatEffect(2, [new TransformEffect("no_such_hook", Args("{}"))]));
        Assert.Contains("'transform'", Assert.Throws<InvalidOperationException>(() => Tooltips.For(broken, 1, Rules)).Message);
    }

    // ---- The header -------------------------------------------------------------------------------------------------

    [Fact]
    public void Header_rule_for_abilities_with_no_roll_is_the_range_or_self()
    {
        Assert.Equal("4 tiles: 1d6 fire", Tooltips.For(Enemy(4, Dmg("1d6", "fire")), 1, Rules));
        Assert.Equal("Self: heals 1d8 + WIS modifier",
            Tooltips.For(Ability(TargetKind.Self, 0, new AreaDef(AreaShape.Self), null,
                new HealEffect(TestAbilities.Flat("1d8"), ["mod.wis"])), 1, Rules));
        Assert.Equal("Self, burst 2: 1d6 fire",
            Tooltips.For(Ability(TargetKind.Self, 0, new AreaDef(AreaShape.Burst, 2), null, Dmg("1d6", "fire")), 1, Rules));
    }

    [Fact]
    public void Range_of_one_reads_as_one_tile()
        => Assert.Equal("1 tile: 1d6 fire", Tooltips.For(Enemy(1, Dmg("1d6", "fire")), 1, Rules));

    [Fact]
    public void Area_suffix_names_the_shape_in_plain_words()
    {
        string Header(AreaDef area) => Tooltips.For(Ability(TargetKind.Tile, 6, area, null, Dmg("1d6")), 1, Rules);
        Assert.Equal("6 tiles, cone 2: 1d6 damage", Header(new AreaDef(AreaShape.Cone, 2)));
        Assert.Equal("6 tiles, line 3: 1d6 damage", Header(new AreaDef(AreaShape.Line, 3)));
        Assert.Equal("6 tiles, cross 1: 1d6 damage", Header(new AreaDef(AreaShape.Cross, 1)));
        Assert.Equal("6 tiles, ring 2: 1d6 damage", Header(new AreaDef(AreaShape.Ring, 2)));
        Assert.Equal("6 tiles, all allies: 1d6 damage", Header(new AreaDef(AreaShape.AllAllies)));
        Assert.Equal("6 tiles, all enemies: 1d6 damage", Header(new AreaDef(AreaShape.AllEnemies)));
        Assert.Equal("6 tiles: 1d6 damage", Header(new AreaDef(AreaShape.Single)));
    }

    [Fact]
    public void Weapon_attack_uses_the_weapons_range()
    {
        Assert.Equal("Weapon attack, weapon range: weapon damage", Tooltips.For(RulesPack.CommonAttack, 1, Rules));
        var smite = Enemy(1, new AttackEffect("weapon", null, [new DamageEffect(null, "radiant", [], Weapon: true), Dmg("2d8", "radiant")],
            [], []));
        Assert.Equal("Weapon attack, 1 tile: weapon radiant damage; 2d8 radiant", Tooltips.For(smite, 1, Rules));
    }

    [Fact]
    public void Save_header_names_the_stat_and_flavour_is_appended()
    {
        var web = Ability(TargetKind.Enemy, 4, new AreaDef(AreaShape.Single), "Sticky strands.",
            new SaveEffect(Stat.Str, 13, OnFail: [Apply("restrained", null, untilSave: true)], OnSuccess: []));
        Assert.Equal("Str save, 4 tiles: target is Restrained until saved — Sticky strands.", Tooltips.For(web, 1, Rules));
    }

    [Fact]
    public void Attack_branches_on_miss_and_crit_are_described()
    {
        var attack = Enemy(1, new AttackEffect("weapon", null, OnHit: [Dmg("1d8", "slashing")], OnMiss: [Dmg("1d4", "slashing")],
            OnCrit: [Apply("prone", 1)]));
        Assert.Equal("Weapon attack, 1 tile: 1d8 slashing; on a crit, target is Prone for 1 round; on a miss, 1d4 slashing",
            Tooltips.For(attack, 1, Rules));
    }

    [Fact]
    public void A_roll_that_is_not_first_is_described_inline()
    {
        var both = Enemy(4, Dmg("1d4", "fire"),
            new SaveEffect(Stat.Con, null, OnFail: [Apply("stunned", 1)], OnSuccess: []));
        Assert.Equal("4 tiles: 1d4 fire; Con save (target is Stunned for 1 round)", Tooltips.For(both, 1, Rules));
    }

    // ---- Phrases ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Durations_read_as_rounds_end_of_turn_or_until_saved()
    {
        Assert.Equal("4 tiles: target is Prone for 3 rounds", Tooltips.For(Enemy(4, Apply("prone", 3)), 1, Rules));
        Assert.Equal("4 tiles: target is Prone for 1 round", Tooltips.For(Enemy(4, Apply("prone", 1)), 1, Rules));
        Assert.Equal("4 tiles: target is Prone until the end of your turn", Tooltips.For(Enemy(4, Apply("prone", 0)), 1, Rules));
        Assert.Equal("4 tiles: target is Prone", Tooltips.For(Enemy(4, Apply("prone", null)), 1, Rules));
        Assert.Equal("Wis save, 4 tiles: target is Stunned until saved",
            Tooltips.For(Enemy(4, new SaveEffect(Stat.Wis, null, [Apply("stunned", null, untilSave: true)], [])), 1, Rules));
    }

    [Fact]
    public void Condition_names_come_from_the_pack_else_the_capitalised_id()
    {
        Assert.Equal("4 tiles: target is Lit for 1 round", Tooltips.For(Enemy(4, Apply("lit", 1)), 1, Rules));
        Assert.Equal("4 tiles: target is Dazzled for 1 round", Tooltips.For(Enemy(4, Apply("dazzled", 1)), 1, Rules));
        var renamed = RulesPack.InMemory("test", conditions: [new ConditionDef(new ConditionId("lit"), "Shining", null, [], Ordinal.Set())]);
        Assert.Equal("4 tiles: target is Shining for 1 round", Tooltips.For(Enemy(4, Apply("lit", 1)), 1, renamed));
    }

    [Fact]
    public void Flow_blocks_describe_their_children()
    {
        Assert.Equal("4 tiles: if the target is Prone, 1d6 fire; otherwise 1d4 fire",
            Tooltips.For(Enemy(4, new IfEffect(new WhenDef(TargetHasCondition: new ConditionId("prone")), [Dmg("1d6", "fire")],
                [Dmg("1d4", "fire")])), 1, Rules));
        Assert.Equal("4 tiles: 3 times: (1d4 force; target is Prone for 1 round)",
            Tooltips.For(Enemy(4, new RepeatEffect(3, [Dmg("1d4", "force"), Apply("prone", 1)])), 1, Rules));
        Assert.Equal("4 tiles: 1d4 lightning; chains to up to 2 more targets within 3 tiles",
            Tooltips.For(Enemy(4, new ChainEffect(2, 3, [Dmg("1d4", "lightning")])), 1, Rules));
        Assert.Equal("4 tiles: one of, at random: 1d6 fire (1 in 4), target is Prone for 1 round (3 in 4)",
            Tooltips.For(Enemy(4, new RandomEffect([new RandomEntry(1, [Dmg("1d6", "fire")]), new RandomEntry(3, [Apply("prone", 1)])])),
                1, Rules));
    }

    [Fact]
    public void Nested_blocks_inside_an_attack_are_described_inline()
    {
        var strike = Enemy(1, new AttackEffect("weapon", null,
            OnHit: [Dmg("1d8", "slashing"), new RepeatEffect(2, [Dmg("1d4", "fire")])], OnMiss: [], OnCrit: []));
        Assert.Equal("Weapon attack, 1 tile: 1d8 slashing; 2 times: 1d4 fire", Tooltips.For(strike, 1, Rules));
    }

    [Fact]
    public void Tile_and_unit_blocks_have_phrases()
    {
        Assert.Equal("6 tiles: pushes target 2 tiles away", Tooltips.For(Enemy(6, new PushEffect(2)), 1, Rules));
        Assert.Equal("6 tiles: summons a wolf for 10 rounds", Tooltips.For(Enemy(6, new SummonEffect("wolf", Rounds: 10)), 1, Rules));
        Assert.Equal("6 tiles: summons 2 × archer for the battle",
            Tooltips.For(Enemy(6, new SummonEffect("bandit.archer", 2)), 1, Rules));
        Assert.Equal("Self: target gains 2 devotion",
            Tooltips.For(Ability(TargetKind.Self, 0, new AreaDef(AreaShape.Self), null, new GrantResourceEffect("devotion", 2)), 1, Rules));
        Assert.Equal("4 tiles: target gets +2 AC for 3 rounds",
            Tooltips.For(Enemy(4, new StatModEffect(new StaticMods(Ac: 2), 3)), 1, Rules));
    }

    [Fact]
    public void Zone_describes_its_area_duration_and_passives()
    {
        var burn = new PassiveDef("zone.burn", HookKind.OnTurnStart, null, [Dmg("1d6", "fire")], null, Target: PassiveTarget.Self);
        var wall = Ability(TargetKind.Tile, 6, new AreaDef(AreaShape.Burst, 2), null,
            new ZoneEffect("fire", new AreaDef(AreaShape.Burst, 2), 3, [burn], Difficult: true));
        Assert.Equal("6 tiles, burst 2: creates a fire zone (burst 2) for 3 rounds, difficult terrain; "
                     + "units inside: At the start of your turn, on yourself: 1d6 fire",
            Tooltips.For(wall, 1, Rules));
    }

    // ---- Passives and conditions ------------------------------------------------------------------------------------

    [Fact]
    public void Static_passive_lists_its_modifiers()
    {
        var tough = new PassiveDef("fighter.defense", null, null, [], new StaticMods(Ac: 1, Move: 1, Resist: Ordinal.Set("fire", "cold")));
        Assert.Equal("+1 AC; +1 Move; resists cold, fire", Tooltips.For(tough, 1, Rules));
    }

    [Fact]
    public void Passive_limits_and_self_target_are_shown()
    {
        var second = new PassiveDef("fighter.second_wind", HookKind.OnTurnStart, null,
            [new HealEffect(TestAbilities.Flat("1d10"), ["level"])], null, UsesPerBattle: 1, Target: PassiveTarget.Self);
        Assert.Equal("At the start of your turn, on yourself: heals 1d10 + level (once per battle)", Tooltips.For(second, 1, Rules));
        var burst = new PassiveDef("x", HookKind.OnKill, null, [Dmg("1d4", "fire")], null, OncePerRound: true, UsesPerBattle: 3);
        Assert.Equal("When you kill a unit: 1d4 fire (once per round, 3 times per battle)", Tooltips.For(burst, 1, Rules));
    }

    [Fact]
    public void Condition_tooltip_lists_modifiers_implied_conditions_and_passives()
    {
        Assert.Equal("disadvantage on its attack rolls; melee attacks against it have advantage; "
                     + "ranged attacks against it have disadvantage", Tooltips.For(TestConditions.Prone, Rules));
        Assert.Equal("attacks against it have advantage; automatically fails Str and Dex saves; also counts as Incapacitated",
            Tooltips.For(TestConditions.Stunned, Rules));
        Assert.Equal("At the start of your turn, on yourself: 1d6 fire", Tooltips.For(TestConditions.Burning, Rules));
        Assert.Equal("can't act", Tooltips.For(TestConditions.Incapacitated, Rules));
        Assert.Equal("no listed effect", Tooltips.For(RulesPack.Helped, Rules));
    }

    // ---- Every block and hook ---------------------------------------------------------------------------------------

    private static readonly AreaDef Burst1 = new(AreaShape.Burst, 1);

    /// <summary>One representative def for each registered block key.</summary>
    private static readonly ImmutableSortedDictionary<string, EffectDef> Samples = new (string, EffectDef)[]
    {
        ("attack", new AttackEffect("spell", Stat.Wis, [Dmg("1d6", "fire")], [], [])),
        ("save", new SaveEffect(Stat.Dex, 12, [Dmg("1d6", "fire")], [], true)),
        ("damage", Dmg("1d6", "fire")),
        ("heal", new HealEffect(TestAbilities.Flat("1d6"), ["mod.wis"])),
        ("tempHp", new TempHpEffect(TestAbilities.Flat("1d6"), ["prof"])),
        ("revive", new ReviveEffect(1)),
        ("apply", Apply("prone", 1)),
        ("remove", new RemoveEffect(new ConditionId("prone"))),
        ("cleanse", new CleanseEffect(Ordinal.Set("magical"))),
        ("dispel", new DispelEffect(Concentration: true, Zones: true)),
        ("help", new HelpEffect()),
        ("push", new PushEffect(2)),
        ("pull", new PullEffect(2)),
        ("teleport", new TeleportEffect(4)),
        ("swap", new SwapEffect()),
        ("dash", new DashEffect(3)),
        ("jumpTo", new JumpToEffect(4)),
        ("zone", new ZoneEffect("fire", Burst1, 3, [])),
        ("alterTerrain", new AlterTerrainEffect(Burst1, 1)),
        ("hazard", new HazardEffect("hazards.fire_pit", Burst1, 3, [Dmg("1d6", "fire")])),
        ("summon", new SummonEffect("wolf")),
        ("transform", new TransformEffect("wild_shape", Args("""{"monster": "bear"}"""))),
        ("grantResource", new GrantResourceEffect("mana", 1)),
        ("extraAction", new ExtraActionEffect("action")),
        ("refreshReaction", new RefreshReactionEffect()),
        ("statMod", new StatModEffect(new StaticMods(Ac: 1), 3)),
        ("mark", new MarkEffect(3)),
        ("taunt", new TauntEffect(2)),
        ("reveal", new RevealEffect(Burst1)),
        ("turnOrder", new TurnOrderEffect("delay", 1)),
        ("chain", new ChainEffect(2, 3, [Dmg("1d4", "lightning")])),
        ("repeat", new RepeatEffect(2, [Dmg("1d4", "fire")])),
        ("if", new IfEffect(new WhenDef(Success: true), [Dmg("1d4", "fire")], [])),
        ("random", new RandomEffect([new RandomEntry(1, [Dmg("1d4", "fire")])])),
    }.ToImmutableSortedDictionary(s => s.Item1, s => s.Item2, StringComparer.Ordinal);

    [Fact]
    public void Samples_cover_every_registered_block()
        => Assert.Equal(BlockRegistry.Keys, Samples.Keys);

    [Fact]
    public void Every_registered_block_describes_a_representative_def()
    {
        foreach (var key in BlockRegistry.Keys)
        {
            var def = Samples[key];
            var block = BlockRegistry.For(key);
            Assert.Same(block, BlockRegistry.For(def));
            var text = block.Describe(def, 1, Rules);
            Assert.False(string.IsNullOrWhiteSpace(text), $"'{key}' gave no phrase.");
        }
    }

    [Fact]
    public void Every_registered_block_survives_a_tooltip_in_an_ability()
    {
        foreach (var key in BlockRegistry.Keys)
            Assert.False(string.IsNullOrWhiteSpace(Tooltips.For(Enemy(4, Samples[key]), 1, Rules)), key);
    }

    [Fact]
    public void Named_hooks_describe_themselves_through_transform()
    {
        string Hook(string name, string args) => Tooltips.For(Enemy(4, new TransformEffect(name, Args(args))), 1, Rules);
        Assert.Equal("4 tiles: transforms into a bear", Hook("wild_shape", """{"monster": "bear"}"""));
        Assert.Equal("4 tiles: transforms into an owlbear", Hook("wild_shape", """{"monster": "beasts.owlbear"}"""));
        Assert.Equal("4 tiles: counters the spell being cast (spells above tier 3 need a check)", Hook("counterspell", "{}"));
        Assert.Equal("4 tiles: turns target into a sheep until it saves or loses concentration", Hook("polymorph", "{}"));
        Assert.Equal("4 tiles: raises a skeleton from the corpse on the aimed tile", Hook("animate_dead", "{}"));
        foreach (var name in NamedHooks.Names)
            Assert.NotNull(NamedHooks.TryFor(name)!.Describe(Args("""{"monster": "bear"}"""), Rules));
    }
}
