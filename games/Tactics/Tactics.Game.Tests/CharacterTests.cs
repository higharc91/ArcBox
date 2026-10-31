using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using Tactics.Logic;

namespace Tactics.Game.Tests;

/// <summary>Sub-project 4: making and growing a character between sessions, through the portal's commands.</summary>
public sealed class CharacterTests
{
    private static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return RulesPack.Load([Path.Combine(dir.FullName, "content", "tactics", "base", "rules")]);
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    private static RulesPack Rules => Pack.Value;

    private static readonly ProfileId Ana = new("pr_ana");

    private static readonly CampaignInfo Info = new("c1", "ABCD", TacticsGame.Id, "Embers", Ana,
        [new CampaignMember(Ana, "Ana", CampaignRole.Owner, DateTimeOffset.UnixEpoch)], 1);

    /// <summary>15, 15, 15, 8, 8, 8: exactly 27 points.</summary>
    private static Dictionary<Stat, int> Scores(int str = 15, int dex = 15, int con = 15, int @int = 8, int wis = 8, int cha = 8)
        => new() { [Stat.Str] = str, [Stat.Dex] = dex, [Stat.Con] = con, [Stat.Int] = @int, [Stat.Wis] = wis, [Stat.Cha] = cha };

    private static string Background => Rules.Backgrounds.Keys.First();

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    [Fact]
    public void Point_buy_costs_and_limits()
    {
        Assert.Equal(27, CharacterRules.PointsSpent(Scores()));
        Assert.Equal(0, CharacterRules.PointsSpent(Scores(8, 8, 8, 8, 8, 8)));
        Assert.Null(CharacterRules.PointsSpent(Scores(str: 16)));
        Assert.Null(CharacterRules.PointsSpent(new Dictionary<Stat, int> { [Stat.Str] = 10 }));

        Assert.NotNull(CharacterRules.Create("Ana", "dwarf", "fighter", Background, Scores(), 1, Rules).Character);
        Assert.Equal("That's 29 points; you have 27.",
            CharacterRules.Create("Ana", "dwarf", "fighter", Background, Scores(@int: 10), 1, Rules).Problem);
        Assert.Equal("Pick one of the races.", CharacterRules.Create("Ana", "orc?", "fighter", Background, Scores(), 1, Rules).Problem);
    }

    [Fact]
    public void Increases_come_at_4_and_8_and_cap_scores_at_20()
    {
        var c = CharacterRules.Create("Ana", "dwarf", "fighter", Background, Scores(), 3, Rules).Character!;
        Assert.Equal("You have no ability score increase to spend.", CharacterRules.SpendIncrease(c, [Stat.Str], Rules).Problem);

        c = c with { Level = 4 };
        Assert.Equal(2, CharacterRules.IncreasesLeft(c));
        var raised = CharacterRules.SpendIncrease(c, [Stat.Dex, Stat.Con], Rules).Character!;
        Assert.Equal((1, 1, 0), (raised.Increases[Stat.Dex], raised.Increases[Stat.Con], CharacterRules.IncreasesLeft(raised)));

        // A soldier (+2 STR) with 15 STR who spent 2 at level 4 has 19; at level 8, +2 would make 21.
        var soldier = CharacterRules.Create("Ana", "dwarf", "fighter", "soldier", Scores(), 4, Rules).Character!;
        soldier = CharacterRules.SpendIncrease(soldier, [Stat.Str], Rules).Character! with { Level = 8 };
        Assert.Equal("STR can't go above 20.", CharacterRules.SpendIncrease(soldier, [Stat.Str], Rules).Problem);
        Assert.NotNull(CharacterRules.SpendIncrease(soldier, [Stat.Str, Stat.Dex], Rules).Character);   // 20 and 16: fine
    }

    [Fact]
    public void A_subclass_is_chosen_once_from_level_3()
    {
        var c = CharacterRules.Create("Ana", "dwarf", "fighter", Background, Scores(), 2, Rules).Character!;
        var first = Rules.Classes["fighter"].Subclasses[0].Id;
        Assert.Equal("You choose a subclass at level 3.", CharacterRules.ChooseSubclass(c, first, Rules).Problem);

        c = c with { Level = 3 };
        Assert.True(CharacterRules.NeedsSubclass(c));
        Assert.Equal("Pick one of your class's subclasses.", CharacterRules.ChooseSubclass(c, "wizard.evoker", Rules).Problem);
        var chosen = CharacterRules.ChooseSubclass(c, first, Rules).Character!;
        Assert.Equal(first, chosen.Subclass);
        Assert.Equal("Your subclass is already chosen.", CharacterRules.ChooseSubclass(chosen, first, Rules).Problem);
    }

