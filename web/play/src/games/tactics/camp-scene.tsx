import type { JSX } from "preact";
import { usePackBase } from "./pack";
import type { RecoveryMember } from "./recovery";

export type CampSceneData = { stage: string; mode?: string; members: Pick<RecoveryMember, "member" | "class" | "here">[]; party?: Guest[]; you?: string };

type Guest = { member: string; job?: string | null };

/** A decorative window onto the party's real rest. */
export function CampScene({ stage, mode, members, party = [], you }: CampSceneData) {
  const base = usePackBase();
  const dawn = stage === "dawn", inn = mode === "inn";
  const guests = members.filter(m => m.here || m.member === you).slice().sort((a, b) => Number(b.member === you) - Number(a.member === you)).slice(0, 6);
  const style = base ? {
    "--fire-0": `url('${base}camp/fire_0.png')`, "--fire-1": `url('${base}camp/fire_1.png')`,
    "--fire-2": `url('${base}camp/fire_2.png')`, "--fire-3": `url('${base}camp/fire_3.png')`,
    "--fire-4": `url('${base}camp/fire_4.png')`, "--embers": `url('${base}camp/fire_unlit.png')`,
    "--tent": `url('${base}camp/shelter.png')`, "--pot": `url('${base}camp/pot_0.png')`,
    "--hearth": `url('${base}camp/hearth.png')`, "--tree": `url('${base}props/tree_autumn.png')`,
    "--bush": `url('${base}props/bush_green.png')`,
  } as JSX.CSSProperties : undefined;
  const pos = [[32, 73], [70, 73], [19, 80], [82, 80], [7, 82], [93, 82]];
  const clips: Record<string, [number, number, string]> = { sit: [102, 4, "1.33s"], doze: [106, 2, "1s"], idle: [0, 6, "1.2s"], item: [82, 5, "1.1s"], kneel: [165, 3, "1.1s"], talk: [108, 4, ".8s"] };
  return <div class={`tx-camp-scene${dawn ? " dawn" : ""}${inn ? " inn" : ""}${stage === "sleep" ? " sleep" : ""}`} style={style} aria-hidden="true">
    <div class="camp-sky" /><div class="camp-moon" /><div class="camp-sun" />
    <div class="camp-stars">{Array.from({ length: 12 }, (_, i) => <i key={i} style={{ left: `${5 + i * 8}%`, top: `${5 + (i * 19 % 42)}px`, animationDelay: `${i * -.3}s` }} />)}</div>
    <div class="camp-hills" />{!inn && <><div class="camp-tree left" /><div class="camp-tree right" /></>}<div class="camp-ground" />
    {!inn && <><div class="camp-tent" /><div class="camp-glow" /><div class="camp-flame" />
      {party.some(p => p.job === "cook") && stage !== "sleep" && !dawn && <div class="camp-pot" />}</>}
    {guests.map((m, i) => {
      const job = party.find(p => p.member === m.member)?.job;
      const anim = stage === "sleep" ? "doze" : dawn ? "idle" : stage === "talk" ? "sit" : job === "cook" ? "item" : job === "tend" ? "kneel" : job === "rally" ? "talk" : "sit";
      const [start, frames, speed] = clips[anim]!;
      const cls = /^[a-z_]+$/.test(m.class) ? m.class : "fighter";
      const sprite = { left: `${pos[i]![0]}%`, top: `${pos[i]![1]}%`, "--sprite": base ? `url('${base}characters/presets/class_${cls}.png')` : "none",
        "--first": `${-start * 32}px`, "--last": `${-(start + frames) * 32}px`, "--frames": frames, "--speed": speed, "--delay": `${i * -.21}s` } as JSX.CSSProperties;
      return <div key={m.member} class="camp-person" style={sprite}>
        {m.member === you && <i class="camp-heart">♥</i>}<span class="camp-sprite" />{stage === "sleep" && <i class="camp-zzz">z</i>}
      </div>;
    })}
    {!inn && <><div class="camp-bush left" /><div class="camp-bush right" /></>}
    <div class="camp-sparks">{Array.from({ length: 5 }, (_, i) => <i key={i} style={{ left: `${46 + i * 2}%`, animationDelay: `${i * -.6}s` }} />)}</div>
  </div>;
}
