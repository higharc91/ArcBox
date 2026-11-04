import { useState, useRef, useEffect } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { MoodPortrait } from "./art";
import { useSfx } from "./sfx";
import "./quests.css";
import { TrinketSlot, type Trinket } from "./trinket";

export interface QuestEntry {
  id: string; name: string; status: "active" | "done" | "failed"; giver: string; portrait?: string;
  objective: string; history: { log: string; done: boolean; objective?: string }[]; result?: string;
  rewards?: { gold: number; xp: number; items: { name: string; count: number }[] };
}
/** A key on the party's key ring (the owner's decision 8, 2026-11-03): its name, where it was found, and whether it has opened a lock. */
export interface KeyEntry { id: string; name: string; found: string | null; used: boolean }
export interface JournalData { quests: QuestEntry[]; selected?: string; pinned?: string; canPin: boolean; reading?: string; keys?: KeyEntry[] }

/** The party's key ring, as a list: only shown once the party holds a key. */
export function KeyRing({ keys }: { keys?: KeyEntry[] }) {
  if (!keys?.length) return null;
  return <section class="tx-keyring" aria-label="Keys"><h3>Keys</h3><ul>
    {keys.map(k => <li key={k.id}><span class="tx-keyring-icon" aria-hidden="true">⚷</span><span><b>{k.name}</b>
      <small>{[k.found ? `Found: ${k.found}` : null, k.used ? "Used" : "Not used yet"].filter(Boolean).join(" · ")}</small></span></li>)}
  </ul></section>;
}
type Send = CustomScreenProps["send"];

export function Journal({ journal, send, shared = false, onClose, initial }: {
  journal: JournalData; send: Send; shared?: boolean; onClose?: () => void; initial?: string;
}) {
  const [selection, setSelection] = useState(initial ?? journal.selected ?? journal.quests[0]?.id);
  const sfx = useSfx();
  const selected = journal.quests.find(q => q.id === (shared && journal.canPin ? journal.selected : selection)) ?? journal.quests[0];
  const close = () => { sfx("questlogClose"); if (shared && journal.canPin) send("questLog", { action: "close" }); else onClose?.(); };
  return <section class="tx-journal" aria-label="Quest journal">
    <header class="tx-journal-cover"><span aria-hidden="true">✦</span><h2>Wayfarer's Journal</h2>
      {!shared || journal.canPin ? <button type="button" class="tx-btn ghost" onClick={close}>Close</button> : null}</header>
    {shared && journal.reading ? <p class="tx-journal-reading">{journal.canPin ? "Open on the TV · Your selections guide the room" : `${journal.reading} is reading the quest log`}</p> : null}
    <div class="tx-journal-pages"><nav aria-label="Quests">{(["active", "done", "failed"] as const).map(status => {
      const entries = journal.quests.filter(q => q.status === status);
      return entries.length ? <section key={status}><h3>{status === "active" ? "Active" : status === "done" ? "Done" : "Failed"}</h3>
        {entries.map(q => <button type="button" class="tx-journal-card" aria-pressed={selected?.id === q.id} key={q.id}
          onClick={() => { sfx("select"); setSelection(q.id); if (shared && journal.canPin) send("questLog", { action: "select", quest: q.id }); }}>
          <span class={`tx-seal ${q.status}`} aria-label={q.status}>{q.status === "done" ? "✓" : q.status === "failed" ? "×" : "✦"}</span>
          <span><b>{q.name}</b><small>{q.objective}</small></span>
          {journal.pinned === q.id ? <span class="tx-pin-ribbon" aria-label="Pinned">◆</span> : null}</button>)}</section> : null;
    })}<KeyRing keys={journal.keys} /></nav>
    {selected ? <article class="tx-journal-entry" aria-label={selected.name}>
      <div class="tx-journal-giver">{selected.portrait ? <MoodPortrait preset={selected.portrait} mood="idle" name={selected.giver} size={80} /> : null}
        <div><span class="tx-eyebrow">{selected.giver}</span><h2>{selected.name}</h2></div></div>
      <ol class="tx-journal-history">{selected.history.map((h,i) => <li class={h.done ? "done" : "current"} key={i}>
        {h.objective && !h.done ? <strong>{h.objective}</strong> : null}<p>{h.log}</p></li>)}</ol>
      {selected.result ? <p class="tx-journal-result">{selected.result}</p> : null}
      {selected.rewards ? <div class="tx-journal-rewards"><b>Rewards received</b><p>{[
        selected.rewards.gold ? `${selected.rewards.gold} gold` : null, selected.rewards.xp ? `${selected.rewards.xp} XP per member` : null,
        ...selected.rewards.items.map(i => `${i.count} × ${i.name}`),
      ].filter(Boolean).join(" · ")}</p></div> : null}
      {journal.canPin && selected.status === "active" ? <button type="button" class="tx-btn gold" aria-pressed={journal.pinned === selected.id}
        onClick={() => { sfx("questlogPin"); send("questLog", { action: journal.pinned === selected.id ? "unpin" : "pin", quest: selected.id }); }}>
        {journal.pinned === selected.id ? "Unpin quest" : "Pin quest"}</button> : null}
    </article> : <p class="tx-journal-empty">Every journey begins with a question.<br />Speak to the people of Ashen Ford to find work.</p>}</div>
    {!shared && journal.canPin ? <button type="button" class="tx-btn gold" onClick={() => { sfx("questlogOpen"); send("questLog", { action: "open", quest: selected?.id }); }}>Open on TV</button> : null}
  </section>;
}

