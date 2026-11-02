using System.Globalization;
using System.Numerics;
using System.Text;
using Tactics.Game;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// Dev only: <c>--walk-log</c>'s <c>WALKAT</c> lines, where the TV is showing the party, so a run can say from the log alone that the
/// party really moved, where it stopped, and how far the followers trail. One line looks like
/// <code>WALKAT 1793500000123 42 1 31.50 20.50 0.90 Ana 31.50 20.50 Bo 30.60 20.50 Cy 29.70 20.50</code>
/// That's the wall clock in Unix milliseconds, the newest step the TV has, held (1) or not (0), the leader's place on the map, the widest
/// gap between one walker and the one ahead of them, and then each walker, leader first, by name, with their place. A place is in tiles,
/// and a tile's middle is .50.
/// <para>
/// <see cref="Next"/> gives a line at most every <see cref="Interval"/> while anything moves, one more when it all comes to rest, and
/// nothing while it stays there, so a party standing still for an hour costs one line.
/// </para>
/// </summary>
public sealed class WalkLog
{
    /// <summary>The least time between two lines, in seconds.</summary>
    public const double Interval = 0.25;

    /// <summary>A walker who has moved less than this, in tiles, since the last line hasn't moved.</summary>
    private const float Still = 0.01f;

    private readonly Dictionary<string, Vector2> _said = new(StringComparer.Ordinal);
    private double _saidAt = double.NegativeInfinity;
    private int _saidSeq = -1;
    private bool _saidHeld;

    /// <summary>
    /// The line for this frame, or null. <paramref name="now"/> is any clock in seconds, for the spacing; <paramref name="unixMs"/> is what
    /// the line says. Walkers with no place yet are left out; with none at all there's no line.
    /// </summary>
    public string? Next(double now, long unixMs, int seq, bool held, IReadOnlyList<WalkerView> party, IReadOnlyDictionary<string, UnitPlace> places)
    {
        var walkers = party.OrderBy(w => w.Leads ? 0 : 1).Where(w => places.ContainsKey(w.Id)).ToArray();     // a stable sort: member order after the leader
        if (walkers.Length == 0)
            return null;

        var changed = seq != _saidSeq || held != _saidHeld || walkers.Length != _said.Count
            || walkers.Any(w => !_said.TryGetValue(w.Id, out var was) || Vector2.Distance(was, places[w.Id].Map) >= Still);
        if (!changed || now - _saidAt < Interval)
            return null;

        (_saidAt, _saidSeq, _saidHeld) = (now, seq, held);
        _said.Clear();
        var widest = 0f;
        for (var i = 0; i < walkers.Length; i++)
        {
            _said[walkers[i].Id] = places[walkers[i].Id].Map;
            if (i > 0)
                widest = Math.Max(widest, Vector2.Distance(places[walkers[i - 1].Id].Map, places[walkers[i].Id].Map));
        }

        var lead = places[walkers[0].Id].Map;
        var line = new StringBuilder(FormattableString.Invariant($"WALKAT {unixMs} {seq} {(held ? 1 : 0)} {lead.X:0.00} {lead.Y:0.00} {widest:0.00}"));
        foreach (var walker in walkers)
        {
            var at = places[walker.Id].Map;
            line.Append(CultureInfo.InvariantCulture, $" {Word(walker.Name)} {at.X:0.00} {at.Y:0.00}");
        }
        return line.ToString();
    }

    /// <summary>A name as one word, so the line splits on spaces.</summary>
    private static string Word(string name) => name.Length == 0 ? "_" : string.Concat(name.Select(c => char.IsWhiteSpace(c) ? '_' : c));
}
