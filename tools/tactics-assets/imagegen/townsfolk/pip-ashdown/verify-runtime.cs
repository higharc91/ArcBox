#:project ../../../../../games/Tactics/Tactics.Presentation/Tactics.Presentation.csproj
// Read-only integration checks against an explicit review or production pack.
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.World;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--production"))
    throw new ArgumentException("Pass the Pip Ashdown review pack, or the production pack followed by --production.");
var production = args.Length == 2;
var fileStem = production ? "pip" : "review_pip";
var root = Path.GetFullPath(args[0]);
var art = UnitArt.Load(root);
var sprite = art.Sprite("characters.child_pip") ?? throw new InvalidDataException("Missing Pip Ashdown.");
Need(sprite.Path == $"creatures/{fileStem}.png", "Expected body is not selected for the child_pip.");
Need(ReferenceEquals(sprite, art.Sprite("characters.pip_ashdown")), "Named alias differs.");
Need(sprite.Width == 64 && sprite.Height == 64 && sprite.Columns == 18 && sprite.RowsPerDirection == 1, "Wrong frame layout.");
Need(sprite.PixelScale == .5f && sprite.AnchorX == 32 && sprite.AnchorY == 56, "Wrong native scale/anchor.");
Need(sprite.Directions.Length == 8 && sprite.Directions.Distinct().Count() == 8, "Missing directions.");
Need(sprite.Idle.Frames.Length == 10 && sprite.Anims["walk"].Frames.Length == 6 && sprite.Anims["talk"].Frames.Length == 10, "Wrong authored playback.");
Need(!sprite.Anims.ContainsKey("attack"), "Civilian must not require attack.");
foreach (var clip in sprite.Anims.Values)
    foreach (var frame in clip.Frames)
        for (var row = 0; row < 8; row++)
        {
            var rect = sprite.Source(frame, row);
            Need(rect.X >= 0 && rect.Y >= 0 && rect.X + rect.Width <= 1152 && rect.Y + rect.Height <= 512, "Out-of-atlas source.");
        }
var hud = HudArt.Load(root, art);
foreach (var mood in new[] { "idle", "hurt", "happy", "angry", "sad", "surprised" })
{
    var face = hud.Speaker("pip_ashdown", mood, "ally") ?? throw new InvalidDataException($"Missing {mood} portrait.");
    Need(face.Path == $"portraits/{fileStem}_{mood}.png" && face.Source.Width == 40 && face.Source.Height == 40, "Wrong portrait selection or frame.");
    Need(File.Exists(Path.Combine(root, face.Path)), "Missing portrait file.");
}
var ground = RulesPack.Load([Path.Combine(root, "rules")], ArtManifests.Load(root)).GroundFor("ashen_ford")!;
Need(art.Sprite("characters.child")?.Path != sprite.Path, "Generic children were replaced.");
Need(ground.People.Single(p => p.Id == "pip_ashdown").Variant == "pip", "Pip variant not selected.");
Need(ground.People.Where(p => p.Preset == "child" && p.Id != "pip_ashdown").All(p => WorldPeople.SpriteKey(p, art) != "characters.child_pip"), "Another child selected Pip art.");
var terrain = new BoardTerrain(ground.Map, null);
var person = ground.People.Single(p => p.Id == "pip_ashdown") with { Walk = [new Tile(30, 60), new Tile(30, 61)] };
var people = new WorldPeople([person], terrain, art);
var around = terrain.Centre(person.At);
var time = Enumerable.Range(1, 1000).Select(i => i / 100d).First(t => people.PlaceAt(person, t + people.Phase(person)).Walking);
var before = people.Units(time, around, []).Single();
var bark = new BarkView("pip_ashdown", 1, DateTimeOffset.MaxValue, new StoryLineView("hello", "child_pip", "Pip Ashdown", null, "idle", "Welcome."));
var first = people.Units(time + .02, around, [], leader: new Tile(33, 62), bark: bark).Single();
var held = people.Units(time + 2, around, [], leader: new Tile(33, 62), bark: bark).Single();
// Fractional world projection can differ by float roundoff on sloped ground.
var startDrift=Vector2.Distance(before.Feet, first.Feet);
var heldDrift=Vector2.Distance(first.Feet, held.Feet);
Need(startDrift < .01 && heldDrift < .01, $"Bark slides or shifts the feet: {startDrift}/{heldDrift} art pixels.");
Need(first.Anim == sprite.Anims["talk"] && held.Anim == first.Anim, "Bark did not use authored talk.");
Need(first.Row is >= 0 and < 8 && !first.Mirror, "Bark did not select a real facing.");
var resumed = people.Units(time + 2.02, around, [], leader: new Tile(33, 62)).Single();
Need(resumed.Anim == sprite.Anims["walk"] && Vector2.Distance(held.Feet, resumed.Feet) < 2, "Route did not resume smoothly.");
Console.WriteLine("""{"result":"PASS","directions":8,"nativeFrameSlots":144,"playback":"10 idle / 6 walk / 10 talk","portraits":6,"barkStopResume":"PASS","nativeScale":0.5,"packMode":"PACK_MODE"}""".Replace("PACK_MODE", production ? "production" : "review"));
static void Need(bool condition, string meschild_pip) { if (!condition) throw new InvalidDataException(meschild_pip); }
