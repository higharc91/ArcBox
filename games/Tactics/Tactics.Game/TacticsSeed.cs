using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// Story a new campaign starts part way through, for trying a beat without playing up to it: until 5d plays scenes and votes, this is
/// the only way to make a battle beat available. It's a Development-only server setting (<c>Tactics:Seed</c>), and it touches only a
/// campaign that has no place in the world yet, once: after that the campaign's own story is saved and the seed is never read again.
/// </summary>
/// <param name="Flags">The story flags the campaign starts with.</param>
/// <param name="Beats">The beats it starts with done.</param>
/// <param name="Region">The region it starts in; null, or one the pack doesn't have, for the one a new campaign would start in.</param>
/// <param name="At">The tile it starts on; null for the region's start spawn. One that can't be stood on goes to the start spawn, with the usual notice.</param>
public sealed record TacticsSeed(ImmutableArray<string> Flags, ImmutableArray<string> Beats, string? Region = null, Tile? At = null)
{
    /// <summary>The flags; never a default array.</summary>
    public ImmutableArray<string> Flags { get => field.IsDefault ? [] : field; init; } = Flags;

    /// <summary>The beats done; never a default array.</summary>
    public ImmutableArray<string> Beats { get => field.IsDefault ? [] : field; init; } = Beats;

    /// <summary>
    /// <paramref name="doc"/> with the seed's place and story, if it has no place in the world yet and <paramref name="rules"/> has a
    /// region for it; otherwise <paramref name="doc"/> as it is. Its battle, members and encounters won are left alone.
    /// </summary>
    internal TacticsDocument SeedInto(TacticsDocument doc, RulesPack rules)
    {
        if (doc.World is not null)
            return doc;
        var region = (Region is { } id ? rules.RegionFor(id) : null) ?? rules.Regions.Values.MinBy(r => r.Def.Chapter);
        if (region is null)
            return doc;
        var at = At ?? region.Def.Spawns.Start;
        return doc with { World = new WorldSave(region.Def.Id, at, new Tile(at.X + 1, at.Y), null, Flags, Beats) };
    }
}
