using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// A campaign member's character, the XP it has earned, whether it has been in a battle (<see cref="HasFought"/>; a lost
/// battle can be worth 0 XP), whether its one free remake after fighting is used (<see cref="Remade"/>) and the player's
/// colour (<see cref="Color"/>) and individual purse (<see cref="Gold"/>).
/// </summary>
/// <param name="Color">
/// The player's colour, "#rrggbb", as picked at character creation or on the sheet (<see cref="PlayerColors"/>), for their TV ring, vote
/// dots and phone. Null when they never picked, and then <see cref="PlayerColors.Default"/> by join order. One more optional field and no
/// version bump: a document from before reads with none, and a null isn't written.
/// </param>
public sealed record MemberRecord(CharacterRecord Character, int Xp,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Fought = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Remade = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Color = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Gold = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool WellFed = false)
{
    /// <summary>True once the character has been in a battle (or earned XP, for documents from before the flag).</summary>
    [JsonIgnore]
    public bool HasFought => Fought || Xp > 0;

    /// <summary>
    /// True while the character may be remade: freely before its first battle, then once more (the user's decision after
    /// the phone playtest: a default character from Muster deserves a second go).
    /// </summary>
    [JsonIgnore]
    public bool CanRemake => !HasFought || !Remade;

    /// <summary>True when remaking now would spend the one free remake.</summary>
    [JsonIgnore]
    public bool RemakeIsTheFreeOne => HasFought && !Remade;
}

/// <summary>
/// One beat of the battle log: the events one applied command produced, numbered so the TV can say which it has shown
/// (adapter spec §4.3). <see cref="Actor"/> is the command's actor, or null for the battle's start.
/// <see cref="ActiveBefore"/> preserves the turn when a command ends the battle without a TurnEnded event;
/// a reaction's actor can be someone else. Absent on older saved beats.
/// </summary>
public sealed record Beat(int Seq, UnitId? Actor, ImmutableArray<BattleEvent> Events,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] UnitId? ActiveBefore = null);

/// <summary>
/// A battle under way, as saved after every command: what it was started from (so a changed rules pack can restart it,
/// spec §7), the state now and the last beats.
/// </summary>
/// <param name="Encounter">The encounter id.</param>
/// <param name="Seed">The seed <see cref="Battle.Start"/> was given.</param>
/// <param name="Members">The members in the battle, by profile, in party order.</param>
/// <param name="State">The battle as it stands.</param>
/// <param name="Log">The most recent beats, oldest first (at most <see cref="TacticsDocument.LogBeats"/>).</param>
/// <param name="Companions">
/// The AI companions who filled the party up to the encounter's minimum, after the members in party order. They belong to
/// this battle only: no XP, no place in the campaign.
/// </param>
/// <param name="Beat">
/// The story beat this fight is, if it's one (overworld 5c): a win completes it. Null for a fight from the battle list, which touches no
/// story, and for every battle saved before 5c. Left out of the JSON when null, and no version bump: a missing member reads as null.
/// </param>
/// <param name="Stage">
/// Where the fight was drawn in the world when it began (the stamp's origin and height), or null on a plain board. A region file isn't in
/// the battle's hash, so a repaint doesn't restart the fight; if the region's stamp has moved since, the fight goes on on a plain board
/// (review). Left out of the JSON when null, and no version bump: a battle saved before it reads with none, and uses the region's stamp.
/// </param>
/// <param name="Plain">
/// True for a fight begun on a plain board (no stamp of its map where it began, or no region), so that it stays plain if a repaint
/// later stamps its map: a null <paramref name="Stage"/> alone can't tell that from a battle saved before stages were kept. Left out of
/// the JSON when false, and no version bump: a battle saved before it reads as not plain, and goes by <paramref name="Stage"/> as before.
/// </param>
public sealed record BattleSave(string Encounter, ulong Seed, ImmutableArray<ProfileId> Members, BattleState State,
    ImmutableArray<Beat> Log,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<CharacterRecord> Companions = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Beat = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SavedStage? Stage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Plain = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Mob = null);

/// <summary>Where a battle was drawn in the world (<see cref="BattleStage"/>), as saved with it: its map's tile (0, 0) and the height added.</summary>
public sealed record SavedStage(Tile Origin, int Height);

