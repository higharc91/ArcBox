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

    /// <summary>The party's best passive Perception and whose it is, and the members and players it was worked out for.</summary>
    private (ImmutableSortedDictionary<string, MemberRecord> Members, IReadOnlyList<Player> Players, int Passive, string? Who)? _passive;

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

    private InteractableSave ThingState(InteractableDef def) => Interactables.StateOf(def, ThingSaves);

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
            : Interactables.Targets(Things, ThingSaves, StoryFlags(), WalkGround(), _at, _facing);

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
        var targets = ThingTargets();
        var pick = target is null ? targets.FirstOrDefault() : targets.FirstOrDefault(t => t.Def.Id == target);
        if (pick.Def is not null)
            Use(pick.Def);
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
        if (def.Loot.IsEmpty || !_doc.Members.TryGetValue(member, out var record))
            return;
        var bag = record.Character.Bag.ToList();
        foreach (var loot in def.Loot.Where(l => Rules.Items.ContainsKey(l.Item)))
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
    /// The best passive Perception of the session's party, here or not (5d default 8, as checks count away characters), and whose it is, the
    /// first in member order on a tie; worked out again only when a character or the seats change.
    /// </summary>
    private (int Passive, string? Who) PartyPassive()
    {
        if (_passive is { } cached && cached.Members == _doc.Members && cached.Players == _players)
            return (cached.Passive, cached.Who);
        (int Passive, string? Who) best = (int.MinValue, null);
        foreach (var (member, _, character, _) in Party())
        {
            var passive = Interactables.Passive(SheetBuilder.ForCharacter(character, Rules));
            if (passive > best.Passive)
                best = (passive, member);
        }
        _passive = (_doc.Members, _players, best.Passive, best.Who);
        return best;
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

    /// <summary>What the party's passive Perception finds from where the leader stands; true if anything was found.</summary>
    private bool LookAround()
    {
        if (Things.IsEmpty)
            return false;
        var (passive, who) = PartyPassive();
        if (who is null)
            return false;       // nobody has a character yet
        var finds = Interactables.Finds(Things, ThingSaves, StoryFlags(), _at, passive);
        foreach (var (def, found) in finds)
        {
            var save = ThingState(def);
            SetThing(def, found == Interactables.Finding.Revealed ? save with { Found = true } : save with { TrapSpotted = true });
            var name = _doc.Members[who].Character.Name;
            var line = found == Interactables.Finding.Revealed ? $"{name} spots a {Interactables.NameOf(def)}." : $"{name} spots a trap.";
            Event(def, found == Interactables.Finding.Revealed ? ThingEvents.Revealed : ThingEvents.Spotted, who, line);
            Say(line);
        }
        return !finds.IsEmpty;
    }

    // ---- Interactions as scenes -------------------------------------------------------------------------------------------------

    /// <summary>The scene of the interaction <paramref name="cursor"/> names, built again from the state it began in; null if the thing is gone.</summary>
    private ScenePlan? ThingPlan(ThingCursor cursor)
    {
        if (_thingPlan is { } cached && ReferenceEquals(cached.Cursor, cursor))
            return cached.Plan;
        var plan = ThingNamed(cursor.Id) is { } def
            ? InteractScenes.Plan(def, cursor.From, StoryFlags(), Interactables.Worked(AllThings).Contains(def.Id), ItemName, again: true) as ScenePlan
            : null;
        _thingPlan = (cursor, plan);
        return plan;
    }

    /// <summary>A node of an interaction's scene is entered: its outcome applies, in the same save as the node's first line.</summary>
    private void ThingNodeEntered(SceneNode node)
    {
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
