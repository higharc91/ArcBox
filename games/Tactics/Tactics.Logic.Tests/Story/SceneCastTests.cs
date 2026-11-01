using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary><see cref="SceneCast"/>: who stands in the world while a scene plays, and on which tiles (the user's answer of 2026-11-01, item 21).</summary>
public sealed class SceneCastTests
{
    private static readonly Lazy<LoreBook> Book = new(() => LoreBook.Load(ContentPaths.ContentBase));

    private static Terrain TerrainOf(string ascii) => new(AsciiMap.Parse(ascii), ImmutableSortedDictionary<Tile, int>.Empty, ImmutableSortedSet<Tile>.Empty);

    private static Terrain Open(int size) => TerrainOf(string.Join("\n", Enumerable.Repeat(string.Join(" ", Enumerable.Repeat("0.", size)), size)));

    private static SceneLine Line(string speaker) => new($"l{speaker}", speaker, "…", "neutral");

    private static SceneNode Node(string id, params string[] speakers) => new(id, [.. speakers.Select(Line)], [], [], false, null, true);

    [Fact]
    public void The_cast_is_every_speaker_but_the_narrator_once_in_the_order_they_first_speak()
    {
        var scene = Book.Value.Scenes.Single(s => s.Id == "ch1_dawn");
        Assert.Equal(["mother_skarrow", "dagna_holt"], SceneCast.Speakers(scene));

        var made = scene with { Nodes = [Node("a", "narrator", "bree", "narrator", "halden"), Node("b", "halden", "pip", "bree")] };
        Assert.Equal(["bree", "halden", "pip"], SceneCast.Speakers(made));
        Assert.Empty(SceneCast.Speakers(scene with { Nodes = [Node("a", "narrator")] }));
    }

    [Fact]
    public void Every_speaker_of_every_base_scene_is_in_the_lore_with_a_preset()
    {
        foreach (var scene in Book.Value.Scenes)
            foreach (var speaker in SceneCast.Speakers(scene))
                Assert.False(string.IsNullOrEmpty(Book.Value.Character(speaker)?.Preset), $"{scene.Id}: {speaker} has no character to stand in the world.");
    }

    [Fact]
    public void The_first_speaker_stands_on_the_spot_and_the_rest_round_it_ahead_of_the_party()
    {
        var ground = Open(12);
        var (spot, leader, facing) = (new Tile(6, 5), new Tile(4, 5), new Tile(5, 5));     // two steps away, facing it

        var tiles = SceneCast.Place(ground, spot, leader, facing, [leader], 4);

        Assert.Equal(4, tiles.Length);
        Assert.Equal(spot, tiles[0]);
        Assert.Equal(tiles.Length, tiles.Distinct().Count());
        foreach (var t in tiles[1..])
            Assert.Equal(1, Math.Max(Math.Abs(t.X - spot.X), Math.Abs(t.Y - spot.Y)));        // a step from the spot
        Assert.All(tiles, t => Assert.True(t.X > leader.X, $"{t} is behind the leader, where the followers gather."));
        Assert.Equal(tiles, SceneCast.Place(ground, spot, leader, facing, [leader], 4));      // the same every time
    }

    [Fact]
    public void Nobody_stands_on_the_party_a_wall_or_a_tile_that_isnt_allowed()
    {
        var ground = TerrainOf("""
            0. 0. 0. 0. 0. 0.
            0. 0# 0# 0. 0. 0.
            0. 0# 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0.
            """);
        var spot = new Tile(2, 2);
        HashSet<Tile> party = [new(3, 2), new(4, 2), new(5, 2)];
        var refused = new Tile(2, 3);

        var tiles = SceneCast.Place(ground, spot, new Tile(3, 2), new Tile(2, 2), party, 30, t => t != refused);

        Assert.NotEmpty(tiles);
        Assert.All(tiles, t => Assert.True(ground.Walkable(t) && !party.Contains(t) && t != refused, $"{t}"));
        Assert.Equal(tiles.Length, tiles.Distinct().Count());
    }

    [Fact]
    public void The_spot_under_the_leader_puts_the_first_speaker_beside_them()
    {
        var ground = Open(9);
        var at = new Tile(4, 4);

        var tiles = SceneCast.Place(ground, at, at, new Tile(5, 4), [at], 2);

        Assert.DoesNotContain(at, tiles);
        Assert.Equal(new Tile(5, 4), tiles[0]);                     // the step they face
        Assert.All(tiles, t => Assert.True(t.X >= at.X, $"{t} is behind the leader."));
    }

    [Fact]
    public void A_cast_too_big_for_the_ground_shows_as_many_as_fit()
    {
        var ground = TerrainOf("""
            0# 0# 0# 0#
            0# 0. 0. 0#
            0# 0# 0# 0#
            """);

        var tiles = SceneCast.Place(ground, new Tile(1, 1), new Tile(2, 1), new Tile(1, 1), [new Tile(2, 1)], 5);

        Assert.Equal([new Tile(1, 1)], tiles);
        Assert.Empty(SceneCast.Place(ground, new Tile(1, 1), new Tile(2, 1), new Tile(1, 1), [], 0));
    }

    [Fact]
    public void The_cast_stands_on_ground_the_party_could_walk_to_from_the_spot()
    {
        // The spot at a cliff's foot: the ground on top, five levels up with no way up, is never anyone's, however near.
        var ground = TerrainOf("""
            0. 0. 5. 5. 5.
            0. 0. 5. 5. 5.
            0. 0. 5. 5. 5.
            """);

        var tiles = SceneCast.Place(ground, new Tile(1, 1), new Tile(0, 1), new Tile(1, 1), [new Tile(0, 1)], 10);

        Assert.All(tiles, t => Assert.True(t.X <= 1, $"{t} is up the cliff."));
        Assert.Equal(5, tiles.Length);
    }
}
