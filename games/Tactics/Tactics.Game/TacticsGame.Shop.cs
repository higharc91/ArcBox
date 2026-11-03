using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>Shop state is a saved Story sub-state. Room input serialization makes each trade one atomic document save.</summary>
public sealed partial class TacticsGame
{
    private ImmutableArray<ShopDef> RegionShops => _region is null ? [] : Rules.ShopsIn(_region.Def.Id);
    private ShopDef? OpenShop => _doc.World?.Shop is { } visit ? RegionShops.FirstOrDefault(s => s.Id == visit.ShopId && ShopRules.Present(s, StoryFlags())) : null;
    private int? VisibleGold(ProfileId profile) => _doc.For(profile) is { } member
        && (member.Gold > 0 || !RegionShops.IsEmpty || _doc.Shops is not null) ? member.Gold : null;
    private long TotalGold => _doc.Members.Values.Sum(m => (long)m.Gold);
    private long? WorldGold => TotalGold > 0 || _doc.Shops is not null ? TotalGold : null;
    private IEnumerable<string> GoldParticipants => Party().Where(m => m.Here).Select(m => m.Member);
    private long StoryGold => GoldParticipants.Sum(id => (long)_doc.Members[id].Gold);
    private bool CanPayGold(int change) => change >= 0 || StoryGold + change >= 0;
    private void ChangePartyGold(int change) => _doc = _doc.ChangeGold(change, GoldParticipants);
    private readonly List<string> _tradeFeed = [];

    private Tile KeeperTile(ShopDef shop) => shop.Keeper.Person is { } person
        ? _region!.Def.People[person].At : ThingNamed(shop.Keeper.Thing!)!.At;

    private ImmutableArray<(InteractableDef Def, string Label)> ShopTargets()
        => _region is null ? [] : [.. RegionShops.Where(s => ShopRules.Present(s, StoryFlags()))
            .Select(s => (Def: new InteractableDef("shop:" + s.Id, "stall", KeeperTile(s)), Label: "Trade with " + s.Name))
            .Where(t => Interactables.InReach(WalkGround(), _at, t.Def))];

    private ImmutableArray<(InteractableDef Def, string Label)> TradeTargets()
        => [.. ThingTargets().Concat(ShopTargets()).Concat(PersonTargets())
            .OrderBy(t => Math.Abs(t.Def.At.X - _at.X) + Math.Abs(t.Def.At.Y - _at.Y))
            .ThenBy(t => t.Def.At == _facing ? 0 : 1).ThenBy(t => t.Def.Id, StringComparer.Ordinal)];

    private void StartShop(ShopDef shop)
    {
        LeaveExploration();
        _phase = TacticsPhase.Story;
        _tradeFeed.Clear();
        _toasts.Clear();
        _doc = _doc with
        {
            World = _doc.World! with { Shop = new ShopCursor(shop.Id, _lead!, 0) },
            Shops = _doc.Shops ?? new ShopsSave(Ordinal.Map<ShopSave>(), _region!.Def.Chapter),
        };
        Save();
        Say(shop.Greeting);
    }

    private void CloseShop()
    {
        var farewell = OpenShop?.Farewell;
        _doc = _doc with { World = _doc.World! with { Shop = null } };
        Save();
        ToExploration();
        if (farewell is not null) Say(farewell);
    }

    private void ResumeShop()
    {
        if (OpenShop is null) { CloseShop(); return; }
        _phase = TacticsPhase.Story;
    }

    private ShopSave StockOf(ShopDef shop)
    {
        var saved = _doc.Shops?.Shops.GetValueOrDefault(shop.Id);
        var chapter = Math.Max(_region!.Def.Chapter, _doc.Shops?.Chapter ?? 0);
        var stock = Ordinal.Map<int>();
        foreach (var line in shop.Stock)
        {
            var count = line.Count;
            if (count >= 0 && saved?.Stock.TryGetValue(line.Item.Value, out var kept) == true
                && !(line.Restock == "chapter" && chapter > saved.Chapter) && !(line.Restock == "rest" && (_doc.World?.RestSeq ?? 0) > saved.Rest)) count = saved.Initial?.GetValueOrDefault(line.Item.Value, int.MinValue) == line.Count ? Math.Max(0, kept) : Math.Clamp(kept, 0, count);
            stock = stock.SetItem(line.Item.Value, count);
        }
        return new ShopSave(stock, chapter, saved?.Seq ?? 0, saved is null || saved.Trades.IsDefault ? [] : saved.Trades,
            Ordinal.Map(shop.Stock.Select(s => (s.Item.Value, s.Count))), _doc.World?.RestSeq ?? 0);
    }

