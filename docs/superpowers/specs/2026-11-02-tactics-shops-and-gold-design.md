# Wardenflame: gold and shops, design

- **Status:** implemented for issue #68; see the implementation decisions below and `../reports/2026-11-02-shops/README.md`. Placeholders only: no art, and no generated audio.
- **Date:** 2026-11-02.
- **Sub-project:** the trading half of sub-project 6 ("loot rolling, need/greed, inventories, trading and shops":
  docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md §616). It needs only the smallest inventory. Need/greed and a full
  inventory screen are out of scope (§9).
- **Builds on:** the world interactables engine (docs/superpowers/specs/2026-11-02-tactics-interactables-design.md, read it first,
  especially §2 the model, §2.7 interacting, §2.9 what's sent, §3 decisions, §4 the art brief conventions, §5 validation) and the story
  runtime (5d: scenes, votes, the leader's choice, presence holds, restart safety).
- **Reference:** Baldur's Gate 3's trading: talk to a trader, a two-sided trade window, buy and sell at prices moved by a check.
  Here it's simpler and multiplayer-first: one room, many phones, one TV.

## 0. Pre-implementation baseline

| Piece | Today | Where |
|---|---|---|
| Currency | **None.** No gold anywhere in the save or the rules. | — |
| Prices | **None.** `ItemDef(Id, Name, Use, Tags, Replaces)` has no value. Weapons and armour have no price either. | `Tactics.Logic/Content/Defs.cs:371`, `rules/items.json` (4 items: potion_of_healing, greater_healing, alchemists_fire, smoke_bomb), `weapons.json`, `armor.json` |
| Inventory | Each character's `CharacterRecord.Bag` (`ImmutableArray<ItemStack>`), filled from the class's starting kit; consumables used in a fight stay spent. Interactables put loot into the **leader's** bag (`TacticsGame.Interact.cs:238`). No equipping beyond the starting `Weapon`/`Armor`/`Shield`. No shared stash. | `Tactics.Logic/Content/SheetBuilder.cs:13`, `Tactics.Logic/State/UnitState.cs:26` |
| Save | `TacticsDocument` v1 → `MemberRecord(CharacterRecord, Xp, …)` per profile, `WorldSave` with `Flags`, `Things` (per-interactable state). New optional fields are added **without a version bump** (absent reads as default, default isn't written). | `Tactics.Game/Campaign/TacticsDocument.cs` |
| Interactions | `InteractScenes.Plan` → `InstantPlan` or `ScenePlan` (5d `SceneDef` + an outcome per node). Outcomes are absolute and applied in the same save as the node's first line, so a restart can't double them (`LootDue`). | `Tactics.Logic/World/InteractScenes.cs`, `Tactics.Game/TacticsGame.Interact.cs` |
| Merchants | **Scenery only.** Ashen Ford has 5 people with `"preset": "merchant"` at stalls (`rules/world/ashen_ford.json` `people`). The town kit has animated shop signs. | |
| A coin art key | `pickups.coin_pile` exists; nothing gives coins. | interactables art spec |
| Inputs | `walk`, `battles`, `backToWalk`, `endSession`, `interact {target}`, `search`, `skip`, `vote`, `pickRoller`. | `TacticsGame.Explore.cs:122` |
| Phone contract | Screens are JSON; fixtures in `web/play/test/fixtures`, regenerated with `UPDATE_FIXTURES=1` via `Tactics.Game.Tests/PhoneFixtureTests.cs`. Regions and screens without a new feature must stay **byte-identical**. | |
| Rules hash | Any change to a file in `rules/manifest.json` changes `Rules.Hash`, so a saved battle restarts once. Fine; mention it. | |

The user's earlier answers that bear on this: oil flasks come "from loot (interactable chests) and future merchants, not starting kits"
(environmental tiles answer 9). Balance decisions are the user's: every number below is a **default to confirm** (§8).

## 1. Goals and non-goals

**Goals.**
1. Gold exists, is saved, and is earned and spent.
2. A trader in the world (a person or a stall) opens a **shop** the whole room sees on the TV, and the players buy and sell from their phones.
3. Prices, stock and restocking are content, not code.
4. Restart-safe and idempotent: a server restart or a dropped phone mid-trade never charges twice, never gives an item twice, and never
   loses gold.
5. Nothing changes for regions, saves or screens without shops (byte-identical fixtures, old saves read unchanged).

**Non-goals now** (§9): equipment trading that changes AC or weapons, need/greed loot rolls, a full inventory/equip screen, haggling
mini-games, stealing, player-to-player trade, a party stash UI beyond what selling needs.

## 2. Gold

### 2.1 Where it lives — owner decision A2

Each player's `MemberRecord` saves its own non-negative `Gold` integer alongside its own character and bag. The authenticated
phone buys with that purse and receives the item in that bag; sale proceeds return to the seller. Other players' balances and
inventories do not change. Interacting with the keeper opens trading on every phone; each connected player with a character can
trade independently until the leader leaves.

No save version bump is required. A missing personal balance reads as zero and zero is omitted. A save from the initial shop build
with a root `TacticsDocument.Gold` balance migrates by splitting that balance across its saved members once, preserving existing
personal balances and the total. The root field is retained only to read legacy saves and is omitted once migrated.

**Owner reward decision:** split **all** gold rewards evenly, including chests/pickups. Battle rewards go to the saved battle
participants, including a participant whose phone disconnected during the fight. Exploration and story rewards go to present
players with characters. Whole-gold remainders go in stable profile order, so no gold is lost and a restart cannot change the
allocation. Shared story costs are divided across present players' available balances; an empty purse's share is covered by the
others. Shop purchases always use the buyer's balance alone.

### 2.2 How gold is earned — USER DECISION B

Defaults, all content-driven and each switchable off:
- **Loot.** Interactables' `loot` gains `{ "gold": 25 }` entries alongside `{ item, count }` (containers, caches, pickups — the
  `pickups.coin_pile` art key already exists). Split across present players' purses, never put into a bag. Line: "Found: 25 gold."
- **Battles.** An encounter may carry `gold` (a fixed number per encounter in its file; default none). Paid on a **win** only (the loss
  rule already pays no XP; same here).
- **Story.** A scene option or node can `gold: +N / -N` (a reward, a bribe). `-N` needs the present players' balances to cover it: the option is disabled
  (shown with "needs 20 gold") when it can't. check.py and `LoreBook` validate it.
- **Selling** (§4).
- **Starting gold:** 0 per player.

Not by default: gold per kill, gold that scales with level, gold from the overworld mobs idea (that's tied to the farming question in §8).

### Chapter 1 reward content (owner-authorized follow-up)

| Source | Party gold | Four players | Six players |
|---|---:|---:|---:|
| Mayor's request, closing node (`ch1_mayor.close`) | 200 | 50 each | 33 each, two remainder coins assigned in profile order |
| Ford battle victory (`ford_assault` or `river_crossing`) | 120 | 30 each | 20 each |
| Reeve's chest, alongside its potion | 60 | 15 each | 10 each |
| Smugglers' cache, alongside its smoke bomb | 40 | 10 each | 6 each, four remainder coins assigned in profile order |

The two ford battles are alternative story routes; either route earns the same 120-gold victory reward, and losses earn nothing.
Normal Chapter 1 play pays 420 gold if both optional treasures are found. The council advance funds an early shop purchase
without selling a starting potion. Story and treasure rewards retain their existing once-only saved resolution; victory gold
is saved with the battle checkpoint. Already-resolved story nodes and emptied treasure are not rewarded retroactively. These
amounts are intentionally small initial tuning values; starting gold remains zero and no new fees or sinks were added.

**Remake exploit fixed:** every existing character carries its current bag, injuries and purse through class/race changes. Starting
kits are granted only on first creation. Pre-battle remakes remain unlimited, and only a post-battle remake uses the one free remake.

### 2.3 What players see
- **TV:** the total across player purses on the party banner while walking (explicitly labelled), individual balances in the shop, and a "+25 gold" pop like XP's. Hidden while it's 0 and no shop
  has ever been seen, so Ashen Ford's TV frame is unchanged until gold exists (keep the size budget: see §6.4).
- **Phone:** the viewer's own balance on the walk screen's party strip and labelled "Your purse" in the shop, only when > 0 or in a region with shops (byte-identical otherwise).

## 3. Prices and the item catalogue

### 3.1 Prices on items
`ItemDef` gains `int? Price` (gp, the **buy** price; null = can't be bought or sold). Optional and `WhenWritingNull`, so packs and
fixtures that don't set it are unchanged. Weapons and armour get the same field for later (§9); shops in this slice only trade
`items.json` consumables. Default prices (SRD-flavoured, **USER DECISION C**):

| Item | Price | Note |
|---|---|---|
| potion_of_healing | 50 | SRD |
| greater_healing | 150 | SRD |
| alchemists_fire | 50 | SRD |
| smoke_bomb | 25 | house |
| oil_flask (when env tiles add it) | 1 → **10** | SRD is 1 gp; house price so it isn't free fire damage |

### 3.2 Sell price — USER DECISION D
Default: **half** the price, rounded down (`SellRatio = 0.5` in a rules file, not code). Items without a price can't be sold. Story
items (keys, quest things — today these are flags, not bag items) can't be sold by construction.

### 3.3 A Persuasion discount — USER DECISION E (decided 2026-11-03: on, once per visit)

**Rooms and cures haggle too (the owner, 2026-11-04).** A passed haggle also takes a tenth off a room or a cure, rounded up as item buys are
(`ShopRules.Service`), and the inn and the temple offer Haggle like any shop. The server charges, phones show and the TV's panel lists the
same price.
BG3 moves prices by attitude/Persuasion. When a shop opens, the leader may try **Haggle** once per shop per visit: a 5d check
(party picks who rolls Persuasion, DC from the shop's `haggle`, default 15). Pass: buy prices −10% and sell +10% (from the ratio) for this
visit; fail: no change (no penalty). The roll is saved like every 5d roll.

**Built (issue #83, `owner-decisions-rules`):** the shop screen offers the leader "Haggle · Persuasion DC 15" while the visit hasn't
haggled; the haggle plays as a story scene over the open shop (the check is decided at once, so the party goes straight to who rolls; 20 s,
best bonus if nobody picks), and the shop comes back with its result. `ShopCursor` keeps `Haggle` (who rolled, the die, the bonus, the DC,
pass or fail), `Haggling` while the check plays and `Under` (the story cursor it set aside), so a restart shows the saved roll or the pick
again and never rolls twice, a reconnect holds the check while nobody is here, and End session keeps it for the next night. Prices:
`ShopRules.Buy(item, shop, haggled)` is `ceil(price × priceMod × 0.9)`, and `Sell` is `floor(plain buy × sellRatio × 1.1)`, never more than
that visit's buy price for the same item (at a sell ratio near 1 a haggle would otherwise pay out more than it charges). The phones get
`haggle: { can, dc, result }` on the shop screen (the five shop fixtures gained it) and the adjusted prices; the TV's shop panel shows the
result and its prices, and waits behind the check while it's rolled. An inn and a temple (services only) have nothing to haggle over, and
service prices (rooms, cures) don't haggle. A new visit may haggle again.

## 4. Shops

### 4.1 Content: `rules/shops/<region>.json`
Separate files, listed in `rules/manifest.json`, like interactables (one source of truth per concern).

```json
{
  "region": "ashen_ford",
  "shops": [
    {
      "id": "af_market_herbalist",
      "name": "Wenna's Remedies",
      "keeper": { "person": 3 },
      "greeting": "Wenna wipes her hands on her apron. \"Potions, salves. Coin first.\"",
      "farewell": "\"Mind the ford.\"",
      "stock": [
        { "item": "potion_of_healing", "count": 3, "restock": "chapter" },
        { "item": "smoke_bomb", "count": 2, "restock": "never" }
      ],
      "buys": ["potion", "bomb"],
      "priceMod": 1.0,
      "requires": [], "unless": ["af_market_burned"],
      "haggle": 15
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `keeper` | **Who you talk to.** `{ "person": <index into the region's people> }` (a townsperson; it then can't wander and gets an Interact target), or `{ "thing": "<interactable id>" }` of a new kind `stall` (§4.4). Exactly one. |
| `stock` | Item, starting count (or `"infinite"`), `restock`: `never` / `chapter` (back to its count when a chapter starts) / `rest` (later, needs a rest model). |
| `buys` | Item tags the keeper will buy from the party (empty = buys nothing). |
| `priceMod` | Multiplier on item prices for this shop (a greedy fence 1.25, a grateful village 0.8). |
| `requires`/`unless` | Flags, as scenes and things: a shop that isn't there has no target and isn't sent. |
| `haggle` | Optional Persuasion DC (only used if decision E is on). |

### 4.2 Validation (load time, like interactables §5)
Unknown item / tag / person index / thing id → `ContentException` naming the file and JSON pointer. A keeper person must stand on a
walkable tile reachable from the region's start (reuse the interactables reachability). A person can keep at most one shop. A `stall`
thing can't be hidden, locked or trapped. Items in `stock` must have a `Price`. `priceMod` in (0, 4]. No shop on a stamp tile.

### 4.3 Saved state
`ShopsSave` on `TacticsDocument`: `{ shop id → { Stock: { item → count }, Chapter: int, Seq } }`, written only once something changes
(absent = as the content says). Content changes: an item no longer in stock is dropped; a count above the content's start is clamped.
`Regressed` doesn't apply (stock goes both ways), so idempotency comes from §5.2's trade ids instead.

### 4.4 The `stall` kind
A new interactable family `trade` with kind `stall` (states `open`, `closed`; blocks: yes), so a shop can be a market stall with nobody
drawn. Its art key `interactables.stall.open|closed`; stand-in `town.stall` (the town kit has stalls). Interacting with it opens its shop.

### 4.5 Talking to a keeper person
People in the world become Interact targets **only** when they keep a shop (no other dialogue yet; NPC talk is its own sub-project).
Reach and choosing are exactly the interactables' (§2.5 of that doc): the walk screen's `targets` gains `{ id: "shop:<id>", label:
"Trade with Wenna" }` in reach. The person faces the party when the shop opens and goes back to their pose when it closes.

## 5. The trade flow

### 5.1 Phase
A new **Shop** sub-state of the Story phase (as interactions reuse Story): walking stops, presence holds and End session work as in
5d, a restart resumes into the open shop. The cursor: `ShopCursor(ShopId, OpenedBy, Seq, Haggled?, Basket?)` saved in `WorldSave.Story`
(or a sibling optional field), so a restart lands back in the shop screen.

1. The leader picks the target → the TV shows the keeper's greeting (one narrated line, 5d style, voiced if a clip exists).
2. **Shop open.** Every phone shows the shop screen (§6.2); the TV shows the shop panel (§6.1). Every present player with a character can buy or sell from their own purse and bag.
3. **Leave.** The leader's "Leave" (or the last phone leaving) closes it with the farewell line, back to walking.

### 5.2 Buying and selling: inputs and idempotency
New inputs, accepted only while that shop is open:
- `shopBuy { shop, item, count, trade }`
- `shopSell { shop, member, item, count, trade }`
- `shopLeave { shop }`
- (`shopHaggle { shop }` if decision E is on — starts the Persuasion check; the shop screen returns afterwards.)

`trade` is a client-generated id (a UUID per tap). The server keeps the last 32 trade ids per shop in `ShopsSave`; a repeated id is a
no-op that re-sends the current state. Each trade is **one atomic change of the document**: the authenticated player's purse, stock and their bag in a single
`_doc with { … }` and a single save, exactly as `LootDue` loot is applied. Checks, in order, all server-side: the shop is open and is
this one; the player is present; the item is in stock (buy) or in that member's bag (sell) with enough count; the keeper buys that tag
(sell); that player's own purse covers the price (buy). A refused trade answers with a reason the phone shows ("Not enough gold", "Sold out").

**Whose bag gets a bought item — owner decision G.** **The buyer's own character** (the phone that tapped Buy). If they have
no character (a spectator), Buy is disabled.

**Selling from whose bag:** only the authenticated seller's own bag. Sale proceeds return to that player's purse.

### 5.3 Concurrency
Two phones buying the last potion: the server serialises inputs (it already does per room); the second gets "Sold out". The phone
shows optimistic "buying…" only until the next screen arrives; it never decrements locally.

### 5.4 Bag limits — USER DECISION I
Default: no weight or slot limit (the bag is a list of stacks). Optional cap per stack (e.g. 5 potions) to stop hoarding.

## 6. Views and contract

### 6.1 TV
- **Shop panel:** keeper portrait/name, the shop name, the stock list (icon, name, price, count; sold out greyed), individual character balances, and a
  short feed of trades ("Ana bought a Potion of Healing — 50"). Reuse the story panel's frame and fonts.
- **Sounds:** `ui_coin_buy`, `ui_coin_sell`, `ui_shop_open`, `ui_shop_close` (keys only — **don't generate audio**; fall back to
  `ui_loot`/`ui_confirm`, as ThingSounds falls back).
- **World:** a keeper person gets a small coin marker over their head when in reach, like a beat's "!" (stand-in: `fx.notice` / a
  gold ring). A `stall` draws its stand-in.

### 6.2 Phone: the shop screen
`{ screen: "shop", shop: { id, name, keeper }, gold, items: [{ item, name, price, count, can }], sell: [{ member, item, name, price,
count }], you: { member?, canBuy }, leader: bool, haggle?: {...} }`. Buy row: name, price, stock, a −/+ count and Buy. Sell tab: the
viewer's own bag, with the sell price. `gold` is always the viewer's own balance. Leave (leader). Item details reuse the character screens' details popup (what it does in
play, from the rules pack). Large tap targets; works one-handed (same rules as walk.tsx).

### 6.3 Contract changes
New screen type, new inputs, optional `gold` on the walk screen and on `ExploreView`, optional `price` on item details. Add fixtures:
`shop-open`, `shop-sold-out`, `shop-poor`, `shop-sell`, `shop-spectator`, and `walk-with-gold`. **Every existing fixture must stay
byte-identical**: only emit `gold`/shop fields where gold > 0 or the region has shops.

### 6.4 Size
The explore view is already over the 1420-byte one-packet budget in Ashen Ford (things; see the handoff §8). The shop screen is
sent only to phones in the shop and only on change; keep the TV's shop state out of `ExploreView` (a `ShopView` sent while open).

## 7. Art and audio brief (placeholders only)

Follow the interactables art brief conventions (`docs/superpowers/specs/2026-11-02-tactics-interactables-design.md` §4.1: pixel art
at 1×, shown at 2×, the pack's palette, anchors at the feet, strips left to right). Add these keys to a new
`docs/superpowers/specs/2026-11-02-tactics-shops-art.json` in the same format as the interactables art JSON, each with a prompt:

| Key | What | Size (1×) |
|---|---|---|
| `icons.gold` | A small stack of gold coins for the purse | 12×12 |
| `icons.gold_large` | A coin pouch, for the shop panel header | 24×24 |
| `fx.coin` | A coin spinning up and fading (6 frames), for "+gold" | 16×16 ×6 |
| `fx.trader` | The trade marker over a keeper (a coin glint, 4-frame loop) | 12×12 ×4 |
| `interactables.stall.open` / `.closed` | A market stall with awning, goods out / covered | match `town.stall` |
| `ui.shop_frame` | Optional 9-slice for the TV shop panel, in the story panel's style | 48×48 9-slice |
| `portraits.<keeper>` | Keepers use their person preset's portrait (no new art) | — |

Example prompt style (write one per key): "Pixel art game icon, 12×12, a small stack of three gold coins with a 1-px dark outline,
light from the top-left, warm gold with two highlight pixels, transparent background, no anti-aliasing, matches a 16-bit fantasy
tactics UI." The user's art AI fills `content/tactics/base/icons|fx|interactables` from this file; until then use stand-ins (a gold
circle, `ui_loot`, the `town.stall` prop).

Sound keys (no generation; list them in `tools/tactics-audio/sfx-list.json` only if the user asks): `ui_coin_buy` ("coins counted
onto a wooden counter, short"), `ui_coin_sell` ("a coin pouch set down, two clinks"), `ui_shop_open` ("a wooden shop door with a
small bell"), `ui_shop_close` ("the bell again, softer").

## 8. Questions for the user (each with the default the build uses)

| # | Question | Default |
|---|---|---|
| A | Party purse or per character? | **Party purse** |
| B | Where gold comes from | **Loot + battle wins (per encounter) + story**; no per-kill gold |
| B2 | Starting gold | **0** |
| C | Item prices | **SRD-ish table in §3.1** |
| D | Sell price | **50%** of price |
| E | Haggling (Persuasion) | **Off** → **decided 2026-11-03: on, once per shop visit** (§3.3) |
| F | Who may trade while a shop is open | **Anyone present** |
| G | Whose bag gets a bought item | **The buyer's own character** |
| H | Sell from companions' bags | **No, only your own** |
| I | Bag limits | **None** |
| J | Restock | **Per chapter**, per stock line |
| K | Where the first shops go | **One** in Ashen Ford (the market herbalist), stocking potions and smoke bombs; Emberwick later |
| L | Does gold affect anything else (fees, ferry, bribes in Chapter 1)? | **No** until the user writes it in |

Record these in the doc as **USER DECISION**s with the defaults, like the interactables doc §3, and add them to the handoff's §8 list.

## 9. Later, noted (don't build now)
- Weapons and armour for sale and equipping (changes AC, attacks: a rules change and an equip screen).
- Need/greed loot rolls and a shared stash screen.
- Direct exchange/barter between players remains deferred. Gifting shipped in #153: the walk screen gives bag items and/or gold to any saved member, including absent heroes, in one atomic save. Receipts are scoped to the sender and retained across restarts so retrying an old gift cannot duplicate it. Gifts are announced on the TV.
- Stealing (Sleight of Hand) and a fence.
- A rest model (restock on rest; inns that cost gold).
- Gold sinks: injury healing at a temple (ties into "injuries never heal"), training, a ferry fare.
- Overworld mobs paying gold (blocked on the farming question).

## 10. Build plan (for the implementer)

TDD throughout; `dotnet test` (solution) and `cd web/play && npm test` green with 0 warnings after each task; small commits.

1. **Logic:** `ItemDef.Price`, a `shops` def + loader + validation (§4.2), the `stall` kind, `ShopRules` (buy/sell price with
   `priceMod`, ratio, haggle), with unit tests. Pack: prices in `items.json`, `SellRatio` in a rules file, `rules/shops/ashen_ford.json`
   with one shop (use a merchant person in the Ash Market area; check the tile is reachable).
2. **Save:** `MemberRecord.Gold` (legacy `TacticsDocument.Gold` migrates once), `ShopsSave`, round-trip tests incl. an old document and a default-not-written check.
3. **Earning:** interactable `loot` gold entries; encounter `gold` on a win; story `gold` options (check.py + `LoreBook` parity
   tests); the "+N gold" line/event.
4. **The trade flow:** targets for keepers, the Shop sub-state, inputs with trade ids, atomic apply, restart and reconnect tests
   (restart after a buy, a repeated trade id, two phones on the last item, a phone that leaves mid-trade, End session in a shop).
5. **Phone:** the shop screen, gold on the walk strip, fixtures (§6.3), tests.
6. **TV:** the shop panel, the trader marker, "+gold" pop, sound keys with fallbacks; a `--world-demo=shop` mode (like `things`) with
   shots of the panel and the marker.
7. **Docs:** USER DECISIONs, `docs/tv-host.md`, the art JSON (§7), the handoff §8.
8. **e2e:** extend the bots to walk to the shop, buy one potion, sell it back, and leave; run `tools/tv-e2e.sh` in that mode, and the
   Chapter 1 run (`ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force`) to show nothing else moved.

## Implementation decisions (issue #68)

Built on `shops-and-gold`. The following **USER DECISION** defaults from the issue are the implemented baseline, with the owner's subsequent purse/reward correction applied:

| Decision | Implemented default |
|---|---|
| A | One saved purse and bag per player. All gold rewards split evenly among participants, including chest gold. Legacy shared gold splits once on load. |
| B / B2 | Content-defined loot, winning encounters and story rewards/payments; Chapter 1 reward amounts above; zero starting gold; no per-kill gold. |
| C | Healing 50 gp; greater healing 150 gp; alchemist's fire 50 gp; smoke bomb 25 gp. Oil remains absent; its future default is 10 gp. |
| D | Content sellRatio 0.5, rounded down after the shop's rounded-up buy price. |
| E | Haggling off. The DC is validated content metadata; no haggle input or discount is enabled. **Superseded 2026-11-03 (issue #83): on, once per visit, as §3.3 says.** |
| F / G / H | Every connected player with a character trades from their own phone once the keeper opens; purchases use their own purse and go to their own bag; selling companions' bags is refused. |
| I | No gameplay bag limit; integer overflow is refused. |
| J | Per-line chapter restock or never. Rest is reserved content metadata; no rest mechanic exists. |
| K | One herbalist, Wenna's Remedies, at merchant person 30 in Ashen Ford's painted market. Accepts potion and obscure tags (the existing smoke-bomb tag). |
| L | No new fees or bribes. The owner subsequently authorized the modest Chapter 1 reward schedule above and the remake exploit fix. |

Gold and prices are integers. Positive fractional modified buy prices round up; sales round down. Accepted trade IDs are retained per shop for the most recent 32 trades; older IDs are outside that bounded replay window. The stock save also retains its content starting counts so normal sales survive reloads, while a changed content baseline is clamped and removed lines disappear. A saved shop cursor sits beside `WorldSave.Story`, preserving a paused beat's cursor. Last-phone departure closes the shop; ending a session preserves the visit for resumption.

All prior phone fixtures are retained byte-for-byte. Their pre-shop contract is exercised using a test pack without shops; six new fixtures exercise the real base shop. Item-price and manifest changes update `Rules.Hash`, so a saved battle is restarted once by the existing compatibility path.

Art and audio remain stand-ins. The new art brief is `2026-11-02-tactics-shops-art.json`; no generated files or sfx-list additions are included. `--world-demo=shop` shows the marker, panel, buy, sell, gold pop and departure. The actual town-stall sprites are supported as the stall's fallback. The existing gallery includes the new stall states.
