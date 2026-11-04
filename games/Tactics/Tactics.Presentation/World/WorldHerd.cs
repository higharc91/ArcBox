using System.Collections.Immutable;
using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// The world's animals (<see cref="WorldAnimalIds"/>): where each may go (<see cref="FaunaRange"/>), where it is now and what it shows, as
/// <see cref="BoardUnit"/>s, so the TV draws them among its people and walkers in painter's order, and a goat behind a haystack is hidden by it.
/// Pure and deterministic: <see cref="Build"/> is all the work, and safe off the main thread, as the world opens; <see cref="Step"/> moves them
/// on. An animal with a creature sheet (<c>fauna.&lt;id&gt;</c>) walks with its compass rows; one with only its props strip (<c>&lt;id&gt;_anim</c>)
/// plays the strip back and forth, mirrored to face the way it goes.
/// </summary>
public sealed class WorldHerd
{
    private sealed record Profile(int Radius, bool Water, float Speed, float PauseMin, float PauseMax);

    private static readonly ImmutableDictionary<string, Profile> Profiles = new Dictionary<string, Profile>
    {
        ["farm_goat"] = new(4, false, 0.48f, 1.8f, 4.0f),
        ["pack_mule"] = new(3, false, 0.30f, 3.0f, 6.0f),
        ["field_crow"] = new(4, false, 0.78f, 1.2f, 3.5f),
        ["river_duck"] = new(5, true, 0.42f, 2.0f, 5.0f),
        ["red_fox"] = new(5, false, 0.38f, 3.5f, 7.0f),
        ["farm_dog"] = new(4, false, 0.52f, 2.0f, 5.0f),
        ["farm_sheep"] = new(3, false, 0.34f, 2.5f, 5.5f),
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private sealed class Animal
    {
        public required string Id { get; init; }
        public required Tile Home { get; init; }
        public required UnitSprite Sprite { get; init; }
        public required bool Sheet { get; init; }
        public required ImmutableArray<Tile> Domain { get; init; }
        public required HashSet<Tile> Allowed { get; init; }
        public required Profile Profile { get; init; }
        public required Random Random { get; init; }
        public Tile Current { get; set; }
        public Tile Target { get; set; }
        public Queue<Tile> Path { get; set; } = new();
        public float Progress { get; set; }
        public double PauseUntil { get; set; }
        public float Phase { get; set; }
        public int Row { get; set; }
        public bool Mirror { get; set; }
    }

    private readonly BoardTerrain _terrain;
    private readonly List<Animal> _animals;
    private double _clock;

    private WorldHerd(BoardTerrain terrain, List<Animal> animals)
    {
        _terrain = terrain;
        _animals = animals;
    }

    /// <summary>How many animals there are.</summary>
    public int Count => _animals.Count;

    /// <summary>The images the animals are drawn from, relative to the pack's root, once each.</summary>
    public ImmutableArray<string> Paths => [.. _animals.Select(a => a.Sprite.Path).Distinct(StringComparer.Ordinal)];

    /// <summary>The animals of <paramref name="terrain"/>'s map that the pack can draw, at home.</summary>
    public static WorldHerd Build(BoardTerrain terrain, ArtIndex art, UnitArt units)
    {
        var map = terrain.Map;
        var free = FaunaRange.Free(map, art.PropHeight);
        var animals = new List<Animal>();
        foreach (var prop in map.Props.Where(p => WorldAnimalIds.Contains(p.Id) && map.InBounds(p.At)))
        {
            if (!Profiles.TryGetValue(prop.Id, out var profile))
                throw new InvalidOperationException($"No world movement profile for '{prop.Id}'.");
            var strip = art.Prop(WorldAnimalIds.AnimationFamily(prop.Id), prop.Variant is null ? map.Materials : map.Materials.Prepend(prop.Variant));
            var sheet = units.Sprite("fauna." + prop.Id);
            var sprite = sheet ?? (strip is null ? null : StripSprite(strip));
            if (sprite is null)
                continue;
            var domain = FaunaRange.Domain(map, free, prop.At, profile.Radius, profile.Water);
            var seed = StableSeed(prop.Id, prop.At);
            animals.Add(new Animal
            {
                Id = prop.Id, Home = prop.At, Sprite = sprite, Sheet = sheet is not null, Domain = domain, Allowed = [.. domain], Profile = profile,
                Random = new Random(seed), Current = prop.At, Target = prop.At,
                PauseUntil = 1.0 + seed % 1200 / 1000.0, Phase = seed % 628 / 100f,
                Row = sheet is null ? 0 : sheet.Looking(0, 0).Row,
            });
        }
        return new WorldHerd(terrain, animals);
    }

    /// <summary>
    /// A props strip as a sheet of one row: idle is its first frame, and walking plays it there and back, so there's no snap between its ends. Art made bigger than
    /// the world (the manifest's <c>scale</c>, below 1) is drawn that much smaller, about the same anchor: its size and anchor stay the file's.
    /// </summary>
    public static UnitSprite StripSprite(PropArt strip)
    {
        var frames = Math.Max(1, strip.Image.Frames);
        ImmutableArray<int> there = [.. Enumerable.Range(0, frames), .. Enumerable.Range(1, Math.Max(0, frames - 2)).Reverse()];
        var walk = new UnitAnim(there, strip.Image.Fps > 0 ? strip.Image.Fps : 8, true);
        var idle = new UnitAnim([0], 1, true);
        return new UnitSprite(strip.Image.Path, strip.Width, strip.Height, strip.AnchorX, strip.AnchorY, 0, idle, idle, 0,
            ImmutableDictionary<string, UnitAnim>.Empty.Add("walk", walk)) { PixelScale = strip.Scale is > 0 and < 1 ? (float)strip.Scale : 1 };
    }

    /// <summary>Moves every animal on by <paramref name="delta"/> seconds.</summary>
    public void Step(double delta)
    {
        _clock += delta;
        foreach (var animal in _animals)
        {
            animal.Phase += (float)delta;
            if (animal.Path.Count == 0 && animal.Progress <= 0 && _clock >= animal.PauseUntil)
                Choose(animal);
            if (animal.Path.Count > 0 && animal.Progress <= 0)
            {
                animal.Target = animal.Path.Dequeue();
                animal.Progress = 0.0001f;
                var (du, dv) = (animal.Target.X - animal.Current.X, animal.Target.Y - animal.Current.Y);
                if (animal.Sheet)
                    (animal.Row, animal.Mirror) = animal.Sprite.Looking(du, dv);
                else
                    animal.Mirror = du - dv < 0;    // the strip looks to the right of the screen
            }
            if (animal.Progress > 0)
            {
                animal.Progress = Math.Min(1f, animal.Progress + (float)(delta * animal.Profile.Speed));
                if (animal.Progress >= 1)
                {
                    animal.Current = animal.Target;
                    animal.Progress = 0;
                    if (animal.Path.Count == 0)
                        animal.PauseUntil = _clock + animal.Profile.PauseMin + animal.Random.NextDouble() * (animal.Profile.PauseMax - animal.Profile.PauseMin);
                }
            }
        }
    }

    /// <summary>Where the animal that lives at <paramref name="home"/> is, in tiles, as a unit stands; null if there's none.</summary>
    public UnitPlace? PlaceOf(Tile home) => _animals.FirstOrDefault(a => a.Home == home) is { } animal ? Place(animal) : null;

    /// <summary>The animals with anything in <paramref name="view"/> (all of them without one), as units, back to front.</summary>
    public ImmutableArray<BoardUnit> Units(ArtArea? view = null)
    {
        var units = ImmutableArray.CreateBuilder<BoardUnit>(_animals.Count);
        foreach (var animal in _animals)
        {
            var place = Place(animal);
            var unit = UnitPlan.Animal($"fauna:{animal.Id}:{animal.Home.X},{animal.Home.Y}", animal.Sprite, place, Column(animal), animal.Row, animal.Mirror);
            if (view is not { } shown || WorldPlan.AreaOf(unit).Overlaps(shown))
                units.Add(unit);
        }
        units.Sort((a, b) => a.Order.CompareTo(b.Order));
        return units.ToImmutable();
    }

    private UnitPlace Place(Animal animal)
    {
        var (from, to) = (UnitPlace.On(_terrain, animal.Current), UnitPlace.On(_terrain, animal.Target));
        if (animal.Progress <= 0)
            return from;
        var t = animal.Progress * animal.Progress * (3 - 2 * animal.Progress);
        return new UnitPlace(from.Map + (to.Map - from.Map) * t, from.Height + (to.Height - from.Height) * t);
    }

    /// <summary>The column of its sheet it shows now: walking, flapping now and then for a crow at rest, bobbing for a bird on a strip, or standing.</summary>
    private static int Column(Animal animal)
    {
        var moving = animal.Progress > 0;
        var sprite = animal.Sprite;
        if (animal.Sheet)
        {
            var anim = moving ? sprite.Anim("walk") : sprite.Idle;
            var phase = (double)animal.Phase;
            if (!moving && animal.Id == "field_crow" && sprite.Anims.TryGetValue("flap", out var flap))
            {
                var stretch = phase % 18 - 16.8;
                if (stretch >= 0 && stretch < flap.Frames.Length / flap.Fps)
                    (anim, phase) = (flap, stretch);
            }
            return anim.FrameAt(phase);
        }
        return moving || animal.Id is "field_crow" or "river_duck" ? sprite.Anim("walk").FrameAt(animal.Phase) : 0;
    }

    private void Choose(Animal animal)
    {
        if (animal.Domain.Length < 2)
        {
            animal.PauseUntil = _clock + 2;
            return;
        }
        var choices = animal.Domain.Where(t => t != animal.Current && _animals.All(other => other == animal || Distance(t, other.Current) > 1))
            .OrderBy(_ => animal.Random.Next()).ToArray();
        foreach (var destination in choices.Take(24))
        {
            var path = FaunaRange.Path(_terrain.Map, animal.Current, destination, animal.Allowed);
            if (path.IsEmpty)
                continue;
            animal.Path = new Queue<Tile>(path);
            return;
        }
        animal.PauseUntil = _clock + 2;
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
