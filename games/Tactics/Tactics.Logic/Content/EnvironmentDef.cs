using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>Opt-in environmental reactions. Materials, durations and damage stay in rules/environment.json.</summary>
public sealed record EnvironmentDef(ImmutableArray<string> Fuel,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, SurfaceDef> Surfaces,
    ImmutableArray<EnvironmentReactionDef> Reactions, ImmutableArray<string> ZoneFuel = default, string? Replaces = null, ImmutableArray<string> MaterialFamilies = default)
{
    public const string Id = "environment";
    public static EnvironmentDef None { get; } = new([], Ordinal.Map<SurfaceDef>(), [], []);
}

/// <summary>A lasting surface's duration, material traits and hooks.</summary>
public sealed record SurfaceDef(int? Rounds = null, bool Spreads = false, string? Leaves = null, bool Difficult = false,
    bool Insulates = false, bool Fuel = false, ImmutableArray<PassiveDef> Passives = default);

/// <summary>One ordered reaction to a damage type or to entering a material.</summary>
public sealed record EnvironmentReactionDef(string Id, string? Damage = null, string? Tiles = null,
    ConductDef? Conduct = null, string? Surface = null, int? Rounds = null, bool Consumes = false,
    bool Ends = false, string? Enters = null, string? Remove = null, ImmutableArray<PassiveDef> Passives = default);

/// <summary>Water conduction's bounded geometry and save.</summary>
public sealed record ConductDef(int Reach, Stat Save, bool Half);
