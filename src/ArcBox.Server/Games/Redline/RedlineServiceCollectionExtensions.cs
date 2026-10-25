using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Redline.Logic;

namespace ArcBox.Server.Games.Redline;

public static class RedlineServiceCollectionExtensions
{
    public const string ConfigurationSection = "Redline:OpenTdb";

    /// <summary>
    /// Wires up REDLINE: binds <c>Redline:OpenTdb</c>, the OpenTDB client and question pool, the pooled question
    /// source and the game itself.
    /// </summary>
    /// <remarks>
    /// The pool singleton is always registered because <see cref="PooledQuestionSource"/> depends on it. It is only
    /// run as a hosted service when <c>Enabled</c> is true (read at registration time; default true).
    /// </remarks>
    public static IServiceCollection AddRedline(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationSection);

        services.AddOptions<OpenTdbOptions>()
            .Bind(section)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OpenTdbOptions>, OpenTdbOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);

        // No retry/resilience handler: the pool owns retry policy (spec §4.5).
        services.AddHttpClient<OpenTdbClient>((sp, http) =>
        {
            var baseUrl = sp.GetRequiredService<IOptions<OpenTdbOptions>>().Value.BaseUrl;
            http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(10);
        })
        // The singleton pool holds its typed client for the process lifetime, so the factory never rotates the
        // handler; recycle pooled connections ourselves so DNS changes are picked up.
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) });

        services.AddSingleton<OpenTdbQuestionPool>();
        if (section.GetValue("Enabled", true))
            services.AddHostedService(sp => sp.GetRequiredService<OpenTdbQuestionPool>());

        services.AddSingleton<IQuestionSource, PooledQuestionSource>();
        services.AddArcGame(sp => new RedlineGame(sp.GetRequiredService<IQuestionSource>()));
        return services;
    }
}

internal sealed class OpenTdbOptionsValidator : IValidateOptions<OpenTdbOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenTdbOptions o)
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _))
            failures.Add($"{nameof(o.BaseUrl)} must be an absolute URL.");
        if (o.MinRequestSpacing <= TimeSpan.Zero)
            failures.Add($"{nameof(o.MinRequestSpacing)} must be greater than zero.");
        if (o.MaxBackoff < o.MinRequestSpacing)
            failures.Add($"{nameof(o.MaxBackoff)} must be at least {nameof(o.MinRequestSpacing)}.");
        if (o.LowWater <= 0)
            failures.Add($"{nameof(o.LowWater)} must be greater than zero.");
        if (o.LowWater > o.Target)
            failures.Add($"{nameof(o.LowWater)} must not exceed {nameof(o.Target)}.");
        if (o.DedupeWindow <= 0)
            failures.Add($"{nameof(o.DedupeWindow)} must be greater than zero.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
