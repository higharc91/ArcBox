using System.Text;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.ExplorationTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// A test region with one of each thing to interact with (interactables design §1), on the base pack: three rooms joined by a locked door at
/// (4, 3) and a gate at (5, 6) that a lever works. Ashen Ford has no things, as the TV can't draw them yet.
/// <code>
///   0123456789
/// 0 ##########
/// 1 #S.s...cC#     S start, s sign, c chest, C cart is at (1, 2)
/// 2 #C......k#     k the iron key
/// 3 ####D#####     D the door
/// 4 #...p...x#     p a plate trap (seen from afar), x a trapped, locked crate
/// 5 #.L......#     L the lever
/// 6 #####G####     G the gate
/// 7 #........#
/// 8 #.t...r.h#     t a pit nobody sees, r the shrine, h a hidden cache
/// 9 ##########
/// </code>
/// </summary>
internal static class Vault
{
    public const string Id = "vault";

    public static readonly string[] Rows =
    [
        "##########",
        "#........#",
        "#........#",
        "####.#####",
        "#........#",
        "#........#",
        "#####.####",
        "#........#",
        "#........#",
        "##########",
    ];

    public static readonly Tile Start = new(1, 1);

    public static readonly string[] Things =
    [
        """{ "id": "sign", "kind": "signpost", "at": [3, 1], "text": ["The vault.", "Keep out."], "sets": ["read_sign"] }""",
        """{ "id": "chest", "kind": "chest", "at": [7, 1], "loot": [{ "item": "potion_of_healing", "count": 2 }], "sets": ["chest_looted"] }""",
        """{ "id": "cart", "kind": "ambient", "at": [1, 2], "art": "props.cart", "text": ["A cart with a broken wheel."] }""",
        """{ "id": "key", "kind": "pickup", "at": [8, 2], "name": "iron key", "sets": ["iron_key"] }""",
        """{ "id": "door", "kind": "door", "at": [4, 3], "lock": { "key": "iron_key", "keyName": "iron key", "pick": 12, "force": 14 } }""",
        """{ "id": "plate", "kind": "trap", "at": [4, 4], "name": "pressure plate", "trap": { "spot": 1, "disarm": 10 } }""",
        """{ "id": "crate", "kind": "crate", "at": [8, 4], "lock": { "pick": 15 }, "trap": { "spot": 1, "disarm": 10, "sets": ["crate_trap_went_off"] }, "loot": [{ "item": "smoke_bomb" }] }""",
        """{ "id": "lever", "kind": "lever", "at": [2, 5], "targets": ["gate"], "sets": ["gate_opened"] }""",
        """{ "id": "gate", "kind": "gate", "at": [5, 6] }""",
        """{ "id": "pit", "kind": "trap", "at": [2, 8], "trap": { "spot": 30, "disarm": 10, "text": ["The floor gives way, but only a foot."], "sets": ["fell_in"] } }""",
        """{ "id": "shrine", "kind": "shrine", "at": [6, 8], "text": ["You kneel.", "The ember-light warms you."], "sets": ["blessed"] }""",
        """{ "id": "cache", "kind": "cache", "at": [8, 8], "hidden": 5, "loot": [{ "item": "alchemists_fire" }] }""",
    ];

    /// <summary>The base pack with the vault on it.</summary>
    public static readonly Lazy<RulesPack> Rules = new(() => Load(Things));

    /// <summary>The base pack with a vault holding <paramref name="things"/>.</summary>
    public static RulesPack Load(string[] things)
    {
        var dir = Directory.CreateTempSubdirectory("tactics-vault-");
        try
        {
            var bytes = new byte[32 * 32 * 3];
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                    if (y >= Rows.Length || x >= Rows[y].Length || Rows[y][x] == '#')
                        bytes[(x + y * 32) * 3 + 2] = (byte)TileFlags.Impassable;
            var region = new RegionDef(Id, "The Vault", 1, [32, 32], ["grass/lush"], [new RegionChunk(0, 0, Convert.ToBase64String(bytes))], [], [],
                new RegionSpawns(Start, new Tile(2, 1)), []);
            var utf8 = new UTF8Encoding(false);
            Directory.CreateDirectory(Path.Combine(dir.FullName, "world"));
            Directory.CreateDirectory(Path.Combine(dir.FullName, "interactables"));
            File.WriteAllText(Path.Combine(dir.FullName, "manifest.json"),
                """{ "id": "vault", "rulesVersion": 1, "requires": ["base"], "files": ["world/vault.json", "interactables/vault.json"] }""", utf8);
            File.WriteAllText(Path.Combine(dir.FullName, "world", "vault.json"), JsonSerializer.Serialize(region, ArcJson.Options), utf8);
            File.WriteAllText(Path.Combine(dir.FullName, "interactables", "vault.json"),
                $$"""{ "region": "vault", "interactables": [ {{string.Join(", ", things)}} ] }""", utf8);
            return RulesPack.Load([ContentRules(), dir.FullName]);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>The base pack with <paramref name="things"/> added to Ashen Ford, from a pack of their own.</summary>
    public static RulesPack InAshenFord(params string[] things)
    {
        var dir = Directory.CreateTempSubdirectory("tactics-ford-things-");
        try
        {
            var utf8 = new UTF8Encoding(false);
            Directory.CreateDirectory(Path.Combine(dir.FullName, "interactables"));
            File.WriteAllText(Path.Combine(dir.FullName, "manifest.json"),
                """{ "id": "ford_things", "rulesVersion": 1, "requires": ["base"], "files": ["interactables/ashen_ford.json"] }""", utf8);
            File.WriteAllText(Path.Combine(dir.FullName, "interactables", "ashen_ford.json"),
                $$"""{ "region": "ashen_ford", "interactables": [ {{string.Join(", ", things)}} ] }""", utf8);
            return RulesPack.Load([ContentRules(), dir.FullName]);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static string ContentRules() => Path.Combine(ContentBase(), "rules");

    private static readonly Lazy<JsonElement> Party = new(() => DocumentAt(null));

    /// <summary>A four-member session in the vault, the leader at <paramref name="at"/> facing east, walking.</summary>
    public static Session At(Tile at, RulesPack? rules = null, Func<WorldSave, WorldSave>? world = null, int seed = 1)
    {
        var save = new WorldSave(Id, at, new Tile(at.X + 1, at.Y), null, [], []);
        var doc = TacticsDocument.Read(Party.Value) with { World = world?.Invoke(save) ?? save };
        var s = new Session(seed: seed, document: doc.ToJson(), game: new TacticsGame(rules ?? Rules.Value));
        s.Harness.StartCampaign(s.Lease);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        s.Send(0, "walk", new { dir = (string?)null });
        return s;
    }
}
