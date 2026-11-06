import { useEffect, useRef, useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { tradeId } from "./shop";

export interface GiveData {
  gold: number;
  items: { id: string; name: string; count: number }[];
  members: { id: string; name: string; away?: boolean }[];
}

function GiftForm({ data, send, viewId, stop }: { data: GiveData; send: CustomScreenProps["send"]; viewId: string; stop: () => void }) {
  const [member, setMember] = useState(data.members[0]?.id ?? "");
  const [item, setItem] = useState("");
  const [count, setCount] = useState(1);
  const [gold, setGold] = useState(0);
  const [pending, setPending] = useState(false);
  const waiting = useRef(false);
  const receipt = useRef(tradeId());
  useEffect(() => { waiting.current = false; setPending(false); receipt.current = tradeId(); }, [viewId]);
  useEffect(() => {
    if (!pending) return;
    const timer = setTimeout(() => { waiting.current = false; setPending(false); }, 3000);
    return () => clearTimeout(timer);
  }, [pending, viewId]);
  const owned = data.items.find(i => i.id === item);
  const valid = data.members.some(m => m.id === member) && Number.isInteger(gold) && gold >= 0 && gold <= data.gold
    && (item ? !!owned && Number.isInteger(count) && count > 0 && count <= owned.count : gold > 0);
  if (!data.members.length) return null;
  return <form onSubmit={e => {
      e.preventDefault();
      if (!valid || waiting.current) return;
      waiting.current = true; setPending(true); stop();
      send("give", { member, ...(item ? { item, count } : {}), gold, trade: receipt.current });
    }}>
      <label>Give to<select value={member} onChange={e => setMember(e.currentTarget.value)}>
        {data.members.map(m => <option key={m.id} value={m.id}>{m.name}{m.away ? " (away)" : ""}</option>)}
      </select></label>
      <label>Item<select value={item} onChange={e => setItem(e.currentTarget.value)}>
        <option value="">Gold only</option>
        {data.items.map(i => <option key={i.id} value={i.id}>{i.name} × {i.count}</option>)}
      </select></label>
      {item && <label>Item count<input type="number" min="1" max={owned?.count ?? 0} value={count} onInput={e => setCount(Number(e.currentTarget.value))} /></label>}
      <label>Gold to give<input type="number" min="0" max={data.gold} value={gold} onInput={e => setGold(Number(e.currentTarget.value))} /></label>
      <button class="tx-btn gold" type="submit" disabled={!valid || pending}>{pending ? "Sending…" : "Give"}</button>
      <p class="tx-note">Gifts go straight to their bag and purse, even while they’re away.</p>
    </form>;
}


/** Load bag/recipient details only while open, so ordinary walking snapshots remain small. */
export function Give({ data, gold, send, viewId, stop }: { data?: Omit<GiveData, "gold">; gold: number; send: CustomScreenProps["send"]; viewId: string; stop: () => void }) {
  return <details class="tx-give" onToggle={e => {
    if (e.currentTarget.open) { stop(); if (!data) send("giveOpen"); }
    else if (data) send("giveClose");
  }}>
    <summary>Give items or gold</summary>
    {data ? <GiftForm data={{ ...data, gold }} send={send} viewId={viewId} stop={stop} /> : <p class="tx-note">Opening your bag…</p>}
  </details>;
}
