using System.Collections.Immutable;
using System.Text.Json;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>One walk of the story: from a tile to a beat's spot on a branch, and how long it takes, or null if it can't be walked.</summary>
public sealed record StoryLeg(string Branch, string Beat, Tile From, Tile To, double? Seconds);

/// <summary>What <see cref="StoryOrder.Check"/> found: every leg walked, each walk back to a fight after losing it, and what can't be done.</summary>
public sealed record StoryWalk(ImmutableArray<StoryLeg> Legs, ImmutableArray<StoryLeg> Retries, ImmutableArray<string> Problems)
{
    /// <summary>How long a branch's legs take to walk, one after another.</summary>
    public double Seconds(string branch) => Legs.Where(l => l.Branch == branch).Sum(l => l.Seconds ?? 0);
}

/// <summary>
/// Replays a region's story in the order it's played, which a flood from the start can't see: a tile is only "reached" in that flood by
/// walking through a battle's stamp, but while that battle is armed the first step in starts the fight. So this walks each branch of
/// <c>lore/arcs.json</c>, beat by beat as milestone 5d offers them: the first beat not yet done whose <c>requires</c> hold. A battle
/// leg ends at the beat's spot or on the first step into its stamp, as 5c starts the fight there, so it never walks through. A battle in
/// the place the last beat ended starts at once with no walk, as 5d chains them. A choice forks a branch per option, and a scene of its
/// beat set in another place moves the party to the spot of the arc's first beat there. After each battle it also proves a beaten party
/// can get from the recovery spawn back to the stamp. Separately, it finds tiles the party can walk into and never leave.
/// It runs on a loaded region, so a painted one gets the same check as the draft. Each problem ends in what a painter should do, and the
/// first tile it names is the one to go to.
/// </summary>
public static class StoryOrder
{
    private sealed record Choice(string Id, ImmutableArray<string> Sets);

    /// <summary>A beat of the arc. <see cref="Place"/> is null for one set anywhere, as <c>LoreBook</c> reads it.</summary>
    private sealed record Beat(string Id, string Kind, string? Place, string? Encounter, ImmutableArray<string> Requires, ImmutableArray<string> Sets, ImmutableArray<Choice> Choices);

    private sealed record Scene(string Beat, string? Place, ImmutableArray<string> Requires, ImmutableArray<string> Unless);

    /// <summary>Where a branch has got to.</summary>
    private sealed record Progress(string Branch, Tile At, string? Place, ImmutableHashSet<string> Flags, ImmutableHashSet<string> Done, ImmutableArray<StoryLeg> Legs, ImmutableArray<StoryLeg> Retries);

    /// <summary>Walks every arc of <paramref name="region"/> in <paramref name="contentBase"/>'s lore, on the maps and encounters of <paramref name="rules"/>.</summary>
    public static StoryWalk Check(WorldRegion region, RulesPack rules, string contentBase)
    {
        var (legs, retries, problems) = (new List<StoryLeg>(), new List<StoryLeg>(), new List<string>());
        // The painted ground, with the things that always stand in the way (a chest, a barrel) and every passage open, as the content check judges reach.
        var solid = rules.Interactables.GetValueOrDefault(region.Def.Id, []).Where(Interactables.Solid).Select(d => d.At).ToList();
        var walk = new RegionWalk(WorldGround.Of(region, rules.Maps, rules.Art ?? ArtManifest.None).With(solid));
        var trapped = walk.Trapped(region.Def.Spawns.Start);
        if (trapped.Count > 0)
            problems.Add($"{TileSpread.Say(trapped, "can be walked into but has no way back", "can be walked into but have no way back")}. Paint a way out, climbing no more than {ClassDef.DefaultJump} levels a step, or raise {TileSpread.It(trapped)}.");

        var arcs = Path.Combine(contentBase, "lore", "arcs.json");
        if (!File.Exists(arcs))
            return new StoryWalk([], [], [.. problems]);

        using var lore = JsonDocument.Parse(File.ReadAllBytes(arcs));
        foreach (var arc in lore.RootElement.GetProperty("arcs").EnumerateArray().Where(a => a.GetProperty("region").GetString() == region.Def.Id))
        {
            var beats = arc.GetProperty("beats").EnumerateArray().Select(ReadBeat).ToImmutableArray();
            var scenes = ReadScenes(contentBase, arc.GetProperty("chapter").GetInt32());
            var prerequisite = arc.TryGetProperty("sideAdventure", out var side) && side.GetBoolean() && !beats.IsEmpty ? beats[0].Requires.ToImmutableHashSet(StringComparer.Ordinal) : ImmutableHashSet<string>.Empty;
            var start = new Progress("", region.Def.Spawns.Start, null, prerequisite, [], [], []);
            foreach (var finished in new Replay(region, rules, walk, beats, scenes, problems).Run(start))
            {
                legs.AddRange(finished.Legs);
                retries.AddRange(finished.Retries);
            }
        }
        return new StoryWalk([.. legs], [.. retries], [.. problems.Distinct(StringComparer.Ordinal)]);
    }

