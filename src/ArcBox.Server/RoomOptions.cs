namespace ArcBox.Server;

public sealed class RoomOptions
{
    public int MaxRooms { get; set; } = 20;
    public int MaxPlayers { get; set; } = 8;
    public int NameMaxLength { get; set; } = 12;
    // Leave room for scheduling delays when the TV and server share a busy machine.
    public TimeSpan GameCallTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public int InvocationsPerSecond { get; set; } = 20;

    /// <summary>How long a stored room can still be rebuilt after it was last saved; older rows are deleted.</summary>
    public TimeSpan RetainFor { get; set; } = TimeSpan.FromHours(24);
}
