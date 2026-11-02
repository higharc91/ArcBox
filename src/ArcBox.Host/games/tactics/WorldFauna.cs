using Godot;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>Small, deterministic world actors. Where they may go is <see cref="FaunaRange"/>'s; art bigger than the world is shrunk by <see cref="SpriteShrink"/>.</summary>
internal sealed partial class WorldFauna : Node2D
{
    private const int BlockDepth = 64;

    private sealed class Actor
    {
        public required string Id { get; init; }
        public required Tile Home { get; init; }
        public required Sprite2D Sprite { get; init; }
        public required Random Random { get; init; }
        public required Tile[] Domain { get; init; }
        public required Vector2 RestOffset { get; init; }
        public required int FrameWidth { get; init; }
        public required int FrameHeight { get; init; }
        public required int Frames { get; init; }
        public required float AnimationFps { get; init; }
        public required bool WaterOnly { get; init; }
        public required float Speed { get; init; }
        public required float PauseMin { get; init; }
        public required float PauseMax { get; init; }
        public Tile Current { get; set; }
        public Tile Target { get; set; }
        public Queue<Tile> Path { get; set; } = new();
        public Vector2 From { get; set; }
        public Vector2 To { get; set; }
        public float Progress { get; set; }
        public double PauseUntil { get; set; }
        public double StartedAt { get; set; }
        public int Direction { get; set; } = 1;
        public float AnimationPhase { get; set; }
    }

    private readonly BattleMap _map;
    private readonly List<Actor> _actors = [];
    private double _clock;

    public WorldFauna(string packRoot, BattleMap map, Color light)
    {
        _map = map;
        var artIndex = ArtIndex.Load(packRoot);
        var animals = map.Props.Where(p => WorldAnimalIds.Contains(p.Id) && map.InBounds(p.At)).ToArray();
        var free = FaunaRange.Free(map, artIndex.PropHeight);

        foreach (var prop in animals)
        {
            var (radius, water, speed, pauseMin, pauseMax) = prop.Id switch
            {
                "farm_goat" => (4, false, 0.48f, 1.8f, 4.0f),
                "pack_mule" => (3, false, 0.30f, 3.0f, 6.0f),
                "field_crow" => (4, false, 0.78f, 1.2f, 3.5f),
                "river_duck" => (5, true, 0.42f, 2.0f, 5.0f),
                "red_fox" => (5, false, 0.38f, 3.5f, 7.0f),
                _ => throw new InvalidOperationException($"No world movement profile for '{prop.Id}'."),
            };

            var domain = FaunaRange.Domain(map, free, prop.At, radius, water).ToArray();
            var art = artIndex.Prop(WorldAnimalIds.AnimationFamily(prop.Id),
                prop.Variant is null ? map.Materials : map.Materials.Prepend(prop.Variant));
            if (art is null)
                continue;
            var imagePath = Path.Combine(packRoot, art.Image.Path);
            if (!File.Exists(imagePath))
                continue;
            var image = Image.LoadFromFile(imagePath);
            if (image.IsEmpty())
                continue;

            // Art made bigger than the world is shrunk to whole pixels once, here, at the manifest's scale.
            var (frameWidth, frameHeight, anchorX, anchorY) = (art.Width, art.Height, art.AnchorX, art.AnchorY);
            if (art.Scale < 1)
            {
                image.Convert(Image.Format.Rgba8);
                var strip = SpriteShrink.Shrink(image.GetData(), image.GetWidth(), image.GetHeight(), art.Image.Frames, art.Scale);
                (anchorX, anchorY) = SpriteShrink.Anchor(art.AnchorX, art.AnchorY, art.Width, art.Height, art.Scale);
                image = Image.CreateFromData(strip.Width, strip.Height, false, Image.Format.Rgba8, strip.Rgba);
                (frameWidth, frameHeight) = (strip.FrameWidth, strip.Height);
            }

            var sprite = new Sprite2D
            {
                Name = $"fauna_{prop.Id}_{prop.At.X}_{prop.At.Y}",
                Texture = ImageTexture.CreateFromImage(image),
                Centered = false,
                RegionEnabled = true,
                RegionRect = new Rect2(0, 0, frameWidth, frameHeight),
                Offset = new Vector2(-anchorX, -anchorY),
                Modulate = light,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                ZIndex = DepthAt(prop.At),
            };
            AddChild(sprite);
            var seed = StableSeed(prop.Id, prop.At);
            var actor = new Actor
            {
                Id = prop.Id, Home = prop.At, Current = prop.At, Target = prop.At,
                Sprite = sprite, Random = new Random(seed), Domain = domain, WaterOnly = water,
                RestOffset = new Vector2(-anchorX, -anchorY),
                FrameWidth = frameWidth, FrameHeight = frameHeight, Frames = art.Image.Frames,
                AnimationFps = (float)art.Image.Fps,
                Speed = speed, PauseMin = pauseMin, PauseMax = pauseMax,
                From = Foot(prop.At), To = Foot(prop.At),
                PauseUntil = 1.0 + (seed % 1200) / 1000.0,
                AnimationPhase = (seed % 628) / 100f,
            };
            sprite.Position = actor.From;
            _actors.Add(actor);
        }
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        foreach (var actor in _actors)
        {
            actor.AnimationPhase += (float)delta;
            if (actor.Path.Count == 0 && actor.Progress <= 0 && _clock >= actor.PauseUntil)
                ChooseDestination(actor);

            if (actor.Path.Count > 0 && actor.Progress <= 0)
            {
                actor.Target = actor.Path.Dequeue();
                actor.From = Foot(actor.Current);
                actor.To = Foot(actor.Target);
                actor.Progress = 0.0001f;
                actor.StartedAt = _clock;
                actor.Direction = actor.To.X >= actor.From.X ? 1 : -1;
                actor.Sprite.FlipH = actor.Direction < 0;
                actor.Sprite.ZIndex = DepthAt(actor.Target);
            }

            var moving = actor.Progress > 0;
            if (moving)
            {
                actor.Progress = Math.Min(1f, actor.Progress + (float)(delta * actor.Speed));
                var t = actor.Progress * actor.Progress * (3 - 2 * actor.Progress);
                actor.Sprite.Position = actor.From.Lerp(actor.To, t);
                if (actor.Progress >= 1)
                {
                    actor.Current = actor.Target;
                    actor.Progress = 0;
                    if (actor.Path.Count == 0)
                        actor.PauseUntil = _clock + actor.PauseMin + (float)actor.Random.NextDouble() * (actor.PauseMax - actor.PauseMin);
                }
            }

            AnimatePose(actor, moving);
        }
    }

