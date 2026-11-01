import { useEffect, useRef, useState } from "preact/hooks";
import { packBase, packJson, usePackBase, usePackJson } from "../games/tactics/pack";
import type { FrameInfo } from "../games/tactics/world/flyover";
import { TitleMusic } from "./music";

const LOGO = { w: 278, h: 108 };
const GLEAM = { frames: 14, fps: 14 };

export interface TitleProps {
  room: string | null;
  frozenAt?: number;
  /** Show this frame of the logo gleam (screenshots). */
  gleamFrame?: number;
  /** The address phones type, shown with the room code. Defaults to this page's host. */
  joinHost?: string;
  reducedMotion?: boolean;
}

declare global {
  interface Window { __titleReady?: boolean }
}

interface Regions { regions: { id: string; name: string; chapter: number }[] }

/** The start screen: the flyover tour filling the screen, the logo over the sky, the region caption and the prompt. */
export function TitleScreen({ room, frozenAt, gleamFrame, joinHost, reducedMotion = false }: TitleProps) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const caption = useRef<HTMLDivElement>(null);
  const base = usePackBase();
  const regions = usePackJson<Regions>("lore/regions.json");
  const names = useRef(new Map<string, { name: string; chapter: number }>());
  names.current = new Map(regions?.regions.map((r) => [r.id, { name: r.name, chapter: r.chapter }]) ?? []);
  const logoScale = useLogoScale();
  const [failed, setFailed] = useState(false);
  const shownArea = useRef("");
  const still = frozenAt ?? (reducedMotion ? 6 : undefined);

  useEffect(() => {
    let live = true;
    let dispose: (() => void) | null = null;
    void (async () => {
      const { Flyover } = await import("../games/tactics/world/flyover");
      if (!live || !canvas.current) return;
      const fly = await Flyover.create({
        canvas: canvas.current,
        frozenAt: still,
        tour: !reducedMotion,
        onFrame: (f) => paintCaption(caption.current, f, names.current, shownArea),
      });
      if (!live) { fly?.dispose(); return; }
      if (!fly) { setFailed(true); return; }
      dispose = () => fly.dispose();
      const ok = await fly.ready;
      if (!ok) setFailed(true);
      fly.play();
      window.__titleReady = true;
    })();
    return () => { live = false; dispose?.(); };
  }, [still, reducedMotion]);

  const host = joinHost ?? location.host;
  return (
    <div class={`wf-title${room ? " wf-title--room" : ""}`}>
      <div class="wf-title__world"><canvas ref={canvas} /></div>
      {failed && <div class="wf-title__fallback" />}
      <header class="wf-title__logo">
        {base && <Logo base={base} scale={logoScale} frame={gleamFrame} animate={!reducedMotion && frozenAt === undefined} />}
      </header>
      <div class="wf-title__caption" ref={caption} style={{ opacity: 0 }}>
        <p class="wf-title__chapter" />
        <h2 class="wf-title__region" />
        <p class="wf-title__place" />
      </div>
      <footer class={`wf-title__band${room ? " wf-title__band--room" : ""}`}>
        <p class="wf-title__start">Press start</p>
        {room && (
          <p class="wf-title__join">
            Join at <b class="wf-title__host">{host}</b> with code <b class="wf-title__code">{room}</b>
          </p>
        )}
      </footer>
      <SoundToggle />
    </div>
  );
}

const ROMAN = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"];

/** Writes the caption straight into the DOM: it changes every frame while fading, which isn't worth a re-render. */
function paintCaption(
  el: HTMLDivElement | null, f: FrameInfo, names: Map<string, { name: string; chapter: number }>, shown: { current: string },
): void {
  if (!el) return;
  const key = f.area.map;
  if (shown.current !== key) {
    shown.current = key;
    const r = names.get(f.area.region);
    const chapter = r?.chapter ?? f.area.chapter;
    el.children[0]!.textContent = `Chapter ${ROMAN[chapter] ?? chapter}`;
    el.children[1]!.textContent = r?.name ?? f.area.caption;
    el.children[2]!.textContent = f.area.place ?? "";
  }
  el.style.opacity = f.caption.toFixed(3);
  el.style.transform = `translateY(${((1 - f.caption) * 0.8).toFixed(3)}vh)`;
}

