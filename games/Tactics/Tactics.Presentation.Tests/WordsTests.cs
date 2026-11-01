using Tactics.Logic;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests;

/// <summary><see cref="Words"/>.</summary>
public sealed class WordsTests
{
    [Theory]
    [InlineData("Goblin Boss", "Boss")]
    [InlineData("Goblin Archer", "Archer")]
    [InlineData("Hobgoblin", "Hobgoblin")]
    [InlineData("Ana", "Ana")]
    [InlineData("  Dire  Wolf ", "Wolf")]
    [InlineData("", "")]
    public void A_short_name_is_the_last_word(string name, string expected)
        => Assert.Equal(expected, Words.Short(name));

    /// <summary>Two kinds of unit that read the same on the strip in one fight would only have their portraits to tell them apart.</summary>
    [Fact]
    public void No_two_kinds_of_enemy_in_an_encounter_share_a_short_name()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        foreach (var encounter in rules.Encounters.Values)
        {
            var units = encounter.Enemies.Select(e => e.Unit)
                .Concat(encounter.Triggers.SelectMany(t => t.Do).Where(d => d.Spawn is not null).Select(d => d.Spawn!.Unit))
                .Distinct(StringComparer.Ordinal).Select(u => rules.Monsters[u].Name).ToArray();

            Assert.Equal(units.Length, units.Select(Words.Short).Distinct(StringComparer.Ordinal).Count());
        }
    }
}
