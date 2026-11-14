namespace Meridian.Logic;

/// <summary>
/// Every rule number (spec §2). Nothing else may hard-code them: the phone's hints, the TV's rules strip and the bots
/// all read this record. The values are starting points for the simulator and then playtests to tune.
/// </summary>
public sealed record MeridianRules
{
    /// <summary>Signal dice faces (spec §2.2). Mean 5/6.</summary>
    public EqArray<int> DieFaces { get; init; } = [0, 0, 1, 1, 1, 2];

    /// <summary>Steps on every trait track (spec §2.1).</summary>
    public int TrackSteps { get; init; } = 8;

    /// <summary>
    /// A Breach roll at or above this fires the Breach (spec §2.6). 6 from the M1 simulation
    /// (docs/superpowers/reports/2026-11-meridian-breach-timing.md); retune after the first playtest.
    /// </summary>
    public int BreachThreshold { get; init; } = 6;

    /// <summary>Drawing this Anomaly fires the Breach without a roll (spec §2.6).</summary>
    public int ForcedBreachAnomaly { get; init; } = 9;

    public int MinPlayers { get; init; } = 3;
    public int MaxPlayers { get; init; } = 6;

    /// <summary>Cells reach from -DeckRadius to +DeckRadius on each axis of a deck; a hatch facing past it is a wall.</summary>
    public int DeckRadius { get; init; } = 6;

    /// <summary>How long a player has to choose a new compartment's rotation (the adapter's clock).</summary>
    public TimeSpan RotationChoice { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Inactivity that ends a turn; restarts whenever the player acts (the adapter's clock).</summary>
    public TimeSpan TurnInactivity { get; init; } = TimeSpan.FromSeconds(60);

    // The adapter's other deadlines (M2 plan, Global constraints).
    public TimeSpan RollWait { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan ChoiceWait { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan TradeWait { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan BoardingTime { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>How long a disconnected seat that owes a decision is waited for before a bot plays it.</summary>
    public TimeSpan DisconnectGrace { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>The pause between a bot's commands, so its turn is watchable.</summary>
    public TimeSpan BotStep { get; init; } = TimeSpan.FromSeconds(0.8);

    // The endgame's clocks (M3 plan, Task 11).
    /// <summary>How long phones have to read their briefings before the endgame begins anyway (spec §3.1).</summary>
    public TimeSpan BriefingTime { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>A living seat that drops in the endgame is waited for this long, once per disconnection (spec §3.3).</summary>
    public TimeSpan EndgamePause { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan VoteWait { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan EchoWait { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan AllocateWait { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan DebriefTime { get; init; } = TimeSpan.FromSeconds(20);
}
