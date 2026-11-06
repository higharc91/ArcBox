import { useEffect, useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { CampScene } from "./camp-scene";
import { Services, type ServiceData } from "./services";
import { Sheet } from "./details";
import { RecoveryMenu } from "./recovery";
import { HostMenu } from "./walk";

interface ShopRow { item: string; name: string; price: number; count: number; can?: boolean; member?: string; detail?: string }
/** This visit's haggle (the owner's decision E, 2026-11-03): whether this phone may start it, its DC, and what came of it. */
interface HaggleData {
  can: boolean; dc: number;
  result: { member: string; name: string; die: number; bonus: number; total: number; dc?: number; passed: boolean } | null;
}
interface ShopData extends ServiceData {
  shop: { id: string; name: string; keeper: string; greeting: string };
  gold: number; items: ShopRow[]; sell: ShopRow[];
  you: { member?: string; canBuy: boolean }; leader: boolean; vip?: boolean; difficulty?: string; feed: string[];
  /** Only at a shop that trades: an inn or a temple has none. */
  haggle?: HaggleData;
}

/** Haggling once a visit: the leader starts it, the party picks who rolls Persuasion, and a pass moves every price here by a tenth. */
function Haggle({ haggle, send, shop, pending }: { haggle: HaggleData; send: CustomScreenProps["send"]; shop: string; pending: boolean }) {
  const r = haggle.result;
  return <div class="tx-shop-haggle" role="status">
    {r ? <p><b>{r.passed ? "Haggled" : "No deal"}</b>{` · ${r.name} rolled ${r.die} + ${r.bonus} = ${r.total} vs DC ${r.dc ?? haggle.dc}. `}
      {r.passed ? "A tenth off what you buy, a tenth more for what you sell, this visit." : "Prices stand. No harm done."}</p>
      : haggle.can ? <button type="button" class="tx-btn gold" disabled={pending} onClick={() => send("shopHaggle", { shop })}>
        {`Haggle · Persuasion DC ${haggle.dc}`}</button>
      : <p class="tx-note">{`The leader can haggle once this visit (Persuasion DC ${haggle.dc}).`}</p>}
  </div>;
}

/** LAN phones can use HTTP, where randomUUID is unavailable but getRandomValues still works. */
export function tradeId(): string {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6]! & 15) | 64;
  bytes[8] = (bytes[8]! & 63) | 128;
  const hex = [...bytes].map(b => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** Every price, count and bag is authoritative. A tap waits for the next server view. */
export function Shop({ data, send, viewId }: CustomScreenProps) {
  const d = data as ShopData;
  const [tab, setTab] = useState<"buy" | "sell">("buy");
  const [counts, setCounts] = useState<Record<string, number>>({});
  const [pending, setPending] = useState(false);
  const [detail, setDetail] = useState<ShopRow | null>(null);
  useEffect(() => { setPending(false); setCounts({}); }, [viewId, data]);
  const recovery = !!d.services?.length;
  const inn = d.services?.some(s => s.kind === "room");
  const rows = tab === "buy" ? d.items : d.sell;
  const trade = (row: ShopRow, count: number) => {
    if (pending) return;
    setPending(true);
    send(tab === "buy" ? "shopBuy" : "shopSell", {
      shop: d.shop.id, item: row.item, count, trade: tradeId(),
      ...(tab === "sell" ? { member: row.member } : {}),
    });
  };
  return <section class={`tx tx-shop${recovery ? " tx-recovery" : ""}`}>
    {d.vip && (recovery ? <RecoveryMenu keep="Keep resting" difficulty={d.difficulty} send={send} /> : <HostMenu canBattle={false} keep="Keep trading" difficulty={d.difficulty} send={send} />)}
    <header><span class="tx-eyebrow">{recovery ? inn ? "Inn" : "Temple" : `Trader · ${d.shop.keeper}`}</span><h1>{d.shop.name}</h1>
      {recovery ? <strong class="tx-recovery-purse" aria-label="Your purse" aria-live="polite">{d.gold} gold</strong> : <><span class="tx-eyebrow">Your purse</span><strong class="tx-shop-gold" aria-live="polite">{d.gold} gold</strong><p>{d.shop.greeting}</p></>}</header>
    {d.haggle && <Haggle haggle={d.haggle} send={send} shop={d.shop.id} pending={pending} />}
    {inn && d.roomsOpen && <CampScene stage="rooms" mode="inn" members={d.members ?? []} you={d.you.member} />}
    {(!d.services?.length || d.items.length > 0) && <div class="tx-shop-tabs" role="tablist" aria-label="Trade">
      <button class="tx-btn" role="tab" aria-selected={tab === "buy"} onClick={() => setTab("buy")}>Buy</button>
      <button class="tx-btn" role="tab" aria-selected={tab === "sell"} onClick={() => setTab("sell")}>Sell</button>
    </div>}
    {d.services?.length ? <Services data={d} send={send} viewId={viewId} makeId={tradeId} /> : null}
    {!d.you.canBuy && <p class="tx-note">Choose a character to trade.</p>}
    <ul class="tx-shop-items" aria-label={tab === "buy" ? "Shop stock" : "Your bag"}>
      {rows.map(row => {
        const limit = tab === "buy" ? Math.min(row.count < 0 ? Number.MAX_SAFE_INTEGER : row.count, row.price > 0 ? Math.floor(d.gold / row.price) : Number.MAX_SAFE_INTEGER) : row.count;
        const count = Math.max(1, Math.min(counts[row.item] ?? 1, limit));
        const can = !pending && d.you.canBuy && limit > 0 && (tab === "sell" || row.can !== false);
        return <li key={row.item}>
          <button type="button" class="tx-shop-name" onClick={() => setDetail(row)} aria-label={`Details for ${row.name}`}>{row.name}</button>
          <span>{row.price} gold each · {row.count === -1 ? "unlimited" : row.count === 0 ? "Sold out" : `${row.count} ${tab === "buy" ? "in stock" : "in your bag"}`}</span>
          <div class="tx-shop-quantity">
            <button class="tx-btn ghost" aria-label={`Fewer ${row.name}`} disabled={!can || count <= 1} onClick={() => setCounts({ ...counts, [row.item]: count - 1 })}>−</button>
            <output aria-label={`Quantity of ${row.name}`}>{count}</output>
            <button class="tx-btn ghost" aria-label={`More ${row.name}`} disabled={!can || count >= limit} onClick={() => setCounts({ ...counts, [row.item]: count + 1 })}>+</button>
            <button class="tx-btn" aria-label={`${tab === "buy" ? "Buy" : "Sell"} ${row.name}`} disabled={!can} onClick={() => trade(row, count)}>
              {pending ? "Trading…" : `${tab === "buy" ? "Buy" : "Sell"} · ${count * row.price} gold`}
            </button>
          </div>
        </li>;
      })}
    </ul>
    {rows.length === 0 && !d.services?.length && <p class="tx-note">No items to sell here.</p>}
    <div class="tx-note" role="status">{(recovery ? d.feed.slice(-1) : d.feed).map((line, i) => <p key={i}>{line}</p>)}</div>
    {d.roomsOpen ? <p class="tx-note">Dawn follows shortly.</p> : d.leader ? <button class="tx-btn ghost" disabled={pending} onClick={() => send("shopLeave", { shop: d.shop.id })}>Leave shop</button> : <p class="tx-note">The leader closes the shop.</p>}
    {detail && <Sheet title={detail.name} eyebrow="Item details" onClose={() => setDetail(null)}><p>{detail.detail ?? detail.name}</p><p>{detail.price} gold each</p></Sheet>}
  </section>;
}
