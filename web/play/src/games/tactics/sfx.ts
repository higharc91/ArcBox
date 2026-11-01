import { useEffect } from "preact/hooks";
import { useFeedback, type Feedback } from "../../feedback/feedback";
import { packBase, packJson } from "./pack";

/**
 * Wardenflame's UI sounds from the content pack (`sfx/manifest.json`, Ogg Vorbis) and the phone's vibration, for the
 * moments that matter: your turn, a reaction to decide, picking, confirming and backing out. They follow the app's
 * speaker toggle (Feedback): muted, or with no Feedback at all (tests, the dev board), nothing plays or buzzes. Audio
 * starts only after the first tap or key press on the page, as browsers require; a browser that can't decode Ogg just
 * stays quiet.
 */

export type Cue = "select" | "confirm" | "cancel" | "yourTurn" | "reaction" | "dice";

const SOUND: Record<Cue, string> = {
  select: "ui_select", confirm: "ui_confirm", cancel: "ui_cancel", yourTurn: "ui_your_turn", reaction: "ui_reaction_prompt",
  dice: "ui_dice_roll",
};
/** Vibration patterns (ms) for the cues worth feeling with the phone in a pocket. */
const BUZZ: Partial<Record<Cue, number[]>> = { yourTurn: [60, 80, 120], reaction: [120, 60, 120] };

interface SfxManifest { sounds: Record<string, { files: string[] }> }

let ctx: AudioContext | null = null;
const buffers = new Map<string, Promise<AudioBuffer | null>>();
let unlockInstalled = false;

/** Creates (or resumes) the audio context inside the page's first gesture, then keeps it running on later ones. */
function installUnlock(): void {
  if (unlockInstalled || typeof document === "undefined") return;
  unlockInstalled = true;
  const unlock = () => {
    try {
      const Context = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
      if (!Context) return;
      ctx ??= new Context();
      if (ctx.state !== "running") void ctx.resume().catch(() => {});
    } catch {
      // Audio is decoration.
    }
  };
  for (const type of ["pointerdown", "touchend", "keydown"]) document.addEventListener(type, unlock, { capture: true, passive: true });
}

function buffer(id: string): Promise<AudioBuffer | null> {
  let pending = buffers.get(id);
  if (!pending) {
    pending = (async () => {
      const [base, manifest] = await Promise.all([packBase(), packJson<SfxManifest>("sfx/manifest.json")]);
      const files = manifest?.sounds[id]?.files;
      if (!base || !files?.length || !ctx) return null;
      const file = files[Math.floor(Math.random() * files.length)]!;
      const data = await (await fetch(`${base}sfx/${file}`)).arrayBuffer();
      return await ctx.decodeAudioData(data);
    })().catch(() => null);
    pending.then((b) => { if (!b) buffers.delete(id); });   // not ready yet, or failed: try again next time
    buffers.set(id, pending);
  }
  return pending;
}

function play(feedback: Feedback | null, cue: Cue): void {
  if (!feedback || feedback.muted) return;
  const buzz = BUZZ[cue];
  if (buzz) {
    try {
      navigator.vibrate?.(buzz);
    } catch {
      // Not supported.
    }
  }
  if (!ctx || ctx.state !== "running") return;
  void buffer(SOUND[cue]).then((b) => {
    if (!b || !ctx || feedback.muted) return;
    try {
      const src = ctx.createBufferSource();
      const gain = ctx.createGain();
      gain.gain.value = 0.7;
      src.buffer = b;
      src.connect(gain).connect(ctx.destination);
      src.start();
    } catch {
      // ignore
    }
  });
}

/** `cue(name)` plays a UI sound (and buzzes for the alerts) unless the phone is muted. */
export function useSfx(): (cue: Cue) => void {
  const feedback = useFeedback();
  useEffect(() => {
    if (!feedback) return;
    installUnlock();
    // Warm the cache once audio can run, so the first cue isn't late.
    if (ctx) for (const id of Object.values(SOUND)) void buffer(id);
  }, [feedback]);
  return (cue) => play(feedback, cue);
}

let lastAlert = { cue: "", at: 0 };

/** Plays an alert once when a screen appears; a remount within a few seconds (a reconnect re-sends the view) stays quiet. */
export function useAlertOnMount(cue: "yourTurn" | "reaction"): void {
  const sfx = useSfx();
  useEffect(() => {
    const now = Date.now();
    if (lastAlert.cue === cue && now - lastAlert.at < 4000) return;
    lastAlert = { cue, at: now };
    sfx(cue);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
}
