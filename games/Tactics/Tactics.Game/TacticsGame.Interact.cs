using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// Things to interact with in the world (interactables design §2): what's there and how it is, what stands in the way of walking, what the
/// leader can reach, and what using one does. Something with nothing to decide happens at once, in the world; anything with a choice or a
/// check, or words to read, is a scene in the Story phase, played by the story's own runtime (5d) with the cursor saved on every line, so its
/// checks are rolled by the party's pick of who rolls, and a restart can't roll again or give its loot twice.
/// </summary>
public sealed partial class TacticsGame
{
    /// <summary>How many of the things' events <see cref="ExploreView.ThingEvents"/> keeps. The TV takes only the newest view each frame.</summary>
    private const int ThingEventsKept = 8;

    /// <summary>
    /// The number of the last thing event. It only goes up, across a server restart too (<see cref="WorldSave.ThingSeq"/>): a TV that stayed
    /// up would otherwise take the new numbers for ones it had played, and drop them.
    /// </summary>
    private int _thingSeq;

    private ImmutableArray<ThingEventView> _thingEvents = [];

    /// <summary>The ground walked, and what's blocked on it, so the ground is built again only when that changes.</summary>
    private (string Region, ImmutableSortedSet<Tile> Blocked, Terrain Ground)? _walkGround;

    /// <summary>The party's best passive score in each skill a thing may be hidden by and whose it is, and the members and players it was worked out for.</summary>
    private (ImmutableSortedDictionary<string, MemberRecord> Members, IReadOnlyList<Player> Players, ImmutableDictionary<string, (int Passive, string? Who)> Best)? _passive;

    /// <summary>The scene of the interaction under way, and the cursor it was built for: it's built again only when that changes.</summary>
    private (ThingCursor Cursor, ScenePlan? Plan)? _thingPlan;

    /// <summary>The things of the region the party is in, in the order they're written.</summary>
    /// <summary>Every thing of the region the party is in, in the order they're written, there or not.</summary>
    private ImmutableArray<InteractableDef> AllThings => _region is null ? [] : Rules.InteractablesIn(_region.Def.Id);

    /// <summary>
    /// The things of the region as the party meets them: all of them but one that would stand in the way and has only just come to be there
    /// (its flags were set) on the leader's own tile. That one waits until the leader steps off it (<see cref="_appeared"/>), so nobody is
    /// ever put inside a door or a chest, and the TV never sees it appear on them.
    /// </summary>
    private ImmutableArray<InteractableDef> Things
    {
        get
        {
            var all = AllThings;
            if (all.IsEmpty || !all.Any(Gated))
                return all;
            var flags = StoryFlags();
            return [.. all.Where(d => !(Gated(d) && d.At == _at && !_appeared.Contains(d.Id) && Interactables.Blocks(d, ThingState(d), flags)))];
        }
    }

    private static bool Gated(InteractableDef d) => !d.Requires.IsEmpty || !d.Unless.IsEmpty;

    /// <summary>The things whose flags put them there while the leader stood elsewhere: they stand where they are from then on.</summary>
    private readonly HashSet<string> _appeared = new(StringComparer.Ordinal);

    /// <summary>Notes every gated thing that's there now and not under the leader, so it's never put off again.</summary>
    private void NoteAppeared()
    {
        var flags = StoryFlags();
        foreach (var d in AllThings.Where(d => Gated(d) && d.At != _at && Interactables.Present(d, flags)))
            _appeared.Add(d.Id);
    }

    /// <summary>The tiles the party could have walked to from the start on the ground as it is now: where it may be landed or sent.</summary>
    private IReadOnlySet<Tile> WalkReach()
    {
        var ground = WalkGround();
        if (ReferenceEquals(ground, Rules.GroundFor(_region!.Def.Id)!.Terrain))
            return Rules.ReachFor(_region.Def.Id)!;        // nothing in the way: the pack's own flood
        if (_walkReach is { } cached && ReferenceEquals(cached.Ground, ground))
            return cached.Reach;
        var reach = WorldWalk.Reach(ground, _region!.Def.Spawns.Start, ClassDef.DefaultJump);
        _walkReach = (ground, reach);
        return reach;
    }

