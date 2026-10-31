using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>The missing-value normaliser.</summary>
internal sealed partial class Validation
{
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
