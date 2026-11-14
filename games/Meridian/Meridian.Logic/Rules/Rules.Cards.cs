namespace Meridian.Logic;

// Cards, items and compartment rules (spec §2.5; plan Task 7).
internal sealed partial class Resolver
{
    partial void DrawFor(CardKind kind) => Draw(kind);

    /// <summary>
    /// Draws the top card of a kind for the active seat. An empty Incident or Salvage pile reshuffles its discard; with
    /// nothing to reshuffle, nothing is drawn and the turn goes on (Review focus 4). Anomalies never reshuffle: they are
    /// held, not discarded, and the ninth fires the Breach anyway.
    /// </summary>
    internal void Draw(CardKind kind)
    {
        var seat = State.ActiveSeat;
        var pile = State.Pile(kind);
        if (pile.Draw.IsEmpty)
        {
            if (kind == CardKind.Anomaly || pile.Discard.IsEmpty)
            {
                Events.Add(new DeckEmpty(kind));
                return;
            }
            (var shuffled, var rng) = State.Rng.Shuffle(pile.Discard);
            pile = new CardPile(shuffled);
            State = State with { Rng = rng };
            Events.Add(new DeckReshuffled(kind));
        }

        var card = pile.Draw[0];
        State = State.WithPile(kind, pile with { Draw = pile.Draw.RemoveAt(0) });
        State = State.CountFor(seat, s => s with { Cards = s.Cards + 1 });
        Events.Add(new CardDrawn(seat, card, kind));

        var def = Pack.Card(card);
        if (kind == CardKind.Incident)
        {
            if (def.Check is not null)
            {
                StartCheck(seat, card);
            }
            else
            {
                State = State with { Waiting = new ChoicePending(seat, card) };
            }
            return;
        }

        var crew = State.CrewOf(seat);
        State = State.WithCrew(crew with { Items = crew.Items.Add(card) });
        Events.Add(new ItemGained(seat, card));
        if (kind == CardKind.Anomaly)
        {
            State = State with { AnomaliesDrawn = State.AnomaliesDrawn + 1 };
            AnomalyDrawn(seat, card);
        }
    }

    /// <summary>Starts the Breach roll, or fires the Breach on the forced Anomaly (plan Task 8).</summary>
    partial void AnomalyDrawn(SeatId seat, CardId card);

    /// <summary>
    /// A choice: the option's own effects apply first. An option with a check then rolls it, and the card waits for the
    /// roll; otherwise the card is discarded and its effects apply.
    /// </summary>
    private void ChooseOption(string optionId)
    {
        var choice = (ChoicePending)State.Waiting!;
        State = State with { Waiting = null };
        var option = Pack.Card(choice.Card).Choice!.Options.First(o => o.Id == optionId);
        Events.Add(new ChoiceMade(choice.From, choice.Card, option.Id, option.Text));
        if (option.Check is not null)
        {
            ApplyEffects(choice.From, option.Effects);
            StartCheck(choice.From, choice.Card, option.Id);
            return;
        }
        DiscardIncident(choice.Card);
        ApplyEffects(choice.From, option.Effects);
    }

    private static void LegalItems(RulesPack pack, GameState state, SeatId seat, List<Command> legal)
    {
        if (state.Turn.UsedItem)
        {
            return;
        }
        foreach (var item in state.CrewOf(seat).Items.Distinct())
        {
            if (pack.Card(item).Item?.Use is not null)
            {
                legal.Add(new UseItem(seat, item));
            }
        }
    }

    private void Use(CardId item)
    {
        var seat = State.ActiveSeat;
        var def = Pack.Card(item);
        var use = def.Item!.Use!;
        var crew = State.CrewOf(seat);
        State = State with { Turn = State.Turn with { UsedItem = true } };
        if (use.Single)
        {
            State = State.WithCrew(crew with { Items = crew.Items.Remove(item) });
            var pile = State.Pile(def.Kind);
            State = State.WithPile(def.Kind, pile with { Discard = pile.Discard.Add(item) });
        }
        Events.Add(new ItemUsed(seat, item, use.Single));
        ApplyEffects(seat, use.Effects);
    }

    /// <summary>A compartment rule the active seat can use: a heal they haven't spent here, with something to heal.</summary>
    private static void LegalRoom(RulesPack pack, GameState state, SeatId seat, List<Command> legal)
    {
        var crew = state.CrewOf(seat);
        if (!state.Turn.UsedCompartment
            && pack.Tile(state.Ship.Get(crew.At).Tile).Rule is HealRule heal
            && (!heal.OncePerCrew || !crew.UsedRooms.Contains(crew.At))
            && heal.Traits.Members().Any(t => crew.Steps[t] < pack.CrewMember(crew.Crew).Tracks[t].Start))
        {
            legal.Add(new UseCompartment(seat));
        }
    }

    /// <summary>Heals one step at a time, each to the lowest trait in the group that is below where it started.</summary>
    private void UseRoom()
    {
        var seat = State.ActiveSeat;
        var crew = State.CrewOf(seat);
        var heal = (HealRule)Pack.Tile(State.Ship.Get(crew.At).Tile).Rule!;
        var tracks = Pack.CrewMember(crew.Crew).Tracks;
        var before = crew.Steps;
        var now = before;
        for (var i = 0; i < heal.Steps; i++)
        {
            var below = heal.Traits.Members().Where(t => now[t] < tracks[t].Start).ToList();
            if (below.Count == 0)
            {
                break;
            }
            var pick = below.MinBy(t => now[t]);
            now = now.With(pick, now[pick] + 1);
        }

        var used = heal.OncePerCrew ? crew.UsedRooms.Add(crew.At) : crew.UsedRooms;
        State = State.WithCrew(crew with { Steps = now, UsedRooms = used }) with
        {
            Turn = State.Turn with { UsedCompartment = true },
        };
        Events.Add(new CompartmentUsed(seat, crew.At));
        foreach (var trait in Traits.All.Where(t => before[t] != now[t]))
        {
            Events.Add(new TraitChanged(seat, trait, before[trait], now[trait]));
        }
    }
}
