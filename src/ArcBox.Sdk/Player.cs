using System.Text.Json.Serialization;

namespace ArcBox.Sdk;

/// <param name="Profile">The profile behind this seat in a profile-backed (campaign) room, otherwise null. Omitted from the JSON when null.</param>
public sealed record Player(PlayerId Id, string Name, bool IsVip,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProfileId? Profile = null);
