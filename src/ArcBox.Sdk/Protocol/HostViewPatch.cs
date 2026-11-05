using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArcBox.Sdk.Protocol;

/// <summary>
/// The TV's view as a patch against the view it already has (issue #77). A step of the overworld changes a few fields, but the
/// whole view was sent each time; a patch carries only what changed, so a step fits one network packet (about 1,420 bytes).
/// <para>
/// The format is JSON merge patch (RFC 7386) made exact, because <see cref="ArcJson"/> writes explicit <c>null</c>s:
/// </para>
/// <list type="bullet">
/// <item>Where the old value is an object and the patch is an object, the patch merges: a property in the patch replaces (or patches,
/// recursively) the old one, <c>null</c> sets it to <c>null</c>, and the names in a <c>"$delete"</c> array are removed.</item>
/// <item>Where the old value is an array of objects that each have a distinct string <c>"id"</c>, and the patch is an object with
/// <c>"$keyed": "id"</c>, the patch merges by id: <c>"$set"</c> maps an id to a patch for that item (or the whole item, if it's new),
/// and <c>"$order"</c>, when the items or their order changed, lists the ids of the new array in order.</item>
/// <item>Anything else in the patch is the new value.</item>
/// </list>
/// Real property names never start with <c>$</c>; <see cref="Diff"/> returns <c>null</c> (send the whole view) if one does.
/// Applying a patch gives a view equal to the server's (<see cref="SameJson"/>), with object properties possibly in another order.
/// </summary>
public static class HostViewPatch
{
    private const string Delete = "$delete";
    private const string Keyed = "$keyed";
    private const string Set = "$set";
    private const string Order = "$order";
    private const string Id = "id";

    /// <summary>
    /// The patch that turns <paramref name="from"/> into <paramref name="to"/>, or <c>null</c> when it can't be expressed (a property
    /// name starting with <c>$</c>). An empty object means nothing changed.
    /// </summary>
    public static JsonNode? Diff(JsonElement from, JsonElement to)
    {
        if (HasReservedName(to))
            return null;
        return from.ValueKind == JsonValueKind.Object && to.ValueKind == JsonValueKind.Object
            ? DiffObject(from, to)
            : JsonNode.Parse(to.GetRawText());
    }

    /// <summary>Applies <paramref name="patch"/> (from <see cref="Diff"/>) to <paramref name="from"/>.</summary>
    public static JsonElement Apply(JsonElement from, JsonElement patch)
    {
        var node = ApplyNode(from, patch);
        return node is null ? JsonDocument.Parse("null").RootElement.Clone() : JsonSerializer.SerializeToElement(node);
    }

