using System.Text.Json.Serialization;
using ArcBox.Sdk;

namespace ArcBox.Server.Rooms;

/// <summary>A seat as stored: never its connection, and its secret only as <see cref="Secrets.Hash"/>.</summary>
/// <param name="ForAbsentMember">
/// <see cref="PlayerSlot.ForAbsentMember"/>: the room made the seat for a campaign member who has never been here. Left
/// out of the JSON when false, so older rows read as false.
/// </param>
public sealed record SlotRecord(PlayerId Id, string Name, string SecretHash, int JoinOrder, ProfileId? Profile,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ForAbsentMember = false);

/// <summary>
/// A room's membership as stored in the <c>rooms</c> table (spec §3.5); never its game state. Built on the room's actor by
/// <see cref="Room.ToRecord"/> and turned back into a room by <see cref="Room.Rebuild"/>.
/// </summary>
/// <param name="Parked">
/// The room left memory because it was idle, not because the server stopped. A rebuilt room uses it to say why a game
/// that was running (<paramref name="GameRunning"/>) was interrupted.
/// </param>
public sealed record RoomRecord(string Code, string HostTokenHash, IReadOnlyList<SlotRecord> Slots, int NextSlotId,
    string? SelectedGame, string? CampaignId, bool GameRunning, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc,
    bool Parked = false);