/// <summary>
/// A roll made in a scene (overworld 5d §6), kept with the node it leads to so a restart never rolls again: who rolled, for which option
/// and skill, the DC, the die, their bonus and whether it passed.
/// </summary>
public sealed record StoryRoll(string Member, string Option, string Skill, int Dc, int Die, int Bonus, bool Passed);

/// <summary>
/// Where a story beat is (overworld 5d §7), saved on every line so that a restart resumes on the same line. Votes aren't kept: a restart
/// reopens a question still being answered. A question decided for a check is kept (<paramref name="Checking"/>), so a restart goes back
/// to the pick of who rolls.
/// </summary>
/// <param name="Beat">The beat being played.</param>
/// <param name="Stage">Which of the beat's scenes: <see cref="Lead"/> (all a story beat has), <see cref="Fight"/> or <see cref="After"/>.</param>
/// <param name="Scene">The scene, or null with none (a battle beat at its fight).</param>
/// <param name="Node">The node whose line is showing; null in the gap before <paramref name="Scene"/> starts.</param>
/// <param name="Line">The line of <paramref name="Node"/> showing, from 0.</param>
/// <param name="Seq">The number of that line's showing, which only goes up, so the TV can tell a line it hasn't said.</param>
/// <param name="Played">
/// The nodes of the scene played so far, so a restart doesn't offer a question already asked (<see cref="SceneRun.Offered"/>).
/// </param>
/// <param name="Roll">The roll that led to <paramref name="Node"/>, while its result shows; then null.</param>
/// <param name="Checking">
/// The option of <paramref name="Node"/>'s question that won and has a check, while the party picks who rolls for it; then null. One more
/// optional field and no version bump: a document from before reads with none, and a null isn't written.
/// </param>
/// <param name="Thing">
/// The thing being interacted with, when this is an interaction's scene and not a beat's (interactables design §2.7): then
/// <paramref name="Beat"/> is empty, and the scene is built again from the thing as it was when the interaction began. One more optional
/// field and no version bump: a document from before reads with none, and a null isn't written.
/// </param>
public sealed record StorySave(string Beat, string Stage, string? Scene, string? Node, int Line, int Seq,
    ImmutableArray<string> Played = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StoryRoll? Roll = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Checking = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ThingCursor? Thing = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PersonCursor? Person = null)
{
    /// <summary>The scenes before a fight, and all of a story beat's.</summary>
    public const string Lead = "lead";

    /// <summary>A battle beat at its fight.</summary>
    public const string Fight = "fight";

    /// <summary>A battle beat's aftermath.</summary>
    public const string After = "after";

    /// <summary>The nodes played; never a default array.</summary>
    public ImmutableArray<string> Played { get => field.IsDefault ? [] : field; init; } = Played;
}

/// <summary>A named conversation and its starter, entry, underlying story cursor and deferred service.</summary>
public sealed record PersonCursor(string Id, string By, string Entry,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StorySave? Under = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Service = null);

/// <summary>
/// An interaction under way (interactables design §2.7): the thing, its state when the interaction began, which its scene is built from
/// again after a restart so it's node for node the same, and the member who began it, who gets its loot and whatever its trap does.
/// <paramref name="Under"/> is the cursor the interaction set aside, a battle beat waiting at its fight, which it puts back when it ends.
/// <paramref name="Search"/> marks an active Search instead (interactables design §7.4): the ids of the hidden things it looks for, fixed as it
/// began, so a restart plays the same scene and judges the same things; then <paramref name="Id"/> is empty and <paramref name="From"/> unused.
/// <paramref name="SearchDcs"/> are their DCs as it began, by position, which the check routes by and each is judged by.
/// One more optional field and no version bump: a document from before reads with none, and none isn't written.
/// </summary>
public sealed record ThingCursor(string Id, InteractableSave From, string By,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StorySave? Under = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> Search = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<int> SearchDcs = default)
{
    /// <summary>True for an active Search, not an interaction with one thing.</summary>
    [JsonIgnore]
    public bool Searching => !Search.IsDefault;
}

