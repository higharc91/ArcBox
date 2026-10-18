using System.Text.Json;
using ArcBox.Sdk;

namespace ArcBox.Server.Rooms;

public sealed record PlayerViewUpdate(PlayerId PlayerId, string ViewId, JsonElement View);

public sealed record ViewUpdates(JsonElement? Host, IReadOnlyList<PlayerViewUpdate> Players)
{
    public static readonly ViewUpdates None = new(null, []);
}
