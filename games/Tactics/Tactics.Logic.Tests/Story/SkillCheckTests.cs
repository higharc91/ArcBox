using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic.Tests;

public sealed class SkillCheckTests
{
    private static RulesPack Rules => BasePackTests.Rules;

    /// <summary>A base-pack character with CHA <paramref name="cha"/> before the background's boosts.</summary>
    private static CharacterRecord Character(string name, string race, string cls, string background, int cha, int level = 3)
    {
        var made = SheetBuilder.DefaultCharacter(name, race, cls, null, level, Rules);
        return made with { Background = background, BaseScores = made.BaseScores.SetItem(Stat.Cha, cha) };
    }

    [Fact]
    public void Every_skill_in_the_schema_has_a_stat()
    {
        using var schema = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContentPaths.ContentBase, "lore", "schema", "scenes.schema.json")));
        var skills = schema.RootElement.GetProperty("$defs").GetProperty("option").GetProperty("properties").GetProperty("check")
            .GetProperty("properties").GetProperty("skill").GetProperty("enum").EnumerateArray().Select(s => s.GetString()!).ToList();

        Assert.Equal(18, skills.Count);
        Assert.Equal(skills.Order(), SkillCheck.Skills.Keys.Order());
        foreach (var skill in skills)
            Assert.NotNull(SkillCheck.StatOf(skill));
        Assert.Null(SkillCheck.StatOf("juggling"));

        // The SRD's table, one line per stat that has skills; CON has none.
        Assert.Equal(["athletics"], SkillCheck.Skills.Where(s => s.Value == Stat.Str).Select(s => s.Key));
        Assert.Equal(["acrobatics", "sleight_of_hand", "stealth"], SkillCheck.Skills.Where(s => s.Value == Stat.Dex).Select(s => s.Key).Order());
        Assert.Equal(["arcana", "history", "investigation", "nature", "religion"], SkillCheck.Skills.Where(s => s.Value == Stat.Int).Select(s => s.Key).Order());
        Assert.Equal(["animal_handling", "insight", "medicine", "perception", "survival"], SkillCheck.Skills.Where(s => s.Value == Stat.Wis).Select(s => s.Key).Order());
        Assert.Equal(["deception", "intimidation", "performance", "persuasion"], SkillCheck.Skills.Where(s => s.Value == Stat.Cha).Select(s => s.Key).Order());

        // Every skill the backgrounds and classes grant, and every check in the lore, is in the table.
        foreach (var skill in Rules.Backgrounds.Values.SelectMany(b => b.Skills).Concat(Rules.Classes.Values.SelectMany(c => c.SkillChoices.From)))
            Assert.NotNull(SkillCheck.StatOf(skill));
        foreach (var check in Campaign.Book.Value.Scenes.SelectMany(s => s.Nodes).SelectMany(n => n.Options).Select(o => o.Check).OfType<SceneCheck>())
            Assert.NotNull(SkillCheck.StatOf(check.Skill));
    }

    [Fact]
    public void A_proficient_bonus_adds_proficiency_once()
    {
        // A noble is proficient in persuasion and history; CHA 14 + 2 = 16 is +3.
        var noble = Character("Wren", "halfling", "rogue", "noble", 14);
        var sheet = SheetBuilder.ForCharacter(noble, Rules);
        Assert.Contains("persuasion", sheet.Skills);

        Assert.Equal(3 + 2, SkillCheck.Bonus(noble, "persuasion", Rules));
        Assert.Equal(3 + 2, SkillCheck.Bonus(sheet, "persuasion"));
        Assert.Equal(Stats.Mod(sheet.Score(Stat.Int)) + 2, SkillCheck.Bonus(sheet, "history"));
        Assert.Equal(Stats.Mod(sheet.Score(Stat.Wis)), SkillCheck.Bonus(sheet, "survival"));
        Assert.Equal(3 + 3, SkillCheck.Bonus(noble with { Level = 5 }, "persuasion", Rules));
        Assert.Equal(3 + 4, SkillCheck.Bonus(noble with { Level = 9 }, "persuasion", Rules));
        Assert.Throws<ArgumentException>(() => SkillCheck.Bonus(sheet, "juggling"));
    }

    /// <summary>Mockup 03's cards, as base-pack characters at level 3: the numbers on the cards are what the rules give.</summary>
    [Fact]
    public void The_mockups_bonuses_add_up()
    {
        // The mockup's "Charlatan" isn't a background in the pack: a noble is the one proficient in persuasion. Aldric's acolyte
        // background gives the +1 CHA that makes his 15 a 16.
        (CharacterRecord Who, int Bonus)[] cards =
        [
            (Character("Kara", "human", "fighter", "soldier", 12), 1),
            (Character("Aldric", "dwarf", "cleric", "acolyte", 15), 3),
            (Character("Wren", "halfling", "rogue", "noble", 14), 5),
            (Character("Ilyra", "elf", "wizard", "sage", 14), 2),
            (Character("Tamsin", "half_elf", "ranger", "outlander", 10), 0),
        ];

        foreach (var (who, bonus) in cards)
            Assert.True(SkillCheck.Bonus(who, "persuasion", Rules) == bonus, $"{who.Name} has {SkillCheck.Bonus(who, "persuasion", Rules)}, not {bonus}.");
        Assert.Equal("Wren", SkillCheck.Best(cards.Select(c => (c.Who.Name, SkillCheck.Bonus(c.Who, "persuasion", Rules)))));

        // "14 + 5 = 19 vs DC 15": a success.
        var roll = SkillCheck.Roll(14, 5, 15);
        Assert.Equal((14, 5, 19, 15, true), (roll.Kept, roll.Modifier, roll.Total, roll.Vs, roll.Success));
    }

    [Fact]
    public void Best_breaks_a_tie_by_member_order()
    {
        Assert.Equal("dave", SkillCheck.Best([("jess", 1), ("dave", 4), ("priya", 4), ("sam", 2)]));
        Assert.Equal("priya", SkillCheck.Best([("priya", 4), ("dave", 4)]));
        Assert.Equal("sam", SkillCheck.Best([("jess", -1), ("sam", 0)]));
        Assert.Equal("jess", SkillCheck.Best([("jess", -2)]));
        Assert.Throws<ArgumentException>(() => SkillCheck.Best([]));
    }

    /// <summary>
    /// Over every die, a check succeeds exactly when d20 + modifier reaches the DC: the chance is (21 − (DC − modifier)) / 20, held
    /// between 0 and 1, with no 5% either way from a natural 20 or 1.
    /// </summary>
    [Fact]
    public void The_chance_of_success_is_d20_plus_modifier_with_no_naturals()
    {
        for (var modifier = -3; modifier <= 12; modifier++)
            for (var dc = 5; dc <= 25; dc++)
            {
                var successes = Enumerable.Range(1, 20).Count(die => SkillCheck.Roll(die, modifier, dc).Success);
                Assert.Equal(Math.Clamp(21 - (dc - modifier), 0, 20), successes);
            }
    }

    /// <summary>
    /// The model test: dice drawn from a seeded RNG, as the game draws them (<c>Random.Next(1, 21)</c>). The same seed gives the same
    /// rolls, and over many rolls each face comes up about a twentieth of the time and the success rate matches the formula.
    /// </summary>
    [Fact]
    public void Seeded_rolls_repeat_and_follow_the_d20()
    {
        static ImmutableArray<RollResult> Rolled(int seed, int count)
        {
            var rng = new Random(seed);
            return [.. Enumerable.Range(0, count).Select(_ => SkillCheck.Roll(rng.Next(1, 21), 4, 15))];
        }

        Assert.Equal(Rolled(42, 50).Select(r => (r.Kept, r.Total, r.Success)), Rolled(42, 50).Select(r => (r.Kept, r.Total, r.Success)));
        Assert.NotEqual(Rolled(42, 50).Select(r => r.Kept), Rolled(43, 50).Select(r => r.Kept));

        const int n = 200_000;
        var rolls = Rolled(7, n);
        foreach (var face in rolls.GroupBy(r => r.Kept))
            Assert.InRange(face.Count(), n / 20 * 0.95, n / 20 * 1.05);
        Assert.Equal(Enumerable.Range(1, 20), rolls.Select(r => r.Kept).Distinct().Order());
        Assert.InRange(rolls.Count(r => r.Success) / (double)n, 0.50 - 0.01, 0.50 + 0.01);    // 11 to 20 succeed
        Assert.All(rolls, r => Assert.Equal(r.Kept + 4 >= 15, r.Success));
    }
}
