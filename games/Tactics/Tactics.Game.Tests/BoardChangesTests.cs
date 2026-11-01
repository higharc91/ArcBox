using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game.Tests;

/// <summary><see cref="BoardChanges"/>: how the host view tells the TV what the battle did to the map.</summary>
public sealed class BoardChangesTests
{
    private static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return RulesPack.Load([Path.Combine(dir.FullName, "content", "tactics", "base", "rules")]);
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    private static BattleState Start()
    {
        var rules = Pack.Value;
        PartyMember[] party = [.. Enumerable.Range(1, 4).Select(i => new PartyMember(new UnitId($"hero{i}"),
            SheetBuilder.DefaultCharacter($"Hero {i}", "human", "fighter", null, 1, rules), new PlayerControlled(new ProfileId($"pr_{i}"))))];
        return Battle.Start(Encounters.Setup("river_crossing", [.. party], Difficulty.Normal, rules), rules, 5).State;
    }

    [Fact]
    public void An_untouched_map_has_no_changes()
    {
        var changes = BoardChanges.Of(Start());

        Assert.Empty(changes.Zones);
        Assert.Empty(changes.Heights);
        Assert.Empty(changes.Sunk);
    }

    [Fact]
    public void Zones_heights_and_sunk_tiles_come_from_the_state()
    {
        var state = Start() with
        {
            Zones = [new Zone(3, new UnitId("hero1"), "fire", [new Tile(2, 1), new Tile(2, 2)], 3, false, false, [])],
            HeightDelta = ImmutableSortedDictionary<Tile, int>.Empty.Add(new Tile(4, 4), -2),
            Sunk = [new Tile(4, 4)],
        };

        var changes = BoardChanges.Of(state);

        var zone = Assert.Single(changes.Zones);
        Assert.Equal((3, "fire"), (zone.Serial, zone.Kind));
        Assert.Equal([new Tile(2, 1), new Tile(2, 2)], zone.Tiles);
        Assert.Equal(new HeightChange(new Tile(4, 4), -2), Assert.Single(changes.Heights));
        Assert.Equal(new Tile(4, 4), Assert.Single(changes.Sunk));
    }

    [Fact]
    public void They_survive_the_trip_to_the_tv()
    {
        var changes = new BoardChanges([new ZoneView(1, "web", [new Tile(1, 2)])], [new HeightChange(new Tile(3, 4), 2)], [new Tile(5, 6)]);

        var back = JsonSerializer.SerializeToElement(changes, ArcJson.Options).Deserialize<BoardChanges>(ArcJson.Options)!;

        Assert.Equal(new Tile(1, 2), back.Zones[0].Tiles[0]);
        Assert.Equal(new HeightChange(new Tile(3, 4), 2), back.Heights[0]);
        Assert.Equal(new Tile(5, 6), back.Sunk[0]);
    }
}
