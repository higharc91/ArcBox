using System.Text.RegularExpressions;
using Meridian.Presentation.Audio;

namespace Meridian.Presentation.Tests.Audio;

public sealed class TillerLinesTests
{
    private static Presented Now(string kind, string title, string text, CardKind? deck = null)
        => new(1, kind, new SeatId("p1"), title, text, 9, Deck: deck);

    [Fact]
    public void An_incident_is_read_in_full_and_other_cards_by_title()
    {
        Assert.Equal("Hull Groan. The hull shifts.", TillerLines.For(Now("card", "Hull Groan", "The hull shifts.", CardKind.Incident))!.Text);
        Assert.Equal("Cutting Torch.", TillerLines.For(Now("card", "Cutting Torch", "Cuts through sealed hatches.", CardKind.Salvage))!.Text);
        Assert.Equal(LineUrgency.CutIn, TillerLines.For(Now("card", "Hull Groan", "The hull shifts.", CardKind.Incident))!.Urgency);
    }

    [Theory]
    [InlineData("roll")]
    [InlineData("reveal")]
    [InlineData("fight")]
    [InlineData("sealed")]
    public void What_the_board_shows_is_not_spoken(string kind)
        => Assert.Null(TillerLines.For(Now(kind, "Something", "3 dice: 4")));

    [Fact]
    public void Round_warnings_and_the_outcome_are_queued_not_cut_in()
    {
        var round = TillerLines.For(Now("round", "Round 7", "Three rounds remain."))!;
        Assert.Equal("Three rounds remain.", round.Text);
        Assert.Equal(LineUrgency.Queue, round.Urgency);
    }

    [Fact]
    public void The_pause_names_nobody_and_the_grace_is_silent()
    {
        // Review focus 7, spoken: the pause sounds the same whoever dropped.
        var ann = TillerLines.ForWait(new HostWait("pause", "p1", "Ann", 60))!;
        var bo = TillerLines.ForWait(new HostWait("pause", "p2", "Bo", 41))!;
        Assert.Equal(ann, bo);
        Assert.DoesNotContain("Ann", ann.Text);
        Assert.Null(TillerLines.ForWait(new HostWait("grace", "p1", "Ann", 20)));
    }

    [Fact]
    public void The_estimate_is_the_line_at_the_rules_rate()
        => Assert.Equal(4 / 2.7, TillerLines.Seconds(new Line("Three rounds remain now.", LineUrgency.Queue), new MeridianRules()), 6);

    // ---- Review focus 3: nothing secret is spoken

    public static TheoryData<string, int> Games() => AllScenarios.WithPlayers;

    [Theory]
    [MemberData(nameof(Games))]
    public void No_spoken_line_gives_away_a_secret(string scenario, int players)
    {
        var game = new Harness(players, seed: 1, scenario).Board();
        var heard = 0;
        var lastSeq = 0;
        game.PlayUntil(() =>
        {
            var lines = new List<Line>();
            if (game.Session.Queue is [var now, ..] && now.Seq != lastSeq && now.Kind != "over")
            {
                lastSeq = now.Seq;
                if (TillerLines.For(now) is { } line)
                {
                    lines.Add(line);
                }
            }
            if (((MeridianHostView)game.Game.HostView()).Wait is { } wait && TillerLines.ForWait(wait) is { } paused)
            {
                lines.Add(paused);
            }
            if (game.Session.Game is { Board: not null, Phase: not Phase.Over } state)
            {
                foreach (var line in lines)
                {
                    heard++;
                    var leaks = Leaks(game, state, line.Text);
                    Assert.True(leaks.Count == 0, $"'{line.Text}' gives away {string.Join(", ", leaks)}");
                }
            }
            return false;
        }, steps: 6000);
        Assert.True(heard > 0, "Tiller said nothing after the Breach");
    }

    /// <summary>
    /// What a line must not say: a seat named together with its secret role, or any secret objective, token or counter.
    /// That a mimic exists is public (the announcement says so); who it is, is not.
    /// </summary>
    private static List<string> Leaks(Harness game, GameState state, string text)
    {
        var board = state.Board!;
        var def = game.Pack.ScenarioDef(board.Scenario);
        bool Says(string word) => Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
        var leaks = new List<string>();
        foreach (var role in board.Roles.Where(r => !r.Audience.Includes(state, null)))
        {
            var seat = game.Session.SeatOf(role.Seat).Name;
            if (Says(seat) && (Says(role.Role) || Says(def.Role(role.Role).Name)))
            {
                leaks.Add($"{seat} as {role.Role}");
            }
        }
        leaks.AddRange(board.Objectives.Where(o => !o.Audience.Includes(state, null) && text.Contains(o.Text)).Select(o => $"objective {o.Id}"));
        leaks.AddRange(board.Tokens.Where(t => !t.Audience.Includes(state, null) && Says(t.Id)).Select(t => $"token {t.Id}"));
        leaks.AddRange(board.Counters.Where(c => !c.Audience.Includes(state, null) && Says(c.Id)).Select(c => $"counter {c.Id}"));
        return leaks;
    }

    [Fact]
    public void The_leak_check_catches_a_named_traitor()
    {
        var game = new Harness(players: 4, seed: 1, OneOfUsIsnt.Id).Board();
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Endgame);
        var mimic = game.State.Board!.Roles.Single(r => r.Role == OneOfUsIsnt.Mimic);
        var name = game.Session.SeatOf(mimic.Seat).Name;
        Assert.NotEmpty(Leaks(game, game.State, $"Attention: {name} is the mimic."));
        Assert.Empty(Leaks(game, game.State, $"{name} is named."));
    }
}
