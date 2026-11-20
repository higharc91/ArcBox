using System.Text.Json;

namespace Meridian.Presentation.Tests;

/// <summary>
/// The TV reads real host views (M4 plan, Task 1): every phase of real sessions, written the way the server writes them
/// and read the way the scene reads them, turns into a screen.
/// </summary>
public sealed class HostViewContractTests
{
    /// <summary>What the scene does with the JSON the shell hands it.</summary>
    private static MeridianHostView Read(JsonElement json) => json.Deserialize<MeridianHostView>(ArcJson.Options)!;

    private static JsonElement Write(object view) => JsonSerializer.SerializeToElement(view, ArcJson.Options);

    [Theory]
    [MemberData(nameof(AllScenarios.Ids), MemberType = typeof(AllScenarios))]
    public void Every_phase_of_a_real_game_becomes_a_screen(string scenario)
    {
        var game = new Harness(players: 4, seed: 2, scenario);
        var modes = new HashSet<TvMode>();
        void Look()
        {
            var json = Write(game.Game.HostView());
            var view = Read(json);
            var screen = TvScreen.Of(view);
            modes.Add(screen.Mode);
            Assert.Equal(view.Seats.Count, screen.Seats.Count);
            // What the scene reads is what the server wrote: nothing is lost on the way.
            Assert.True(JsonElement.DeepEquals(json, Write(view)), "the host view does not round-trip");
        }

        Look();   // Boarding, before anyone picks
        game.Board();
        game.PlayUntil(() =>
        {
            Look();
            return false;
        }, steps: 6000);
        Assert.Equal(SessionPhase.Over, game.Session.Phase);
        Assert.Equal([TvMode.Boarding, TvMode.Board, TvMode.Breach, TvMode.Debrief], modes.Order());
    }

    [Theory]
    [InlineData("explore", TvMode.Board)]
    [InlineData("endgame", TvMode.Board)]
    [InlineData("debrief", TvMode.Debrief)]
    public void The_dev_boards_fixtures_are_screens_too(string fixture, TvMode mode)
    {
        var path = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(BasePack.Root)!)!, "..", "web", "play", "test", "fixtures",
                                "meridian-host", fixture + ".json");
        var view = Read(JsonDocument.Parse(File.ReadAllText(path)).RootElement);
        Assert.Equal(mode, TvScreen.Of(view).Mode);
    }

    [Fact]
    public void The_breach_opens_its_sequence_before_the_briefing_begins()
    {
        var game = new Harness(players: 3, seed: 1, TillerSaysNo.Id).Board();
        game.PlayUntil(() => game.Session.Queue is [{ Kind: "breach" }, ..]);
        Assert.Equal(SessionPhase.Explore, game.Session.Phase);
        Assert.Equal(TvMode.Breach, TvScreen.Of((MeridianHostView)game.Game.HostView()).Mode);
    }
}
