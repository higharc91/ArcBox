#:project ../../../../../games/Tactics/Tactics.Presentation/Tactics.Presentation.csproj
using System.Text.Json;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.World;

if (args.Length != 1) throw new ArgumentException("Pass the generic townsfolk review pack.");
var root = Path.GetFullPath(args[0]);
var art = UnitArt.Load(root);
var hud = HudArt.Load(root, art);
var ground = RulesPack.Load([Path.Combine(root, "rules")], ArtManifests.Load(root)).GroundFor("ashen_ford")!;
string[] presets = ["barmaid", "child", "farmer", "farmhand", "fisher", "guard", "merchant"];
string[] moods = ["idle", "hurt", "happy", "angry", "sad", "surprised"];
var count = 0;
foreach (var preset in presets)
{
    foreach (var variant in new[] { "a", "b" })
    {
        var key = $"{preset}_{variant}";
        var sprite = art.Sprite($"characters.{key}") ?? throw new InvalidDataException(key);
        Need(sprite.Path == $"creatures/generic_{key}.png", "Wrong sprite alias.");
        Need(sprite.Width == 96 && sprite.Height == 96 && sprite.Columns == 6 && sprite.RowsPerDirection == 1, "Wrong frame layout.");
        Need(sprite.Directions.Distinct().Count() == 8 && sprite.PixelScale == .5f && sprite.AnchorX == 48 && sprite.AnchorY == 84, "Missing directions or wrong anchor.");
        Need(sprite.HeadLift == (preset == "child" ? 26 : 36), "Wrong child/adult scale.");
        Need(sprite.Idle.Frames.Length > 1 && sprite.Anims["walk"].Frames.SequenceEqual(new[] { 2, 3, 4, 5 }), "Missing real animation frames.");
        Need(!sprite.Anims.ContainsKey("attack") && !sprite.Anims.ContainsKey("talk"), "Generic civilian animation contract changed.");
        foreach (var clip in sprite.Anims.Values)
            foreach (var frame in clip.Frames)
                for (var direction = 0; direction < 8; direction++)
                {
                    var rect = sprite.Source(frame, direction);
                    Need(rect.X >= 0 && rect.Y >= 0 && rect.X + rect.Width <= 576 && rect.Y + rect.Height <= 768, "Out-of-atlas source rectangle.");
                }
        foreach (var mood in moods)
        {
            var face = hud.Speaker(key, mood, "npc") ?? throw new InvalidDataException("Missing mood.");
            Need(face.Path == $"portraits/generic_{key}_{mood}.png" && face.Source.Width == 40 && face.Source.Height == 40, "Wrong portrait.");
            Need(File.Exists(Path.Combine(root, face.Path)), "Missing portrait image.");
        }
    }
    var people = ground.People.Where(p => p.Preset == preset && p.Character is null).ToArray();
    Need(people.Select(p => p.Variant).Distinct().Count() == 2, "Both variants must be placed.");
    foreach (var person in people)
    {
        Need(WorldPeople.SpriteKey(person, art) == $"characters.{preset}_{person.Variant}", "World/preload variant resolution differs.");
        count++;
    }
}
Need(count == 28, "Missing generic people.");
foreach (var named in ground.People.Where(p => p.Character is not null))
    Need(!art.Sprite(WorldPeople.SpriteKey(named, art))!.Path.Contains("generic_"), "Named NPC was changed by generic art.");
Console.WriteLine("PASS: 14 eight-facing atlases, 672 source slots, 84 portraits, 28 placed variants, all named bodies preserved.");
static void Need(bool pass, string message) { if (!pass) throw new InvalidDataException(message); }
