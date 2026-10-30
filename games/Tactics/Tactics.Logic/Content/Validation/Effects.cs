using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>The effect walker: block keys, branches and each block's values.</summary>
internal sealed partial class Validation
{
    // ---- Effects ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Checks the effect list at <c>e[name]</c>, if there is one. False when an effect couldn't be read.
    /// <paramref name="concentration"/> is whether the ability the effects belong to costs concentration (null outside an
    /// ability): a block that ties itself to concentration needs it.
    /// </summary>
    private bool EffectsIn(JsonElement e, string name, string file, string ptr, bool inSave, bool? concentration = null)
        => e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var list)
           || EffectList(list, file, Pointer.Append(ptr, name), inSave, concentration);

    private bool EffectList(JsonElement list, string file, string ptr, bool inSave, bool? concentration)
    {
        if (list.ValueKind != JsonValueKind.Array)
            return true;   // the element's own deserialisation reports it
        var ok = true;
        var i = 0;
        foreach (var effect in list.EnumerateArray())
            ok &= Effect(effect, file, Pointer.Append(ptr, i++), inSave, concentration);
        return ok;
    }

    /// <summary>
    /// One effect: exactly one known block key, its branches, a <c>random</c> table's effects and a <c>zone</c>'s passives
    /// (checked first, with their own pointers), then the block's values: attack kinds, named addends, by-level tables,
    /// condition, monster and art ids, until-save under a save, ranges, area sizes, counts, the named hook and its args, and
    /// the extra-action kinds and turn-order ops, the monsters a named hook needs, and <c>concentration: true</c> on a zone,
    /// summon or statMod only in an ability that costs concentration (otherwise it would never end).
    /// </summary>
    private bool Effect(JsonElement e, string file, string ptr, bool inSave, bool? concentration)
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
                ok &= EffectList(found.Value, file, found.Pointer, inSave || key == "save", concentration);
        if (key == "random" && Locate(e, key, "table", ptr) is { Value.ValueKind: JsonValueKind.Array } table)
        {
            var i = 0;
            foreach (var entry in table.Value.EnumerateArray())
            {
                var at = Pointer.Append(table.Pointer, i++);
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("effects", out var list))
                    ok &= EffectList(list, file, Pointer.Append(at, "effects"), inSave, concentration);
            }
        }
        if (key == "zone" && Locate(e, key, "passives", ptr) is { Value.ValueKind: JsonValueKind.Array } passives)
        {
            var i = 0;
            foreach (var passive in passives.Value.EnumerateArray())
                ok &= Passive(passive, file, Pointer.Append(passives.Pointer, i++)) is not null;
        }
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
            case TempHpEffect tempHp:
                Levels(tempHp.Dice, file, At("dice"));
                Addends(tempHp.Bonus, file, At("bonus"));
                break;
            case ReviveEffect revive:
                AtLeast(revive.Hp, 1, file, At("hp"));
                break;
            case ZoneEffect zone:
                AreaSize(zone.Area, file, At("area"));
                NeedsConcentration(zone.Concentration, concentration, file, At("concentration"));
                break;
            case AlterTerrainEffect terrain:
                AreaSize(terrain.Area, file, At("area"));
                break;
            case HazardEffect hazard:
                AreaSize(hazard.Area, file, At("area"));
                Ref(RefKind.Art, hazard.HazardArt, file, At("hazardArt"));
                break;
            case SummonEffect summon:
                Ref(RefKind.Monster, summon.Monster, file, At("monster"));
                AtLeast(summon.Count, 1, file, At("count"));
                NeedsConcentration(summon.Concentration, concentration, file, At("concentration"));
                break;
            case StatModEffect statMod:
                NeedsConcentration(statMod.Concentration, concentration, file, At("concentration"));
                break;
            case TransformEffect transform:
                if (NamedHooks.TryFor(transform.Hook) is { } hook)
                {
                    hook.Validate(transform.Args, message => Error(file, At("args"), message));
                    foreach (var monster in hook.Monsters(transform.Args))
                        Ref(RefKind.Monster, monster, file, ArgPointer(transform.Args, monster) is { } arg
                            ? Pointer.Append(At("args"), arg)
                            : At("hook"));
                }
                else
                    Error(file, At("hook"), $"Unknown named hook '{transform.Hook}' (expected {OneOf(NamedHooks.Names)}).");
                break;
            case ExtraActionEffect extra when !ExtraActionBlock.Kinds.Contains(extra.Kind):
                Error(file, At("kind"), $"Unknown extra action '{extra.Kind}' (expected {OneOf(ExtraActionBlock.Kinds)}).");
                break;
            case GrantResourceEffect grant:
                Levels(grant.Amount, file, At("amount"));
                break;
            case RevealEffect reveal:
                AreaSize(reveal.Area, file, At("area"));
                break;
            case TurnOrderEffect turnOrder:
                if (!TurnOrderBlock.Ops.Contains(turnOrder.Op))
                    Error(file, At("op"), $"Unknown turn order op '{turnOrder.Op}' (expected {OneOf(TurnOrderBlock.Ops)}).");
                AtLeast(turnOrder.Slots, 1, file, At("slots"));
                break;
            case ChainEffect chain:
                AtLeast(chain.Times, 1, file, At("times"));
                if (chain.Range > MaxRange)
                    Error(file, At("range"), $"Range {chain.Range} is over the limit of {MaxRange}.");
                break;
            case RepeatEffect repeat:
                AtLeast(repeat.Times, 1, file, At("times"));
                break;
            case IfEffect branching:
                When(branching.When, file, At("when"));
                break;
            case RandomEffect random:
                if (random.Table.IsEmpty)
                    Error(file, At("table"), "The table is empty.");
                for (var i = 0; i < random.Table.Length; i++)
                    if (random.Table[i].Weight < 1)
                        Error(file, Pointer.Append(Pointer.Append(At("table"), i), "weight"), "A weight must be at least 1.");
                break;
        }
        return true;
    }

    /// <summary>Reports <c>'&lt;name&gt;' must be at least &lt;min&gt;.</c> at <paramref name="ptr"/> when <paramref name="value"/> is lower.</summary>
    private void AtLeast(int value, int min, string file, string ptr)
    {
        if (value < min)
            Error(file, ptr, $"'{ptr[(ptr.LastIndexOf('/') + 1)..]}' must be at least {min}.");
    }

    /// <summary>The name of the string arg holding <paramref name="value"/>, or null when the hook fixed it itself.</summary>
    private static string? ArgPointer(JsonElement args, string value)
    {
        if (args.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var arg in args.EnumerateObject())
            if (arg.Value.ValueKind == JsonValueKind.String && arg.Value.GetString() == value)
                return arg.Name;
        return null;
    }

    /// <summary>A block tied to concentration (<paramref name="tied"/>) inside an ability that doesn't cost it would never end.</summary>
    private void NeedsConcentration(bool tied, bool? abilityConcentrates, string file, string ptr)
    {
        if (tied && abilityConcentrates == false)
            Error(file, ptr, "'concentration' needs an ability that costs concentration (\"cost\": {\"concentration\": true}); "
                             + "otherwise it would never end.");
    }

    private void AreaSize(AreaDef area, string file, string ptr)
    {
        if (area.Size > MaxAreaSize)
            Error(file, Pointer.Append(ptr, "size"), $"Area size {area.Size} is over the limit of {MaxAreaSize}.");
    }

    /// <summary>The choices quoted, as <c>"a", "b" or "c"</c>.</summary>
    private static string OneOf(IReadOnlyList<string> choices)
        => choices.Count == 1 ? $"\"{choices[0]}\""
            : string.Join(", ", choices.Take(choices.Count - 1).Select(c => $"\"{c}\"")) + $" or \"{choices[^1]}\"";

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
