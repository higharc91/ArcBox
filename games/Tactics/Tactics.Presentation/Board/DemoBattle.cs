using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>
/// Dev-only views for <c>--tactics-demo=&lt;map&gt;[:changed]</c>: a battle on a map with no server, so the board can be
/// looked at. <c>:changed</c> adds what a fight does to a map: zones, raised tiles (one on the back corner, burning, which
/// must leave the board where it was) and the bridge's tiles sunk. <c>:fight</c> adds one of every kind of unit: a turn in
/// progress, a Downed character, conditions, a summon, flyers, a boss wider than a tile, a sprite the pack doesn't have, and
/// Out and Dead units, which aren't drawn, and on the HUD a long turn order and a reaction prompt.
/// </summary>
public static class DemoBattle
{
    private const string Changed = ":changed";
    private const string Fight = ":fight";
    private const string Creatures = ":creatures";
    private const string MobRefresh = ":mob-refresh";
    private const string GoblinRoles = ":goblin-roles";
    private const string RoadAndHost = ":road-and-host";
    private const string Kindled = ":kindled";
    private const string BoneWardens = ":bone-wardens";
    private const string HollowDead = ":hollow-dead";
    private const string RimeKeepers = ":rime-keepers";
    private const string OathAndShadow = ":oath-and-shadow";
    private const string StoneAndMire = ":stone-and-mire";
    private const string WildElements = ":wild-elements";
    private const string TideAndWyrm = ":tide-and-wyrm";
    private const string ElementalTriad = ":elemental-triad";
    private const string GroveWard = ":grove-ward";
    private const string EmberFamiliar = ":ember-familiar";
    private const string TrailCompanion = ":trail-companion";
    private const string OakAmbush = ":oak-ambush";

    private static UnitView Unit(string id, string side, string? sprite, int x, int y, int hp, int maxHp, string life = "Up", int countdown = 0,
        params string[] conditions)
        => new(new UnitId(id), id, side, sprite, new Tile(x, y), hp, maxHp, 0, life, countdown, [.. conditions], null);

    private static ImmutableArray<UnitView> Units() =>
    [
        Unit("fighter", "Party", "characters.class_fighter", 5, 5, 28, 34),
        Unit("cleric", "Party", "characters.class_cleric", 3, 7, 0, 22, "Downed", 2, "prone"),
        Unit("wizard", "Party", "characters.class_wizard", 2, 9, 9, 18, "Up", 0, "poisoned", "hasted"),
        Unit("rogue", "Party", "characters.class_rogue", 4, 9, 14, 20, "Up", 0, "burning", "marked", "slowed", "inspired", "bleeding"),
        Unit("hound", "Party", "bosses.hound.tan", 1, 9, 11, 12),
        Unit("nobody", "Party", null, 3, 10, 10, 10),
        Unit("gone", "Party", "characters.class_monk", 2, 11, 0, 20, "Out"),
        Unit("goblin", "Enemy", "characters.goblin_cutthroat", 9, 5, 3, 7),
        Unit("scout", "Enemy", "characters.kobold", 5, 7, 5, 5),
        Unit("lookout", "Enemy", "characters.kobold", 0, 0, 5, 5),
        Unit("spider", "Enemy", "beasts.spider.black", 11, 7, 6, 11, "Up", 0, "stunned"),
        Unit("archer", "Enemy", "characters.goblin_archer", 12, 3, 7, 7),
        Unit("wolf", "Enemy", "beasts.wolf.grey", 11, 6, 11, 11),
        Unit("troll", "Enemy", "bosses.troll.troll", 11, 9, 50, 84),
        Unit("dragon", "Enemy", "bosses.dragon.red", 10, 2, 120, 200),
        Unit("wraith", "Enemy", "bosses.wraith.wraith", 12, 7, 20, 30),
        Unit("mystery", "Enemy", "nonsense.key", 11, 11, 4, 8),
        Unit("dead", "Enemy", "characters.bandit", 9, 3, 0, 8, "Dead"),
    ];

