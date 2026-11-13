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

    /// <summary>A Breach roll at or above this fires the Breach (spec §2.6).</summary>
    public int BreachThreshold { get; init; } = 5;

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
}
