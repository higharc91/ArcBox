using System.Text.Json.Serialization;
using ArcBox.Sdk;
using Meridian.Logic;

namespace Meridian.Game;

[JsonConverter(typeof(JsonStringEnumConverter<SessionPhase>))]
public enum SessionPhase
{
    [JsonStringEnumMemberName("boarding")] Boarding,
    [JsonStringEnumMemberName("explore")] Explore,
    /// <summary>The Breach has been shown; phones read their briefings (spec §3.1).</summary>
    [JsonStringEnumMemberName("briefing")] Briefing,
    [JsonStringEnumMemberName("endgame")] Endgame,
    /// <summary>A side has won; every role is revealed until the VIP continues.</summary>
    [JsonStringEnumMemberName("debrief")] Debrief,
    [JsonStringEnumMemberName("over")] Over,
}

/// <summary>A player's seat. Its <see cref="Seat"/> id in the rules is the player id's string.</summary>
/// <param name="Auto">A bot is playing this seat until the player reconnects (spec §3.3).</param>
/// <param name="PauseUsed">This disconnection's one endgame pause has run out; it resets when the player reconnects.</param>
public sealed record SeatInfo(PlayerId Player, string Name, CrewId? Crew = null, bool Connected = true, bool Auto = false,
                              bool Vip = false, bool PauseUsed = false)
{
    [JsonIgnore]
    public SeatId Seat => new(Player.Value);
}

[JsonConverter(typeof(JsonStringEnumConverter<ClockPurpose>))]
public enum ClockPurpose
{
    /// <summary>Nothing is timed: the game is over.</summary>
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("boarding")] Boarding,
    /// <summary>A connected player's decision: a wait's deadline, or the turn's inactivity.</summary>
    [JsonStringEnumMemberName("deadline")] Deadline,
    /// <summary>A disconnected player owes a decision; when this runs out a bot takes the seat.</summary>
    [JsonStringEnumMemberName("grace")] Grace,
    /// <summary>A bot's next command.</summary>
    [JsonStringEnumMemberName("bot")] Bot,
    /// <summary>The TV is presenting the front of the queue; this is its fallback.</summary>
    [JsonStringEnumMemberName("present")] Present,
    [JsonStringEnumMemberName("briefing")] Briefing,
    /// <summary>The endgame's pause for a seat that dropped while owing a decision (spec §3.3).</summary>
    [JsonStringEnumMemberName("pause")] Pause,
    [JsonStringEnumMemberName("debrief")] Debrief,
}

/// <summary>
/// The game's one clock. At any moment the game waits on exactly one thing, so one timer, keyed <c>clock:{Seq}</c>,
/// covers them all. Every change bumps <see cref="Seq"/>, so a timer that fired just before a change is recognised as
/// stale and ignored.
/// </summary>
public sealed record Clock(int Seq, ClockPurpose Purpose);

/// <summary>
/// Everything the adapter knows besides the rules state: the seats, the clock, the presentation queue and the log. It
/// is one serializable record next to <see cref="GameState"/>, so stage M6 can snapshot the two and resume (spec §5.6).
/// </summary>
/// <param name="GameSeed">Seeds <see cref="Rules.NewGame"/> when Boarding ends.</param>
/// <param name="Game">Null during Boarding.</param>
/// <param name="BotRng">The bots' own generator, never the game's.</param>
/// <param name="Ending">The results message, once the game is over.</param>
/// <param name="Ready">Seats that have read their briefing.</param>
public sealed record MeridianSession(
    SessionPhase Phase,
    EqArray<SeatInfo> Seats,
    ulong GameSeed,
    GameState? Game,
    Rng BotRng,
    Clock Clock,
    EqArray<Presented> Queue,
    int NextPresent,
    EqArray<string> Log,
    string? Ending = null,
    EqArray<SeatId> Ready = default)
{
    public int IndexOf(PlayerId player)
    {
        for (var i = 0; i < Seats.Count; i++)
        {
            if (Seats[i].Player == player)
            {
                return i;
            }
        }
        return -1;
    }

    public SeatInfo? Find(PlayerId player) => IndexOf(player) is var i and >= 0 ? Seats[i] : null;

    public SeatInfo SeatOf(SeatId seat) => Seats.First(s => s.Seat == seat);

    public MeridianSession With(SeatInfo seat) => this with { Seats = Seats.SetItem(IndexOf(seat.Player), seat) };
}

/// <summary>
/// An event the TV shows on its own before play moves on (spec §3.2): a reveal, a card, a roll, a sealed hatch or the
/// Breach. <see cref="Seconds"/> is the fallback, used when the TV never says it has finished.
/// </summary>
public sealed record Presented(int Seq, string Kind, SeatId Seat, string Title, string Text, double Seconds,
                               EqArray<int> Faces = default, string? Compartment = null);
