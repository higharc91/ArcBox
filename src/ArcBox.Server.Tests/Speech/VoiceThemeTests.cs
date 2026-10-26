using ArcBox.Sdk.Speech;
using ArcBox.Server.Games.Redline;
using ArcBox.Server.Speech;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Redline.Logic;

namespace ArcBox.Server.Tests.Speech;

public class VoiceThemeTests
{
    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    [Theory]
    [InlineData("arcbox.host", true)]
    [InlineData("redline.announcer", true)]
    [InlineData("my-game.voice", false)]       // the first segment allows no hyphen
    [InlineData("game.pit-wall", true)]
    [InlineData("game2.a.b", true)]
    [InlineData("host", false)]
    [InlineData("Arcbox.Host", false)]
    [InlineData("arcbox.", false)]
    [InlineData("arcbox.host\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Theme_ids_are_validated(string? id, bool valid) => Assert.Equal(valid, VoiceThemes.IsValidId(id));

    [Fact]
    public void Themes_from_game_and_config_merge()
    {
        var config = Config(
            ("Redline:OpenTdb:Enabled", "false"),
            ("Speech:Voices:redline.announcer:Voice", "en-US-Chirp3-HD-Puck"),
            ("Speech:Voices:redline.announcer:SpeakingRate", "1.2"),
            ("Speech:Voices:redline.announcer:Fallback:Pitch", "0.9"),
            ("Speech:Voices:party.quizmaster:Voice", "en-GB-Chirp3-HD-Charon"),
            ("Speech:Voices:party.quizmaster:LanguageCode", "en-GB"),
            ("Speech:Voices:party.quizmaster:Description", "Quiz master"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSpeech(config);
        services.AddRedline(config);
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IVoiceThemeRegistry>();

        Assert.Equal(
            [VoiceThemes.Host, RedlineVoices.Announcer, RedlineVoices.Questions, "party.quizmaster"],
            registry.All.Select(t => t.Id));
        var announcer = registry.Find(RedlineVoices.Announcer)!;
        Assert.Equal("en-US-Chirp3-HD-Puck", announcer.Voice);
        Assert.Equal(1.2, announcer.SpeakingRate);
        Assert.Equal(RedlineVoices.All[0].Description, announcer.Description); // untouched fields keep the game's value
        Assert.Equal(new FallbackVoice(1.1f, 0.9f), announcer.Fallback);
        Assert.Equal(RedlineVoices.All[1], registry.Find(RedlineVoices.Questions));
        Assert.Equal(new VoiceTheme("party.quizmaster", "Quiz master", "en-GB-Chirp3-HD-Charon", "en-GB"), registry.Find("party.quizmaster"));
        Assert.Null(registry.Find("party.unknown"));
    }

    [Fact]
    public async Task Duplicate_theme_id_fails_startup()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(s => s
                .AddSpeech(Config())
                .AddVoiceThemes(new VoiceTheme("game.voice", "One", "en-US-Chirp3-HD-Puck"))
                .AddVoiceThemes(new VoiceTheme("game.voice", "Two", "en-US-Chirp3-HD-Charon")))
            .Build();

        var e = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains(e.Failures, f => f.Contains("'game.voice' is registered more than once"));
    }

    [Theory]
    [InlineData("Speech:Voices:nobody.voice:SpeakingRate", "1.1", "must set Voice")]
    [InlineData("Speech:Voices:arcbox.host:SpeakingRate", "3", "SpeakingRate must be 0.25–2.0")]
    [InlineData("Speech:Voices:Bad_Id:Voice", "en-US-Chirp3-HD-Puck", "not a valid voice theme id")]
    [InlineData("Speech:Provider", "azure", "Provider must be 'google'")]
    [InlineData("Speech:MaxChars", "0", "MaxChars must be 1–5000")]
    public async Task Invalid_speech_configuration_fails_startup(string key, string value, string expected)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(s => s.AddSpeech(Config((key, value))))
            .Build();

        var e = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains(e.Failures, f => f.Contains(expected));
    }

    [Fact]
    public void Invalid_registered_theme_id_fails_validation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSpeech(Config());
        services.AddVoiceThemes(new VoiceTheme("NoNamespace", "Bad", "en-US-Chirp3-HD-Puck"));
        using var sp = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<SpeechOptions>>().Value);
    }
}
