using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Sim;

/// <summary>
/// What a balance sweep plays (spec §5.6): every encounter in <see cref="Encounters"/> at every level in
/// <see cref="Levels"/>, with <see cref="PartiesPerSize"/> sampled class combinations of each size in
/// <see cref="PartySizes"/> (sizes the encounter doesn't take are skipped), each with seeds 1 to <see cref="Seeds"/>, on
/// <see cref="Difficulty"/>. A battle still going when round <see cref="RoundCap"/> is over stops and counts as lost.
/// </summary>
public sealed record SimConfig(ImmutableArray<string> Encounters, ImmutableArray<int> Levels, int PartiesPerSize,
    ImmutableArray<int> PartySizes, int Seeds, Difficulty Difficulty, int RoundCap = 30);

/// <summary>
/// One simulated battle: the encounter, the party's level and classes (one entry per member, ordinal order), the seed,
/// the outcome and how many rounds it took; party members Downed (each time) and Out; the per-class measures
/// (<see cref="ClassStats"/>, averaged over the members of each class); for each ability party members could use, the
/// turns it was used in and the turns it was legal in; and the items the AI used. <see cref="Capped"/> marks a battle
/// stopped at the round cap (counted as <see cref="BattleOutcome.Lost"/>, <see cref="Rounds"/> = the cap). How the AI's
/// plans fared, on both sides: <see cref="Rejected"/> commands <see cref="Battle.Apply"/> refused, <see cref="Replans"/>
/// (a turn planned again after a refusal or a move cut short) and <see cref="ForcedEnds"/> (turns the simulator ended
/// because the plans didn't).
/// </summary>
public sealed record SimBattle(string Encounter, int Level, ImmutableArray<string> Classes, ulong Seed, BattleOutcome Outcome,
    int Rounds, int Downs, int Outs, ImmutableSortedDictionary<string, ClassStats> PerClass,
    ImmutableSortedDictionary<AbilityId, (int Used, int Legal)> Abilities, int AiItemUses, bool Capped = false,
    int Rejected = 0, int Replans = 0, int ForcedEnds = 0);

/// <summary>
/// A class's measures in one battle, per member of that class (see <see cref="Tally"/> for how each is read from the
/// events): HP taken off hostile units per round (no overkill, nor temporary HP); healing done (HP restored, and the HP of a revive that wasn't a
/// heal) per round; damage mitigated per round (soaked: HP and temporary HP lost, no overkill; plus attacks against it
/// that missed times the battle's mean soaked damage of a hostile hit on the party); and enemy turns denied (skipped while incapacitated by a condition it applied) in the
/// battle.
/// </summary>
public sealed record ClassStats(double DamagePerRound, double HealingPerRound, double MitigatedPerRound, double TurnsDenied);

/// <summary>The simulator's role bands (spec §5.6); <see cref="None"/> for a class in no band.</summary>
public enum RoleBand { None, Defender, Striker, Healer, Control }

/// <summary>
/// Plays the battles a <see cref="SimConfig"/> describes, AI on both sides (spec §5.6). Party members are built with
/// <see cref="SheetBuilder.DefaultCharacter"/> (human, no subclass) and play the profile their class's <c>aiTags</c> name
/// (<see cref="ProfileFor"/>) with no items; enemies play their monster's profile. Every battle is independent and
/// deterministic, so they run in parallel and come back in the config's order.
/// </summary>
public static class SimRunner
{
    /// <summary>The seed of the generator that samples the party compositions.</summary>
    public const ulong PartySeed = 20261030;

    /// <summary>At most this many members of one class in a sampled party (more only when the pack is too small to fill it).</summary>
    public const int MaxCopies = 2;

    /// <summary>The race every simulated character takes.</summary>
    public const string Race = "human";

    /// <summary>How many times a turn is planned again after a plan goes stale (a move cut short) before it is ended.</summary>
    private const int MaxPlansPerTurn = 4;

    private static readonly AiOptions PartyOptions = new(AllowItems: false);

