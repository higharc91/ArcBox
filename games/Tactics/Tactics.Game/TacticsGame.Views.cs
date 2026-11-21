using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>A unit as the TV and phones draw it.</summary>
/// <param name="Color">
/// The TV's alone (spec §4.5): a hero's player's colour, "#rrggbb" (<see cref="WalkerView.Color"/>), for their ring in the fight; null for
/// everyone else, and always on a phone, which never sees it.
/// </param>
public sealed record UnitView(UnitId Id, string Name, string Side, string? Sprite, Tile At, int Hp, int MaxHp, int TempHp,
    string Life, int Countdown, ImmutableArray<string> Conditions, string? Player,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Color = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? BaseAc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Role = null);

/// <summary>
/// An ability on the phone's turn screen. <see cref="Targets"/> are the targets that affect someone (with their
/// previews); <see cref="Tiles"/> are the other legal aim tiles, bare, since a tile-aimed area spell can have hundreds
/// and each would otherwise repeat an empty preview (adapter spec §8: the view stays under 64 KB). <see cref="Attacks"/> is
/// set for a weapon attack of several attacks (<see cref="AbilityChoice.Attacks"/>): the phone offers "Attack 2 of N" and
/// sends a <c>targets</c> list (<see cref="Use.Targets"/>).
/// </summary>
public sealed record PhonePreview(UnitId Unit, double Damage, double Heal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Roll = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DamageMin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DamageMax = null);
public sealed record PhoneTarget(Target Target, Tile Aim, ImmutableArray<PhonePreview> Affected,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<Tile> Spread = default,
    [property: JsonPropertyName("p"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<int> PreviewIds = default);

public sealed record PhoneAbility(AbilityId Ability, ItemId? Item, string Name, string Tooltip, CostDef Cost,
    ImmutableArray<PhoneTarget> Targets, ImmutableArray<Tile> Tiles, string Aim = "unit",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Attacks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<AimRoll> Rolls = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<PhonePreview> Previews = default);

/// <summary><see cref="TurnChoices"/> as the phone gets them, abilities compacted (<see cref="PhoneAbility"/>).</summary>
public sealed record PhoneChoices(UnitId Unit, TurnEconomy Economy, ImmutableArray<MoveChoice> Moves,
    ImmutableArray<PhoneAbility> Abilities, bool CanDelay)
{
    /// <summary>
    /// Compacts <paramref name="choices"/>: a tile target that affects no one becomes a bare tile. Each ability says how
    /// it's aimed (<see cref="AimOf"/>), so the phone can say "Pick a direction" for a cone.
    /// </summary>
    public static PhoneChoices Of(TurnChoices choices, RulesPack? rules = null) => new(choices.Unit, choices.Economy, choices.Moves,
        [.. choices.Abilities.Select(a =>
        {
            bool Bare(TargetChoice t) => t.Target.Unit is null && t.Target.Tile == t.Aim && t.Affected.IsEmpty && t.Spread.IsDefaultOrEmpty;
            var def = rules is null ? null
                : a.Item is { } item && rules.Items.TryGetValue(item, out var it) ? rules.Abilities.GetValueOrDefault(it.Use)
                : rules.Abilities.GetValueOrDefault(a.Ability);
            var rolls = ImmutableArray.CreateBuilder<AimRoll>();
            PhonePreview Preview(UnitPreview p)
            {
                int? index = null;
                if (p.Roll is { } roll)
                {
                    var found = rolls.IndexOf(roll);
                    if (found < 0) { found = rolls.Count; rolls.Add(roll); }
                    index = found;
                }
                return new(p.Unit, p.Damage, p.Heal, index, p.DamageMin, p.DamageMax);
            }
            var previews = ImmutableArray.CreateBuilder<PhonePreview>();
            int Intern(UnitPreview p)
            {
                var value = Preview(p); var found = previews.IndexOf(value);
                if (found < 0) { found = previews.Count; previews.Add(value); }
                return found;
            }
            var targets = a.Targets.Where(t => !Bare(t)).Select(t => new PhoneTarget(t.Target, t.Aim, [], t.Spread,
                t.Affected.IsEmpty ? default : [.. t.Affected.Select(Intern)])).ToImmutableArray();
            return new PhoneAbility(a.Ability, a.Item, a.Name, a.Tooltip, a.Cost,
                targets, [.. a.Targets.Where(Bare).Select(t => t.Aim)], AimOf(def), a.Attacks, rolls.Count == 0 ? default : rolls.ToImmutable(), previews.Count == 0 ? default : previews.ToImmutable());
        })],
        choices.CanDelay);

    /// <summary>"self", "direction" (a line or cone from the user), "tile" or "unit".</summary>
    public static string AimOf(AbilityDef? ability) => ability?.Target switch
    {
        null => "unit",
        { Kind: TargetKind.Self, Area.Shape: AreaShape.Line or AreaShape.Cone } => "direction",
        { Kind: TargetKind.Self } or { Area.Shape: AreaShape.AllAllies or AreaShape.AllEnemies } => "self",
        { Kind: TargetKind.Tile or TargetKind.EmptyTile } => "tile",
        _ => "unit",
    };
}

/// <summary>A lasting area effect on the map, as the TV draws it.</summary>
public sealed record ZoneView(int Serial, string Kind, ImmutableArray<Tile> Tiles);

/// <summary>A tile's height, changed from the map's by <paramref name="Delta"/> steps.</summary>
public sealed record HeightChange(Tile At, int Delta);

/// <summary>What the battle has done to the map so far. The TV's alone: phones don't get it.</summary>
/// <param name="Zones">The zones on the map (<see cref="BattleState.Zones"/>).</param>
/// <param name="Heights">The changed heights (<see cref="BattleState.HeightDelta"/>).</param>
/// <param name="Sunk">The tiles that became deep (<see cref="BattleState.Sunk"/>).</param>
public sealed record BoardChanges(ImmutableArray<ZoneView> Zones, ImmutableArray<HeightChange> Heights, ImmutableArray<Tile> Sunk)
{
    /// <summary>An untouched map.</summary>
    public static BoardChanges None { get; } = new([], [], []);

    /// <summary>The changes in <paramref name="state"/>.</summary>
    public static BoardChanges Of(BattleState state) => new(
        [.. state.Zones.Select(z => new ZoneView(z.Serial, z.Kind, z.Tiles))],
        [.. state.HeightDelta.Select(h => new HeightChange(h.Key, h.Value))],
        state.Sunk is { } sunk ? [.. sunk] : []);
}

/// <summary>
/// The sprite key the TV gets for a unit. A character's sheet has no sprite, since a record says nothing about looks yet, so
/// it's drawn as its class's preset; a monster or a wild shape has its own. Phones don't get this: their views are unchanged.
/// </summary>
public static class HostSprite
{
    private static readonly ImmutableHashSet<string> LegacyRaces =
        ["human", "elf", "half_elf", "dwarf", "halfling", "gnome", "half_orc", "tiefling", "aasimar", "goliath", "dragonborn"];
    /// <summary>The key for a sheet with this sprite and these tags, or null if it has neither a sprite nor a class.</summary>
    public static string? Of(string? sprite, IEnumerable<string> tags)
    {
        if (sprite is not null)
            return sprite;
        var all = tags.ToArray();
        var cls = all.FirstOrDefault(t => t.StartsWith(SheetBuilder.ClassTag, StringComparison.Ordinal))?[SheetBuilder.ClassTag.Length..];
        if (cls is null)
            return null;
        var race = all.FirstOrDefault(t => t.StartsWith(SheetBuilder.RaceTag, StringComparison.Ordinal))?[SheetBuilder.RaceTag.Length..]
            ?? all.FirstOrDefault(LegacyRaces.Contains);  // campaign battles saved before explicit race tags
        return $"characters.{(race is null ? "class" : race)}_{cls}";
    }
}

/// <summary>
/// Where a battle stands in the world (overworld 5c §3), for the TV to draw the board in place. Tiles in the battle are still the map's
/// own: <paramref name="Origin"/> is the world tile of the map's (0, 0), and <paramref name="Height"/> is added to its heights.
/// </summary>
/// <param name="Region">The region's id, as <see cref="ExploreView.Region"/>.</param>
/// <param name="Origin">The world tile of the battle map's tile (0, 0).</param>
/// <param name="Height">The levels the stamp is raised by.</param>
/// <param name="Heroes">
/// The walker each <c>hero&lt;n&gt;</c> is, by <see cref="WalkerView.Id"/>, in order: <c>hero1</c> first. A hero the AI plays has no
/// <see cref="UnitView.Player"/>, so this is the only way to tell which walker steps into which slot.
/// </param>
public sealed record StageView(string Region, Tile Origin, int Height, ImmutableArray<string> Heroes);

/// <summary>The battle as the TV draws it (adapter spec §8).</summary>
/// <param name="Encounter">The encounter.</param>
/// <param name="Map">The map.</param>
/// <param name="Round">The round.</param>
/// <param name="Order">This round's turn order.</param>
/// <param name="Active">Whose turn it is.</param>
/// <param name="Units">The units.</param>
/// <param name="Beats">The last beats.</param>
/// <param name="Outcome">How it ended, if it has.</param>
/// <param name="Board">What it has done to the map.</param>
/// <param name="Objectives">What wins and loses it, as they stand (<see cref="ObjectiveState.Current"/>).</param>
/// <param name="Reacting">The unit whose player has a reaction prompt waiting on their phone. An AI's offer isn't a wait, so it isn't here.</param>
/// <param name="Name">The encounter's own name. <paramref name="Encounter"/> is its id.</param>
/// <param name="Stage">Where it stands in the world, or null for a battle on a plain board: no region, or a map the region doesn't stamp.</param>
public sealed record BattleView(string Encounter, string Map, int Round, ImmutableArray<UnitId> Order, UnitId? Active,
    ImmutableArray<UnitView> Units, ImmutableArray<Beat> Beats, string? Outcome, BoardChanges? Board = null, ObjectivesDef? Objectives = null,
    UnitId? Reacting = null, string? Name = null, StageView? Stage = null, string? Difficulty = null,
    RetreatView? Retreat = null);

/// <summary>An encounter the VIP can pick, with how many AI companions would fill the party up to its minimum.</summary>
public sealed record EncounterOption(string Id, string Name, int? Level, int Min, int Max, bool Fits, int Companions = 0,
    string? Guidance = null)
{
    /// <summary>Small parties get companions; only the encounter's maximum prevents entry.</summary>
    public string? UnavailableReason => Fits ? null : $"Party too big (max {Max})";

    /// <summary>Level guidance compares the intended level, not a prediction of the battle's outcome.</summary>
    public static string GuidanceFor(int? level, int partyLevel) => level switch
    {
        null => "Level not specified",
        _ when level > partyLevel => "Higher level",
        _ when level < partyLevel => "Lower level",
        _ => "Recommended",
    };
}

/// <summary>A class a member can pick at Muster.</summary>
public sealed record ClassOption(string Id, string Name, string Role);

/// <summary>A member's line on the results screen.</summary>
public sealed record ResultLine(string Name, int Xp, ImmutableArray<string> Injuries, int TotalXp = 0, int? NextLevelXp = null, int PrevLevelXp = 0, int Level = 1);

/// <summary>One step the party took: the tile it reached and how long the step takes.</summary>
/// <param name="Seq">The step's number, which only goes up.</param>
/// <param name="To">The tile at the end of it.</param>
/// <param name="Ms">How long it takes to walk.</param>
public sealed record WalkLeg(int Seq, Tile To, int Ms);

/// <summary>A member of the party on the TV.</summary>
/// <param name="Id">Their profile.</param>
/// <param name="Name">Their player's name.</param>
/// <param name="Sprite">Their sprite key (<see cref="HostSprite"/>).</param>
/// <param name="Here">True while their phone is connected.</param>
/// <param name="Leads">True for the leader.</param>
/// <param name="Color">
/// The player's colour, "#rrggbb" (<see cref="PlayerColors"/>), for their ring: the one they picked, else the palette's for their place in
/// join order. Always readable on grass, stone and ash.
/// </param>
public sealed record WalkerView(string Id, string Name, string Sprite, bool Here, bool Leads, string? Color = null);

/// <summary>
/// The party in the world, for the TV: in every phase once a region exists, so a TV that joins mid-fight can learn the region. It
/// changes only when a step commits, the party turns, or <paramref name="Held"/> or <paramref name="Blocked"/> does.
/// </summary>
/// <param name="Region">The region's id.</param>
/// <param name="Name">The region's name.</param>
/// <param name="Epoch">Changes on a teleport, or a new session: the path can't be followed across it, so the TV snaps.</param>
/// <param name="Seq">The number of the last step.</param>
/// <param name="At">The leader's tile.</param>
/// <param name="Facing">The tile the leader faces.</param>
/// <param name="Held">True while the leader holds a direction the party can walk. Against a wall it's false, so the party stands, and <paramref name="Facing"/> says which way.</param>
/// <param name="Legs">The last few steps, oldest first. The TV can miss views, so it fills in from these.</param>
/// <param name="Party">The party, in member order.</param>
/// <param name="Leader">The leader's player's name, or null with nobody here to lead.</param>
/// <param name="Chapter">The region's chapter, for the TV's panel.</param>
/// <param name="Blocked">
/// True while the leader holds a direction a wall refused (spec §4.5, overworld 5b review item 2), so the TV can bump the leader
/// against it: a press that does nothing must not look like letting go or a dropped phone. It's false again once the thumb lifts, so
/// each press against a wall is its own bump. The phone isn't told, because that would mint a new view at every release; the leader's phone has the count instead (<paramref name="Bumps"/>).
/// </param>
/// <param name="Bumps">
/// How many steps a wall has refused, counting up from 0 each session: every press, change of direction or latched quick tap that a
/// wall turns back, and a held walk reaching one. The TV bumps whenever it goes up, which catches what <paramref name="Blocked"/> can't:
/// a latched tap into a wall is refused and let go in the same moment. A new <paramref name="Epoch"/> may start it again from 0.
/// </param>
/// <param name="Arrived">
/// How the party came to <paramref name="At"/> when a fight in the world ended, so the TV knows how to show that <paramref name="Epoch"/>
/// (overworld 5c §3): <c>fellIn</c> after a win or a flight, where the followers fall in behind the leader; <c>woke</c> after a loss, at
/// the recovery spawn. Null otherwise, and again from the party's next step or fight.
/// </param>
/// <param name="Markers">
/// The story beat on offer, where it waits (overworld 5d §2): at most one, since one beat is offered at a time. It changes only when what's
/// on offer does, or its scene starts or ends, never with a step.
/// </param>
/// <param name="Cast">
/// Who stands at the scene's spot while a scene plays (the user's answer of 2026-11-01, item 21): its speakers who aren't the narrator, in
/// the order they first speak, each on a tile of their own that no walker stands on. Empty outside a scene, and in the chapter's card.
/// </param>
/// <param name="Things">
/// The things to interact with that the TV may draw (interactables design §2.9): what's there and has been seen, in the order they're
/// written, each in its state. A hidden thing or a trap nobody has seen isn't sent. Null, and left out of the JSON, for a region with no things.
/// </param>
/// <param name="ThingEvents">
/// What has just happened to things, oldest first, for the TV to animate (design §2.8): the last few, each numbered. The numbers go on
/// across a server restart (<see cref="WorldSave.ThingSeq"/>). Null, and left out of the JSON, for a region with no things.
/// </param>
/// <param name="Plugs">
/// Where the TV draws wall over a gap: each shut passage nobody has found (a secret door), by tile and which way its wall runs, and nothing
/// else, so it gives nothing away; the region is painted with the gap open. In tile order. Null, and left out of the JSON, for a region with
/// no things.
/// </param>
public sealed record ExploreView(string Region, string Name, int Epoch, int Seq, Tile At, Tile Facing, bool Held,
    ImmutableArray<WalkLeg> Legs, ImmutableArray<WalkerView> Party, string? Leader, int Chapter = 0, bool Blocked = false, int Bumps = 0,
    string? Arrived = null, ImmutableArray<StoryMarker>? Markers = null, ImmutableArray<CastView>? Cast = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<ThingView>? Things = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<ThingEventView>? ThingEvents = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<WallPlug>? Plugs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Gold = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<TraderView>? Traders = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Morning = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<string>? PeopleFlags = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BarkView? Bark = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BarkView? ThingSpeech = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] QuestEventView? QuestEvent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] QuestTrackerView? Pinned = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] QuestLogView? QuestLog = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<QuestPersonMarker>? QuestMarkers = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<MobView>? Mobs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Danger = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Weather = null);

