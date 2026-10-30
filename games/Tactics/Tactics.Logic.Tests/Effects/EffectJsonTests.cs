using System.Text;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class EffectJsonTests
{
    /// <summary>The spec §4.2 example, verbatim.</summary>
    private const string GuidingBoltJson = """
        {
          "id": "cleric.guiding_bolt", "name": "Guiding Bolt", "tier": 1,
          "cost": { "action": 1, "resource": { "devotion": 1 } },
          "target": { "kind": "enemy", "range": 6, "los": true, "area": { "shape": "single" } },
          "effects": [
            { "attack": { "kind": "spell", "stat": "wis" },
              "onHit":  [ { "damage": { "dice": { "byLevel": { "1": "4d6", "5": "5d6", "9": "6d6" } }, "type": "radiant" } },
                          { "apply": "lit", "rounds": 1 } ] }
          ],
          "presentation": { "anim": "cast", "fx": "signature.radiant_bolt", "projectile": "holy" },
          "ai": { "tags": ["damage", "single"] }
        }
        """;

    /// <summary>Removes whitespace outside string literals.</summary>
    private static string Compact(string json)
    {
        var sb = new StringBuilder();
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                sb.Append(c);
                if (c == '\\')
                    sb.Append(json[++i]);
                else if (c == '"')
                    inString = false;
            }
            else if (c == '"')
            {
                inString = true;
                sb.Append(c);
            }
            else if (!char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    [Fact]
    public void Single_key_form_round_trips()
    {
        var ability = JsonSerializer.Deserialize<AbilityDef>(GuidingBoltJson, ArcJson.Options)!;

        Assert.Equal(new AbilityId("cleric.guiding_bolt"), ability.Id);
        Assert.Equal(1, ability.Cost.Action);
        Assert.Equal(1, ability.Cost.Resource!["devotion"]);
        Assert.Same(StringComparer.Ordinal, ability.Cost.Resource.KeyComparer);
        Assert.Equal(TargetKind.Enemy, ability.Target.Kind);
        Assert.Equal(AreaShape.Single, ability.Target.Area.Shape);
        var attack = Assert.IsType<AttackEffect>(Assert.Single(ability.Effects));
        Assert.Equal(("spell", Stat.Wis, 0), (attack.Kind, attack.Stat, attack.Bonus));
        Assert.Empty(attack.OnMiss);
        Assert.Empty(attack.OnCrit);
        Assert.Equal(2, attack.OnHit.Length);
        var damage = Assert.IsType<DamageEffect>(attack.OnHit[0]);
        Assert.Equal(Dice.Parse("5d6"), damage.Dice!.At(7));
        Assert.Equal("radiant", damage.Type);
        Assert.Empty(damage.Bonus);
        Assert.False(damage.Weapon);
        Assert.Equal(new ApplyEffect(new ConditionId("lit"), 1), attack.OnHit[1]);
        Assert.Equal("holy", ability.Presentation!.Projectile);
        Assert.Equal(["damage", "single"], ability.Ai!.Tags);

        var json = JsonSerializer.Serialize(ability, ArcJson.Options);
        Assert.Equal(Compact(GuidingBoltJson), json);

        var built = JsonSerializer.Serialize(TestAbilities.GuidingBolt, ArcJson.Options);
        Assert.Equal(json, built);
    }

    [Fact]
    public void Every_first_block_round_trips()
    {
        EffectDef[] effects =
        [
            new AttackEffect("weapon", null, [new DamageEffect(null, null, [], Weapon: true)], [new RemoveEffect(new ConditionId("hidden"))],
                [new ApplyEffect(new ConditionId("prone"), null)], Bonus: 1),
            new SaveEffect(Stat.Dex, 14, [new DamageEffect(TestAbilities.Flat("8d6"), "fire", ["mod.int", "prof"])], [], HalfOnSuccess: true),
            new HealEffect(TestAbilities.Levels((1, "1d8+1"), (5, "2d8")), ["mod.cast", "level"]),
            new ApplyEffect(new ConditionId("frightened"), 10, UntilSave: true),
        ];

        var json = JsonSerializer.Serialize(effects, ArcJson.Options);
        var back = JsonSerializer.Deserialize<EffectDef[]>(json, ArcJson.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, ArcJson.Options));

        Assert.StartsWith("""[{"attack":{"kind":"weapon","bonus":1},"onHit":[{"damage":{"weapon":true}}],"onMiss":[{"remove":"hidden"}],"onCrit":[{"apply":"prone"}]}""", json);
        Assert.Contains("""{"save":{"stat":"dex","dc":14,"halfOnSuccess":true},"onFail":[{"damage":{"dice":"8d6","type":"fire","bonus":["mod.int","prof"]}}]}""", json);
        Assert.Contains("""{"apply":"frightened","rounds":10,"untilSave":true}""", json);
        var heal = Assert.IsType<HealEffect>(back[2]);
        Assert.Equal(Dice.Parse("1d8+1"), heal.Dice.At(4));
        Assert.Equal(Dice.Parse("2d8"), heal.Dice.At(5));
        Assert.Equal(["mod.cast", "level"], heal.Bonus);
    }

    [Fact]
    public void Object_form_and_bare_level_table_also_read()
    {
        var effect = JsonSerializer.Deserialize<EffectDef>(
            """{"apply":{"condition":"lit","rounds":2}}""", ArcJson.Options);
        Assert.Equal(new ApplyEffect(new ConditionId("lit"), 2), effect);

        var damage = Assert.IsType<DamageEffect>(JsonSerializer.Deserialize<EffectDef>(
            """{"damage":{"dice":{"1":"1d6","3":"2d6"}}}""", ArcJson.Options));
        Assert.Equal(Dice.Parse("2d6"), damage.Dice!.At(3));
    }

    [Fact]
    public void Unknown_key_throws_naming_it()
    {
        var unknown = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EffectDef>("""{"explode": {"radius": 2}}""", ArcJson.Options));
        Assert.Contains("explode", unknown.Message);

        var param = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EffectDef>("""{"damage": {"dice": "1d6", "colour": "red"}}""", ArcJson.Options));
        Assert.Contains("colour", param.Message);

        var two = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EffectDef>("""{"damage": {"dice": "1d6"}, "heal": {"dice": "1d6"}}""", ArcJson.Options));
        Assert.Contains("damage", two.Message);
        Assert.Contains("heal", two.Message);

        var missing = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EffectDef>("""{"save": {"dc": 12}}""", ArcJson.Options));
        Assert.Contains("stat", missing.Message);
    }

    [Fact]
    public void Registry_finds_blocks_by_key_and_type()
    {
        // FlowBlockTests.Every_registered_key_has_a_test pins the full key list.
        Assert.Subset(BlockRegistry.Keys.ToHashSet(),
            new HashSet<string> { "apply", "attack", "damage", "dash", "heal", "help", "jumpTo", "pull", "push", "remove", "save", "swap", "teleport" });
        Assert.Equal("damage", BlockRegistry.For(new DamageEffect(null, null, [], Weapon: true)).Key);
        Assert.Equal(typeof(SaveEffect), BlockRegistry.For("save").DefType);
        Assert.Throws<KeyNotFoundException>(() => BlockRegistry.For("explode"));
    }
}
