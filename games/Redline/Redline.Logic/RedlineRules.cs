namespace Redline.Logic;

public enum Throttle { Cruise, Push, Redline }

public sealed record AiRules
{
    public double Easy { get; init; } = 0.72;
    public double Medium { get; init; } = 0.52;
    public double Hard { get; init; } = 0.32;
    public double MinP { get; init; } = 0.08;
    public double MaxP { get; init; } = 0.92;
    public double Temperature { get; init; } = 0.35;
    public double LimiterCost { get; init; } = 0.6;
    public double RedlineFloor { get; init; } = 0.3;
    public int ChaseGap { get; init; } = 6;
    public int ChaseRoundsLeft { get; init; } = 5;
    public double ChaseBonus { get; init; } = 0.5;
}

public sealed record RedlineRules
{
    public int FinishLine { get; init; } = 28;
    public int MaxRounds { get; init; } = 15;
    public string CircuitId { get; init; } = "cota";

    public TimeSpan DriverSelect { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan Grid { get; init; } = TimeSpan.FromSeconds(6);
    public TimeSpan Throttle { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan Question { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan Reveal { get; init; } = TimeSpan.FromSeconds(7);
    public TimeSpan Chequered { get; init; } = TimeSpan.FromSeconds(10);

    public int CruiseGain { get; init; } = 1;
    public int PushGain { get; init; } = 3;
    public int PushLoss { get; init; } = 1;
    public int RedlineGain { get; init; } = 4;
    public int RedlineLoss { get; init; } = 2;

    public AiRules Ai { get; init; } = new();
}
