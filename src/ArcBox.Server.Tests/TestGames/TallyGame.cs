using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;

namespace ArcBox.Server.Tests.TestGames;

/// <summary>
/// A snapshot game for the platform's restart tests (Meridian M6 plan, Task 2): it counts "add" presses, snapshots only
/// when the count changes (so nothing is saved before the first press), and keeps a secret in its snapshot that no client
/// may ever see.
/// </summary>
public sealed class TallyGame : IGame, ISnapshotGame
{
    /// <summary>Written into every snapshot; a test checks it never reaches a client.</summary>
    public const string Secret = "tally-secret-7f3a";

    private IGameContext? _ctx;
    private int _count;
    private int _saved;
    private bool _resumed;

    public GameManifest Manifest { get; } = new("tally", "Tally", "", 1, 8, ResumesAfterRestart: true);

    public void Start(IGameContext ctx, IReadOnlyList<Player> players) => _ctx = ctx;

    public void Resume(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease? campaign)
    {
        _ctx = ctx;
        _resumed = true;
        _saved = _count;
    }

    public JsonElement? TakeSnapshot()
    {
        if (_count == _saved)
        {
            return null;
        }
        _saved = _count;
        return JsonSerializer.SerializeToElement(new { count = _count, secret = Secret });
    }

    public void Restore(JsonElement snapshot)
    {
        if (!snapshot.TryGetProperty("count", out var count) || snapshot.GetProperty("secret").GetString() != Secret)
        {
            throw new JsonException("Not a tally snapshot.");
        }
        _count = count.GetInt32();
    }

    public void OnInput(PlayerId from, PlayerInput input)
    {
        switch (input.Read<ButtonsInput>().Pressed)
        {
            case "add":
                _count++;
                break;
            case "end":
                _ctx?.EndGame(new GameResults([], $"Tallied {_count}"));
                break;
        }
    }

    public void OnTimer(string key) { }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected) { }

    public object HostView() => new { count = _count, resumed = _resumed };

    public ControllerView PlayerView(PlayerId id) => new ButtonsView($"{_count}", [new Option("add", "Add"), new Option("end", "End")]);
}