    private (Terrain Ground, HashSet<Tile> Reach)? _walkReach;

    /// <summary>Someone's character came or went from the party mid-walk: their eyes count for passive Perception at once, not at the next step.</summary>
    private void PartyChanged()
    {
        if (_phase == TacticsPhase.Exploration && LookAround())
        {
            KeepWorld();
            Save();
        }
    }

    /// <summary>True when the tile holds something in the way that the TV hasn't been told of (a door nobody has found): bumping it says nothing.</summary>
    private bool UnseenBlocker(Tile at)
    {
        var flags = StoryFlags();
        return AllThings.Any(d => d.At == at && Interactables.Blocks(d, ThingState(d), flags) && !Interactables.Visible(d, ThingState(d), flags));
    }

    private ImmutableSortedDictionary<string, InteractableSave>? ThingSaves => _doc.World?.Things;

    private InteractableSave ThingState(InteractableDef def) => def.Kind == "stall"
        ? new InteractableSave(RegionShops.Any(s => s.Keeper.Thing == def.Id && ShopRules.Present(s, StoryFlags())) ? "open" : "closed")
        : Interactables.StateOf(def, ThingSaves);

    private InteractableDef? ThingNamed(string id) => AllThings.FirstOrDefault(d => d.Id == id);

    private string ItemName(ItemId id) => Rules.Items.GetValueOrDefault(id)?.Name ?? id.Value;

    /// <summary>
    /// The ground the party walks: the region's, with whatever its things put in the way now (design §2.4). Walking, reach, standing, landing
    /// and travel all use it, so nobody walks through a closed door.
    /// </summary>
    private Terrain WalkGround()
    {
        var region = _region!.Def.Id;
        var blocked = Interactables.Blocking(Things, ThingSaves, StoryFlags());
        if (_walkGround is { } cached && cached.Region == region && cached.Blocked.SetEquals(blocked))
            return cached.Ground;
        var ground = Rules.GroundFor(region)!.With(blocked);
        _walkGround = (region, blocked, ground);
        return ground;
    }

    /// <summary>What the leader can use now, with each one's button, the nearest first (<see cref="Interactables.Targets"/>).</summary>
    private ImmutableArray<(InteractableDef Def, string Label)> ThingTargets()
        => _phase != TacticsPhase.Exploration || _region is null || Things.IsEmpty ? []
            : Interactables.Targets(Things.Where(d => !RegionShops.Any(s => s.Keeper.Thing == d.Id && ShopRules.Present(s, StoryFlags()))), ThingSaves, StoryFlags(), WalkGround(), _at, _facing);

    /// <summary>
    /// The leader's Interact: <c>{target}</c> names the thing, as the phone's list does; with none, the beat in reach, or else the first thing
    /// in reach. Checked again here, since the phone can be a step behind.
    /// </summary>
    private void Interact(Player player, JsonElement? data)
    {
        if (player.Profile?.Value != _lead)
            return;
        var target = Str(data, "target");
        if (target is null && InteractLabel() is not null)
        {
            InteractBeat(player);
            return;
        }
        var targets = TradeTargets();
        var pick = target is null ? targets.FirstOrDefault() : targets.FirstOrDefault(t => t.Def.Id == target);
        if (pick.Def is not null && pick.Def.Id.StartsWith("shop:", StringComparison.Ordinal)
            && RegionShops.FirstOrDefault(s => "shop:" + s.Id == pick.Def.Id) is { } shop)
        { StartShop(shop); return; }
        if (target is not null && RegionShops.FirstOrDefault(s => s.Keeper.Thing == target && ShopRules.Present(s, StoryFlags())) is { } stall
            && targets.Any(t => t.Def.Id == "shop:" + stall.Id)) { StartShop(stall); return; }
        if (pick.Def is not null) Use(pick.Def);
    }

