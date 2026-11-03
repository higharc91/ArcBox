using System.Collections.Immutable;
using Tactics.Presentation.Audio;

namespace Tactics.Presentation.World;

/// <summary>Shop sound keys with existing-pack fallbacks; no generated audio.</summary>
public static class ShopSounds
{
    /// <summary>Best sound first for a shop event.</summary>
    public static ImmutableArray<string> Wanted(string action) => action switch
    {
        "buy" => ["ui_coin_buy", "ui_loot", "ui_confirm"],
        "sell" => ["ui_coin_sell", "ui_loot", "ui_confirm"],
        "open" => ["ui_shop_open", "ui_confirm", "ui_loot"],
        "close" => ["ui_shop_close", "ui_confirm", "ui_loot"],
        _ => [],
    };

    /// <summary>The first key the pack actually has, or silence.</summary>
    public static string? For(string action, SoundBank? bank) => bank is null ? null : Wanted(action).FirstOrDefault(bank.Has);
}
