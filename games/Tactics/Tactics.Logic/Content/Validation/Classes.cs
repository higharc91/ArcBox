using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>Classes, subclasses and their feature lists.</summary>
internal sealed partial class Validation
{
    /// <summary>
    /// A class: hit die, resource tables, a feature at every level 1–10, subclasses with features at exactly 3, 6 and 10
    /// (unless <see cref="ClassDef.SubclassesPending"/>), the stat priority, and every id its features and kit name.
    /// </summary>
    public ClassDef? Class(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
        {
            var def = Deserialize<ClassDef>(e, file, ptr);
            if (def is null)
                return null;
            if (def.HitDie is not (6 or 8 or 10 or 12))
                Error(file, Pointer.Append(ptr, "hitDie"), $"Hit die {def.HitDie} isn't one of 6, 8, 10 or 12.");
            foreach (var (resource, table) in def.Resources)
                Levels(table, file, Pointer.Append(Pointer.Append(ptr, "resources"), resource));

            var features = Pointer.Append(ptr, "features");
            for (var level = 1; level <= 10; level++)
                if (!def.Features.TryGetValue(level, out var list) || list.IsEmpty)
                    Error(file, features, $"Class '{def.Id}' has no features at level {level}.");
            FeatureLists(def.Features, file, features, level => level is >= 1 and <= 10, "Level {0} is outside levels 1–10.");

            var subclasses = Pointer.Append(ptr, "subclasses");
            if (def.Subclasses.IsEmpty && !def.SubclassesPending)
                Error(file, subclasses, $"Class '{def.Id}' has no subclasses.");
            for (var i = 0; i < def.Subclasses.Length; i++)
            {
                var sub = def.Subclasses[i];
                var at = Pointer.Append(subclasses, i);
                if (string.IsNullOrWhiteSpace(sub.Id))
                    Error(file, Pointer.Append(at, "id"), "The id is empty.");
                var subFeatures = Pointer.Append(at, "features");
                if (!def.SubclassesPending)
                {
                    foreach (var level in SubclassLevels)
                        if (!sub.Features.TryGetValue(level, out var list) || list.IsEmpty)
                            Error(file, subFeatures, $"Subclass '{sub.Id}' has no features at level {level}.");
                }
                FeatureLists(sub.Features, file, subFeatures, level => SubclassLevels.Contains(level),
                    "Subclass features come only at levels 3, 6 and 10, not {0}.");
                if (sub.Companion is { } companion)
                    Ref(RefKind.Monster, companion, file, Pointer.Append(at, "companion"));
            }

            foreach (var (level, picks) in def.DefaultPicks)
                for (var i = 0; i < picks.Length; i++)
                    Ref(RefKind.Ability, picks[i].Value, file,
                        Pointer.Append(Pointer.Append(Pointer.Append(ptr, "defaultPicks"), level), i));
            for (var i = 0; i < def.StartingKit.Bag.Length; i++)
                Ref(RefKind.Item, def.StartingKit.Bag[i].Item.Value, file,
                    Pointer.Append(Pointer.Append(Pointer.Append(Pointer.Append(ptr, "startingKit"), "bag"), i), "item"));
            if (def.AiTags.Priority.Length != 6 || def.AiTags.Priority.Distinct().Count() != 6)
                Error(file, Pointer.Append(Pointer.Append(ptr, "aiTags"), "priority"), "The priority must list each of the six stats once.");
            return def;
        });

    // ---- Classes ----------------------------------------------------------------------------------------------------

    private void FeatureLists(ImmutableSortedDictionary<int, ImmutableArray<FeatureRef>> lists, string file, string ptr,
        Func<int, bool> levelAllowed, string badLevel)
    {
        foreach (var (level, features) in lists)
        {
            var at = Pointer.Append(ptr, level);
            if (!levelAllowed(level))
                Error(file, at, string.Format(CultureInfo.InvariantCulture, badLevel, level));
            for (var i = 0; i < features.Length; i++)
                Feature(features[i], file, Pointer.Append(at, i));
        }
    }

    private void Feature(FeatureRef f, string file, string ptr)
    {
        var kinds = new object?[] { f.Ability, f.Passive, f.Choose, f.ResourceRegen, f.Attacks }.Count(k => k is not null);
        if (kinds != 1 || (!f.From.IsEmpty && f.Choose is null))
        {
            Error(file, ptr, "A feature has exactly one of \"ability\", \"passive\", \"choose\" (with \"from\"), \"resourceRegen\" or \"attacks\".");
            return;
        }
        if (f.Ability is { } ability)
            Ref(RefKind.Ability, ability.Value, file, Pointer.Append(ptr, "ability"));
        Ref(RefKind.Passive, f.Passive, file, Pointer.Append(ptr, "passive"));
        if (f.Choose is { } n)
        {
            if (n < 1 || f.From.Length < n)
                Error(file, Pointer.Append(ptr, "choose"), $"'choose' {n} needs at least 1, and at least {n} entries in 'from'.");
            Refs(RefKind.AbilityOrPassive, f.From, file, Pointer.Append(ptr, "from"));
        }
        if (f.Attacks is < 1)
            Error(file, Pointer.Append(ptr, "attacks"), "'attacks' must be at least 1.");
        if (f.ResourceRegen is { } regen && (regen.Amount < 1 || regen.EveryRounds < 1))
            Error(file, Pointer.Append(ptr, "resourceRegen"), "'amount' and 'everyRounds' must be at least 1.");
    }
}
