namespace Tactics.Logic;

/// <summary>
/// Where a campaign is in its story (spec §4.2): one beat at a time, in the arc's order. 5c arms a battle stamp only for the beat
/// this offers, and 5d's markers and chains follow the same rule, so it means the same thing to both. <c>StoryOrder</c> in
/// <c>Tactics.WorldTools</c> replays a region's story by this rule, and by <see cref="Chains"/> and <see cref="SpotFor"/>: they must
/// change together.
/// </summary>
public static class StoryRules
{
    /// <summary>How many legal steps from its spot the leader can be and still interact with a beat (5d plan §2).</summary>
    public const int Reach = 2;

    /// <summary>
    /// The beat on offer: the first of the arc, in order, that isn't done and whose <see cref="ArcBeat.Requires"/> are all set. A beat
    /// whose flags aren't set is passed over, not waited for, so a branch the party didn't take never blocks the story; it's offered
    /// again if a later beat sets them. Null when nothing is left to offer.
    /// </summary>
    public static ArcBeat? Available(Arc arc, IReadOnlySet<string> flags, IReadOnlySet<string> beats)
        => arc.Beats.FirstOrDefault(b => !beats.Contains(b.Id) && b.Requires.All(flags.Contains));

    /// <summary>True once the arc's last beat is done, which ends its chapter (spec §2). An arc with no beats never ends.</summary>
    public static bool ChapterDone(Arc arc, IReadOnlySet<string> beats) => arc.Beats is [.., var last] && beats.Contains(last.Id);

    /// <summary>Where <paramref name="scene"/> of <paramref name="beat"/> is set: its own place, or else the beat's. Null for a beat set anywhere.</summary>
    public static string? PlaceOf(SceneDef scene, ArcBeat beat) => scene.Place ?? beat.Place;

    /// <summary>
    /// Where the party goes for a scene set in <paramref name="place"/>, away from its beat (5d plan §3, Another place): the spot of the
    /// first beat of <paramref name="arc"/> in that place, as <paramref name="region"/> marks it. Null when no beat of the arc is there,
    /// or that beat has no spot: the scene then plays where the party stands.
    /// </summary>
    public static Tile? SpotFor(Arc arc, RegionDef region, string place)
        => arc.Beats.FirstOrDefault(b => b.Place == place) is { } first
            ? region.Beats.Where(s => s.Beat == first.Id).Select(s => (Tile?)s.At).FirstOrDefault()
            : null;

    /// <summary>
    /// True when <paramref name="next"/> starts as soon as the beat before it ends, with no walk and no marker: a battle in the place the
    /// party already is (<paramref name="here"/>, the place of the last scene played). A battle set nowhere in particular never chains.
    /// </summary>
    public static bool Chains(ArcBeat next, string? here) => next.Kind == ArcBeat.Battle && next.Place is not null && next.Place == here;

    /// <summary>
    /// True when the leader at <paramref name="at"/> can reach <paramref name="spot"/> in at most <see cref="Reach"/> legal steps, by the
    /// one walking rule (<see cref="WorldWalk.Next"/>, any of the eight directions, at the default jump). Steps, not tiles, so a spot up
    /// a cliff isn't in reach from its foot.
    /// </summary>
    public static bool InReach(Terrain terrain, Tile at, Tile spot)
    {
        var frontier = new HashSet<Tile> { at };
        var seen = new HashSet<Tile> { at };
        for (var step = 0; step <= Reach; step++)
        {
            if (frontier.Contains(spot))
                return true;
            if (step == Reach)
                break;
            var next = new HashSet<Tile>();
            foreach (var from in frontier)
                foreach (var dir in Enum.GetValues<WalkDir>())
                    if (WorldWalk.Next(terrain, from, dir, ClassDef.DefaultJump, null) is { } move && seen.Add(move.To))
                        next.Add(move.To);
            frontier = next;
        }
        return false;
    }
}
