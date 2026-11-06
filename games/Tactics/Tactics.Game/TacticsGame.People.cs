using System.Collections.Immutable;
using Tactics.Logic;
using ArcBox.Sdk;

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
    {
        if (_region is null || _region.Def.People.IsDefault) yield break;
        var flags = PeopleFlags();
        for (var index = 0; index < _region.Def.People.Length; index++)
            if (RegionPeople.Present(_region.Def.People[index], flags)) yield return (_region.Def.People[index], index);
    }

    private BarkView? _bark;
    private string? _barkNote;

    private ImmutableArray<(InteractableDef Def, string Label)> PersonTargets()
        => _phase != TacticsPhase.Exploration ? [] : [.. PresentPeople()
            .Where(p => ConversationFor(p.Person) is not null || Lore.Barks.For(p.Person) is not null)
            .Select(p => (Def: new InteractableDef("person:" + RegionPeople.Id(p.Person, p.Index), "sign", p.Person.At),
                Label: "Talk to " + (p.Person.Character is { } character ? Lore.Character(character)?.Name ?? character : "the " + p.Person.Preset.Replace('_', ' '))))
            .Where(t => Interactables.InReach(WalkGround(), _at, t.Def))];

    private void TalkToPerson(string target)
    {
        var pick = PresentPeople().FirstOrDefault(p => "person:" + RegionPeople.Id(p.Person, p.Index) == target);
        if (pick.Person is null) return;
        if (ConversationFor(pick.Person) is { } conversation) { BeginConversation(pick.Person, pick.Index, conversation); return; }
        if (Lore.Barks.For(pick.Person) is not { } set || _doc.World is not { } world) return;
        var id = RegionPeople.Id(pick.Person, pick.Index);
        var counters = world.BarkCounters ?? Ordinal.Map<long>();
        var counter = counters.GetValueOrDefault(id);
        if (set.Pick(StoryFlags(), counter) is not { } line) return;
        _doc = _doc with { World = world with { BarkCounters = counters.SetItem(id, counter + 1) } };
        Save();
        _bark = new BarkView(id, counter + 1, _ctx.Now.AddSeconds(Math.Max(4, Lore.ClipSeconds(line.Id) ?? line.Text.Length / 14d)),
            new StoryLineView(line.Id, pick.Person.Voice ?? set.Speaker,
                Lore.Character(pick.Person.Character ?? set.Speaker)?.Name ?? "The " + pick.Person.Preset.Replace('_', ' '), pick.Person.Preset, line.Mood, line.Text));
        _barkNote = _bark.Line.Name + ": " + line.Text;
        Say(_barkNote);
        _ctx.SetTimer("townBark", _bark.Until - _ctx.Now);
    }

    private BarkView? CurrentBark() => _bark is { } bark && bark.Until > _ctx.Now ? bark : null;
}
