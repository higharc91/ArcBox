using Meridian.Presentation.Board;

namespace Meridian.Presentation.Debrief;

/// <summary>A seat at the Debrief: its role revealed, and whether it won.</summary>
public sealed record DebriefRow(string Seat, string Name, string Mark, int Colour, string Role, bool Won);

/// <summary>An award and who took it.</summary>
public sealed record DebriefAward(string Title, string Name, int Colour);

/// <summary>
/// The TV's Debrief (spec §3.1; M4 plan, Task 8): the outcome, every seat's role, the awards, and all three decks at once,
/// because nothing is secret any more. The host view only carries the reveal once the game is over.
/// </summary>
public sealed record DebriefModel(string Title, string Outcome, EqArray<DebriefRow> Rows, EqArray<DebriefAward> Awards)
{
    public static DebriefModel? Of(MeridianHostView view)
    {
        if (view.Scenario is not { Reveal: { } reveal } panel)
        {
            return null;
        }
        var marks = BoardStyle.Marks([.. view.Seats.Select(s => s.Name)]);
        SeatCard? Seat(string id) => view.Seats.FirstOrDefault(s => s.Id == id);
        string Mark(string id)
        {
            for (var i = 0; i < view.Seats.Count; i++)
            {
                if (view.Seats[i].Id == id)
                {
                    return marks[i];
                }
            }
            return "?";
        }
        var rows = reveal.Select(r => new DebriefRow(r.Id, Seat(r.Id)?.Name ?? r.Id, Mark(r.Id), Seat(r.Id)?.Colour ?? 0, r.Role, r.Won))
                         .OrderByDescending(r => r.Won).ToEqArray();
        var awards = (panel.Awards ?? []).Select(a => new DebriefAward(a.Title, Seat(a.Seat)?.Name ?? a.Seat, Seat(a.Seat)?.Colour ?? 0)).ToEqArray();
        return new DebriefModel(panel.Title, panel.Outcome ?? view.Banner, rows, awards);
    }

    /// <summary>The Debrief's layout: the text column on the left, and the three decks side by side on the right.</summary>
    public static (Box Text, Box Command, Box Habitat, Box Engineering) Layout(TvSize size)
    {
        var main = DeckLayout.Areas(size) is var a ? new Box(0, a.Hud.H, size.W, size.H - a.Hud.H) : default;
        var text = main with { W = main.W * 0.36 };
        var decks = main with { X = text.W, W = main.W - text.W };
        var third = decks.W / 3;
        return (text, decks with { W = third }, decks with { X = decks.X + third, W = third }, decks with { X = decks.X + 2 * third, W = third });
    }
}
