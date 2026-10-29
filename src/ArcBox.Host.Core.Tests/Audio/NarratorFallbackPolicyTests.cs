using System.Net;
using ArcBox.Host.Core.Audio;

namespace ArcBox.Host.Core.Tests.Audio;

public class NarratorFallbackPolicyTests
{
    private static NarratorFallbackPolicy Policy(bool server = true, bool voices = true) =>
        new() { HasServer = server, HasBuiltInVoice = voices };

    [Fact]
    public void Starts_on_the_server_voice()
    {
        var p = Policy();
        Assert.True(p.UseServer(0));
        Assert.Equal(NarratorState.Google, p.State(0));
    }

    [Fact]
    public void Narrator_falls_back_on_timeout_and_cools_down()
    {
        var p = Policy();
        Assert.Equal(TimeSpan.FromSeconds(2.5), NarratorFallbackPolicy.Timeout);

        p.Failed(TtsFailure.Timeout, now: 10);

        Assert.False(p.UseServer(10));
        Assert.Equal(NarratorState.Fallback, p.State(10));
        Assert.False(p.UseServer(10 + 59.9));
    }

    [Fact]
    public void Fallback_retries_google_after_cooldown()
    {
        var p = Policy();
        p.Failed(TtsFailure.Unavailable, now: 10);

        Assert.True(p.UseServer(10 + NarratorFallbackPolicy.CoolDown.TotalSeconds));
        Assert.Equal(NarratorState.Google, p.State(70));

        // A second failure starts a fresh cool-down from then.
        p.Failed(TtsFailure.Timeout, now: 70);
        Assert.False(p.UseServer(129));
        Assert.True(p.UseServer(130));
    }

    [Theory]
    [InlineData(TtsFailure.Unauthorized)]
    [InlineData(TtsFailure.Unavailable)]
    [InlineData(TtsFailure.Network)]
    [InlineData(TtsFailure.Timeout)]
    public void Server_problems_start_the_sixty_second_cool_down(TtsFailure failure)
    {
        var p = Policy();
        p.Failed(failure, 0);
        Assert.False(p.UseServer(59));
        Assert.True(p.UseServer(60));
    }

    [Fact]
    public void A_bad_request_falls_back_for_that_line_only()
    {
        var p = Policy();
        p.Failed(TtsFailure.BadRequest, 0);
        Assert.True(p.UseServer(0));
    }

    [Fact]
    public void Rate_limit_waits_for_retry_after()
    {
        var p = Policy();
        p.Failed(TtsFailure.RateLimited, 0, TimeSpan.FromSeconds(7));
        Assert.False(p.UseServer(6.9));
        Assert.True(p.UseServer(7));
    }

    [Fact]
    public void Success_clears_the_cool_down()
    {
        var p = Policy();
        p.Failed(TtsFailure.Timeout, 0);
        p.Succeeded();
        Assert.True(p.UseServer(1));
    }

    [Fact]
    public void No_built_in_voices_and_no_server_is_off()
    {
        var p = Policy(voices: false);
        p.Failed(TtsFailure.Unavailable, 0);
        Assert.Equal(NarratorState.Off, p.State(1));
        Assert.Equal(NarratorState.Off, Policy(server: false, voices: false).State(0));
        Assert.Equal(NarratorState.Fallback, Policy(server: false).State(0));
        Assert.False(Policy(server: false).UseServer(0));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TtsFailure.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest, TtsFailure.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests, TtsFailure.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TtsFailure.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, TtsFailure.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, TtsFailure.Unavailable)]
    public void Status_codes_map_to_failures(HttpStatusCode status, TtsFailure expected)
    {
        Assert.Equal(expected, TtsClient.Classify(status));
    }
}
