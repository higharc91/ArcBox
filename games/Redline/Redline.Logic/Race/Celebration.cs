using ArcBox.Sdk;

namespace Redline.Logic;

/// <summary>Podium spec §2: who stands on the podium, and how shaking and firing turn into charge and sprays.</summary>
public static class Celebration
{
    /// <summary>Podium places.</summary>
    public const int Steps = 3;

    /// <summary>The host view shows the charge in steps of this size, so a flood of shake inputs changes it at most ~20 times.</summary>
    public const double ChargeStep = 0.05;

    /// <summary>The weakest spray: a cork popped with no pressure still fizzes.</summary>
    public const double MinSprayStrength = 0.15;

    /// <summary>
    /// The players in race order, first <see cref="Steps"/>, however many AI cars finished ahead of them. The ranking is
    /// stable, so players sharing a position keep the classification order.
    /// </summary>
    public static IReadOnlyList<Driver> PodiumDrivers(IEnumerable<(Driver Driver, int Position)> ranked) =>
        [.. ranked.Select(r => r.Driver).Where(d => d.IsHuman).Take(Steps)];

    /// <summary>Clamped to 0..1 and rounded to the nearest <see cref="ChargeStep"/>.</summary>
    public static double Quantise(double charge) => Math.Round(Math.Round(Math.Clamp(charge, 0, 1) / ChargeStep) * ChargeStep, 2);

    /// <summary>A spray's strength for the stored <paramref name="charge"/>: at least <see cref="MinSprayStrength"/>.</summary>
    public static double Strength(double charge) => Math.Round(Math.Max(Math.Clamp(charge, 0, 1), MinSprayStrength), 2);
}

/// <summary>One podium place during the ceremony: the player's stored charge and their sprays.</summary>
internal sealed class PodiumPlace(int step, Driver driver)
{
    public int Step { get; } = step;
    public Driver Driver { get; } = driver;
    public PlayerId Player => Driver.PlayerId!.Value;

    /// <summary>The latest charge the phone sent, clamped to 0..1; reset to 0 by a spray.</summary>
    public double Charge { get; set; }

    /// <summary>The charge when Champagne began: the Champagne phone view starts from it.</summary>
    public double ChargeAtChampagne { get; set; }

    public List<Spray> Sprays { get; } = [];
    public DateTimeOffset? LastSprayAt { get; set; }

    public PodiumStep ToView() => new(
        Step, Player, Driver.PlayerName ?? "", Driver.Code, Driver.Seat.Team.Id, Driver.Position,
        Celebration.Quantise(Charge), [.. Sprays]);
}
