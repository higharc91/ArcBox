using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>An authored fixed reward bundle. No RNG is consumed; every win receives the listed items.</summary>
public sealed record LootTableDef(string Id, ImmutableArray<LootDef> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null);

internal sealed partial class Validation
{
    public LootTableDef? LootTable(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
        {
            var def = Deserialize<LootTableDef>(e, file, ptr);
            if (def is null) return null;
            if (string.IsNullOrWhiteSpace(def.Id)) Error(file, Pointer.Append(ptr, "id"), "The id is empty.");
            for (var i = 0; i < def.Items.Length; i++)
            {
                var at = Pointer.Append(Pointer.Append(ptr, "items"), i);
                Ref(RefKind.Item, def.Items[i].Item.Value, file, Pointer.Append(at, "item"));
                AtLeast(def.Items[i].Count, 1, file, Pointer.Append(at, "count"));
            }
            return def;
        });
}
