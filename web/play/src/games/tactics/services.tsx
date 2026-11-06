import { useEffect, useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { RecoveryParty, type RecoveryMember } from "./recovery";

export interface ServiceData {
  shop: { id: string }; gold: number; leader: boolean; you: { canBuy: boolean; member?: string };
  services?: { id: string; kind: string; name: string; price: number }[];
  guests?: { member: string; name: string; room: boolean; injuries: { id: string; name: string; detail: string; kind: string }[] }[];
  members?: RecoveryMember[];
  restReady?: boolean; roomsOpen?: boolean; canPropose?: boolean;
}
export function Services({ data: d, send, viewId, makeId }: { data: ServiceData; send: CustomScreenProps["send"]; viewId: string; makeId(): string }) {
  const [pending, setPending] = useState(false);
  useEffect(() => setPending(false), [viewId, d]);
  const pay = (service: string, member: string, injury?: string) => {
    if (pending) return;
    setPending(true);
    send("shopService", { shop: d.shop.id, service, member, injury, trade: makeId() });
  };
  const members = d.members ?? d.guests?.map(g => ({ ...g, class: "", here: true, hp: 0, maxHp: 0, resources: [], wellFed: false })) ?? [];
  return <section class="tx-services" aria-label="Recovery services">{d.services?.map(s => <div key={s.id}>
    <div class="tx-recovery-service-line"><span>{s.kind === "room" ? "A room for the night" : "Heal an injury"}</span><b>{s.price} gold{s.kind === "room" ? " each" : ""}</b></div>
    {s.kind === "room" && !d.roomsOpen && <>
      {d.leader ? <button class="tx-btn gold tx-recovery-propose" disabled={!d.canPropose} onClick={() => send("restPropose", { shop: d.shop.id })}>Rest for the night</button>
        : <p class="tx-note">The leader proposes the night.</p>}
      {!d.restReady && <p class="tx-note">You're not tired yet.</p>}
    </>}
    <div class="tx-recovery-section"><span>Party · battle ready</span><small>{s.kind === "cure" ? "Tap an injury to heal" : d.roomsOpen ? "Book a room for anyone" : "Tap injuries for details"}</small></div>
    <RecoveryParty members={members} you={d.you.member}
      status={m => m.room ? "Room booked" : !m.here ? "Away" : undefined}
      action={m => s.kind === "room" && d.roomsOpen ? <button class={`tx-recovery-book${m.room ? " booked" : ""}`} aria-label={`Buy room for ${m.name} · ${s.price} gold`}
        disabled={pending || !d.you.canBuy || d.gold < s.price || m.room} onClick={() => pay(s.id, m.member)}>{m.room ? "✓ Booked" : `${s.price} gold`}</button> : null}
      injuryAction={(m, i) => s.kind === "cure" ? { label: `Heal ${m.name}'s ${i.name} · ${s.price} gold`, disabled: pending || !d.you.canBuy || d.gold < s.price,
        run: () => pay(s.id, m.member, i.id) } : undefined} />
    <details class="tx-recovery-help"><summary>{s.kind === "room" ? "What does a room restore?" : "About healing"}</summary>
      <p>{s.kind === "room" ? "The party votes first. Each booked room mends every injury and grants Well fed. Unbooked characters still mend ordinary rest injuries. Pay for anyone from your own purse."
        : "Choose an injury icon, then confirm a cure. You can pay for any party member from your own purse. No rest is needed."}</p></details>
  </div>)}</section>;
}
