#:project ../../../../../games/Tactics/Tactics.Presentation/Tactics.Presentation.csproj
// Read-only integration checks against an explicit review or production pack.
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.World;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--production"))
    throw new ArgumentException("Pass the Tobin review pack, or the production pack followed by --production.");
var production = args.Length == 2;
var fileStem = production ? "tobin_fell" : "review_tobin";
var root = Path.GetFullPath(args[0]);
var art = UnitArt.Load(root);
var sprite = art.Sprite("characters.blacksmith") ?? throw new InvalidDataException("Missing Tobin.");
Need(sprite.Path == $"creatures/{fileStem}.png", "Expected body is not selected for the blacksmith.");
Need(ReferenceEquals(sprite, art.Sprite("characters.tobin_fell")), "Named alias differs.");
Need(sprite.Width == 96 && sprite.Height == 96 && sprite.Columns == 18 && sprite.RowsPerDirection == 1, "Wrong frame layout.");
Need(sprite.PixelScale == .5f && sprite.AnchorX == 48 && sprite.AnchorY == 84, "Wrong native scale/anchor.");
Need(sprite.Directions.Length == 8 && sprite.Directions.Distinct().Count() == 8, "Missing directions.");
Need(sprite.Idle.Frames.Length == 10 && sprite.Anims["walk"].Frames.Length == 6 && sprite.Anims["talk"].Frames.Length == 10, "Wrong authored playback.");
Need(!sprite.Anims.ContainsKey("attack"), "Civilian must not require attack.");
foreach (var clip in sprite.Anims.Values)
    foreach (var frame in clip.Frames)
        for (var row = 0; row < 8; row++)
        {
            var rect = sprite.Source(frame, row);
            Need(rect.X >= 0 && rect.Y >= 0 && rect.X + rect.Width <= 1728 && rect.Y + rect.Height <= 768, "Out-of-atlas source.");
        }
var hud = HudArt.Load(root, art);
foreach (var mood in new[] { "idle", "hurt", "happy", "angry", "sad", "surprised" })
{
    var face = hud.Speaker("blacksmith", mood, "ally") ?? throw new InvalidDataException($"Missing {mood} portrait.");
    Need(face.Path == $"portraits/{fileStem}_{mood}.png" && face.Source.Width == 40 && face.Source.Height == 40, "Wrong portrait selection or frame.");
    Need(File.Exists(Path.Combine(root, face.Path)), "Missing portrait file.");
}
var ground = RulesPack.Load([Path.Combine(root, "rules")], ArtManifests.Load(root)).GroundFor("ashen_ford")!;
var terrain = new BoardTerrain(ground.Map, null);
var person = ground.People.Single(p => p.Id == "tobin_fell") with { Walk = [new Tile(37, 55), new Tile(37, 56)] };
var people = new WorldPeople([person], terrain, art);
var around = terrain.Centre(person.At);
var time = Enumerable.Range(1, 1000).Select(i => i / 100d).First(t => people.PlaceAt(person, t + people.Phase(person)).Walking);
var before = people.Units(time, around, []).Single();
var bark = new BarkView("tobin_fell", 1, DateTimeOffset.MaxValue, new StoryLineView("hello", "blacksmith", "Tobin Fell", null, "idle", "Welcome."));
var first = people.Units(time + .02, around, [], leader: new Tile(39, 58), bark: bark).Single();
var held = people.Units(time + 2, around, [], leader: new Tile(39, 58), bark: bark).Single();
// Fractional world projection can differ by float roundoff on sloped ground.
var startDrift=Vector2.Distance(before.Feet, first.Feet);
var heldDrift=Vector2.Distance(first.Feet, held.Feet);
Need(startDrift < .01 && heldDrift < .01, $"Bark slides or shifts the feet: {startDrift}/{heldDrift} art pixels.");
Need(first.Anim == sprite.Anims["talk"] && held.Anim == first.Anim, "Bark did not use authored talk.");
Need(first.Row is >= 0 and < 8 && !first.Mirror, "Bark did not select a real facing.");
var resumed = people.Units(time + 2.02, around, [], leader: new Tile(39, 58)).Single();
Need(resumed.Anim == sprite.Anims["walk"] && Vector2.Distance(held.Feet, resumed.Feet) < 2, "Route did not resume smoothly.");
Console.WriteLine("""{"result":"PASS","directions":8,"sourceFrames":144,"playback":"10 idle / 6 walk / 10 talk","portraits":6,"barkStopResume":"PASS","nativeScale":0.5,"packMode":"PACK_MODE"}""".Replace("PACK_MODE", production ? "production" : "review"));
static void Need(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
