using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Hubs;

/// <summary>
/// A token bucket per connection (spec §5.4, "Message flood"). Over the limit, <c>SendInput</c> is dropped
/// silently and anything else fails with "Slow down.". Register as a singleton: it holds the buckets.
/// </summary>
public sealed class RateLimitHubFilter(IOptions<RoomOptions> options) : IHubFilter
{
    private readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _limiters = new();

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var limiter = _limiters.GetOrAdd(context.Context.ConnectionId, _ => Create());
        using var lease = limiter.AttemptAcquire();
        if (!lease.IsAcquired)
        {
            if (context.HubMethodName == nameof(ArcHub.SendInput)) return null;
            throw new HubException("Slow down.");
        }
        return await next(context);
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        try
        {
            await next(context, exception);
        }
        finally
        {
            if (_limiters.TryRemove(context.Context.ConnectionId, out var limiter))
            {
                limiter.Dispose();
            }
        }
    }

    private TokenBucketRateLimiter Create()
    {
        var perSecond = options.Value.InvocationsPerSecond;
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = perSecond,
            TokensPerPeriod = perSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
