using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// Battle barks for everyone who has them (lore spec §7): player characters in their voice (<see cref="PlayerVoices"/>), named
/// units by sheet tag (<c>"mother_skarrow"</c>; Vorga is placed as the <c>"warlord"</c>), and the narrator. Lines are from the
/// speaker's point of view: <c>hit</c> is a blow they land, <c>low_hp</c> is their own wound. Cosmetic like
/// <see cref="GriskBarks"/>, which keeps Grisk's own lines: the moment comes from the beat's events and the line and speaker from
/// the beat counter, so a replay says the same thing, and the battle never rolls or changes for it.
/// </summary>
internal static class BattleBarks
{
    /// <summary>Beats after any spoken line in which nobody barks, so lines don't crowd each other; victory and defeat still speak.</summary>
    public const int QuietBeats = 2;

    private static readonly ImmutableDictionary<string, string> Aliases =
        ImmutableDictionary<string, string>.Empty.Add("warlord", "vorga");

    private sealed record Candidate(string Speaker, string Moment, string? Voice);

    /// <param name="beat">The beat's number, which picks the speaker and the line.</param>
    /// <param name="actor">Who acted this beat, credited with a kill.</param>
    /// <param name="voiceOf">A player character's voice, or null for a unit no player controls.</param>
    /// <param name="beatsSinceSaid">Beats since the last one with a spoken line.</param>
    public static ImmutableArray<BattleEvent> Enrich(BattleState state, ImmutableArray<BattleEvent> events, BarkBook book, int beat,
        UnitId? actor, Func<UnitId, string?> voiceOf, int beatsSinceSaid)
    {
        if (events.Any(e => e is Said)) return events;      // never over an encounter's say line, or Grisk
        var ended = events.OfType<BattleEnded>().FirstOrDefault();
        if (ended is null && beatsSinceSaid < QuietBeats) return events;
        foreach (var group in Moments(state, events, actor, ended, book, voiceOf))
        {
            var list = group.ToList();
            for (var k = 0; k < list.Count; k++)
            {
                var c = list[(beat + k) % list.Count];
                var set = c.Voice is { } voice && PlayerVoices.TryParse(voice, out var name, out _)
                    ? book.ForVoice(name, c.Moment) : book.ForBattle(c.Speaker, c.Moment);
                if (set?.PickWeighted(state.Flags, beat) is { } line)
                    return events.Add(new Said(c.Speaker, line.Text, c.Voice));
            }
        }
        return events;
    }

    /// <summary>The beat's moments, most important first; each is everyone who could speak it.</summary>
    private static IEnumerable<IEnumerable<Candidate>> Moments(BattleState state, ImmutableArray<BattleEvent> events, UnitId? actor,
        BattleEnded? ended, BarkBook book, Func<UnitId, string?> voiceOf)
    {
        var units = state.Units.Values.OrderBy(u => u.Id.Value, StringComparer.Ordinal).ToList();
        Candidate? Of(UnitState unit, string moment)
        {
            if (voiceOf(unit.Id) is { } voice)
                return new Candidate(unit.Id.Value, moment, voice);
            var named = unit.Sheet.Tags.Select(t => Aliases.GetValueOrDefault(t, t))
                .FirstOrDefault(t => t != "grisk" && book.ForBattle(t, moment) is not null);
            return named is null ? null : new Candidate(named, moment, null);
        }
        IEnumerable<Candidate> Speakers(IEnumerable<UnitState> who, string moment) => who.Select(u => Of(u, moment)).OfType<Candidate>();
        IEnumerable<UnitState> Up(Func<UnitState, bool> where) => units.Where(u => u.Life == LifeState.Up && where(u));
        var narrator = (string moment) => new Candidate(TacticsVoices.NarratorSpeaker, moment, null);
        UnitState? Unit(UnitId id) => state.Units.GetValueOrDefault(id);
        var party = units.FirstOrDefault(u => voiceOf(u.Id) is not null)?.Sheet.Side;

        if (ended is not null)
        {
            if (ended.Outcome == BattleOutcome.Won)
                yield return [.. Speakers(Up(u => u.Sheet.Side == party), "victory"), narrator("victory")];
            else if (ended.Outcome == BattleOutcome.Lost)
                yield return [.. Speakers(Up(u => u.Sheet.Side != party), "victory"), .. Speakers(units.Where(u => u.Sheet.Side == party), "defeat"),
                    narrator("defeat")];
            yield break;
        }
        foreach (var died in events.OfType<Died>())        // a named foe's last words
            if (Unit(died.Unit) is { } dead && voiceOf(dead.Id) is null && Of(dead, "defeat") is { } last)
                yield return [last];
        foreach (var fell in events.OfType<Downed>().Select(d => Unit(d.Unit)).OfType<UnitState>())
            yield return [.. Speakers(Up(u => u.Sheet.Side == fell.Sheet.Side && u.Id != fell.Id), "ally_down"), narrator("ally_down")];
        // The one who struck the blow speaks before the fallen's friends.
        if (events.OfType<Died>().Select(d => Unit(d.Unit)).OfType<UnitState>().FirstOrDefault() is { } kill
            && actor is { } by && Unit(by) is { } killer && killer.Sheet.Side != kill.Sheet.Side)
            yield return [.. Speakers(Up(u => u.Id == by), "kill"), narrator("kill")];
        foreach (var died in events.OfType<Died>().Select(d => Unit(d.Unit)).OfType<UnitState>())
            yield return Speakers(Up(u => u.Sheet.Side == died.Sheet.Side && u.Id != died.Id), "ally_down");
        foreach (var hurt in events.OfType<Damaged>())
            if (Unit(hurt.Unit) is { Life: LifeState.Up } u && hurt.HpAfter > 0 && hurt.HpAfter * 4 < u.Sheet.MaxHp
                && (hurt.HpAfter + hurt.Amount) * 4 >= u.Sheet.MaxHp)
                yield return Speakers([u], "low_hp");
        foreach (var crit in events.OfType<AttackResolved>().Where(a => a.Crit))
            yield return [.. Speakers(Up(u => u.Id == crit.Source), "crit"), narrator("crit")];
        foreach (var up in events.OfType<Revived>())
            yield return Speakers(Up(u => u.Id == up.Unit), "revived");
        foreach (var healed in events.OfType<Healed>().Where(h => h.Amount > 0))
            yield return Speakers(Up(u => u.Id == healed.Unit), "healed");
        if (events.OfType<RoundStarted>().Any(r => r.Round == 1))
            yield return [.. Speakers(Up(u => u.Sheet.Side != party), "battle_start"), .. Speakers(Up(u => u.Sheet.Side == party), "battle_start"),
                narrator("battle_start")];
        foreach (var attack in events.OfType<AttackResolved>().Where(a => !a.Crit))
            yield return Speakers(Up(u => u.Id == attack.Source), attack.Hit ? "hit" : "miss");
        foreach (var turn in events.OfType<TurnStarted>())
            yield return Speakers(Up(u => u.Id == turn.Unit), "turn_start");
        if (events.OfType<RoundStarted>().Any(r => r.Round > 1))
            yield return [narrator("turn_start")];
    }
}
