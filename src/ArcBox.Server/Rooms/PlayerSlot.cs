using ArcBox.Sdk;

namespace ArcBox.Server.Rooms;

public sealed class PlayerSlot
{
    public required PlayerId Id { get; init; }          // "p1", "p2", … in join order, never reused within a room
    public required string Name { get; init; }
    public required string Secret { get; init; }
    public required int JoinOrder { get; init; }
    public string? ConnectionId { get; set; }
    public bool Connected => ConnectionId is not null;
}
