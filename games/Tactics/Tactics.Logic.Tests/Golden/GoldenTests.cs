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
            var replay = Replay.Load(path);
            if (replay.Commands.IsDefaultOrEmpty)
            {
                // A replay recorded from the AI (warlord-bridge) is written with no commands until it is first recorded.
                Assert.True(update, $"{Path.GetFileName(path)} has no commands; record it with UPDATE_GOLDENS=1.");
                replay = replay with { Commands = Replay.RecordWithAi(replay) };
                File.WriteAllText(path, Replay.ReplayJson(replay));
            }
            var (_, events) = Replay.Run(replay, roundTripEachStep: true);
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

    [Fact]
    public void Warlord_bridge_plays_the_showcase_triggers()
    {
        var replay = Replay.Load(Path.Combine(Folder(), "warlord-bridge" + Suffix));
        Assert.False(replay.Commands.IsDefaultOrEmpty, "warlord-bridge has no commands; record it with UPDATE_GOLDENS=1.");
        var (final, events) = Replay.Run(replay, roundTripEachStep: false);

        Assert.Equal(5, final.Units.Values.Count(u => u.Sheet.Side == Side.Party && u.SummonedBy is null && !u.Protected));
        Assert.All(final.Units.Values.Where(u => u.Sheet.Side == Side.Party && u.SummonedBy is null), u => Assert.Equal(5, u.Sheet.Level));
        Assert.Contains(new TriggerFired("warlord_taunt"), events);
        Assert.Contains(events, e => e is Said { Speaker: "warlord" });
        Assert.Contains(events, e => e is CameraHint);

        // The seed is picked so the whole showcase plays out: the archers, the collapse and the boss's phase.
        Assert.Contains(new TriggerFired("ridge_archers"), events);
        Assert.Equal(2, events.OfType<Spawned>().Count(s => s.Unit.Value.StartsWith("goblin_archer.", StringComparison.Ordinal)));
        Assert.Contains(new TriggerFired("bridge_collapse"), events);
        Assert.Contains(events, e => e is Fell { IntoDeep: true });
        Assert.NotNull(final.Sunk);
        Assert.Contains("bridge_down", final.Flags);
        Assert.Contains(new TriggerFired("warlord_enraged"), events);
        Assert.Contains(new PhaseEntered(new UnitId("warlord"), 1), events);
        Assert.NotNull(final.Outcome);   // defeat the warlord, survive 8 rounds, or fall
        Assert.Single(events.OfType<BattleEnded>());
    }

    [Fact]
    public void River_ford_storm_replays_conduction_freeze_and_dousing_on_the_shipped_map()
    {
        var replay = Replay.Load(Path.Combine(Folder(), "river-ford-storm" + Suffix));
        var (final, events) = Replay.Run(replay, roundTripEachStep: true);
        Assert.True(final.Environment);
        Assert.Equal("river_crossing", final.MapId);
        Assert.Contains(events, e => e is TileReacted { Rule: "conduct" });
        Assert.Contains(events, e => e is TileReacted { Rule: "freeze" });
        Assert.Contains(events, e => e is TileReacted { Rule: "douse" });
        Assert.Contains(events, e => e is ConditionApplied { Unit.Value: "mage", Condition.Value: "burning" });
        Assert.Contains(events, e => e is ConditionRemoved { Unit.Value: "mage", Condition.Value: "burning" });
        Assert.DoesNotContain(final.Units[new UnitId("mage")].Conditions, c => c.Id.Value == "burning");
    }

    [Fact]
    public void Environmental_tally_reads_the_live_ford_replay_packets()
    {
        var replay = Replay.Load(Path.Combine(Folder(), "river-ford-storm" + Suffix));
        var preset = Replay.Preset(replay.Setup);
        var start = preset.Begin(replay.Seed);
        var state = start.State;
        var tally = new Tactics.Sim.EnvironmentTally(preset.Rules);
        tally.Read(start.Events, state);
        foreach (var command in replay.Commands)
        {
            var result = Battle.Apply(state, command, preset.Rules);
            Assert.Null(result.Rejected);
            state = result.State;
            tally.Read(result.Events, state);
        }
        var stats = tally.Snapshot();
        Assert.Equal(4, stats.Reactions);
        Assert.Equal(6, stats.FriendlyConductionDamage); // the shaman conducts into its own goblin
        Assert.Equal(1, stats.BurningTileRounds);
        Assert.Equal(0, stats.SurfaceDeaths);
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