    /// <summary>Art review only: every primary creature beside a party figure, using the real battle renderer.</summary>
    private static ImmutableArray<UnitView> CreatureUnits()
    {
        string[] keys = ["beasts.wolf.grey", "beasts.boar.brown", "beasts.bear.brown", "beasts.spider.black",
            "beasts.slime.green", "beasts.bat.dusk", "beasts.drake.red", "bestiary.warg.black",
            "bestiary.hookbeak.brown", "bestiary.scorpion.sand", "bestiary.harpy.dusk", "bestiary.wisp.marsh",
            "bestiary.gargoyle.granite", "bestiary.crab.shore"];
        Tile[] spots = [new(2,1),new(5,1),new(8,1),new(11,1),new(2,4),new(5,4),new(8,4),new(11,4),
            new(2,7),new(5,7),new(8,7),new(11,7),new(3,10),new(10,10)];
        return [.. keys.Select((key,i) => Unit(key.Split('.')[1], "Enemy", key, spots[i].X, spots[i].Y, 10, 10)),
            Unit("human reference", "Party", "characters.class_fighter", 6, 11, 30, 30)];
    }

    private static ImmutableArray<UnitView> RefreshedMobs() =>
    [
        Unit("Goblin cutthroat", "Enemy", "characters.goblin_cutthroat", 11, 6, 7, 7),
        Unit("Orc brute", "Enemy", "characters.orc_brute", 12, 9, 20, 20),
        Unit("Red drake", "Enemy", "beasts.drake.red", 9, 10, 64, 64),
        Unit("Fighter reference", "Party", "characters.class_fighter", 11, 10, 28, 28),
        Unit("Kobold reference", "Enemy", "characters.kobold", 13, 10, 5, 5),
    ];

    private static ImmutableArray<UnitView> RefreshedGoblinRoles() =>
    [
        Unit("Goblin archer", "Enemy", "characters.goblin_archer", 11, 6, 14, 14),
        Unit("Stormcaller", "Enemy", "characters.goblin_shaman", 12, 9, 18, 18),
        Unit("Vorga", "Enemy", "characters.goblin_spear", 9, 10, 21, 21),
        Unit("Fighter reference", "Party", "characters.class_fighter", 11, 10, 28, 28),
        Unit("Cutthroat reference", "Enemy", "characters.goblin_cutthroat", 13, 10, 16, 16),
    ];

    private static ImmutableArray<UnitView> RefreshedRoadAndHost() =>
    [
        Unit("Bandit", "Enemy", "characters.bandit", 11, 6, 26, 26),
        Unit("Hobgoblin", "Enemy", "characters.hobgoblin", 12, 9, 11, 11),
        Unit("Vorga reference", "Enemy", "characters.goblin_spear", 9, 10, 21, 21),
        Unit("Fighter reference", "Party", "characters.class_fighter", 11, 10, 28, 28),
        Unit("Cutthroat reference", "Enemy", "characters.goblin_cutthroat", 13, 10, 16, 16),
    ];

    private static ImmutableArray<UnitView> RefreshedKindled() =>
    [
        Unit("Cultist", "Enemy", "characters.cultist", 11, 6, 16, 16),
        Unit("Maren", "Enemy", "characters.cult_leader", 12, 9, 105, 105),
        Unit("Bandit reference", "Enemy", "characters.bandit", 9, 10, 26, 26),
        Unit("Fighter reference", "Party", "characters.class_fighter", 11, 10, 28, 28),
        Unit("Hobgoblin reference", "Enemy", "characters.hobgoblin", 13, 10, 11, 11),
    ];

    private static ImmutableArray<UnitView> RefreshedBoneWardens() =>
    [
        Unit("Bone warrior", "Enemy", "characters.skeleton", 11, 6, 13, 13),
        Unit("Bone archer", "Enemy", "characters.skeleton_archer", 12, 9, 16, 16),
        Unit("Bandit reference", "Enemy", "characters.bandit", 9, 10, 26, 26),
        Unit("Fighter reference", "Party", "characters.class_fighter", 11, 10, 28, 28),
        Unit("Hobgoblin reference", "Enemy", "characters.hobgoblin", 13, 10, 11, 11),
    ];

    private static ImmutableArray<UnitView> RefreshedHollowDead() =>
    [
        Unit("Risen", "Enemy", "characters.zombie", 11, 6, 15, 15),
        Unit("Wraith", "Enemy", "bosses.wraith.wraith", 13, 6, 40, 40),
        Unit("Bone reference", "Enemy", "characters.skeleton", 9, 10, 13, 13),
        Unit("Fighter reference", "Party", "characters.class_fighter", 11, 10, 28, 28),
        Unit("Bandit reference", "Enemy", "characters.bandit", 13, 10, 26, 26),
    ];

