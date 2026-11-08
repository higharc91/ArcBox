import { useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { Sheet } from "./details";
import { Check } from "./story";
import { CampScene } from "./camp-scene";
import { Sprite, useIcons } from "./art";
import { usePackBase } from "./pack";
import { RecoveryMenu, RecoveryParty, type RecoveryMember } from "./recovery";

interface CampData {
  stage: string; mode?: string; job?: string; canPick: boolean; leader: boolean; vip: boolean; difficulty?: string; you: string;
  jobs: { id: string; name: string; skill: string; dc: number; can?: boolean }[];
  targets: { member: string; name: string; injury: string; label: string; detail: string; kind?: string; can: boolean; selected: boolean }[];
  party: { member: string; name: string; job?: string }[];
  members?: RecoveryMember[];
  result?: { member: string; job: string; skill: string; die: number; secondDie?: number; bonus: number; dc: number; passed: boolean; text: string };
}
const jobArt: Record<string, string> = { tend: "heal", rally: "inspire", cook: "food", watch: "torch", rest: "second_wind" };
const shortJob: Record<string, string> = { tend: "Tend", rally: "Rally", cook: "Cook", watch: "Watch", rest: "Rest" };
const jobEffect: Record<string, string> = {
  tend: "Mend one body injury on a successful roll.",
  rally: "Mend one spirit injury on a successful roll.",
  cook: "Give everyone 3 temporary HP next battle on a successful roll.",
  watch: "Look for nearby secrets.",
  rest: "Rest with the party; no extra job.",
};
const jobCheck = (j: CampData["jobs"][number]) => j.id === "rest" ? "No roll"
  : `${j.skill.replaceAll("_", " ").replace(/^./, c => c.toUpperCase())} · ${j.dc > 0 ? `roll ${j.dc}+` : "varies by secret"}`;

/** A short job row above the shared party cards; injury details and treatment open on tap. */
export function Camp(props: CustomScreenProps) {
  const { data, send } = props;
  const base = usePackBase();
  const icons = useIcons();
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
    {d.vip && <RecoveryMenu keep={d.mode === "inn" ? "Stay at the inn" : "Stay by the fire"} difficulty={d.difficulty} send={send} />}
    <header><span class="tx-eyebrow">{d.mode === "inn" ? "At the inn" : "By the fire"}</span><h1>{d.stage === "dawn" ? "A new dawn" : rolled ? "A moment by the fire" : d.stage === "sleep" ? "A quiet night" : d.stage === "results" ? "A quiet moment" : "Make yourself useful"}</h1></header>
    <CampScene stage={d.stage} mode={d.mode} members={members} party={d.party} you={d.you} />
    {d.stage === "jobs" ? <>
      <div class="tx-camp-jobs" aria-label="Camp jobs">{d.jobs.map(j => <button class="tx-btn" aria-label={j.name} aria-describedby={`camp-job-${j.id}`} aria-pressed={d.job === j.id} disabled={!d.canPick || j.can === false}
        onClick={() => send("campJob", { job: j.id })} key={j.id}><span class="tx-job-disc" aria-hidden="true">{base && icons?.icons[jobArt[j.id] ?? ""]
          ? <Sprite src={`${base}icons/${icons.icons[jobArt[j.id]!]!.atlas}`} rect={icons.icons[jobArt[j.id]!]!.rect} /> : shortJob[j.id]?.[0]}</span>
          <span class="tx-job-text"><b>{j.name}</b><span id={`camp-job-${j.id}`}><span>{jobEffect[j.id] ?? j.name}</span><small>{jobCheck(j)}</small></span></span></button>)}</div>
      <div class="tx-recovery-section"><span>{d.job === "tend" || d.job === "rally" ? "Tap an injury to help" : "Party · battle ready"}</span><button class="tx-link" onClick={() => setHelp(true)}>About jobs</button></div>
    </> : rolled ? <Check {...props} data={{ skill: result.skill, dc: result.job === "watch" ? null : result.dc, title: result.text,
      cards: [{ id: result.member, name: d.party.find(p => p.member === result.member)?.name ?? result.member, player: "", cls: "", bonus: result.bonus,
        note: result.secondDie ? `Advantage: ${result.die}, ${result.secondDie}` : "Camp check", here: true }], mine: null, canChoose: false, tie: false, decider: null, tied: [], vip: false,
      roll: { member: result.member, die: Math.max(result.die, result.secondDie ?? result.die), bonus: result.bonus,
        total: Math.max(result.die, result.secondDie ?? result.die) + result.bonus, dc: result.job === "watch" ? null : result.dc, success: result.passed } }} />
      : <p class="tx-recovery-result" role="status">{d.stage === "dawn" ? "Rest injuries mended. Ready for the road." : d.stage === "sleep" ? "The party settles down to sleep." : result?.text ?? "A quiet night."}</p>}
    {rolled ? <details class="tx-recovery-roll-party"><summary>Party status · {members.length} members</summary>{partyCards}</details> : partyCards}
    {d.stage !== "jobs" && d.leader && <button class="tx-btn ghost tx-recovery-continue" onClick={() => send("campNext")}>Continue</button>}
    {help && <Sheet title="Camp jobs" eyebrow="Your own character rolls" onClose={() => setHelp(false)}>
      <p>Add your skill bonus to the roll. Reach the number shown on the job to succeed.</p>
      <p>Tend mends a body injury. If you know a healing spell, roll twice and keep the higher result.</p>
      <p>Rally mends a spirit injury using your better social skill. Cook gives everyone 3 temporary HP for the next battle on a successful roll. Temporary HP absorb damage before your normal HP.</p>
      <p>Watch uses Perception to look for nearby secrets; each secret has its own target number.</p>
      <p>Rest needs no roll. Choose Rest, or leave your job unchosen, to skip an extra job and recover with the party at dawn.</p></Sheet>}
  </section>;
}
