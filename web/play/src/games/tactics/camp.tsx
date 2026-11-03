import { useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { Sheet } from "./details";
import { Check } from "./story";
import { RecoveryMenu, RecoveryParty, type RecoveryMember } from "./recovery";

interface CampData {
  stage: string; mode?: string; job?: string; canPick: boolean; leader: boolean; vip: boolean; you: string;
  jobs: { id: string; name: string; skill: string; dc: number; can?: boolean }[];
  targets: { member: string; name: string; injury: string; label: string; detail: string; kind?: string; can: boolean; selected: boolean }[];
  party: { member: string; name: string; job?: string }[];
  members?: RecoveryMember[];
  result?: { member: string; job: string; skill: string; die: number; secondDie?: number; bonus: number; dc: number; passed: boolean; text: string };
}
const shortJob: Record<string, string> = { tend: "Tend", rally: "Rally", cook: "Cook", watch: "Watch", rest: "Rest" };

/** A short job row above the shared party cards; injury details and treatment open on tap. */
export function Camp(props: CustomScreenProps) {
  const { data, send } = props;
  const d = data as CampData;
  const [help, setHelp] = useState(false);
  const result = d.result;
  const fallback = d.party.map(p => ({ ...p, class: "", here: true, hp: 0, maxHp: 0, resources: [], wellFed: false, room: false,
    injuries: d.targets.filter(t => t.member === p.member).map(t => ({ id: t.injury, name: t.label, kind: t.kind ?? "body", detail: t.detail })) }));
  const members = d.members ?? fallback;
  const rolled = d.stage === "results" && result && result.die > 0;
  const partyCards = <RecoveryParty members={members} you={d.you} status={m => d.party.find(p => p.member === m.member)?.job ? shortJob[d.party.find(p => p.member === m.member)!.job!] : undefined}
      injuryAction={(m, i) => {
        const target = d.targets.find(t => t.member === m.member && t.injury === i.id);
        return d.stage === "jobs" && target ? { label: !target.can ? "Being tended" : target.selected ? "Selected" : `Help ${m.name}`,
          disabled: !d.canPick || !target.can, run: () => send("campJob", { job: d.job, target: m.member, injury: i.id }) } : undefined;
      }} />;
  return <section class={`tx tx-camp tx-recovery${rolled ? " tx-recovery-roll" : ""}`}>
    {d.vip && <RecoveryMenu keep={d.mode === "inn" ? "Stay at the inn" : "Stay by the fire"} send={send} />}
    <header><span class="tx-eyebrow">{d.mode === "inn" ? "At the inn" : "By the fire"}</span><h1>{d.stage === "dawn" ? "A new dawn" : rolled ? "A moment by the fire" : d.stage === "sleep" ? "A quiet night" : d.stage === "results" ? "A quiet moment" : "Make yourself useful"}</h1></header>
    {d.stage === "jobs" ? <>
      <div class="tx-camp-jobs" aria-label="Camp jobs">{d.jobs.map(j => <button class="tx-btn" aria-label={j.name} aria-pressed={d.job === j.id} disabled={!d.canPick || j.can === false}
        onClick={() => send("campJob", { job: j.id })} key={j.id}><span class="tx-job-disc" aria-hidden="true">{shortJob[j.id]?.[0]}</span>{shortJob[j.id] ?? j.name}</button>)}</div>
      <div class="tx-recovery-section"><span>{d.job === "tend" || d.job === "rally" ? "Tap an injury to help" : "Party · battle ready"}</span><button class="tx-link" onClick={() => setHelp(true)}>About jobs</button></div>
    </> : rolled ? <Check {...props} data={{ skill: result.skill, dc: result.job === "watch" ? null : result.dc, title: result.text,
      cards: [{ id: result.member, name: d.party.find(p => p.member === result.member)?.name ?? result.member, player: "", cls: "", bonus: result.bonus,
        note: result.secondDie ? `Advantage: ${result.die}, ${result.secondDie}` : "Camp check", here: true }], mine: null, canChoose: false, tie: false, decider: null, tied: [], vip: false,
      roll: { member: result.member, die: Math.max(result.die, result.secondDie ?? result.die), bonus: result.bonus,
        total: Math.max(result.die, result.secondDie ?? result.die) + result.bonus, dc: result.job === "watch" ? null : result.dc, success: result.passed } }} />
      : <p class="tx-recovery-result" role="status">{d.stage === "dawn" ? "Rest injuries mended. Ready for the road." : d.stage === "sleep" ? "The party settles down to sleep." : result?.text ?? "A quiet night."}</p>}
    {rolled ? <details class="tx-recovery-roll-party"><summary>Party status · {members.length} members</summary>{partyCards}</details> : partyCards}
    {d.stage !== "jobs" && d.leader && <button class="tx-btn ghost tx-recovery-continue" onClick={() => send("campNext")}>Continue</button>}
    {help && <Sheet title="Camp jobs" eyebrow="Your own character rolls" onClose={() => setHelp(false)}><p>Tend: Medicine DC 12, mend a body injury. Healing spells grant advantage.</p>
      <p>Rally: your better social skill, DC 12, mend a spirit injury. Cook: Survival DC 10, give everyone 3 temporary HP next battle.</p>
      <p>Watch: Perception, find nearby secrets. Rest easy: no check. Leave a job unchosen to rest easy.</p></Sheet>}
  </section>;
}
