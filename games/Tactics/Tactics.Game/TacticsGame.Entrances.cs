using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    private bool EntranceVote => Cursor is { Node: "ask", Thing: { } thing } && ThingNamed(thing.Id)?.To is not null;

    private void EntranceVoteDue()
    {
        var voters = Voters().Select(p => p.Profile!.Value.Value).ToArray();
        if (voters.Length == 0) { Hold(); return; }
        var yes = voters.Count(v => !_votes.TryGetValue(v, out var pick) || pick == "enter");
        // As with rest, unanswered votes count as yes, but a tie stays put. Decide closes the question.
        Resolve(yes * 2 > voters.Length ? "enter" : "later");
    }

    private bool TravelThrough(InteractableDef mouth, StorySave? under)
    {
        var to = mouth.To!;
        var destination = Rules.RegionFor(to.Region);
        if (destination?.Def.Spawns.Find(to.Spawn) is not { } at)
        { Note(StoryChanged); return false; }
        var blocking = Interactables.Blocking(Rules.InteractablesIn(to.Region), _doc.World!.Things, StoryFlags());
        var ground = Rules.GroundFor(to.Region)!.With(blocking, StoryFlags());
        if (!ground.Walkable(at) || !Rules.ReachFor(to.Region)!.Contains(at))
        { Note("The passage's destination is blocked. The party stays here."); return false; }

        EndTelling();
        var world = _doc.World!;
        var stories = world.RegionStories ?? Ordinal.Map<StorySave>();
        stories = under is null ? stories.Remove(world.Region) : stories.SetItem(world.Region, under);
        var resume = stories.GetValueOrDefault(to.Region);
        stories = stories.Remove(to.Region);
        _doc = _doc with { World = world with { Story = resume, RegionStories = stories.Count == 0 ? null : stories } };
        _region = destination;
        (_at, _facing, _arrived) = (at, new Tile(at.X + 1, at.Y), Travelled);
        _epoch++;
        _legs = [];
        _walkGround = null;
        _walkReach = null;
        _thingPlan = null;
        _thingEvents = [];
        _appeared.Clear();
        _edgeSaid = false;
        _notice = null;
        _thingNote = null;
        AddFlags(mouth.Sets);
        KeepWorld();
        Save();
        ToExploration();
        return true;
    }
}
