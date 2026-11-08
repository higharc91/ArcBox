using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>Development-only art lineups. These do not register encounters or change creature sheets.</summary>
public static class LibraryVariantReview
{
    public sealed record Creature(string Id, string Name, string Sprite);

    public static IReadOnlyList<Creature> Creatures(string batch) => batch switch
    {
        "crypt-kin" => [new("Bat", "Vampire Bat", "beasts.bat.vampire"),
            new("Bear", "Undead Bear", "beasts.bear.undead"), new("Slime", "Crystal Slime", "beasts.slime.crystal")],
        "web-and-hunt" => [new("Spider", "Phase Spider", "beasts.spider.phase"),
            new("Wolf", "Alpha Wolf", "beasts.wolf.alpha"), new("Crab", "Deep Crab", "bestiary.crab.deep")],
        "iron-and-fangs" => [new("Scorpion", "Black Scorpion", "bestiary.scorpion.black"),
            new("Rider", "Warg Rider", "bestiary.warg.rider"), new("Mimic", "Ironbound Mimic", "bosses.mimic.ironbound")],
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