    public Vector2? PositionOf(Tile home) => _actors.FirstOrDefault(a => a.Home == home)?.Sprite.Position;

    private void ChooseDestination(Actor actor)
    {
        if (actor.Domain.Length < 2)
        {
            actor.PauseUntil = _clock + 2;
            return;
        }

        var choices = actor.Domain.Where(t => t != actor.Current && _actors.All(other => other == actor || Distance(t, other.Current) > 1))
            .OrderBy(_ => actor.Random.Next()).ToArray();
        foreach (var destination in choices.Take(24))
        {
            var path = FaunaRange.Path(_map, actor.Current, destination, actor.Domain.ToHashSet());
            if (path.IsEmpty)
                continue;
            actor.Path = new Queue<Tile>(path);
            return;
        }
        actor.PauseUntil = _clock + 2;
    }

    private void AnimatePose(Actor actor, bool moving)
    {
        var animateAtRest = actor.Id is "field_crow" or "river_duck";
        var frame = 0;
        if (actor.Frames > 1 && (moving || animateAtRest))
        {
            // Ping-pong avoids a hard snap between generated endpoints while keeping all distinct poses in play.
            var cycle = actor.Frames * 2 - 2;
            var step = (int)(actor.AnimationPhase * actor.AnimationFps) % cycle;
            frame = step < actor.Frames ? step : cycle - step;
        }
        actor.Sprite.RegionRect = new Rect2(frame * actor.FrameWidth, 0, actor.FrameWidth, actor.FrameHeight);
        actor.Sprite.Offset = actor.RestOffset;
        actor.Sprite.Rotation = 0;
    }

    private Vector2 Foot(Tile tile)
    {
        var corner = BoardProjection.Corner(tile.X, tile.Y, _map[tile].Height);
        return new Vector2(corner.X + 32, corner.Y + 16);
    }

    private int DepthAt(Tile tile)
    {
        var bx = tile.X / WorldTuning.BlockSize;
        var by = tile.Y / WorldTuning.BlockSize;
        return (bx + by) * BlockDepth + tile.X % WorldTuning.BlockSize + tile.Y % WorldTuning.BlockSize + 1;
    }

    private static int Distance(Tile a, Tile b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static int StableSeed(string id, Tile tile)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var c in id)
                hash = (hash ^ c) * 16777619;
            return (int)((hash ^ (uint)(tile.X * 397 ^ tile.Y)) & 0x7fffffff);
        }
    }
}