    /// <summary>Uses <paramref name="def"/>: at once, or as an interaction's scene.</summary>
    private void Use(InteractableDef def)
    {
        var save = ThingState(def);
        var worked = Interactables.Worked(AllThings).Contains(def.Id);
        switch (InteractScenes.Plan(def, save, StoryFlags(), worked, ItemName))
        {
            case InstantPlan instant:
                Halt();
                if (instant.Outcome is { } outcome && ShutsOnTheParty(def, outcome))
                {
                    Event(def, ThingEvents.Looked, _lead, "Someone's standing in the way.");
                    Say("Someone's standing in the way.");
                    return;
                }
                if (instant.Outcome is { } o)
                    Apply(def, o, _lead!, instant.Line);
                else
                    Event(def, ThingEvents.Looked, _lead, instant.Line);
                Say(instant.Line);
                KeepWorld();
                Save();
                break;
            case ScenePlan scene:
                LeaveExploration();
                _toasts.Clear();
                _phase = TacticsPhase.Story;
                // A battle beat waiting at its fight keeps its cursor through the interaction, under the thing's, and has it back after.
                SetCursor(new StorySave("", StorySave.Lead, scene.Scene.Id, null, 0, _storySeq, Thing: new ThingCursor(def.Id, save, _lead!, Cursor)));
                EnterNode(scene.Scene, scene.Scene.Start);
                break;
        }
    }

    /// <summary>Puts a line up on the TV, in place of whatever the last thing said.</summary>
    private void Say(string line)
    {
        Unnote(_thingNote);
        _thingNote = line;
        Note(line);
    }

    private string? _thingNote;

    /// <summary>True when pulling a lever would shut one of its passages on the leader's tile: it doesn't move, so nobody is shut in a wall.</summary>
    private bool ShutsOnTheParty(InteractableDef lever, InteractOutcome outcome)
        => Interactables.KindOf(lever).Family == InteractableFamily.Switch && outcome.State == InteractableKinds.Off
           && lever.Targets.Any(id => ThingNamed(id)?.At == _at);

    /// <summary>
    /// Applies one step of an interaction to <paramref name="def"/>, begun by <paramref name="by"/> (design §2.7): its new state, its flags,
    /// its loot to <paramref name="by"/> if this step opens it, its trap's effect on them if this step springs it, and a lever's passages.
    /// All of it goes in the document; the caller saves.
    /// </summary>
    private void Apply(InteractableDef def, InteractOutcome outcome, string by, string? line = null)
    {
        var before = ThingState(def);
        var after = outcome.Apply(before);
        SetThing(def, after);
        AddFlags(outcome.Sets);
        if (InteractScenes.LootDue(before, outcome))
            GiveLoot(def, by);
        if (InteractScenes.SpringDue(before, outcome))
            Spring(def, by);
        Event(def, outcome.Event, by, line);
        if (Interactables.KindOf(def).Family == InteractableFamily.Switch && after.State != before.State)
            foreach (var target in def.Targets.Select(ThingNamed).OfType<InteractableDef>())
            {
                SetThing(target, ThingState(target) with { State = after.State == InteractableKinds.On ? InteractableKinds.Opened : InteractableKinds.Closed });
                if (Interactables.Visible(target, ThingState(target), StoryFlags()))
                    Event(target, ThingEvents.Toggled, by, null);      // a passage that isn't there, or isn't found, moves unseen
            }
    }

    private void SetThing(InteractableDef def, InteractableSave save)
    {
        var world = _doc.World!;
        var things = world.Things ?? Ordinal.Map<InteractableSave>();
        if (things.TryGetValue(def.Id, out var kept) && kept == save)
            return;
        _doc = _doc with { World = world with { Things = things.SetItem(def.Id, save) } };
    }

    /// <summary>The thing's loot goes into <paramref name="member"/>'s bag, stacking with what's there (design §3.1, USER DECISION 1).</summary>
    private void GiveLoot(InteractableDef def, string member)
    {
        foreach (var loot in def.Loot.Where(l => l.Gold > 0))
            _doc = _doc with { Gold = ShopRules.Gold(_doc.Gold, loot.Gold) };
        if (def.Loot.IsEmpty || !_doc.Members.TryGetValue(member, out var record))
            return;
        var bag = record.Character.Bag.ToList();
        foreach (var loot in def.Loot.Where(l => l.Gold == 0 && Rules.Items.ContainsKey(l.Item)))
        {
            var at = bag.FindIndex(s => s.Item == loot.Item);
            if (at >= 0)
                bag[at] = bag[at] with { Count = bag[at].Count + loot.Count };
            else
                bag.Add(new ItemStack(loot.Item, loot.Count));
        }
        _doc = _doc with { Members = _doc.Members.SetItem(member, record with { Character = record.Character with { Bag = [.. bag] } }) };
    }