public sealed record MobView(string Id, string Group, string Sprite, Tile At, long Step,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<WalkLeg> Legs = default);

public sealed record QuestPersonMarker(Tile At, bool TurnIn);

public sealed record QuestTrackerView(string Id, string Name, string Objective, string Status, Tile? Target);
public sealed record QuestHistoryView(string Log, bool Done, string? Objective);
public sealed record QuestItemView(string Name, int Count);
public sealed record QuestRewardView(int Gold, int Xp, ImmutableArray<QuestItemView> Items);
public sealed record QuestEntryView(string Id, string Name, string Status, string Giver, string? Portrait, string Objective, ImmutableArray<QuestHistoryView> History, string? Result, QuestRewardView? Rewards);
/// <summary>
/// The quest journal. <see cref="Keys"/> is the party's key ring (USER DECISION 8, decided 2026-11-03), for everyone: written only once the
/// party holds a key, so a journal with none is as it was.
/// </summary>
public sealed record QuestLogView(ImmutableArray<QuestEntryView> Quests, string? Selected, string? Pinned, bool CanPin, string? Reading,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<KeyInfo>? Keys = null);

public sealed record TrinketGiftView(string Member, string Name, string Class, string Trinket, string Title, bool Here);
public sealed record QuestEventView(int Seq, string Quest, string Name, string Kind, DateTimeOffset Until, int Gold, int Xp,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<TrinketGiftView>? Gifts = null);