    private static ImmutableArray<UnitView> RefreshedRimeKeepers() =>
    [
        Unit("Ivo / necromancer", "Enemy", "characters.necromancer", 11, 6, 52, 52),
        Unit("Oswin / lich", "Enemy", "characters.lich", 13, 6, 95, 95),
        Unit("Bone reference", "Enemy", "characters.skeleton", 9, 10, 13, 13),
        Unit("Fighter reference", "Party", "characters.class_fighter", 11, 10, 28, 28),
        Unit("Bandit reference", "Enemy", "characters.bandit", 13, 10, 26, 26),
    ];

    private static ImmutableArray<UnitView> RefreshedOathAndShadow() =>
    [
        Unit("Halvard", "Enemy", "characters.black_knight", 11, 6, 280, 280),
        Unit("Drow", "Enemy", "characters.dark_elf", 13, 6, 34, 34),
        Unit("Bone reference", "Enemy", "characters.skeleton", 9, 10, 13, 13),
        Unit("Fighter reference", "Party", "characters.class_fighter", 11, 10, 28, 28),
        Unit("Bandit reference", "Enemy", "characters.bandit", 13, 10, 26, 26),
    ];

    private static ImmutableArray<UnitView> RefreshedStoneAndMire() =>
    [
        Unit("Troll", "Enemy", "bosses.troll.troll", 11, 6, 72, 72),
        Unit("Golem", "Enemy", "bosses.golem.granite", 13, 6, 95, 95),
        Unit("Bone reference", "Enemy", "characters.skeleton", 9, 10, 13, 13),
        Unit("Fighter reference", "Party", "characters.class_fighter", 11, 10, 28, 28),
        Unit("Bandit reference", "Enemy", "characters.bandit", 13, 10, 26, 26),
    ];

    private static ImmutableArray<UnitView> RefreshedWildElements() =>
    [
        Unit("Storm", "Enemy", "bosses.air_elemental.storm", 9, 10, 64, 64),
        Unit("Magma", "Enemy", "bosses.earth_elemental.magma", 12, 9, 84, 84),
        Unit("Swamp", "Enemy", "bosses.water_elemental.swamp", 13, 6, 74, 74),
        Unit("Fighter", "Party", "characters.class_fighter", 12, 10, 28, 28),
        Unit("Bandit reference", "Enemy", "characters.bandit", 13, 10, 26, 26),
    ];

    private static ImmutableArray<UnitView> RefreshedTideAndWyrm() =>
    [
        Unit("Water", "Enemy", "bosses.water_elemental.sea", 9, 10, 74, 74),
        Unit("Dragon", "Enemy", "bosses.dragon.red", 13, 6, 160, 160),
        Unit("Fighter", "Party", "characters.class_fighter", 12, 10, 28, 28),
        Unit("Bandit reference", "Enemy", "characters.bandit", 13, 10, 26, 26),
    ];

    private static ImmutableArray<UnitView> RefreshedElementalTriad() =>
    [
        Unit("Air", "Enemy", "bosses.air_elemental.gale", 9, 10, 64, 64),
        Unit("Earth", "Enemy", "bosses.earth_elemental.stone", 12, 9, 84, 84),
        Unit("Fire", "Enemy", "bosses.fire_elemental.ember", 13, 6, 68, 68),
        Unit("Fighter", "Party", "characters.class_fighter", 12, 10, 28, 28),
        Unit("Bandit reference", "Enemy", "characters.bandit", 13, 10, 26, 26),
    ];

    private static ImmutableArray<UnitView> RefreshedGroveWard() =>
    [
        Unit("Sapling", "Party", "bosses.sapling.spring", 11, 9, 18, 18),
        Unit("Bandit", "Enemy", "characters.bandit", 13, 9, 26, 26),
        Unit("Fighter", "Party", "characters.class_fighter", 11, 6, 28, 28),
        Unit("Hound reference", "Party", "bosses.hound.tan", 9, 10, 16, 16),
        Unit("Imp reference", "Party", "bosses.imp.crimson", 13, 6, 14, 14),
    ];

