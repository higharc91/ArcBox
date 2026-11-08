using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>Development-only art lineups. These do not register encounters or change creature sheets.</summary>
public static class LibraryVariantReview
{
    public sealed record Special(string Ability, string Label, double Impact, bool Targeted = false);
    public sealed record Creature(string Id, string Name, string Sprite, IReadOnlyList<Special>? Specials = null);

    private static Creature Dragon(string color) => new(color, color + " Dragon", "bosses.dragon." + color.ToLowerInvariant(),
        [new("dragon.fire_breath", "breath", .5), new("dragon.frightful_roar", "roar", .5),
            new("dragon.cataclysm", "wingbeat", .7)]);

    private static Creature Giant(string id, string name, string variant) => new(id, name, "bosses.troll." + variant,
        [new("troll.boulder_toss", "boulder", .4, true), new("troll.frenzy_roar", "roar", .4)]);

    public static IReadOnlyList<Creature> Creatures(string batch) => batch switch
    {
        "crypt-kin" => [new("Bat", "Vampire Bat", "beasts.bat.vampire"),
            new("Bear", "Undead Bear", "beasts.bear.undead"), new("Slime", "Crystal Slime", "beasts.slime.crystal")],
        "web-and-hunt" => [new("Spider", "Phase Spider", "beasts.spider.phase"),
            new("Wolf", "Alpha Wolf", "beasts.wolf.alpha"), new("Crab", "Deep Crab", "bestiary.crab.deep")],
        "iron-and-fangs" => [new("Scorpion", "Black Scorpion", "bestiary.scorpion.black"),
            new("Rider", "Warg Rider", "bestiary.warg.rider"), new("Mimic", "Ironbound Mimic", "bosses.mimic.ironbound")],
        "wyrm-triad" => [Dragon("Black"), Dragon("Blue"), Dragon("Green")],
        "gilded-and-rime" => [Dragon("Gold"), Giant("Frost", "Frost Troll", "frost"), Giant("Ogre", "Ogre", "ogre")],
        "autumn-and-veil" => [new("Autumn", "Autumn Sapling", "bosses.sapling.autumn"),
            new("Blossom", "Blossom Sapling", "bosses.sapling.blossom"),
            new("Shade", "Shade", "bosses.wraith.shade", [new("wraith.life_drain", "drain", .4, true)])],
        "spectral-remnant" => [new("Spectre", "Spectre", "bosses.wraith.spectre", [new("wraith.life_drain", "drain", .4, true)])],
        _ => throw new ArgumentException($"Unknown library art batch: {batch}", nameof(batch)),
    };

    public static string[] WorldRoster(string batch)
        => ["characters.class_fighter", "characters.bandit", .. Creatures(batch).Select(c => c.Sprite)];

    public static TacticsHostView View(string map, string batch)
    {
        var creatures = Creatures(batch);
        Tile[] positions = [new(9, 10), new(12, 9), new(13, 6)];
        UnitView Unit(string id, string name, string side, string sprite, Tile at)
            => new(new(id), name, side, sprite, at, 28, 28, 0, "Up", 0, [], null);
        ImmutableArray<UnitView> units = [.. creatures.Select((c, i) => Unit(c.Id, c.Name, "Enemy", c.Sprite, positions[i])),
            Unit("Fighter", "Fighter", "Party", "characters.class_fighter", new(12, 10)),
            Unit("Reference", "Bandit reference", "Enemy", "characters.bandit", new(11, 10))];
        var battle = new BattleView(map, map, 2, [.. units.Select(u => u.Id)], null, units, [], null,
            BoardChanges.None, ObjectivesDef.Default);
        return new TacticsHostView("Battle", "Library art review", null, 1, [], null, battle, null, null);
    }
}
