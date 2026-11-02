using System.Collections.Immutable;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// A model test of things to interact with (interactables design §2), as <c>WalkModelTests</c> is of walking: random walking, interacting,
/// answering, waiting and restarting in the vault, with the invariants checked after every event. Raise the count with
/// <c>INTERACT_MODEL_EVENTS</c>.
/// </summary>
public sealed class InteractModelTests
{
    private static readonly int Events = int.TryParse(Environment.GetEnvironmentVariable("INTERACT_MODEL_EVENTS"), out var n) ? n : 1500;

    private static readonly string[] Dirs = ["n", "ne", "e", "se", "s", "sw", "w", "nw"];

    private static ImmutableArray<InteractableDef> Defs => Vault.Rules.Value.InteractablesIn(Vault.Id);

    private static TacticsDocument Doc(Session s) => TacticsDocument.Read(s.Lease.Document);

    private static Dictionary<string, int> Bags(TacticsDocument doc)
        => doc.Members.Values.SelectMany(m => m.Character.Bag).GroupBy(b => b.Item.Value).ToDictionary(g => g.Key, g => g.Sum(b => b.Count));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Random_play_keeps_every_invariant(int seed)
    {
        var rng = new Random(seed);
        var s = Vault.At(Vault.Start, seed: seed);
        var start = Bags(Doc(s));
        var worked = Interactables.Worked(Defs);
        var before = Doc(s);
        var log = new List<string>();
        for (var i = 0; i < Events; i++)
        {
            var roll = rng.Next(100);
            string what;
            if (roll < 35)
            {
                var dir = Dirs[rng.Next(Dirs.Length)];
                s.Send(0, "walk", new { dir });
                s.Harness.Advance(TimeSpan.FromMilliseconds(rng.Next(100, 900)));
                s.Send(0, "walk", new { dir = (string?)null });
                what = $"walk {dir}";
            }
            else if (roll < 55)
            {
                var targets = s.Screen(0)?.Screen == TacticsGame.Screens.Walk
                    ? s.Screen(0)!.Data.GetProperty("targets").EnumerateArray().Select(t => t.GetProperty("id").GetString()).ToList()
                    : [];
                var target = targets.Count > 0 && rng.Next(3) > 0 ? targets[rng.Next(targets.Count)] : null;
                s.Send(0, "interact", target is null ? null : new { target });
                what = $"interact {target ?? "-"}";
            }
            else if (roll < 70 && s.Game.Phase == TacticsPhase.Story && s.Host.Story is { } story)
            {
                if (story.Ask is { } ask)
                {
                    var option = ask.Options[rng.Next(ask.Options.Length)].Id;
                    s.Send(rng.Next(4), "vote", new { option });
                    what = $"vote {option}";
                }
                else if (story.Check is { Roll: null } check)
                {
                    var member = check.Cards[rng.Next(check.Cards.Length)].Member;
                    s.Send(rng.Next(4), "pickRoller", new { member });
                    what = $"pick {member}";
                }
                else
                {
                    s.Send(0, "skip", null);
                    what = "skip";
                }
            }
            else if (roll < 95)
            {
                s.Harness.Advance(TimeSpan.FromMilliseconds(rng.Next(200, 20_000)));
                what = "wait";
            }
            else
            {
                var saved = Doc(s);
                s = Again(s, seed + i);
                var after = Doc(s);
                Assert.Equal(Things(saved), Things(after));                                  // a restart changes nothing on its own
                Assert.Equal(Bags(saved), Bags(after));
                Assert.Equal(saved.World!.Flags.Order(), after.World!.Flags.Order());
                what = "restart";
            }
            log.Add(what);
            var now = Doc(s);
            Check(s, before, now, start, worked, string.Join(", ", log.TakeLast(12)));
            before = now;
        }
        // The walk got somewhere: something was done in the vault.
        Assert.NotEmpty(Doc(s).World!.Things ?? Ordinal.Map<InteractableSave>());
        if (Environment.GetEnvironmentVariable("INTERACT_MODEL_OUT") is { } dump)
            File.AppendAllText(dump, $"seed {seed}: {string.Join(" ", Things(Doc(s)).Select(t => $"{t.Key}={t.Value}"))} | "
                + string.Join(",", log.GroupBy(l => l.Split(' ')[0]).Select(g => $"{g.Key}:{g.Count()}")) + Environment.NewLine);
    }

    private static Session Again(Session s, int seed)
    {
        var again = new Session(seed: seed, document: s.Lease.Document, game: new TacticsGame(Vault.Rules.Value));
        again.Harness.Resume(again.Lease);
        for (var p = 0; p < 4; p++)
            again.Harness.SetConnected(p, true);
        again.Send(0, "walk", new { dir = (string?)null });
        return again;
    }

