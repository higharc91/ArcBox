namespace Tactics.Logic;

/// <summary>Pure camp job rules; dice are supplied by the caller and saved with their results.</summary>
public static class RecoveryRules
{
    /// <summary>The five available jobs.</summary>
    public static readonly string[] Jobs = ["tend", "rally", "cook", "watch", "rest"];
    /// <summary>A job's skill; Rally uses the better social skill, with Persuasion on a tie.</summary>
    public static string Skill(string job, Func<string, int> bonus) => job switch
    {
        "tend" => "medicine", "cook" => "survival", "watch" => "perception",
        "rally" => bonus("performance") > bonus("persuasion") ? "performance" : "persuasion", _ => "survival",
    };
    /// <summary>The check DC; Watch compares its total against each hidden thing's own DC.</summary>
    public static int Dc(string job) => job switch { "tend" or "rally" => 12, "cook" => 10, _ => 0 };
    /// <summary>The injury kind a targeted job can mend.</summary>
    public static string? Mends(string job) => job switch { "tend" => "body", "rally" => "spirit", _ => null };
    /// <summary>Checks have neither automatic natural-one failure nor natural-twenty success.</summary>
    public static bool Passed(int die, int bonus, int dc) => SkillCheck.Roll(die, bonus, dc).Success;
    /// <summary>Healing spells are discovered from the character's actual abilities and the pack's healing AI tag.</summary>
    public static bool HealingSpell(CharacterRecord character, RulesPack rules)
        => SheetBuilder.ForCharacter(character, rules).Abilities.Any(id => rules.Abilities.TryGetValue(id, out var a)
            && a.Tier > 0 && a.Ai?.Tags.Contains("heal") == true);
}
