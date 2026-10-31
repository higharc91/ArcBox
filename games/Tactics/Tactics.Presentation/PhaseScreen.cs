using System.Collections.Immutable;
using System.Globalization;
using Tactics.Game;

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
public sealed record PhaseScreen(string Meta, string Title, Tone Tone, string? Notice, int PartyLevel,
    ImmutableArray<ScreenRow> Rows, string? Empty, string Shot, BattleView? Board = null)
{
    /// <summary>The screen for <paramref name="view"/>. A phase this build doesn't know still shows the banner.</summary>
    public static PhaseScreen Of(TacticsHostView view)
    {
        var shot = $"tactics-{view.Phase.ToLowerInvariant()}";
        var screen = new PhaseScreen(view.Phase, view.Banner, Tone.Neutral, view.Notice, view.PartyLevel, [], null, shot);
        if (!Enum.TryParse<TacticsPhase>(view.Phase, out var phase))
            return screen;
        return phase switch
        {
            TacticsPhase.Muster => Muster(screen, view),
            TacticsPhase.EncounterPick => EncounterPick(screen, view),
            TacticsPhase.Results => Results(screen, view),
            TacticsPhase.Resuming => Resuming(screen, view),
            TacticsPhase.Battle => Battle(screen, view),
            _ => screen,
        };
    }

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
            Rows = [.. lines.Select(r => new ScreenRow(r.Name, r.Injuries.IsEmpty ? "No injuries" : string.Join(", ", r.Injuries), null,
                $"+{r.Xp.ToString(CultureInfo.InvariantCulture)} XP"))],
            Empty = lines.IsEmpty ? "No results to show" : null,
        };
    }

    private static PhaseScreen Resuming(PhaseScreen screen, TacticsHostView view) => screen with
    {
        Meta = view.Battle is { } battle ? $"Resuming · {Words.Title(battle.Encounter)}" : "Resuming",
        Empty = "Waiting for the players to come back",
    };

    // The board, with the banner and which fight this is. The encounter's name is for a TV that can't draw the map.
    private static PhaseScreen Battle(PhaseScreen screen, TacticsHostView view)
    {
        if (view.Battle is not { } battle)
            return screen with { Meta = "Battle" };
        var name = Words.Title(battle.Encounter);
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
