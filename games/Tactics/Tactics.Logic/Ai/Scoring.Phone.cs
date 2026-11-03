using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    /// <summary>Possible reaction tiles and expected secondary conduction damage. Shares the AI's branch/save maths, never rolls.</summary>
    public (ImmutableArray<Tile> Spread, ImmutableArray<UnitPreview> Affected) ReactionPreview(Strike st, Tile aim, ImmutableArray<UnitId> direct)
    {
        if (!State.Environment || st.Ability is not { } ability)
            return (default, []);
        var footprint = Areas.Tiles(ability.Target.Area, st.From, aim, st.W.Terrain);
        var spread = new SortedSet<Tile>();
        var damage = new Dictionary<UnitId, double>();
        foreach (var branch in ReactionBranches(st, direct).Where(b => b.Chance > 0))
        {
            var changed = new HashSet<Tile>();
            foreach (var rule in Rules.Environment.Reactions)
            {
                if (rule.Damage is null || !branch.Damage.Any(d => d.Type == rule.Damage))
                    continue;
                var available = footprint.Where(t => !changed.Contains(t)).ToImmutableArray();
                if (rule.Conduct is { } conduct)
                {
                    var tiles = Conducted(st.W, available, conduct.Reach);
                    var effects = branch.Damage.Where(d => d.Type == rule.Damage).Cast<EffectDef>().ToImmutableArray();
                    var secondary = new Strike(st.W, st.User, null, aim, neutral: false);
                    foreach (var unit in st.W.State.Units.Values.Where(u => u.Life == LifeState.Up && tiles.Contains(u.Pos) && !direct.Contains(u.Id)))
                    {
                        var outcome = Walk([new SaveEffect(conduct.Save, null, effects, [], conduct.Half)], secondary, unit);
                        damage[unit.Id] = damage.GetValueOrDefault(unit.Id) + branch.Chance * outcome.Damage.Mean;
                    }
                    spread.UnionWith(tiles);
                    changed.UnionWith(tiles);
                }
                else
                {
                    var tiles = available.Where(t => Surfaces.Matches(st.W, Rules.Environment, rule.Tiles, t)).Order().ToImmutableArray();
                    spread.UnionWith(tiles.Where(t => rule.Surface != "burning" || !Surfaces.Water(st.W, t) && !Surfaces.Has(st.W, t, "scorched")));
                    changed.UnionWith(tiles);
                }
            }
        }
        return (spread.Count == 0 ? default : [.. spread],
            [.. damage.OrderBy(d => d.Key).Select(d => new UnitPreview(d.Key, Math.Round(d.Value, 1), 0))]);
    }
}
