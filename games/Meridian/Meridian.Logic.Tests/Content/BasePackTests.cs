namespace Meridian.Logic.Tests.Content;

public sealed class BasePackTests
{
    private static RulesPack Pack => BasePack.Pack;

    [Fact]
    public void The_shipped_pack_loads_with_no_errors()
        => Assert.NotNull(Pack);

    [Fact]
    public void Has_the_counts_the_plan_promises()
    {
        Assert.Equal(45, Pack.Tiles.Sum(t => t.Copies));
        Assert.Equal(5, Pack.StartTiles.Count);
        Assert.Equal(8, Pack.Crew.Count);
        Assert.Equal(12, Pack.CardsOf(CardKind.Incident).Count());
        Assert.Equal(8, Pack.CardsOf(CardKind.Salvage).Count());
        Assert.Equal(12, Pack.CardsOf(CardKind.Anomaly).Count());
    }

    [Fact]
    public void Symbols_match_the_card_counts()
    {
        int Count(CardKind kind) => Pack.Tiles.Where(t => t.Symbol == kind).Sum(t => t.Copies);
        Assert.Equal(12, Count(CardKind.Incident));
        Assert.Equal(10, Count(CardKind.Salvage));
        Assert.Equal(12, Count(CardKind.Anomaly));
    }

    [Fact]
    public void Every_crew_member_starts_on_the_same_total()
        => Assert.All(Pack.Crew, c => Assert.Equal(14, Traits.All.Sum(t => c.Tracks[t].ValueAt(c.Tracks[t].Start))));

    [Fact]
    public void Two_crew_lean_towards_each_trait()
    {
        foreach (var trait in Traits.All)
        {
            var leaning = Pack.Crew.Count(c => Traits.All.All(o => o == trait || c.Tracks[trait].ValueAt(4) > c.Tracks[o].ValueAt(4)));
            Assert.True(leaning >= 2, $"{trait}: {leaning}");
        }
    }

    [Fact]
    public void Nobody_starts_unable_to_move()
        => Assert.All(Pack.Crew, c => Assert.True(c.Tracks.Agility.Values.Min() >= 1, c.Id.Value));

    [Fact]
    public void Every_deck_can_grow_in_every_direction()
    {
        // Each deck has tiles with three or more hatches, so its frontier doesn't close early.
        foreach (var deck in Enum.GetValues<DeckId>())
        {
            var open = Pack.Tiles.Where(t => t.Decks.Contains(deck) && t.Hatches.Count >= 3).Sum(t => t.Copies);
            Assert.True(open >= 5, $"{deck}: {open}");
        }
    }

    [Fact]
    public void Incidents_check_every_trait_and_include_choices()
    {
        var incidents = Pack.CardsOf(CardKind.Incident).ToList();
        Assert.All(Traits.All, t => Assert.True(incidents.Count(c => c.Check?.Trait == t) >= 2, t.ToString()));
        Assert.True(incidents.Count(c => c.Choice is not null) >= 2);
    }

    [Fact]
    public void Salvage_has_passive_single_use_and_reusable_items()
    {
        var items = Pack.CardsOf(CardKind.Salvage).Select(c => c.Item!).ToList();
        Assert.Contains(items, i => !i.Passive.IsEmpty);
        Assert.Contains(items, i => i.Use is { Single: true });
        Assert.Contains(items, i => i.Use is { Single: false });
    }
}
