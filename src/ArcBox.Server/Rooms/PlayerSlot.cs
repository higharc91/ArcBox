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
    public bool ForAbsentMember { get; set; }            // made by the room at a campaign Start for a member who wasn't here; cleared once a phone binds to it
    /// <summary>
    /// The profiles this seat's phone has proved it holds, by presenting their keys, or because the room made the profile for
    /// this seat (a new member, a new campaign's owner). Only these let the room list and select a campaign (issue #60:
    /// "you have to be there"); a key the room hands out for a member's seat on the grid doesn't count. Reset when
    /// another phone takes the seat; kept across a rejoin, which proves the same phone by the seat's secret.
    /// </summary>
    public HashSet<ProfileId> Vouched { get; set; } = [];
    public bool Connected => ConnectionId is not null;
}
