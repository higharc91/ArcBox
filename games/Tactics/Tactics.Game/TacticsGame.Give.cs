using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    private readonly HashSet<string> _giving = new(StringComparer.Ordinal);

    // Room input serialization and one immutable document/save make debit, credit and receipt atomic.
    private void Give(Player player, ProfileId profile, JsonElement? payload)
    {
        if (payload is not { } data || data.ValueKind != JsonValueKind.Object) return;
        if (!_online.Contains(player.Id) || !_doc.Members.TryGetValue(profile.Value, out var giver)) return;
        var trade = Str(data, "trade");
        var recipient = Str(data, "member");
        if (string.IsNullOrWhiteSpace(trade) || trade.Length > 128 || recipient == profile.Value
            || recipient is null || !_doc.Members.TryGetValue(recipient, out var to)) return;
        var receipt = profile.Value + "/" + trade;
        var receipts = _doc.GiftTrades.IsDefault ? ImmutableArray<string>.Empty : _doc.GiftTrades;
        if (receipts.Contains(receipt)) return;
        var gold = 0;
        if (data.TryGetProperty("gold", out var amount) && (amount.ValueKind != JsonValueKind.Number || !amount.TryGetInt32(out gold) || gold < 0)) return;
        if (gold > giver.Gold || (long)to.Gold + gold > int.MaxValue) return;
        var item = Str(data, "item");
        var count = 0;
        ItemDef? def = null;
        ItemStack? stack = null;
        ItemStack? targetStack = null;
        if (item is not null)
        {
            if (!Rules.Items.TryGetValue(new ItemId(item), out def) || !data.TryGetProperty("count", out var quantity)
                || quantity.ValueKind != JsonValueKind.Number || !quantity.TryGetInt32(out count) || count <= 0) return;
            stack = giver.Character.Bag.FirstOrDefault(s => s.Item == def.Id);
            targetStack = to.Character.Bag.FirstOrDefault(s => s.Item == def.Id);
            if (stack is null || stack.Count < count || (long)(targetStack?.Count ?? 0) + count > int.MaxValue) return;
        }
        if (gold == 0 && item is null) return;
        var fromBag = giver.Character.Bag;
        var toBag = to.Character.Bag;
        if (def is not null && stack is not null)
        {
            fromBag = fromBag.Remove(stack);
            if (stack.Count > count) fromBag = fromBag.Add(stack with { Count = stack.Count - count });
            if (targetStack is not null) toBag = toBag.Remove(targetStack);
            toBag = toBag.Add(new ItemStack(def.Id, (targetStack?.Count ?? 0) + count));
        }
        _doc = _doc with
        {
            Members = _doc.Members
                .SetItem(profile.Value, giver with { Gold = giver.Gold - gold, Character = giver.Character with { Bag = fromBag } })
                .SetItem(recipient, to with { Gold = to.Gold + gold, Character = to.Character with { Bag = toBag } }),
            GiftTrades = receipts.Add(receipt),
        };
        Save();
        var gifts = new List<string>();
        if (def is not null) gifts.Add($"{count} × {def.Name}");
        if (gold > 0) gifts.Add($"{gold} gold");
        Say($"{giver.Character.Name} gave {string.Join(" and ", gifts)} to {to.Character.Name}.");
    }

    private sealed record GiftMemberView(string Id, string Name, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Away);

    private object GiveScreen(ProfileId profile)
    {
        var member = _doc.Members[profile.Value];
        var present = Party().Where(m => m.Here).Select(m => m.Member).ToHashSet(StringComparer.Ordinal);
        return new
        {
            items = member.Character.Bag.Where(s => s.Count > 0).Select(s => new { id = s.Item.Value, name = Rules.Items[s.Item].Name, count = s.Count }),
            members = _doc.Members.Where(m => m.Key != profile.Value).Select(m => new GiftMemberView(m.Key, m.Value.Character.Name, !present.Contains(m.Key))),
        };
    }
}
