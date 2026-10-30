using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// Marks an <see cref="EffectDef"/> whose key may hold one parameter directly, with the rest beside the key:
/// <c>{"apply": "lit", "rounds": 1}</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EffectShorthandAttribute(string parameter) : Attribute
{
    /// <summary>The record parameter the key holds.</summary>
    public string Parameter { get; } = parameter;
}

/// <summary>
/// Reads and writes an <see cref="EffectDef"/> in its single-key content form. The one property whose name is a
/// <see cref="BlockRegistry"/> key names the block; its value is an object of parameters (or, for an
/// <see cref="EffectShorthandAttribute"/> block, that one parameter), and every other property is a further parameter.
/// <list type="bullet">
/// <item>Written: parameters in record order; branches (lists of effects) and a shorthand block's other parameters beside
/// the key, the rest inside it. Parameters at their default (an optional value, null, an empty list) are left out.</item>
/// <item>A <see cref="ByLevel{T}"/> parameter is written <c>{"byLevel": {...}}</c>, or as the bare value when it is the same
/// at every level, and is read from either form or from a bare level-keyed object.</item>
/// <item>An unknown key, two block keys, an unknown or repeated parameter, or a missing required one throws
/// <see cref="JsonException"/> naming it.</item>
/// </list>
/// </summary>
public sealed class EffectJsonConverter : JsonConverter<EffectDef>
{
    private const string ByLevelKey = "byLevel";

