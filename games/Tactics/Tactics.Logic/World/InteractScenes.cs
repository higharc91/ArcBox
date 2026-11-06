using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>What the TV animates a thing from (design §2.8): the values of <see cref="InteractOutcome.Event"/>.</summary>
public static class ThingEvents
{
    /// <summary>A container or passage opened.</summary>
    public const string Opened = "opened";
    /// <summary>A lock gave way.</summary>
    public const string Unlocked = "unlocked";
    /// <summary>A way through a lock, or a disarm, failed.</summary>
    public const string Failed = "failed";
    /// <summary>A hidden thing was found.</summary>
    public const string Revealed = "revealed";
    /// <summary>A trap was seen.</summary>
    public const string Spotted = "spotted";
    /// <summary>A trap went off.</summary>
    public const string Sprung = "sprung";
    /// <summary>A trap was taken apart.</summary>
    public const string Disarmed = "disarmed";
    /// <summary>A lever was pulled, or a passage it works moved.</summary>
    public const string Toggled = "toggled";
    /// <summary>A pickup was taken.</summary>
    public const string Taken = "taken";
    /// <summary>A readable was read.</summary>
    public const string Read = "read";
    /// <summary>A shrine or a well was used.</summary>
    public const string Used = "used";
    /// <summary>Something said its line and didn't change.</summary>
    public const string Looked = "looked";
    /// <summary>A notice was noticed (design §7.2): its line is said, and what it reveals is found with it.</summary>
    public const string Noticed = "noticed";
}

/// <summary>
/// What one step of an interaction does to a thing (design §2.7), in absolute terms, so applying it twice is the same as once: the state it
/// goes to, its lock opened, its trap seen, disarmed or sprung, a failed way spent, its loot given, its trap's effect, and flags. Loot and a
/// trap's effect are due only when the step changes the thing (<see cref="InteractScenes.LootDue"/>, <see cref="InteractScenes.SpringDue"/>).
/// </summary>
public sealed record InteractOutcome(string Event, string? State = null, bool Unlock = false, bool SpotTrap = false, string? Trap = null,
    string? Spend = null, bool Loot = false, ImmutableArray<string> Sets = default, bool Key = false)
{
    /// <summary>The flags it sets; never a default array.</summary>
    public ImmutableArray<string> Sets { get => field.IsDefault ? [] : field; init; } = Sets;

    /// <summary>The thing after this step.</summary>
    public InteractableSave Apply(InteractableSave save) => save with
    {
        State = State ?? save.State,
        Unlocked = save.Unlocked || Unlock,
        TrapSpotted = save.TrapSpotted || SpotTrap || Trap is not null,
        Trap = save.Trap ?? Trap,
        Spent = Spend is { } s && !save.Spent.Contains(s) ? save.Spent.Add(s) : save.Spent,
        Keyed = save.Keyed || Key,
    };
}

/// <summary>What interacting with a thing does: at once, or as a scene the story runtime plays.</summary>
public abstract record InteractPlan;

/// <summary>Done at once, in the world: <see cref="Outcome"/>, if anything changes, and the line the TV shows.</summary>
public sealed record InstantPlan(InteractOutcome? Outcome, string Line) : InteractPlan;

/// <summary>
/// A scene the story runtime plays (5d): <see cref="Scene"/>'s nodes are lines, choices and checks like any scene's, and entering a node
/// of <see cref="Outcomes"/> applies its outcome, saved with that node's first line.
/// </summary>
public sealed record ScenePlan(SceneDef Scene, ImmutableDictionary<string, InteractOutcome> Outcomes) : InteractPlan;

/// <summary>
/// What interacting with a thing does (design §2.7), as a pure function of the thing, its state when the interaction began, and the
/// flags: an instant change, or a scene in 5d's format, with checks the party rolls by the story's own flow. A scene is built again from the
/// same state after a restart, so it's the same scene, node for node.
/// </summary>
public static class InteractScenes
{
    /// <summary>The prefix of an interaction scene's id; the rest is the thing's.</summary>
    public const string ScenePrefix = "thing.";

    /// <summary>The scene of an interaction with <paramref name="def"/>.</summary>
    public static string SceneId(InteractableDef def) => ScenePrefix + def.Id;

