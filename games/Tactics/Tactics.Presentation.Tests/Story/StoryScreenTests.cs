using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Story;
using Tactics.Presentation.Tests.Board;
using static Tactics.Presentation.Tests.Story.StoryFeed;
using States = Tactics.Game.TacticsGame.StoryStates;

namespace Tactics.Presentation.Tests.Story;

/// <summary><see cref="StoryScreen"/>: what the TV shows of a scene, worded and laid out from mockups 02 and 03.</summary>
public sealed class StoryScreenTests
{
    private static StoryScreen Screen(StoryView view, string? clock = "0:14") => StoryScreen.Of(view, Art, clock);

    private static string Words(StoryCaption caption) => string.Join(" ", caption.Rows.Select(r => string.Concat(r.Select(run => run.Text))));

    // ---- The caption ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_narrator_has_no_portrait()
    {
        var caption = Screen(Showing(1, Line("ch1_mayor_open_1"))).Caption!;

        Assert.Null(caption.Portrait);
        Assert.Null(caption.Speaker);
        Assert.True(caption.Narrator);
        Assert.StartsWith("Emberwick, at the Ashen Ford.", Words(caption));     // and no quotes: nobody's speaking them
    }

    [Fact]
    public void A_speakers_mood_picks_their_portrait()
    {
        var happy = Screen(Showing(1, Line("ch1_mayor_halden_1"))).Caption!;     // Halden, happy
        var sad = Screen(Showing(1, Line("ch1_mayor_halden_4"))).Caption!;       // Halden, sad

        Assert.Equal("portraits/mayor_happy.png", happy.Portrait!.Path);
        Assert.Equal("portraits/mayor_sad.png", sad.Portrait!.Path);
        Assert.Equal("Halden Varrow", happy.Speaker);
        Assert.StartsWith("“Friends.", Words(happy));
        Assert.EndsWith("”", Words(happy));
    }

    [Theory]
    [InlineData("ally", UnitTone.Party)]
    [InlineData("villain", UnitTone.Enemy)]
    [InlineData("rival", UnitTone.Neutral)]
    [InlineData("npc", UnitTone.Neutral)]
    public void A_speakers_role_picks_their_backdrop(string role, UnitTone tone)
    {
        var face = Art.Speaker("mayor", "idle", role)!;
        var backdrop = Art.Portrait("characters.mayor", tone).Backdrop;

        Assert.Equal(backdrop, face.Backdrop);
    }

    [Fact]
    public void A_mood_the_preset_lacks_falls_back_to_idle()
        => Assert.Equal("portraits/mayor_idle.png", Art.Speaker("mayor", "smug", "ally")!.Path);

    [Fact]
    public void A_preset_the_pack_lacks_has_no_face()
        => Assert.Null(Art.Speaker("dragon_king", "idle", "villain"));

    [Fact]
    public void Every_speaker_in_the_lore_has_a_face_in_every_mood_they_speak_in()
    {
        var speakers = 0;
        foreach (var (_, line) in AllLines())
        {
            var view = LineOf(line);
            var face = Art.Speaker(view.Portrait, view.Mood, view.Role);
            if (line.Speaker == "narrator")
            {
                Assert.Null(face);
                continue;
            }
            Assert.True(face is not null, $"{line.Id}: {line.Speaker} has no portrait");
            Assert.Equal($"portraits/{view.Portrait}_{view.Mood}.png", face.Path);     // the mood's own face, not the idle fallback
            Assert.True(File.Exists(Path.Combine(Pack.Root, face.Path)), face.Path);
            Assert.True(File.Exists(Path.Combine(Pack.Root, face.Backdrop)), face.Backdrop);
            speakers++;
        }
        Assert.Equal(900 - AllLines().Count(l => l.Line.Speaker == "narrator"), speakers);
    }

    [Fact]
    public void Apostrophes_are_typeset()
        => Assert.Contains("I’d shake every hand", Words(Screen(Showing(1, Line("ch1_mayor_halden_1"))).Caption!));

