using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// The character screens (the user's note, 2026-11-01): the details the portal explains, from the pack, and player colours, picked at
/// creation and kept on the member, for the TV's rings and vote dots and the phone.
/// </summary>
public sealed class CharacterScreenTests
{
    private static readonly ProfileId Ana = new("pr_ana");
    private static readonly ProfileId Bo = new("pr_bo");

    private static readonly CampaignInfo Info = new("c1", "ABCD", TacticsGame.Id, "Embers", Ana,
        [new CampaignMember(Ana, "Ana", CampaignRole.Owner, DateTimeOffset.UnixEpoch),
         new CampaignMember(Bo, "Bo", CampaignRole.Player, DateTimeOffset.UnixEpoch)], 1);

    private static TacticsCampaignRules Portal => new(Rules);

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    private static object Dwarf(string? color = null) => color is null
        ? new { race = "dwarf", @class = "cleric", background = "acolyte", scores = new { str = 8, dex = 8, con = 15, @int = 8, wis = 15, cha = 15 } }
        : new { race = "dwarf", @class = "cleric", background = "acolyte", scores = new { str = 8, dex = 8, con = 15, @int = 8, wis = 15, cha = 15 }, color };

    private static JsonElement Created(ProfileId who, string? color = null, JsonElement? doc = null)
    {
        var result = Portal.Handle(Info, doc ?? Portal.NewDocument(Info), who, "createCharacter", Json(Dwarf(color)));
        Assert.Null(result.Error);
        return result.Document!.Value;
    }

    private static JsonNode View(JsonElement doc, ProfileId who) => JsonNode.Parse(Portal.PortalView(Info, doc, who).GetRawText())!;

    // ---- Colours ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_palette_colour_reads_on_grass_stone_and_ash_and_each_is_its_own()
    {
        foreach (var c in PlayerColors.Palette)
        {
            Assert.Equal(c.Hex, PlayerColors.Parse(c.Hex));
            Assert.True(PlayerColors.WorstContrast(c.Hex) >= PlayerColors.MinContrast, $"{c.Name} {c.Hex}");
            Assert.Equal(c.Hex, PlayerColors.Readable(c.Hex));
        }
        Assert.Equal(PlayerColors.Palette.Length, PlayerColors.Palette.Select(c => c.Hex).Distinct().Count());
        Assert.Equal(PlayerColors.Palette.Length, PlayerColors.Palette.Select(c => c.Name).Distinct().Count());
    }

    [Fact]
    public void Contrast_is_wcags()
    {
        Assert.Equal(21, PlayerColors.Contrast("#000000", "#ffffff"), 3);
        Assert.Equal(1, PlayerColors.Contrast("#59983f", "#59983f"), 6);
        Assert.Equal(PlayerColors.Contrast("#ffd23f", "#6c6166"), PlayerColors.Contrast("#6c6166", "#ffd23f"), 9);
    }

    [Theory]
    [InlineData("#1b2a6b")]   // navy: lost on the panels
    [InlineData("#2e7d32")]   // grass green: lost on grass
    [InlineData("#000000")]
    [InlineData("#C0392B")]   // a mid red, in upper case
    public void A_colour_too_dark_for_the_ground_is_lightened_until_it_reads(string hex)
    {
        var made = PlayerColors.Readable(hex)!;
        Assert.True(PlayerColors.WorstContrast(made) >= PlayerColors.MinContrast, made);
        Assert.True(PlayerColors.Luminance(made) > PlayerColors.Luminance(hex.ToLowerInvariant()));
        Assert.Matches("^#[0-9a-f]{6}$", made);
        Assert.True(PlayerColors.WorstContrast(hex.ToLowerInvariant()) < PlayerColors.MinContrast);
    }