    /// <summary>True when the step gives its loot: it opens a closed container, or takes a pickup still there.</summary>
    public static bool LootDue(InteractableSave before, InteractOutcome outcome)
        => outcome.Loot && before.State is not (InteractableKinds.Opened or InteractableKinds.Taken);

    /// <summary>True when the step springs a trap that was set: its effect falls on whoever sprang it.</summary>
    public static bool SpringDue(InteractableSave before, InteractOutcome outcome) => outcome.Trap == InteractableSave.Sprung && before.Trap is null;

    /// <summary>
    /// What interacting with <paramref name="def"/>, in <paramref name="save"/>, does now; null when there's nothing to do (see
    /// <see cref="Interactables.Label"/>). <paramref name="worked"/> says a lever works it, and <paramref name="itemName"/> names the loot.
    /// <paramref name="again"/> builds the scene of an interaction already under way, from the state it began in: whatever the flags have
    /// become since (its own outcomes may have set some), it's the same scene.
    /// </summary>
    public static InteractPlan? Plan(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags, bool worked, Func<ItemId, string> itemName,
        bool again = false)
    {
        if (!again && Interactables.Label(def, save, flags, worked) is null)
            return null;
        var kind = Interactables.KindOf(def);
        var name = Interactables.NameOf(def);
        var text = Interactables.TextOf(def);
        if (def.To is not null)
        {
            var scene = new SceneDef("entrance:" + def.Id, null, null, $"Enter {name}?", [], [], [SceneDef.Narrator], "ask",
                [new SceneNode("ask", [new SceneLine("entrance_" + def.Id + "_ask", SceneDef.Narrator, $"Enter {name}, or stay here?", "idle")], [],
                    [new SceneOption("enter", "Enter", "enter", null, [], [], OptionTags.None, null),
                     new SceneOption("later", "Not yet", "later", null, [], [], OptionTags.None, null)], true, null, false),
                 new SceneNode("enter", [], [], [], false, null, true),
                 new SceneNode("later", [], [], [], false, null, true)], false);
            return new ScenePlan(scene, ImmutableDictionary<string, InteractOutcome>.Empty);
        }
        switch (kind.Family)
        {
            case InteractableFamily.Trade:
                return new InstantPlan(null, text[0]);
            case InteractableFamily.Ambient:
                return new InstantPlan(null, text[0]);
            case InteractableFamily.Notice:
                return new InstantPlan(null, NoticeText(def));        // "Look" says it again; nothing changes
            case InteractableFamily.Path:
                return null;                                          // ground, once found: nothing to do
            case InteractableFamily.Switch:
                var on = save.State != InteractableKinds.On;
                return new InstantPlan(new InteractOutcome(ThingEvents.Toggled, on ? InteractableKinds.On : InteractableKinds.Off,
                    Sets: on ? def.Sets : []), text[0]);
            case InteractableFamily.Pickup:
                return new InstantPlan(new InteractOutcome(ThingEvents.Taken, InteractableKinds.Taken, Loot: true, Sets: def.Sets),
                    def.Loot.IsEmpty ? $"You take the {name}." : $"Found: {Loot(def, itemName)}.");
            case InteractableFamily.Passage when worked:
                return new InstantPlan(null, $"The {name} won't budge. Something else works it.");
            case InteractableFamily.Readable:
            case InteractableFamily.Shrine:
                var verb = kind.Family == InteractableFamily.Readable ? ThingEvents.Read : ThingEvents.Used;
                var state = kind.Family == InteractableFamily.Readable ? InteractableKinds.Read : InteractableKinds.Used;
                return Scene(def, "words", [new Node("words", def.Text.IsDefaultOrEmpty ? text : def.Text, End: true)],
                    new() { ["words"] = new InteractOutcome(verb, state, Sets: def.Sets) });
            case InteractableFamily.FloorTrap:
                return TrapScene(def, save, after: null);
            default:
                return Opening(def, save, flags, itemName, again);
        }
    }

    /// <summary>
    /// A floor trap stepped on before it was seen: it goes off at once (design §1.6), whatever anyone wants. Null when there's no trap set
    /// there that the party hasn't seen.
    /// </summary>
    public static InstantPlan? SteppedOn(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags)
        => Interactables.KindOf(def).Family == InteractableFamily.FloorTrap && Interactables.Present(def, flags) && def.Trap is { } trap
           && save.Trap is null && !save.TrapSpotted
            ? new InstantPlan(Sprung(trap), SprungText(def)[0])
            : null;

