using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// Every <c>Golden/*.replay.json</c> must produce exactly the events in its <c>*.events.json</c>. Set
/// <c>UPDATE_GOLDENS=1</c> to rewrite the event files from the engine instead, then review the diff.
/// </summary>
public sealed class GoldenTests
{
    private const string Suffix = ".replay.json";

    /// <summary>The source folder of the golden files (found from the test binaries' folder upwards).</summary>
    private static string Folder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tactics.Logic.Tests.csproj")))
                return Path.Combine(dir.FullName, "Golden");
        throw new DirectoryNotFoundException("Can't find the Tactics.Logic.Tests source folder.");
    }

    private static string[] Replays()
        => [.. Directory.GetFiles(Folder(), "*" + Suffix).Order(StringComparer.Ordinal)];

    [Fact]
    public void Every_replay_matches_events()
    {
        var update = Environment.GetEnvironmentVariable("UPDATE_GOLDENS") == "1";
        var replays = Replays();
        Assert.NotEmpty(replays);

        foreach (var path in replays)
        {
            var (_, events) = Replay.Run(Replay.Load(path), roundTripEachStep: true);
            var actual = Replay.EventsJson(events);
            var golden = path[..^Suffix.Length] + ".events.json";
            if (update)
            {
                File.WriteAllText(golden, actual);
                continue;
            }

            Assert.True(File.Exists(golden), $"{Path.GetFileName(golden)} is missing; record it with UPDATE_GOLDENS=1.");
            var expected = File.ReadAllText(golden);
            if (expected != actual)
                Assert.Fail($"{Path.GetFileName(path)} no longer matches {Path.GetFileName(golden)}: {FirstDifference(expected, actual)}");
        }
    }

    [Fact]
    public void Replay_is_deterministic()
    {
        foreach (var path in Replays())
        {
            var replay = Replay.Load(path);
            var (first, firstEvents) = Replay.Run(replay, roundTripEachStep: false);
            var (second, secondEvents) = Replay.Run(replay, roundTripEachStep: true);

            Assert.Equal(JsonSerializer.Serialize(first, ArcJson.Options), JsonSerializer.Serialize(second, ArcJson.Options));
            Assert.Equal(Replay.EventsJson(firstEvents), Replay.EventsJson(secondEvents));
        }
    }

    [Fact]
    public void Skirmish_basic_covers_the_basics()
    {
        var replay = Replay.Load(Path.Combine(Folder(), "skirmish-basic" + Suffix));
        var (_, events) = Replay.Run(replay, roundTripEachStep: false);

        Assert.InRange(replay.Commands.Length, 10, 20);
        Assert.Contains(replay.Commands, c => c is Move);
        Assert.Contains(replay.Commands, c => c is React);
        Assert.Contains(events, e => e is AttackResolved);
        Assert.Contains(events, e => e is Downed);
        Assert.Contains(events, e => e is Revived);
    }

    [Fact]
    public void River_crossing_plays_the_base_pack_content()
    {
        var replay = Replay.Load(Path.Combine(Folder(), "river-crossing" + Suffix));
        var (final, events) = Replay.Run(replay, roundTripEachStep: false);

        Assert.Equal(3, final.Round);   // two full rounds, as written
        Assert.Contains(events, e => e is ConditionApplied { Condition.Value: "unconscious" });   // Sleep
        Assert.Contains(events, e => e is TurnSkipped);
        Assert.Contains(events, e => e is ConditionApplied { Condition.Value: "statMod.cleric.bless" });
        Assert.Contains(events, e => e is Died);
        Assert.Contains(events, e => e is Downed);
        Assert.Contains(events, e => e is PassiveTriggered { Passive: "human.resourceful" });
    }

    private static string FirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var (x, y) = (i < e.Length ? e[i] : "(end)", i < a.Length ? a[i] : "(end)");
            if (x != y)
                return $"line {i + 1}\n  expected: {x}\n  actual:   {y}";
        }
        return "they differ only in line endings";
    }
}
