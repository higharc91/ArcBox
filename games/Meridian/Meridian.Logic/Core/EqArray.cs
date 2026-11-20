using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Logic;

/// <summary>
/// An immutable array with <b>structural</b> equality. <see cref="ImmutableArray{T}"/> compares by reference, so a record
/// holding one loses value equality after a JSON round trip; every collection in the game state is an
/// <see cref="EqArray{T}"/> instead, and records get correct <c>Equals</c> without hand-written overrides.
/// </summary>
/// <remarks>
/// <c>default(EqArray&lt;T&gt;)</c> is empty, never "uninitialised", so a record built with a missing collection still
/// compares, hashes and serialises. Supports collection expressions: <c>EqArray&lt;int&gt; xs = [1, 2, 3];</c>.
/// </remarks>
[CollectionBuilder(typeof(EqArray), nameof(EqArray.Create))]
[JsonConverter(typeof(EqArrayJsonConverterFactory))]
public readonly struct EqArray<T> : IReadOnlyList<T>, IEquatable<EqArray<T>>
{
    private readonly ImmutableArray<T> _items;

    public EqArray(ImmutableArray<T> items) => _items = items.IsDefault ? [] : items;

    public static EqArray<T> Empty => new([]);

    /// <summary>The underlying array; never default.</summary>
    public ImmutableArray<T> Items => _items.IsDefault ? [] : _items;

    public int Count => Items.Length;
    public bool IsEmpty => Items.Length == 0;
    public T this[int index] => Items[index];

    public EqArray<T> Add(T item) => new(Items.Add(item));
    public EqArray<T> RemoveAt(int index) => new(Items.RemoveAt(index));
    public EqArray<T> Remove(T item) => new(Items.Remove(item));
    public EqArray<T> SetItem(int index, T item) => new(Items.SetItem(index, item));
    public EqArray<T> AddRange(IEnumerable<T> items) => new(Items.AddRange(items));
    public int IndexOf(T item) => Items.IndexOf(item);
    public bool Contains(T item) => Items.Contains(item);

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    public bool Equals(EqArray<T> other) => Items.AsSpan().SequenceEqual(other.Items.AsSpan(), EqualityComparer<T>.Default);
    public override bool Equals(object? obj) => obj is EqArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(EqArray<T> left, EqArray<T> right) => left.Equals(right);
    public static bool operator !=(EqArray<T> left, EqArray<T> right) => !left.Equals(right);

    public static implicit operator EqArray<T>(ImmutableArray<T> items) => new(items);

    public override string ToString() => $"[{string.Join(", ", Items)}]";
}

public static class EqArray
{
    /// <summary>The collection-expression builder.</summary>
    public static EqArray<T> Create<T>(ReadOnlySpan<T> items) => new(ImmutableArray.Create(items));

    public static EqArray<T> ToEqArray<T>(this IEnumerable<T> items) => new([.. items]);
}

/// <summary>Writes an <see cref="EqArray{T}"/> as a plain JSON array.</summary>
public sealed class EqArrayJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(EqArray<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(EqArrayJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class EqArrayJsonConverter<T> : JsonConverter<EqArray<T>>
    {
        public override EqArray<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(JsonSerializer.Deserialize<ImmutableArray<T>>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, EqArray<T> value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.Items, options);

        public override bool HandleNull => true;
    }
}
