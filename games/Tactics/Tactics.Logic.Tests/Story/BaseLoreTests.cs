namespace Tactics.Logic.Tests;

/// <summary>The base pack's arcs, as <see cref="LoreBook"/> reads them, against its rules.</summary>
public sealed class BaseLoreTests
{
    /// <summary>The game's <c>Manifest.MaxPlayers</c>: every seat has a member, and every member fights.</summary>
    private const int Seats = 6;

    private static readonly Lazy<LoreBook> Book = new(() => LoreBook.Load(ContentPaths.ContentBase));

    private static RulesPack Rules => BasePackTests.Rules;

    private static IEnumerable<(Arc Arc, ArcBeat Beat)> Battles()
        => Book.Value.Arcs.SelectMany(arc => arc.Beats.Where(b => b.Kind == ArcBeat.Battle).Select(b => (arc, b)));

    [Fact]
    public void Every_chapter_has_its_arc_in_order()
        => Assert.Equal([1, 2, 3, 4, 5], Book.Value.Arcs.Select(a => a.Chapter));

    [Fact]
    public void The_book_reads_a_beat_as_it_is_written()
    {
        var beat = Book.Value.ArcFor("ashen_ford")!.Beats.Single(b => b.Id == "ch1_river_crossing");

        Assert.Equal(("battle", "ford_bridge", "river_crossing"), (beat.Kind, beat.Place, beat.Encounter));
        Assert.Equal(["ch1_skarrow_spared"], beat.Requires);
        Assert.Empty(beat.Sets);
        Assert.Empty(beat.Choices);

        var choice = Book.Value.ArcFor("ashen_ford")!.Beats.Single(b => b.Id == "ch1_the_ford_choice");
        Assert.Equal(["ch1_force", "ch1_parley"], choice.Choices.Select(c => c.Id));
        Assert.Equal(["ch1_skarrow_slain"], choice.Choices[0].Sets);
        Assert.Equal("We'll clear the ford by force.", choice.Choices[0].Label);
        Assert.Null(Book.Value.ArcFor("frostpeak")!.Beats.Single(b => b.Id == "ch4_pip_remembers").Place);
    }

    [Fact]
    public void Every_arc_battle_names_a_known_encounter()
    {
        Assert.NotEmpty(Battles());
        foreach (var (_, beat) in Battles())
            Assert.True(beat.Encounter is { } id && Rules.Encounters.ContainsKey(id), $"The battle beat '{beat.Id}' names no known encounter.");
    }

    /// <summary>The party is everyone at the table, so an arc's fight has to take a full table.</summary>
    [Fact]
    public void Every_arc_battle_takes_a_full_party()
    {
        foreach (var (_, beat) in Battles())
            Assert.True(Rules.Encounters[beat.Encounter!].Party.Max >= Seats, $"The encounter '{beat.Encounter}' of '{beat.Id}' takes at most {Rules.Encounters[beat.Encounter!].Party.Max}.");
    }

    [Fact]
    public void Every_battle_beat_in_chapter_1_has_a_stamp()
    {
        var arc = Book.Value.Arcs.Single(a => a.Chapter == 1);
        var region = Rules.RegionFor(arc.Region)!;
        var battles = arc.Beats.Where(b => b.Kind == ArcBeat.Battle).ToList();

        Assert.Equal(2, battles.Count);
        foreach (var beat in battles)
            Assert.NotNull(BattleStage.Of(region, Rules.MapFor(Rules.Encounters[beat.Encounter!].Map)!));
    }

    [Fact]
    public void An_empty_book_has_no_beats()
    {
        Assert.Empty(LoreBook.Empty.Arcs);
        Assert.Null(LoreBook.Empty.ArcFor("ashen_ford"));
    }

    [Fact]
    public void A_pack_whose_lore_lists_no_arcs_has_none()
    {
        var pack = Directory.CreateTempSubdirectory("tactics-lore-test-");
        try
        {
            Directory.CreateDirectory(Path.Combine(pack.FullName, "lore"));
            File.WriteAllText(Path.Combine(pack.FullName, "lore", "pack.json"), """{ "id": "base", "loreVersion": 1, "files": ["world.json"] }""");
            File.WriteAllText(Path.Combine(pack.FullName, "lore", "arcs.json"), "not even json");

            Assert.Empty(LoreBook.Load(pack.FullName).Arcs);
        }
        finally
        {
            pack.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("""{ "arcs": [{ "id": "a", "chapter": 1, "region": "r", "beats": [] }, { "id": "b", "chapter": 2, "region": "r", "beats": [] }] }""", "the region 'r' has more than one arc")]
    [InlineData("""{ "arcs": [{ "id": "a", "chapter": 1, "region": "r", "beats": [{ "id": "x", "kind": "story" }, { "id": "x", "kind": "camp" }] }] }""", "the arc 'a' lists the beat 'x' twice")]
    [InlineData("""{ "arcs": [{ "id": "a", "region": "r", "beats": [] }] }""", "can't be read as lore")]
    [InlineData("""{ "arcs": [ """, "can't be read as lore")]
    public void Lore_that_cant_be_played_is_refused_naming_the_file(string arcs, string problem)
    {
        var pack = Directory.CreateTempSubdirectory("tactics-lore-test-");
        try
        {
            Directory.CreateDirectory(Path.Combine(pack.FullName, "lore"));
            File.WriteAllText(Path.Combine(pack.FullName, "lore", "pack.json"), """{ "id": "base", "loreVersion": 1, "files": ["arcs.json"] }""");
            File.WriteAllText(Path.Combine(pack.FullName, "lore", "arcs.json"), arcs);

            var error = Assert.Throws<InvalidDataException>(() => LoreBook.Load(pack.FullName));

            Assert.Contains("arcs.json", error.Message, StringComparison.Ordinal);
            Assert.Contains(problem, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            pack.Delete(recursive: true);
        }
    }
}