    private void ShopInput(Player player, ProfileId profile, CustomInput input)
    {
        if (OpenShop is not { } shop || Str(input.Data, "shop") != shop.Id || !_online.Contains(player.Id)) return;
        if (input.Action == "restPropose") { if (profile.Value == _lead && shop.Services.Any(s => s.Kind == "room") && Camp is null) ProposeRest("inn", shop.Id, KeeperTile(shop)); return; }
        if (input.Action == "shopService") { ShopService(player, profile, input); return; }
        if (input.Action == "shopLeave") { if (profile.Value == _lead && Camp is null) CloseShop(); return; }
        if (input.Action is not ("shopBuy" or "shopSell")) return;
        var trade = Str(input.Data, "trade");
        if (string.IsNullOrWhiteSpace(trade) || trade.Length > 128) { Toast(player.Id, "A trade id is required."); return; }
        var stock = StockOf(shop);
        if (stock.Trades.Contains(trade)) return;
        if (!_doc.Members.TryGetValue(profile.Value, out var member)) { Toast(player.Id, "Choose a character first."); return; }
        var itemId = Str(input.Data, "item");
        if (itemId is null || !Rules.Items.TryGetValue(new ItemId(itemId), out var item) || item.Price is null) { Toast(player.Id, "That item cannot be traded."); return; }
        if (input.Data is not { ValueKind: JsonValueKind.Object } data || !data.TryGetProperty("count", out var countJson)
            || countJson.ValueKind != JsonValueKind.Number || !countJson.TryGetInt32(out var count) || count <= 0) { Toast(player.Id, "Choose a positive quantity."); return; }
        var buy = input.Action == "shopBuy";
        var bag = member.Character.Bag;
        var stack = bag.FirstOrDefault(s => s.Item == item.Id);
        var available = stock.Stock.GetValueOrDefault(itemId);
        if (buy && (!stock.Stock.ContainsKey(itemId) || available != -1 && available < count)) { Toast(player.Id, "Sold out"); return; }
        if (!buy && (Str(input.Data, "member") != profile.Value || stack is null || stack.Count < count)) { Toast(player.Id, "You may only sell items in your own bag."); return; }
        if (!buy && !ShopRules.Accepts(item, shop)) { Toast(player.Id, "The keeper does not buy that item."); return; }
        var total = (long)(buy ? ShopRules.Buy(item, shop) : ShopRules.Sell(item, shop)) * count;
        if (total > int.MaxValue || !buy && (long)member.Gold + total > int.MaxValue) { Toast(player.Id, "That trade exceeds the gold limit."); return; }
        if (buy && member.Gold < total) { Toast(player.Id, "Not enough gold"); return; }
        var newCount = (long)(stack?.Count ?? 0) + (buy ? count : -count);
        if (newCount > int.MaxValue) { Toast(player.Id, "That stack is too large."); return; }
        if (stack is not null) bag = bag.Remove(stack);
        if (newCount > 0) bag = bag.Add(new ItemStack(item.Id, (int)newCount));
        if (stock.Stock.ContainsKey(itemId) && available != -1)
            stock = stock with { Stock = stock.Stock.SetItem(itemId, buy ? available - count : (int)Math.Min(int.MaxValue, (long)available + count)) };
        stock = stock with { Seq = checked(stock.Seq + 1), Trades = [.. stock.Trades.TakeLast(31), trade] };
        var shops = (_doc.Shops?.Shops ?? Ordinal.Map<ShopSave>()).SetItem(shop.Id, stock);
        _doc = _doc with
        {
            Members = _doc.Members.SetItem(profile.Value, member with { Gold = ShopRules.Gold(member.Gold, buy ? -(int)total : (int)total), Character = member.Character with { Bag = bag } }),
            Shops = new ShopsSave(shops, stock.Chapter),
            World = _doc.World! with { Shop = _doc.World.Shop! with { Seq = stock.Seq } },
        };
        Save();
        _tradeFeed.Add($"{member.Character.Name} {(buy ? "bought" : "sold")} {count} × {item.Name} — {total} gold");
        if (_tradeFeed.Count > 4) _tradeFeed.RemoveAt(0);
    }

    private CustomView ShopScreen(Player player, ProfileId profile)
    {
        var shop = OpenShop!;
        var stock = StockOf(shop);
        var member = _doc.For(profile);
        return WithServices(Custom("shop", new
        {
            shop = new { id = shop.Id, name = shop.Name, keeper = shop.Name, greeting = shop.Greeting },
            gold = member?.Gold ?? 0,
            items = shop.Stock.Select(s => new { item = s.Item.Value, name = Rules.Items[s.Item].Name, price = ShopRules.Buy(Rules.Items[s.Item], shop),
                count = stock.Stock[s.Item.Value], can = member is not null && _online.Contains(player.Id) && stock.Stock[s.Item.Value] != 0 && member.Gold >= ShopRules.Buy(Rules.Items[s.Item], shop),
                detail = Tooltips.For(Rules.Abilities[Rules.Items[s.Item].Use], _doc.PartyLevel, Rules) }),
            sell = member is null ? [] : member.Character.Bag.Where(s => ShopRules.Accepts(Rules.Items[s.Item], shop))
                .Select(s => new { member = profile.Value, item = s.Item.Value, name = Rules.Items[s.Item].Name, price = ShopRules.Sell(Rules.Items[s.Item], shop), count = s.Count }).ToArray(),
            you = new { member = member is null ? null : profile.Value, canBuy = member is not null && _online.Contains(player.Id) },
            leader = profile.Value == _lead,
            vip = IsVip(player),
            feed = _tradeFeed.ToArray(),
            portal = PortalPath(),
        }), profile);
    }

    private ShopView? ShopViewOf() => CampVote || CampTalk || OpenShop is not { } shop ? null : new ShopView(shop.Id, shop.Name, shop.Name,
        shop.Greeting, TotalGold, StockOf(shop).Seq,
        [.. shop.Stock.Select(s => new ShopItemView(s.Item.Value, Rules.Items[s.Item].Name, ShopRules.Buy(Rules.Items[s.Item], shop), StockOf(shop).Stock[s.Item.Value]))],
        [.. _tradeFeed], shop.Keeper.Person is { } person ? "characters." + _region!.Def.People[person].Preset : null,
        [.. Party().Select(m => new PurseView(m.Member, m.Character.Name, _doc.Members[m.Member].Gold))],
        shop.Services.IsEmpty ? null : shop.Services,
        shop.Services.IsEmpty ? null : [.. _doc.Members.Select(m => new ServiceGuestView(m.Key, m.Value.Character.Name,
            [.. m.Value.Character.Injuries.Select(i => Rules.Injuries.GetValueOrDefault(i)?.Name ?? i.Value)], Camp?.Rooms.Contains(m.Key) == true))], shop.Services.IsEmpty ? null : RecoveryMembers());
}