    private static InteractOutcome Sprung(TrapDef trap, string? spend = null)
        => new(ThingEvents.Sprung, SpotTrap: true, Trap: InteractableSave.Sprung, Spend: spend, Sets: trap.Sets);

    /// <summary>
    /// What a trap with no words of its own says as it goes off. It isn't harmless any more (USER DECISION 3, decided 2026-11-03): the game
    /// says the minor injury it gave after it.
    /// </summary>
    public const string DefaultSprung = "Click. A trap goes off.";

    private static ImmutableArray<string> SprungText(InteractableDef def)
        => def.Trap!.Text.IsDefaultOrEmpty ? [DefaultSprung] : def.Trap.Text;

    /// <summary>A notice's words, as one line: what was noticed.</summary>
    public static string NoticeText(InteractableDef def) => string.Join(" ", Interactables.TextOf(def));

    // ---- An active Search (design §7.4) ----------------------------------------------------------------------------------------

    /// <summary>The search's scene id. No thing's scene has it: theirs start <see cref="ScenePrefix"/>.</summary>
    public const string SearchId = "search";

    /// <summary>The search's one option: its Investigation check, which the party picks who rolls for.</summary>
    public const string SearchOption = "search";

    /// <summary>The node a search goes to when its roll finds something.</summary>
    public const string SearchFound = "found";

    /// <summary>The node a search goes to when it finds nothing.</summary>
    public const string SearchNothing = "nothing";

    /// <summary>The DC a search with nothing to find routes by: past any roll, so it's always "nothing". Never shown: a search's DC isn't.</summary>
    public const int NothingToFind = 99;

    /// <summary>
    /// The scene of an active Search (design §7.4): a line, then straight to the party's pick of who rolls Investigation (the game decides the
    /// option at once, so the leader isn't asked twice), then "Something turns up." or "Nothing turns up." The check routes by the lowest DC
    /// of <paramref name="dcs"/>, the things it looks for, so it passes exactly when it finds at least one; each is then found or not by its
    /// own DC. With nothing to look for it plays the same, and fails, and neither the TV nor a phone is shown a DC.
    /// </summary>
    public static SceneDef SearchScene(IEnumerable<int> dcs)
    {
        var dc = dcs.DefaultIfEmpty(NothingToFind).Min();
        SceneLine Line(string node, string text) => new($"{SearchId}.{node}.0", SceneDef.Narrator, text, SceneLine.DefaultMood);
        var option = Option(SearchOption, "Search the area", null, check: new SceneCheck(Interactables.SearchSkill, dc, SearchFound, SearchNothing));
        ImmutableArray<SceneNode> nodes =
        [
            new(SearchId, [Line(SearchId, "The party searches the ground nearby.")], [], [option], false, null, false),
            new(SearchFound, [Line(SearchFound, "Something turns up.")], [], [], false, null, true),
            new(SearchNothing, [Line(SearchNothing, "Nothing turns up.")], [], [], false, null, true),
        ];
        return new SceneDef(SearchId, SearchId, null, "Search", [], [], [SceneDef.Narrator], SearchId, nodes, false);
    }

    /// <summary>The items of its loot, in words: "2 × Potion of Healing, Smoke Bomb".</summary>
    public static string Loot(InteractableDef def, Func<ItemId, string> itemName)
        => string.Join(", ", def.Loot.Select(l => l.Gold > 0 ? $"{l.Gold} gold" : l.Count == 1 ? itemName(l.Item) : $"{l.Count} × {itemName(l.Item)}"));

