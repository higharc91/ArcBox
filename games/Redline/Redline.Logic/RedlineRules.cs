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

    /// <summary>
    /// Podium spec §2: after the Chequered flag, the Anthem and Champagne phases with the top three players on the podium.
    /// Off, or with no players in the room: the game ends when the Chequered timer runs out.
    /// </summary>
    public bool Celebrate { get; init; } = true;
    public TimeSpan Anthem { get; init; } = TimeSpan.FromSeconds(16);
    public TimeSpan Champagne { get; init; } = TimeSpan.FromSeconds(16);

    /// <summary>The shortest time between two sprays from one podium player; a fire sooner than this is ignored.</summary>
    public TimeSpan SprayCooldown { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Audio spec §2: a Reading phase between Throttle and Question, while the TV reads the question out, before the
    /// answer clock starts. Off: Throttle goes straight to Question.
    /// </summary>
    public bool ReadQuestions { get; init; } = true;

    /// <summary>
    /// How long the Reading phase lasts: clamp(0.6 s + words × 0.36 s, 2.5 s, 10 s), about Chirp 3: HD's 2.7 words a
    /// second. An estimate from the text, never the real clip length, so the game does not depend on audio.
    /// </summary>
    public TimeSpan ReadingTime(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return TimeSpan.FromMilliseconds(Math.Clamp(600 + words * 360, 2500, 10_000));
    }

    public int CruiseGain { get; init; } = 1;
    public int PushGain { get; init; } = 3;
    public int PushLoss { get; init; } = 1;
    public int RedlineGain { get; init; } = 4;
    public int RedlineLoss { get; init; } = 2;

    public AiRules Ai { get; init; } = new();
}
