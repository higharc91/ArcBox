using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>Owned, proficiency-checked equipment. Carried equipment cannot be consumed in battle.</summary>
public static class EquipmentRules
{
    public static bool Reserved(CharacterRecord c, ItemDef item) => item.Equipment is { } e &&
        (e.Weapon is { } weapon && c.Weapon == weapon || e.Armor is { } armor && c.Armor == armor || e.Shield && c.Shield);

    public static string Describe(ItemDef item, RulesPack rules) => item.Equipment is { } e
        ? e.Weapon is { } w ? $"{item.Name}: {rules.Weapons[w].Damage} {rules.Weapons[w].DamageType}. " + (rules.Weapons[w].Tags.Contains("plus_one") ? "+1 to attacks and weapon damage. " : "") + "Equip between sessions."
        : e.Armor is { } a ? $"{item.Name}: base AC {rules.Armors[a].BaseAc}. Equip between sessions."
        : "Shield: +2 AC. Equip between sessions." : item.Name;

    public static bool Proficient(CharacterRecord c, EquipmentDef gear, RulesPack rules)
    {
        var cls = rules.Classes[c.Class];
        var hexblade = c.Subclass == "warlock.hexblade";
        if (gear.Weapon is { } id)
        {
            var w = rules.Weapons[id];
            return cls.WeaponProf.Contains(id) || cls.WeaponProf.Contains(id.Replace("_plus_one", "", StringComparison.Ordinal)) || cls.WeaponProf.Any(w.Tags.Contains)
                || cls.WeaponProf.Contains("martial_light") && w.Tags.Contains("martial") && w.Tags.Contains("light")
                || hexblade && w.Tags.Contains("martial");
        }
        if (gear.Shield) return cls.ArmorProf.Contains("shield") || cls.ArmorProf.Contains("shields") || hexblade;
        return gear.Armor is { } armor && (cls.ArmorProf.Contains(rules.Armors[armor].Kind.ToString().ToLowerInvariant())
            || hexblade && rules.Armors[armor].Kind == ArmorKind.Medium);
    }

    public static CharacterRecord ClaimStarting(CharacterRecord c, RulesPack rules)
    {
        if (c.EquipmentClaimed == true) return c;
        var bag = c.Bag;
        foreach (var item in rules.Items.Values.Where(i => Reserved(c, i)))
            if (!bag.Any(s => s.Item == item.Id && s.Count > 0)) bag = bag.Add(new(item.Id, 1));
        return c with { Bag = bag, EquipmentClaimed = true };
    }

    public static (CharacterRecord? Character, string? Problem) Equip(CharacterRecord c, string slot, string item, RulesPack rules)
    {
        c = ClaimStarting(c, rules);
        if (item.Length == 0) return slot switch
        {
            "armor" => (c with { Armor = "" }, null), "shield" => (c with { Shield = false }, null),
            _ => (null, "Choose equipment you own."),
        };
        if (!rules.Items.TryGetValue(new ItemId(item), out var definition) || definition.Equipment is not { } gear
            || !c.Bag.Any(s => s.Item == definition.Id && s.Count > 0)) return (null, "Choose equipment you own.");
        if (!Proficient(c, gear, rules)) return (null, "Your class is not proficient with that equipment.");
        if (gear.Shield && rules.Weapons[c.Weapon].Tags.Contains("two_handed")) return (null, "A two-handed weapon leaves no hand for a shield.");
        if (gear.Weapon is { } newWeapon && c.Shield && rules.Weapons[newWeapon].Tags.Contains("two_handed")) return (null, "Unequip your shield before using a two-handed weapon.");
        var changed = slot switch
        {
            "weapon" when gear.Weapon is { } w => c with { Weapon = w },
            "armor" when gear.Armor is { } a => c with { Armor = a },
            "shield" when gear.Shield => c with { Shield = true },
            _ => null,
        };
        return changed is null ? (null, "That item does not fit this slot.") : (changed, null);
    }
}
