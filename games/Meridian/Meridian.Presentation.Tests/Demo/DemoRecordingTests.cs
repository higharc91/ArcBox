using System.Text.Json;
using Meridian.Presentation.Demo;

namespace Meridian.Presentation.Tests.Demo;

/// <summary>
/// The demo mode's recordings (M4 plan, Task 9): one seeded four-player bot game per S1 scenario, every host view the TV
/// would have received, from crew pick to the Debrief. Run with UPDATE_FIXTURES=1 to write them again.
/// </summary>
public sealed class DemoRecordingTests
{
    public static string Dir => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(BasePack.Root)!)!, "..", "src", "ArcBox.Host",
                                             "games", "meridian", "Demo");

    private static List<JsonElement> Record(string scenario)
    {
        var views = new List<JsonElement>();
        var game = new Harness(players: 4, seed: 5, scenario);
        void Look() => views.Add(JsonSerializer.SerializeToElement(game.Game.HostView(), ArcJson.Options));
        Look();
        game.Board();
        Look();
        game.PlayUntil(() =>
        {
            Look();
            return game.Session.Phase == SessionPhase.Debrief;
        }, steps: 6000, seed: 5);
        Look();
        return views;
    }

    [Theory]
    [InlineData(ThePassenger.Id)]
    [InlineData(OneOfUsIsnt.Id)]
    [InlineData(TillerSaysNo.Id)]
    [InlineData(DecayingOrbit.Id)]
    [InlineData(SalvageRights.Id)]
    public void The_recording_matches_what_the_game_sends(string scenario)
    {
        var views = Record(scenario);
        var text = DemoViews.Write(views);
        var path = Path.Combine(Dir, scenario + ".jsonl");
        if (Environment.GetEnvironmentVariable("UPDATE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(path, text);
        }
        Assert.True(File.Exists(path), $"Missing {path}; run with UPDATE_FIXTURES=1.");
        Assert.True(text == File.ReadAllText(path), $"{scenario}.jsonl differs from what the game sends; run with UPDATE_FIXTURES=1.");

        // Reading it back gives every view the TV saw, ending at the Debrief.
        var read = DemoViews.Read(text).ToList();
        Assert.Equal("debrief", read[^1].GetProperty("phase").GetString());
        Assert.True(JsonElement.DeepEquals(views[^1], read[^1]));
        Assert.True(text.Length < 400_000, $"{scenario}: {text.Length} bytes");
    }
}