/// <summary>A numbered, expiring person utterance; never a phase change.</summary>
public sealed record BarkView(string Person, long Seq, DateTimeOffset Until, StoryLineView Line);

/// <summary>A reachable trader marker, never the full shop stock.</summary>
public sealed record TraderView(string Id, Tile At, int? Person, bool Open, string? Sprite = null);

/// <summary>A plug of wall for the TV to draw over a gap (<see cref="ExploreView.Plugs"/>): only where, and which way the wall runs.</summary>
/// <param name="At">The gap's tile.</param>
/// <param name="Facing">Which way its wall runs, as a passage's facing: <c>sw</c> and <c>ne</c> along x, <c>se</c> and <c>nw</c> along y.</param>
public sealed record WallPlug(Tile At, string Facing);

/// <summary>A thing to interact with, as the TV draws it (interactables design §2.9, §4).</summary>
/// <param name="Id">Its id.</param>
/// <param name="Kind">Its kind (<see cref="InteractableKinds"/>).</param>
/// <param name="At">Its tile.</param>
/// <param name="Facing">Which way its sprite faces: <c>ne</c>, <c>se</c>, <c>sw</c> or <c>nw</c>.</param>
/// <param name="Art">Its art key, without its state (<c>interactables.chest</c>): the art brief's, which may not be made yet.</param>
/// <param name="State">The state its art is drawn in: the art's variant (<c>closed</c>, <c>open</c>, <c>on</c>, <c>armed</c>…).</param>
/// <param name="StandIn">An art key the pack has now, to draw until <paramref name="Art"/> exists; null for a plain marker.</param>
/// <param name="Name">Its name, as a title.</param>
/// <param name="Blocks">True while it stands in the way of walking.</param>
/// <param name="Trapped">True for a thing whose trap is set and has been seen: the TV warns of it.</param>
public sealed record ThingView(string Id, string Kind, Tile At, string Facing, string Art, string State, string? StandIn, string Name, bool Blocks,
    bool Trapped)
{
    /// <summary>
    /// <paramref name="def"/> as the TV is sent it in <paramref name="save"/>'s state with <paramref name="flags"/> set, whether or not it's
    /// <see cref="Interactables.Visible"/> (the caller sends only what is): the server's views, and the art check's (<c>--world-demo=things</c>).
    /// </summary>
    public static ThingView Of(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags)
        => new(def.Id, def.Kind, def.At, def.Facing ?? "sw", Interactables.ArtOf(def), Interactables.ArtState(def, save), Interactables.StandInOf(def, save),
            Interactables.TitleOf(def), Interactables.Blocks(def, save, flags), Interactables.Trapped(def, save));
}

/// <summary>Something that happened to a thing, for the TV to animate (interactables design §2.8).</summary>
/// <param name="Seq">Its number, which only goes up in a session.</param>
/// <param name="Id">The thing.</param>
/// <param name="What">What happened: one of <see cref="Tactics.Logic.ThingEvents"/>.</param>
/// <param name="By">The name of the character who did it, or found it; null with nobody.</param>
/// <param name="Line">What the TV says of it, or null.</param>
public sealed record ThingEventView(int Seq, string Id, string What, string? By, string? Line);

/// <summary>A speaker of the scene under way, standing in the world (<see cref="ExploreView.Cast"/>). Dressing only: it doesn't block the party.</summary>
/// <param name="Id">The character's id in the lore.</param>
/// <param name="Name">The character's name.</param>
/// <param name="Sprite">Their sprite key: <c>characters.&lt;preset&gt;</c>.</param>
/// <param name="At">The tile they stand on.</param>
/// <param name="Facing">The tile they look towards: the leader's, so they face the party.</param>
public sealed record CastView(string Id, string Name, string Sprite, Tile At, Tile Facing);