    /// <summary>Whether two JSON values are equal, ignoring the order of object properties.</summary>
    public static bool SameJson(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
            return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var props = a.EnumerateObject().ToList();
                if (props.Count != b.EnumerateObject().Count())
                    return false;
                foreach (var p in props)
                    if (!b.TryGetProperty(p.Name, out var other) || !SameJson(p.Value, other))
                        return false;
                return true;
            }
            case JsonValueKind.Array:
            {
                if (a.GetArrayLength() != b.GetArrayLength())
                    return false;
                using var x = a.EnumerateArray().GetEnumerator();
                using var y = b.EnumerateArray().GetEnumerator();
                while (x.MoveNext() && y.MoveNext())
                    if (!SameJson(x.Current, y.Current))
                        return false;
                return true;
            }
            default:
                return a.GetRawText() == b.GetRawText();
        }
    }

    // ---- Diff ---------------------------------------------------------------------------------------------------------------

    private static JsonObject DiffObject(JsonElement from, JsonElement to)
    {
        var patch = new JsonObject();
        var deleted = new JsonArray();
        foreach (var old in from.EnumerateObject())
            if (!to.TryGetProperty(old.Name, out _))
                deleted.Add(old.Name);
        foreach (var prop in to.EnumerateObject())
        {
            if (!from.TryGetProperty(prop.Name, out var old))
            {
                patch[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
                continue;
            }
            if (Same(old, prop.Value))
                continue;
            patch[prop.Name] = DiffValue(old, prop.Value);
        }
        if (deleted.Count > 0)
            patch[Delete] = deleted;
        return patch;
    }

    private static JsonNode? DiffValue(JsonElement from, JsonElement to)
    {
        if (from.ValueKind == JsonValueKind.Object && to.ValueKind == JsonValueKind.Object)
            return DiffObject(from, to);
        if (from.ValueKind == JsonValueKind.Array && to.ValueKind == JsonValueKind.Array && KeyedIds(from) is { } oldIds && KeyedIds(to) is { } newIds)
        {
            var keyed = DiffKeyed(from, oldIds, to, newIds);
            // Keyed only when it's smaller than the array itself: a short or wholly new array goes as it is.
            if (keyed.ToJsonString().Length < to.GetRawText().Length)
                return keyed;
        }
        return JsonNode.Parse(to.GetRawText());
    }

    private static JsonObject DiffKeyed(JsonElement from, List<string> oldIds, JsonElement to, List<string> newIds)
    {
        var old = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var i = 0;
        foreach (var item in from.EnumerateArray())
            old[oldIds[i++]] = item;
        var set = new JsonObject();
        i = 0;
        foreach (var item in to.EnumerateArray())
        {
            var id = newIds[i++];
            if (!old.TryGetValue(id, out var before))
                set[id] = JsonNode.Parse(item.GetRawText());
            else if (!Same(before, item))
                set[id] = DiffObject(before, item);
        }
        var patch = new JsonObject { [Keyed] = Id };
        if (set.Count > 0)
            patch[Set] = set;
        if (!oldIds.SequenceEqual(newIds, StringComparer.Ordinal))
            patch[Order] = new JsonArray([.. newIds.Select(id => (JsonNode?)JsonValue.Create(id))]);
        return patch;
    }

    /// <summary>The items' ids, when every item is an object with a distinct string <c>"id"</c>; else null.</summary>
    private static List<string>? KeyedIds(JsonElement array)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(Id, out var id) || id.ValueKind != JsonValueKind.String)
                return null;
            var value = id.GetString()!;
            if (!seen.Add(value))
                return null;
            ids.Add(value);
        }
        return ids.Count == 0 ? null : ids;
    }

    private static bool Same(JsonElement a, JsonElement b) => a.ValueKind == b.ValueKind && a.GetRawText() == b.GetRawText() || SameJson(a, b);

    private static bool HasReservedName(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in value.EnumerateObject())
                    if (p.Name.StartsWith('$') || HasReservedName(p.Value))
                        return true;
                return false;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    if (HasReservedName(item))
                        return true;
                return false;
            default:
                return false;
        }
    }

    // ---- Apply --------------------------------------------------------------------------------------------------------------

    private static JsonNode? ApplyNode(JsonElement from, JsonElement patch)
    {
        if (from.ValueKind == JsonValueKind.Object && patch.ValueKind == JsonValueKind.Object)
            return ApplyObject(from, patch);
        if (from.ValueKind == JsonValueKind.Array && patch.ValueKind == JsonValueKind.Object && patch.TryGetProperty(Keyed, out _))
            return ApplyKeyed(from, patch);
        return JsonNode.Parse(patch.GetRawText());
    }

    private static JsonObject ApplyObject(JsonElement from, JsonElement patch)
    {
        var deleted = patch.TryGetProperty(Delete, out var names)
            ? names.EnumerateArray().Select(n => n.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];
        var result = new JsonObject();
        foreach (var old in from.EnumerateObject())
        {
            if (deleted.Contains(old.Name))
                continue;
            result[old.Name] = patch.TryGetProperty(old.Name, out var change) ? ApplyNode(old.Value, change) : JsonNode.Parse(old.Value.GetRawText());
        }
        foreach (var prop in patch.EnumerateObject())
            if (prop.Name != Delete && !from.TryGetProperty(prop.Name, out _))
                result[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        return result;
    }

    private static JsonArray ApplyKeyed(JsonElement from, JsonElement patch)
    {
        var old = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var item in from.EnumerateArray())
        {
            var id = item.GetProperty(Id).GetString()!;
            old[id] = item;
            order.Add(id);
        }
        if (patch.TryGetProperty(Order, out var newOrder))
            order = [.. newOrder.EnumerateArray().Select(i => i.GetString()!)];
        var set = patch.TryGetProperty(Set, out var s) ? s : default;
        var result = new JsonArray();
        foreach (var id in order)
        {
            var has = old.TryGetValue(id, out var before);
            var change = default(JsonElement);
            var changed = set.ValueKind == JsonValueKind.Object && set.TryGetProperty(id, out change);
            result.Add(changed
                ? has ? ApplyNode(before, change) : JsonNode.Parse(change.GetRawText())
                : has ? JsonNode.Parse(before.GetRawText())
                : throw new InvalidOperationException($"Host view patch names item '{id}', which the view doesn't have and the patch doesn't set."));
        }
        return result;
    }
}
