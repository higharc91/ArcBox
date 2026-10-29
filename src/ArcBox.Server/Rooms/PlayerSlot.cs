using ArcBox.Sdk;

namespace ArcBox.Server.Rooms;

public sealed class PlayerSlot
{
    public required PlayerId Id { get; init; }          // "p1", "p2", … in join order, never reused within a room
    public required string Name { get; set; }           // changes only when a phone in a campaign lobby claims a member
    public required string SecretHash { get; set; }     // Secrets.Hash of the plaintext; the plaintext is never stored
    public required int JoinOrder { get; init; }
    public ProfileId? Profile { get; set; }             // the person behind the seat in a campaign room; null otherwise
    public string? ConnectionId { get; set; }
    public bool AbsentAtStart { get; set; }              // offline when its campaign game started; cleared once someone takes it
    public bool Connected => ConnectionId is not null;
}