    private static ImmutableArray<UnitView> RefreshedEmberFamiliar() =>
    [
        Unit("Imp", "Party", "bosses.imp.crimson", 11, 9, 14, 14),
        Unit("Bandit", "Enemy", "characters.bandit", 13, 10, 26, 26),
        Unit("Fighter", "Party", "characters.class_fighter", 11, 6, 28, 28),
        Unit("Hound reference", "Party", "bosses.hound.tan", 9, 10, 16, 16),
        Unit("Bone reference", "Enemy", "characters.skeleton", 13, 6, 13, 13),
    ];

    private static ImmutableArray<UnitView> RefreshedTrailCompanion() =>
    [
        Unit("Hound", "Party", "bosses.hound.tan", 12, 10, 16, 16),
        Unit("Bandit", "Enemy", "characters.bandit", 13, 10, 26, 26),
        Unit("Fighter", "Party", "characters.class_fighter", 11, 6, 28, 28),
        Unit("Mimic reference", "Enemy", "bosses.mimic.oak", 9, 10, 40, 40),
        Unit("Bone reference", "Enemy", "characters.skeleton", 13, 6, 13, 13),
    ];

    private static ImmutableArray<UnitView> RefreshedOakAmbush() =>
    [
        Unit("Mimic", "Enemy", "bosses.mimic.oak", 12, 9, 40, 40),
        Unit("Fighter", "Party", "characters.class_fighter", 12, 10, 28, 28),
        Unit("Bandit", "Enemy", "characters.bandit", 13, 10, 26, 26),
        Unit("Bone reference", "Enemy", "characters.skeleton", 9, 10, 13, 13),
        Unit("Golem reference", "Enemy", "bosses.golem.granite", 13, 6, 95, 95),
    ];

    private static BoardChanges Changes()
    {
        ImmutableArray<Tile> bridge = [.. Enumerable.Range(5, 2).SelectMany(y => Enumerable.Range(5, 4).Select(x => new Tile(x, y)))];
        return new BoardChanges(
            [
                new ZoneView(1, "fire", [new Tile(0, 0), new Tile(3, 5), new Tile(3, 6), new Tile(4, 6)]),
                new ZoneView(2, "web", [new Tile(10, 4), new Tile(10, 5)]),
                new ZoneView(3, "ice", [new Tile(11, 6), new Tile(12, 6)]),
                new ZoneView(4, "poison", [new Tile(3, 8), new Tile(4, 8)]),
                new ZoneView(5, "grand_sigil", [new Tile(10, 9), new Tile(11, 9)]),
            ],
            [.. bridge.Select(t => new HeightChange(t, -2)), new HeightChange(new Tile(11, 8), 2), new HeightChange(new Tile(0, 0), 2)],
            bridge);
    }

    /// <summary>A visual check of the shipped encounter at its intended level, with real enemies and starting zones.</summary>
    public static TacticsHostView Encounter(string id, string packRoot)
    {
        var rules = RulesPack.Load([Path.Combine(packRoot, "rules")], ArtManifests.Load(packRoot));
        var encounter = rules.Encounters[id];
        string[] classes = ["fighter", "cleric", "rogue", "wizard", "ranger", "paladin"];
        var size = Math.Clamp(5, encounter.Party.Min, encounter.Party.Max);
        ImmutableArray<PartyMember> party = [.. classes.Take(size).Select(cls => new PartyMember(new UnitId(cls),
            SheetBuilder.DefaultCharacter(cls, "human", cls, null, encounter.Level ?? 1, rules), new AiControlled("companion")))];
        var state = Battle.Start(Encounters.Setup(id, party, Difficulty.Normal, rules), rules, 7).State;
        ImmutableArray<UnitView> units = [.. state.Units.Values.Select(u => new UnitView(u.Id, u.Sheet.Name,
            u.Sheet.Side.ToString(), HostSprite.Of(u.Sheet.Sprite, u.Sheet.Tags), u.Pos, u.Hp, u.Sheet.MaxHp, u.TempHp,
            u.Life.ToString(), u.Countdown, [.. u.Conditions.Select(c => c.Id.Value)], null))];
        var battle = new BattleView(id, state.MapId, state.Round, state.Order, state.Active, units, [], null,
            BoardChanges.Of(state), state.Objectives.Current, Name: encounter.Name);
        return new TacticsHostView("Battle", "Encounter art preview", null, 1, [], null, battle, null, null);
    }

