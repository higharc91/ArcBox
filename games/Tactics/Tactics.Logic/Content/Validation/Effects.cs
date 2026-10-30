using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>The effect walker: block keys, branches and each block's values.</summary>
internal sealed partial class Validation
{
    // ---- Effects ----------------------------------------------------------------------------------------------------

    /// <summary>Checks the effect list at <c>e[name]</c>, if there is one. False when an effect couldn't be read.</summary>
    private bool EffectsIn(JsonElement e, string name, string file, string ptr, bool inSave)
        => e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var list)
           || EffectList(list, file, Pointer.Append(ptr, name), inSave);

    private bool EffectList(JsonElement list, string file, string ptr, bool inSave)
    {
        if (list.ValueKind != JsonValueKind.Array)
            return true;   // the element's own deserialisation reports it
        var ok = true;
        var i = 0;
        foreach (var effect in list.EnumerateArray())
            ok &= Effect(effect, file, Pointer.Append(ptr, i++), inSave);
        return ok;
    }

    /// <summary>
    /// One effect: exactly one known block key, its branches (checked first, with their own pointers), then the block's
    /// values: attack kinds, named addends, by-level tables, condition ids, until-save under a save, and ranges.
    /// </summary>
    private bool Effect(JsonElement e, string file, string ptr, bool inSave)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            Error(file, ptr, "An effect must be an object with one block key, e.g. {\"damage\": {...}}.");
            return false;
        }
        var names = e.EnumerateObject().Select(p => p.Name).ToArray();
        var keys = names.Where(n => BlockRegistry.TryFor(n) is not null).ToArray();
        if (keys.Length != 1)
        {
            Error(file, ptr, keys.Length == 0
                ? names.Length == 0 ? "An effect needs a block key, e.g. {\"damage\": {...}}."
                    : $"Unknown effect block {string.Join(", ", names.Select(n => $"'{n}'"))}."
                : $"An effect has one block key, but this one has {string.Join(" and ", keys.Select(k => $"'{k}'"))}.");
            return false;
        }

        var key = keys[0];
        var ok = true;
        foreach (var branch in Branches(BlockRegistry.For(key).DefType))
            if (Locate(e, key, branch, ptr) is { } found)
                ok &= EffectList(found.Value, file, found.Pointer, inSave || key == "save");
        if (!ok)
            return false;

        var def = Deserialize<EffectDef>(e, file, ptr);
        if (def is null)
            return false;
        string At(string param) => Locate(e, key, param, ptr)?.Pointer ?? Pointer.Append(ptr, key);
        switch (def)
        {
            case AttackEffect { Kind: not ("weapon" or "spell") } attack:
                Error(file, At("kind"), $"Unknown attack kind '{attack.Kind}' (expected \"weapon\" or \"spell\").");
                break;
            case DamageEffect damage:
                if (damage.Dice is not null)
                    Levels(damage.Dice, file, At("dice"));
                Addends(damage.Bonus, file, At("bonus"));
                break;
            case HealEffect heal:
                Levels(heal.Dice, file, At("dice"));
                Addends(heal.Bonus, file, At("bonus"));
                break;
            case ApplyEffect apply:
                Ref(RefKind.Condition, apply.Condition.Value, file, At("condition"));
                if (apply.UntilSave && !inSave)
                    Error(file, At("untilSave"), "'untilSave' needs an enclosing \"save\" block.");
                break;
            case RemoveEffect remove:
                Ref(RefKind.Condition, remove.Condition.Value, file, At("condition"));
                break;
            case TeleportEffect { Range: > MaxRange } teleport:
                Error(file, At("range"), $"Range {teleport.Range} is over the limit of {MaxRange}.");
                break;
            case JumpToEffect { Range: > MaxRange } jump:
                Error(file, At("range"), $"Range {jump.Range} is over the limit of {MaxRange}.");
                break;
        }
        return true;
    }

    private static readonly ConcurrentDictionary<Type, ImmutableArray<string>> BranchCache = new();

    /// <summary>The JSON names of an effect record's branch parameters (its lists of effects).</summary>
    private static ImmutableArray<string> Branches(Type defType)
        => BranchCache.GetOrAdd(defType, t => [.. t.GetConstructors().MaxBy(c => c.GetParameters().Length)!.GetParameters()
            .Where(p => p.ParameterType == typeof(ImmutableArray<EffectDef>))
            .Select(p => JsonName(p.Name!))]);

    /// <summary>
    /// Where an effect's parameter is written: beside the block key, or inside the key's object. Null when it is absent.
    /// </summary>
    private static (JsonElement Value, string Pointer)? Locate(JsonElement e, string key, string param, string ptr)
    {
        if (param != key && e.TryGetProperty(param, out var beside))
            return (beside, Pointer.Append(ptr, param));
        if (e.GetProperty(key) is { ValueKind: JsonValueKind.Object } head && head.TryGetProperty(param, out var inner))
            return (inner, Pointer.Append(Pointer.Append(ptr, key), param));
        return null;
    }

    private void Addends(ImmutableArray<string> addends, string file, string ptr)
    {
        for (var i = 0; i < addends.Length; i++)
        {
            var name = addends[i];
            var known = name is "prof" or "level" or "mod.cast"
                        || (name.StartsWith("mod.", StringComparison.Ordinal) && EffectContext.ParseStat(name[4..]) is not null);
            if (!known)
                Error(file, Pointer.Append(ptr, i),
                    $"Unknown addend '{name}' (expected \"mod.<stat>\", \"mod.cast\", \"prof\" or \"level\").");
        }
    }

    /// <summary>A by-level table must start at level 1 and stay within levels 1–10.</summary>
    private void Levels<T>(ByLevel<T> table, string file, string ptr)
    {
        if (table.Steps.IsEmpty)
        {
            Error(file, ptr, "The by-level table is empty.");
            return;
        }
        foreach (var level in table.Steps.Keys.Where(l => l is < 1 or > 10))
            Error(file, ptr, $"Level {level} is outside levels 1–10.");
        var first = table.Steps.Keys.First();
        if (first > 1)
            Error(file, ptr, $"The by-level table doesn't cover level 1 (it starts at level {first}).");
    }
}