/// <summary>
/// A story beat's marker in the world (overworld 5d §2, default 12): who stands at its spot, and the emote over them. Dressing only: the
/// figure doesn't block the party.
/// </summary>
/// <param name="Beat">The beat.</param>
/// <param name="At">Its spot.</param>
/// <param name="Sprite">The sprite key of the figure standing there: the first of the cast of the scene it opens with who isn't the narrator. Null with nobody to show.</param>
/// <param name="Emote">The emote over them: <c>alert</c> (the "!") while the beat waits, null while its scene plays.</param>
public sealed record StoryMarker(string Beat, Tile At, string? Sprite, string? Emote);

/// <summary>A line of a scene, as the TV shows and speaks it (overworld 5d §9).</summary>
/// <param name="Id">The line's id, which its clip is keyed by (<c>VoiceClips.ForLine</c>).</param>
/// <param name="Speaker">A character's id, or <c>narrator</c>.</param>
/// <param name="Name">The speaker's name, or null for the narrator.</param>
/// <param name="Portrait">The speaker's portrait preset (<c>portraits/&lt;preset&gt;_&lt;mood&gt;.png</c>), or null for the narrator, who has none.</param>
/// <param name="Mood">The portrait's mood.</param>
/// <param name="Text">What's said.</param>
/// <param name="Role">The speaker's role in the lore (<c>ally</c>, <c>npc</c>, <c>rival</c>, <c>villain</c>), for the portrait's backdrop; null for the narrator.</param>
public sealed record StoryLineView(string Id, string Speaker, string? Name, string? Portrait, string Mood, string Text, string? Role = null);

/// <summary>What a scene is doing, for the TV (overworld 5d §9). <see cref="Seq"/> is the line's: a new one is a line to say.</summary>
/// <param name="Seq">The number of the line's showing; it only goes up, across a restart too.</param>
/// <param name="Scene">The scene's id.</param>
/// <param name="Title">Its title.</param>
/// <param name="State">One of <see cref="TacticsGame.StoryStates"/>.</param>
/// <param name="Line">The line showing, or null in a gap or a roll's reveal.</param>
/// <param name="Ask">The question open, or null.</param>
/// <param name="Check">The skill check under way, or null.</param>
/// <param name="Until">When what's showing ends: the line (a question's close is <see cref="StoryAskView.Deadline"/>), the gap or the reveal.</param>
/// <param name="Campaign">
/// The campaign the story is of: <see cref="Seq"/> counts within it, so a TV that sees it change knows another story has begun, and what it
/// heard of the last one says nothing of this one.
/// </param>
public sealed record StoryView(int Seq, string Scene, string Title, string State, StoryLineView? Line, StoryAskView? Ask, StoryCheckView? Check,
    DateTimeOffset? Until, string? Campaign = null);

/// <summary>An option of a question, for the TV, with who has voted for it: the tally is the TV's alone (5d flag 7).</summary>
public sealed record StoryOptionView(string Id, string Label, string? Skill, int? Dc, ImmutableArray<string> Voters);

/// <summary>A question open in a scene, for the TV.</summary>
/// <param name="Party">True for a party vote; false for a choice that's the leader's.</param>
/// <param name="Voted">How many have voted.</param>
/// <param name="Of">How many can: everyone here with a character, and anyone who voted and has gone.</param>
/// <param name="Away">The players whose characters are in the party but whose phones aren't here: they don't vote.</param>
/// <param name="Tie">True while a tie is being broken.</param>
/// <param name="Decider">Who breaks the tie, or chooses for the party: their player's name.</param>
/// <param name="Deadline">When it closes, or null while it waits for a phone.</param>
/// <param name="Options">What's offered, in the node's order; only the tied during a tie-break.</param>
/// <param name="Span">
/// How long its clock ran when it started, in seconds, with <paramref name="Deadline"/>: the question's length, or longer if the line asking it
/// outlasts that. The TV counts down against it, and learns how far its own clock is off the server's when it sees a question open.
/// </param>
/// <param name="Colors">
/// Each party player's colour, "#rrggbb", by their player's name (the names <see cref="StoryOptionView.Voters"/> holds), for the vote dots.
/// </param>
public sealed record StoryAskView(bool Party, int Voted, int Of, ImmutableArray<string> Away, bool Tie, string? Decider, DateTimeOffset? Deadline,
    ImmutableArray<StoryOptionView> Options, double? Span = null, ImmutableSortedDictionary<string, string>? Colors = null);

/// <summary>A character who can roll a skill check, for the TV.</summary>
/// <param name="Member">Their member id.</param>
/// <param name="Name">The character's name.</param>
/// <param name="Player">Their player's name.</param>
/// <param name="Sprite">Their sprite key (<see cref="HostSprite"/>).</param>
/// <param name="Bonus">What they add to the die.</param>
/// <param name="Note">Where the bonus comes from: "Proficient · WIS 14", or the stat alone.</param>
/// <param name="Here">False when their phone isn't here: they can still be picked.</param>
/// <param name="Votes">How many picked them.</param>
/// <param name="Class">Their class's name, as the phone's card shows it.</param>
/// <param name="Color">Their player's colour, "#rrggbb" (<see cref="WalkerView.Color"/>).</param>
public sealed record StoryCardView(string Member, string Name, string Player, string? Sprite, int Bonus, string Note, bool Here, int Votes,
    string? Class = null, string? Color = null);

/// <summary>A skill check under way, for the TV.</summary>
/// <param name="Skill">The skill.</param>
/// <param name="Dc">Its DC; null for an active Search, whose DC isn't shown: each thing it looks for has its own, and it mustn't say whether there are any (interactables design §7.4).</param>
/// <param name="Title">The option's label.</param>
/// <param name="Cards">Who can roll, in member order.</param>
/// <param name="Chosen">Who rolls, once picked.</param>
/// <param name="Roll">The roll, once made.</param>
/// <param name="Tie">True while a tie over who rolls is being broken.</param>
/// <param name="Decider">Who breaks it: their player's name.</param>
/// <param name="Span">How long the pick's clock ran when it started, in seconds, with <paramref name="Deadline"/> (<see cref="StoryAskView.Span"/>).</param>
/// <param name="Deadline">When the pick closes, or null.</param>
public sealed record StoryCheckView(string Skill, int? Dc, string Title, ImmutableArray<StoryCardView> Cards, string? Chosen, RollResult? Roll,
    bool Tie = false, string? Decider = null, DateTimeOffset? Deadline = null, double? Span = null, string? Helper = null, bool GroupCheck = false, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<StoryGroupRoll> Group = default);