    /// <summary>The host view for a demo spec: a map id, with <c>:changed</c> or <c>:fight</c> or neither.</summary>
    public static TacticsHostView View(string spec)
    {
        var changed = spec.EndsWith(Changed, StringComparison.Ordinal);
        var fight = spec.EndsWith(Fight, StringComparison.Ordinal);
        var creatures = spec.EndsWith(Creatures, StringComparison.Ordinal);
        var mobRefresh = spec.EndsWith(MobRefresh, StringComparison.Ordinal);
        var goblinRoles = spec.EndsWith(GoblinRoles, StringComparison.Ordinal);
        var roadAndHost = spec.EndsWith(RoadAndHost, StringComparison.Ordinal);
        var kindled = spec.EndsWith(Kindled, StringComparison.Ordinal);
        var boneWardens = spec.EndsWith(BoneWardens, StringComparison.Ordinal);
        var hollowDead = spec.EndsWith(HollowDead, StringComparison.Ordinal);
        var rimeKeepers = spec.EndsWith(RimeKeepers, StringComparison.Ordinal);
        var wildElements = spec.EndsWith(WildElements, StringComparison.Ordinal);
        var tideAndWyrm = spec.EndsWith(TideAndWyrm, StringComparison.Ordinal);
        var elementalTriad = spec.EndsWith(ElementalTriad, StringComparison.Ordinal);
        var groveWard = spec.EndsWith(GroveWard, StringComparison.Ordinal);
        var emberFamiliar = spec.EndsWith(EmberFamiliar, StringComparison.Ordinal);
        var trailCompanion = spec.EndsWith(TrailCompanion, StringComparison.Ordinal);
        var oakAmbush = spec.EndsWith(OakAmbush, StringComparison.Ordinal);
        var stoneAndMire = spec.EndsWith(StoneAndMire, StringComparison.Ordinal);
        var oathAndShadow = spec.EndsWith(OathAndShadow, StringComparison.Ordinal);
        var map = wildElements ? spec[..^WildElements.Length] : tideAndWyrm ? spec[..^TideAndWyrm.Length] : elementalTriad ? spec[..^ElementalTriad.Length] : groveWard ? spec[..^GroveWard.Length] : emberFamiliar ? spec[..^EmberFamiliar.Length] : trailCompanion ? spec[..^TrailCompanion.Length] : oakAmbush ? spec[..^OakAmbush.Length] : stoneAndMire ? spec[..^StoneAndMire.Length] : oathAndShadow ? spec[..^OathAndShadow.Length] : rimeKeepers ? spec[..^RimeKeepers.Length] : hollowDead ? spec[..^HollowDead.Length] : boneWardens ? spec[..^BoneWardens.Length] : kindled ? spec[..^Kindled.Length] : roadAndHost ? spec[..^RoadAndHost.Length] : goblinRoles ? spec[..^GoblinRoles.Length] : mobRefresh ? spec[..^MobRefresh.Length] : creatures ? spec[..^Creatures.Length] : changed ? spec[..^Changed.Length] : fight ? spec[..^Fight.Length] : spec;
        var units = wildElements ? RefreshedWildElements() : tideAndWyrm ? RefreshedTideAndWyrm() : elementalTriad ? RefreshedElementalTriad() : groveWard ? RefreshedGroveWard() : emberFamiliar ? RefreshedEmberFamiliar() : trailCompanion ? RefreshedTrailCompanion() : oakAmbush ? RefreshedOakAmbush() : stoneAndMire ? RefreshedStoneAndMire() : oathAndShadow ? RefreshedOathAndShadow() : rimeKeepers ? RefreshedRimeKeepers() : hollowDead ? RefreshedHollowDead() : boneWardens ? RefreshedBoneWardens() : kindled ? RefreshedKindled() : roadAndHost ? RefreshedRoadAndHost() : goblinRoles ? RefreshedGoblinRoles() : mobRefresh ? RefreshedMobs() : creatures ? CreatureUnits() : fight ? Units() : [];
        var battle = new BattleView(map, map, 2, [.. units.Select(u => u.Id)], fight ? new UnitId("fighter") : null, units, [], null,
            changed ? Changes() : BoardChanges.None, ObjectivesDef.Default, fight ? new UnitId("rogue") : null);
        return new TacticsHostView("Battle", fight ? "rogue can react" : "Board demo", null, 1, [], null, battle, null, null);
    }
}