    // ---- Containers and passages ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Opening a container or a passage: its trap first, if one is set; then its lock, if it's locked; then it opens. With neither, it opens
    /// at once. A lock with no way left to try is said and left.
    /// </summary>
    private static InteractPlan Opening(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags, Func<ItemId, string> itemName, bool again)
    {
        var kind = Interactables.KindOf(def);
        var container = kind.Family == InteractableFamily.Container;
        var open = new InteractOutcome(ThingEvents.Opened, InteractableKinds.Opened, Loot: container, Sets: def.Sets);
        var openLine = container
            ? def.Loot.IsEmpty ? Interactables.TextOf(def)[0] : $"Inside: {Loot(def, itemName)}."
            : $"The {Interactables.NameOf(def)} opens.";
        var trapSet = def.Trap is not null && save.Trap is null;
        var locked = def.Lock is not null && !save.Unlocked;
        if (!trapSet && !locked)
            return new InstantPlan(open, openLine);
        if (!again && !trapSet && LockWays(def, save, flags).IsEmpty)
            return new InstantPlan(null, $"The {Interactables.NameOf(def)} is locked, and there's nothing left to try until the party has rested.");

        var nodes = new List<Node>();
        var outcomes = new Dictionary<string, InteractOutcome>();
        nodes.Add(new Node("open", [openLine], End: true));
        outcomes["open"] = open;
        var afterTrap = locked ? "locked" : "open";
        if (locked)
            AddLock(def, save, nodes, outcomes);
        if (!trapSet)
            return Scene(def, "locked", nodes, outcomes);
        var trap = (ScenePlan)TrapScene(def, save, afterTrap);
        nodes.AddRange(trap.Scene.Nodes.Select(Node.Of));
        foreach (var (id, outcome) in trap.Outcomes)
            outcomes[id] = outcome;
        return Scene(def, trap.Scene.Start, nodes, outcomes);
    }

    /// <summary>The ways through its lock still offered: the key whether or not the party holds it (the scene's flags decide), and each check not yet failed.</summary>
    private static ImmutableArray<(string Approach, string? Skill, int? Dc)> LockWays(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags)
        => [.. def.Lock!.Approaches.Where(a => a.Approach == Interactables.KeyApproach ? flags.Contains(def.Lock.Key!) : !save.Spent.Contains(a.Approach))];

    private static void AddLock(InteractableDef def, InteractableSave save, List<Node> nodes, Dictionary<string, InteractOutcome> outcomes)
    {
        var name = Interactables.NameOf(def);
        var lk = def.Lock!;
        var options = new List<SceneOption>();
        foreach (var (approach, skill, dc) in lk.Approaches)
        {
            if (approach == Interactables.KeyApproach)
            {
                options.Add(Option(approach, $"Use the {lk.KeyName ?? "key"}", "keyed", requires: [lk.Key!]));
                continue;
            }
            if (save.Spent.Contains(approach))
                continue;
            var label = approach switch
            {
                Interactables.PickApproach => "Pick the lock",
                Interactables.ForceApproach => $"Force the {name}",
                _ => "Break the seal",
            };
            options.Add(Option(approach, label, null, check: new SceneCheck(skill!, dc!.Value, "unlocked", $"{approach}_failed")));
            nodes.Add(new Node($"{approach}_failed", [approach switch
            {
                Interactables.PickApproach => "The pick slips, and the lock holds.",
                Interactables.ForceApproach => $"The {name} doesn't give.",
                _ => "The seal holds fast.",
            }], End: true));
            outcomes[$"{approach}_failed"] = new InteractOutcome(ThingEvents.Failed, Spend: approach);
        }
        options.Add(Option("leave", "Leave it", "leave"));
        nodes.Add(new Node("locked", [lk.Seal is not null && !save.Spent.Contains(Interactables.SealApproach) && lk.Pick is null && lk.Force is null
            ? $"A seal of old magic holds the {name} shut."
            : $"The {name} is locked."], Options: [.. options]));
        nodes.Add(new Node("unlocked", ["The lock gives."], Next: "open"));
        outcomes["unlocked"] = new InteractOutcome(ThingEvents.Unlocked, Unlock: true);
        nodes.Add(new Node("keyed", [$"The {lk.KeyName ?? "key"} turns in the lock."], Next: "open"));
        outcomes["keyed"] = new InteractOutcome(ThingEvents.Unlocked, Unlock: true, Key: true);
        AddLeave(nodes);
    }

