using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// A recorded battle (<c>Golden/*.replay.json</c>): the <see cref="TestBattle"/> preset it starts from
/// (<see cref="Replay.Preset"/>), the seed, and every command in order, whoever gave it (players, the AI and reactions
/// alike).
/// </summary>
public sealed record ReplayFile(string Name, ulong Seed, string Setup, ImmutableArray<Command> Commands);

/// <summary>Runs recorded battles and writes their events in the canonical form golden files compare.</summary>
public static class Replay
{
    private const string SkirmishMap = """
        0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0.
        """;

    /// <summary>
    /// The battle a replay's <see cref="ReplayFile.Setup"/> names:
    /// <list type="bullet">
    /// <item><c>skirmish-basic</c>: a test fighter (kara) and rogue (vex), both player-controlled, against three goblins
    /// (g1, g2, g3) on a flat 7 × 4 field, with the test conditions (<see cref="TestConditions.All"/>). Turn order: vex,
    /// g1, g2, g3, kara.</item>
    /// </list>
    /// </summary>
    public static TestBattle Preset(string name) => name switch
    {
        "skirmish-basic" => TestBattle.On(SkirmishMap)
            .Party("kara", TestSheets.Fighter, new Tile(1, 1))
            .Party("vex", TestSheets.Rogue, new Tile(1, 2))
            .Enemy("g1", TestSheets.Goblin, new Tile(5, 1))
            .Enemy("g2", TestSheets.Goblin, new Tile(5, 2))
            .Enemy("g3", TestSheets.Goblin, new Tile(6, 3))
            .WithConditions([.. TestConditions.All]),
        _ => throw new ArgumentException($"There is no replay preset '{name}'.", nameof(name)),
    };

    /// <summary>
    /// Starts the replay's battle and applies its commands in order, returning the final state and every event (the
    /// start's included). With <paramref name="roundTripEachStep"/> the state goes through its JSON before each command,
    /// as a saved and resumed battle would, and must come back byte-identical. Throws when a command is refused.
    /// </summary>
    public static (BattleState Final, ImmutableArray<BattleEvent> Events) Run(ReplayFile file, bool roundTripEachStep)
    {
        var battle = Preset(file.Setup);
        var (state, started) = battle.Start(file.Seed);
        var events = started.ToBuilder();
        for (var i = 0; i < file.Commands.Length; i++)
        {
            if (roundTripEachStep)
                state = RoundTrip(state);
            var command = file.Commands[i];
            var resolution = Battle.Apply(state, command, battle.Rules);
            if (resolution.Rejected is { } refused)
                throw new InvalidOperationException(
                    $"Replay '{file.Name}' command {i} {JsonSerializer.Serialize(command, ArcJson.Options)} was refused: {refused.Reason}");
            state = resolution.State;
            events.AddRange(resolution.Events);
        }
        return (state, events.ToImmutable());
    }

    /// <summary>Reads a replay file.</summary>
    public static ReplayFile Load(string path)
        => JsonSerializer.Deserialize<ReplayFile>(File.ReadAllText(path), ArcJson.Options)
           ?? throw new InvalidDataException($"'{path}' holds no replay.");

    /// <summary>The state after a trip through its JSON; throws unless it serialises back to the same bytes.</summary>
    public static BattleState RoundTrip(BattleState state)
    {
        var json = JsonSerializer.Serialize(state, ArcJson.Options);
        var back = JsonSerializer.Deserialize<BattleState>(json, ArcJson.Options)!;
        if (JsonSerializer.Serialize(back, ArcJson.Options) != json)
            throw new InvalidOperationException("The state changed on its way through JSON.");
        return back;
    }

    /// <summary>The events as a golden file holds them: a JSON array with one event (canonical ArcJson) per line.</summary>
    public static string EventsJson(ImmutableArray<BattleEvent> events)
        => "[\n" + string.Join(",\n", events.Select(e => JsonSerializer.Serialize(e, ArcJson.Options))) + "\n]\n";
}