    private static Dictionary<string, string> Things(TacticsDocument doc)
        => (doc.World!.Things ?? Ordinal.Map<InteractableSave>()).ToDictionary(t => t.Key,
            t => $"{t.Value.State}/{t.Value.Found}/{t.Value.Unlocked}/{t.Value.TrapSpotted}/{t.Value.Trap}/{string.Join("+", t.Value.Spent)}");

    private static void Check(Session s, TacticsDocument was, TacticsDocument now, Dictionary<string, int> start, ImmutableHashSet<string> worked, string log)
    {
        var flags = now.World!.Flags.ToHashSet(StringComparer.Ordinal);
        // 1. Nothing goes back, but a lever and the passages it works.
        foreach (var def in Defs)
        {
            var (a, b) = (Interactables.StateOf(def, was.World!.Things), Interactables.StateOf(def, now.World.Things));
            Assert.False(Interactables.Regressed(def, a, b, worked.Contains(def.Id)), $"'{def.Id}' went back from {a} to {b} after {log}");
        }
        // 2. Flags only grow.
        Assert.Empty(was.World!.Flags.Except(now.World.Flags));
        // 3. Nobody stands in a closed door, a chest, or a trap they've seen.
        var blocked = Interactables.Blocking(Defs, now.World.Things, flags);
        Assert.False(blocked.Contains(s.Host.Explore!.At), $"the party is on a blocked tile {s.Host.Explore.At} after {log}");
        Assert.True(Vault.Rules.Value.GroundFor(Vault.Id)!.Terrain.Walkable(s.Host.Explore.At));
        // 4. Loot comes out of a thing once, the moment it's opened or taken.
        var expected = new Dictionary<string, int>(start);
        foreach (var def in Defs)
            if (Interactables.StateOf(def, now.World.Things).State is InteractableKinds.Opened or InteractableKinds.Taken)
                foreach (var loot in def.Loot)
                    expected[loot.Item.Value] = expected.GetValueOrDefault(loot.Item.Value) + loot.Count;
        Assert.Equal(expected.Where(e => e.Value > 0).OrderBy(e => e.Key), Bags(now).Where(e => e.Value > 0).OrderBy(e => e.Key));
        // 5. An interaction's cursor is there only in the Story phase, and the TV is never sent what nobody has seen.
        if (now.World.Story?.Thing is not null && s.Game.Phase is not (TacticsPhase.Resuming or TacticsPhase.Muster))
            Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        // Walking, the only cursor there can be is a battle beat's, waiting at its fight: never an interaction's.
        if (s.Game.Phase == TacticsPhase.Exploration)
            Assert.True(now.World.Story is null or { Thing: null, Stage: StorySave.Fight }, $"a cursor {now.World.Story} while walking after {log}");
        // Nothing unseen is named: not in the events, the leader's list, or the TV's notice.
        foreach (var e in s.Host.Explore.ThingEvents ?? [])
        {
            var def = Defs.Single(d => d.Id == e.Id);
            Assert.True(Interactables.Visible(def, Interactables.StateOf(def, now.World.Things), flags), $"an event of unseen '{def.Id}' after {log}");
        }
        if (s.Screen(0) is { Screen: TacticsGame.Screens.Walk } walk)
            foreach (var t in walk.Data.GetProperty("targets").EnumerateArray())
            {
                var def = Defs.Single(d => d.Id == t.GetProperty("id").GetString());
                Assert.True(Interactables.Visible(def, Interactables.StateOf(def, now.World.Things), flags));
            }
        var cache = Defs.Single(d => d.Id == "cache");
        if (!Interactables.StateOf(cache, now.World.Things).Found)
            Assert.DoesNotContain("hidden cache", s.Host.Notice ?? "");
        foreach (var view in s.Host.Explore.Things ?? [])
        {
            var def = Defs.Single(d => d.Id == view.Id);
            Assert.True(Interactables.Visible(def, Interactables.StateOf(def, now.World.Things), flags), $"'{def.Id}' was sent unseen");
        }
        // 6. A lever's passages are open exactly while it's on.
        foreach (var lever in Defs.Where(d => d.Kind == "lever"))
            foreach (var target in lever.Targets)
                Assert.Equal(Interactables.StateOf(lever, now.World.Things).State == InteractableKinds.On,
                    Interactables.StateOf(Defs.Single(d => d.Id == target), now.World.Things).State == InteractableKinds.Opened);
    }
}
