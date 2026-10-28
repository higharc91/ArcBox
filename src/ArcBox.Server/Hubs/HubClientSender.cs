using Microsoft.AspNetCore.SignalR;

namespace ArcBox.Server.Hubs;

public sealed class HubClientSender(IHubContext<ArcHub> hub, ILogger<HubClientSender> logger) : IClientSender
{
    // Fire-and-forget so the room loop never waits on a slow client. Sends still go out in
    // call order: SendCoreAsync queues on the connection's write lock before it yields.
    public void Send(string connectionId, string method, params object?[] args)
    {
        _ = SendAsync(connectionId, method, args);
    }

    private async Task SendAsync(string connectionId, string method, object?[] args)
    {
        try
        {
            await hub.Clients.Client(connectionId).SendCoreAsync(method, args);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send {Method} to {ConnectionId}", method, connectionId);
        }
    }
}