/// <summary>The TV's view of a tactics session (adapter spec §8).</summary>
/// <param name="Phase">The phase.</param>
/// <param name="Banner">The prompt banner.</param>
/// <param name="Notice">A note for everyone, or null.</param>
/// <param name="PartyLevel">The party's level.</param>
/// <param name="Mustered">Who has a character.</param>
/// <param name="Encounters">The list at Encounter pick.</param>
/// <param name="Battle">The battle in progress.</param>
/// <param name="Outcome">How the last battle ended.</param>
/// <param name="Results">The results' lines.</param>
/// <param name="LastBattle">
/// At Results, the battle as it ended. The blow that ends a battle goes straight to Results, so no <paramref name="Battle"/>
/// ever carries it: without this the TV could never play it.
/// </param>
/// <param name="Explore">The party in the world, in every phase; null for a pack with no region.</param>
/// <param name="Story">The scene under way, in the Story phase; null otherwise.</param>
/// <param name="Chapter">The chapter that has just ended, while its card shows ("Chapter I · The Ashen Ford"); null otherwise.</param>
/// <param name="ResultNote">At Results after a loss, what the loss did to the party (<see cref="TacticsGame.LossNote"/>); null otherwise.</param>
public sealed record TacticsHostView(string Phase, string Banner, string? Notice, int PartyLevel,
    ImmutableArray<string> Mustered, ImmutableArray<EncounterOption>? Encounters, BattleView? Battle,
    string? Outcome, ImmutableArray<ResultLine>? Results, BattleView? LastBattle = null, ExploreView? Explore = null, StoryView? Story = null,
    string? Chapter = null, string? ResultNote = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ShopView? Shop = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CampView? Camp = null,
    bool LeveledUp = false, int Gold = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<LootDef>? Loot = null);

/// <summary>The TV shop panel, sent separately from the walking packet.</summary>
public sealed record ShopView(string Id, string Name, string Keeper, string Greeting, long Gold, int Seq, ImmutableArray<ShopItemView> Items, ImmutableArray<string> Feed, string? Portrait = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<PurseView> Purses = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<ShopService>? Services = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<ServiceGuestView>? Guests = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<RecoveryMemberView>? Members = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HaggleView? Haggle = null);

/// <summary>This visit's haggle, for the TV (USER DECISION E, decided 2026-11-03): who rolled, the roll, the DC, and whether the prices moved.</summary>
public sealed record HaggleView(string Name, int Die, int Bonus, int Dc, bool Passed);

/// <summary>A recovery service's possible guest, including absent campaign members.</summary>
public sealed record ServiceGuestView(string Member, string Name, ImmutableArray<string> Injuries, bool Room);
/// <summary>One participant's saved job and clear gathering tile.</summary>
public sealed record CampPersonView(string Member, string Name, string? Job, Tile At, bool WellFed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Target = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Outcome = null);
/// <summary>Saved recovery presentation, separate from walking and normal story packets.</summary>
public sealed record CampView(string Mode, string Source, string Stage, Tile At, int Visit, ImmutableArray<CampPersonView> People,
    CampResult? Result, int ResultCount, DateTimeOffset? Until,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<RecoveryMemberView>? Members = null);

/// <summary>A character's individual purse, for the shared TV shop display.</summary>
public sealed record PurseView(string Member, string Name, int Gold);
/// <summary>One stock row.</summary>
public sealed record ShopItemView(string Item, string Name, int Price, int Count);

public sealed partial class TacticsGame
{
    /// <inheritdoc />
    public object HostView() => new TacticsHostView(_phase.ToString(), Banner(), Notice(), _doc.PartyLevel,
        [.. _players.Where(p => _doc.For(p.Profile!.Value) is not null).Select(p => p.Name)],
        _phase == TacticsPhase.EncounterPick ? EncounterOptions() : null,
        _doc.Battle is not null && _phase is TacticsPhase.Battle or TacticsPhase.Resuming ? BattleViewOf() : null,
        _result?.Outcome.ToString(), _phase == TacticsPhase.Results ? ResultLines() : null,
        _phase == TacticsPhase.Results ? _lastBattle : null, ExploreViewOf(), Coloured(StoryViewOf()), _card, _phase == TacticsPhase.Results ? _resultNote : null, ShopViewOf(), CampViewOf(),
        _phase == TacticsPhase.Results && _resultLeveledUp,
        _phase == TacticsPhase.Results ? _resultGold : 0,
        _phase == TacticsPhase.Results && !_resultLoot.IsDefaultOrEmpty ? _resultLoot : null);

    /// <summary>The player's colour: the one kept with them (every member has one, <see cref="PlayerColors.Assign"/>).</summary>
    private string ColorOf(string member) => _doc.Members.GetValueOrDefault(member)?.Color ?? PlayerColors.Default(0);

    /// <summary>A scene's view with the players' colours on its question and its roll's cards, for the TV's vote dots.</summary>
    private StoryView? Coloured(StoryView? story) => story is null ? null : story with
    {
        Ask = story.Ask is { } ask
            ? ask with { Colors = Walkers().ToImmutableSortedDictionary(w => w.Player.Name, w => ColorOf(w.Player.Profile!.Value.Value), StringComparer.Ordinal) }
            : null,
        Check = story.Check is { } check
            ? check with { Cards = [.. check.Cards.Select(c => c with { Color = ColorOf(c.Member) })] }
            : null,
    };

    /// <inheritdoc />
    public ControllerView PlayerView(PlayerId id)
    {
        var player = _players.FirstOrDefault(p => p.Id == id);
        if (player?.Profile is not { } profile)
            return new WaitView("Waiting…");
        var view = _phase switch
        {
            TacticsPhase.Story when Camp is not null => CampScreen(player, profile),
            TacticsPhase.Story when OpenShop is not null && !Haggling => ShopScreen(player, profile),
            TacticsPhase.Muster or TacticsPhase.Exploration or TacticsPhase.Story when _doc.For(profile) is null => Custom(Screens.PickClass, new
            {
                classes = Rules.Classes.Values.Select(c => new ClassOption(c.Id, c.Name, c.Role)),
                portal = PortalPath(),
            }),
            TacticsPhase.Muster when _doc.For(profile) is { } musterMember && PendingChoices(musterMember.Character) > 0 => Custom(Screens.PickClass, new
            {
                classes = Rules.Classes.Values.Select(c => new ClassOption(c.Id, c.Name, c.Role)),
                portal = PortalPath(),
                me = CharacterChoices(musterMember),
            }),
            TacticsPhase.Muster => new WaitView("Waiting for everyone to choose a class"),
            TacticsPhase.Exploration when QuestLogOpen => QuestLogScreen(player, profile),
            TacticsPhase.Exploration => WalkScreen(player, profile),
            TacticsPhase.Story => StoryScreen(player, profile),
            // canWalk: whether "Back to walking" goes anywhere, which it doesn't in a pack with no region (decisions 9 and 29).
            TacticsPhase.EncounterPick when IsVip(player) => Custom(Screens.PickEncounter, new { encounters = EncounterOptions(), canWalk = _region is not null, partyLevel = _doc.PartyLevel }),
            TacticsPhase.EncounterPick => new WaitView($"Waiting for {Vip?.Name ?? "the VIP"} to choose a battle"),
            TacticsPhase.Battle => BattleScreen(profile),
            TacticsPhase.Results => Custom(Screens.Results, new
            {
                outcome = _result?.Outcome.ToString(), encounter = _resultEncounter, lines = ResultLines(), note = _resultNote, vip = IsVip(player), difficulty = CampaignDifficulty.ToString(),
                level = _doc.PartyLevel, portal = PortalPath(),
                leveledUp = _resultLeveledUp,
                gold = _resultGold > 0 ? _resultGold : (int?)null,
                loot = _resultLoot.IsDefaultOrEmpty ? null : _resultLoot.Select(l => new { id = l.Item.Value, name = Rules.Items.GetValueOrDefault(l.Item)?.Name ?? l.Item.Value, count = l.Count }).ToArray(),
                choices = _doc.For(profile) is { } resMember ? CharacterChoices(resMember) : null,
            }),
            TacticsPhase.Resuming when IsVip(player) => new ButtonsView(
                $"Resuming… waiting for {string.Join(", ", Waiting())}", [new Option("continue", "Continue without them")]),
            TacticsPhase.Resuming => new WaitView($"Resuming… waiting for {string.Join(", ", Waiting())}"),
            _ => (ControllerView)new WaitView("Waiting…"),
        };
        view = WithControlNotice(view, profile);
        // Recovery is rare and uses the existing header; it stays stable across walk presses/releases.
        if (_phase == TacticsPhase.Exploration && view is CustomView recovered && _worldRecoveryNotice is { } recovery)
            return recovered with { Header = recovery };
        // A toast changes the view, and a new view means a new viewId under the leader's thumb.
        return view is CustomView { Screen: not Screens.Walk } custom && _toasts.TryGetValue(id, out var toast)
            ? custom with { Header = toast.Text }
            : view;
    }

