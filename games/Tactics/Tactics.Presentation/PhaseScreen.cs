using System.Collections.Immutable;
using System.Globalization;
using Tactics.Game;
using Tactics.Presentation.Audio;
using Tactics.Presentation.World;

namespace Tactics.Presentation;

/// <summary>How a screen feels: the colour of its accents.</summary>
public enum Tone { Neutral, Good, Bad }

/// <summary>One line of a phase screen's table.</summary>
/// <param name="Title">The big text: a name.</param>
/// <param name="Detail">The small line under it, or empty.</param>
/// <param name="Chip">A short tag at the right ("READY"), or null.</param>
/// <param name="Value">The big text at the right ("+120 XP"), or null.</param>
/// <param name="Dim">Greyed out: on the list, but not on offer.</param>
public sealed record ScreenRow(string Title, string Detail, string? Chip, string? Value, bool Dim = false);

/// <summary>
/// What the TV shows for a host view, worded and ready to draw (adapter spec §3, §8): the header, the notice and one table,
/// which is the Muster roster, the encounter list or the results. Pure, so <c>TacticsScene</c> only draws it.
/// </summary>
/// <param name="Meta">The small line above the title.</param>
/// <param name="Title">The game's banner.</param>
/// <param name="Tone">The screen's accent colour.</param>
/// <param name="Notice">A note from the game for everyone to read, or null.</param>
/// <param name="PartyLevel">The party's level.</param>
/// <param name="Rows">The table.</param>
/// <param name="Empty">The big text to show instead of the table when it has no rows, or null.</param>
/// <param name="Shot">The label for <c>--shots</c>: a new label is a new screenshot.</param>
/// <param name="Board">The battle to draw on the board, or null. If it can't be drawn, the screen still reads without it.</param>
/// <param name="Music">What should be playing on this screen, or null to leave it.</param>
/// <param name="Ambience">The bed under it, or null for quiet.</param>
/// <param name="Banner">The strip in the title art to play over the results, or null.</param>
/// <param name="World">The party to draw in the world, or null. If it can't be drawn, the screen still reads without it.</param>
/// <param name="Walk">What the world's panels say, with <paramref name="World"/>.</param>
/// <param name="InPlace">The fight's board is drawn on the world (overworld 5c §4), not on the plain board: <see cref="World.StageDirector.InPlace"/>.</param>
/// <param name="OverWorld">
/// The header and table go over the world, with no backdrop: the results and the wait to resume of a fight in the world, which keep the
/// world on screen.
/// </param>
public sealed record PhaseScreen(string Meta, string Title, Tone Tone, string? Notice, int PartyLevel,
    ImmutableArray<ScreenRow> Rows, string? Empty, string Shot, BattleView? Board = null, MusicCue? Music = null, string? Ambience = null, string? Banner = null,
    ExploreView? World = null, WalkHud? Walk = null, bool InPlace = false, bool OverWorld = false)
{
    /// <summary>The rows the table has room for on a screen of its own: a row is a slot's height, whatever the count.</summary>
    public const int TableSlots = 6;

    /// <summary>
    /// The table fills the column under the header, as on a screen of its own. Over the world (<see cref="OverWorld"/>) it's only as tall as
    /// its rows, or its <see cref="Empty"/> text, so the world shows below it rather than an empty glass panel.
    /// </summary>
    public bool TableFills => !OverWorld;

    /// <summary>The slots left empty under the rows of a table that fills the column: none over the world, and none with no rows, when <see cref="Empty"/> fills it.</summary>
    public int EmptySlots => TableFills && !Rows.IsEmpty ? Math.Max(0, TableSlots - Rows.Length) : 0;

    /// <summary>
    /// The screen for <paramref name="view"/>. A phase this build doesn't know still shows the banner. With <paramref name="stage"/>, which has
    /// had the view, a fight in the world keeps the world (<see cref="World.StageDirector.InWorld"/>), and the party is the one the stage lets
    /// the walk follow (<see cref="World.StageDirector.Walk"/>), so its new epoch waits for the results to be over. Without it, every fight is
    /// on the plain board, as before the world.
    /// </summary>
    public static PhaseScreen Of(TacticsHostView view, StageDirector? stage = null)
    {
        var shot = $"tactics-{view.Phase.ToLowerInvariant()}";
        var screen = new PhaseScreen(view.Phase, view.Banner, Tone.Neutral, view.Notice, view.PartyLevel, [], null, shot);
        if (!Enum.TryParse<TacticsPhase>(view.Phase, out var phase))
            return screen;
        var drawn = phase switch
        {
            TacticsPhase.Muster => Muster(screen, view),
            TacticsPhase.EncounterPick => EncounterPick(screen, view),
            TacticsPhase.Results => Results(screen, view),
            TacticsPhase.Resuming => Resuming(screen, view),
            TacticsPhase.Battle => Battle(screen, view),
            TacticsPhase.Exploration => Exploration(screen, view),
            TacticsPhase.Story => Story(screen, view),
            _ => screen,
        };
        drawn = drawn with { Music = MusicChoice.Of(view), Ambience = MusicChoice.AmbienceOf(view) };
        if (stage is null)
            return drawn;
        var world = stage.Walk ?? view.Explore;
        if (stage.InWorld && phase is TacticsPhase.Battle or TacticsPhase.Resuming or TacticsPhase.Results && world is not null)
            drawn = drawn with { World = world, Walk = WalkHud.Of(world, view.PartyLevel), OverWorld = phase != TacticsPhase.Battle };
        else if (drawn.World is not null && world is not null)
            drawn = drawn with { World = world, Walk = WalkHud.Of(world, view.PartyLevel) };
        return drawn with { InPlace = stage.InPlace };
    }

    private static PhaseScreen Exploration(PhaseScreen screen, TacticsHostView view) => screen with
    {
        Meta = view.Explore is { } explore ? $"Exploration · {explore.Name}" : "Exploration",
        Empty = view.Explore?.Name,
        World = view.Explore,
        Walk = view.Explore is { } shown ? WalkHud.Of(shown, view.PartyLevel) : null,
    };

    // A scene plays over the world (overworld 5d): the world stays drawn, and the scene's own panels are the story's to draw.
    private static PhaseScreen Story(PhaseScreen screen, TacticsHostView view) => Exploration(screen, view) with
    {
        Meta = view.Story is { } story ? $"Story · {story.Title}" : "Story",
    };

    private static PhaseScreen Muster(PhaseScreen screen, TacticsHostView view) => screen with
    {
        Meta = $"Muster · {view.Mustered.Length} ready",
        Rows = [.. view.Mustered.Select(name => new ScreenRow(name, "", "READY", null))],
        Empty = view.Mustered.IsEmpty ? "Nobody's ready yet" : null,
        Shot = $"{screen.Shot}-{view.Mustered.Length}",
    };

    private static PhaseScreen EncounterPick(PhaseScreen screen, TacticsHostView view)
    {
        var encounters = view.Encounters ?? [];
        return screen with
        {
            Meta = $"Encounter pick · {Count(encounters.Length, "battle")}",
            Rows = [.. encounters.Select(e => new ScreenRow(e.Name, PartyLine(e), e.Fits ? null : "PARTY TOO BIG",
                $"Lv {(e.Level is { } level ? level.ToString(CultureInfo.InvariantCulture) : "?")}", !e.Fits))],
            Empty = encounters.IsEmpty ? "No battles to choose from" : null,
        };
    }

    private static PhaseScreen Results(PhaseScreen screen, TacticsHostView view)
    {
        var lines = view.Results ?? [];
        return screen with
        {
            Meta = view.Outcome switch
            {
                "Won" => "Results · Battle won",
                "Lost" => "Results · Battle lost",
                "Fled" => "Results · The party fled",
                _ => "Results",
            },
            Tone = view.Outcome == "Won" ? Tone.Good : Tone.Bad,
            Banner = view.LeveledUp ? "level_up_anim" : view.Outcome switch
            {
                "Won" => "victory_anim",
                "Lost" => "defeat_anim",
                _ => null,
            },
            // After a loss the game's one line says it all (TacticsGame.LossNote): no injury words and no "+0 XP" on each row.
            Notice = view.LeveledUp ? $"LEVEL UP! The party reached level {view.PartyLevel}." : (view.ResultNote ?? screen.Notice),
            Rows = [.. lines.Select(r => view.Outcome == "Lost"
                ? new ScreenRow(r.Name, "", null, null)
                : new ScreenRow(r.Name, r.Injuries.IsEmpty ? "No injuries" : string.Join(", ", r.Injuries), null,
                    $"+{r.Xp.ToString(CultureInfo.InvariantCulture)} XP"))],
            Empty = lines.IsEmpty ? "No results to show" : null,
        };
    }

    // A scene waiting for the party (overworld 5d §8) keeps the world behind the wait, as the wait to resume a fight in the world does.
    private static PhaseScreen Resuming(PhaseScreen screen, TacticsHostView view) => screen with
    {
        Meta = view.Battle is { } battle ? $"Resuming · {Words.Encounter(battle)}" : "Resuming",
        Empty = "Waiting for the players to come back",
        World = view.Battle is null ? view.Explore : null,
        Walk = view.Battle is null && view.Explore is { } explore ? WalkHud.Of(explore, view.PartyLevel) : null,
        OverWorld = view.Battle is null && view.Explore is not null,
    };

    // The board, with the banner and which fight this is. The encounter's name is for a TV that can't draw the map.
    private static PhaseScreen Battle(PhaseScreen screen, TacticsHostView view)
    {
        if (view.Battle is not { } battle)
            return screen with { Meta = "Battle" };
        var name = Words.Encounter(battle);
        var round = $"Round {battle.Round.ToString(CultureInfo.InvariantCulture)}";
        return screen with
        {
            Meta = $"Battle · {name} · {round}",
            Title = view.Banner.Length > 0 ? view.Banner : round,
            Empty = name,
            Shot = $"{screen.Shot}-{battle.Map}{ShotMoment(battle)}",
            Board = battle,
        };
    }

    // A first look at what a fight does to its units is worth a screenshot.
    private static string ShotMoment(BattleView battle)
    {
        if (battle.Units.Any(u => u.Life == "Downed"))
            return "-downed";
        return battle.Units.Any(u => u.Life == "Up" && !u.Conditions.IsEmpty) ? "-conditions" : "";
    }

    private static string PartyLine(EncounterOption e)
    {
        var size = e.Min == e.Max ? Count(e.Min, "player") : $"{e.Min}–{e.Max} players";
        return e.Companions > 0 ? $"{size} · +{Count(e.Companions, "AI companion")}" : size;
    }

    private static string Count(int n, string noun)
        => $"{n.ToString(CultureInfo.InvariantCulture)} {noun}{(n == 1 ? "" : "s")}";
}
