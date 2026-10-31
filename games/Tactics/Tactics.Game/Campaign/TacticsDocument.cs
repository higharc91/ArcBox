using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// A campaign member's character, the XP it has earned and whether it has been in a battle (adapter spec §6). A character
/// that has fought can't be remade (<see cref="HasFought"/>; a lost battle can be worth 0 XP).
/// </summary>
public sealed record MemberRecord(CharacterRecord Character, int Xp,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Fought = false)
{
    /// <summary>True once the character has been in a battle (or earned XP, for documents from before the flag).</summary>
    [JsonIgnore]
    public bool HasFought => Fought || Xp > 0;
}

/// <summary>
/// One beat of the battle log: the events one applied command produced, numbered so the TV can say which it has shown
/// (adapter spec §4.3). <see cref="Actor"/> is the command's actor, or null for the battle's start.
/// </summary>
public sealed record Beat(int Seq, UnitId? Actor, ImmutableArray<BattleEvent> Events);

/// <summary>
/// A battle under way, as saved after every command: what it was started from (so a changed rules pack can restart it,
/// spec §7), the state now and the last beats.
/// </summary>
/// <param name="Encounter">The encounter id.</param>
/// <param name="Seed">The seed <see cref="Battle.Start"/> was given.</param>
/// <param name="Members">The members in the battle, by profile, in party order.</param>
/// <param name="State">The battle as it stands.</param>
/// <param name="Log">The most recent beats, oldest first (at most <see cref="TacticsDocument.LogBeats"/>).</param>
public sealed record BattleSave(string Encounter, ulong Seed, ImmutableArray<ProfileId> Members, BattleState State,
    ImmutableArray<Beat> Log);

/// <summary>
/// The campaign document, version 0 (adapter spec §6): the party's level, each member's character and XP, the encounters
/// won, and the battle under way, if any. Members are keyed by profile id.
/// </summary>
public sealed record TacticsDocument(int Version, string? RulesHash, int PartyLevel,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, MemberRecord> Members,
    ImmutableArray<string> Completed, BattleSave? Battle)
{
    /// <summary>The document version this code reads and writes.</summary>
    public const int CurrentVersion = 0;

    /// <summary>How many beats a saved battle keeps.</summary>
    public const int LogBeats = 20;

    /// <summary>A brand-new campaign's document: level 1, no characters yet.</summary>
    public static TacticsDocument New { get; } = new(CurrentVersion, null, 1, Ordinal.Map<MemberRecord>(), [], null);

    /// <summary>The member's record, or null when they have no character yet.</summary>
    public MemberRecord? For(ProfileId profile) => Members.GetValueOrDefault(profile.Value);

    /// <summary>Reads a document; an empty or unreadable one is a new campaign's.</summary>
    public static TacticsDocument Read(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.EnumerateObject().Any())
            return New;
        var doc = json.Deserialize<TacticsDocument>(ArcJson.Options)
                  ?? throw new JsonException("The campaign document is null.");
        if (doc.Version != CurrentVersion)
            throw new JsonException($"Campaign document version {doc.Version} isn't supported (this build reads {CurrentVersion}).");
        return doc with
        {
            Completed = doc.Completed.IsDefault ? [] : doc.Completed,
            Members = Ordinal.Map((doc.Members ?? Ordinal.Map<MemberRecord>()).Where(m => m.Value?.Character is not null)
                .Select(m => (m.Key, m.Value with { Character = Whole(m.Value.Character) }))),
        };
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