/**
 * The logo at a whole multiple of its pixels, on a canvas so the gleam strip can play over it: every 5–8 s the
 * 14 frames run at 14 fps (title manifest `logo_gleam`), then it rests on logo.png.
 */
function Logo({ base, scale, frame, animate }: { base: string; scale: number; frame?: number; animate: boolean }) {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const c = ref.current;
    if (!c) return;
    const ctx = c.getContext("2d")!;
    ctx.imageSmoothingEnabled = false;
    let timer = 0;
    let live = true;
    const load = (src: string) => new Promise<HTMLImageElement | null>((res) => {
      const img = new Image();
      img.onload = () => res(img);
      img.onerror = () => res(null);
      img.src = src;
    });
    void Promise.all([load(`${base}title/logo.png`), load(`${base}title/logo_gleam.png`)]).then(([logo, gleam]) => {
      if (!live) return;
      const draw = (f: number | null) => {
        ctx.clearRect(0, 0, LOGO.w, LOGO.h);
        if (f !== null && gleam) ctx.drawImage(gleam, f * LOGO.w, 0, LOGO.w, LOGO.h, 0, 0, LOGO.w, LOGO.h);
        else if (logo) ctx.drawImage(logo, 0, 0);
      };
      if (frame !== undefined) return draw(Math.max(0, Math.min(GLEAM.frames - 1, frame)));
      draw(null);
      if (!animate || !gleam) return;
      const play = () => {
        let f = 0;
        const step = () => {
          if (!live) return;
          if (f < GLEAM.frames) {
            draw(f++);
            timer = window.setTimeout(step, 1000 / GLEAM.fps);
          } else {
            draw(null);
            timer = window.setTimeout(play, 5000 + Math.random() * 3000);
          }
        };
        step();
      };
      timer = window.setTimeout(play, 2500);
    });
    return () => { live = false; clearTimeout(timer); };
  }, [base, frame, animate]);
  return (
    <canvas
      ref={ref}
      width={LOGO.w}
      height={LOGO.h}
      role="img"
      aria-label="Wardenflame: Embers of the Vale"
      style={{ width: `${LOGO.w * scale}px`, height: `${LOGO.h * scale}px` }}
    />
  );
}

/** Browsers only start sound after a gesture: any key or tap starts the title theme; this pill says so and toggles it. */
function SoundToggle() {
  const music = useRef<TitleMusic | null>(null);
  const [on, setOn] = useState(false);
  useEffect(() => {
    let live = true;
    void packJson<{ tracks: Record<string, { files: string[] }> }>("music/manifest.json").then(async (m) => {
      const file = m?.tracks.title?.files[0];
      const prefix = await packBase();
      if (!live || !file || !prefix) return;
      music.current = new TitleMusic(`${prefix}music/${file}`);
    });
    const first = () => {
      if (music.current && !music.current.started) void music.current.play().then((ok) => setOn(ok));
    };
    window.addEventListener("pointerdown", first);
    window.addEventListener("keydown", first);
    return () => {
      live = false;
      window.removeEventListener("pointerdown", first);
      window.removeEventListener("keydown", first);
      music.current?.stop();
    };
  }, []);
  const toggle = (e: Event) => {
    e.stopPropagation();
    const m = music.current;
    if (!m) return;
    if (on) { m.stop(); setOn(false); } else void m.play().then(setOn);
  };
  return (
    <button type="button" class={`wf-title__sound${on ? " is-on" : ""}`} onPointerDown={toggle}>
      <span aria-hidden="true">♪</span> {on ? "Sound off" : "Sound on"}
    </button>
  );
}

/** The logo's CSS scale: a whole multiple of its pixels, about a third of the screen's height, never wider than it. */
function useLogoScale(): number {
  const calc = () => {
    const dpr = window.devicePixelRatio || 1;
    const byHeight = Math.round((window.innerHeight * dpr * 0.3) / LOGO.h);
    const byWidth = Math.floor((window.innerWidth * dpr * 0.88) / LOGO.w);
    return Math.max(1, Math.min(byHeight, byWidth)) / dpr;
  };
  const [s, setS] = useState(calc);
  useEffect(() => {
    const on = () => setS(calc());
    window.addEventListener("resize", on);
    return () => window.removeEventListener("resize", on);
  }, []);
  return s;
}
