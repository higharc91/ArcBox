using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// What the party has, for an option's tags (lore §7): every member's background, race and class, here or not (5d plan, default 8),
/// as skill checks count away characters too.
/// </summary>
public sealed record PartyTags(ImmutableHashSet<string> Backgrounds, ImmutableHashSet<string> Races, ImmutableHashSet<string> Classes)
{
    /// <summary>A party with nothing to match: only untagged options show.</summary>
    public static PartyTags None { get; } = new([], [], []);

    /// <summary>The tags of these characters together.</summary>
    public static PartyTags Of(IEnumerable<CharacterRecord> members)
    {
        var all = members.ToList();
        return new([.. all.Select(m => m.Background)], [.. all.Select(m => m.Race)], [.. all.Select(m => m.Class)]);
    }

    /// <summary>True when the party has at least one of <paramref name="tags"/>, of any kind, or there are none.</summary>
    public bool Match(OptionTags tags)
        => !tags.Any || tags.Background.Any(Backgrounds.Contains) || tags.Race.Any(Races.Contains) || tags.Class.Any(Classes.Contains);
}

/// <summary>
/// How a scene is walked (lore §7, Scenes; 5d plan §3), with no clock: the game asks which scene plays next, what a node offers, what
/// an unanswered question takes, and where an option leads. The game keeps the cursor and the timers.
/// </summary>
public static class SceneRun
{
    /// <summary>True when <paramref name="scene"/> plays with these flags: all of its <c>requires</c> are set and none of its <c>unless</c>.</summary>
    public static bool Plays(SceneDef scene, IReadOnlySet<string> flags)
        => scene.Requires.All(flags.Contains) && !scene.Unless.Any(flags.Contains);

    /// <summary>
    /// The scene of a beat to play next: the first of <paramref name="scenes"/> (the beat's, in file order) after the one named
    /// <paramref name="after"/>, in the stage <paramref name="aftermath"/> says (a battle beat's aftermath, or else its lead-in, which is
    /// all a story beat has), that <see cref="Plays"/> with <paramref name="flags"/> as they are now. Each scene is tested as it starts,
    /// so a vote in one picks which of the next plays. <paramref name="after"/> is null to start a stage; one that isn't among the scenes
    /// (gone in a content update) starts it again from the beginning. Null when the stage has nothing more to play.
    /// </summary>
    public static SceneDef? Next(ImmutableArray<SceneDef> scenes, IReadOnlySet<string> flags, string? after, bool aftermath)
    {
        var from = after is null ? 0 : scenes.Select(s => s.Id).ToList().IndexOf(after) + 1;
        return scenes.Skip(from).FirstOrDefault(s => s.After == aftermath && Plays(s, flags));
    }

    /// <summary>
    /// What <paramref name="node"/> offers, in its order: each option whose <c>requires</c> are set, that the party's tags match, and
    /// that doesn't lead back to a node that's already played in this scene (<paramref name="played"/>, required: an empty set
    /// only as a scene starts). That last is so a hub's
    /// questions can each be asked once: an option asked, or a check tried, is gone the next time round, and only the way on is left.
    /// Without it, a hub nobody answers would take its first question for ever (5d plan, default 3 and the user's decision 2).
    /// </summary>
    public static ImmutableArray<SceneOption> Offered(SceneNode node, IReadOnlySet<string> flags, PartyTags tags, IReadOnlySet<string> played)
        => [.. node.Options.Where(o => o.Requires.All(flags.Contains) && tags.Match(o.Tags) && !o.Targets.Any(played.Contains))];

    /// <summary>
    /// What a question nobody answers takes, from what's <paramref name="offered"/>: a vote takes the first option, as the spec says, and
    /// a choice the first with no check, so a timer never spends a roll (5d plan, default 3). When every option is a check (Chapter II's
    /// trick), there's no way round one, and it takes the first. Throws when nothing's offered: the game ends the scene instead.
    /// </summary>
    public static SceneOption Unanswered(ImmutableArray<SceneOption> offered, bool vote)
    {
        if (offered.IsEmpty)
            throw new ArgumentException("Nothing is offered, so there's nothing to take.", nameof(offered));
        return vote ? offered[0] : offered.FirstOrDefault(o => o.Check is null) ?? offered[0];
    }

    /// <summary>
    /// Where <paramref name="option"/> leads: its check's pass or fail node, as <paramref name="passed"/> says, or its <c>to</c>. A
    /// check needs <paramref name="passed"/>.
    /// </summary>
    public static string Target(SceneOption option, bool? passed = null) => option.Check switch
    {
        { } check when passed is { } p => p ? check.Pass : check.Fail,
        not null => throw new ArgumentException($"The option '{option.Id}' is a check, so it needs a roll.", nameof(passed)),
        _ => option.To!,
    };

    /// <summary>
    /// The flags picking <paramref name="option"/> sets: its own, then, for a vote's option that stands for a choice of
    /// <paramref name="beat"/>, that choice's (lore §7: they're set when it wins).
    /// </summary>
    public static ImmutableArray<string> Sets(SceneOption option, ArcBeat beat)
        => [.. option.Sets, .. beat.Choices.Where(c => c.Id == option.Choice).SelectMany(c => c.Sets)];
}