    /// <inheritdoc />
    public override EffectDef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject obj)
            throw new JsonException("An effect must be an object with one block key, e.g. {\"damage\": {...}}.");

        var keys = obj.Select(p => p.Key).Where(k => BlockRegistry.TryFor(k) is not null).ToArray();
        if (keys.Length == 0)
        {
            var names = string.Join(", ", obj.Select(p => $"'{p.Key}'"));
            throw new JsonException($"Unknown effect block {(names.Length == 0 ? "(the object is empty)" : names)}.");
        }
        if (keys.Length > 1)
            throw new JsonException($"An effect has one block key, but this one has {string.Join(" and ", keys.Select(k => $"'{k}'"))}.");

        var key = keys[0];
        var shape = Shape.For(BlockRegistry.For(key).DefType, options);
        var args = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

        void Add(string name, JsonNode? value)
        {
            if (!args.TryAdd(name, value))
                throw new JsonException($"'{key}' has '{name}' twice.");
        }

        var head = obj[key];
        if (shape.Shorthand is { } shorthand && head is not JsonObject)
            Add(shorthand.JsonName, head);
        else if (head is JsonObject inner)
            foreach (var (name, value) in inner)
                Add(name, value);
        else
            throw new JsonException($"'{key}' must hold an object of parameters.");
        foreach (var (name, value) in obj)
            if (name != key)
                Add(name, value);

        foreach (var name in args.Keys)
            if (shape.Parameters.All(p => p.JsonName != name))
                throw new JsonException($"'{key}' has no parameter '{name}'.");

        var values = new object?[shape.Parameters.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var p = shape.Parameters[i];
            if (args.TryGetValue(p.JsonName, out var node) && node is not null)
                values[i] = Deserialize(p, node, key, options);
            else if (p.Optional)
                values[i] = p.Default;
            else
                throw new JsonException($"'{key}' needs '{p.JsonName}'.");
        }
        return (EffectDef)shape.Constructor.Invoke(values);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, EffectDef value, JsonSerializerOptions options)
    {
        var block = BlockRegistry.For(value);
        var shape = Shape.For(block.DefType, options);

        JsonNode? head = null;
        var inner = new JsonObject();
        var beside = new List<(string Name, JsonNode? Node)>();
        foreach (var p in shape.Parameters)
        {
            var node = Serialize(p, p.Property.GetValue(value), options);
            if (p == shape.Shorthand)
                head = node;
            else if (p.IsDefault(node))
                continue;
            else if (p.IsBranch || shape.Shorthand is not null)
                beside.Add((p.JsonName, node));
            else
                inner[p.JsonName] = node;
        }

        var result = new JsonObject { [block.Key] = shape.Shorthand is null ? inner : head };
        foreach (var (name, node) in beside)
            result[name] = node;
        result.WriteTo(writer, options);
    }

    private static object? Deserialize(Param p, JsonNode node, string key, JsonSerializerOptions options)
    {
        if (p.IsByLevel)
        {
            if (node is not JsonObject)
                node = new JsonObject { ["1"] = node.DeepClone() };
            else if (node is JsonObject { Count: 1 } wrapped && wrapped[ByLevelKey] is { } table)
                node = table;
        }
        try
        {
            return node.Deserialize(p.Type, options);
        }
        catch (JsonException ex)
        {
            throw new JsonException($"'{key}' has a bad '{p.JsonName}': {ex.Message}", ex);
        }
    }

    private static JsonNode? Serialize(Param p, object? value, JsonSerializerOptions options)
    {
        var node = JsonSerializer.SerializeToNode(value, p.Type, options);
        if (!p.IsByLevel || node is not JsonObject table)
            return node;
        if (table.Count == 1 && table.ContainsKey("1"))
            return table["1"]!.DeepClone();
        return new JsonObject { [ByLevelKey] = table.DeepClone() };
    }

    /// <summary>One constructor parameter of an effect record.</summary>
    private sealed record Param(string JsonName, Type Type, PropertyInfo Property, bool Optional, object? Default,
        JsonNode? DefaultJson, bool IsBranch, bool IsByLevel, bool IsArray)
    {
        public bool IsDefault(JsonNode? node)
            => node is null
               || (IsArray && node is JsonArray { Count: 0 })
               || (Optional && JsonNode.DeepEquals(node, DefaultJson));
    }

    /// <summary>The parameters of an effect record and the constructor that takes them, cached per type.</summary>
    private sealed record Shape(ConstructorInfo Constructor, ImmutableArray<Param> Parameters, Param? Shorthand)
    {
        private static readonly ConditionalWeakTable<JsonSerializerOptions, Dictionary<Type, Shape>> Cache = new();

        public static Shape For(Type type, JsonSerializerOptions options)
        {
            var shapes = Cache.GetValue(options, _ => []);
            lock (shapes)
            {
                if (!shapes.TryGetValue(type, out var shape))
                    shapes[type] = shape = Build(type, options);
                return shape;
            }
        }

        private static Shape Build(Type type, JsonSerializerOptions options)
        {
            var ctor = type.GetConstructors().MaxBy(c => c.GetParameters().Length)
                       ?? throw new InvalidOperationException($"{type.Name} has no public constructor.");
            var parameters = ImmutableArray.CreateBuilder<Param>();
            foreach (var p in ctor.GetParameters())
            {
                var property = type.GetProperty(p.Name!)
                               ?? throw new InvalidOperationException($"{type.Name} has no property for parameter '{p.Name}'.");
                var t = p.ParameterType;
                var isArray = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ImmutableArray<>);
                var isBranch = t == typeof(ImmutableArray<EffectDef>);
                var isByLevel = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ByLevel<>);
                var nullable = Nullable.GetUnderlyingType(t) is not null
                               || (!t.IsValueType && new NullabilityInfoContext().Create(p).WriteState == NullabilityState.Nullable);
                (bool optional, object? fallback) = p.HasDefaultValue ? (true, p.DefaultValue)
                    : isArray ? (true, (object?)EmptyArray(t))
                    : nullable ? (true, null)
                    : (false, null);
                var name = options.PropertyNamingPolicy?.ConvertName(p.Name!) ?? p.Name!;
                var fallbackJson = optional ? JsonSerializer.SerializeToNode(fallback, t, options) : null;
                parameters.Add(new Param(name, t, property, optional, fallback, fallbackJson, isBranch, isByLevel, isArray));
            }

            var built = parameters.ToImmutable();
            Param? shorthand = null;
            if (type.GetCustomAttribute<EffectShorthandAttribute>() is { } attribute)
                shorthand = built.SingleOrDefault(p => p.Property.Name == attribute.Parameter)
                            ?? throw new InvalidOperationException($"{type.Name} has no shorthand parameter '{attribute.Parameter}'.");
            return new Shape(ctor, built, shorthand);
        }

        private static object EmptyArray(Type immutableArray)
            => immutableArray.GetField(nameof(ImmutableArray<int>.Empty), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    }
}