    [Fact]
    public void Level_up_keeps_the_players_character_and_fills_new_slots()
    {
        var wizard = CharacterRules.Create("Ana", "elf", "wizard", Background, Scores(8, 15, 15, 15, 8, 8), 1, Rules).Character!;
        var mine = wizard.Picks.Take(wizard.Picks.Length - 1).ToList();   // one choice dropped, so the picks are the player's own
        wizard = CharacterRules.SetPicks(wizard, mine, Rules).Character!;

        var up = CharacterRules.LevelUp(wizard, 5, Rules);

        Assert.Equal(("elf", wizard.Background, 5), (up.Race, up.Background, up.Level));
        Assert.Equal(wizard.BaseScores, up.BaseScores);
        Assert.All(mine, p => Assert.Contains(p, up.Picks));
        Assert.True(up.Picks.Length > mine.Count);   // the new levels' slots got the class's defaults
        SheetBuilder.ForCharacter(up, Rules);        // and the sheet builds
    }

    [Fact]
    public void The_portal_creates_a_character_then_refuses_to_remake_it_once_it_has_fought()
    {
        var rules = new TacticsCampaignRules(Rules);
        var doc = rules.NewDocument(Info);
        var view = rules.PortalView(Info, doc, Ana);
        Assert.Equal(JsonValueKind.Null, view.GetProperty("me").ValueKind);
        Assert.True(view.GetProperty("options").GetProperty("races").GetArrayLength() > 0);

        var create = Json(new { race = "dwarf", @class = "cleric", background = Background,
            scores = new { str = 8, dex = 8, con = 15, @int = 8, wis = 15, cha = 15 } });
        var made = rules.Handle(Info, doc, Ana, "createCharacter", create);
        Assert.Null(made.Error);
        Assert.Equal("Dwarf Cleric · Lv 1", rules.CardFor(Info, made.Document!.Value, Ana).Subtitle);
        var me = rules.PortalView(Info, made.Document!.Value, Ana).GetProperty("me");
        Assert.Equal("cleric", me.GetProperty("class").GetString());
        Assert.True(me.GetProperty("choices").GetArrayLength() > 0);

        // Before its first battle it can be remade freely, and that doesn't use up the free remake.
        var again = rules.Handle(Info, made.Document!.Value, Ana, "createCharacter", create);
        Assert.Null(again.Error);
        Assert.False(TacticsDocument.Read(again.Document!.Value).Members[Ana.Value].Remade);

        // After fighting: one free remake, which keeps the XP, injuries and bag; then the character is locked.
        var fought = TacticsDocument.Read(made.Document!.Value);
        var old = fought.Members[Ana.Value];
        fought = fought with { Members = fought.Members.SetItem(Ana.Value, old with
        {
            Xp = 250, Character = old.Character with { Injuries = [new InjuryId("black_eye")], Bag = [] },
        }) };
        Assert.True(rules.PortalView(Info, fought.ToJson(), Ana).GetProperty("me").GetProperty("freeRemake").GetBoolean());
        var elf = Json(new { race = "elf", @class = "wizard", background = Background,
            scores = new { str = 8, dex = 15, con = 15, @int = 15, wis = 8, cha = 8 } });
        var remade = rules.Handle(Info, fought.ToJson(), Ana, "createCharacter", elf);
        Assert.Null(remade.Error);
        var after = TacticsDocument.Read(remade.Document!.Value).Members[Ana.Value];
        Assert.Equal(("elf", "wizard", 250, true), (after.Character.Race, after.Character.Class, after.Xp, after.Remade));
        Assert.Equal([new InjuryId("black_eye")], after.Character.Injuries);
        Assert.Empty(after.Character.Bag);

        Assert.Equal("You've used your free remake; this character is yours now.",
            rules.Handle(Info, remade.Document!.Value, Ana, "createCharacter", create).Error);
        Assert.Equal(JsonValueKind.Null, rules.PortalView(Info, remade.Document!.Value, Ana).GetProperty("options").ValueKind);
    }

    [Fact]
    public void Portal_commands_refuse_nonsense_with_a_reason()
    {
        var rules = new TacticsCampaignRules(Rules);
        var doc = rules.NewDocument(Info);
        Assert.Equal("Create your character first.", rules.Handle(Info, doc, Ana, "setPicks", Json(new { picks = new[] { "x" } })).Error);
        Assert.Equal("Every ability score starts between 8 and 15.",
            rules.Handle(Info, doc, Ana, "createCharacter", Json(new { race = "dwarf", @class = "cleric", background = Background, scores = 7 })).Error);

        var made = rules.Handle(Info, doc, Ana, "createCharacter", Json(new { race = "dwarf", @class = "cleric", background = Background,
            scores = new { str = 8, dex = 8, con = 15, @int = 8, wis = 15, cha = 15 } })).Document!.Value;
        Assert.NotNull(rules.Handle(Info, made, Ana, "setPicks", Json(new { picks = new[] { "wizard.fireball" } })).Error);
        Assert.Equal("Pick abilities to raise.", rules.Handle(Info, made, Ana, "spendIncrease", Json(new { stats = new[] { "luck" } })).Error);
        Assert.Equal("That isn't something the portal can do.", rules.Handle(Info, made, Ana, "levelUpTo10", Json(new { })).Error);
    }

