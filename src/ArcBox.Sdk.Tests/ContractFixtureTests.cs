using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Sdk.Tests;

/// <summary>
/// Pins the JSON the server sends to the phone app (web/play) to checked-in fixtures, which the TS contract test reads.
/// Set ARCBOX_WRITE_FIXTURES=1 to regenerate them after an intentional protocol change.
/// </summary>
public class ContractFixtureTests
{
    private static readonly DateTimeOffset Deadline = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Names => [.. Samples().Keys];

    public static Dictionary<string, object> Samples() => new()
    {
        ["wait"] = View(new WaitView("Waiting for the race…")),
        ["textEntry"] = View(new TextEntryView("Your answer", 40, "type here")),
        ["choice"] = View(new ChoiceView("Pick your driver",
            [new Option("PIA", "#81 Oscar Piastri", "McLaren · MCL40"), new Option("LEC", "#16 Charles Leclerc", "Ferrari · SF-26"), new Option("x", "No hint")],
            AllowMultiple: false, MaxSelections: 1)),
        ["buttons"] = View(new ButtonsView("Science & Nature · Hard. How hard do you push?",
        [
            new Option("cruise", "CRUISE", "+1 if right · nothing lost"),
            new Option("push", "PUSH", "+3 if right · −1 if wrong"),
            new Option("redline", "REDLINE", "+4 if right · spin out if wrong"),
        ])),
        ["draw"] = View(new DrawView("Draw a cat", ["#111111", "#e10600"])),
        ["shake"] = View(new ShakeView("Pop the cork! Shake again to reload", "#ff8000", CanFire: true, InitialCharge: 0.35)),
        ["lobby"] = new LobbyState(
            "BCDF",
            [new LobbyPlayer(new PlayerId("p1"), "ALEX", IsVip: true, Connected: true), new LobbyPlayer(new PlayerId("p2"), "MO", IsVip: false, Connected: false)],
            "redline",
            [new GameManifest("counter", "Counter", "Tap to count", 1, 8), new GameManifest("redline", "REDLINE", "Racing trivia", 1, 8)],
            InGame: false,
            CanStart: true,
            new GameResults([new PlayerScore(new PlayerId("p1"), "ALEX (NOR)", 21)], "ALEX (NOR) wins the race!")),
        ["results"] = new GameResults(
            [new PlayerScore(new PlayerId("p1"), "ALEX (NOR)", 21), new PlayerScore(new PlayerId("p2"), "MO (ALO)", 14)],
            "ALEX (NOR) wins the race!"),
    };

    // Serialized through the base type so the "kind" discriminator is written, as the hub does.
    private static ControllerView View(ControllerView v) => v with { ViewId = "v1", Header = "H", DeadlineUtc = Deadline };

    [Theory]
    [MemberData(nameof(Names))]
    public void Fixtures_match_serializer_output(string name)
    {
        var sample = Samples()[name];
        var declared = sample is ControllerView ? typeof(ControllerView) : sample.GetType();
        var actual = JsonSerializer.SerializeToNode(sample, declared, ArcJson.Options)!;
        var path = Path.Combine(FixtureDir(), name + ".json");

        if (Environment.GetEnvironmentVariable("ARCBOX_WRITE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var text = actual.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            File.WriteAllText(path, text.ReplaceLineEndings("\n") + "\n");
            return;
        }

        Assert.True(File.Exists(path), $"Missing fixture {path}; run with ARCBOX_WRITE_FIXTURES=1 to generate it.");
        var expected = JsonNode.Parse(File.ReadAllText(path));
        Assert.True(JsonNode.DeepEquals(expected, actual),
            $"Fixture {name}.json is out of date.\nExpected: {expected?.ToJsonString()}\nActual:   {actual.ToJsonString()}\nRun with ARCBOX_WRITE_FIXTURES=1 to regenerate.");
    }

    private static string FixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "web", "play", "test", "fixtures");
    }
}