    /// <summary>
    /// A trap goes off on <paramref name="member"/> (design §3.2): its flags are set, and by default that's all. A trap the content gives an
    /// injury or a lost item does that too; nothing in the base pack does yet (USER DECISION 3).
    /// </summary>
    private void Spring(InteractableDef def, string member)
    {
        var trap = def.Trap!;
        AddFlags(trap.Sets);
        if (!_doc.Members.TryGetValue(member, out var record))
            return;
        var character = record.Character;
        if (trap.Injury is { } injury && Rules.Injuries.ContainsKey(new InjuryId(injury)))
            character = character with { Injuries = character.Injuries.Add(new InjuryId(injury)) };
        if (trap.Lose is { } lose && character.Bag.FirstOrDefault(s => s.Item == lose) is { } stack)
            character = character with
            {
                Bag = stack.Count > 1 ? character.Bag.Replace(stack, stack with { Count = stack.Count - 1 }) : character.Bag.Remove(stack),
            };
        if (character != record.Character)
            _doc = _doc with { Members = _doc.Members.SetItem(member, record with { Character = character }) };
    }

    private void Event(InteractableDef def, string what, string? by, string? line)
    {
        var name = by is null ? null : _doc.Members.GetValueOrDefault(by)?.Character.Name;
        var e = new ThingEventView(++_thingSeq, def.Id, what, name, line);
        if (_doc.World is { } world)
            _doc = _doc with { World = world with { ThingSeq = _thingSeq } };
        _thingEvents = (_thingEvents.Length < ThingEventsKept ? _thingEvents : _thingEvents.RemoveAt(0)).Add(e);
    }

    // ---- Walking past things ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The best passive score of the session's party in each skill a thing may be hidden by (design §7.1), here or not (5d default 8, as checks
    /// count away characters), and whose it is, the first in member order on a tie; worked out again only when a character or the seats change.
    /// </summary>
    private ImmutableDictionary<string, (int Passive, string? Who)> PartyPassives()
    {
        if (_passive is { } cached && cached.Members == _doc.Members && cached.Players == _players)
            return cached.Best;
        var best = HiddenDef.Skills.ToDictionary(s => s, _ => (Passive: int.MinValue, Who: (string?)null), StringComparer.Ordinal);
        foreach (var (member, _, character, _) in Party())
        {
            var sheet = SheetBuilder.ForCharacter(character, Rules);
            foreach (var skill in HiddenDef.Skills)
            {
                var passive = Interactables.Passive(sheet, skill);
                if (passive > best[skill].Passive)
                    best[skill] = (passive, member);
            }
        }
        var all = best.ToImmutableDictionary(StringComparer.Ordinal);
        _passive = (_doc.Members, _players, all);
        return all;
    }

    /// <summary>
    /// The party has stepped onto <see cref="_at"/> (design §2.6): a trap there that nobody had seen goes off on the leader, and the party's
    /// passive Perception finds what's hidden near. Either is saved at once. The party walks on: a trap that's been sprung does nothing more.
    /// </summary>
    private void SteppedOnto()
    {
        if (AllThings.IsEmpty)
            return;
        NoteAppeared();
        var flags = StoryFlags();
        var changed = false;
        foreach (var def in Things.Where(d => d.At == _at))
            if (InteractScenes.SteppedOn(def, ThingState(def), flags) is { Outcome: { } sprung } plan)
            {
                Apply(def, sprung, _lead!, plan.Line);
                Say(plan.Line);
                changed = true;
            }
        changed |= LookAround();
        if (changed)
        {
            KeepWorld();
            Save();
        }
    }