/// <summary>Where the party is in the world, and how far through its story.</summary>
/// <param name="Region">The region's id. A hand-edited document can leave it null, and the party then goes to the start with a notice, keeping its story.</param>
/// <param name="At">The leader's tile. A repaint can make it illegal, and it's checked on the way in.</param>
/// <param name="Facing">The tile the leader faces.</param>
/// <param name="LastLeader">The profile whose session it last was, whoever held the lead at the end, which the next session's leader follows in member order; null before anyone has.</param>
/// <param name="Flags">The story flags set so far.</param>
/// <param name="Beats">The story beats done so far.</param>
/// <param name="Story">
/// The beat under way, or null between beats (overworld 5d). One more optional field and no version bump: a document from before reads
/// with none, and a null isn't written.
/// </param>
/// <param name="Things">
/// How each thing to interact with is, by id, once anything about it has changed (interactables design §2.3); a thing not here is as it
/// started. One more optional field and no version bump: a document from before reads with none, and a null isn't written.
/// </param>
/// <param name="StorySeq">
/// The number of the last scene line shown (<see cref="StoryView.Seq"/>), kept between beats too, so the TV's line number only ever goes
/// up, across restarts and sessions: a TV that stays up while the server restarts must never take a new line for an old one. 0 isn't written.
/// </param>
public sealed record WorldSave(string Region, Tile At, Tile Facing, string? LastLeader, ImmutableArray<string> Flags, ImmutableArray<string> Beats,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StorySave? Story = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int StorySeq = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonConverter(typeof(OrdinalJsonConverter))]
    ImmutableSortedDictionary<string, InteractableSave>? Things = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]     int ThingSeq = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ShopCursor? Shop = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool RestReady = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CampSave? Camp = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int RestSeq = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> CampScenes = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonConverter(typeof(OrdinalJsonConverter))]
    ImmutableSortedDictionary<string, long>? BarkCounters = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> QuestsRewarded = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PinnedQuest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonConverter(typeof(OrdinalJsonConverter))]
    ImmutableSortedDictionary<string, StorySave>? RegionStories = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LootSave? Loot = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> MobsBeaten = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonConverter(typeof(OrdinalJsonConverter))]
    ImmutableSortedDictionary<string, MobActorSave>? MobActors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int MobChapter = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MobAmbush = null)
{
    /// <summary>The story flags set so far; never a default array, so a read or a write can't throw.</summary>
    public ImmutableArray<string> Flags { get => field.IsDefault ? [] : field; init; } = Flags;

    /// <summary>The story beats done so far; never a default array, so a read or a write can't throw.</summary>
    public ImmutableArray<string> Beats { get => field.IsDefault ? [] : field; init; } = Beats;
}

