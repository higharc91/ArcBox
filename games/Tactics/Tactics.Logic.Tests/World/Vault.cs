using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// A small walled region for the interactables' tests: three rooms, joined by a door at (4, 3) and a gate at (5, 6). <c>#</c> is a wall,
/// <c>.</c> ground at height 0, a digit ground at that height, and anything off the rows a wall.
/// </summary>
internal static class Vault
{
    public static readonly string[] Rows =
    [
        "##########",
        "#S.......#",
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

    public static readonly Tile Recovery = new(2, 1);

    /// <summary>The region <paramref name="rows"/> draw, named <paramref name="id"/>.</summary>
    public static RegionDef Region(string id = "vault", string[]? rows = null)
    {
        rows ??= Rows;
        var bytes = new byte[32 * 32 * 3];
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var c = y < rows.Length && x < rows[y].Length ? rows[y][x] : '#';
                var at = (x + y * 32) * 3;
                (bytes[at], bytes[at + 1], bytes[at + 2]) = c switch
                {
                    '#' => ((byte)0, (byte)0, (byte)TileFlags.Impassable),
                    >= '1' and <= '9' => ((byte)(c - '0'), (byte)0, (byte)0),
                    _ => ((byte)0, (byte)0, (byte)0),
                };
            }
        return new RegionDef(id, "The Vault", 1, [32, 32], ["grass/lush"], [new RegionChunk(0, 0, Convert.ToBase64String(bytes))], [], [],
            new RegionSpawns(Start, Recovery), []);
    }

    public static (string Path, string Json) World(RegionDef def) => ($"world/{def.Id}.json", JsonSerializer.Serialize(def, ArcJson.Options));

    /// <summary>An interactables file for <paramref name="region"/> holding <paramref name="things"/>, each a JSON object.</summary>
    public static (string Path, string Json) Things(string region, params string[] things)
        => ($"interactables/{region}.json", $$"""{ "region": "{{region}}", "interactables": [ {{string.Join(", ", things)}} ] }""");

    /// <summary>One thing, read as the loader would (without its checks).</summary>
    public static InteractableDef Def(string json)
    {
        var def = JsonSerializer.Deserialize<InteractableDef>(json, ArcJson.Options)!;
        return def with
        {
            Text = def.Text.IsDefault ? [] : def.Text, Requires = def.Requires.IsDefault ? [] : def.Requires,
            Unless = def.Unless.IsDefault ? [] : def.Unless, Sets = def.Sets.IsDefault ? [] : def.Sets, Loot = def.Loot.IsDefault ? [] : def.Loot,
            Targets = def.Targets.IsDefault ? [] : def.Targets,
            Trap = def.Trap is { } t ? t with { Text = t.Text.IsDefault ? [] : t.Text, Sets = t.Sets.IsDefault ? [] : t.Sets } : null,
        };
    }

    /// <summary>The plain ground of <paramref name="def"/>.</summary>
    public static WorldGround Ground(RegionDef? def = null) => WorldGround.Of(WorldRegion.From(def ?? Region()), new Dictionary<string, BattleMap>(), ArtManifest.None);
}
