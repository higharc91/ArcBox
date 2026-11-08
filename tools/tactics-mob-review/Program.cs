using System.Text.Json;
using Tactics.Presentation.Board;

var root = args[0];
var art = UnitArt.Load(root);
var keys = new SortedSet<string>(StringComparer.Ordinal);
var rules = new List<object>();
foreach (var file in Directory.GetFiles(Path.Combine(root, "rules", "monsters"), "*.json").Order())
{
    using var doc = JsonDocument.Parse(File.ReadAllText(file));
    foreach (var monster in doc.RootElement.EnumerateArray())
    {
        var key = monster.GetProperty("sprite").GetString()!;
        keys.Add(key);
        rules.Add(new { id = monster.GetProperty("id").GetString(), name = monster.GetProperty("name").GetString(), key, group = Path.GetFileNameWithoutExtension(file) });
    }
}
foreach (var (folder, collection) in new[] { ("beasts", "beasts"), ("bosses", "monsters"), ("bestiary", "creatures") })
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, folder, "manifest.json")));
    foreach (var creature in doc.RootElement.GetProperty(collection).EnumerateObject())
        foreach (var variant in creature.Value.GetProperty("variants").EnumerateObject())
            keys.Add($"{folder}.{creature.Name}.{variant.Name}");
}
var sprites = keys.Select(key =>
{
    var sprite = art.Sprite(key) ?? throw new InvalidOperationException("Missing runtime sprite " + key);
    var frontRow = sprite.Directions.IsEmpty ? 0 : sprite.Directions.IndexOf("south");
    var backRow = sprite.Directions.IsEmpty ? 1 : sprite.Directions.IndexOf("north");
    return new { key, sprite.Path, sprite.Width, sprite.Height, sprite.AnchorX, sprite.AnchorY, sprite.PixelScale,
        directions = sprite.Directions.IsEmpty ? 2 : sprite.Directions.Length,
        front = sprite.Source(sprite.Idle.Frames[0], frontRow), back = sprite.Source(sprite.Idle.Frames[0], backRow),
        sprite.Columns, sprite.RowsPerDirection, idle = sprite.Idle };
}).ToArray();
File.WriteAllText(args[1], JsonSerializer.Serialize(new { rules, sprites }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
Console.WriteLine($"{rules.Count} stat blocks; {sprites.Length} sprite keys resolved through UnitArt.");
