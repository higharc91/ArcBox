using System.Text.Json;
using ArcBox.Sdk;

namespace Meridian.Logic.Tests;

/// <summary>Round trips through the platform's JSON options, as the server and a snapshot would.</summary>
internal static class Json
{
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, ArcJson.Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, ArcJson.Options)!;
    public static T RoundTrip<T>(T value) => Read<T>(Write(value));
}
