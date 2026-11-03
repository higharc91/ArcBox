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
    /// <summary>Clear, reachable seats around a blocking fire. The fire itself cannot seed a walk flood.</summary>
    public static System.Collections.Immutable.ImmutableArray<Tile> Seats(Terrain terrain, Tile fire, IReadOnlySet<Tile> reached, int count)
    {
        Tile[] ring = [new(-2, 0), new(-1, -2), new(1, -2), new(2, 0), new(1, 2), new(-1, 2)];
        var candidates = reached.Where(t => t != fire && terrain.Walkable(t) && Math.Abs(t.X - fire.X) <= 4 && Math.Abs(t.Y - fire.Y) <= 4).Order().ToArray();
        var chosen = new List<Tile>();
        for (var i = 0; i < count; i++)
        {
            var aim = new Tile(fire.X + ring[i % ring.Length].X, fire.Y + ring[i % ring.Length].Y);
            var available = candidates.Where(t => !chosen.Contains(t)).ToArray();
            if (available.Length == 0) break;
            var spaced = available.Where(t => chosen.All(s => Math.Abs(t.X - s.X) + Math.Abs(t.Y - s.Y) >= 2)).ToArray();
            chosen.Add((spaced.Length > 0 ? spaced : available).OrderBy(t => (t.X - aim.X) * (t.X - aim.X) + (t.Y - aim.Y) * (t.Y - aim.Y)).ThenBy(t => t).First());
        }
        return [.. chosen];
    }

}
