namespace Tactics.Logic.Tests;

/// <summary>Things against the story (interactables design §5.6): every flag a thing reads is set by something, and no thing makes a choice.</summary>
public sealed class InteractableLoreTests
{
    private static readonly Lazy<LoreBook> Book = new(() => LoreBook.Load(ContentPaths.ContentBase));
    private static readonly Lazy<RulesPack> Pack = new(() => BaseContent.LogicRules);

    [Fact]
    public void A_flag_nothing_sets_and_a_choices_flag_are_problems()
    {
        InteractableDef[] defs =
        [
            Vault.Def("""{ "id": "a", "kind": "note", "at": [1, 1], "text": ["Hi."], "requires": ["ghost"], "unless": ["story_done"] }"""),
            Vault.Def("""{ "id": "b", "kind": "door", "at": [2, 1], "lock": { "key": "lost_key" } }"""),
            Vault.Def("""{ "id": "c", "kind": "pickup", "at": [3, 1], "sets": ["spared"] }"""),
            Vault.Def("""{ "id": "d", "kind": "note", "at": [4, 1], "text": ["Hi."], "requires": ["c_given"], "sets": ["c_given"] }"""),
        ];
        var problems = InteractableLore.Problems(defs, new HashSet<string> { "story_done", "spared" }, new HashSet<string> { "spared" });
        Assert.Equal([
            "'a' requires 'ghost', which nothing sets, so it's never there.",
            "The key 'lost_key' of 'b' is never given: nothing sets it.",
            "'c' sets 'spared', which a choice of the story sets: it would choose for the party.",
        ], problems);
    }

    [Fact]
    public void The_story_flags_take_in_beats_choices_scenes_and_battle_triggers()
    {
        var arc = Book.Value.ArcFor("ashen_ford")!;
        var flags = InteractableLore.StoryFlags(arc, Book.Value, Pack.Value);
        Assert.Superset(arc.Beats.SelectMany(b => b.Sets).ToHashSet(), flags);
        Assert.Superset(InteractableLore.ChoiceFlags(arc), flags);
        Assert.NotEmpty(InteractableLore.ChoiceFlags(arc));
    }

    /// <summary>The base pack's things against its story: the loader can't read lore, so this is where it's checked, as beat spots are.</summary>
    [Fact]
    public void The_base_packs_things_agree_with_its_story()
    {
        foreach (var (region, things) in Pack.Value.Interactables)
        {
            if (Book.Value.ArcFor(region) is not { } arc)
                continue;
            Assert.Empty(InteractableLore.Problems(things, InteractableLore.StoryFlags(arc, Book.Value, Pack.Value), InteractableLore.ChoiceFlags(arc)));
        }
    }
}
