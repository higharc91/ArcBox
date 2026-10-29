using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArcBox.Server.Storage;

public static class StorageServiceCollectionExtensions
{
    public const string HealthCheckName = "storage";

    /// <summary>
    /// The platform store: binds <c>Storage</c>, opens (and migrates) the database, runs the background writer and the
    /// daily backup, and adds the <c>storage</c> health check. Serve it with <see cref="MapHealthEndpoint"/>.
    /// </summary>
    public static IServiceCollection AddStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ArcDb>();
        services.TryAddSingleton<StorageWriter>();
        services.AddHostedService(sp => sp.GetRequiredService<StorageWriter>());
        services.TryAddSingleton<BackupService>();
        services.AddHostedService(sp => sp.GetRequiredService<BackupService>());
        services.AddHealthChecks().AddCheck<StorageHealthCheck>(HealthCheckName);
        return services;
    }

    /// <summary>
    /// <c>/healthz</c> (or <paramref name="pattern"/>): 200 <c>ok</c> when healthy, 200 <c>degraded</c> when the storage
    /// writer is backed up, 503 <c>unhealthy</c> otherwise.
    /// </summary>
    public static IEndpointConventionBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints, string pattern = "/healthz") =>
        endpoints.MapHealthChecks(pattern, new()
        {
            ResponseWriter = (context, report) =>
            {
                context.Response.ContentType = "text/plain; charset=utf-8";
                return context.Response.WriteAsync(report.Status switch
                {
                    HealthStatus.Healthy => "ok",
                    HealthStatus.Degraded => "degraded",
                    _ => "unhealthy",
                });
            },
        });
}

/// <summary>Unhealthy if the database won't open; degraded if the oldest queued write is more than 10 s old.</summary>
public sealed class StorageHealthCheck(ArcDb db, StorageWriter writer) : IHealthCheck
{
    public static readonly TimeSpan DegradedAfter = TimeSpan.FromSeconds(10);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = db.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteScalar();
        }
        catch (Exception e)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("The database can't be opened.", e));
        }

        var age = writer.OldestPendingAge;
        return Task.FromResult(age > DegradedAfter
            ? HealthCheckResult.Degraded($"The oldest pending write is {age.TotalSeconds:0} s old.")
            : HealthCheckResult.Healthy());
    }
}
