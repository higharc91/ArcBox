using System.Text.Json;
using ArcBox.Sdk;
using Meridian.Logic;

namespace Meridian.Game;

// Presentation pacing (spec §3.2; M2 plan, Task 3).
public sealed partial class MeridianGame
{
    /// <summary>The TV's signal that it finished presenting an event: <c>presented { seq }</c>.</summary>
    public const string PresentedSignal = "presented";

    private sealed record SeqPayload(int Seq);

    public void OnHostSignal(string kind, PlayerInput payload)
    {
        if (kind != PresentedSignal || Session.Queue.IsEmpty)
        {
            return;
        }
        int seq;
        try
        {
            seq = payload.Read<SeqPayload>().Seq;
        }
        catch (JsonException)
        {
            return;
        }
        // A signal only ever shortens a wait, and only for the event at the front (M2 review focus 7).
        if (seq == Session.Queue[0].Seq)
        {
            Pop();
        }
    }

    /// <summary>Queues the events the TV presents on their own, numbered in order.</summary>
    private void Present(EqArray<GameEvent> events)
    {
        var next = Session.NextPresent;
        var queue = Session.Queue.Items.ToBuilder();
        foreach (var e in events)
        {
            if (Presentable(e, next) is { } item)
            {
                queue.Add(item);
                next++;
            }
        }
        Session = Session with { Queue = new EqArray<Presented>(queue.ToImmutable()), NextPresent = next };
    }

    /// <summary>The front event has been shown: move on. With the queue empty, the next decision gets its full time.</summary>
    private void Pop()
    {
        Session = Session with { Queue = Session.Queue.RemoveAt(0) };
        if (Session.Queue.IsEmpty)
        {
            switch (Session.Game!.Phase)
            {
                case Phase.Breach when Session.Phase == SessionPhase.Explore:
                    if (Session.Game.Board is null)
                    {
                        End();   // no scenario could be set up: end at the Breach, as stage M2 did
                    }
                    else
                    {
                        EnterBriefing();
                    }
                    return;
                case Phase.Over when Session.Phase == SessionPhase.Endgame:
                    EnterDebrief();
                    return;
            }
        }
        Rearm(fresh: true);
    }

    /// <summary>
    /// What the TV shows for an event, and how long to wait for it at most. Moves, trait changes and turn changes aren't
    /// presented; they just update the board.
    /// </summary>
    private Presented? Presentable(GameEvent e, int seq) => e switch
    {
        CompartmentRevealed r => new Presented(seq, "reveal", r.Seat, _pack.Tile(r.Compartment.Tile).Name, "", 1.2,
                                               Compartment: r.Compartment.Id.Value),
        HatchSealed h => new Presented(seq, "sealed", Session.Game!.ActiveSeat, "Hatch sealed",
                                       "Nothing is left that fits behind it.", 0.8, Compartment: h.Compartment.Value),
        CardDrawn d => CardShown(seq, d),
        Rolled r => new Presented(seq, "roll", r.Seat, r.Reason == RollReason.Breach ? "Breach roll" : $"{Name(r.Trait)} check",
                                  $"{r.Dice.Faces.Count} {(r.Dice.Faces.Count == 1 ? "die" : "dice")}: {r.Dice.Total}", 1.5, Faces: r.Dice.Faces),
        BreachFired b => new Presented(seq, "breach", b.Breach.Seat, "The Breach", "Something aboard has woken.",
                                       Math.Max(6, ReadingSeconds(_pack.Rules, "The Breach", "Something aboard has woken.")),
                                       Compartment: b.Breach.Compartment?.Value),
        ScenarioChosen c => Announced(seq, c),
        RoundClosed r => RoundWarning(seq, r),
        Attacked a => new Presented(seq, "fight", Session.Game!.ActiveSeat, $"{Fighter(a.Attacker)} attacks {Fighter(a.Defender)}",
                                    $"{a.AttackRoll.Total} against {a.DefendRoll.Total}: {a.Result}.", 1.5),
        Died d => Read(seq, "death", d.Seat, $"{Session.SeatOf(d.Seat).Name} is dead", "They stay as an echo.", 2),
        ScenarioNotice n => Read(seq, "notice", Session.Game!.ActiveSeat, "", n.Text, 1.5) with { Compartment = n.At?.Value },
        VoteResolved v => Read(seq, "vote", Session.Game!.ActiveSeat, "The vote",
                               v.Result is { } r ? $"{Session.SeatOf(new SeatId(r)).Name} is named." : "Nobody is named.", 2),
        GameOver g => Read(seq, "over", Session.Game!.ActiveSeat, "It's over", g.Outcome.Message, 3),
        _ => null,
    };

    private Presented CardShown(int seq, CardDrawn d)
    {
        var card = _pack.Card(d.Card);
        return new Presented(seq, "card", d.Seat, card.Title, card.Text, ReadingSeconds(_pack.Rules, card.Title, card.Text),
                             Deck: card.Kind);
    }

    /// <summary>A presentation with text: it waits for whichever is longer, its own minimum or the time to read it aloud.</summary>
    private Presented Read(int seq, string kind, SeatId seat, string title, string text, double minimum)
        => new(seq, kind, seat, title, text, Math.Max(minimum, ReadingSeconds(_pack.Rules, title, text)));

    /// <summary>The scenario begins: Tiller names it and reads its public announcement.</summary>
    private Presented Announced(int seq, ScenarioChosen c)
    {
        var def = _pack.ScenarioDef(c.Scenario);
        return Read(seq, "scenario", Session.Game!.ActiveSeat, def.Title, def.Announcement, 3);
    }

    /// <summary>
    /// Tiller's round warnings (spec §5.5): when the endgame's next round leaves three, and when it is the last. None once
    /// the game is over, or before the Breach.
    /// </summary>
    private Presented? RoundWarning(int seq, RoundClosed closed)
    {
        if (Session.Game is not { Board: { } board, Phase: Phase.Endgame } game)
        {
            return null;
        }
        var left = board.RoundLimit - (closed.Round + 1 - board.StartRound);
        var text = left switch
        {
            3 => "Three rounds remain.",
            1 => "Final round.",
            _ => null,
        };
        return text is null ? null : Read(seq, "round", game.ActiveSeat, $"Round {closed.Round + 1}", text, 2);
    }

    /// <summary>
    /// How long Tiller takes to read a presentation aloud (M4 plan, Task 2): the lead, plus the title and text at the
    /// rules' speaking rate. A presentation's wait is the fallback for a missing TV, so it must outlast a present one.
    /// </summary>
    internal static double ReadingSeconds(MeridianRules rules, string title, string text)
    {
        var words = $"{title} {text}".Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return rules.ReadingLead.TotalSeconds + words / rules.ReadingWordsPerSecond;
    }

    private static string Name(Trait? trait) => trait is { } t ? t.ToString() : "";

    /// <summary>
    /// Who fought, for the TV: a seat by its player's name, a monster by its kind (brood-2 is "Brood"). A fight happens in
    /// plain view of the table, so naming its sides gives nothing away.
    /// </summary>
    private string Fighter(string id)
    {
        if (Session.Seats.FirstOrDefault(s => s.Seat.Value == id) is { } seat)
        {
            return seat.Name;
        }
        var kind = Session.Game?.Board?.Tokens.FirstOrDefault(t => t.Id == id)?.Kind ?? id;
        return kind.Length == 0 ? id : char.ToUpperInvariant(kind[0]) + kind[1..];
    }
}
