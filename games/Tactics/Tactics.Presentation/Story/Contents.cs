using System.Collections.Immutable;

namespace Tactics.Presentation.Story;

/// <summary>
/// Equality by what an <see cref="ImmutableArray{T}"/> holds. A record compares its arrays by reference, and the story's screens are drawn
/// afresh each frame, so without this two frames showing the same thing never compare equal and the TV's boards rebuild every frame. A
/// default array holds nothing.
/// </summary>
internal static class Contents
{
    public static bool Same<T>(ImmutableArray<T> a, ImmutableArray<T> b, IEqualityComparer<T>? items = null)
        => Of(a).AsSpan().SequenceEqual(Of(b).AsSpan(), items);

    public static int Hash<T>(ImmutableArray<T> a, IEqualityComparer<T>? items = null)
    {
        var hash = new HashCode();
        foreach (var item in Of(a))
            hash.Add(item, items);
        return hash.ToHashCode();
    }

    private static ImmutableArray<T> Of<T>(ImmutableArray<T> a) => a.IsDefault ? [] : a;

    /// <summary>Rows of rows, compared row by row by what they hold.</summary>
    public sealed class Rows<T> : IEqualityComparer<ImmutableArray<T>>
    {
        public static readonly Rows<T> Instance = new();

        public bool Equals(ImmutableArray<T> a, ImmutableArray<T> b) => Same(a, b);

        public int GetHashCode(ImmutableArray<T> row) => Hash(row);
    }
}
