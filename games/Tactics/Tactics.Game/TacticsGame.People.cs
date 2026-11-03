using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    private ImmutableHashSet<string> PeopleFlags()
        => RegionPeople.PresenceFlags(StoryFlags(), _doc.World?.Beats.ToImmutableHashSet(StringComparer.Ordinal) ?? []);

    private ImmutableArray<string>? VisiblePeopleFlags()
    {
        if (_region is null || _region.Def.People.IsDefaultOrEmpty) return null;
        var used = _region.Def.People.SelectMany(p => (p.Requires.IsDefault ? [] : p.Requires).Concat(p.Unless.IsDefault ? [] : p.Unless)).ToHashSet(StringComparer.Ordinal);
        ImmutableArray<string> flags = [.. PeopleFlags().Where(used.Contains).Order(StringComparer.Ordinal)];
        return flags.IsEmpty ? null : flags;
    }

    private IEnumerable<(RegionPerson Person, int Index)> PresentPeople()
        => _region is null || _region.Def.People.IsDefault ? [] : _region.Def.People.Select((p, i) => (Person: p, Index: i))
            .Where(p => RegionPeople.Present(p.Person, PeopleFlags()));
}
