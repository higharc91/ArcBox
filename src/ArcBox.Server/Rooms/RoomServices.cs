using ArcBox.Server.Games;
using ArcBox.Server.Hubs;
using ArcBox.Server.Profiles;

namespace ArcBox.Server.Rooms;

/// <summary>Everything a <see cref="Room"/> needs besides its own state; one instance shared by all rooms.</summary>
public sealed record RoomServices(
    IGameRegistry Registry,
    IClientSender Sender,
    TimeProvider Time,
    RoomOptions Options,
    ProfileService Profiles,
    ILoggerFactory Loggers);
