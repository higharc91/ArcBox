using ArcBox.Sdk.Speech;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Speech;

public static class SpeechServiceCollectionExtensions
{
    /// <summary>
    /// The platform default theme. Puck is "upbeat" in Google's voice list (see <c>RedlineVoices</c> for the sources).
    /// </summary>
    public static readonly VoiceTheme HostTheme =
        new(VoiceThemes.Host, "ArcBox host: upbeat and friendly", "en-US-Chirp3-HD-Puck", Fallback: new FallbackVoice());

    /// <summary>
    /// The platform speech service: binds and validates <c>Speech</c>, registers the <c>arcbox.host</c> theme, the
    /// Google client, the disk cache, prewarming, the per-room rate limiter and the hourly usage log. Map the endpoints
    /// with <see cref="SpeechEndpoints.MapSpeech"/>.
    /// </summary>
    public static IServiceCollection AddSpeech(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SpeechOptions>()
            .Bind(configuration.GetSection(SpeechOptions.Section))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SpeechOptions>, SpeechOptionsValidator>());

        services.AddVoiceThemes(HostTheme);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IVoiceThemeRegistry, VoiceThemeRegistry>();

        services.AddHttpClient<ISpeechSynthesizer, GoogleTtsClient>((sp, http) =>
            {
                var endpoint = sp.GetRequiredService<IOptions<SpeechOptions>>().Value.Google.Endpoint;
                http.BaseAddress = new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/");
                http.Timeout = TimeSpan.FromSeconds(10);
            })
            // The request URL carries the API key; the factory's default loggers would log it.
            .RemoveAllLoggers()
            // The singleton service holds its typed client for the process lifetime; recycle connections for DNS.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) });

        services.TryAddSingleton<SpeechCache>();
        services.TryAddSingleton<SpeechUsage>();
        services.TryAddSingleton<ISpeechService, SpeechService>();
        services.TryAddSingleton<SpeechRateLimiter>();
        services.AddHostedService<SpeechUsageReporter>();
        return services;
    }

    /// <summary>
    /// Registers a game's (or the platform's) voice themes. Ids must be unique across everything registered;
    /// configuration under <c>Speech:Voices:&lt;id&gt;</c> can override any field. Safe to call before or after
    /// <see cref="AddSpeech"/>, and harmless without it.
    /// </summary>
    public static IServiceCollection AddVoiceThemes(this IServiceCollection services, params IEnumerable<VoiceTheme> themes)
    {
        services.AddSingleton(new VoiceThemeRegistration([.. themes]));
        return services;
    }
}

internal sealed class SpeechOptionsValidator(IEnumerable<VoiceThemeRegistration> registrations) : IValidateOptions<SpeechOptions>
{
    public ValidateOptionsResult Validate(string? name, SpeechOptions o)
    {
        var failures = new List<string>();
        if (!string.Equals(o.Provider, "google", StringComparison.OrdinalIgnoreCase))
            failures.Add($"{nameof(o.Provider)} must be 'google'.");
        if (!Uri.TryCreate(o.Google.Endpoint, UriKind.Absolute, out _))
            failures.Add("Google:Endpoint must be an absolute URL.");
        if (o.MaxChars is < 1 or > 5000)
            failures.Add($"{nameof(o.MaxChars)} must be 1–5000.");
        if (o.RequestsPerMinutePerRoom < 1)
            failures.Add($"{nameof(o.RequestsPerMinutePerRoom)} must be at least 1.");
        if (string.IsNullOrWhiteSpace(o.CacheDir))
            failures.Add($"{nameof(o.CacheDir)} must be set.");

        VoiceThemeMerger.Merge(registrations, o.Voices, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