    /// <summary>
    /// A trap set on a thing, or on the ground: seen, the party can take it apart (a check), leave it, or (on a thing) open it anyway; not
    /// seen, opening the thing springs it. A failed disarm springs it too. After it, the scene goes on to <paramref name="after"/>: the
    /// thing's lock or its opening. A floor trap has nothing after it.
    /// </summary>
    private static InteractPlan TrapScene(InteractableDef def, InteractableSave save, string? after)
    {
        var trap = def.Trap!;
        var name = Interactables.NameOf(def);
        var nodes = new List<Node>();
        var outcomes = new Dictionary<string, InteractOutcome>();
        var sprungText = SprungText(def);
        nodes.Add(new Node("sprung", sprungText, Next: after, End: after is null));
        outcomes["sprung"] = Sprung(trap);
        if (!save.TrapSpotted)
            return Scene(def, "sprung", nodes, outcomes);

        var options = new List<SceneOption>();
        if (trap.Disarm is { } dc && !save.Spent.Contains(Interactables.DisarmApproach))
        {
            options.Add(Option(Interactables.DisarmApproach, "Disarm the trap", null, check: new SceneCheck("sleight_of_hand", dc, "disarmed", "disarm_failed")));
            nodes.Add(new Node("disarmed", ["The trap comes apart in careful hands."], Next: after, End: after is null));
            outcomes["disarmed"] = new InteractOutcome(ThingEvents.Disarmed, Trap: InteractableSave.Disarmed);
            nodes.Add(new Node("disarm_failed", ["A wrong move.", .. sprungText], Next: after, End: after is null));
            outcomes["disarm_failed"] = Sprung(trap, Interactables.DisarmApproach);
        }
        options.Add(Option("leave", "Leave it", "leave"));
        if (after is not null)
            options.Add(Option("anyway", "Open it anyway", "sprung"));
        var where = after is null ? "There's a trap here." : $"There's a trap on the {name}.";
        nodes.Add(new Node("trap", [where], Options: [.. options]));
        AddLeave(nodes);
        return Scene(def, "trap", nodes, outcomes);
    }

    private static void AddLeave(List<Node> nodes)
    {
        if (!nodes.Any(n => n.Id == "leave"))
            nodes.Add(new Node("leave", ["You leave it be."], End: true));
    }

    private static SceneOption Option(string id, string label, string? to, ImmutableArray<string> requires = default, SceneCheck? check = null)
        => new(id, label, to, null, [], requires.IsDefault ? [] : requires, OptionTags.None, check);

    /// <summary>A node as it's built, before its lines get their ids.</summary>
    private sealed record Node(string Id, ImmutableArray<string> Lines, ImmutableArray<SceneOption> Options = default, string? Next = null, bool End = false)
    {
        public static Node Of(SceneNode n) => new(n.Id, [.. n.Lines.Select(l => l.Text)], n.Options, n.Next, n.End);
    }

    /// <summary>The scene, narrated, with the thing's name as its title; every option is the leader's to choose, as no vote is (5d default 3).</summary>
    private static ScenePlan Scene(InteractableDef def, string start, IEnumerable<Node> nodes, Dictionary<string, InteractOutcome> outcomes)
    {
        var id = SceneId(def);
        var all = nodes.DistinctBy(n => n.Id).ToList();
        // Only what can be reached from the start: a way that's spent leaves its nodes behind, and a scene has no dead nodes.
        var reached = new HashSet<string> { start };
        var open = new Queue<string>([start]);
        while (open.TryDequeue(out var at))
            if (all.FirstOrDefault(n => n.Id == at) is { } node)
                foreach (var next in (node.Next is { } n2 ? [n2] : Array.Empty<string>()).Concat((node.Options.IsDefault ? [] : node.Options).SelectMany(o => o.Targets)))
                    if (reached.Add(next))
                        open.Enqueue(next);
        ImmutableArray<SceneNode> built = [.. all.Where(n => reached.Contains(n.Id)).Select(n => new SceneNode(n.Id,
            [.. n.Lines.Select((text, i) => new SceneLine($"{id}.{n.Id}.{i}", SceneDef.Narrator, text, SceneLine.DefaultMood))],
            [], n.Options.IsDefault ? [] : n.Options, false, n.Next, n.End))];
        return new ScenePlan(new SceneDef(id, id, null, Interactables.TitleOf(def), [], [], [SceneDef.Narrator], start, built, false),
            outcomes.Where(o => reached.Contains(o.Key)).ToImmutableDictionary(StringComparer.Ordinal));
    }
}
