import { useState } from "preact/hooks";
import type { ComponentChildren, JSX } from "preact";
import { InjuryIcon, Portrait } from "./art";
import { Sheet } from "./details";
import { HostMenu } from "./walk";
import type { CustomScreenProps } from "../module";

export interface RecoveryInjury { id: string; name: string; kind: string; detail: string }
export interface RecoveryMember {
  member: string; name: string; class: string; here: boolean; hp: number; maxHp: number;
  resources: { name: string; current: number; max: number }[];
  injuries: RecoveryInjury[]; wellFed: boolean; room: boolean;
}
export const resourceName = (name: string) => name.replaceAll("_", " ").replace(/\b\w/g, c => c.toUpperCase());

function Meter({ name, label, current, max, resource = false }: { name: string; label: string; current: number; max: number; resource?: boolean }) {
  return <div class={`tx-recovery-meter ${resource ? "resource" : "health"}`}>
    <span>{label}<b>{current}/{max}</b></span>
    <div role="progressbar" aria-label={name} aria-valuemin={0} aria-valuemax={max} aria-valuenow={current}
      style={{ "--fill": `${max > 0 ? Math.max(0, Math.min(100, current / max * 100)) : 0}%` } as JSX.CSSProperties}><i /></div>
  </div>;
}

/** Small party cards; injuries disclose details on tap rather than repeating their text in every row. */
export function RecoveryParty({ members, you, status, action, injuryAction }: {
  members: RecoveryMember[]; you?: string;
  status?(member: RecoveryMember): string | undefined;
  action?(member: RecoveryMember): ComponentChildren;
  injuryAction?(member: RecoveryMember, injury: RecoveryInjury): { label: string; disabled?: boolean; run(): void } | undefined;
}) {
  const [opened, setOpened] = useState<{ member: RecoveryMember; injury: RecoveryInjury } | null>(null);
  const treatment = opened ? injuryAction?.(opened.member, opened.injury) : undefined;
  return <><div class="tx-recovery-party" aria-label="Party status">{members.map(m => <article class={`tx-recovery-card${m.member === you ? " you" : ""}`} key={m.member}>
    <Portrait unit={{ name: m.name, hp: m.hp, maxHp: m.maxHp, side: "Party", sprite: null }} cls={m.class} size={40} tone={m.member === you ? "you" : "ally"} />
    <div class="tx-recovery-card-main">
      <div class="tx-recovery-name"><div class="tx-recovery-identity"><strong>{m.name}</strong><small>{status?.(m) ?? (!m.here ? "Away" : resourceName(m.class))}</small></div>
      {action?.(m)}</div>
      {m.maxHp > 0 && <Meter name={`${m.name} HP`} label="HP" current={m.hp} max={m.maxHp} />}
      <div class="tx-recovery-resources">{m.resources.map(r => <Meter key={r.name} name={`${m.name} ${resourceName(r.name)}`} label={resourceName(r.name)} current={r.current} max={r.max} resource />)}</div>
      {(m.injuries.length > 0 || m.wellFed) && <div class="tx-recovery-effects">
        {m.injuries.map(i => <button type="button" class={`tx-injury-button ${i.kind}`} aria-label={`${m.name}: ${i.name}`}
          onClick={() => setOpened({ member: m, injury: i })} key={i.id}><InjuryIcon id={i.id} /><span>{i.name}</span></button>)}
        {m.wellFed && <span class="tx-recovery-fed"><span aria-hidden="true">♨</span><span><b>Well fed</b><small>3 temporary HP next battle</small></span></span>}
      </div>}

    </div>
  </article>)}</div>
    {opened && <Sheet title={opened.injury.name} eyebrow={opened.member.name} onClose={() => setOpened(null)}>
      <p>{opened.injury.detail}</p>
      {treatment && <button class="tx-btn gold" disabled={treatment.disabled} onClick={() => { treatment.run(); setOpened(null); }}>{treatment.label}</button>}
    </Sheet>}
  </>;
}

/** Host controls are available without taking a row from every recovery screen. */
export function RecoveryMenu({ send, keep, difficulty }: { send: CustomScreenProps["send"]; keep: string; difficulty?: string }) {
  return <details class="tx-recovery-menu"><summary aria-label="Session options">•••</summary><HostMenu canBattle={false} keep={keep} difficulty={difficulty} send={send} /></details>;
}