export function QuestLog({ data, send }: CustomScreenProps) {
  const journal = data as JournalData;
  const [privateView, setPrivateView] = useState(false);
  if (!journal.canPin && !journal.reading) return <div class="tx tx-questlog"><Journal journal={journal} send={send} /></div>;
  return <div class="tx tx-questlog">{journal.canPin ? <Journal journal={journal} send={send} shared /> : <>
    <p class="tx-journal-reading">{journal.reading ?? "The leader"} is reading the quest log</p>
    <button type="button" class="tx-btn gold" onClick={() => setPrivateView(v => !v)}>{privateView ? "Return to room view" : "Read privately"}</button>
    {privateView ? <Journal journal={{ ...journal, canPin: false }} send={send} onClose={() => setPrivateView(false)} /> : null}
  </>}</div>;
}

export interface QuestNoticeData { id: string; name: string; kind: string; objective: string; canPin: boolean; trinket?: Trinket }
function QuestNotice({ notice, send, read, stop }: { notice: QuestNoticeData; send: Send; read: (quest: string) => void; stop: () => void }) {
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const held = useRef(false); const sfx = useSfx();
  const cancel = () => { if (timer.current !== null) clearTimeout(timer.current); timer.current = null; };
  useEffect(() => cancel, []);
  useEffect(() => { if (notice.trinket) stop(); }, [notice.id, notice.trinket?.id]);
  const label = notice.kind === "start" ? "Quest started" : notice.kind === "update" ? "Quest updated" : notice.kind === "complete" ? "Quest complete" : "Quest failed";
  return <div role="status"><button type="button" class="tx-tracker-chip tx-quest-notice" onContextMenu={e => e.preventDefault()}
    onPointerDown={() => { cancel(); held.current = false; if (notice.canPin) timer.current = setTimeout(() => {
      held.current = true; sfx("questlogPin"); send("questLog", { action: "pin", quest: notice.id });
    }, 600); }} onPointerUp={cancel} onPointerCancel={cancel} onPointerLeave={cancel}
    onClick={() => { if (!held.current) read(notice.id); held.current = false; }}>
      <span aria-hidden="true">✦</span><span><span class="tx-quest-notice-label">{label}{notice.canPin ? " · Hold to pin" : ""}</span>
      <b>{notice.name}</b>{notice.kind === "start" || notice.kind === "update" ? <small>{notice.objective}</small> : null}</span>
    </button>{notice.trinket ? <div class="tx-trinket-presented"><span class="tx-eyebrow">Presented to you</span><TrinketSlot key={`${notice.id}:${notice.trinket.id}`} trinket={notice.trinket} presented /></div> : null}</div>;
}

export function WalkJournal({ journal, pinned, notice, send, stop }: {
  journal: JournalData; pinned?: { id: string; name: string; objective: string }; notice?: QuestNoticeData; send: Send; stop: () => void;
}) {
  const [open, setOpen] = useState(false); const [initial, setInitial] = useState<string>();
  const read = (quest?: string) => { stop(); setInitial(quest); setOpen(true); };
  return <div class="tx-walk-journal">
    {notice ? <QuestNotice notice={notice} send={send} read={read} stop={stop} /> : null}
    {pinned && notice?.id !== pinned.id ? <button type="button" class="tx-tracker-chip" onClick={() => read(pinned.id)}><span aria-hidden="true">◆</span><span><b>{pinned.name}</b><small>{pinned.objective}</small></span></button> : null}
    <button type="button" class="tx-btn ghost" onClick={() => { stop(); if (journal.canPin) send("questLog", { action: "open" }); else read(); }}>{journal.canPin ? "Quest log" : "Read quest journal"}</button>
    {open ? <div class="tx-journal-modal" role="dialog" aria-modal="true" aria-label="Private quest journal" onKeyDown={e => { if (e.key === "Escape") setOpen(false); }}>
      <Journal journal={journal} send={send} initial={initial} onClose={() => setOpen(false)} /></div> : null}
  </div>;
}
