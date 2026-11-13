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
        if (Session.Queue.IsEmpty && Session.Game!.Phase == Phase.Breach)
        {
            End();
            return;
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
                                  $"{r.Dice.Faces.Count} dice: {r.Dice.Total}", 1.5, Faces: r.Dice.Faces),
        BreachFired b => new Presented(seq, "breach", b.Breach.Seat, "The Breach", "Something aboard has woken.", 4,
                                       Compartment: b.Breach.Compartment?.Value),
        _ => null,
    };

    private Presented CardShown(int seq, CardDrawn d)
    {
        var card = _pack.Card(d.Card);
        var words = card.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return new Presented(seq, "card", d.Seat, card.Title, card.Text, 1 + 0.06 * words);
    }

    private static string Name(Trait? trait) => trait is { } t ? t.ToString() : "";
}