    /// <summary>
    /// The walk screen. It holds nothing that changes as the party walks (no tile, deadline or toast): the server mints a new
    /// view only when this JSON changes, and a new view would drop whatever the pad is sending. The one exception is the leader's
    /// <c>interact</c> (overworld 5d §2), which changes only on the step that brings a beat's spot into reach or takes it out of reach.
    /// </summary>
    private CustomView WalkScreen(Player player, ProfileId profile)
    {
        var character = _doc.For(profile)!.Character;
        var vip = IsVip(player);
        var leads = _lead == profile.Value;
        var targets = leads ? TradeTargets() : [];
        var pointable = leads ? [] : TradeTargets();
        var data = JsonSerializer.SerializeToNode(new
        {
            leader = leads,
            leaderName = Leader?.Name,
            region = _region?.Def.Name,
            objective = Objective(),
            me = new { name = character.Name, cls = ClassName(character), level = _doc.PartyLevel, color = ColorOf(profile.Value) },
            party = Walkers().Select(w => new { name = w.Player.Name, cls = ClassName(w.Character), here = w.Here, leads = w.Leads,
                color = ColorOf(w.Player.Profile!.Value.Value) }),
            vip,
            difficulty = CampaignDifficulty.ToString(),
            canBattle = vip && battleList && !Rules.Encounters.IsEmpty,
            portal = PortalPath(),
            interact = leads ? InteractLabel() ?? targets.FirstOrDefault().Label : null,
        }, ArcJson.Options)!.AsObject();
        if (PendingChoices(character) is var waiting and > 0)
            data["choices"] = JsonSerializer.SerializeToNode(CharacterChoices(_doc.For(profile)!), ArcJson.Options);
        if (_doc.Members.Count > 1) data["canGive"] = true;
        if (_giving.Contains(profile.Value)) data["give"] = JsonSerializer.SerializeToNode(GiveScreen(profile), ArcJson.Options);
        if (_doc.World?.MobAmbush is { } ambush) data["ambush"] = Rules.Mobs.Values.SelectMany(m => m).Where(m => m.Id == ambush).Select(m => Rules.Encounters[m.Encounter].Name).FirstOrDefault();
        if (leads && NearbyMob() is { } danger) data["danger"] = danger;
        // Everything in reach, for the phone's list (interactables design §2.5): it changes only as things come into reach or go. Only in a
        // region that has things, so a walk screen anywhere else is exactly as it was.
        if (HasJournal)
        {
            data["journal"] = JsonSerializer.SerializeToNode(Journal(leads), ArcJson.Options);
            if (leads) data["canOpenLog"] = true;
            if (_questEvent is { } notice && notice.Until > _ctx.Now)
            {
                var quest = RegionQuests.FirstOrDefault(q => q.Id == notice.Quest)?.State(StoryFlags());
                data["questNotice"] = JsonSerializer.SerializeToNode(new { id = notice.Quest, notice.Name, notice.Kind,
                    objective = quest?.Status == "active" ? quest.Current.Objective : "Quest " + (quest?.Status == "done" ? "complete" : "failed"),
                    canPin = leads && quest?.Status == "active" }, ArcJson.Options);
                if (notice.Gifts?.FirstOrDefault(g => g.Member == profile.Value) is { } gift
                    && Rules.Trinkets.TryGetValue(gift.Trinket, out var trinket))
                    data["questNotice"]!["trinket"] = JsonSerializer.SerializeToNode(new
                    { id = trinket.Id, name = trinket.Name, text = trinket.Text, rarity = trinket.Rarity,
                        @class = trinket.Class, className = Rules.Classes[trinket.Class].Name, active = true }, ArcJson.Options);
            }
            if (QuestTracker() is { } tracker) data["pinned"] = JsonSerializer.SerializeToNode(new { tracker.Id, tracker.Name, tracker.Objective }, ArcJson.Options);
        }
        if (HasCampfires && _doc.World?.RestReady == true) data["restReady"] = true;
        // #154: the leader can hand the lead on; a follower can point out what's in reach. The heirs are the members here (not
        // whether they're silent, which a walk input changes), so a step never changes a phone; PassLead checks the rest.
        if (leads && _doc.Members.Keys.Where(m => m != profile.Value && _players.Any(p => p.Profile?.Value == m && _online.Contains(p.Id)))
                .ToArray() is { Length: > 0 } heirs)
            data["passTo"] = JsonSerializer.SerializeToNode(heirs.Select(m => new
                { id = m, name = _players.FirstOrDefault(p => p.Profile?.Value == m)?.Name ?? _doc.Members[m].Character.Name }), ArcJson.Options);
        if (!pointable.IsDefaultOrEmpty)
            data["pointAt"] = JsonSerializer.SerializeToNode(pointable.Select(t => new { id = t.Def.Id, label = t.Label }), ArcJson.Options);
        if (VisibleGold(profile) is { } gold) data["gold"] = gold;
        if (!AllThings.IsEmpty || !RegionShops.IsEmpty || !PersonTargets().IsEmpty)
        {
            if (leads && InteractLabel() is null && targets.Length > 0) data["interact"] = targets[0].Label;
            data["targets"] = JsonSerializer.SerializeToNode(targets.Select(t => new { id = t.Def.Id, label = t.Label }), ArcJson.Options);
            data["beat"] = leads && InteractLabel() is not null;     // the button is the beat's, not the first thing's
            data["search"] = leads && !AllThings.IsEmpty;     // the leader's Search (design §7.4): only where there are things, so a walk screen elsewhere is as it was
        }
        // The leader's own bump count, so their phone can buzz and flash the held sector: it goes up only when a wall the TV draws refuses
        // a step (never an unfound secret door), once per press, so the new view it mints comes with the thumb already stopped by the wall.
        if (leads && _bumps > 0) data["bumps"] = _bumps;
        // Tap to travel: the journey under way (only where to, so the screen doesn't change as it walks), why the last pick went nowhere,
        // and the map while it's open on this phone, which does change with every step, as no thumb is on the pad then.
        if (leads && _travel is { } journey) data["travel"] = JsonSerializer.SerializeToNode(new { to = journey.Goal }, ArcJson.Options);
        if (leads && _travelNote is { } note) data["travelNote"] = note;
        if (leads && MapView(profile.Value) is { } map) data["map"] = JsonSerializer.SerializeToNode(map, ArcJson.Options);
        return Custom(Screens.Walk, data);
    }

