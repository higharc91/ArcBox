namespace ArcBox.Server;

public sealed class RoomOptions
{
    public int MaxRooms { get; set; } = 20;
    public int MaxPlayers { get; set; } = 8;
    public int NameMaxLength { get; set; } = 12;
    public TimeSpan GameCallTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public int InvocationsPerSecond { get; set; } = 20;

    /// <summary>How long a stored room can still be rebuilt after it was last saved; older rows are deleted.</summary>
    public TimeSpan RetainFor { get; set; } = TimeSpan.FromHours(24);
}
