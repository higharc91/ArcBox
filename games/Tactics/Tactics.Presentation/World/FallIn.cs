using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// The party falling in after a fight in the world that didn't end in a loss (overworld 5c §5, §6): each walker goes from where its hero stood
/// when the fight ended to where it stands in the world, over <see cref="StageTuning.FallIn"/>, all in world tiles. The leader goes to the
/// view's tile; each follower to the nearest tile to its own that can be stood on and isn't anyone else's, which is where
/// <see cref="WalkTrack.Seed"/> then keeps it. So nobody stacks on the leader after the new epoch (plan 5c, "falls short" 7). Pure.
/// </summary>
/// <param name="From">Where each walker's hero stood.</param>
/// <param name="To">Where each walker ends up.</param>
/// <param name="Seed">The followers of <paramref name="To"/>, for <see cref="WalkTrack.Seed"/>: the leader is the view's own.</param>
public sealed record FallIn(ImmutableDictionary<string, UnitPlace> From, ImmutableDictionary<string, UnitPlace> To, ImmutableDictionary<string, UnitPlace> Seed)
{
    /// <summary>The fall-in from <paramref name="last"/>, the battle as it ended on <paramref name="stage"/>, to <paramref name="explore"/>.</summary>
    public static FallIn Plan(BattleView last, BattleStage stage, ExploreView explore, BoardTerrain world)
    {
        var party = explore.Party.IsDefault ? [] : explore.Party;
        var leader = party.FirstOrDefault(w => w.Leads)?.Id ?? party.FirstOrDefault()?.Id;
        var heroes = last.Stage?.Heroes ?? [];
        var from = ImmutableDictionary.CreateBuilder<string, UnitPlace>(StringComparer.Ordinal);
        var to = ImmutableDictionary.CreateBuilder<string, UnitPlace>(StringComparer.Ordinal);
        var seed = ImmutableDictionary.CreateBuilder<string, UnitPlace>(StringComparer.Ordinal);
        var taken = new HashSet<Tile> { explore.At };

        foreach (var unit in last.Units.OrderBy(u => u.Id.Value, StringComparer.Ordinal))
        {
            if (BattleIntro.WalkerOf(unit.Id, heroes) is not { } walker || party.All(w => w.Id != walker))
                continue;
            var tile = stage.ToWorld(unit.At);
            from[walker] = On(world, tile);
            if (walker == leader)
            {
                to[walker] = On(world, explore.At);
                continue;
            }
            if (BattleStage.Standable(world.Rules, tile, [], t => !taken.Contains(t)) is not { } stand)
                continue;
            taken.Add(stand);
            to[walker] = seed[walker] = On(world, stand);
        }
        return new FallIn(from.ToImmutable(), to.ToImmutable(), seed.ToImmutable());
    }

    /// <summary>
    /// Where each walker that falls in is, <paramref name="seconds"/> in: eased in and out, and at <see cref="To"/> once it's over. Written into
    /// <paramref name="into"/>, which is cleared first and given back, so a frame allocates nothing.
    /// </summary>
    public Dictionary<string, UnitPlace> At(double seconds, Dictionary<string, UnitPlace>? into = null)
    {
        var places = into ?? new Dictionary<string, UnitPlace>(StringComparer.Ordinal);
        places.Clear();
        var t = (float)Math.Clamp(seconds / StageTuning.FallIn, 0, 1);
        var eased = t * t * (3 - 2 * t);
        foreach (var (walker, end) in To)
            places[walker] = From.TryGetValue(walker, out var start)
                ? new UnitPlace(Vector2.Lerp(start.Map, end.Map, eased), float.Lerp(start.Height, end.Height, eased))
                : end;
        return places;
    }

    private static UnitPlace On(BoardTerrain world, Tile tile)
        => new(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), world.Map.InBounds(tile) ? world.Top(tile) : 0);
}
