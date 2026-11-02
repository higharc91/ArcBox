using ArcBox.Host.Audio;
using ArcBox.Host.Core.Audio;
using ArcBox.Host.Shell;
using Godot;
using Tactics.Game;
using Tactics.Presentation.World;
using Tactics.Presentation.Hud;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

public partial class TacticsScene
{
    private PanelContainer? _shopPanel;
    private VBoxContainer? _shopRows;
    private Label? _shopPurse;
    private Label? _goldGain;
    private ShopView? _lastShop;
    private int? _lastPurse;

    private void ShowShop(TacticsHostView view)
    {
        if (_shopPanel is null)
        {
            _shopRows = ArcTheme.VBox(14);
            _shopPanel = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Gold, 26, 24), _shopRows);
            _shopPanel.SetAnchorsPreset(LayoutPreset.CenterRight);
            _shopPanel.OffsetLeft = -660;
            _shopPanel.OffsetRight = -40;
            _shopPanel.OffsetTop = -340;
            _shopPanel.OffsetBottom = 340;
            AddChild(_shopPanel);
            _shopPurse = ArcTheme.Label("", ArcTheme.Bold, 28, ArcTheme.Gold);
            _shopPurse.Position = new Vector2(44, 136);
            AddChild(_shopPurse);
            _goldGain = ArcTheme.Label("", ArcTheme.Bold, 38, ArcTheme.Gold);
            _goldGain.Position = new Vector2(44, 180);
            AddChild(_goldGain);
        }
        _shopPanel.Visible = view.Shop is not null;
        _shopPurse!.Visible = false;
        _shopPurse.Text = view.Explore?.Gold is { } gold ? $"◆ {gold} gold" : "";
        if (_lastPurse is { } before && view.Explore?.Gold is { } after && after > before)
        {
            _goldGain!.Text = $"+{after - before} gold";
            _goldGain.Modulate = Colors.White;
            var fade = CreateTween();
            fade.TweenInterval(1.8);
            fade.TweenProperty(_goldGain, "modulate:a", 0f, .8);
        }
        _lastPurse = view.Explore?.Gold;
        var shop = view.Shop;
        if (shop is not null && (_lastShop?.Id != shop.Id || _lastShop.Seq != shop.Seq))
        {
            foreach (var child in _shopRows!.GetChildren()) { _shopRows.RemoveChild(child); child.QueueFree(); }
            var heading = ArcTheme.VBox(5, ArcTheme.Meta("TRADER · " + shop.Keeper), ArcTheme.Label(shop.Name, ArcTheme.Bold, 38, ArcTheme.Gold));
            if (_unitArt is not null && _content.TryGetValue(BasePack, out var packRoot))
            {
                var portrait = HudArt.Load(packRoot, _unitArt).Portrait(shop.Portrait, UnitTone.Neutral);
                var file = System.IO.Path.Combine(packRoot, portrait.Path);
                if (System.IO.File.Exists(file))
                {
                    var texture = ImageTexture.CreateFromImage(Image.LoadFromFile(file));
                    var source = portrait.Source;
                    var face = new TextureRect { Texture = new AtlasTexture { Atlas = texture, Region = new Rect2(source.X, source.Y, source.Width, source.Height) },
                        CustomMinimumSize = new Vector2(92, 92), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest };
                    _shopRows.AddChild(ArcTheme.HBox(16, face, heading));
                }
                else _shopRows.AddChild(heading);
            }
            else _shopRows.AddChild(heading);
            _shopRows.AddChild(ArcTheme.Label($"◆ {shop.Gold} gold in the party purse", ArcTheme.Bold, 28));
            var greeting = ArcTheme.Label(shop.Greeting, ArcTheme.SemiBold, 23);
            greeting.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _shopRows.AddChild(greeting);
            foreach (var item in shop.Items)
            {
                var row = ArcTheme.Label($"{item.Name}\n{item.Price} gold · {(item.Count == -1 ? "Unlimited" : item.Count == 0 ? "Sold out" : item.Count + " in stock")}", ArcTheme.SemiBold, 26,
                    item.Count == 0 ? new Color(.5f, .5f, .5f) : Colors.White);
                _shopRows.AddChild(row);
            }
            foreach (var line in shop.Feed) _shopRows.AddChild(ArcTheme.Label(line, ArcTheme.SemiBold, 21, ArcTheme.Gold));
            _shopRows.AddChild(ArcTheme.Meta("Buy and sell on your phone · The leader leaves"));
            var action = _lastShop is null ? "open" : shop.Gold < _lastShop.Gold ? "buy" : "sell";
            if (ShopSounds.For(action, _sound) is { } sound) _audio.Play(sound, ThingSounds.Db);
            if (_lastShop?.Id != shop.Id) _narrator?.Say(shop.Greeting, "tactics.narrator", NarrationPriority.Question);
        }
        if (shop is null && _lastShop is not null && ShopSounds.For("close", _sound) is { } close) _audio.Play(close, ThingSounds.Db);
        if (shop is not null && (_lastShop?.Id != shop.Id || _lastShop.Seq != shop.Seq)) _shots?.Capture("shop-" + (shop.Seq == 0 ? "open" : "trade-" + shop.Seq));
        _lastShop = shop;
    }
}
