using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>The missing-value normaliser.</summary>
internal sealed partial class Validation
{
    /// <summary>
    /// A region (<c>world/&lt;id&gt;.json</c>): its size and the shape of its chunks, so <see cref="WorldRegion.From"/> can't
    /// fail on it. What it says about the rest of the pack is checked once everything is loaded.
    /// </summary>
    public RegionDef? Region(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
        {
            var def = Deserialize<RegionDef>(e, file, ptr);
            if (def is null)
                return null;
            if (def.Size is not [var w and > 0, var h and > 0])
            {
                Error(file, Pointer.Append(ptr, "size"), "The size must be [w, h], both at least 1.");
                return null;
            }

            var errors = Errors.Count;
            var seen = new HashSet<(int, int)>();
            for (var i = 0; i < def.Chunks.Length; i++)
            {
                var chunk = def.Chunks[i];
                var at = Pointer.Append(Pointer.Append(ptr, "chunks"), i);
                if (chunk.X < 0 || chunk.Y < 0 || (long)chunk.X * WorldRegion.ChunkSize >= w || (long)chunk.Y * WorldRegion.ChunkSize >= h)
                {
                    Error(file, at, $"Chunk ({chunk.X}, {chunk.Y}) is outside the {w}x{h} region.");
                    continue;
                }
                if (!seen.Add((chunk.X, chunk.Y)))
                    Error(file, at, $"Chunk ({chunk.X}, {chunk.Y}) is listed twice.");
                ChunkTiles(chunk, def.Materials.Length, file, Pointer.Append(at, "tiles"));
            }
            return Errors.Count == errors ? def : null;
        });

