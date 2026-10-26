using Microsoft.AspNetCore.SignalR;

namespace ArcBox.Server.Hubs;

/// <summary>Fire-and-forget sends from rooms to connections; a failed send is logged, never thrown.</summary>
public sealed class HubClientSender(IHubContext<ArcHub> hub, ILogger<HubClientSender> logger) : IClientSender
{
    public void Send(string connectionId, string method, params object?[] args)
    {
        Task task;
        try
        {
            task = hub.Clients.Client(connectionId).SendCoreAsync(method, args);
        }
        catch (Exception ex)
        {
            Log(ex, connectionId, method);
            return;
        }

        if (!task.IsCompletedSuccessfully)
        {
            task.ContinueWith(t => Log(t.Exception, connectionId, method),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    private void Log(Exception? ex, string connectionId, string method) =>
        logger.LogWarning(ex, "Failed to send {Method} to {ConnectionId}", method, connectionId);
}