/// <summary>
/// The campaign document, version 1 (adapter spec §6): the party's level, each member's character and XP, the encounters
/// won, the battle under way, if any, and the party's place in the world, or null if it has none yet, which means the start
/// spawn. Members are keyed by profile id. Version 0 is the same without <see cref="World"/>.
/// </summary>
public sealed record TacticsDocument(int Version, string? RulesHash, int PartyLevel,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, MemberRecord> Members,
    ImmutableArray<string> Completed, BattleSave? Battle, WorldSave? World = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Gold = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ShopsSave? Shops = null,
    Difficulty Difficulty = Difficulty.Normal)
{
    /// <summary>The document version this code reads and writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>How many beats a saved battle keeps.</summary>
    public const int LogBeats = 20;

    /// <summary>A brand-new campaign's document: level 1, no characters yet.</summary>
    public static TacticsDocument New { get; } = new(CurrentVersion, null, 1, Ordinal.Map<MemberRecord>(), [], null);

    /// <summary>The member's record, or null when they have no character yet.</summary>
    public MemberRecord? For(ProfileId profile) => Members.GetValueOrDefault(profile.Value);

    /// <summary>Reads a document of any version up to <see cref="CurrentVersion"/>, as that version; an empty or unreadable one is a new campaign's.</summary>
    public static TacticsDocument Read(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.EnumerateObject().Any())
            return New;
        var doc = json.Deserialize<TacticsDocument>(ArcJson.Options)
                  ?? throw new JsonException("The campaign document is null.");
        if (doc.Version is < 0 or > CurrentVersion)
            throw new JsonException($"Campaign document version {doc.Version} isn't supported (this build reads 0 to {CurrentVersion}).");
        doc = doc with
        {
            Version = CurrentVersion,
            Gold = Math.Max(0, doc.Gold),
            Completed = doc.Completed.IsDefault ? [] : doc.Completed,
            Members = Ordinal.Map((doc.Members ?? Ordinal.Map<MemberRecord>()).Where(m => m.Value?.Character is not null)
                .Select(m => (m.Key, m.Value with { Character = Whole(m.Value.Character), Gold = Math.Max(0, m.Value.Gold) }))),
        };
        // Failed lock ways and searches saved before USER DECISION 2 name no rest: they're spent until the next one (issue #83).
        if (doc.World is { Things: { } things } stamped && things.Values.Any(t => Interactables.Stamped(t, stamped.RestSeq) != t))
            doc = doc with { World = stamped with { Things = Ordinal.Map(things.Select(t => (t.Key, Interactables.Stamped(t.Value, stamped.RestSeq)))) } };
        // The first shop build stored a shared purse. Split it once, then omit the legacy field on future writes.
        return doc.Gold > 0 && !doc.Members.IsEmpty
            ? (doc with { Gold = 0 }).ChangeGold(doc.Gold, doc.Members.Keys) : doc;
    }

    /// <summary>
    /// Divides a reward (or a shared story payment) across these members' own purses. Whole-gold remainders go in stable
    /// profile order; capped/full purses or empty purses on payment redistribute their share without losing gold.
    /// </summary>
    public TacticsDocument ChangeGold(int amount, IEnumerable<string> profiles)
    {
        var ids = profiles.Distinct(StringComparer.Ordinal).Where(Members.ContainsKey).Order(StringComparer.Ordinal).ToArray();
        if (amount == 0 || ids.Length == 0) return this;
        var members = Members;
        var remaining = Math.Abs((long)amount);
        long Capacity(string id) => amount > 0 ? int.MaxValue - (long)members[id].Gold : members[id].Gold;
        if (ids.Sum(Capacity) < remaining) throw new RulesInvariantException("The participating purses cannot cover this gold change.");
        while (remaining > 0)
        {
            var active = ids.Where(id => Capacity(id) > 0).ToArray();
            var share = remaining / active.Length;
            var extra = remaining % active.Length;
            for (var i = 0; i < active.Length; i++)
            {
                var id = active[i];
                var part = (int)Math.Min(Capacity(id), share + (i < extra ? 1 : 0));
                members = members.SetItem(id, members[id] with { Gold = ShopRules.Gold(members[id].Gold, amount > 0 ? part : -part) });
                remaining -= part;
            }
        }
        return this with { Members = members };
    }

    /// <summary>
    /// A stored character with every collection present (a document written by hand, or by an older build, may leave
    /// fields out, which would read as null), so rules and views never meet a null.
    /// </summary>
    private static CharacterRecord Whole(CharacterRecord c) => c with
    {
        Name = c.Name ?? "Adventurer", Race = c.Race ?? "", Class = c.Class ?? "", Background = c.Background ?? "",
        BaseScores = c.BaseScores ?? ImmutableSortedDictionary<Stat, int>.Empty,
        Increases = c.Increases ?? ImmutableSortedDictionary<Stat, int>.Empty,
        Picks = c.Picks.IsDefault ? [] : c.Picks, Injuries = c.Injuries.IsDefault ? [] : c.Injuries, Bag = c.Bag.IsDefault ? [] : c.Bag,
        Weapon = c.Weapon ?? "", Armor = c.Armor ?? "",
    };

    /// <summary>The document as JSON, for <see cref="ArcBox.Sdk.Campaigns.ICampaignLease.Save"/>.</summary>
    public JsonElement ToJson() => JsonSerializer.SerializeToElement(this, ArcJson.Options);

    /// <summary>
    /// The party's level from its members' XP: the level every member with a character has reached, so the party never
    /// splits across levels (adapter spec §6, decision 3). At least 1, at most 10.
    /// </summary>
    public static int LevelFor(IEnumerable<int> xp, XpTable table)
    {
        var lowest = xp.DefaultIfEmpty(0).Min();
        return Math.Clamp(table.Thresholds.Count(t => t <= lowest), 1, 10);
    }
}

/// <summary>A roaming actor cursor, persisted so restart never chooses another random step.</summary>
public sealed record MobActorSave(Tile At, long Step, int Seed);