    /// <summary>
    /// Checks one chunk's bytes. The first bad tile of each kind is enough to find the problem, as on a battle map: one
    /// error per tile could be thousands.
    /// </summary>
    private void ChunkTiles(RegionChunk chunk, int materials, string file, string ptr)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(chunk.Tiles);
        }
        catch (FormatException)
        {
            Error(file, ptr, "The tiles aren't valid base64.");
            return;
        }
        if (bytes.Length != WorldRegion.ChunkBytes)
        {
            Error(file, ptr, $"Expected {WorldRegion.ChunkBytes} tile bytes for a chunk ({WorldRegion.ChunkSize}x{WorldRegion.ChunkSize}, 3 per tile), got {bytes.Length}.");
            return;
        }

        const int size = WorldRegion.ChunkSize;
        const int knownFlags = (int)(TileFlags.Water | TileFlags.Difficult | TileFlags.Impassable | TileFlags.Hazard | TileFlags.Deep);
        Tile WorldTile(int i) => new(chunk.X * size + i % size, chunk.Y * size + i / size);
        string? height = null, material = null, flags = null;
        for (var i = 0; i < size * size; i++)
        {
            height ??= bytes[i * 3] > WorldRegion.MaxHeight
                ? $"Tile {WorldTile(i)} has height {bytes[i * 3]}, above the limit of {WorldRegion.MaxHeight}."
                : null;
            material ??= bytes[i * 3 + 1] >= materials
                ? $"Tile {WorldTile(i)} uses material {bytes[i * 3 + 1]}, but the region has {materials} materials."
                : null;
            flags ??= (bytes[i * 3 + 2] & ~knownFlags) != 0
                ? $"Tile {WorldTile(i)} has undefined flag bits 0x{bytes[i * 3 + 2] & ~knownFlags:x2}."
                : null;
        }
        foreach (var problem in new[] { height, material, flags })
            if (problem is not null)
                Error(file, ptr, problem);
    }

    /// <summary>
    /// Makes a freshly deserialised definition safe to use: System.Text.Json leaves an absent list as a default
    /// <see cref="ImmutableArray{T}"/> and an absent map, set or object as null. Lists, sets and maps become empty (string
    /// keys ordinal); a missing non-nullable value of any other kind, and a null inside a list or map, is an error.
    /// Walks every content record reachable from the definition.
    /// </summary>
    private static class ContentShape
    {
        private sealed record Prop(PropertyInfo Info, string JsonName, bool NotNull);

        private static readonly ConcurrentDictionary<Type, ImmutableArray<Prop>> Props = new();

        public static void Fix(object def, string file, string ptr, Validation v)
        {
            foreach (var prop in Props.GetOrAdd(def.GetType(), PropsOf))
            {
                var value = prop.Info.GetValue(def);
                var at = Pointer.Append(ptr, prop.JsonName);
                var type = prop.Info.PropertyType;
                if (IsImmutableArray(type) && IsDefaultArray(value!))
                    prop.Info.SetValue(def, type.GetField(nameof(ImmutableArray<int>.Empty))!.GetValue(null));
                else if (value is null)
                {
                    if (!prop.NotNull)
                        continue;
                    if (EmptyCollection(type) is { } empty)
                        prop.Info.SetValue(def, empty);
                    else
                        v.Error(file, at, $"'{prop.JsonName}' is missing.");
                }
                else
                    Item(value, file, at, v);
            }
        }

        private static void Item(object? item, string file, string ptr, Validation v)
        {
            switch (item)
            {
                case null:
                    v.Error(file, ptr, "A value is missing.");
                    break;
                case not null when IsImmutableArray(item.GetType()):
                    if (IsDefaultArray(item))
                        v.Error(file, ptr, "A value is missing.");
                    else
                    {
                        var i = 0;
                        foreach (var element in (IEnumerable)item)
                            Item(element, file, Pointer.Append(ptr, i++), v);
                    }
                    break;
                case ImmutableSortedSet<string> set:
                    if (set.Contains(null!))
                        v.Error(file, ptr, "A value is missing.");
                    break;
                case IDictionary map:
                    foreach (DictionaryEntry entry in map)
                        Item(entry.Value, file, Pointer.Append(ptr, KeyText(entry.Key)), v);
                    break;
                case not null when IsContentRecord(item.GetType()):
                    Fix(item, file, ptr, v);
                    break;
            }
        }

        private static ImmutableArray<Prop> PropsOf(Type type)
        {
            if (!IsContentRecord(type))
                return [];
            var nullability = new NullabilityInfoContext();
            return
            [
                .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
                    .Select(p => new Prop(p, JsonName(p.Name),
                        !p.PropertyType.IsValueType && nullability.Create(p).ReadState == NullabilityState.NotNull)),
            ];
        }

        /// <summary>A record of this assembly worth walking (not a <see cref="ByLevel{T}"/> table, whose steps are values).</summary>
        private static bool IsContentRecord(Type type)
            => type.IsClass && type.Assembly == typeof(ContentShape).Assembly
               && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ByLevel<>));

        private static bool IsImmutableArray(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>);

        private static bool IsDefaultArray(object array)
            => (bool)array.GetType().GetProperty(nameof(ImmutableArray<int>.IsDefault))!.GetValue(array)!;

        private static object? EmptyCollection(Type type)
        {
            if (!type.IsGenericType)
                return null;
            var generic = type.GetGenericTypeDefinition();
            if (generic != typeof(ImmutableSortedSet<>) && generic != typeof(ImmutableSortedDictionary<,>))
                return null;
            if (type == typeof(ImmutableSortedSet<string>))
                return Ordinal.Set();
            var empty = type.GetField("Empty")!.GetValue(null)!;
            return generic == typeof(ImmutableSortedDictionary<,>) && type.GetGenericArguments()[0] == typeof(string)
                ? type.GetMethod("WithComparers", [typeof(IComparer<string>)])!.Invoke(empty, [StringComparer.Ordinal])
                : empty;
        }

        private static string KeyText(object key)
            => key is Enum
                ? key.GetType().GetField(key.ToString()!)?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? key.ToString()!
                : Convert.ToString(key, CultureInfo.InvariantCulture) ?? "";
    }
}