    [Theory]
    [InlineData("'tis the season.", "’tis the season.")]
    [InlineData("Lock 'em up.", "Lock ’em up.")]
    [InlineData("Back in '90.", "Back in ’90.")]
    [InlineData("She said ('not the ford') twice.", "She said (‘not the ford’) twice.")]
    [InlineData("\"'Twas me,\" he said.", "\"’Twas me,\" he said.")]
    [InlineData("The Skarrows' bridge.", "The Skarrows’ bridge.")]
    [InlineData("'Ember' they call it.", "‘Ember’ they call it.")]
    public void Apostrophes_and_quotes_are_set_by_what_they_do(string text, string set)
        => Assert.Equal(set, StoryScreen.Typeset(text));

    [Fact]
    public void Someone_the_lore_doesnt_list_speaks_under_their_id_not_as_the_narrator()
    {
        var caption = Screen(Showing(1, new StoryLineView("x_1", "old_ferryman", null, null, "idle", "Mind the step."))).Caption!;

        Assert.False(caption.Narrator);
        Assert.Equal("Old Ferryman", caption.Speaker);
        Assert.Null(caption.Portrait);
        Assert.Equal("“Mind the step.”", Words(caption));
    }

    [Fact]
    public void A_quote_inside_a_line_opens_and_closes()
        => Assert.Equal("‘Ember’ they call it.",
            Words(Screen(Showing(1, Unvoiced(text: "'Ember' they call it."))).Caption!));

    [Fact]
    public void A_long_line_wraps_on_words_into_three_rows()
    {
        var text = string.Join(" ", Enumerable.Repeat("Emberwick", 18));
        var rows = Screen(Showing(1, Unvoiced(text: text))).Caption!.Rows;

        Assert.Equal(3, rows.Length);
        Assert.All(rows, r => Assert.True(string.Concat(r.Select(run => run.Text)).Length <= StoryScreen.CaptionColumns));
        Assert.All(rows, r => Assert.DoesNotContain("  ", string.Concat(r.Select(run => run.Text))));
    }

    [Fact]
    public void Every_line_in_the_lore_fits_the_caption()
    {
        var lines = AllLines().ToArray();
        Assert.Equal(900, lines.Length);
        foreach (var (_, line) in lines)
        {
            var caption = Screen(Showing(1, LineOf(line))).Caption!;
            var rows = caption.Rows.Select(r => string.Concat(r.Select(run => run.Text))).ToArray();
            Assert.True(rows.Length <= StoryScreen.CaptionRows, $"{line.Id} takes {rows.Length} rows");
            Assert.All(rows, row => Assert.True(row.Length <= StoryScreen.CaptionColumns, $"{line.Id}: '{row}'"));
            Assert.Equal(Plain(line.Text), Plain(string.Join(" ", rows)));     // nothing lost, nothing added but the quotes
        }
    }

    private static string Plain(string text) => text.Replace("“", "").Replace("”", "").Replace("’", "'").Replace("‘", "'");

    [Fact]
    public void A_question_line_lights_its_question()
    {
        var line = Line("ch1_kindler_open_7");
        var asking = Screen(Showing(4, line, States.Asking, Ask(true, 0, 2, [Option("a", "Yes.")], deadline: T0))).Caption!;
        var plain = Screen(Showing(4, line)).Caption!;

        var strong = string.Join(" ", asking.Rows.SelectMany(r => r).Where(r => r.Strong).Select(r => r.Text.Trim()));
        Assert.EndsWith("?”", strong);
        Assert.DoesNotContain(".", strong.TrimEnd('”', '?'));       // only the last sentence, the question
        Assert.All(plain.Rows.SelectMany(r => r), run => Assert.False(run.Strong));
    }

    [Theory]
    [InlineData("Will you help?", "“Will you help?”")]
    [InlineData("The ford is the only road east. Will you clear them out, or talk to their chief?", "Will you clear them out, or talk to their chief?”")]
    [InlineData("Who? Why? What do you want?", "What do you want?”")]
    [InlineData("We go at dawn.", "")]
    public void The_question_is_the_last_sentence(string text, string strong)
    {
        var caption = Screen(Showing(4, Unvoiced(text: text, speaker: "halden_varrow"), States.Asking, Ask(true, 0, 2, [Option("a", "Yes.")]))).Caption!;

        Assert.Equal(strong, string.Join(" ", caption.Rows.SelectMany(r => r).Where(run => run.Strong).Select(run => run.Text.Trim())));
    }