    /// <summary>The phone lightens a colour as it's picked (colors.ts); these are the answers its test pins too.</summary>
    [Theory]
    [InlineData("#1b2a6b", "#aeb9ea")]
    [InlineData("#2e7d32", "#79ce7d")]
    [InlineData("#C0392B", "#ebaea7")]
    [InlineData("#000000", "#bababa")]
    [InlineData("#ff00ff", "#ff94ff")]
    [InlineData("#123456", "#96bfe9")]
    [InlineData("#0011cc", "#b2b9ff")]   // its red lands on 178.5: halves round to even, here and on the phone
    public void The_phone_and_the_server_lighten_alike(string hex, string made) => Assert.Equal(made, PlayerColors.Readable(hex));

    [Fact]
    public void A_lightened_colour_keeps_its_hue()
    {
        var made = PlayerColors.Readable("#1b2a6b")!;     // navy becomes a light blue, not a grey or a purple
        var (r, g, b) = (Convert.ToInt32(made[1..3], 16), Convert.ToInt32(made[3..5], 16), Convert.ToInt32(made[5..7], 16));
        Assert.True(b > g && g > r, made);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("#gg0000")]
    [InlineData("ffd23f0")]
    public void Anything_but_a_hex_colour_is_refused(string? hex)
    {
        Assert.Null(PlayerColors.Parse(hex));
        Assert.Null(PlayerColors.Readable(hex));
    }

    [Fact]
    public void Defaults_go_round_the_palette_in_join_order_skipping_colours_taken()
    {
        Assert.Equal(PlayerColors.Palette[0].Hex, PlayerColors.Default(0));
        Assert.Equal(PlayerColors.Palette[1].Hex, PlayerColors.Default(1));
        Assert.Equal(PlayerColors.Palette[0].Hex, PlayerColors.Default(PlayerColors.Palette.Length));
        Assert.Equal(PlayerColors.Default(1), PlayerColors.FreeDefault(1, [PlayerColors.Default(0)]));
        Assert.Equal(PlayerColors.Default(2), PlayerColors.FreeDefault(1, [PlayerColors.Default(1), null]));
        Assert.Equal(PlayerColors.Default(3), PlayerColors.FreeDefault(1, PlayerColors.Palette.Select(c => c.Hex).Where(h => h != PlayerColors.Default(3))));
        Assert.Equal(PlayerColors.Default(1), PlayerColors.FreeDefault(1, PlayerColors.Palette.Select(c => c.Hex)));    // all taken: share
    }

    private static MemberRecord Member(string? color = null) => new(SheetBuilder.DefaultCharacter("X", "human", "fighter", null, 1, Rules), 0, Color: color);

    /// <summary>Colours are given in join order, once; a member who has one keeps it, and one given later skips what others have.</summary>
    [Fact]
    public void Assigning_keeps_colours_and_never_duplicates_a_default()
    {
        var members = Ordinal.Map<MemberRecord>([("pr_a", Member()), ("pr_b", Member(PlayerColors.Default(0))), ("pr_c", Member())]);
        var given = PlayerColors.Assign(members, ["pr_c", "pr_a", "pr_b"]);
        Assert.Equal(PlayerColors.Default(0), given["pr_b"].Color);       // kept, though they joined third
        Assert.Equal(PlayerColors.Default(1), given["pr_c"].Color);       // first to join: Gold is taken, so the next free
        Assert.Equal(PlayerColors.Default(2), given["pr_a"].Color);       // Sky would be its own, but c has it; then Coral
        Assert.Same(given, PlayerColors.Assign(given, ["pr_c", "pr_a", "pr_b"]));      // nothing more to give

        // A member leaving doesn't move anyone's colour: it's kept with them.
        var after = PlayerColors.Assign(given.Remove("pr_c"), ["pr_a", "pr_b"]);
        Assert.Equal(given["pr_a"].Color, after["pr_a"].Color);
    }