    /// <summary>
    /// What the party's passive skills find from where the leader stands (design §2.6, §7.1): each hidden thing by its own skill, traps by
    /// Perception, what a notice reveals with it, and then, since a find can bring a trap on it into sight or reveal more, again until nothing
    /// new is found. Everything found says its line, all on the TV's notice at once. True if anything was found.
    /// </summary>
    private bool LookAround()
    {
        if (Things.IsEmpty)
            return false;
        var passives = PartyPassives();
        if (passives[HiddenDef.Perception].Who is null)
            return false;       // nobody has a character yet
        var lines = new List<string>();
        var any = false;
        for (var pass = 0; pass <= AllThings.Length; pass++)
        {
            // Nothing is found under the leader: a hidden chest found there would stand on them. It's found as they step off it.
            var near = Things.Where(d => d.At != _at).ToList();
            var flags = StoryFlags();
            var found = 0;
            foreach (var (def, finding, skill) in Interactables.Finds(near, ThingSaves, flags, _at, s => passives[s].Passive))
                found += Find(def, finding, passives[skill].Who!, lines, FindHow.Passive) ? 1 : 0;
            // What a notice noticed earlier still reveals: a thing that wasn't there then, or was under the leader. Who noticed it isn't kept,
            // so the line names nobody: "The hidden cache is found."
            foreach (var notice in near.Where(d => Interactables.KindOf(d).Family == InteractableFamily.Notice && ThingState(d).Found))
                foreach (var (def, finding) in Interactables.Revealing(notice, near, ThingSaves, StoryFlags()))
                    found += Find(def, finding, null, lines, FindHow.Later) ? 1 : 0;
            if (found == 0)
                break;
            any = true;
        }
        if (lines.Count > 0)
            Say(string.Join(" ", lines));
        return any;
    }

    /// <summary>How a thing came to be found, for its line: by a passive skill, by a search's roll, led to by a notice, or by a notice later.</summary>
    private enum FindHow
    {
        Passive,
        Search,
        Revealed,
        Later,
    }

    /// <summary>
    /// <paramref name="who"/> finds <paramref name="def"/> (design §2.6, §7): a hidden thing is found, a notice noticed (its flags set, its
    /// words said, and what it reveals found with it), a hidden path found (its flags set), or a trap seen. Its line goes on
    /// <paramref name="lines"/>. False, and nothing done, if it already was, or if it isn't there with the flags as they are now: a notice found
    /// a moment before may have set a flag that takes it away (review). <paramref name="who"/> is null when nobody is named (a notice's
    /// later reveal: who noticed it isn't kept). The caller saves.
    /// </summary>
    private bool Find(InteractableDef def, Interactables.Finding finding, string? who, List<string> lines, FindHow how)
    {
        if (!Interactables.Present(def, StoryFlags()))
            return false;
        var save = ThingState(def);
        var name = who is null ? null : _doc.Members.GetValueOrDefault(who)?.Character.Name ?? who;
        string line;
        if (finding == Interactables.Finding.Spotted)
        {
            if (save.TrapSpotted || save.Trap is not null)
                return false;
            SetThing(def, save with { TrapSpotted = true });
            line = name is null ? "A trap is seen." : $"{name} spots a trap.";
            Event(def, ThingEvents.Spotted, who, line);
            lines.Add(line);
            return true;
        }
        if (save.Found)
            return false;
        SetThing(def, save with { Found = true });
        if (Interactables.FoundIsItsOutcome(def))
            AddFlags(def.Sets);
        // Only what the finder's own skill found says how ("senses" for Arcana and Religion): something a notice led to is just spotted.
        var verb = how switch
        {
            FindHow.Search => "finds",
            FindHow.Passive when def.Hidden?.Skill is "arcana" or "religion" => "senses",
            _ => "spots",
        };
        line = (finding, name) switch
        {
            (Interactables.Finding.Noticed, null) => $"Noticed: {InteractScenes.NoticeText(def)}",
            (Interactables.Finding.Noticed, _) => $"{name} notices: {InteractScenes.NoticeText(def)}",
            (_, null) => $"The {Interactables.NameOf(def)} is found.",
            _ => $"{name} {verb} a {Interactables.NameOf(def)}.",
        };
        Event(def, finding == Interactables.Finding.Noticed ? ThingEvents.Noticed : ThingEvents.Revealed, who, line);
        lines.Add(line);
        if (finding == Interactables.Finding.Noticed)
            foreach (var (next, nextFinding) in Interactables.Revealing(def, Things.Where(d => d.At != _at), ThingSaves, StoryFlags()))
                Find(next, nextFinding, who, lines, how == FindHow.Later ? FindHow.Later : FindHow.Revealed);
        return true;
    }

