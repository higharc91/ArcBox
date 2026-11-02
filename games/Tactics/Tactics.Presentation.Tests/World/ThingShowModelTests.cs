using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using static Tactics.Presentation.Tests.World.ThingShowTests;

namespace Tactics.Presentation.Tests.World;

/// <summary>
/// A model test of <see cref="ThingShow"/>: random views (things coming, going and changing, events for things shown and not, numbers starting
/// again, views sent twice) and random frame lengths, against what must always hold. Raise the count with <c>THING_MODEL_EVENTS</c>.
/// </summary>
public sealed class ThingShowModelTests
{
    private static readonly Lazy<UnitArt> Units = new(() => UnitArt.Load(Pack.Root));
    private static readonly Lazy<ThingArt> Own = new(() => ThingArt.Load(ThingArtTests.WithArt.Value));
    private static readonly Lazy<ThingArt> Now = new(() => ThingArt.Load(Pack.Root));
    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));
    private static readonly Lazy<SoundBank> Sounds = new(() => SoundBank.Load(Pack.Root));

    private static readonly string[] Events = [ThingEvents.Opened, ThingEvents.Unlocked, ThingEvents.Failed, ThingEvents.Revealed, ThingEvents.Spotted,
        ThingEvents.Sprung, ThingEvents.Disarmed, ThingEvents.Toggled, ThingEvents.Taken, ThingEvents.Read, ThingEvents.Used, ThingEvents.Looked];

    private static int Count => int.TryParse(Environment.GetEnvironmentVariable("THING_MODEL_EVENTS"), out var n) ? n : 3000;

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void Random_views_and_frames_keep_every_thing_drawn_once_and_every_moment_bounded(int seed, bool ownArt)
    {
        var random = new Random(seed);
        var ground = WalkFeed.Ground(24, 24);
        var show = new ThingShow(ground, Units.Value, ownArt ? Own.Value : Now.Value, Fx.Value, Sounds.Value);
        var pool = ThingArtTests.EveryState().Select((t, i) => t with { Id = $"t{i % 12}", At = new Tile(2 + i % 12, 3 + i / 12 % 18) })
            .GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.ToList());
        var shown = new Dictionary<string, ThingView>();
        var events = new List<ThingEventView>();
        var seq = 0;
        var fresh = 0;
        var heard = 0;
        var lastMax = 0;

        for (var step = 0; step < Count; step++)
        {
            switch (random.Next(10))
            {
                case 0 or 1:        // something comes or changes
                {
                    var id = $"t{random.Next(12)}";
                    shown[id] = pool[id][random.Next(pool[id].Count)];
                    break;
                }
                case 2:
                    shown.Remove($"t{random.Next(12)}");
                    break;
                case 3 or 4:        // something happens, to something shown or not, maybe off the map
                {
                    var id = random.Next(5) == 0 ? "nobody" : $"t{random.Next(12)}";
                    events.Add(new ThingEventView(++seq, id, Events[random.Next(Events.Length)], "Ana", null));
                    if (random.Next(3) == 0 && pool.TryGetValue(id, out var states))
                        shown[id] = states[random.Next(states.Count)];
                    if (random.Next(4) == 0)
                        shown.Remove(id);
                    break;
                }
                case 5 when random.Next(20) == 0:   // a server restart: numbers start again
                    (seq, events) = (0, []);
                    break;
                default:
                    break;
            }
            while (events.Count > 8)
                events.RemoveAt(0);
            var view = WalkFeed.View(new Tile(1, 1)) with { Things = [.. shown.Values], ThingEvents = [.. events] };
            var playingBefore = show.Playing.Count();
            show.Receive(view);
            if (random.Next(3) == 0)
                show.Receive(view);                // sent twice: nothing new
            var max = events.Count == 0 ? 0 : events.Max(e => e.Seq);
            fresh += Math.Max(0, show.Playing.Count() - playingBefore);
            lastMax = max;

            var frame = show.Frame(random.NextDouble() * 0.2);
            heard += frame.Sounds.Length;
            Check(frame, shown);
        }
        Assert.True(heard <= fresh, $"{heard} sounds for {fresh} events");

        // Left alone, everything settles as it stands, and nothing that's gone is left behind.
        var settled = show.Frame(4);
        settled = show.Frame(1.0 / 60);
        Assert.Empty(show.Playing);
        Assert.Equal(string.Join(" ", shown.Keys.Order()), string.Join(" ", settled.Units.Select(u => u.Id.Value["thing:".Length..]).Order()));
        foreach (var unit in settled.Units)
        {
            Assert.Equal((Vector2.One, ThingLook.Plain, 1f), (unit.Thing!.Scale, unit.Thing.Tint, unit.Thing.Alpha));
            var thing = shown[unit.Id.Value["thing:".Length..]];
            Assert.Equal(UnitPlace.On(ground, thing.At).Feet, unit.Feet);
        }
        Assert.Empty(settled.Effects);
        Assert.True(lastMax >= 0);
    }

    private static void Check(ThingFrame frame, Dictionary<string, ThingView> shown)
    {
        var ids = frame.Units.Select(u => u.Id.Value["thing:".Length..]).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());                     // drawn once each, ghost or not
        foreach (var id in shown.Keys)
            Assert.Contains(id, ids);                                         // everything shown is drawn
        Assert.Equal(frame.Units.OrderBy(u => u.Order).Select(u => u.Order), frame.Units.Select(u => u.Order));
        foreach (var unit in frame.Units)
        {
            var look = unit.Thing!;
            Assert.InRange(look.Alpha, 0, 1);
            Assert.InRange(look.Scale.X, 0, 1.3f);
            Assert.InRange(look.Scale.Y, 0, 1.3f);
            Assert.True(float.IsFinite(unit.Feet.X) && float.IsFinite(unit.Feet.Y));
            Assert.True(look.Marker is null || look.Marker.FillAlpha is >= 0 and <= 1);
        }
    }
}
