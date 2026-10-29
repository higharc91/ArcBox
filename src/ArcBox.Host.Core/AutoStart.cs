using System.Globalization;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Host.Core;

/// <summary>Dev argument <c>--autostart=&lt;gameId&gt;[:&lt;players&gt;]</c> (players 1–8, default 1).</summary>
public sealed record AutoStartSpec(string GameId, int MinPlayers)
{
    public static bool TryParse(string? value, out AutoStartSpec spec)
    {
        spec = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var parts = value.Split(':', 2);
        var game = parts[0].Trim();
        var players = 1;
        if (game.Length == 0
            || (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out players) || players is < 1 or > 8)))
        {
            return false;
        }
        spec = new AutoStartSpec(game, players);
        return true;
    }
}

public enum AutoStartAction { None, Select, Start }

/// <summary>
/// Drives <c>--autostart</c> for scripted runs: on each lobby the host selects the game if it isn't selected, then
/// starts it once at least <see cref="AutoStartSpec.MinPlayers"/> connected players are in and the server says the
/// room can start, and the TV has the game's scene ready. It acts for the first game only: once the room is in a game, it is done for good.
/// </summary>
public sealed class AutoStart(AutoStartSpec spec)
{
    private bool _startAsked;

    public AutoStartSpec Spec { get; } = spec;

    /// <summary>True once the room has been in a game; nothing more happens after that.</summary>
    public bool Done { get; private set; }

    /// <param name="lobby">The newest lobby.</param>
    /// <param name="sceneReady">False while the TV is still getting the game's scene ready (the start waits for it).</param>
    public AutoStartAction Next(LobbyState lobby, bool sceneReady = true)
    {
        if (Done)
        {
            return AutoStartAction.None;
        }
        if (lobby.InGame)
        {
            Done = true;
            return AutoStartAction.None;
        }
        if (!lobby.Games.Any(g => g.Id == Spec.GameId))
        {
            return AutoStartAction.None;
        }
        if (lobby.SelectedGameId != Spec.GameId)
        {
            return AutoStartAction.Select;
        }
        if (_startAsked || !sceneReady || !lobby.CanStart || lobby.Players.Count(p => p.Connected) < Spec.MinPlayers)
        {
            return AutoStartAction.None;
        }
        _startAsked = true;
        return AutoStartAction.Start;
    }

    /// <summary>The start call failed: try again on the next lobby.</summary>
    public void StartFailed() => _startAsked = false;
}