    [Fact]
    public void A_damaged_character_never_breaks_the_portal_or_its_commands()
    {
        var rules = new TacticsCampaignRules(Rules);
        // A stored character missing most of its fields (hand-edited, or from an older build).
        var doc = JsonDocument.Parse("""
            { "version": 0, "partyLevel": 4, "members": { "pr_ana": { "character": { "name": "Ana", "race": "dwarf", "class": "cleric",
              "background": "acolyte", "level": 4 }, "xp": 0 } }, "completed": [] }
            """).RootElement;

        var me = rules.PortalView(Info, doc, Ana).GetProperty("me");
        Assert.Equal("Dwarf Cleric", me.GetProperty("describe").GetString());
        foreach (var (type, data) in new (string, object)[]
                 {
                     ("spendIncrease", new { stats = new[] { "wis" } }), ("setPicks", new { picks = new[] { "cleric.sacred_flame" } }),
                     ("chooseSubclass", new { subclass = "cleric.life" }), ("createCharacter", new { race = 1 }),
                 })
            rules.Handle(Info, doc, Ana, type, Json(data));   // must not throw

        // And a document that isn't one at all.
        var junk = JsonDocument.Parse("""{ "version": 0, "members": 5 }""").RootElement;
        Assert.NotNull(rules.Handle(Info, junk, Ana, "setPicks", Json(new { picks = new[] { "x" } })).Error);
        Assert.Equal("This campaign's saved data couldn't be read.", rules.PortalView(Info, junk, Ana).GetProperty("problem").GetString());
    }

    [Fact]
    public void Stats_are_named_picks_are_never_empty_and_a_lost_battle_still_counts_as_fought()
    {
        var rules = new TacticsCampaignRules(Rules);
        var made = rules.Handle(Info, rules.NewDocument(Info), Ana, "createCharacter", Json(new { race = "dwarf", @class = "cleric",
            background = Background, scores = new { str = 8, dex = 8, con = 15, @int = 8, wis = 15, cha = 15 } })).Document!.Value;

        var four = TacticsDocument.Read(made);
        four = four with { Members = four.Members.SetItem(Ana.Value, four.Members[Ana.Value] with
        {
            Character = four.Members[Ana.Value].Character with { Level = 4 },
        }) };
        Assert.Equal("Pick abilities to raise.", rules.Handle(Info, four.ToJson(), Ana, "spendIncrease", Json(new { stats = new[] { "7" } })).Error);
        Assert.Equal("Pick abilities to raise.", rules.Handle(Info, four.ToJson(), Ana, "spendIncrease", Json(new { stats = new[] { "str,dex" } })).Error);
        Assert.Equal("Choose at least one.", rules.Handle(Info, made, Ana, "setPicks", Json(new { picks = Array.Empty<string>() })).Error);

        // A battle lost for 0 XP still counts as fought: once its free remake is used, the character is locked.
        var lost = TacticsDocument.Read(made);
        lost = lost with { Members = lost.Members.SetItem(Ana.Value, lost.Members[Ana.Value] with { Fought = true, Remade = true }) };
        Assert.Equal("You've used your free remake; this character is yours now.",
            rules.Handle(Info, lost.ToJson(), Ana, "createCharacter", Json(new { race = "dwarf", @class = "cleric", background = Background,
                scores = new { str = 8, dex = 8, con = 15, @int = 8, wis = 15, cha = 15 } })).Error);
    }

    [Fact]
    public void A_member_who_made_a_character_skips_the_class_pick()
    {
        var rules = new TacticsCampaignRules(Rules);
        ProfileId?[] profiles = [new ProfileId("pr_member1"), new ProfileId("pr_member2"), new ProfileId("pr_member3"), new ProfileId("pr_member4")];
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
            [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
        var doc = rules.Handle(info, rules.NewDocument(info), profiles[0]!.Value, "createCharacter", Json(new { race = "dwarf",
            @class = "cleric", background = Background, scores = new { str = 8, dex = 8, con = 15, @int = 8, wis = 15, cha = 15 } })).Document!.Value;

        var game = new TacticsGame(Rules);
        var h = new ArcBox.Sdk.Testing.GameHarness<TacticsGame>(game, playerCount: 4, profiles: profiles);
        h.StartCampaign(new ArcBox.Sdk.Testing.FakeCampaignLease(info, doc));

        Assert.IsType<WaitView>(h.PlayerView(0));
        Assert.Equal(TacticsGame.Screens.PickClass, ((CustomView)h.PlayerView(1)).Screen);
        Assert.Equal("dwarf", game.Document.For(profiles[0]!.Value)!.Character.Race);
    }
}