    // ---- The vote --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_vote_panel_words_the_tally()
    {
        var ask = Ask(true, 4, 4, [Option("force", "We'll clear the ford by force.", "Jess", "Marcus"), Option("parley", "We'll go and parley with the chief.", "Priya", "Sam")],
            tie: true, decider: "Sam", deadline: T0.AddSeconds(10));

        var vote = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.TieBreak, ask)).Vote!;

        Assert.Equal("4 of 4 voted · 2–2 tie, Sam decides", vote.Tally);
        Assert.Equal("Tie-break", vote.Heading);
        Assert.Equal(["A", "B"], vote.Rows.Select(r => r.Letter));
        Assert.Equal(["Jess", "Marcus"], vote.Rows[0].Voters.Select(v => v.Name));
    }

    // The user's answer 14: a voter's pip is their colour, with their initial in whichever of black and white reads on it.
    [Fact]
    public void A_voters_pip_is_their_colour_with_an_initial_that_reads_on_it()
    {
        var ask = Ask(true, 3, 4, [Option("force", "We'll clear the ford by force.", "jess", "Marcus"), Option("parley", "We'll parley.", "Priya")],
            deadline: T0.AddSeconds(30)) with
        {
            Colors = new Dictionary<string, string> { ["jess"] = "#7ccbff", ["Marcus"] = "#1a1e3a", ["Sam"] = "#ffd23f" }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        };

        var rows = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask)).Vote!.Rows;

        var (jess, marcus) = (rows[0].Voters[0], rows[0].Voters[1]);
        Assert.Equal(new VoterPip("jess", "J", Rgb.Hex("#7ccbff"), PlayerInk.Dark), jess);
        Assert.Equal(new VoterPip("Marcus", "M", Rgb.Hex("#1a1e3a"), PlayerInk.Light), marcus);    // a dark colour gets white
        Assert.Equal(new VoterPip("Priya", "P", PlayerInk.PipFallback, PlayerInk.Dark), rows[1].Voters[0]);   // no colour: the gold
    }

    [Fact]
    public void Without_colours_every_pip_is_the_panels_gold()
    {
        var ask = Ask(true, 2, 2, [Option("a", "Yes.", "Jess", ""), Option("b", "No.")], deadline: T0.AddSeconds(30));

        var pips = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask)).Vote!.Rows[0].Voters;

        Assert.All(pips, p => Assert.Equal((PlayerInk.PipFallback, PlayerInk.Dark), (p.Fill, p.Ink)));
        Assert.Equal(["J", "?"], pips.Select(p => p.Initial));
    }

    // A colour changing under a vote is a new row, so the TV redraws the pip rather than keep the old one.
    [Fact]
    public void A_pips_colour_tells_two_rows_apart()
    {
        var ask = Ask(true, 1, 2, [Option("a", "Yes.", "Jess")], deadline: T0.AddSeconds(30));
        StoryAskView Coloured(string hex) => ask with { Colors = new Dictionary<string, string> { ["Jess"] = hex }.ToImmutableSortedDictionary(StringComparer.Ordinal) };

        var gold = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, Coloured("#ffd23f"))).Vote!;
        var sky = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, Coloured("#7ccbff"))).Vote!;

        Assert.NotEqual(gold.Rows[0], sky.Rows[0]);
        Assert.NotEqual(gold, sky);
        Assert.Equal(gold, Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, Coloured("#ffd23f"))).Vote!);
    }

    [Fact]
    public void An_open_vote_counts_who_has_voted()
    {
        var ask = Ask(true, 1, 3, [Option("a", "Yes.", "Jess"), Option("b", "No.")], deadline: T0.AddSeconds(45));

        var vote = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask)).Vote!;

        Assert.Equal("Party vote", vote.Heading);
        Assert.Equal("1 of 3 voted", vote.Tally);
        Assert.Equal("0:14", vote.Clock);
    }

    [Fact]
    public void A_three_way_tie_with_nobody_to_break_it()
    {
        var ask = Ask(true, 3, 3, [Option("a", "A.", "Jess"), Option("b", "B.", "Sam"), Option("c", "C.", "Dave")], tie: true, deadline: T0.AddSeconds(10));

        Assert.Equal("3 of 3 voted · 1–1–1 tie", Screen(Showing(4, Line("ch1_mayor_halden_4"), States.TieBreak, ask)).Vote!.Tally);
    }

    [Fact]
    public void The_leaders_choice_says_who_chooses()
    {
        var ask = Ask(false, 0, 1, [Option("a", "Yes."), Option("b", "No.")], away: ["Dave"], decider: "Sam", deadline: T0.AddSeconds(30));

        var vote = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask)).Vote!;

        Assert.Equal("Leader's choice", vote.Heading);
        Assert.Equal("Sam chooses for the party", vote.Tally);
        Assert.Null(vote.Away);                                            // nobody votes on a choice, so nobody's missing it
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "Dave" }, "Dave is away and doesn’t vote")]
    [InlineData(new[] { "Dave", "Jess" }, "Dave and Jess are away and don’t vote")]
    [InlineData(new[] { "Dave", "Jess", "Sam" }, "Dave, Jess and Sam are away and don’t vote")]
    public void The_vote_panel_names_who_is_away(string[] away, string? words)
    {
        var ask = Ask(true, 0, 2, [Option("a", "Yes.")], away: [.. away], deadline: T0.AddSeconds(45));

        Assert.Equal(words, Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask)).Vote!.Away);
    }

    [Fact]
    public void A_vote_waiting_for_a_phone_says_so_and_has_no_clock()
    {
        var ask = Ask(true, 0, 0, [Option("a", "Yes.")], away: ["Dave"]);

        var vote = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask), clock: null).Vote!;

        Assert.Null(vote.Clock);
        Assert.Equal("Waiting for a phone to come back", vote.Tally);
    }

    [Fact]
    public void A_check_option_shows_its_dc()
    {
        var ask = Ask(true, 0, 4, [Option("force", "We'll clear the ford by force."), Check("worth", "First, what's it worth to you?", "persuasion", 15),
            Check("hand", "Let me look at the lock.", "sleight_of_hand", 12)], deadline: T0.AddSeconds(45));

        var rows = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask)).Vote!.Rows;

        Assert.Null(rows[0].Detail);
        Assert.Equal("Persuasion check · DC 15", rows[1].Detail);
        Assert.Equal("Sleight of Hand check · DC 12", rows[2].Detail);
        Assert.Equal(["“First, what’s it worth to you?”"], rows[1].Label);
    }

    [Fact]
    public void Every_option_in_the_lore_fits_its_row()
    {
        foreach (var option in Lore.Scenes.SelectMany(s => s.Nodes).SelectMany(n => n.Options))
        {
            var label = StoryScreen.Label(option.Label);
            Assert.True(label.Length <= StoryScreen.LabelRows, $"{option.Id}: {label.Length} rows");
            Assert.All(label, row => Assert.True(row.Length <= StoryScreen.LabelColumns, $"{option.Id}: '{row}'"));
        }
    }

    /// <summary>
    /// The most a question offers in the lore, Maren's <c>ask_me</c> with all eleven of its options (more than any party is offered at once,
    /// since some exclude others), fits the panel's height: smaller, and every label within its rows.
    /// </summary>
    [Fact]
    public void Eleven_long_options_fit_the_panel()
    {
        var node = Lore.Scenes.Single(s => s.Id == "ch3_maren_parley").Node("ask_me")!;
        var ask = Ask(true, 0, 4, [.. node.Options.Select(o => o.Check is { } c ? Check(o.Id, o.Label, c.Skill, c.Dc) : Option(o.Id, o.Label))],
            deadline: T0.AddSeconds(45));

        var vote = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask)).Vote!;

        Assert.Equal(11, vote.Rows.Length);
        Assert.True(vote.Height <= VoteLayout.MaxHeight, $"{vote.Height} tall");
        Assert.True(vote.Scale < 1);
        Assert.All(vote.Rows, r => Assert.True(r.Label.Length <= StoryScreen.LabelRows));
        Assert.All(vote.Rows.SelectMany(r => r.Label), row => Assert.True(row.Length <= VoteLayout.Columns(vote.Scale), row));
    }

    [Fact]
    public void A_few_options_keep_their_full_size()
    {
        var ask = Ask(true, 0, 4, [Option("a", "We'll clear the ford by force."), Option("b", "We'll go and parley with the chief."),
            Check("c", "First, what's it worth to you?", "persuasion", 15)], deadline: T0.AddSeconds(45));

        var vote = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask)).Vote!;

        Assert.Equal(1, vote.Scale);
        Assert.Equal(VoteLayout.Chrome + vote.Rows.Sum(r => VoteLayout.RowHeight(r, 1)), vote.Height);
    }

    [Fact]
    public void Every_question_in_the_lore_fits_the_panel()
    {
        foreach (var node in Lore.Scenes.SelectMany(s => s.Nodes).Where(n => !n.Options.IsEmpty))
        {
            var ask = Ask(true, 0, 4, [.. node.Options.Select(o => o.Check is { } c ? Check(o.Id, o.Label, c.Skill, c.Dc) : Option(o.Id, o.Label))],
                deadline: T0.AddSeconds(45));
            Assert.True(Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask)).Vote!.Height <= VoteLayout.MaxHeight, node.Id);
        }
    }

    [Fact]
    public void Eleven_options_letter_through_k()
    {
        var ask = Ask(true, 0, 4, [.. Enumerable.Range(0, 11).Select(i => Option($"o{i}", "Go."))], deadline: T0.AddSeconds(45));

        Assert.Equal("ABCDEFGHIJK", string.Concat(Screen(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask)).Vote!.Rows.Select(r => r.Letter)));
    }

    // ---- The check -----------------------------------------------------------------------------------------------------------

    private static StoryCheckView CheckOf(string? chosen = null, RollResult? roll = null, bool tie = false, string? decider = null, DateTimeOffset? deadline = null)
        => new("persuasion", 15, "First, what's it worth to you?",
            [Card("pr_kara", "Kara", "Jess", 1), Card("pr_aldric", "Aldric", "Dave", 3, "WIS 16", here: false), Card("pr_wren", "Wren", "Priya", 5, "Proficient · CHA 14", votes: 2),
                Card("pr_ilyra", "Ilyra", "Sam", 2, "CHA 14", votes: 2), Card("pr_tamsin", "Tamsin", "Marcus", -1, "CHA 8")],
            chosen, roll, tie, decider, deadline);

    [Fact]
    public void The_check_reads_as_in_mockup_03()
    {
        var check = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.PickRoller, check: CheckOf(deadline: T0.AddSeconds(20)))).Check!;

        Assert.Equal("Skill check", check.Heading);
        Assert.Equal("Persuasion", check.Skill);
        Assert.Equal("DC 15", check.Dc);
        Assert.Equal("“First, what’s it worth to you?”", check.Title);
        Assert.Equal(["+1", "+3", "+5", "+2", "−1"], check.Cards.Select(c => c.Bonus));
        Assert.Equal("Dave away", check.Cards[1].Tag);
        Assert.Equal("Proficient · CHA 14", check.Cards[2].Note);
        Assert.Equal("Vote for who rolls · 4 voted", check.Footer);
        Assert.Equal("0:14", check.Clock);
        Assert.True(Screen(Showing(4, null, States.PickRoller, check: CheckOf())).Dimmed);
    }

    [Theory]
    [InlineData("Wren", "Priya", "Rogue", "Priya · Rogue")]
    [InlineData("Bo", "Bo", "Cleric", "Cleric")]          // a bot names its character after itself: the name isn't said twice
    [InlineData("Wren", "Priya", null, "Priya")]
    public void A_card_says_who_plays_it_as_the_phone_does(string name, string player, string? cls, string who)
        => Assert.Equal(who, CheckCard.WhoOf(name, player, cls));

    [Fact]
    public void A_card_carries_its_class_and_its_votes_in_words()
    {
        var view = CheckOf() with { Cards = [Card("pr_wren", "Wren", "Priya", 5, votes: 2) with { Class = "Rogue" }, Card("pr_bo", "Bo", "Bo", 1, votes: 1) with { Class = "Cleric" },
            Card("pr_kara", "Kara", "Jess", 0)] };

        var cards = Screen(Showing(4, null, States.PickRoller, check: view)).Check!.Cards;

        Assert.Equal(["Priya · Rogue", "Cleric", "Jess"], cards.Select(c => c.Who));
        Assert.Equal(["2 votes", "1 vote", ""], cards.Select(c => c.VotesText));
    }

    // The user's answer 14: each roller's card has an edge in their player's colour; one the server sent none for (or a bad one) has none.
    [Fact]
    public void A_card_has_its_players_colour_for_its_edge()
    {
        var view = CheckOf() with { Cards = [Card("pr_wren", "Wren", "Priya", 5) with { Color = "#FFAB94" }, Card("pr_bo", "Bo", "Bo", 1),
            Card("pr_kara", "Kara", "Jess", 0) with { Color = "coral" }] };

        var cards = Screen(Showing(4, null, States.PickRoller, check: view)).Check!.Cards;

        Assert.Equal([Rgb.Hex("#ffab94"), null, null], cards.Select(c => c.Colour));
        Assert.NotEqual(cards[0], cards[0] with { Colour = Rgb.Hex("#7ccbff") });    // a new colour redraws the card
    }

    [Fact]
    public void The_roll_says_who_rolls_then_who_rolled()
    {
        var roll = Rolls.Flat(14, 5, 15);

        Assert.Equal("Wren rolls…", StoryScreen.Reveal(roll, "Wren", 0).Line);
        Assert.Equal("Wren rolled for the party", StoryScreen.Reveal(roll, "Wren", StoryScreen.Tumble).Line);
    }

    [Fact]
    public void A_check_has_the_screen_to_itself_with_no_caption()
    {
        var picking = Screen(Showing(4, Line("ch1_mayor_halden_4"), States.PickRoller, check: CheckOf(deadline: T0.AddSeconds(20))));

        Assert.Null(picking.Caption);
        Assert.NotNull(picking.Check);
        Assert.NotNull(Screen(Showing(4, Line("ch1_mayor_halden_4"))).Caption);
    }

    [Fact]
    public void A_tie_over_who_rolls_says_who_decides()
        => Assert.Equal("2–2 tie, Sam decides",
            Screen(Showing(4, Line("ch1_mayor_halden_4"), States.TieBreak, check: CheckOf(tie: true, decider: "Sam"))).Check!.Footer);

    [Fact]
    public void The_roll_reads_as_in_mockup_03()
    {
        var check = Screen(Showing(4, null, States.Rolled, check: CheckOf("pr_wren", Rolls.Flat(14, 5, 15)))).Check!;

        Assert.Equal("14 + 5 = 19 vs DC 15", check.Roll!.Sum);
        Assert.Equal("Success", check.Roll.Verdict);
        Assert.True(check.Roll.Success);
        Assert.Equal("Wren", check.Roll.Roller);
        Assert.Equal("Rolls for the party", check.Cards[2].Tag);
        Assert.True(check.Cards[2].Chosen);
        Assert.Null(check.Footer);
        Assert.Null(check.Clock);
    }

    [Fact]
    public void A_failed_roll_with_a_minus()
    {
        var roll = Screen(Showing(4, null, States.Rolled, check: CheckOf("pr_tamsin", Rolls.Flat(9, -1, 15)))).Check!.Roll!;

        Assert.Equal("9 − 1 = 8 vs DC 15", roll.Sum);
        Assert.Equal("Failure", roll.Verdict);
        Assert.False(roll.Success);
    }

    [Fact]
    public void A_tumbling_die_shows_other_faces_before_it_lands()
    {
        var roll = Rolls.Flat(14, 5, 15);
        var faces = Enumerable.Range(0, 12).Select(i => StoryScreen.Reveal(roll, "Wren", i * 0.1)).ToArray();

        Assert.All(faces.Where(f => !f.Landed), f => Assert.InRange(f.Face, 1, 20));
        Assert.True(faces.Where(f => !f.Landed).Select(f => f.Face).Distinct().Count() > 3);
        Assert.Equal(14, StoryScreen.Reveal(roll, "Wren", StoryScreen.Tumble).Face);
        Assert.True(StoryScreen.Reveal(roll, "Wren", StoryScreen.Tumble).Landed);
    }

    // ---- Between scenes --------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_gap_shows_the_next_scenes_title_and_nothing_else()
    {
        var gap = Screen(Showing(9, null, States.Gap, title: "What the Bridge Cost"));

        Assert.Equal("What the Bridge Cost", gap.Interlude);
        Assert.Null(gap.Caption);
        Assert.Null(gap.Vote);
        Assert.Null(gap.Check);
        Assert.False(gap.Dimmed);
    }
}
