using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    private void ShopService(Player player, ProfileId profile, CustomInput input)
    {
        var shop = OpenShop!;
        var service = shop.Services.FirstOrDefault(s => s.Id == Str(input.Data, "service"));
        if (service is null || !_doc.Members.TryGetValue(profile.Value, out var buyer)) return;
        var trade = Str(input.Data, "trade");
        if (string.IsNullOrWhiteSpace(trade) || trade.Length > 128) { Toast(player.Id, "A trade id is required."); return; }
        var stock = StockOf(shop);
        if (stock.Trades.Contains(trade)) return;
        var target = Str(input.Data, "member");
        if (target is null || !_doc.Members.TryGetValue(target, out var patient)) return;
        var price = ShopRules.Service(service, Haggled);
        if (buyer.Gold < price) { Toast(player.Id, "Not enough gold"); return; }
        if (service.Kind == "room")
        {
            if (Camp is not { Mode: "inn", Stage: "rooms" } || Camp.Source != shop.Id) { Toast(player.Id, "The party must agree to take rooms first."); return; }
            if (Camp.Rooms.Contains(target)) { Toast(player.Id, "That character already has a room."); return; }
        }
        else
        {
            if (Str(input.Data, "injury") is not { } injury || !patient.Character.Injuries.Contains(new InjuryId(injury)))
            { Toast(player.Id, "Choose an injury that still needs healing."); return; }
            _doc = _doc with { Members = _doc.Members.SetItem(target, patient with { Character = patient.Character with { Injuries = patient.Character.Injuries.Remove(new InjuryId(injury)) } }) };
        }
        // Reread the buyer: they may also be the patient. Preserve the just-applied injury change.
        buyer = _doc.Members[profile.Value];
        stock = stock with { Seq = checked(stock.Seq + 1), Trades = [.. stock.Trades.TakeLast(31), trade] };
        _doc = _doc with
        {
            Members = _doc.Members.SetItem(profile.Value, buyer with { Gold = ShopRules.Gold(buyer.Gold, -price) }),
            Shops = new ShopsSave((_doc.Shops?.Shops ?? Ordinal.Map<ShopSave>()).SetItem(shop.Id, stock), stock.Chapter),
            World = _doc.World! with
            {
                Shop = _doc.World.Shop! with { Seq = stock.Seq },
                Camp = service.Kind == "room" ? Camp! with { Rooms = Camp.Rooms.Add(target) } : Camp,
            },
        };
        Save();
        _tradeFeed.Add($"{buyer.Character.Name} paid for {patient.Character.Name}: {service.Name} — {price} gold");
        if (_tradeFeed.Count > 4) _tradeFeed.RemoveAt(0);
    }

    private CustomView WithServices(CustomView view, ProfileId profile)
    {
        var shop = OpenShop!;
        if (shop.Services.IsEmpty) return view;
        var data = JsonNode.Parse(view.Data.GetRawText())!.AsObject();
        data["services"] = JsonSerializer.SerializeToNode(shop.Services.Select(s => new { id = s.Id, name = s.Name, kind = s.Kind, price = ShopRules.Service(s, Haggled) }), ArcJson.Options);
        data["guests"] = JsonSerializer.SerializeToNode(_doc.Members.Select(m => new { member = m.Key, name = m.Value.Character.Name,
            room = Camp?.Rooms.Contains(m.Key) == true, wellFed = m.Value.WellFed,
            injuries = m.Value.Character.Injuries.Select(i => Rules.Injuries.GetValueOrDefault(i)).OfType<InjuryDef>()
                .Select(i => new { id = i.Id.Value, name = i.Name, kind = i.Mends, detail = InjuryDetail(i) }) }), ArcJson.Options);
        data["members"] = JsonSerializer.SerializeToNode(RecoveryMembers(), ArcJson.Options);
        data["restReady"] = _doc.World?.RestReady == true;
        data["roomsOpen"] = Camp?.Stage == "rooms";
        data["canPropose"] = profile.Value == _lead && Camp is null && _doc.World?.RestReady == true;
        return view with { Data = JsonSerializer.SerializeToElement(data, ArcJson.Options), DeadlineUtc = Camp?.Stage == "rooms" ? Camp.Due : null };
    }
}
