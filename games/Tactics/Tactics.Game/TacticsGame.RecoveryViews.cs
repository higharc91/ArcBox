using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>Battle-ready values: HP and class resources refill each fight; injuries alter the built sheet.</summary>
public sealed record RecoveryResourceView(string Name, int Current, int Max);
/// <summary>A reusable injury badge; its id resolves through injury_icons/manifest.json.</summary>
public sealed record RecoveryInjuryView(string Id, string Name, string Kind, string Detail);
/// <summary>A compact party card shared by camp, inn and temple on phones and TV.</summary>
public sealed record RecoveryMemberView(string Member, string Name, string Class, bool Here, int Hp, int MaxHp,
    ImmutableArray<RecoveryResourceView> Resources, ImmutableArray<RecoveryInjuryView> Injuries, bool WellFed, bool Room);

public sealed partial class TacticsGame
{
    private ImmutableArray<RecoveryMemberView> RecoveryMembers() => [.. _doc.Members.Select(m =>
    {
        var c = m.Value.Character;
        var sheet = SheetBuilder.ForCharacter(c, Rules);
        return new RecoveryMemberView(m.Key, c.Name, c.Class, _players.Any(p => p.Profile?.Value == m.Key && _online.Contains(p.Id)),
            sheet.MaxHp, sheet.MaxHp, [.. sheet.Resources.Where(r => r.Value > 0).Select(r => new RecoveryResourceView(r.Key, r.Value, r.Value))],
            [.. c.Injuries.Distinct().Select(i => Rules.Injuries.GetValueOrDefault(i)).OfType<InjuryDef>()
                .Select(i => new RecoveryInjuryView(i.Id.Value, i.Name, i.Mends, InjuryDetail(i)))], m.Value.WellFed, Camp?.Rooms.Contains(m.Key) == true);
    })];
}
