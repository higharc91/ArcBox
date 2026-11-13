using System.Text.RegularExpressions;

namespace Meridian.Logic;

/// <summary>Checks a pack's meaning once its JSON has parsed. Returns every error; an empty list means valid.</summary>
public static partial class Validation
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    public static IReadOnlyList<ContentError> Check(RulesPack pack)
    {
        var errors = new List<ContentError>();
        CheckTiles(pack, errors);
        CheckStart(pack, errors);
        CheckCrew(pack, errors);
        CheckCards(pack, errors);
        return errors;
    }

    private static void CheckTiles(RulesPack pack, List<ContentError> errors)
    {
        const string file = PackLoader.TilesFile;
        var seen = new HashSet<TileId>();
        foreach (var (tile, start) in pack.StartTiles.Select(t => (t, true)).Concat(pack.Tiles.Select(t => (t, false))))
        {
            var where = $"tile {tile.Id}";
            Id(tile.Id.Value, file, where, errors);
            if (!seen.Add(tile.Id))
            {
                errors.Add(new(file, where, "is defined twice"));
            }
            if (tile.Hatches.Count is < 1 or > 4 || tile.Hatches.Distinct().Count() != tile.Hatches.Count)
            {
                errors.Add(new(file, where, "needs 1-4 distinct hatches"));
            }
            if (tile.Decks.IsEmpty || tile.Decks.Distinct().Count() != tile.Decks.Count)
            {
                errors.Add(new(file, where, "needs at least one deck, each listed once"));
            }
            if (tile.ExitCost is < 0 or > 3)
            {
                errors.Add(new(file, where, "exitCost must be 0-3"));
            }
            if (start && tile.Symbol is not null)
            {
                errors.Add(new(file, where, "a start tile can't have a card symbol"));
            }
            if (!start && tile.Copies < 1)
            {
                errors.Add(new(file, where, "copies must be at least 1"));
            }
            if (tile.Rule is HealRule { Steps: < 1 })
            {
                errors.Add(new(file, where, "a heal rule needs steps of at least 1"));
            }
        }
    }

    private static void CheckStart(RulesPack pack, List<ContentError> errors)
    {
        const string file = PackLoader.TilesFile;
        var start = pack.Start;
        var startIds = pack.StartTiles.Select(t => t.Id).ToHashSet();
        var radius = pack.Rules.DeckRadius;

        if (start.Place.IsEmpty)
        {
            errors.Add(new(file, "start", "places no tiles"));
            return;
        }

        var cells = new HashSet<(DeckId, int, int)>();
        foreach (var (p, i) in start.Place.Select((p, i) => (p, i)))
        {
            var where = $"start.place[{i}]";
            if (!startIds.Contains(p.Tile))
            {
                errors.Add(new(file, where, $"tile {p.Tile} is not a start tile"));
            }
            if (Math.Abs(p.X) > radius || Math.Abs(p.Y) > radius)
            {
                errors.Add(new(file, where, $"is outside the deck (radius {radius})"));
            }
            if (p.Rotation is < 0 or > 3)
            {
                errors.Add(new(file, where, "rotation must be 0-3"));
            }
            if (!cells.Add((p.Deck, p.X, p.Y)))
            {
                errors.Add(new(file, where, "shares a cell with another start tile"));
            }
        }

        if (!start.Place.Any(p => p.Tile == start.Entry))
        {
            errors.Add(new(file, "start.entry", $"{start.Entry} is not placed"));
        }
        foreach (var deck in Enum.GetValues<DeckId>().Where(d => !start.Place.Any(p => p.Deck == d)))
        {
            errors.Add(new(file, "start", $"places nothing on the {deck} deck"));
        }

        var links = start.Links;
        foreach (var (l, i) in links.Select((l, i) => (l, i)))
        {
            if (l.From < 0 || l.From >= start.Place.Count || l.To < 0 || l.To >= start.Place.Count || l.From == l.To)
            {
                errors.Add(new(file, $"start.links[{i}]", "must join two different placements by index"));
            }
        }
        if (errors.Count > 0)
        {
            return;
        }

        // Every start tile must be reachable from the entry through matched hatches and links.
        var placed = start.Place.Select(p => (p, Hatches: pack.Tile(p.Tile).HatchesAt(p.Rotation))).ToArray();
        var entry = Array.FindIndex(placed, x => x.p.Tile == start.Entry);
        var reached = new HashSet<int> { entry };
        var queue = new Queue<int>([entry]);
        while (queue.TryDequeue(out var at))
        {
            var (p, hatches) = placed[at];
            var next = new List<int>();
            foreach (var side in hatches)
            {
                var (dx, dy) = side.Offset();
                var j = Array.FindIndex(placed, x => x.p.Deck == p.Deck && x.p.X == p.X + dx && x.p.Y == p.Y + dy);
                if (j >= 0 && placed[j].Hatches.Contains(side.Opposite()))
                {
                    next.Add(j);
                }
            }
            next.AddRange(links.Where(l => l.From == at).Select(l => l.To));
            next.AddRange(links.Where(l => l.To == at && !l.OneWay).Select(l => l.From));
            foreach (var j in next.Where(reached.Add))
            {
                queue.Enqueue(j);
            }
        }
        for (var i = 0; i < placed.Length; i++)
        {
            if (!reached.Contains(i))
            {
                errors.Add(new(file, $"start.place[{i}]", "can't be reached from the entry"));
            }
        }
    }

    private static void CheckCrew(RulesPack pack, List<ContentError> errors)
    {
        const string file = PackLoader.CrewFile;
        var steps = pack.Rules.TrackSteps;
        var seen = new HashSet<CrewId>();
        foreach (var crew in pack.Crew)
        {
            var where = $"crew {crew.Id}";
            Id(crew.Id.Value, file, where, errors);
            if (!seen.Add(crew.Id))
            {
                errors.Add(new(file, where, "is defined twice"));
            }
            foreach (var trait in Traits.All)
            {
                var track = crew.Tracks[trait];
                var at = $"{where} {trait.ToString().ToLowerInvariant()}";
                if (track.Values.Count != steps)
                {
                    errors.Add(new(file, at, $"needs exactly {steps} values"));
                }
                if (track.Values.Any(v => v < 0) || track.Values.Zip(track.Values.Skip(1)).Any(p => p.Second < p.First))
                {
                    errors.Add(new(file, at, "values must be non-negative and never decrease"));
                }
                if (track.Start < 1 || track.Start > steps)
                {
                    errors.Add(new(file, at, $"start must be a step from 1 to {steps}"));
                }
            }
        }
    }

    private static void CheckCards(RulesPack pack, List<ContentError> errors)
    {
        var seen = new HashSet<CardId>();
        foreach (var card in pack.Cards)
        {
            var file = PackLoader.CardFiles.First(f => f.Kind == card.Kind).File;
            var where = $"card {card.Id}";
            Id(card.Id.Value, file, where, errors);
            if (!seen.Add(card.Id))
            {
                errors.Add(new(file, where, "is defined twice (card ids are shared across every deck)"));
            }

            var bodies = (card.Check is null ? 0 : 1) + (card.Choice is null ? 0 : 1) + (card.Item is null ? 0 : 1);
            if (bodies != 1)
            {
                errors.Add(new(file, where, "needs exactly one of check, choice or item"));
                continue;
            }
            if (card.Kind == CardKind.Incident && card.Item is not null)
            {
                errors.Add(new(file, where, "an Incident is a check or a choice, not an item"));
            }
            if (card.Kind != CardKind.Incident && card.Item is null)
            {
                errors.Add(new(file, where, $"a {card.Kind} card is an item"));
            }

            if (card.Check is { } check)
            {
                CheckBands(pack, check, file, where, errors);
            }
            if (card.Choice is { } choice)
            {
                if (choice.Options.Count is < 2 or > 4)
                {
                    errors.Add(new(file, where, "a choice needs 2-4 options"));
                }
                if (choice.Options.Select(o => o.Id).Distinct().Count() != choice.Options.Count)
                {
                    errors.Add(new(file, where, "option ids must be unique"));
                }
                foreach (var option in choice.Options)
                {
                    var at = $"{where} option {option.Id}";
                    Effects(pack, option.Effects, file, at, errors, passive: false);
                    if (option.Check is { } optionCheck)
                    {
                        if (option.Effects.Any(e => e.IsTerminal))
                        {
                            errors.Add(new(file, at, "an option with a check can't also end in a draw or end the turn"));
                        }
                        CheckBands(pack, optionCheck, file, at, errors);
                    }
                }
            }
            if (card.Item is { } item)
            {
                Effects(pack, item.Passive, file, $"{where} passive", errors, passive: true);
                if (item.Use is { } use)
                {
                    if (use.Effects.IsEmpty)
                    {
                        errors.Add(new(file, $"{where} use", "has no effects"));
                    }
                    Effects(pack, use.Effects, file, $"{where} use", errors, passive: false);
                }
            }
        }
    }

    private static void CheckBands(RulesPack pack, CheckBody check, string file, string where, List<ContentError> errors)
    {
        var bands = check.Bands;
        if (bands.IsEmpty || bands[0].Min != 0)
        {
            errors.Add(new(file, where, "bands must start at a total of 0"));
        }
        for (var i = 0; i < bands.Count; i++)
        {
            var band = bands[i];
            var last = i == bands.Count - 1;
            if (last && band.Max is not null)
            {
                errors.Add(new(file, where, "the last band must have no max, so every total is covered"));
            }
            if (!last && (band.Max is not { } max || max < band.Min || bands[i + 1].Min != max + 1))
            {
                errors.Add(new(file, where, $"band {i} must end just before the next one starts, with no gap or overlap"));
            }
            Effects(pack, band.Effects, file, $"{where} band {i}", errors, passive: false);
        }
    }

    private static void Effects(RulesPack pack, EqArray<Effect> effects, string file, string where,
                                List<ContentError> errors, bool passive)
    {
        for (var i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            if (passive != effect is ExtraDice)
            {
                errors.Add(new(file, where, passive ? "passive effects can only be extraDice"
                                                   : "extraDice is only a passive effect"));
            }
            if (effect.IsTerminal && i != effects.Count - 1)
            {
                errors.Add(new(file, where, $"{effect.GetType().Name} must be the last effect"));
            }
            switch (effect)
            {
                case Gain { Steps: < 1 } or Lose { Steps: < 1 } or ExtraDice { Dice: < 1 }:
                    errors.Add(new(file, where, "steps and dice must be at least 1"));
                    break;
                case Moves { Delta: 0 }:
                    errors.Add(new(file, where, "moves must not be 0"));
                    break;
                case MoveToTile m when !pack.HasTile(m.Tile):
                    errors.Add(new(file, where, $"moveTo names unknown tile {m.Tile}"));
                    break;
            }
        }
    }

    private static void Id(string id, string file, string where, List<ContentError> errors)
    {
        if (!IdPattern().IsMatch(id))
        {
            errors.Add(new(file, where, "ids are lower-case letters, digits and underscores, starting with a letter"));
        }
    }
}

public static class TileGeometry
{
    /// <summary>A tile's hatches after <paramref name="rotation"/> clockwise quarter turns, in N-E-S-W order.</summary>
    public static EqArray<Side> HatchesAt(this TileDef tile, int rotation)
        => tile.Hatches.Select(s => s.Rotate(rotation)).Order().ToEqArray();
}
