using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>Hostile roaming groups in one region; authored separately from exported terrain.</summary>
public sealed record MobsFile(string Region, ImmutableArray<MobDef> Mobs);

/// <summary>An avoidable group constrained to the tiles of its stamped battle arena.</summary>
public sealed record MobDef(string Id, string Encounter, string Arena, ImmutableArray<Tile> Patch,
    ImmutableArray<string> Show, double Pace = 2,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> Requires = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> Unless = default, int Xp = 75, int Gold = 10);
