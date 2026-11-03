using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

public sealed class RecoveryShowTests
{
    [Theory]
    [InlineData("camp_start", "spell_fire_aura")]
    [InlineData("camp_mend", "ui_level_up")]
    [InlineData("camp_cook", "ui_confirm")]
    [InlineData("camp_dawn", "ui_menu_open")]
    [InlineData("temple_heal", "shrine_use")]
    [InlineData("inn_room", "door_open")]
    public void Recovery_uses_the_requested_key_then_the_existing_stand_in(string key, string fallback)
        => Assert.Equal([key, fallback], RecoveryShow.Wanted(key));

    [Fact]
    public void The_camp_demo_shows_night_mending_well_fed_and_dawn_from_normal_host_views()
    {
        var steps = CampDemo.Steps();
        Assert.True(RecoveryShow.Night(steps[0].View.Camp));
        Assert.True(steps[1].View.Camp!.Result!.Passed);
        Assert.Equal("tend", steps[1].View.Camp!.Result!.Job);
        Assert.All(steps[2].View.Camp!.People, p => Assert.True(p.WellFed));
        Assert.False(RecoveryShow.Night(steps[3].View.Camp));
        Assert.Null(steps[^1].View.Camp);
        Assert.Single(steps[0].View.Explore!.Things!.Value.Where(t => t.Kind == "campfire"));
    }
    [Fact]
    public void Every_rules_injury_has_a_reusable_native_pixel_icon()
    {
        var root = Tactics.Presentation.Tests.Board.Pack.Root;
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "injury_icons", "manifest.json")));
        var icons = manifest.RootElement.GetProperty("icons");
        var rules = Tactics.Logic.RulesPack.Load([Path.Combine(root, "rules")], Tactics.Logic.ArtManifests.Load(root));
        Assert.Equal(rules.Injuries.Count, icons.EnumerateObject().Count());
        foreach (var injury in rules.Injuries.Values)
        {
            var entry = icons.GetProperty(injury.Id.Value);
            Assert.Equal(injury.Mends, entry.GetProperty("kind").GetString());
            var png = File.ReadAllBytes(Path.Combine(root, "injury_icons", entry.GetProperty("file").GetString()!));
            Assert.Equal(32, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            Assert.Equal(32, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        }
    }

}
