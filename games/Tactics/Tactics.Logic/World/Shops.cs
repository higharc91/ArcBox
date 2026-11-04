using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>A region's shops and its content-defined sale ratio.</summary>
public sealed record ShopsFile(string Region, ImmutableArray<ShopDef> Shops, decimal SellRatio = 0.5m);

/// <summary>Exactly one keeper: a person index or a stall id.</summary>
public sealed record ShopKeeper(int? Person = null, string? Thing = null);

/// <summary>A stock line; -1 means infinite, written as the string "infinite".</summary>
public sealed record ShopStock(ItemId Item,
    [property: JsonConverter(typeof(StockCountConverter))] int Count, string Restock = "chapter");

/// <summary>A trader's stock, accepted tags, prices and presence conditions.</summary>
public sealed record ShopDef(string Id, string Name, ShopKeeper Keeper, ImmutableArray<ShopStock> Stock,
    ImmutableArray<string> Buys, string Greeting = "Welcome.", string Farewell = "Safe travels.", decimal PriceMod = 1m,
    ImmutableArray<string> Requires = default, ImmutableArray<string> Unless = default, int? Haggle = null,
    decimal SellRatio = 0.5m, ImmutableArray<ShopService> Services = default)
{
    /// <summary>Paid rooms and cures; never a default array.</summary>
    public ImmutableArray<ShopService> Services { get => field.IsDefault ? [] : field; init; } = Services;
}

/// <summary>A paid recovery service, charged to the authenticated buyer.</summary>
public sealed record ShopService(string Id, string Name, string Kind, int Price);

/// <summary>Reads finite stock or the explicit infinite sentinel.</summary>
public sealed class StockCountConverter : JsonConverter<int>
{
    /// <inheritdoc />
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String && reader.GetString() == "infinite" ? -1
            : reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n) && n >= 0 ? n
            : throw new JsonException("Stock count must be a non-negative integer or \"infinite\".");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        if (value == -1) writer.WriteStringValue("infinite");
        else writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Pure pricing and presence rules. Haggling is on, once per visit (shops design §3.3, USER DECISION E, decided by the owner on 2026-11-03):
/// a pass takes <see cref="HaggleBuy"/> off what the party buys and adds <see cref="HaggleSell"/> to the ratio it sells at, for that visit.
/// </summary>
public static class ShopRules
{
    /// <summary>The Persuasion DC of a shop whose content doesn't set <c>haggle</c>.</summary>
    public const int DefaultHaggleDc = 15;

    /// <summary>What a haggle that passed leaves of a buy price: a tenth off.</summary>
    public const decimal HaggleBuy = 0.9m;

    /// <summary>What a haggle that passed makes of the sell ratio: a tenth more.</summary>
    public const decimal HaggleSell = 1.1m;

    /// <summary>The purchase price, rounded up to whole gold: a tenth less after a haggle that passed.</summary>
    public static int Buy(ItemDef item, ShopDef shop, bool haggled = false)
        => checked((int)decimal.Ceiling((item.Price ?? throw new RulesInvariantException("An unpriced item cannot be traded.")) * shop.PriceMod
            * (haggled ? HaggleBuy : 1m)));

    /// <summary>The sale price, rounded down to whole gold: the shop's ratio of its plain price, and a tenth more after a haggle that passed.</summary>
    public static int Sell(ItemDef item, ShopDef shop, bool haggled = false)
        => checked((int)decimal.Floor(Buy(item, shop) * shop.SellRatio * (haggled ? HaggleSell : 1m)));

    /// <summary>Whether the shop exists with these flags.</summary>
    public static bool Present(ShopDef shop, IReadOnlySet<string> flags)
        => shop.Requires.All(flags.Contains) && !shop.Unless.Any(flags.Contains);

    /// <summary>Whether the keeper accepts this priced consumable.</summary>
    public static bool Accepts(ItemDef item, ShopDef shop) => item.Price is not null && item.Tags.Any(shop.Buys.Contains);

    /// <summary>A checked individual-purse change; never negative or wrapped.</summary>
    public static int Gold(int before, int change)
    {
        var after = (long)before + change;
        if (before < 0 || after < 0 || after > int.MaxValue) throw new RulesInvariantException("Gold must stay between zero and Int32.MaxValue.");
        return (int)after;
    }
}