    private static readonly ImmutableSortedDictionary<string, RoleBand> Bands = Ordinal.Map(
        ("fighter", RoleBand.Defender), ("paladin", RoleBand.Defender), ("barbarian", RoleBand.Defender),
        ("rogue", RoleBand.Striker), ("ranger", RoleBand.Striker), ("warlock", RoleBand.Striker), ("monk", RoleBand.Striker),
        ("sorcerer", RoleBand.Striker),
        ("cleric", RoleBand.Healer), ("bard", RoleBand.Healer),
        ("wizard", RoleBand.Control), ("druid", RoleBand.Control));

    /// <summary>The role band of class <paramref name="cls"/> (global constraints), <see cref="RoleBand.None"/> if it has none.</summary>
    public static RoleBand BandOf(string cls) => Bands.GetValueOrDefault(cls, RoleBand.None);

    /// <summary>The classes of <paramref name="band"/>, in ordinal order, whether or not the pack defines them.</summary>
    public static IEnumerable<string> ClassesIn(RoleBand band) => Bands.Where(b => b.Value == band).Select(b => b.Key);

    /// <summary>
    /// The AI profile a party member of <paramref name="cls"/> plays, from its <c>aiTags</c>: <c>healer</c> for a healer,
    /// <c>caster</c> for a caster or controller, <c>archer</c> for another ranged class, <c>skirmisher</c> for a striker,
    /// <c>brute</c> otherwise.
    /// </summary>
    public static string ProfileFor(ClassDef cls)
    {
        var tags = cls.AiTags.Tags.IsDefault ? [] : cls.AiTags.Tags;
        return tags.Contains("healer") ? "healer"
            : tags.Contains("caster") || tags.Contains("control") ? "caster"
            : tags.Contains("ranged") ? "archer"
            : tags.Contains("striker") ? "skirmisher"
            : "brute";
    }

    /// <summary>
    /// The sampled party compositions, size by size in <see cref="SimConfig.PartySizes"/> order: of every combination of the
    /// pack's classes of that size (each member's class in ordinal order, at most <see cref="MaxCopies"/> of a class) with
    /// at least one healer or defender, <see cref="SimConfig.PartiesPerSize"/> distinct ones (all of them if there are no
    /// more) drawn with one generator seeded <see cref="PartySeed"/>, listed in ordinal order within a size.
    /// </summary>
    public static ImmutableArray<ImmutableArray<string>> Parties(SimConfig config, RulesPack rules)
    {
        var classes = rules.Classes.Keys.Order(StringComparer.Ordinal).ToArray();
        var rng = Rng.Seeded(PartySeed);
        var parties = ImmutableArray.CreateBuilder<ImmutableArray<string>>();
        foreach (var size in config.PartySizes)
        {
            if (classes.Length == 0 || size < 1)
                continue;
            var cap = Math.Max(MaxCopies, (size + classes.Length - 1) / classes.Length);
            var all = Combinations(classes, size, cap)
                .Where(p => p.Any(c => BandOf(c) is RoleBand.Healer or RoleBand.Defender))
                .ToList();
            var picks = new List<ImmutableArray<string>>();
            for (var i = 0; i < Math.Min(config.PartiesPerSize, all.Count); i++)
            {
                (var j, rng) = rng.Below(all.Count - i);
                (all[i], all[i + j]) = (all[i + j], all[i]);
                picks.Add(all[i]);
            }
            parties.AddRange(picks.OrderBy(p => string.Join(",", p), StringComparer.Ordinal));
        }
        return parties.ToImmutable();
    }

    /// <summary>Every multiset of <paramref name="size"/> of <paramref name="classes"/> with at most <paramref name="cap"/> of each, in order.</summary>
    private static IEnumerable<ImmutableArray<string>> Combinations(string[] classes, int size, int cap)
    {
        var picked = new int[size];
        IEnumerable<ImmutableArray<string>> From(int slot, int first, int copies)
        {
            if (slot == size)
            {
                yield return [.. picked.Select(i => classes[i])];
                yield break;
            }
            for (var c = first; c < classes.Length; c++)
            {
                var run = c == first && slot > 0 && picked[slot - 1] == c ? copies + 1 : 1;
                if (run > cap)
                    continue;
                picked[slot] = c;
                foreach (var party in From(slot + 1, c, run))
                    yield return party;
            }
        }
        return From(0, 0, 0);
    }