    private string ClassName(CharacterRecord character) => Rules.Classes.GetValueOrDefault(character.Class)?.Name ?? character.Class;

    /// <summary>The session's players who have a character, in member order, since seats change between nights.</summary>
    private IEnumerable<(Player Player, CharacterRecord Character, bool Here, bool Leads)> Walkers()
        => _doc.Members.SelectMany(m => _players.Where(p => p.Profile?.Value == m.Key)
            .Select(p => (p, m.Value.Character, _online.Contains(p.Id), _lead == m.Key)));

    private ExploreView? ExploreViewOf() => _region is not { } region ? null : new ExploreView(region.Def.Id, region.Def.Name, _epoch, _walkSeq, _at, _facing, _dir is not null && !_blocked, _legs,
        [.. Walkers().Select(w => new WalkerView(w.Player.Profile!.Value.Value, w.Player.Name,
            HostSprite.Of(null, [$"{SheetBuilder.ClassTag}{w.Character.Class}"])!, w.Here, w.Leads, ColorOf(w.Player.Profile!.Value.Value)))],
        Leader?.Name, region.Def.Chapter, _dir is not null && _blocked && !_unseenWall, _bumps, _arrived, Markers(), CastOf(), ThingViews(),
        Things.IsEmpty ? null : _thingEvents, PlugViews(), WorldGold, ShopTargets().IsEmpty ? null :
            [.. RegionShops.Where(s => ShopTargets().Any(t => t.Def.Id == "shop:" + s.Id)).Select(s => new TraderView(s.Id, KeeperTile(s), s.Keeper.Person, OpenShop?.Id == s.Id, s.Keeper.Person is { } person ? "characters." + region.Def.People[person].Preset : null))], Morning: HasCampfires && (_doc.World?.RestSeq ?? 0) > 0, PeopleFlags: VisiblePeopleFlags(), Bark: CurrentBark(), ThingSpeech: _thingSpeech is { } speech && speech.Until > _ctx.Now ? speech : null, QuestEvent: _questEvent, Pinned: QuestTracker(), QuestLog: QuestLogOpen ? Journal(true) : null, QuestMarkers: QuestPersonMarkers(), Mobs: VisibleMobs(), Danger: NearbyMob(), Weather: region.Def.Weather);

    /// <summary>
    /// The phone's battle screen with the player's reaction preferences (issue #113) as <c>reactions</c> ({ opportunity, defence }: the words
    /// the "Reactions" sheet sets), for the sheet to show.
    /// </summary>
    private ControllerView BattleScreen(ProfileId profile)
    {
        var view = BattleScreenOf(profile);
        if (view is not CustomView custom)
            return view;
        var prefs = _doc.For(profile)?.Reactions ?? ReactionPrefs.Ask;
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(custom.Data, ArcJson.Options)!;
        data["reactions"] = JsonSerializer.SerializeToElement(new { opportunity = prefs.OpportunityWord, defence = prefs.DefenceWord }, ArcJson.Options);
        return custom with { Data = JsonSerializer.SerializeToElement(data, ArcJson.Options) };
    }

    private ControllerView BattleScreenOf(ProfileId profile)
    {
        var state = State;
        if (UnitOf(profile) is not { } hero || !state.Units.TryGetValue(hero, out var heroUnit))
            return new WaitView("You're not in this battle.");
        if (state.Pending is ReactionOffer offer && Plays(state, hero, offer.Reactor) && IsPlayed(offer.Reactor))
        {
            // The hero's own reaction, or a summon's: the owner's phone answers for both.
            var unit = offer.Reactor;
            var me = state.Units[unit];
            var prompt = PhoneSurfaces.Attach(Custom(Screens.React, new
            {
                unit,
                trigger = offer.Trigger,
                cause = offer.Cause,
                // Who is doing what to whom, in words, so a 10 s decision doesn't start with working it out.
                actor = offer.Cause is { } c && state.Units.TryGetValue(c.Actor, out var actor) ? actor.Sheet.Name : null,
                targetName = offer.Cause?.Target is { } t && state.Units.TryGetValue(t, out var tu) ? (t == unit ? "you" : tu.Sheet.Name) : null,
                ability = offer.Cause?.Ability is { } ca ? Rules.Abilities.GetValueOrDefault(ca)?.Name : null,
                timeout = ReactionTimeoutWords(state, offer),
                incomingDamage = Ai.PreviewReaction(state, offer, Pass(offer), Rules).IncomingDamage,
                options = offer.Options.Select(o => new
                {
                    id = o.Id,
                    label = ReactionWords.Label(o, Rules),
                    outcome = Ai.PreviewReaction(state, offer, o, Rules).Outcome,
                    cost = ReactionWords.Cost(o, me, Rules),
                    target = o.Target,
                    tooltip = o.Ability is { } ta && Rules.Abilities.TryGetValue(ta, out var def) ? Tooltips.For(def, me.Sheet.Level, Rules, state.Environment) : null,
                }),
            }), state, me, Rules);
            return WithFallBack(prompt, profile, hero, offer: false) with { DeadlineUtc = _ctx.TimerDeadline(Timers.React) };
        }
        // The active unit of this phone's: the hero, or one of its summons (the owner's phone controls them, spec 4.5).
        var turn = TurnUnit(state, hero);
        if (state.Active == turn && state.Pending is null && IsPlayed(turn) && _aiFinishing != turn)
        {
            var actor = state.Units[turn];
            return WithFallBack(PhoneSurfaces.Attach(Custom(Screens.Turn, new
            {
                me = UnitViewOf(actor),
                summoned = actor.SummonedBy is not null,
                map = state.MapId,
                terrain = PhoneTerrain.Of(state, Rules),
                units = state.Units.Values.Where(DownedRules.InBattle).Select(UnitViewOf),
                choices = PhoneChoices.Of(Choices.For(state, turn, Rules), Rules),
            }), state, actor, Rules), profile, hero)
                with { DeadlineUtc = _ctx.TimerDeadline(Timers.Turn) };
        }
        return WithFallBack(PhoneSurfaces.Attach(Custom(Screens.Status, new
        {
            me = UnitViewOf(heroUnit),
            cls = _doc.For(profile)?.Character.Class,
            round = state.Round,
            objective = BattleObjectiveWords.Of(state.Objectives.Current, state.Units.Values.Select(UnitViewOf)),
            party = state.Units.Values.Where(u => u.Sheet.Side == heroUnit.Sheet.Side && u.SummonedBy is null).Select(UnitViewOf),
            acting = state.Active is { } acting ? UnitViewOf(state.Units[acting]) : null,
            abilities = WaitingAbilities(heroUnit),
            recent = RecentBattleEvents(),
            active = state.Active is { } a2 && state.Units.TryGetValue(a2, out var au) ? au.Sheet.Name : null,
            upIn = UpIn(state, hero),
            aiPlaying = !IsPlayed(hero) || _aiFinishing is { } finishing && Plays(State, hero, finishing),
        }), state, heroUnit, Rules), profile, hero);
    }