    [Fact]
    public void A_colour_picked_at_creation_is_kept_readable_on_the_member()
    {
        var doc = Created(Ana, "#1B2A6B");
        var kept = TacticsDocument.Read(doc).For(Ana)!.Color;
        Assert.Equal(PlayerColors.Readable("#1b2a6b"), kept);
        Assert.Equal(kept, View(doc, Ana)["me"]!["color"]!.GetValue<string>());

        var refused = Portal.Handle(Info, Portal.NewDocument(Info), Ana, "createCharacter", Json(Dwarf("blue")));
        Assert.Equal("Pick a colour.", refused.Error);
    }

    /// <summary>
    /// A member who never picked is given their join order's free colour the first time the campaign is changed, and keeps it; the view of
    /// a document saved before colours shows the same colour without writing anything.
    /// </summary>
    [Fact]
    public void A_character_from_before_colours_is_given_its_join_orders_colour_and_keeps_it()
    {
        var old = TacticsDocument.Read(Created(Bo, doc: Created(Ana)));
        old = old with { Members = Ordinal.Map(old.Members.Select(m => (m.Key, m.Value with { Color = null }))) };
        Assert.DoesNotContain("\"color\"", old.ToJson().GetRawText());
        var view = View(old.ToJson(), Bo);
        Assert.Equal(PlayerColors.Default(1), view["me"]!["color"]!.GetValue<string>());
        Assert.Equal([PlayerColors.Default(0), PlayerColors.Default(1)], view["party"]!.AsArray().Select(p => p!["color"]!.GetValue<string>()));

        // Any change saves them: Ana changes hers, and Bo's default is now kept with him.
        var saved = TacticsDocument.Read(Portal.Handle(Info, old.ToJson(), Ana, "setColor", Json(new { color = "#d2b4ff" })).Document!.Value);
        Assert.Equal("#d2b4ff", saved.For(Ana)!.Color);
        Assert.Equal(PlayerColors.Default(1), saved.For(Bo)!.Color);
    }

    /// <summary>A new member's default skips a colour someone already picked, and the picker is told which colours are whose.</summary>
    [Fact]
    public void A_new_members_default_skips_taken_colours_and_the_picker_marks_them()
    {
        var doc = Created(Ana, PlayerColors.Default(1));      // Ana picked Bo's would-be default
        var colors = View(doc, Bo)["options"]!["colors"]!;
        Assert.Equal(PlayerColors.Default(2), colors["current"]!.GetValue<string>());     // Sky is Ana's: the next free, Coral
        Assert.Equal([(PlayerColors.Default(1), "Ana")], colors["taken"]!.AsArray().Select(t => (t!["hex"]!.GetValue<string>(), t["name"]!.GetValue<string>())));
        Assert.Equal(PlayerColors.Default(2), TacticsDocument.Read(Created(Bo, doc: doc)).For(Bo)!.Color);
    }

    [Fact]
    public void The_sheet_changes_the_colour_and_a_remake_keeps_it()
    {
        var doc = Created(Ana);
        var set = Portal.Handle(Info, doc, Ana, "setColor", Json(new { color = "#7ccbff" }));
        Assert.Null(set.Error);
        Assert.Equal("#7ccbff", TacticsDocument.Read(set.Document!.Value).For(Ana)!.Color);
        Assert.Equal("Pick a colour.", Portal.Handle(Info, doc, Ana, "setColor", Json(new { color = 7 })).Error);
        Assert.Equal("Create your character first.", Portal.Handle(Info, Portal.NewDocument(Info), Ana, "setColor", Json(new { color = "#7ccbff" })).Error);

        var remade = Created(Ana, doc: set.Document);
        Assert.Equal("#7ccbff", TacticsDocument.Read(remade).For(Ana)!.Color);
    }