    /// <summary>
    /// Plays every battle of <paramref name="config"/>: encounter by encounter, level by level, party by party
    /// (<see cref="Parties"/>), seed by seed (1 to <see cref="SimConfig.Seeds"/>), in that order. Battles run in parallel
    /// as the sequence is read. Throws <see cref="ArgumentException"/> for an unknown encounter.
    /// </summary>
    public static IEnumerable<SimBattle> Run(SimConfig config, RulesPack rules)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(config.RoundCap, 1);
        var parties = Parties(config, rules);
        var jobs = new List<(string Encounter, int Level, ImmutableArray<string> Party, ulong Seed)>();
        foreach (var encounter in config.Encounters)
        {
            if (!rules.Encounters.TryGetValue(encounter, out var def))
                throw new ArgumentException($"Unknown encounter '{encounter}'.", nameof(config));
            foreach (var level in config.Levels)
                foreach (var party in parties.Where(p => p.Length >= def.Party.Min && p.Length <= def.Party.Max))
                    for (var seed = 1; seed <= config.Seeds; seed++)
                        jobs.Add((encounter, level, party, (ulong)seed));
        }
        return jobs.AsParallel().AsOrdered().Select(j => Play(config, rules, j.Encounter, j.Level, j.Party, j.Seed));
    }

    /// <summary>One battle, played to its end or to the round cap.</summary>
    private static SimBattle Play(SimConfig config, RulesPack rules, string encounter, int level, ImmutableArray<string> classes,
        ulong seed)
    {
        var race = rules.Races.ContainsKey(Race) ? Race : rules.Races.Keys.First();
        ImmutableArray<PartyMember> party = [.. classes.Select((cls, i) =>
        {
            var id = new UnitId($"p{i + 1}.{cls}");
            return new PartyMember(id, SheetBuilder.DefaultCharacter(id.Value, race, cls, null, level, rules),
                new AiControlled(ProfileFor(rules.Classes[cls])));
        })];
        var setup = Encounters.Setup(encounter, party, config.Difficulty, rules);

        var tally = new Tally(rules);
        var ai = new AiCounts();
        var start = Battle.Start(setup, rules, seed);
        var state = start.State;
        tally.Read(start.Events, state);
        var capped = false;
        while (state.Outcome is null)
        {
            if (state.Round > config.RoundCap)
            {
                capped = true;
                break;
            }
            if (state.Pending is not null || state.Active is not { } unit)
                throw new InvalidOperationException($"The battle is waiting on something the AI can't answer ({state.Pending}).");
            state = PlayTurn(state, unit, rules, tally, ai);
        }

        var rounds = capped ? config.RoundCap : Math.Max(1, state.Round);
        return new SimBattle(encounter, level, classes, seed, capped ? BattleOutcome.Lost : state.Outcome!.Value, rounds,
            tally.Downs, tally.Outs, tally.PerClass(rounds), tally.Abilities(), state.ItemsUsed.Count(u => u.ByAi), capped,
            ai.Rejected, ai.Replans, ai.ForcedEnds);
    }

    /// <summary>How a battle's AI plans fared (see <see cref="SimBattle"/>).</summary>
    private sealed class AiCounts
    {
        public int Rejected { get; set; }
        public int Replans { get; set; }
        public int ForcedEnds { get; set; }
    }

    /// <summary>
    /// <paramref name="unit"/>'s whole turn: the AI's plan, applied command by command, planned again when a command is
    /// refused or a move is cut short (at most <see cref="MaxPlansPerTurn"/> plans), then ended if it hasn't ended.
    /// </summary>
    private static BattleState PlayTurn(BattleState state, UnitId unit, RulesPack rules, Tally tally, AiCounts ai)
    {
        var party = tally.IsMember(state, unit);
        if (party)
            tally.BeginTurn(unit, LegalAbilities(state, unit, rules));
        var options = party ? PartyOptions : null;
        var ended = false;
        for (var plan = 0; plan < MaxPlansPerTurn && !ended && state.Outcome is null; plan++)
        {
            if (plan > 0)
                ai.Replans++;
            foreach (var command in Ai.Plan(state, unit, rules, options).Commands)
            {
                var resolution = Battle.Apply(state, command, rules);
                if (resolution.Rejected is not null)
                {
                    ai.Rejected++;
                    break;
                }
                state = resolution.State;
                tally.Read(resolution.Events, state);
                ended = resolution.Events.Any(e => e is TurnEnded t && t.Unit == unit);
                if (ended || state.Outcome is not null || command is Move move && state.Units[unit].Pos != move.Path[^1])
                    break;
            }
        }
        if (!ended && state.Outcome is null)
        {
            ai.ForcedEnds++;
            var resolution = Battle.Apply(state, new EndTurn(unit), rules);
            state = resolution.State;
            tally.Read(resolution.Events, state);
        }
        return state;
    }

    /// <summary>
    /// The abilities <paramref name="unit"/> could use this turn, judged at its start (an approximation, cheap enough for a
    /// sweep): the sheet's abilities and the <c>common.*</c> actions, less reactions and item use, for which
    /// <see cref="Battle.Check"/> accepts some target, or refuses it only for range or line of sight when the aim is within
    /// the unit's Move left plus the ability's range. Units and their tiles are tried as targets, and for tile abilities
    /// the tiles next to each unit too; a self or whole-side ability needs none, but an area around the user (a burst,
    /// line or cone) also needs a unit of another side within the unit's Move left plus the area's size.
    /// </summary>
    internal static ImmutableSortedSet<AbilityId> LegalAbilities(BattleState s, UnitId unit, RulesPack rules)
    {
        var me = s.Units[unit];
        var legal = ImmutableSortedSet.CreateBuilder<AbilityId>();
        var candidates = me.Sheet.Abilities
            .Concat(rules.Abilities.Keys.Where(id => id.Value.StartsWith("common.", StringComparison.Ordinal) && id != Battle.UseItem))
            .Distinct();
        var inBattle = s.Units.Values.Where(DownedRules.InBattle).ToArray();
        foreach (var id in candidates)
            if (rules.Abilities.TryGetValue(id, out var ability) && ability.Cost.Reaction == 0 && Legal(s, me, ability, inBattle, rules))
                legal.Add(id);
        return legal.ToImmutable();
    }

    private static bool Legal(BattleState s, UnitState me, AbilityDef ability, UnitState[] inBattle, RulesPack rules)
    {
        var def = ability.Target;
        var range = ability.IsWeaponAttack ? me.Sheet.Weapon.Ranged ? me.Sheet.Weapon.Range : me.Sheet.Weapon.Reach ? 2 : 1 : def.Range;
        foreach (var target in Targets(me, def, inBattle))
        {
            var refused = Battle.Check(s, new Use(me.Id, ability.Id, target), rules);
            switch (refused?.Code)
            {
                case null:
                    // A burst, line or cone around the user is accepted on its own; it counts only with a foe in reach.
                    return def.Kind != TargetKind.Self
                           || def.Area.Shape is AreaShape.Single or AreaShape.Self or AreaShape.AllAllies or AreaShape.AllEnemies
                           || inBattle.Any(u => u.Sheet.Side != me.Sheet.Side
                                                && Reach.Distance(me.Pos, u.Pos) <= me.Economy.MoveLeft + Math.Max(1, def.Area.Size));
                case RejectCode.OutOfRange or RejectCode.NoLineOfSight:
                    var aim = target.Tile ?? (target.Unit is { } u ? s.Units[u].Pos : me.Pos);
                    if (Reach.Distance(me.Pos, aim) <= me.Economy.MoveLeft + range)
                        return true;
                    break;
                case RejectCode.InvalidTarget:
                    break;
                default:
                    return false;   // the cost, the economy or the unit itself: no target changes that
            }
        }
        return false;
    }

    private static IEnumerable<Target> Targets(UnitState me, TargetDef def, UnitState[] inBattle)
    {
        if (def.Kind == TargetKind.Self || def.Area.Shape is AreaShape.AllAllies or AreaShape.AllEnemies)
        {
            yield return new Target();
            yield break;
        }
        if (def.Kind is TargetKind.Tile or TargetKind.EmptyTile)
        {
            foreach (var t in inBattle.Select(u => u.Pos).Prepend(me.Pos)
                         .SelectMany(p => new[] { p, p with { Y = p.Y - 1 }, p with { X = p.X + 1 }, p with { Y = p.Y + 1 }, p with { X = p.X - 1 } })
                         .Distinct())
                yield return new Target(Tile: t);
            yield break;
        }
        foreach (var u in inBattle)
            yield return new Target(Unit: u.Id);
    }
}
