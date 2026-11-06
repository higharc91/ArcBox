using System.Collections.Immutable;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// Haggling, once per shop visit (shops design §3.3, USER DECISION E, decided by the owner on 2026-11-03). With a shop open, the leader taps
/// Haggle; the party picks who rolls Persuasion against the shop's <c>haggle</c> DC (default 15), on the 5d check screen, and the roll is
/// saved with the node it leads to, as every 5d roll is. A pass takes a tenth off what the party buys and adds a tenth to what it sells for
/// the rest of the visit; a fail keeps prices unchanged and spends the attempt until the next long rest. The visit's cursor keeps the result, so a restart neither rolls again nor
/// forgets it, and a failed attempt remains spent across visits and restarts until RestSeq advances. Rooms and cures haggle too (the owner, 2026-11-04): a tenth off, as for what the party buys.
/// </summary>
public sealed partial class TacticsGame
{
    /// <summary>The haggle's scene id: no lore scene and no thing's has it.</summary>
    public const string HaggleId = "haggle";

    /// <summary>True while the party haggles at the open shop: its check is the story's, and the shop waits behind it.</summary>
    private bool Haggling => _doc.World?.Shop is { Haggling: true } && Cursor?.Scene == HaggleId;

    /// <summary>True while a shop is open and trading: open, and not haggling.</summary>
    private bool Trading => _doc.World?.Shop is not null && !Haggling;

    /// <summary>True when the open shop has a price to haggle over: it sells, buys, or offers rooms or cures.</summary>
    private static bool Haggles(ShopDef shop) => !shop.Stock.IsEmpty || !shop.Buys.IsEmpty || !shop.Services.IsEmpty;

    /// <summary>True when the party may haggle now: at a shop that trades, not yet this visit, with no failed attempt since the last long rest and no rest under way.</summary>
    private bool CanHaggle => OpenShop is { } shop && Haggles(shop) && _doc.World!.Shop is { Haggle: null, Haggling: false } && Camp is null
        && _doc.Shops?.Shops.GetValueOrDefault(shop.Id)?.FailedHaggleRest != _doc.World.RestSeq;

    /// <summary>True when this visit's haggle passed: its prices are a tenth better.</summary>
    private bool Haggled => _doc.World?.Shop?.Haggle?.Passed == true;

    /// <summary>
    /// The haggle's scene: a line, then straight to the party's pick of who rolls Persuasion (the option is decided as it starts, as a
    /// search's is), then the keeper's answer. Null with no shop open.
    /// </summary>
    private SceneDef? HaggleScene()
    {
        if (OpenShop is not { } shop)
            return null;
        var dc = shop.Haggle ?? ShopRules.DefaultHaggleDc;
        SceneLine Line(string node, string text) => new($"{HaggleId}.{shop.Id}.{node}.0", SceneDef.Narrator, text, SceneLine.DefaultMood);
        var option = new SceneOption(HaggleId, "Haggle", null, null, [], [], OptionTags.None, new SceneCheck("persuasion", dc, "haggled", "refused"));
        ImmutableArray<SceneNode> nodes =
        [
            new(HaggleId, [Line(HaggleId, $"The party haggles with {shop.Name}.")], [], [option], false, null, false),
            new("haggled", [Line("haggled", shop.Stock.IsEmpty && shop.Buys.IsEmpty
                ? $"{shop.Name} gives way: a tenth off, this visit."
                : $"{shop.Name} gives way: a tenth off what you buy, and a tenth more for what you sell, this visit.")], [], [], false, null, true),
            new("refused", [Line("refused", $"{shop.Name} won't budge on price. Try again after a long rest.")], [], [], false, null, true),
        ];
        return new SceneDef(HaggleId, "", null, $"Haggle with {shop.Name}", [], [], [SceneDef.Narrator], HaggleId, nodes, false);
    }

