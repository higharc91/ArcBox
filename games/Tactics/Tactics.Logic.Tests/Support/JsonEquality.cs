using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// Compares values by their JSON (<see cref="ArcJson.Options"/>), for records holding arrays, which compare by reference.
/// </summary>
public sealed class JsonEquality<T> : IEqualityComparer<T>
{
    /// <inheritdoc />
    public bool Equals(T? x, T? y) => Json(x) == Json(y);

    /// <inheritdoc />
    public int GetHashCode(T obj) => StringComparer.Ordinal.GetHashCode(Json(obj));

    /// <summary>The value's JSON.</summary>
    public static string Json(T? value) => JsonSerializer.Serialize(value, ArcJson.Options);
}