    /// <summary>#206: a character barks in a voice the player picks, at creation or later on the sheet; a remake keeps it.</summary>
    [Fact]
    public void A_character_picks_a_voice_and_keeps_it()
    {
        var fresh = Portal.NewDocument(Info);
        var voices = View(fresh, Ana)["voices"]!.AsArray();
        Assert.Equal(12, voices.Count);
        Assert.Contains(voices, v => v!["id"]!.GetValue<string>() == "wry.high" && v["name"]!.GetValue<string>() == "Wry, higher");

        // Without a pick, the character speaks in the default for their name.
        Assert.Equal(PlayerVoices.Default("Ana"), View(Created(Ana), Ana)["me"]!["voice"]!.GetValue<string>());

        var picked = Portal.Handle(Info, fresh, Ana, "createCharacter", Json(new
        {
            race = "dwarf", @class = "cleric", background = "acolyte", scores = new { str = 8, dex = 8, con = 15, @int = 8, wis = 15, cha = 15 },
            voice = "grim.low",
        }));
        Assert.Null(picked.Error);
        Assert.Equal("grim.low", TacticsDocument.Read(picked.Document!.Value).For(Ana)!.Character.Voice);
        Assert.Equal("Pick a voice.", Portal.Handle(Info, fresh, Ana, "createCharacter", Json(new
        {
            race = "dwarf", @class = "cleric", background = "acolyte", scores = new { str = 8, dex = 8, con = 15, @int = 8, wis = 15, cha = 15 },
            voice = "operatic.high",
        })).Error);

        var changed = Portal.Handle(Info, picked.Document!.Value, Ana, "setVoice", Json(new { voice = "wry.high" }));
        Assert.Null(changed.Error);
        Assert.Equal("wry.high", View(changed.Document!.Value, Ana)["me"]!["voice"]!.GetValue<string>());
        Assert.Equal("Pick a voice.", Portal.Handle(Info, picked.Document!.Value, Ana, "setVoice", Json(new { voice = "wry" })).Error);
        Assert.Equal("wry.high", TacticsDocument.Read(Created(Ana, doc: changed.Document)).For(Ana)!.Character.Voice);
    }

    [Fact]
    public void Creation_offers_the_palette_the_grounds_and_the_colour_it_starts_on()
    {
        var colors = View(Created(Ana), Bo)["options"]!["colors"]!;
        Assert.Equal(PlayerColors.Palette.Select(c => c.Hex), colors["palette"]!.AsArray().Select(c => c!["hex"]!.GetValue<string>()));
        Assert.Equal(["grass", "stone", "ash", "panel"], colors["grounds"]!.AsArray().Select(g => g!["name"]!.GetValue<string>()));
        Assert.Equal(PlayerColors.MinContrast, colors["minContrast"]!.GetValue<double>());
        Assert.Equal(PlayerColors.Default(1), colors["current"]!.GetValue<string>());
        Assert.Equal([PlayerColors.Default(0)], colors["taken"]!.AsArray().Select(t => t!["hex"]!.GetValue<string>()));
    }

    // ---- The TV and the phones ------------------------------------------------------------------------------------------------

    /// <summary>The TV's walkers and the walk screen carry each player's colour: the one picked, else the join order's.</summary>
    [Fact]
    public void Walkers_and_the_walk_screen_carry_the_players_colours()
    {
        var first = new Session();
        first.ToWalking();
        var member2 = first.Profiles[1]!.Value.Value;
        var doc = TacticsDocument.Read(first.Lease.Document);
        doc = doc with { Members = doc.Members.SetItem(member2, doc.Members[member2] with { Color = "#d2b4ff" }) };
        var s = new Session(document: doc.ToJson());
        s.Harness.StartCampaign(s.Lease);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);

        var colors = s.Host.Explore!.Party.ToDictionary(w => w.Id, w => w.Color);
        for (var i = 0; i < 4; i++)
            Assert.Equal(i == 1 ? "#d2b4ff" : PlayerColors.Default(i), colors[s.Profiles[i]!.Value.Value]);