    /// <summary>
    /// The leader haggles: the shop's visit sets aside the story cursor it was paused over, and the haggle's scene plays in its place.
    /// Refused once this visit has haggled.
    /// </summary>
    private void StartHaggle(Player player)
    {
        if (!CanHaggle || player.Profile?.Value != _lead || !Party().Any())
            return;
        var world = _doc.World!;
        _doc = _doc with
        {
            World = world with
            {
                Shop = world.Shop! with { Haggling = true, Under = world.Story },
                Story = new StorySave("", StorySave.Lead, HaggleId, null, 0, _storySeq),
            },
        };
        var scene = HaggleScene()!;
        EnterNode(scene, scene.Start);
    }

    /// <summary>
    /// A node of the haggle is entered: its first decides the check at once, so the party goes straight to who rolls; its result, with the
    /// roll that led there, is kept in the visit, in the same save as the result's line.
    /// </summary>
    private void HaggleNodeEntered(SceneNode node, StoryRoll? roll)
    {
        if (node.Id == HaggleId)
        {
            SetCursor(Cursor! with { Checking = HaggleId });
            return;
        }
        if (roll is null)
            return;
        if (!roll.Passed && OpenShop is { } shop)
        {
            var stock = StockOf(shop) with { FailedHaggleRest = _doc.World!.RestSeq };
            _doc = _doc with { Shops = new ShopsSave((_doc.Shops?.Shops ?? Ordinal.Map<ShopSave>()).SetItem(shop.Id, stock), stock.Chapter) };
        }
        var name = _doc.Members.GetValueOrDefault(roll.Member)?.Character.Name ?? roll.Member;
        _doc = _doc with { World = _doc.World! with { Shop = _doc.World.Shop! with
        {
            Haggle = new HaggleSave(roll.Member, name, roll.Die, roll.Bonus, roll.Dc, roll.Passed),
        } } };
    }

    /// <summary>The haggle's scene has ended: the cursor it set aside comes back, and the shop is open again, at the prices it won.</summary>
    private void FinishHaggle()
    {
        EndTelling();
        var visit = _doc.World!.Shop!;
        _doc = _doc with { World = _doc.World with { Shop = visit with { Haggling = false, Under = null }, Story = visit.Under } };
        KeepWorld();
        Save();
        _phase = TacticsPhase.Story;
        if (visit.Haggle is { } result)
            Say(result.Passed ? $"{result.Name} haggles: a tenth off buys and a tenth more for sales." : $"{result.Name} haggles, but the price stands.");
    }

    /// <summary>Back into a haggle after a restart: on its saved line, or its roll's result; a haggle with no shop any more just ends.</summary>
    private void ResumeHaggle()
    {
        var c = Cursor!;
        _storySeq = Math.Max(_storySeq, c.Seq);
        var scene = HaggleScene();
        var node = c.Node is null ? null : scene?.Node(c.Node);
        if (scene is null || (c.Node is not null && (node is null || c.Line < 0 || c.Line >= node.Lines.Length)))
        {
            FinishHaggle();
            return;
        }
        _phase = TacticsPhase.Story;
        if (node is null)
            EnterNode(scene, scene.Start);
        else if (c.Roll is not null)
            ShowRoll();
        else
            PlayLine(scene, node, c.Line);
    }

    /// <summary>What the phone's shop shows of haggling: whether this phone may start it, the DC, and the visit's result once rolled. Null at a shop that doesn't trade.</summary>
    private object? HaggleScreen(ShopDef shop, ProfileId profile)
    {
        if (!Haggles(shop))
            return null;
        var result = _doc.World!.Shop!.Haggle;
        return new
        {
            can = CanHaggle && profile.Value == _lead,
            spent = _doc.Shops?.Shops.GetValueOrDefault(shop.Id)?.FailedHaggleRest == _doc.World.RestSeq,
            dc = shop.Haggle ?? ShopRules.DefaultHaggleDc,
            result = result is null ? null : new { member = result.Member, name = result.Name, die = result.Die, bonus = result.Bonus, total = result.Die + result.Bonus, dc = result.Dc, passed = result.Passed },
        };
    }
}
