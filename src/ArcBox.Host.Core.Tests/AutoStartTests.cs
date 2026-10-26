using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Host.Core.Tests;

public class AutoStartTests
{
    private static readonly GameManifest[] Games =
    [
        new("counter", "Counter", "", 1, 8),
        new("redline", "REDLINE", "", 2, 8),
    ];

    private static LobbyState Lobby(int players, string? selected = null, bool canStart = false, bool inGame = false, int disconnected = 0) => new(
        "BCDF",
        [
            .. Enumerable.Range(1, players).Select(i => new LobbyPlayer(new PlayerId($"p{i}"), $"Bot{i}", i == 1, true)),
            .. Enumerable.Range(1, disconnected).Select(i => new LobbyPlayer(new PlayerId($"d{i}"), $"Gone{i}", false, false)),
        ],
        selected,
        Games,
        inGame,
        canStart,
        null);

    [Theory]
    [InlineData("redline", "redline", 1)]
    [InlineData("redline:3", "redline", 3)]
    [InlineData("counter:1", "counter", 1)]
    public void Parses_game_and_player_count(string arg, string game, int players)
    {
        Assert.True(AutoStartSpec.TryParse(arg, out var spec));
        Assert.Equal(new AutoStartSpec(game, players), spec);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(":3")]
    [InlineData("redline:0")]
    [InlineData("redline:x")]
    [InlineData("redline:9")]
    public void Rejects_bad_arguments(string? arg)
    {
        Assert.False(AutoStartSpec.TryParse(arg, out _));
    }

    [Fact]
    public void Selects_the_game_then_waits_for_enough_players_then_starts_once()
    {
        var auto = new AutoStart(new AutoStartSpec("redline", 3));

        Assert.Equal(AutoStartAction.Select, auto.Next(Lobby(0)));
        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(1, "redline")));
        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(2, "redline", canStart: true)));
        Assert.Equal(AutoStartAction.Start, auto.Next(Lobby(3, "redline", canStart: true)));
        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(3, "redline", canStart: true)));   // already asked
        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(3, "redline", canStart: true, inGame: true)));
        Assert.True(auto.Done);
    }

    [Fact]
    public void Never_starts_again_after_the_game()
    {
        var auto = new AutoStart(new AutoStartSpec("redline", 2));
        auto.Next(Lobby(2, "redline", canStart: true));
        auto.Next(Lobby(2, "redline", canStart: true, inGame: true));

        // Back in the lobby after the race: the players decide what happens next.
        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(2, "redline", canStart: true)));
        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(2, "counter", canStart: true)));
    }

    [Fact]
    public void Waits_for_the_server_to_allow_the_start()
    {
        var auto = new AutoStart(new AutoStartSpec("redline", 1));

        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(1, "redline", canStart: false)));
        Assert.Equal(AutoStartAction.Start, auto.Next(Lobby(2, "redline", canStart: true)));
    }

    [Fact]
    public void Waits_for_the_tv_scene_to_be_ready()
    {
        var auto = new AutoStart(new AutoStartSpec("redline", 2));

        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(2, "redline", canStart: true), sceneReady: false));
        Assert.Equal(AutoStartAction.Start, auto.Next(Lobby(2, "redline", canStart: true), sceneReady: true));
    }

    [Fact]
    public void Disconnected_players_do_not_count()
    {
        var auto = new AutoStart(new AutoStartSpec("redline", 3));

        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(2, "redline", canStart: true, disconnected: 1)));
    }

    [Fact]
    public void A_failed_start_is_retried_on_the_next_lobby()
    {
        var auto = new AutoStart(new AutoStartSpec("redline", 2));
        Assert.Equal(AutoStartAction.Start, auto.Next(Lobby(2, "redline", canStart: true)));

        auto.StartFailed();

        Assert.Equal(AutoStartAction.Start, auto.Next(Lobby(2, "redline", canStart: true)));
    }

    [Fact]
    public void A_game_the_server_does_not_have_is_never_selected()
    {
        var auto = new AutoStart(new AutoStartSpec("tetris", 1));

        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(3)));
    }

    [Fact]
    public void A_room_already_in_a_game_ends_the_autostart()
    {
        var auto = new AutoStart(new AutoStartSpec("redline", 2));

        Assert.Equal(AutoStartAction.None, auto.Next(Lobby(3, "counter", inGame: true)));
        Assert.True(auto.Done);
    }
}
