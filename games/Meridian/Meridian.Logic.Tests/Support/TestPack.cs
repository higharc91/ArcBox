using System.Text.Json.Nodes;
using ArcBox.Sdk.Content;

namespace Meridian.Logic.Tests;

/// <summary>An in-memory content pack. Tests edit one node of a valid pack to break exactly one rule.</summary>
internal sealed class MemoryFiles : IFileSource
{
    public const string Root = "pack";
    private readonly Dictionary<string, JsonNode> _files = new(StringComparer.Ordinal);

    public JsonNode this[string relative]
    {
        get => _files[relative];
        set => _files[relative] = value;
    }

    public void Remove(string relative) => _files.Remove(relative);

    public RulesPack Load(MeridianRules? rules = null) => PackLoader.Load(this, Root, rules);

    private static string Key(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    public bool Exists(string path) => _files.ContainsKey(Key(path));
    public string ReadAllText(string path) => _files[Key(path)].ToJsonString();
    public byte[] ReadAllBytes(string path) => System.Text.Encoding.UTF8.GetBytes(ReadAllText(path));
    public string[] GetDirectories(string path) => [];
    public Stream OpenRead(string path) => new MemoryStream(ReadAllBytes(path));
}

/// <summary>
/// A small, valid pack: the five start tiles, six placeable tiles, three crew and one card of each kind. Rules tests
/// set piles and positions directly on the state, so they don't depend on how this pack shuffles.
/// </summary>
internal static class TestPack
{
    public static MemoryFiles Files()
    {
        var files = new MemoryFiles
        {
            [PackLoader.TilesFile] = JsonNode.Parse("""
            {
              "start": {
                "entry": "airlock",
                "place": [
                  { "tile": "airlock", "deck": "habitat", "x": 0, "y": 2 },
                  { "tile": "docking_ring", "deck": "habitat", "x": 0, "y": 1 },
                  { "tile": "central_lift", "deck": "habitat", "x": 0, "y": 0 },
                  { "tile": "command_landing", "deck": "command", "x": 0, "y": 0 },
                  { "tile": "engineering_landing", "deck": "engineering", "x": 0, "y": 0 }
                ],
                "links": [ { "from": 2, "to": 3 }, { "from": 2, "to": 4 } ]
              },
              "startTiles": [
                { "id": "airlock", "name": "Airlock", "decks": ["habitat"], "hatches": ["n"] },
                { "id": "docking_ring", "name": "Docking Ring", "decks": ["habitat"], "hatches": ["n", "e", "s", "w"] },
                { "id": "central_lift", "name": "Central Lift", "decks": ["habitat"], "hatches": ["e", "s", "w"] },
                { "id": "command_landing", "name": "Command Landing", "decks": ["command"], "hatches": ["n", "e", "s", "w"] },
                { "id": "engineering_landing", "name": "Engineering Landing", "decks": ["engineering"], "hatches": ["n", "e", "s", "w"] }
              ],
              "tiles": [
                { "id": "corridor", "name": "Corridor", "decks": ["habitat", "command", "engineering"], "hatches": ["n", "s"], "copies": 2 },
                { "id": "junction", "name": "Junction", "decks": ["habitat", "command", "engineering"], "hatches": ["n", "e", "s", "w"] },
                { "id": "storage", "name": "Storage", "decks": ["habitat"], "hatches": ["s"], "symbol": "salvage" },
                { "id": "lab", "name": "Lab", "decks": ["command"], "hatches": ["s", "e"], "symbol": "anomaly" },
                { "id": "quarters", "name": "Quarters", "decks": ["habitat"], "hatches": ["n", "e", "s"], "symbol": "incident" },
                { "id": "med_bay", "name": "Med Bay", "decks": ["habitat"], "hatches": ["s", "w"],
                  "rule": { "kind": "heal", "traits": "physical", "steps": 1 } }
              ]
            }
            """)!,
            [PackLoader.CrewFile] = JsonNode.Parse("""
            {
              "crew": [
                { "id": "ivo", "name": "Ivo", "role": "Mechanic", "tracks": {
                  "brawn":   { "values": [1, 2, 3, 3, 4, 5, 5, 6], "start": 4 },
                  "agility": { "values": [1, 2, 2, 3, 4, 4, 5, 6], "start": 4 },
                  "tech":    { "values": [1, 2, 3, 4, 4, 5, 6, 7], "start": 4 },
                  "nerve":   { "values": [1, 1, 2, 3, 3, 4, 5, 5], "start": 4 } } },
                { "id": "mara", "name": "Mara", "role": "Pilot", "tracks": {
                  "brawn":   { "values": [1, 1, 2, 3, 3, 4, 4, 5], "start": 4 },
                  "agility": { "values": [2, 3, 3, 4, 5, 5, 6, 7], "start": 4 },
                  "tech":    { "values": [1, 2, 2, 3, 4, 4, 5, 6], "start": 4 },
                  "nerve":   { "values": [1, 2, 3, 3, 4, 5, 5, 6], "start": 4 } } },
                { "id": "dex", "name": "Dex", "role": "Medic", "tracks": {
                  "brawn":   { "values": [1, 2, 2, 3, 3, 4, 5, 5], "start": 4 },
                  "agility": { "values": [1, 2, 3, 3, 4, 5, 5, 6], "start": 4 },
                  "tech":    { "values": [1, 2, 3, 3, 4, 5, 6, 6], "start": 4 },
                  "nerve":   { "values": [2, 2, 3, 4, 4, 5, 6, 7], "start": 4 } } }
              ]
            }
            """)!,
            ["rules/cards/incidents.json"] = JsonNode.Parse("""
            {
              "cards": [
                { "id": "power_flicker", "title": "Power Flicker", "text": "The lights stutter.",
                  "check": { "trait": "nerve", "bands": [
                    { "min": 0, "max": 1, "text": "You flinch.", "effects": [ { "kind": "lose", "traits": "mental", "steps": 1 } ] },
                    { "min": 2, "max": 3, "text": "Nothing happens." },
                    { "min": 4, "max": null, "text": "You steady yourself.", "effects": [ { "kind": "gain", "traits": "nerve", "steps": 1 } ] }
                  ] } }
              ]
            }
            """)!,
            ["rules/cards/salvage.json"] = JsonNode.Parse("""
            {
              "cards": [
                { "id": "pry_bar", "title": "Pry Bar", "text": "Heavy and honest.",
                  "item": { "passive": [ { "kind": "extraDice", "trait": "brawn", "dice": 1 } ] } }
              ]
            }
            """)!,
            ["rules/cards/anomalies.json"] = JsonNode.Parse("""
            {
              "cards": [
                { "id": "humming_shard", "title": "Humming Shard", "text": "It is warm.",
                  "item": { "passive": [ { "kind": "extraDice", "trait": "tech", "dice": 1 } ] } }
              ]
            }
            """)!,
        };
        return files;
    }

    public static RulesPack Load() => Files().Load();
}
