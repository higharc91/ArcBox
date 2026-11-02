using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.World;

/// <summary>Deterministic stand-in art review: a trader marker, shop, purchase, sale and departure.</summary>
public static class ShopDemo
{
    /// <summary>Uses the existing things-demo courtyard, so no new art or content pack is needed.</summary>
    public static IReadOnlyList<ThingsDemoStep> Steps()
    {
        var basis = ThingsDemo.Steps()[0].View;
        var explore = basis.Explore!;
        var trader = new TraderView("demo", new Tile(explore.At.X + 1, explore.At.Y), null, false, "characters.merchant");
        explore = explore with { Name = "Market courtyard", Things = [], ThingEvents = [], Markers = [], Traders = [trader], Gold = 100 };
        basis = basis with { Explore = explore, Notice = null };
        var shop = new ShopView("demo", "Wenna's Remedies", "Wenna", "Potions, salves. Coin first.", 100, 0,
            [new ShopItemView("potion_of_healing", "Potion of Healing", 50, 3), new ShopItemView("smoke_bomb", "Smoke Bomb", 25, 2)], [], "characters.merchant");
        var open = basis with { Phase = nameof(TacticsPhase.Story), Shop = shop, Explore = explore with { Traders = [trader with { Open = true }] } };
        var bought = open with { Shop = shop with { Gold = 50, Seq = 1, Items = shop.Items.SetItem(0, shop.Items[0] with { Count = 2 }), Feed = ["Ana bought a Potion of Healing — 50 gold"] }, Explore = open.Explore! with { Gold = 50 } };
        var sold = bought with { Shop = bought.Shop! with { Gold = 75, Seq = 2, Feed = ["Bo sold a Potion of Healing — 25 gold"] }, Explore = bought.Explore! with { Gold = 75 } };
        return [new("shop-marker", 0, basis, [(5, "shop-marker")], false),
            new("shop-open", 7, open, [(1, "shop-open")], false),
            new("shop-buy", 10, bought, [(1, "shop-buy")], false),
            new("shop-sell", 13, sold, [(.5, "shop-gold-pop")], false),
            new("shop-close", 16, basis with { Explore = explore with { Gold = 75 }, Notice = "Mind the ford." }, [(1, "shop-close")], false)];
    }
}
