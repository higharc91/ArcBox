using Godot;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Draws and plays pure ambient life on the world (<see cref="AmbientPlan"/>): butterflies or fireflies, fish jumping in deep water, steam off the river, falling leaves,
/// embers, motes in lamplight and birds across the sky,
/// and quiet positional calls from nearby farm animals.
/// It stands in the world's light, all but what glows by itself, which goes through its own additive layer.
/// TV-only: no gameplay logic, no view data, no network inputs.
/// </summary>
internal sealed partial class WorldAmbient : Node2D
{
    private const float AnimalReach = 440;

    private sealed class AnimalCall(AudioStreamPlayer2D player, string sound, Tile home, Vector2 position, int seed, int firstDelay, int firstSpread, int interval, int intervalSpread)
    {
        public AudioStreamPlayer2D Player { get; } = player;
        public string Sound { get; } = sound;
        public Tile Home { get; } = home;
        public Vector2 Position { get; set; } = position;
        public int Seed { get; } = seed;
        public int FirstDelay { get; } = firstDelay;
        public int FirstSpread { get; } = firstSpread;
        public int Interval { get; } = interval;
        public int IntervalSpread { get; } = intervalSpread;
        public double NextAt { get; set; }
        public bool WasNear { get; set; }
        public int Calls { get; set; }
    }