    private static ImmutableArray<string> Strings(JsonElement e, string name)
        => e.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(s => s.GetString()!)] : [];

    private static Beat ReadBeat(JsonElement e)
    {
        ImmutableArray<Choice> choices = e.TryGetProperty("choices", out var list)
            ? [.. list.EnumerateArray().Select(c => new Choice(c.GetProperty("id").GetString()!, Strings(c, "sets")))]
            : [];
        return new Beat(e.GetProperty("id").GetString()!, e.GetProperty("kind").GetString()!, e.TryGetProperty("place", out var place) ? place.GetString() : null,
            e.TryGetProperty("encounter", out var encounter) ? encounter.GetString() : null, Strings(e, "requires"), Strings(e, "sets"), choices);
    }

    private static ImmutableArray<Scene> ReadScenes(string contentBase, int chapter)
    {
        var path = Path.Combine(contentBase, "lore", "scenes", $"ch{chapter}.json");
        if (!File.Exists(path))
            return [];
        using var scenes = JsonDocument.Parse(File.ReadAllBytes(path));
        return
        [
            .. scenes.RootElement.GetProperty("scenes").EnumerateArray().Select(s => new Scene(s.GetProperty("beat").GetString()!,
                s.TryGetProperty("place", out var place) ? place.GetString() : null, Strings(s, "requires"), Strings(s, "unless"))),
        ];
    }

    /// <summary>One arc, walked.</summary>
    private sealed class Replay(WorldRegion region, RulesPack rules, RegionWalk walk, ImmutableArray<Beat> beats, ImmutableArray<Scene> scenes, List<string> problems)
    {
        private static string Named(Progress p) => p.Branch.Length > 0 ? $"On the {p.Branch} branch, the" : "The";

        private Tile? SpotOf(string beat) => region.Def.Beats.Where(b => b.Beat == beat).Select(b => (Tile?)b.At).FirstOrDefault();

        /// <summary>The battle map and stamp of a battle beat, or null if its encounter isn't stamped here.</summary>
        private (BattleMap Map, RegionStamp Stamp)? StampOf(Beat beat)
        {
            if (beat.Encounter is null || !rules.Encounters.TryGetValue(beat.Encounter, out var encounter))
                return null;
            var stamp = region.Def.Stamps.FirstOrDefault(s => s.Map == encounter.Map);
            return stamp is not null && rules.MapFor(stamp.Map) is { } map ? (map, stamp) : null;
        }

        private static bool Inside((BattleMap Map, RegionStamp Stamp) on, Tile t)
            => t.X >= on.Stamp.At.X && t.Y >= on.Stamp.At.Y && t.X < on.Stamp.At.X + on.Map.Width && t.Y < on.Stamp.At.Y + on.Map.Height;

        /// <summary>Where walking resumes after a win, at best: 5c puts the party on the leader's battle tile, which can be anywhere, and falls back to this.</summary>
        private static Tile AfterTheFight((BattleMap Map, RegionStamp Stamp) on)
        {
            var at = on.Map.Zones.TryGetValue("party_spawn", out var zone) && zone.Length > 0 ? zone[0] : default;
            return new Tile(at.X + on.Stamp.At.X, at.Y + on.Stamp.At.Y);
        }

        public IEnumerable<Progress> Run(Progress progress)
        {
            // This is StoryRules.Available's rule in Tactics.Logic, copied: the two must change together. It applies a beat's own
            // sets and its choices' sets, but never a battle's FlagsSet from the encounter, which the game merges on a win.
            while (beats.FirstOrDefault(b => !progress.Done.Contains(b.Id) && b.Requires.All(progress.Flags.Contains)) is { } beat)
            {
                // A battle chains only from a place: a battle set nowhere in particular is walked to, like any other beat.
                var chained = beat.Kind == "battle" && beat.Place is not null && beat.Place == progress.Place;
                progress = Walk(progress with { Done = progress.Done.Add(beat.Id), Flags = progress.Flags.Union(beat.Sets) }, beat, chained);
                if (beat.Choices.Length > 0)
                    return beat.Choices.SelectMany(choice => Run(Chose(progress, beat, choice)));
            }
            return [progress];
        }

        private Progress Walk(Progress progress, Beat beat, bool chained)
        {
            if (SpotOf(beat.Id) is not { } spot)
            {
                problems.Add($"The beat '{beat.Id}' has no spot in the region. Add a BeatMarker under Beats and set its Beat.");
                return progress;
            }

            var on = beat.Kind == "battle" ? StampOf(beat) : null;
            if (beat.Kind == "battle" && on is null)
                problems.Add($"The battle beat '{beat.Id}' has no stamp in the region. Add a StampMarker under Stamps for its encounter's map, then press Apply stamp.");

            var seconds = chained && on is not null ? 0 : walk.Seconds(progress.At, t => t == spot || (on is { } armed && Inside(armed, t)));
            var leg = new StoryLeg(progress.Branch, beat.Id, progress.At, spot, seconds);
            if (seconds is null)
                problems.Add($"{Named(progress)} party can't walk to '{beat.Id}' at {spot} from {progress.At}{(on is null ? ". Paint a way there, or move the marker." : $", or to its stamp '{on.Value.Stamp.Map}'. Paint a way to the stamp, or move the marker.")}");
            progress = progress with { At = spot, Place = beat.Place, Legs = progress.Legs.Add(leg) };
            if (on is not { } fight)
                return progress;

            // A loss wakes the party at the recovery spawn with the stamp armed again, and any step into it restarts the fight.
            var recovery = region.Def.Spawns.Recovery;
            var back = Inside(fight, recovery) ? null : walk.Seconds(recovery, t => Inside(fight, t));
            if (back is null)
                problems.Add($"{Named(progress)} party can't get back from the recovery spawn at {recovery} to the stamp '{fight.Stamp.Map}' after losing '{beat.Id}'. Paint a way to the stamp, or move the Recovery marker.");
            return progress with { At = AfterTheFight(fight), Retries = progress.Retries.Add(new StoryLeg(progress.Branch, beat.Id, recovery, fight.Stamp.At, back)) };
        }

        /// <summary>The branch a choice starts: its flags, then the move to wherever a scene that now plays is set.</summary>
        private Progress Chose(Progress progress, Beat beat, Choice choice)
        {
            var branch = progress.Branch.Length > 0 ? $"{progress.Branch}/{choice.Id}" : choice.Id;
            var chosen = progress with
            {
                Branch = branch,
                Flags = progress.Flags.Union(choice.Sets),
                Legs = [.. progress.Legs.Select(l => l with { Branch = branch })],
                Retries = [.. progress.Retries.Select(l => l with { Branch = branch })],
            };
            var elsewhere = scenes.FirstOrDefault(s => s.Beat == beat.Id && s.Place is not null && s.Place != beat.Place
                && s.Requires.All(chosen.Flags.Contains) && !s.Unless.Any(chosen.Flags.Contains));
            if (elsewhere is null)
                return chosen;

            var first = beats.FirstOrDefault(b => b.Place == elsewhere.Place);
            if (first is not null && SpotOf(first.Id) is { } spot)
                return chosen with { At = spot, Place = elsewhere.Place };
            problems.Add($"{Named(chosen)} scene set in '{elsewhere.Place}' has no beat spot to move the party to. Add a BeatMarker for the arc's first beat there.");
            return chosen;
        }
    }
}
