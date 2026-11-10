using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Story;

/// <summary>One view of <see cref="StoryDemo"/>: sent <paramref name="At"/> seconds into the demo, under a <paramref name="Label"/> to start from.</summary>
public sealed record StoryDemoStep(string Label, double At, TacticsHostView View);

/// <summary>
/// Dev-only views for <c>--story-demo[=&lt;step&gt;]</c>: the story on the TV with no server, from the real lore and the real region, through
/// everything a scene can show. The "!" over Pip in the world, the narrator and speakers with their faces, a leader's choice, a party vote
/// that ties and is broken, a question of eleven long options, the party picking who rolls from six cards, a roll that passes and one that
/// fails, the gap between two scenes, and the chapter's card. Line lengths are their clips', as on the server, so the voice fits.
/// </summary>
public static class StoryDemo
{
    /// <summary>The steps to start from, in order.</summary>
    public static readonly ImmutableArray<string> Labels = ["marker", "line", "choice", "gap", "vote", "tie", "many", "check", "pass", "fail", "card"];

    private const string Region = "ashen_ford";
    private const string Campaign = "story-demo";
    private static readonly Tile Spot = new(33, 59);      // ch1_mayors_request's spot, where Pip stands
    private static readonly Tile Party = new(33, 61);
    private static readonly string[] Players = ["Jess", "Dave", "Priya", "Sam", "Marcus", "Ana"];
    private static readonly string[] Classes = ["fighter", "cleric", "rogue", "wizard", "paladin", "bard"];
    private static readonly string[] Characters = ["Kara", "Aldric", "Wren", "Ilyra", "Tamsin", "Sefa"];

