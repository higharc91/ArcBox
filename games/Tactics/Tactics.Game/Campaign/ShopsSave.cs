using System.Collections.Immutable;

namespace Tactics.Game;

/// <summary>One shop's retained stock and the last 32 accepted trade ids.</summary>
public sealed record ShopSave(ImmutableSortedDictionary<string, int> Stock, int Chapter, int Seq,
    ImmutableArray<string> Trades, ImmutableSortedDictionary<string, int>? Initial = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int Rest = 0);

/// <summary>Campaign-wide shop state, recorded once a trader has been visited.</summary>
public sealed record ShopsSave(ImmutableSortedDictionary<string, ShopSave> Shops, int Chapter = 0);

/// <summary>A restart-safe shop visit, alongside the story cursor it temporarily pauses.</summary>
public sealed record ShopCursor(string ShopId, string OpenedBy, int Seq);
