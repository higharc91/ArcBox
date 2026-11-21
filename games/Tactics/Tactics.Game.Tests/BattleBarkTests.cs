using System.Collections.Immutable;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>Battle barks for player characters, named units and the narrator (#206, lore spec §7).</summary>
public sealed class BattleBarkTests
{
    private static readonly Lazy<BarkBook> Book = new(() => LoreBook.Load(ContentBase()).Barks);
    private static readonly UnitId Hero = new("hero1"), Second = new("hero2"), Warlord = new("warlord");
    private const string Wry = "wry.high";

    /// <summary>The Warlord's Bridge with four heroes: hero1 speaks in <see cref="Wry"/>; the others belong to no player.</summary>
    private static BattleState State()
    {
        var fighter = SheetBuilder.DefaultCharacter("Ana", "human", "fighter", null, 5, Rules);
        var cleric = SheetBuilder.DefaultCharacter("Bo", "dwarf", "cleric", null, 5, Rules);
        return Battle.Start(Encounters.Setup("warlord_bridge",
            [new(Hero, fighter, new AiControlled("companion")), new(Second, cleric, new AiControlled("companion")),
             new(new("hero3"), fighter with { Name = "Cy" }, new AiControlled("companion")), new(new("hero4"), cleric with { Name = "Di" }, new AiControlled("companion"))],
            Difficulty.Normal, Rules), Rules, 7).State;
    }

    private static string? Voices(UnitId unit) => unit == Hero ? Wry : null;

    private static ImmutableArray<BattleEvent> Enrich(BattleState state, ImmutableArray<BattleEvent> events, int beat, UnitId? actor = null,
        int since = 10, Func<UnitId, string?>? voices = null)
        => BattleBarks.Enrich(state, events, Book.Value, beat, actor, voices ?? Voices, since);

    private static IEnumerable<string> Lines(string speaker, string moment)
        => (speaker.StartsWith("voice:", StringComparison.Ordinal) ? Book.Value.ForVoice(speaker[6..], moment) : Book.Value.ForBattle(speaker, moment))!
            .Lines.Select(l => l.Text);

    [Fact]
    public void A_player_character_barks_in_their_voice()
    {
        var state = State();
        var foe = state.Units.Values.First(u => u.Sheet.Side != state.Units[Hero].Sheet.Side && u.Id != Warlord).Id;

        var crit = Assert.Single(Enrich(state, [new AttackResolved(Hero, foe, true, true)], 0).OfType<Said>());
        var kill = Assert.Single(Enrich(state, [new Died(foe)], 0, actor: Hero).OfType<Said>());

        Assert.Equal((Hero.Value, Wry), (crit.Speaker, crit.Voice));
        Assert.Contains(crit.Line, Lines("voice:wry", "crit"));
        Assert.Equal((Hero.Value, Wry), (kill.Speaker, kill.Voice));
        Assert.Contains(kill.Line, Lines("voice:wry", "kill"));
    }

    [Fact]
    public void A_named_villain_barks_by_tag_and_has_last_words()
    {
        var state = State();
        var speakers = Enumerable.Range(0, 6).Select(beat => Assert.Single(Enrich(state, [new RoundStarted(1, state.Order)], beat).OfType<Said>()))
            .ToList();
        var last = Assert.Single(Enrich(state, [new Died(Warlord)], 3).OfType<Said>());

        // Vorga, the voiced hero and the narrator take turns opening the fight; heroes without a voice stay quiet.
        Assert.Equal(["hero1", "narrator", "vorga"], speakers.Select(s => s.Speaker).Distinct().Order(StringComparer.Ordinal));
        Assert.All(speakers.Where(s => s.Speaker == "vorga"), s => Assert.Contains(s.Line, Lines("vorga", "battle_start")));
        Assert.Equal("vorga", last.Speaker);
        Assert.Contains(last.Line, Lines("vorga", "defeat"));
    }

    [Fact]
    public void Barks_are_deterministic_rate_limited_and_never_over_another_line()
    {
        var state = State();
        ImmutableArray<BattleEvent> start = [new RoundStarted(1, state.Order)];
        ImmutableArray<BattleEvent> said = [new TriggerFired("taunt"), new Said("warlord", "You want my bridge?")];

        Assert.Equal(Enrich(state, start, 4), Enrich(state, start, 4));
        Assert.Equal(start, Enrich(state, start, 4, since: BattleBarks.QuietBeats - 1));
        Assert.Equal(said, Enrich(state, said, 4));
        // The end of a battle speaks even right after another line.
        Assert.Single(Enrich(state, [new BattleEnded(BattleOutcome.Won)], 4, since: 0).OfType<Said>());
    }

    [Fact]
    public void Every_battle_bark_can_fire_for_a_speaker_who_is_there()
    {
        var silent = new List<string>();
        foreach (var (speaker, moment) in Book.Value.BattleMoments.Where(m => m.Moment is not ("camp" or "level_up") && m.Speaker != "grisk"))
        {
            var state = State();
            var voice = speaker.StartsWith("voice:", StringComparison.Ordinal) ? speaker[6..] + ".low" : null;
            var named = voice is null && speaker != TacticsVoices.NarratorSpeaker;
            // A named speaker stands in for the warlord: same side, same place, their own tag.
            if (named)
                state = state with { Units = state.Units.SetItem(Warlord, state.Units[Warlord] with
                    { Sheet = state.Units[Warlord].Sheet with { Tags = [speaker] } }) };
            Func<UnitId, string?> voices = u => u == Hero ? voice : null;
            var who = named ? Warlord : Hero;
            var them = state.Units[who];
            var other = state.Units.Values.First(u => u.Sheet.Side != them.Sheet.Side && u.Id != Warlord).Id;
            var friend = state.Units.Values.First(u => u.Sheet.Side == them.Sheet.Side && u.Id != who).Id;
            var party = !named;
            ImmutableArray<BattleEvent> events = moment switch
            {
                "battle_start" => [new RoundStarted(1, state.Order)],
                "turn_start" => speaker == TacticsVoices.NarratorSpeaker ? [new RoundStarted(2, state.Order)] : [new TurnStarted(who, 2)],
                "hit" => [new AttackResolved(who, other, true, false)],
                "miss" => [new AttackResolved(who, other, false, false)],
                "crit" => [new AttackResolved(who, other, true, true)],
                "kill" => [new Died(other)],
                "ally_down" => party ? [new Downed(friend)] : [new Died(friend)],
                "low_hp" => [new Damaged(who, them.Hp - 1, "slashing", 1)],
                "healed" => [new Healed(who, 3, them.Hp)],
                "revived" => [new Revived(who, 1)],
                "victory" => [new BattleEnded(party ? BattleOutcome.Won : BattleOutcome.Lost)],
                "defeat" => party ? [new BattleEnded(BattleOutcome.Lost)] : [new Died(who)],
                _ => [],
            };
            var expected = Lines(speaker, moment).ToHashSet();
            var heard = Enumerable.Range(0, 12).Any(beat => Enrich(state, events, beat, actor: who, voices: voices).OfType<Said>()
                .Any(s => expected.Contains(s.Line)));
            if (!heard)
                silent.Add($"{speaker}:{moment}");
        }
        Assert.Empty(silent);
    }
}

/// <summary>The level-up line (#206): someone present, or the narrator, marks the party's new level.</summary>
public sealed class LevelUpBarkTests
{
    [Fact]
    public void A_new_level_is_marked_by_someone_present_or_the_narrator()
    {
        var book = StoryTests.Lore.Barks;
        var s = StoryTests.Near(StoryTests.Mayor);
        s.Game.LevelUpBark();
        var line = Assert.IsType<TacticsHostView>(s.Game.HostView()).Explore!.Bark!.Line;

        Assert.Contains(line.Speaker, new[] { "lark", "narrator" });
        Assert.Contains(book.ForBattle(line.Speaker, "level_up")!.Lines, l => l.Id == line.Id && l.Text == line.Text);
        s.Game.LevelUpBark();
        Assert.Equal(line, Assert.IsType<TacticsHostView>(s.Game.HostView()).Explore!.Bark!.Line);      // the party level picks it
    }
}