    /// <summary>How many turns until <paramref name="unit"/>'s, this round (exact: no initiative rolls), or null.</summary>
    private static int? UpIn(BattleState state, UnitId unit)
    {
        var at = state.Order.IndexOf(unit);
        return at > state.TurnIndex ? at - state.TurnIndex : null;
    }

    /// <summary>The campaign portal's path (<c>/c/&lt;code&gt;</c>), or null outside a real campaign.</summary>
    private string? PortalPath() => _lease.Info.Id == "local" ? null : $"/c/{_lease.Info.Code}";

    private static CustomView Custom(string screen, object data)
        => new(Id, screen, JsonSerializer.SerializeToElement(data, ArcJson.Options));

    private IEnumerable<string> Waiting()
        => _players.Where(p => p.Profile is { } pr && _awaited.Contains(pr)).Select(p => p.Name);

    private ImmutableArray<EncounterOption> EncounterOptions()
        => [.. Rules.Encounters.Values.Where(e => !e.StoryOnly).OrderBy(e => e.Level ?? 0).ThenBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new EncounterOption(e.Id, e.Name, e.Level, e.Party.Min, e.Party.Max,
                _players.Count <= e.Party.Max, Math.Max(0, e.Party.Min - _players.Count),
                EncounterOption.GuidanceFor(e.Level, _doc.PartyLevel)))];

    /// <summary>
    /// The one line Results show after a loss, on the phone and the TV alike: the party wakes with that fight's wounds tended, no XP, and the
    /// potions it drank gone. <paramref name="woke"/>: it woke at the region's recovery point (a loss with no world to wake in doesn't).
    /// </summary>
    public static string LossNote(bool woke)
        => (woke ? "You wake at the recovery point. " : "") + "Wounds tended — no XP. Potions used are gone.";

    /// <summary>Each hero's line at Results: the XP the result pays, and the fight's injuries, except after a loss, which tends them.</summary>
    private ImmutableArray<ResultLine> ResultLines()
    {
        if (_result is null || _doc.Battle is not null && _phase != TacticsPhase.Results)
            return [];
        return [.. _resultMembers.Select((profile, i) =>
        {
            var unit = UnitFor(i);
            var member = _doc.For(profile);
            var name = _players.FirstOrDefault(p => p.Profile == profile)?.Name ?? member?.Character.Name ?? unit.Value;
            var totalXp = member?.Xp ?? 0;
            var lvl = member?.Character.Level ?? 1;
            var nextLevelXp = lvl < Rules.Xp.Thresholds.Length ? Rules.Xp.Thresholds[lvl] : (int?)null;
            var prevLevelXp = lvl > 1 && lvl - 1 < Rules.Xp.Thresholds.Length ? Rules.Xp.Thresholds[lvl - 1] : 0;
            return new ResultLine(name, _result.Xp.GetValueOrDefault(unit), _result.Outcome == BattleOutcome.Lost ? [] :
                [.. _result.Injuries.GetValueOrDefault(unit, []).Select(inj => Rules.Injuries.GetValueOrDefault(inj)?.Name ?? inj.Value)],
                TotalXp: totalXp, NextLevelXp: nextLevelXp, PrevLevelXp: prevLevelXp, Level: lvl);
        })];
    }

    private BattleView BattleViewOf()
    {
        var save = Save_;
        var state = save.State;
        return new BattleView(save.Encounter, state.MapId, state.Round, state.Order, state.Active,
            [.. state.Units.Values.Select(u => UnitViewOf(u) with { Sprite = HostSprite.Of(u.Sheet.Sprite, u.Sheet.Tags), Color = HeroColor(save, u.Id), BaseAc = u.Sheet.Ac, Role = HostRole(u) })],
            save.Log.IsDefault ? [] : save.Log, state.Outcome?.ToString(), BoardChanges.Of(state), state.Objectives.Current,
            state.Pending is ReactionOffer offer && IsPlayed(offer.Reactor) ? offer.Reactor : null,
            Rules.Encounters.GetValueOrDefault(save.Encounter)?.Name,
            StageOf(save) is { } stage
                ? new StageView(_region!.Def.Id, stage.Origin, stage.Height, [.. save.Members.Select(m => m.Value)])
                : null,
            state.Difficulty.ToString(), RetreatViewOf());
    }

    /// <summary>
    /// The colour of the member <paramref name="unit"/> is (<c>hero&lt;n&gt;</c> is <see cref="BattleSave.Members"/>' n-th), whoever plays it
    /// now; null for a companion, a monster, or a member with no colour kept.
    /// </summary>
    private string? HeroColor(BattleSave save, UnitId unit)
    {
        for (var i = 0; i < save.Members.Length; i++)
            if (UnitFor(i) == unit)
                return PlayerColors.Parse(_doc.Members.GetValueOrDefault(save.Members[i].Value)?.Color);
        return null;
    }

    private string HostRole(UnitState unit)
    {
        var cls = unit.Sheet.Tags.FirstOrDefault(t => t.StartsWith(SheetBuilder.ClassTag, StringComparison.Ordinal))?[SheetBuilder.ClassTag.Length..];
        return cls is not null ? Rules.Classes.GetValueOrDefault(cls)?.Name ?? cls
            : unit.Sheet.Weapon.Ranged ? "Ranged " + unit.Sheet.Side.ToString().ToLowerInvariant()
            : "Melee " + unit.Sheet.Side.ToString().ToLowerInvariant();
    }

    private UnitView UnitViewOf(UnitState u)
    {
        var player = u.Controller is PlayerControlled { Owner: var owner }
            ? _players.FirstOrDefault(p => p.Profile == owner)?.Name
            : null;
        return new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), u.Sheet.Sprite, u.Pos, u.Hp, u.Sheet.MaxHp, u.TempHp,
            u.Life.ToString(), u.Countdown, [.. u.Conditions.Select(c => c.Id.Value)], player);
    }

    private string Banner()
    {
        switch (_phase)
        {
            case TacticsPhase.Muster:
                return "Choose your classes on your phones";
            case TacticsPhase.EncounterPick:
                return $"{Vip?.Name ?? "The VIP"} is choosing a battle";
            case TacticsPhase.Results:
                return _result?.Outcome == BattleOutcome.Won ? $"Victory at {_resultEncounter}!" : _result?.Outcome == BattleOutcome.Fled ? $"The party fled {_resultEncounter}" : $"Defeat at {_resultEncounter}";
            case TacticsPhase.Resuming:
                return _doc.Battle is null ? "Resuming the story…" : "Resuming the battle…";
            case TacticsPhase.Exploration:
                return Leader is { } leader ? $"{leader.Name} leads the party" : "The party waits for someone to lead it";
            case TacticsPhase.Story:
                return StoryBanner();
        }
        var state = State;
        if (state.Pending is ReactionOffer offer)
            return $"{state.Units[offer.Reactor].Sheet.Name} can react";
        if (state.Active is not { } active)
            return "";
        var unit = state.Units[active];
        return IsPlayed(active) && _aiFinishing != active
            ? $"{_players.FirstOrDefault(p => p.Profile == ((PlayerControlled)unit.Controller).Owner)?.Name ?? unit.Sheet.Name}'s turn"
            : $"{unit.Sheet.Name} is acting";
    }
}