    // ---- An active Search (design §7.4) -----------------------------------------------------------------------------------------

    /// <summary>
    /// The leader searches (design §7.4): a scene in the Story phase whose one check, Investigation, the party picks who rolls. It looks for
    /// every hidden thing within <see cref="Interactables.SearchRange"/> not found and not already searched for, fixed now and kept in the
    /// cursor, so a restart judges the same ones. With none it plays just the same, and finds nothing.
    /// </summary>
    private void Search(Player player)
    {
        if (player.Profile?.Value != _lead || _region is null || AllThings.IsEmpty || !Party().Any())
            return;     // as LookAround: with nobody's character in the party, there's nobody to roll
        var looked = Interactables.Searchable(Things, ThingSaves, StoryFlags(), _at);
        LeaveExploration();
        _toasts.Clear();
        _phase = TacticsPhase.Story;
        var thing = new ThingCursor("", new InteractableSave(InteractScenes.SearchId), _lead!, Cursor, [.. looked.Select(d => d.Id)],
            [.. looked.Select(d => d.Hidden!.Dc)]);
        SetCursor(new StorySave("", StorySave.Lead, InteractScenes.SearchId, null, 0, _storySeq, Thing: thing));
        var scene = ThingPlan(thing)!.Scene;
        EnterNode(scene, scene.Start);
    }

    /// <summary>
    /// A node of the search is entered. Its first: the option is decided at once, so the line goes straight on to the party's pick of who
    /// rolls (saved as <see cref="StorySave.Checking"/>, as any decided check is). Its result, with the roll that led there: each thing it
    /// looked for that's there and not found is found if the roll meets its own DC as it was when the search began (kept in the cursor, as the
    /// check routed by the same DCs: so "Something turns up." always finds something, whatever a content update did meanwhile), and is
    /// otherwise searched for, for good. Each is judged with the flags as they are by then: a notice it found may have taken another away.
    /// </summary>
    private void SearchNodeEntered(ThingCursor cursor, SceneNode node, StoryRoll? roll)
    {
        if (node.Id == InteractScenes.SearchId)
        {
            SetCursor(Cursor! with { Checking = InteractScenes.SearchOption });
            return;
        }
        if (roll is null)
            return;
        var total = roll.Die + roll.Bonus;
        var lines = new List<string>();
        for (var i = 0; i < cursor.Search.Length; i++)
        {
            if (ThingNamed(cursor.Search[i]) is not { Hidden: not null } def)
                continue;
            var save = ThingState(def);
            if (save.Found || !Interactables.Present(def, StoryFlags()))
                continue;
            var dc = SearchDc(cursor, i, def);
            if (node.Id == InteractScenes.SearchFound && total >= dc)
                Find(def, Interactables.FindingOf(def), roll.Member, lines, FindHow.Search);
            else
                SetThing(def, save with { Searched = true });
        }
        // Only if what it would have found is gone from the pack since: the line said something turned up, so say what came of it.
        if (node.Id == InteractScenes.SearchFound && lines.Count == 0)
            lines.Add("It was nothing after all.");
        if (lines.Count > 0)
            Say(string.Join(" ", lines));
    }

    /// <summary>The DC of the <paramref name="i"/>th thing a search looks for, as it was when it began; a search saved before DCs were kept uses today's.</summary>
    private static int SearchDc(ThingCursor cursor, int i, InteractableDef? def)
        => !cursor.SearchDcs.IsDefault && i < cursor.SearchDcs.Length ? cursor.SearchDcs[i] : def?.Hidden?.Dc ?? InteractScenes.NothingToFind;

    // ---- Interactions as scenes -------------------------------------------------------------------------------------------------

