namespace Redline.Sim;

/// <summary>The default scenario suite. Names are stable so <c>--only</c> prefixes and seeds stay reproducible.</summary>
public static class Scenarios
{
    private static readonly Strategy[] All = Enum.GetValues<Strategy>();

    private static string Slug(Strategy s) => s switch
    {
        Strategy.AlwaysCruise => "cruise",
        Strategy.AlwaysPush => "push",
        Strategy.AlwaysRedline => "redline",
        Strategy.Random => "random",
        Strategy.Sensible => "sensible",
        Strategy.Chaser => "chaser",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static IReadOnlyList<Scenario> Default()
    {
        var list = new List<Scenario>
        {
            new("ai-only", "22 AI, no humans (baseline for the AI field)", []),
        };

        // 1 human + 21 AI, every strategy at both skills.
        foreach (var skill in new[] { Skill.Casual, Skill.Strong })
            foreach (var s in All)
                list.Add(new($"solo-{Slug(s)}-{skill.Name}", $"1 human ({s}/{skill.Name}) + 21 AI", [new BotSpec(s, skill)]));

        // 4 humans + 18 AI: two casual and two strong, all on one strategy.
        foreach (var s in All)
            list.Add(new($"four-{Slug(s)}", $"4 humans ({s}: 2 casual, 2 strong) + 18 AI",
                [new(s, Skill.Casual), new(s, Skill.Casual), new(s, Skill.Strong), new(s, Skill.Strong)]));

        // 8 humans + 14 AI: four casual and four strong, all on one strategy.
        foreach (var s in All)
            list.Add(new($"eight-{Slug(s)}", $"8 humans ({s}: 4 casual, 4 strong) + 14 AI",
                [.. Enumerable.Repeat(new BotSpec(s, Skill.Casual), 4), .. Enumerable.Repeat(new BotSpec(s, Skill.Strong), 4)]));

        // Mixed strategies in one race.
        list.Add(new("mixed-casual", "6 casual humans, one per strategy, + 16 AI", [.. All.Select(s => new BotSpec(s, Skill.Casual))]));
        list.Add(new("mixed-strong", "6 strong humans, one per strategy, + 16 AI", [.. All.Select(s => new BotSpec(s, Skill.Strong))]));
        list.Add(new("mixed-eight", "8 humans: Sensible, Chaser, AlwaysRedline at both skills + Random and AlwaysCruise casual, + 14 AI",
        [
            new(Strategy.Sensible, Skill.Strong), new(Strategy.Sensible, Skill.Casual),
            new(Strategy.Chaser, Skill.Strong), new(Strategy.Chaser, Skill.Casual),
            new(Strategy.AlwaysRedline, Skill.Strong), new(Strategy.AlwaysRedline, Skill.Casual),
            new(Strategy.Random, Skill.Casual), new(Strategy.AlwaysCruise, Skill.Casual),
        ]));
        list.Add(new("party-three", "3 casual humans, all Sensible, + 19 AI (the qualifying headline: does the best of three take pole?)",
            [new(Strategy.Sensible, Skill.Casual), new(Strategy.Sensible, Skill.Casual), new(Strategy.Sensible, Skill.Casual)]));
        list.Add(new("party-four", "4 humans as a realistic group: 1 strong + 3 casual, all Sensible, + 18 AI",
            [new(Strategy.Sensible, Skill.Strong), new(Strategy.Sensible, Skill.Casual), new(Strategy.Sensible, Skill.Casual), new(Strategy.Sensible, Skill.Casual)]));
        return list;
    }
}
