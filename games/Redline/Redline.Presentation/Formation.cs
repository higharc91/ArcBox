using Redline.Logic;

namespace Redline.Presentation;

/// <summary>
/// Where a car sits: <paramref name="RaceMeters"/> is metres from the start line along the race, not wrapped to one lap
/// (negative on the grid behind the line), so a tween between two slots never runs the wrong way round the lap.
/// <paramref name="Lateral"/> is metres left of the centre line.
/// </summary>
public readonly record struct CarSlot(double RaceMeters, double Lateral);

/// <summary>
/// §4.8 placement: every car's slot from a <see cref="RedlineHostView"/>. Cars sharing a distance form a group ordered by
/// position (ties keep the view's ranking order); a group at distance 0 lines up in the F1 grid stagger behind the start
/// line (Driver Select, Grid, round 1, or a car that never got away), every other group runs in <see cref="LapMath.Pack"/>
/// formation behind its anchor at <c>distance / finishLine × lapLength</c>.
/// </summary>
public static class Formation
{
    /// <summary>The pole car's origin sits this far behind the start line.</summary>
    public const double PoleSetback = 4;

    /// <summary>Slots for every driver code, from the current distances.</summary>
    public static IReadOnlyDictionary<string, CarSlot> Current(RedlineHostView view, double lapLength) =>
        Slots(view, lapLength, d => d.Distance, d => d.Position);

    /// <summary>
    /// Slots at the start of the round: <see cref="DriverRow.PreviousDistance"/>, ordered by the position the driver held
    /// then (<c>Position + PositionChange</c>). This is where the car stood during Throttle and Question.
    /// </summary>
    public static IReadOnlyDictionary<string, CarSlot> RoundStart(RedlineHostView view, double lapLength) =>
        Slots(view, lapLength, d => d.PreviousDistance, d => d.Position + d.PositionChange);

    private static Dictionary<string, CarSlot> Slots(
        RedlineHostView view, double lapLength, Func<DriverRow, int> distance, Func<DriverRow, int> position)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lapLength, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(view.FinishLine, 0);

        var result = new Dictionary<string, CarSlot>(view.Drivers.Count, StringComparer.Ordinal);
        var groups = view.Drivers
            .Select((d, index) => (d, index))
            .GroupBy(x => Math.Max(0, distance(x.d)));
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(x => position(x.d)).ThenBy(x => x.index).Select(x => x.d).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                if (group.Key == 0)
                {
                    var g = LapMath.Grid(i);
                    result[ordered[i].Code] = new CarSlot(-PoleSetback - g.Back, g.Lateral);
                }
                else
                {
                    var p = LapMath.Pack(i, ordered.Count);
                    result[ordered[i].Code] = new CarSlot((double)group.Key / view.FinishLine * lapLength - p.Back, p.Lateral);
                }
            }
        }
        return result;
    }
}