        var walk = JsonNode.Parse(PhoneJson(s, 1))!["data"]!;
        Assert.Equal("#d2b4ff", walk["me"]!["color"]!.GetValue<string>());
        Assert.Contains("#d2b4ff", walk["party"]!.AsArray().Select(p => p!["color"]!.GetValue<string>()));
    }

    /// <summary>Defaults follow the campaign's join order, whoever sits where tonight.</summary>
    [Fact]
    public void Default_colours_follow_the_campaigns_join_order()
    {
        var s = new Session(reseated: true);
        s.ToWalking();
        var colors = s.Host.Explore!.Party.ToDictionary(w => w.Id, w => w.Color);
        var members = s.Lease.Info.Members;
        for (var i = 0; i < members.Count; i++)
            Assert.Equal(PlayerColors.Default(i), colors[members[i].Profile.Value]);
    }

    [Fact]
    public void A_question_names_every_voters_colour()
    {
        var s = AtTheVote();
        var colors = Ask(s).Colors!;
        Assert.Equal(4, colors.Count);
        foreach (var w in s.Host.Explore!.Party)
            Assert.Equal(w.Color, colors[w.Name]);
    }

    // ---- The details ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_race_says_what_it_does_in_play()
    {
        var dwarf = CharacterGuide.Race(Rules.Races["dwarf"], Rules);
        Assert.Contains("Speed: 3 tiles a turn.", dwarf.Facts);
        Assert.Contains("Size: Medium (not used in battles yet).", dwarf.Facts);
        Assert.Contains("Resistant to poison damage: takes half.", dwarf.Facts);
        Assert.Contains(dwarf.Facts, f => f.StartsWith("Darkvision: 12 tiles.", StringComparison.Ordinal));
        Assert.Contains("Ability boosts come from your background, not your race.", dwarf.Facts);
        Assert.Equal(["dwarf.stoneskin"], dwarf.Traits);

        var human = CharacterGuide.Race(Rules.Races["human"], Rules);
        Assert.Contains("Senses: normal sight.", human.Facts);
        Assert.Contains("Skilled: 1 more skill from your class's list.", human.Facts);
        Assert.Equal(["human.resourceful"], human.Traits);
    }

    /// <summary>A trait's text is the engine's tooltip for it, so it can't drift from what a battle does.</summary>
    [Fact]
    public void A_feature_is_the_engines_own_tooltip()
    {
        var stoneskin = CharacterGuide.Feature("dwarf.stoneskin", 1, Rules)!;
        Assert.Equal("Stoneskin", stoneskin.Name);
        Assert.Equal(Tooltips.For(Rules.Abilities[new AbilityId("dwarf.stoneskin")], 1, Rules), stoneskin.Text);
        Assert.Equal(["Bonus action", "Once per battle"], stoneskin.Tags);

        var lucky = CharacterGuide.Feature("halfling.lucky", 1, Rules)!;
        Assert.Equal("Lucky", lucky.Name);
        Assert.Equal(["Once per battle"], lucky.Tags);
        Assert.StartsWith("When you miss", lucky.Text, StringComparison.Ordinal);

        Assert.Null(CharacterGuide.Feature("no.such", 1, Rules));
    }

    [Fact]
    public void A_spell_says_its_tier_and_cost()
    {
        var spell = Rules.Abilities.Values.First(a => a.Tier >= 1 && a.Cost.Action > 0 && a.Cost.Resource is { Count: > 0 });
        var tags = CharacterGuide.Feature(spell.Id.Value, 3, Rules)!.Tags;
        Assert.Equal($"Tier {spell.Tier} spell", tags[0]);
        Assert.Contains("Action", tags);
        var (resource, amount) = spell.Cost.Resource!.First();
        Assert.Contains($"{amount} {resource.Replace('_', ' ')}", tags);
    }

    [Fact]
    public void A_class_explains_its_role_proficiencies_spellcasting_and_levels()
    {
        var cleric = CharacterGuide.Class(Rules.Classes["cleric"], upTo: 3, subclassesTo: 10, Rules);
        Assert.Equal("Healer: keeps the party standing.", cleric.Role);
        Assert.Contains("Hit points: 8 + your CON modifier at level 1, then 5 + CON modifier each level (hit die d8).", cleric.Facts);
        Assert.Contains("Saving throws: Wisdom and Charisma.", cleric.Facts);
        Assert.Contains("Armour: light armour, medium armour and shields (not used in battles yet).", cleric.Facts);
        Assert.Contains("Weapons: simple weapons (not used in battles yet).", cleric.Facts);
        Assert.Contains(cleric.Facts, f => f.StartsWith("Spellcasting: Wisdom.", StringComparison.Ordinal));
        Assert.Contains(cleric.Facts, f => f.StartsWith("Skills: 2 skills from Medicine, Religion, Insight, History or Persuasion", StringComparison.Ordinal));
        Assert.Equal([1, 2, 3], cleric.Levels.Select(l => l.Level));
        Assert.Contains("cleric.turn_undead", cleric.Levels[1].Features);
        Assert.Equal([3, 4], cleric.Levels[0].Choices.Select(c => c.Choose));
        Assert.All(cleric.Subclasses, s => Assert.Contains(s.Levels, l => l.Level > 3));

        var rogue = CharacterGuide.Class(Rules.Classes["rogue"], upTo: 2, subclassesTo: 2, Rules);
        Assert.Equal(2, rogue.Levels[0].Attacks);     // at most two since #108
        Assert.Equal(1, rogue.Levels[0].RangedAttacks);
        Assert.DoesNotContain(rogue.Facts, f => f.StartsWith("Spellcasting", StringComparison.Ordinal));
        Assert.All(rogue.Subclasses, s => Assert.Empty(s.Levels));      // nothing before level 3
    }

    /// <summary>No guide shows an id: no underscores, no "class.thing", in any fact, name, drive or feature text, at any level.</summary>
    [Fact]
    public void No_guide_text_shows_a_raw_id()
    {
        var d = CharacterGuide.For(Rules.Races.Values, Rules.Classes.Values, Rules.Backgrounds.Values, 9, Rules, "cleric");
        var texts = d.Races.Values.SelectMany(r => r.Facts.Append(r.Name))
            .Concat(d.Classes.Values.SelectMany(c => c.Facts.Append(c.Name).Append(c.Role).Concat(c.Subclasses.Select(x => x.Name))))
            .Concat(d.Backgrounds.Values.SelectMany(b => b.Facts.Append(b.Name)))
            .Concat(d.Features.Values.SelectMany(f => f.Tags.Append(f.Name).Append(f.Text)))
            .Concat(d.Skills.Values.SelectMany(k => new[] { k.Name, k.Text }))
            .Concat(d.Stats.Values.SelectMany(x => x.Drives.Append(x.Name)))
            .ToList();
        Assert.NotEmpty(texts);
        var id = new System.Text.RegularExpressions.Regex(@"_|\b[a-z]+\.[a-z]");
        Assert.All(texts, t => Assert.False(id.IsMatch(t), t));
    }

    /// <summary>
    /// Facts the pack holds that no battle reads say so (the one rule, <see cref="CharacterGuide.NotInBattles"/>), and the Jump a battle's
    /// movement does read is given for a class that changes it.
    /// </summary>
    [Fact]
    public void Facts_no_battle_reads_say_so_and_jump_is_given()
    {
        var rogue = CharacterGuide.Class(Rules.Classes["rogue"], 2, 2, Rules);
        Assert.Contains("Climbs 3 levels in one step (most climb 2).", rogue.Facts);
        Assert.Contains("Weapons: simple weapons, hand crossbow, longsword, rapier and shortsword (not used in battles yet).",
            CharacterGuide.Class(Rules.Classes["bard"], 2, 2, Rules).Facts);
        Assert.Contains("Armour: light armour (not used in battles yet).", rogue.Facts);
        Assert.DoesNotContain(CharacterGuide.Class(Rules.Classes["cleric"], 2, 2, Rules).Facts, f => f.StartsWith("Climbs", StringComparison.Ordinal));
        Assert.Contains(CharacterGuide.Class(Rules.Classes["monk"], 2, 2, Rules).Facts, f => f.StartsWith("Weapons: simple weapons and light martial weapons", StringComparison.Ordinal));
        Assert.Contains("Size: Medium (not used in battles yet).", CharacterGuide.Race(Rules.Races["dwarf"], Rules).Facts);
        Assert.Contains("Keepsake: prayer beads (not used in battles yet).", CharacterGuide.Background(Rules.Backgrounds["acolyte"]).Facts);
    }

    [Fact]
    public void A_background_lists_its_boosts_skills_and_keepsake()
    {
        var acolyte = CharacterGuide.Background(Rules.Backgrounds["acolyte"]);
        Assert.Equal(["Ability boosts: WIS +2 and CHA +1.", "Skills: Insight and Religion.", "Keepsake: prayer beads (not used in battles yet)."], acolyte.Facts);
        Assert.Equal("Sleight of Hand", CharacterGuide.SkillName("sleight_of_hand"));
        Assert.Equal("Animal Handling", CharacterGuide.SkillName("animal_handling"));
    }

    [Fact]
    public void An_ability_score_says_what_it_drives()
    {
        var wis = CharacterGuide.StatFor(Stat.Wis, Rules);
        Assert.Equal("Wisdom", wis.Name);
        Assert.Contains("Skill checks in the story: Animal Handling, Insight, Medicine, Perception and Survival.", wis.Drives);
        Assert.Contains(wis.Drives, d => d.StartsWith("Spell attacks and spell save DCs for a ", StringComparison.Ordinal) && d.Contains("Cleric"));
        Assert.Contains(wis.Drives, d => d.StartsWith("Armour Class without armour, for a Monk", StringComparison.Ordinal));

        var dex = CharacterGuide.StatFor(Stat.Dex, Rules);
        Assert.Contains(dex.Drives, d => d.StartsWith("Initiative:", StringComparison.Ordinal));
        Assert.Contains(dex.Drives, d => d.StartsWith("Finesse weapons (", StringComparison.Ordinal) && d.Contains("rapier"));
        var con = CharacterGuide.StatFor(Stat.Con, Rules);
        Assert.Contains(con.Drives, d => d.StartsWith("Hit points:", StringComparison.Ordinal));
        Assert.Contains(con.Drives, d => d.Contains("Breath Weapon (Dragonborn)"));
        var str = CharacterGuide.StatFor(Stat.Str, Rules).Drives.Single(d => d.StartsWith("Attack and damage rolls with ", StringComparison.Ordinal));
        Assert.Contains("greatclub", str);
        Assert.DoesNotContain("rapier", str);       // a finesse weapon, which has its own line
    }

    /// <summary>Every id a race, class or subclass names is described once, in the one features map.</summary>
    [Fact]
    public void The_portal_view_describes_every_feature_it_names_once()
    {
        var details = View(Portal.NewDocument(Info), Ana)["details"]!;
        var features = details["features"]!.AsObject();
        var named = details["races"]!.AsObject().SelectMany(r => r.Value!["traits"]!.AsArray())
            .Concat(details["classes"]!.AsObject().SelectMany(c => c.Value!["levels"]!.AsArray()
                .Concat(c.Value["subclasses"]!.AsArray().SelectMany(s => s!["levels"]!.AsArray()))
                .SelectMany(l => l!["features"]!.AsArray().Concat(l["choices"]!.AsArray().SelectMany(ch => ch!["from"]!.AsArray())))))
            .Select(n => n!.GetValue<string>()).ToHashSet();
        Assert.NotEmpty(named);
        Assert.All(named, id => Assert.True(features.ContainsKey(id), id));
        Assert.Equal(11, details["races"]!.AsObject().Count);
        Assert.Equal(12, details["classes"]!.AsObject().Count);
        Assert.Equal(18, details["skills"]!.AsObject().Count);
        Assert.Equal(["str", "dex", "con", "int", "wis", "cha"], details["stats"]!.AsObject().Select(p => p.Key));
    }

    /// <summary>A character that can't be remade gets only its own race, class and background explained.</summary>
    [Fact]
    public void A_settled_character_gets_only_its_own_details()
    {
        var doc = TacticsDocument.Read(Created(Ana));
        doc = doc with { Members = doc.Members.SetItem(Ana.Value, doc.For(Ana)! with { Fought = true, Remade = true }) };
        var details = View(doc.ToJson(), Ana)["details"]!;
        Assert.Equal(["dwarf"], details["races"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["cleric"], details["classes"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["acolyte"], details["backgrounds"]!.AsObject().Select(p => p.Key));
    }

    /// <summary>
    /// The views stay small enough for a phone on Wi-Fi: descriptions are sent once, not per level, and a class's progression stops at the
    /// level after the character's. The largest is creation in a level-9 party, where every class sends all ten levels.
    /// </summary>
    [Theory]
    [InlineData(1, 64_000)]
    [InlineData(4, 110_000)]
    [InlineData(9, 200_000)]
    public void The_portal_views_stay_small(int level, int most)
    {
        var doc = TacticsDocument.Read(Created(Ana)) with { PartyLevel = level };
        Assert.InRange(Portal.PortalView(Info, doc.ToJson(), Bo).GetRawText().Length, 1, most);
        Assert.InRange(Portal.PortalView(Info, doc.ToJson(), Ana).GetRawText().Length, 1, most);
    }

    /// <summary>The sheet shows each choice's picks: the server's matching, so a spell offered at two levels sits in one.</summary>
    [Fact]
    public void Each_choice_says_which_picks_fill_it()
    {
        var doc = TacticsDocument.Read(Created(Ana));
        var cleric = doc.For(Ana)!;
        doc = doc with { PartyLevel = 4, Members = doc.Members.SetItem(Ana.Value, cleric with { Character = CharacterRules.LevelUp(cleric.Character, 4, Rules) }) };
        var me = View(doc.ToJson(), Ana)["me"]!;
        var picks = me["picks"]!.AsArray().Select(p => p!.GetValue<string>()).ToList();
        var placed = new List<string>();
        foreach (var choice in me["choices"]!.AsArray())
        {
            var picked = choice!["picked"]!.AsArray().Select(p => p!.GetValue<string>()).ToList();
            Assert.True(picked.Count <= choice["choose"]!.GetValue<int>());
            Assert.All(picked, p => Assert.Contains(p, choice["from"]!.AsArray().Select(o => o!["id"]!.GetValue<string>())));
            placed.AddRange(picked);
        }
        Assert.Equal(picks.Order(), placed.Order());
        Assert.Contains("insight", me["skills"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.Equal(SkillCheck.Bonus(CharacterRules.LevelUp(cleric.Character, 4, Rules), "insight", Rules), me["bonuses"]!["insight"]!.GetValue<int>());
        Assert.Equal(["wis", "cha"], me["saves"]!.AsArray().Select(s => s!.GetValue<string>()));
    }

    [Fact]
    public void Assigning_picks_moves_one_along_to_make_room()
    {
        // Two features: one takes a or b, the other only a. Picking a then b needs a moved to the second.
        ImmutableArray<(int, FeatureRef)> choices =
        [
            (1, new FeatureRef(Choose: 1, From: ["x.a", "x.b"])),
            (2, new FeatureRef(Choose: 1, From: ["x.a"])),
        ];
        var placed = SheetBuilder.AssignPicks([new AbilityId("x.a"), new AbilityId("x.b")], choices);
        Assert.Equal(["x.b"], placed[0].Select(p => p.Value));
        Assert.Equal(["x.a"], placed[1].Select(p => p.Value));
        Assert.All(SheetBuilder.AssignPicks([new AbilityId("x.c")], choices), p => Assert.Empty(p));
    }
}
