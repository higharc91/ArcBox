using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class EntranceTests
{
    private static RegionDef Region(string id) => new(id, id, 1, [8, 8], ["grass/lush"],
        [new RegionChunk(0, 0, RegionTests.Tiles())], [], [],
        new RegionSpawns(new Tile(1, 1), new Tile(2, 1), Ordinal.Map(new[] { ("entrance", new Tile(2, 2)) })), []);

    private static (string Path, string Json)[] Files(string destination = "hollow", string spawn = "entrance", bool exit = true) =>
    [
        ("world/ford.json", JsonSerializer.Serialize(Region("ford"), ArcJson.Options)),
        ("world/hollow.json", JsonSerializer.Serialize(Region("hollow"), ArcJson.Options)),
        ("interactables/ford.json", JsonSerializer.Serialize(new { region = "ford", interactables = new[] { new { id = "mouth", kind = "cave_mouth", at = new[] { 3, 2 }, name = "the Hollow", to = new { region = destination, spawn } } } }, ArcJson.Options)),
        ("interactables/hollow.json", exit ? """{"region":"hollow","interactables":[{"id":"exit","kind":"cave_mouth","at":[3,2],"to":{"region":"ford","spawn":"entrance"}}]}""" : """{"region":"hollow","interactables":[]}"""),
    ];

    [Fact]
    public void An_entrance_is_always_open_and_proposes_a_travel_vote()
    {
        var rules = ValidationTests.LoadChapter(Files());
        var mouth = Assert.Single(rules.InteractablesIn("ford"));
        var save = Interactables.Initial(mouth);
        Assert.Equal(InteractableKinds.Idle, save.State);
        Assert.False(Interactables.Blocks(mouth, save, new HashSet<string>()));
        Assert.Equal("Enter the Hollow", Interactables.Label(mouth, save, new HashSet<string>(), false));
        var plan = Assert.IsType<ScenePlan>(InteractScenes.Plan(mouth, save, new HashSet<string>(), false, _ => "item"));
        var ask = plan.Scene.Node(plan.Scene.Start)!;
        Assert.True(ask.Vote);
        Assert.Equal(["enter", "later"], ask.Options.Select(o => o.Id));
        Assert.Empty(plan.Outcomes);
    }

    [Theory]
    [InlineData("missing", "entrance", "region")]
    [InlineData("hollow", "missing", "spawn")]
    public void Bad_destinations_report_the_source_entrance(string destination, string spawn, string field)
    {
        var errors = ValidationTests.ErrorsOfChapter(Files(destination, spawn));
        Assert.Contains(errors, e => e.File.EndsWith("interactables/ford.json", StringComparison.Ordinal) && e.Pointer == "/interactables/0/to/" + field);
    }

    [Fact]
    public void An_entered_region_needs_a_reachable_return_mouth()
    {
        var errors = ValidationTests.ErrorsOfChapter(Files(exit: false));
        Assert.Contains(errors, e => e.Pointer == "/interactables/0/to" && e.Message.Contains("return", StringComparison.Ordinal));
    }
}
