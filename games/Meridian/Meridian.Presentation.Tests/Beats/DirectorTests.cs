using Meridian.Presentation.Beats;

namespace Meridian.Presentation.Tests.Beats;

/// <summary>The director: one beat per presented event, signalled once when shown, never for one it skipped (M4 plan, Task 4).</summary>
public sealed class DirectorTests
{
    private static readonly MeridianRules Rules = new();
    private readonly List<int> _signals = [];
    private readonly Director _director;

    public DirectorTests() => _director = new Director(Rules, _signals.Add);

    private static Presented Now(int seq, string kind = "roll", string title = "Tech check", string text = "3 dice: 4", double seconds = 1.5)
        => new(seq, kind, new SeatId("p1"), title, text, seconds);

    private static MeridianHostView View(Presented? now, int round = 1)
        => new("explore", "Ann's turn", [new SeatCard("p1", "Ann", "Ines Okafor", 0, true, false, true)], [], [], 40, 0, round, now, []);

    /// <summary>Plays the clock forward in frames, as the scene does.</summary>
    private void Run(double seconds)
    {
        for (var t = 0.0; t < seconds; t += 1.0 / 60)
        {
            _director.Tick(1.0 / 60);
        }
    }

    [Fact]
    public void A_beat_is_signalled_once_when_its_timeline_ends()
    {
        _director.Receive(View(null));   // the TV was there before anything happened
        _director.Receive(View(Now(1)));
        var beat = _director.Current!;
        Assert.Equal("roll", beat.Kind);
        Run(beat.Seconds - 0.05);
        Assert.Empty(_signals);
        Run(0.1);
        Assert.Equal([1], _signals);
        Run(5);
        _director.Receive(View(Now(1)));   // the same view again: nothing new
        Run(5);
        Assert.Equal([1], _signals);
    }

    [Fact]
    public void A_text_beat_waits_for_the_voice_before_it_signals()
    {
        _director.Receive(View(null));
        var card = Now(2, "card", "Hull Groan", "The hull shifts under you. Everyone in a compartment with a breach makes a Brawn check.", 11);
        _director.Receive(View(card));
        Assert.True(_director.Current!.Speaks);
        Run(_director.Current.Seconds + 1);
        Assert.Empty(_signals);              // the timeline has ended, but Tiller is still talking
        _director.SpeechFinished(2);
        Run(2);
        Assert.Equal([2], _signals);
    }

    [Fact]
    public void A_silent_voice_doesnt_hold_the_game_past_its_estimate()
    {
        _director.Receive(View(null));
        _director.Receive(View(Now(3, "card", "Static", "Nothing but static.", 4)));
        Run(_director.Current!.SpeakSeconds + 2.5 + _director.Current.Seconds);
        Assert.Equal([3], _signals);
    }

    [Fact]
    public void A_newer_event_mid_beat_cuts_it_short_without_signalling_it()
    {
        // Review focus 1: the TV was slower than the fallback, so the game moved on.
        _director.Receive(View(null));
        _director.Receive(View(Now(4)));
        Run(0.3);
        _director.Receive(View(Now(5, "reveal", "Med Bay", "", 1.2)));
        Assert.Equal(5, _director.Current!.Seq);
        Run(3);
        Assert.Equal([5], _signals);
    }

    [Fact]
    public void An_empty_queue_mid_beat_clears_it_without_signalling()
    {
        _director.Receive(View(null));
        _director.Receive(View(Now(6)));
        Run(0.3);
        _director.Receive(View(null, round: 2));
        Assert.Null(_director.Current);
        Run(3);
        Assert.Empty(_signals);
    }

    [Fact]
    public void After_a_reconnect_the_current_event_is_shown_briefly_and_nothing_is_replayed()
    {
        // Review focus 2: the first view after a dropped connection is a fresh look at the room, not news.
        _director.Receive(View(null));
        _director.Receive(View(Now(7)));
        Run(0.2);
        _director.ConnectionLost();
        _director.Receive(View(Now(9, "card", "Hull Groan", "A long card text that would take a while to read aloud.", 9)));
        var beat = _director.Current!;
        Assert.True(beat.Short);
        Assert.False(beat.Speaks);   // not read again: the room heard it, or missed it
        Run(0.6);
        Assert.Equal([9], _signals);
    }

    [Fact]
    public void A_tv_joining_a_running_game_doesnt_replay_what_it_finds()
    {
        _director.Receive(View(Now(12, "card", "Hull Groan", "Some text.", 6)));
        Assert.True(_director.Current!.Short);
        Run(0.6);
        Assert.Equal([12], _signals);
    }

    [Fact]
    public void A_stale_event_is_never_shown_again()
    {
        _director.Receive(View(null));
        _director.Receive(View(Now(20)));
        Run(3);
        _director.Receive(View(Now(19)));
        Assert.Null(_director.Current);
        Run(3);
        Assert.Equal([20], _signals);
    }

    [Fact]
    public void The_deck_changes_only_between_beats()
    {
        var rooms = new EqArray<CompartmentCard>([
            new("c1", "airlock", null, new Cell(DeckId.Habitat, 0, 2), 0, [Side.N], null, null),
            new("c9", "engine_room", null, new Cell(DeckId.Engineering, 0, 0), 0, [Side.N], null, null),
        ]);
        MeridianHostView At(string room, Presented? now)
            => View(now) with { Compartments = rooms, Crew = [new CrewMarker("p1", "Ann", room, 0)] };
        _director.Receive(At("c1", null));
        Assert.Equal(DeckId.Habitat, _director.Deck);
        _director.Receive(At("c1", Now(30, "reveal", "Engine Room", "", 1.2) with { Compartment = "c9" }));
        Assert.Equal(DeckId.Engineering, _director.Deck);   // the beat's own deck, from its first frame
        _director.Receive(At("c1", Now(30, "reveal", "Engine Room", "", 1.2) with { Compartment = "c9" }) with { Round = 2 });
        Assert.Equal(DeckId.Engineering, _director.Deck);   // still mid-beat
        Run(3);
        _director.Receive(At("c1", null));
        Assert.Equal(DeckId.Habitat, _director.Deck);
    }
}
