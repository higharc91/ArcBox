using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Hubs;

public sealed class RateLimitHubFilter(IOptions<RoomOptions> options) : IHubFilter
{
    // Each connection's limiter lives in that connection's Items, so the filter itself is
    // stateless: SignalR creates a fresh filter per invocation unless it's registered in DI.
    private static readonly object LimiterKey = new();

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var perSecond = options.Value.InvocationsPerSecond;
        context.Context.Items[LimiterKey] = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = perSecond,
            TokensPerPeriod = perSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
        });

        try
        {
            await next(context);
        }
        catch
        {
            // SignalR skips OnDisconnectedAsync when OnConnectedAsync fails.
            DisposeLimiter(context.Context);
            throw;
        }
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (invocationContext.Context.Items.TryGetValue(LimiterKey, out var value) && value is RateLimiter limiter)
        {
            using var lease = limiter.AttemptAcquire();
            if (!lease.IsAcquired)
            {
                // Button-mashing past the limit is normal play, so excess input is just dropped.
                if (invocationContext.HubMethodName == nameof(ArcHub.SendInput))
                {
                    return null;
                }
                throw new HubException("Slow down.");
            }
        }

        return await next(invocationContext);
    }

    public async Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        try
        {
            await next(context, exception);
        }
        finally
        {
            DisposeLimiter(context.Context);
        }
    }

    private static void DisposeLimiter(HubCallerContext context)
    {
        if (context.Items.Remove(LimiterKey, out var value) && value is IDisposable limiter)
        {
            limiter.Dispose();
        }
    }
}
