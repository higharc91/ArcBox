using Tactics.Presentation.Audio;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

public sealed class ShopShowTests
{
    [Theory]
    [InlineData("buy", "ui_coin_buy")]
    [InlineData("sell", "ui_coin_sell")]
    [InlineData("open", "ui_shop_open")]
    [InlineData("close", "ui_shop_close")]
    public void Shop_events_use_their_key_or_an_existing_pack_fallback(string action, string key)
    {
        Assert.Equal(key, ShopSounds.Wanted(action)[0]);
        var bank = SoundBank.Load(Pack.Root);
        var sound = ShopSounds.For(action, bank);
        Assert.NotNull(sound);
        Assert.True(bank.Has(sound));
        Assert.Null(ShopSounds.For(action, null));
    }

    [Fact]
    public void Shop_demo_uses_separate_stock_views_and_shows_buy_sell_and_departure()
    {
        var steps = ShopDemo.Steps();
        Assert.Null(steps[0].View.Shop);
        Assert.NotEmpty(steps[0].View.Explore!.Traders!.Value);
        Assert.Equal([100, 50, 75], steps.Where(s => s.View.Shop is not null).Select(s => s.View.Shop!.Gold));
        Assert.Equal([0, 1, 2], steps.Where(s => s.View.Shop is not null).Select(s => s.View.Shop!.Seq));
        Assert.Null(steps[^1].View.Shop);
        Assert.Equal(75, steps[^1].View.Explore!.Gold);
        Assert.Contains(steps.SelectMany(s => s.Shots), s => s.Name == "shop-marker");
        Assert.Contains(steps.SelectMany(s => s.Shots), s => s.Name == "shop-gold-pop");
    }
}
