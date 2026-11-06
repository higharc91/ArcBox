using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>A player's saved camp job and exact injury target.</summary>
public sealed record CampJob(string Job, string? Target = null, string? Injury = null);

/// <summary>A roll and its already-applied result. Replaying this line never applies its effect again.</summary>
public sealed record CampResult(string Member, string Job, string Skill, int Die, int? SecondDie, int Bonus, int Dc, bool Passed, string Text,
    string? Target = null, string? Injury = null);

/// <summary>A rest visit beside the normal story cursor; decisions and effects persist, presentation deadlines can hold for presence.</summary>
public sealed record CampSave(string Mode, string Source, Tile At, string Stage,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, CampJob> Jobs,
    ImmutableArray<string> Actors, ImmutableArray<CampResult> Results, ImmutableArray<string> Rooms,
    StorySave? Under = null, DateTimeOffset? Due = null, string? Talk = null, string? Watcher = null);
