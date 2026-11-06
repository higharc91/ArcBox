using System.Collections.Immutable;

namespace Tactics.Game;

/// <summary>One shop's retained stock and the last 32 accepted trade ids.</summary>
public sealed record ShopSave(ImmutableSortedDictionary<string, int> Stock, int Chapter, int Seq,
    ImmutableArray<string> Trades, ImmutableSortedDictionary<string, int>? Initial = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int Rest = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? FailedHaggleRest = null);

/// <summary>Campaign-wide shop state, recorded once a trader has been visited.</summary>
public sealed record ShopsSave(ImmutableSortedDictionary<string, ShopSave> Shops, int Chapter = 0);

/// <summary>
/// A restart-safe shop visit, alongside the story cursor it temporarily pauses. <paramref name="Haggle"/> is this visit's haggle, once rolled
/// (USER DECISION E, decided 2026-11-03): one a visit, and its prices hold for the rest of it. <paramref name="Haggling"/> is true while its
/// check plays as the story's scene, and <paramref name="Under"/> is the story cursor that scene set aside. Optional, and none isn't written.
/// </summary>
public sealed record ShopCursor(string ShopId, string OpenedBy, int Seq,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] HaggleSave? Haggle = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Haggling = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StorySave? Under = null);

/// <summary>A visit's haggle, as rolled: who (and their character's name), the die, their Persuasion bonus, the DC, and whether it passed.</summary>
public sealed record HaggleSave(string Member, string Name, int Die, int Bonus, int Dc, bool Passed);