    private readonly AmbientPlan? _plan;
    private readonly string _ambientRoot;
    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly Color _light;
    private readonly Func<Tile, Vector2?> _animalAt;
    private readonly Node2D _glow = new() { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
    private readonly List<AnimalCall> _animalCalls = [];
    private AmbientItem[] _items = [];

    /// <param name="animalAt">Where the animal that lives on a tile is now, in art pixels, so its call comes from it.</param>
    public WorldAmbient(AmbientPlan? plan, string ambientRoot, string packRoot, BattleMap map, Color light, int zIndex, Func<Tile, Vector2?> animalAt)
    {
        _plan = plan;
        _ambientRoot = ambientRoot;
        _light = light;
        _animalAt = animalAt;
        ZIndex = zIndex;

        foreach (var prop in map.Props.Where(p => p.Id is "farm_goat" or "pack_mule" or "field_crow" or "river_duck" or "red_fox"))
        {
            var (id, volume, firstDelay, firstSpread, interval, intervalSpread) = prop.Id switch
            {
                "farm_goat" => ("animal_goat_bleat", -13f, 3, 6, 22, 27),
                "pack_mule" => ("animal_mule_call", -16f, 5, 10, 30, 30),
                "field_crow" => ("animal_crow_caw", -18f, 2, 7, 25, 25),
                "river_duck" => ("animal_duck_quack", -20f, 4, 10, 35, 25),
                "red_fox" => ("animal_fox_yip", -24f, 10, 15, 80, 60),
                _ => throw new InvalidOperationException($"No ambient call for '{prop.Id}'."),
            };
            var path = System.IO.Path.Combine(packRoot, "sfx", $"{id}.wav");
            if (!System.IO.File.Exists(path) || !map.InBounds(prop.At))
                continue;


            // Its sound is read later, a file a frame (LoadNext), not as the world opens.
            var player = new AudioStreamPlayer2D
            {
                Name = $"{id}_{prop.At.X}_{prop.At.Y}",
                Bus = ArcBox.Host.Audio.AudioBuses.Sfx,
                VolumeDb = volume,
                MaxDistance = AnimalReach,
                Attenuation = 1.15f,
                PanningStrength = 0.85f,
            };
            var ground = BoardProjection.Corner(prop.At.X, prop.At.Y, map[prop.At].Height);
            _animalCalls.Add(new AnimalCall(player, path, prop.At, new Vector2(ground.X + 32, ground.Y + 16), HashCode.Combine(prop.At.X, prop.At.Y, prop.Id.GetHashCode(StringComparison.Ordinal)),
                firstDelay, firstSpread, interval, intervalSpread));
        }
    }

    public override void _Ready()
    {
        AddChild(_glow);
        _glow.Draw += () => DrawItems(_glow, add: true);
        foreach (var call in _animalCalls)
        {
            call.Player.Position = call.Position;
            AddChild(call.Player);
        }
    }

    /// <summary>Reads the next animal call's sound, once each; false when there's none left. The world calls it with its textures, within their budget.</summary>
    public bool LoadNext()
    {
        // Its images first: read as one first came into view, a strip cost that frame a file read and an upload.
        _paths ??= new Queue<string>(_plan?.Paths ?? []);
        if (_paths.TryDequeue(out var image))
        {
            Texture(System.IO.Path.Combine(_ambientRoot, image));
            return true;
        }
        var call = _animalCalls.FirstOrDefault(c => c.Player.Stream is null && !_unreadable.Contains(c.Sound));
        if (call is null)
            return false;
        _sounds.TryGetValue(call.Sound, out var stream);
        stream ??= _sounds[call.Sound] = AudioStreamWav.LoadFromFile(call.Sound);
        if (stream is null)
            _unreadable.Add(call.Sound);
        call.Player.Stream = stream;
        return true;
    }

    private readonly Dictionary<string, AudioStreamWav?> _sounds = [];
    private readonly HashSet<string> _unreadable = [];
    private Queue<string>? _paths;

    /// <summary>The texture of an ambient image, read once; null if there's no such file.</summary>
    private Texture2D? Texture(string fullPath)
    {
        if (_textures.TryGetValue(fullPath, out var known))
            return known;
        Texture2D? texture = null;
        if (System.IO.File.Exists(fullPath))
        {
            using var image = Image.LoadFromFile(fullPath);
            texture = ImageTexture.CreateFromImage(image);
        }
        return _textures[fullPath] = texture;
    }

    /// <summary>Updates active ambient creatures for the camera's view.</summary>
    public void Update(ArtArea view, double clock)
    {
        UpdateAnimalCalls(view, clock);
        if (_plan is null)
        {
            return;
        }

        _items = [.. _plan.ActiveAt(clock, view)];
        QueueRedraw();
        _glow.QueueRedraw();
    }

    public override void _Draw() => DrawItems(this, add: false);

    private void UpdateAnimalCalls(ArtArea view, double clock)
    {
        var center = new Vector2((view.Min.X + view.Max.X) * 0.5f, (view.Min.Y + view.Max.Y) * 0.5f);
        foreach (var call in _animalCalls)
        {
            call.Position = _animalAt(call.Home) ?? call.Position;
            call.Player.Position = call.Position;
            var near = call.Position.DistanceTo(center) <= AnimalReach;
            if (!near)
            {
                call.WasNear = false;
                continue;
            }

            if (!call.WasNear)
            {
                call.WasNear = true;
                call.NextAt = clock + call.FirstDelay + Math.Abs(call.Seed % call.FirstSpread);
            }

            if (clock < call.NextAt || call.Player.Playing || call.Player.Stream is null)
                continue;

            call.Player.Play();
            call.Calls++;
            // Stock calls are sparse and offset per animal, so they read as a nearby farm, not a repeated sound bed.
            call.NextAt = clock + call.Interval + Math.Abs((call.Seed + call.Calls * 17) % call.IntervalSpread);
        }
    }

    private void DrawItems(CanvasItem canvas, bool add)
    {
        foreach (var item in _items)
        {
            if (string.IsNullOrEmpty(item.Path) || item.Add != add)
            {
                continue;
            }

            if (Texture(System.IO.Path.Combine(_ambientRoot, item.Path)) is { } texture)
            {
                var srcRect = new Rect2(item.Frame * item.FrameSize.X, 0, item.FrameSize.X, item.FrameSize.Y);
                var corner = new Vector2(
                    item.Position.X - (item.Mirror ? item.FrameSize.X - item.Anchor.X : item.Anchor.X),
                    item.Position.Y - item.Anchor.Y);
                var dstRect = new Rect2(corner, item.Mirror ? new Vector2(-item.FrameSize.X, item.FrameSize.Y) : new Vector2(item.FrameSize.X, item.FrameSize.Y));
                var colour = item.Add ? new Color(1, 1, 1, item.Alpha) : new Color(_light, item.Alpha);
                canvas.DrawTextureRectRegion(texture, dstRect, srcRect, colour);
            }
        }
    }
}
