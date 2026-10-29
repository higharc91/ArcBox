using System.Text.Json.Serialization;

namespace ArcBox.Sdk;

/// <summary>
/// A person across rooms and campaigns (<c>pr_</c> + 16 random base64url characters). Opaque and never a credential:
/// games may store it in their documents. Serializes as a plain JSON string, like <see cref="PlayerId"/>.
/// </summary>
[JsonConverter(typeof(ProfileIdJsonConverter))]
public readonly record struct ProfileId(string Value)
{
    public override string ToString() => Value;
}
