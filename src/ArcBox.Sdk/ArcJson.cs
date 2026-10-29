using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ArcBox.Sdk;

public static class ArcJson
{
    public static JsonSerializerOptions Options { get; }

    static ArcJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new PlayerIdJsonConverter());
        options.Converters.Add(new ProfileIdJsonConverter());
        options.MakeReadOnly();
        Options = options;
    }
}

public sealed class PlayerIdJsonConverter : JsonConverter<PlayerId>
{
    public override PlayerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return new PlayerId(value ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, PlayerId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

public sealed class ProfileIdJsonConverter : JsonConverter<ProfileId>
{
    public override ProfileId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return new ProfileId(value ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, ProfileId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