    /// <summary>The scene of the interaction <paramref name="cursor"/> names, built again from the state it began in; null if the thing is gone.</summary>
    private ScenePlan? ThingPlan(ThingCursor cursor)
    {
        if (_thingPlan is { } cached && ReferenceEquals(cached.Cursor, cursor))
            return cached.Plan;
        var plan = cursor.Searching
            ? new ScenePlan(InteractScenes.SearchScene(cursor.Search.Select((id, i) => SearchDc(cursor, i, ThingNamed(id)))),
                ImmutableDictionary<string, InteractOutcome>.Empty)
            : ThingNamed(cursor.Id) is { } def
            ? InteractScenes.Plan(def, cursor.From, StoryFlags(), Interactables.Worked(AllThings).Contains(def.Id), ItemName, again: true) as ScenePlan
            : null;
        _thingPlan = (cursor, plan);
        return plan;
    }

    /// <summary>
    /// A node of an interaction's scene is entered: its outcome applies, in the same save as the node's first line. <paramref name="roll"/>
    /// is the roll that led there, if one did (a search judges by it).
    /// </summary>
    private void ThingNodeEntered(SceneNode node, StoryRoll? roll)
    {
        if (Cursor?.Thing is { Searching: true } search)
        {
            SearchNodeEntered(search, node, roll);
            return;
        }
        if (Cursor?.Thing is not { } cursor || ThingPlan(cursor) is not { } plan || !plan.Outcomes.TryGetValue(node.Id, out var outcome)
            || ThingNamed(cursor.Id) is not { } def)
            return;
        Apply(def, outcome, cursor.By, node.Lines.Length > 0 ? node.Lines[^1].Text : null);
    }

    /// <summary>The interaction's scene has ended: the cursor goes, and the party walks again.</summary>
    private void FinishInteraction()
    {
        EndTelling();
        SetCursor(Cursor?.Thing?.Under);        // a battle beat that was waiting at its fight goes on waiting
        KeepWorld();
        Save();
        ToExploration();
    }

    /// <summary>
    /// Back into an interaction after a restart (5d §8): on its saved line, or its roll's result, as a beat's scene would be. A thing that's
    /// gone, or a scene that no longer has the saved node, ends it with a notice: nothing it already did is undone.
    /// </summary>
    private void ResumeInteraction(StorySave c, ThingCursor thing)
    {
        var scene = ThingPlan(thing)?.Scene;
        var node = c.Node is null ? null : scene?.Node(c.Node);
        if (scene is null || scene.Id != c.Scene || (c.Node is not null && (node is null || c.Line < 0 || c.Line >= node.Lines.Length)))
        {
            Note(StoryChanged);
            SetCursor(thing.Under);
            Save();
            ToExploration();
            return;
        }
        _phase = TacticsPhase.Story;
        if (node is null)
            EnterNode(scene, scene.Start);
        else if (c.Roll is not null)
            ShowRoll();
        else
            PlayLine(scene, node, c.Line);
    }

    // ---- Views ----------------------------------------------------------------------------------------------------------------

    /// <summary>The plugs of wall over the region's unfound shut passages (<see cref="ExploreView.Plugs"/>). Null for a region with no things.</summary>
    private ImmutableArray<WallPlug>? PlugViews()
    {
        if (Things.IsEmpty)
            return null;
        var flags = StoryFlags();
        return [.. Things.Where(d => Interactables.Plugged(d, ThingState(d), flags)).OrderBy(d => d.At).Select(d => new WallPlug(d.At, d.Facing ?? "sw"))];
    }

    /// <summary>What the TV draws of the region's things: only what's there and has been seen (design §2.9). Null for a region with none.</summary>
    private ImmutableArray<ThingView>? ThingViews()
    {
        if (Things.IsEmpty)
            return null;
        var flags = StoryFlags();
        return [.. Things.Select(d => (Def: d, Save: ThingState(d))).Where(t => Interactables.Visible(t.Def, t.Save, flags) && Interactables.Drawn(t.Def, t.Save))
            .Select(t => ThingView.Of(t.Def, t.Save, flags))];
    }
}
