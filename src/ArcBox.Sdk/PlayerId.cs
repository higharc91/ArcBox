using System.Text.Json.Serialization;

namespace ArcBox.Sdk;

[JsonConverter(typeof(PlayerIdJsonConverter))]
public readonly record struct PlayerId(string Value)
{
    public override string ToString() => Value;
}