    /// <summary>The demo's views, the first at 0 s, with deadlines on <paramref name="start"/>'s clock.</summary>
    public static ImmutableArray<StoryDemoStep> Steps(LoreBook lore, DateTimeOffset start)
    {
        var steps = ImmutableArray.CreateBuilder<StoryDemoStep>();
        var t = 0.0;
        var seq = 0;
        var label = Labels[0];
        DateTimeOffset At(double seconds) => start + TimeSpan.FromSeconds(seconds);
        void Add(TacticsHostView view, double hold)
        {
            steps.Add(new StoryDemoStep(label, t, view));
            label = "";
            t += hold;
        }
        StoryLineView Line(string id)
        {
            var line = lore.Scenes.SelectMany(s => s.Lines).Single(l => l.Id == id);
            var who = lore.Character(line.Speaker);
            return new StoryLineView(line.Id, line.Speaker, who?.Name, who?.Preset, line.Mood, line.Text, who?.Role);
        }
        double Length(string id)
        {
            var line = lore.Scenes.SelectMany(s => s.Lines).Single(l => l.Id == id);
            return StoryTiming.Default.Line(lore.ClipSeconds(id), line.Text).TotalSeconds;
        }
        TacticsHostView Story(StoryView story) => Host(nameof(TacticsPhase.Story), story.Title, story, marker: null);
        StoryView Said(string scene, string title, string id, string state = TacticsGame.StoryStates.Lines, StoryAskView? ask = null,
            StoryCheckView? check = null, double? until = null)
            => new(seq, scene, title, state, state is TacticsGame.StoryStates.Rolled or TacticsGame.StoryStates.Gap ? null : Line(id), ask, check,
                At(until ?? t + Length(id)), Campaign);
        void Speak(string scene, string title, string id)
        {
            seq++;
            Add(Story(Said(scene, title, id)), Length(id));
        }

        Add(Host(nameof(TacticsPhase.Exploration), "Jess leads the party", null, marker: "alert"), 8);     // long enough for the world to open

        label = "line";
        foreach (var id in new[] { "ch1_mayor_open_1", "ch1_mayor_open_3", "ch1_mayor_halden_1", "ch1_mayor_halden_3" })
            Speak("ch1_mayor", "Emberwick at Dusk", id);

        label = "choice";
        seq++;
        StoryOptionView[] mayor = [Option("goblins", "Who are these goblins?"), Option("bridge", "Show us the bridge.")];
        var choice = new StoryAskView(false, 0, 1, [], false, "Jess", At(t + 30), [.. mayor], 30);
        Add(Story(Said("ch1_mayor", "Emberwick at Dusk", "ch1_mayor_halden_4", TacticsGame.StoryStates.Asking, choice)), Length("ch1_mayor_halden_4") + 1);
        Add(Story(Said("ch1_mayor", "Emberwick at Dusk", "ch1_mayor_halden_4", TacticsGame.StoryStates.Asking,
            choice with { Voted = 1, Options = [mayor[0] with { Voters = ["Jess"] }, mayor[1]] }, until: t)), 2.5);

        label = "gap";
        Add(Story(Said("ch1_ford", "The Ford Choice", "ch1_ford_vote_1", TacticsGame.StoryStates.Gap, until: t + 2)), 2.5);

        label = "vote";
        Speak("ch1_ford", "The Ford Choice", "ch1_ford_vote_1");
        seq++;
        var opened = t;
        StoryOptionView[] ford = [Option("force", "We'll clear the ford by force."), Option("parley", "We'll go and parley with the chief."),
            new("pay", "First, what's it worth to you?", "persuasion", 15, [])];
        StoryAskView Vote(int voted, params string[][] voters)
            => new(true, voted, 4, ["Ana"], false, null, At(opened + 45), [.. ford.Select((o, i) => o with { Voters = i < voters.Length ? [.. voters[i]] : [] })], 45);
        Add(Story(Said("ch1_ford", "The Ford Choice", "ch1_ford_vote_2", TacticsGame.StoryStates.Asking, Vote(0))), Length("ch1_ford_vote_2"));
        Add(Story(Said("ch1_ford", "The Ford Choice", "ch1_ford_vote_2", TacticsGame.StoryStates.Asking, Vote(2, ["Jess"], ["Priya"]), until: opened + Length("ch1_ford_vote_2"))), 2);
        Add(Story(Said("ch1_ford", "The Ford Choice", "ch1_ford_vote_2", TacticsGame.StoryStates.Asking, Vote(4, ["Jess", "Marcus"], ["Priya", "Sam"]),
            until: opened + Length("ch1_ford_vote_2"))), 2);

        label = "tie";
        var tie = new StoryAskView(true, 4, 4, ["Ana"], true, "Sam", At(t + 10), [ford[0] with { Voters = ["Jess", "Marcus"] }, ford[1] with { Voters = ["Priya", "Sam"] }], 10);
        Add(Story(Said("ch1_ford", "The Ford Choice", "ch1_ford_vote_2", TacticsGame.StoryStates.TieBreak, tie, until: t)), 5);

        label = "many";
        seq++;
        var maren = lore.Scenes.Single(s => s.Id == "ch3_maren_parley").Node("ask_me")!;
        var many = new StoryAskView(true, 1, 4, [], false, null, At(t + 45),
            [.. maren.Options.Select((o, i) => new StoryOptionView(o.Id, o.Label, o.Check?.Skill, o.Check?.Dc, i == 1 ? ["Dave"] : []))], 45);
        Add(Story(Said("ch3_maren_parley", "Under the White Cloth", "ch3_maren_parley_ask_me_1", TacticsGame.StoryStates.Asking, many)),
            Math.Max(6, Length("ch3_maren_parley_ask_me_1")));

        label = "check";
        seq++;
        var picking = t;
        StoryCheckView Check(int[] votes, string? chosen = null, RollResult? roll = null, double? deadline = null)
            => new("persuasion", 15, ford[2].Label, [.. Characters.Select((name, i) => new StoryCardView($"m{i}", name, Players[i],
                $"characters.class_{Classes[i]}", i == 2 ? 5 : i % 3 - 1 + i / 2, i == 2 ? "Proficient · CHA 14" : $"CHA {10 + 2 * (i % 3)}", i != 5,
                votes[i]))], chosen, roll, false, null, deadline is null ? null : At(deadline.Value), deadline is null ? null : 20);
        Add(Story(Said("ch1_ford", "The Ford Choice", "ch1_ford_vote_2", TacticsGame.StoryStates.PickRoller, check: Check([0, 0, 0, 0, 0, 0], deadline: picking + 20), until: t)), 3);
        Add(Story(Said("ch1_ford", "The Ford Choice", "ch1_ford_vote_2", TacticsGame.StoryStates.PickRoller, check: Check([0, 0, 3, 1, 0, 0], deadline: picking + 20), until: t)), 2.5);

        label = "pass";
        Add(Story(Said("ch1_ford", "The Ford Choice", "", TacticsGame.StoryStates.Rolled, check: Check([0, 0, 3, 1, 0, 0], "m2", Rolls.Flat(14, 5, 15)), until: t + 4.5)), 4.5);
        Speak("ch1_ford", "The Ford Choice", "ch1_ford_pay_pass_1");

        label = "fail";
        seq++;
        Add(Story(Said("ch1_ford", "The Ford Choice", "ch1_ford_vote_2", TacticsGame.StoryStates.PickRoller, check: Check([0, 0, 0, 0, 2, 0], deadline: t + 20), until: t)), 2);
        Add(Story(Said("ch1_ford", "The Ford Choice", "", TacticsGame.StoryStates.Rolled, check: Check([0, 0, 0, 0, 2, 0], "m4", Rolls.Flat(3, 2, 15)), until: t + 4.5)), 4.5);
        Speak("ch1_ford", "The Ford Choice", "ch1_ford_pay_fail_2");

        label = "card";
        Add(Host(nameof(TacticsPhase.Story), "The story goes on", null, marker: null) with { Chapter = "Chapter I · The Ashen Ford" }, 6);
        Add(Host(nameof(TacticsPhase.Exploration), "Jess leads the party", null, marker: null), 3);
        return steps.ToImmutable();
    }

    private static StoryOptionView Option(string id, string label) => new(id, label, null, null, []);

    private static TacticsHostView Host(string phase, string banner, StoryView? story, string? marker)
        => new(phase, banner, null, 1, [.. Players.Take(4)], null, null, null, null, null, Explore(marker), story);

    private static ExploreView Explore(string? emote)
        => new(Region, "The Ashen Ford", 1, 0, Party, new Tile(Party.X + 1, Party.Y), false, [],
            [.. Players.Take(4).Select((p, i) => new WalkerView($"m{i}", p, $"characters.class_{Classes[i]}", true, i == 0))], "Jess", 1,
            Markers: [new StoryMarker("ch1_mayors_request", Spot, "characters.child", emote)]);
}
