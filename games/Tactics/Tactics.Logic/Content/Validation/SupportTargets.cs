using System.Text.Json;

namespace Tactics.Logic;

internal sealed partial class Validation
{
    private static bool CanAffectHostiles(TargetDef target)
    {
        if (target.Kind == TargetKind.Self && target.Area.Shape is AreaShape.Single or AreaShape.Self)
            return false;
        if (target.Kind == TargetKind.EmptyTile && target.Area.Shape == AreaShape.Single)
            return false;
        if (target.Area.Affects is { } filter)
            return filter != AffectedSide.Allies;
        return target.Area.Shape switch
        {
            AreaShape.AllAllies => false,
            AreaShape.AllEnemies => true,
            _ => target.Kind is not (TargetKind.Ally or TargetKind.DownedAlly),
        };
    }

    // Follow each branch at its actual target scope, including zones whose passives have their own recipients.
    private void SupportTargets(JsonElement list, string file, string ptr, bool hostile)
    {
        var i = 0;
        foreach (var effect in list.EnumerateArray())
        {
            var at = Pointer.Append(ptr, i++);
            var key = effect.EnumerateObject().Single(p => BlockRegistry.TryFor(p.Name) is not null).Name;
            var def = effect.Deserialize<EffectDef>(ContentOptions)!;
            if (hostile && (def is HealEffect { Self: false } or TempHpEffect { Self: false } or ReviveEffect or HelpEffect
                || def is StatModEffect mod && Beneficial(mod.Mods)))
                Error(file, Pointer.Append(at, key), "A beneficial effect can reach a hostile side; use an ally target, area affects: allies, or self: true for a caster-only heal.");

            foreach (var branch in Branches(def.GetType()))
                if (Locate(effect, key, branch, at) is { } found)
                    SupportTargets(found.Value, file, found.Pointer, def is HazardEffect || hostile);
            if (def is RandomEffect && Locate(effect, key, "table", at) is { } table)
            {
                var n = 0;
                foreach (var entry in table.Value.EnumerateArray())
                    SupportTargets(entry.GetProperty("effects"), file, Pointer.Append(Pointer.Append(table.Pointer, n++), "effects"), hostile);
            }
            if (def is ZoneEffect zone && Locate(effect, key, "passives", at) is { } passives)
            {
                var n = 0;
                foreach (var passive in passives.Value.EnumerateArray())
                {
                    var passiveAt = Pointer.Append(passives.Pointer, n++);
                    if (passive.TryGetProperty("effects", out var effects))
                    {
                        var targetsSelf = passive.TryGetProperty("target", out var target) && target.GetString() == "self";
                        SupportTargets(effects, file, Pointer.Append(passiveAt, "effects"), !targetsSelf || zone.Affects != AffectedSide.Allies);
                    }
                }
            }
        }
    }

    // Unknown or mixed modifiers are left to content review; do not mistake a debuff for a rally buff.
    private static bool Beneficial(StaticMods m)
        => m.Ac >= 0 && m.Attack >= 0 && m.Damage >= 0 && m.Move >= 0 && m.Initiative >= 0
           && m.ExtraActions >= 0 && m.MaxHp >= 0 && m.SpellDc >= 0 && m.MaxHpPercent >= 0
           && !m.Incapacitated && !m.CantMove && !m.CritWhenAdjacent
           && m.OwnAttacks != Edge.Disadvantage && m.AttackedMelee != Edge.Advantage && m.AttackedRanged != Edge.Advantage
           && (m.AutoFailSaves is null || m.AutoFailSaves.Count == 0)
           && (m.Saves is null || m.Saves.Values.All(v => v >= 0))
           && (m.SaveEdges is null || m.SaveEdges.Values.All(e => e != Edge.Disadvantage));
}
