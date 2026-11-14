namespace Meridian.Logic;

// Trait checks (spec §2.2; plan Task 6).
internal sealed partial class Resolver
{
    /// <summary>
    /// Opens a check on <paramref name="card"/> (or its <paramref name="option"/>): dice equal to the trait's value plus
    /// carried items' extra dice. The card stays out of every pile until the check resolves.
    /// </summary>
    internal void StartCheck(SeatId seat, CardId card, string? option = null)
    {
        var check = CheckOf(card, option);
        var crew = State.CrewOf(seat);
        var dice = Rules.Value(Pack, crew, check.Trait) + ExtraDiceFor(Pack, crew, check.Trait);
        State = State with { Waiting = new RollPending(seat, RollReason.Check, check.Trait, dice, card, option) };
    }

    private CheckBody CheckOf(CardId card, string? option)
    {
        var def = Pack.Card(card);
        return option is null ? def.Check! : def.Choice!.Options.First(o => o.Id == option).Check!;
    }

    private void RollDice()
    {
        var roll = (RollPending)State.Waiting!;
        var (dice, rng) = SignalDice.Roll(State.Rng, roll.Dice, R);
        State = State with { Rng = rng, Waiting = null };
        State = State.CountFor(roll.From, s => s with { Dice = s.Dice + dice.Faces.Count });
        Events.Add(new Rolled(roll.From, roll.Reason, roll.Trait, dice));

        if (roll.Reason == RollReason.Breach)
        {
            BreachRolled(roll, dice.Total);
            return;
        }

        var card = roll.Card!.Value;
        var check = CheckOf(card, roll.Option);
        var index = check.Bands.IndexOf(check.BandFor(dice.Total));
        var band = check.Bands[index];
        Events.Add(new BandResolved(roll.From, card, index, band.Text));
        DiscardIncident(card);
        ApplyEffects(roll.From, band.Effects);
    }

    /// <summary>Settles the Breach roll (plan Task 8).</summary>
    partial void BreachRolled(RollPending roll, int total);

    /// <summary>
    /// An Incident goes to its discard as soon as it resolves, before its effects: an effect may draw again, and the
    /// discard must already hold it if that draw has to reshuffle.
    /// </summary>
    private void DiscardIncident(CardId card)
        => State = State with { Incidents = State.Incidents with { Discard = State.Incidents.Discard.Add(card) } };
}
