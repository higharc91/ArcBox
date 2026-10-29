import { useId } from "preact/hooks";

// ---------------------------------------------------------------- colour

const HEX = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i;

/** A `#rgb` / `#rrggbb` colour, or undefined for anything else (it ends up in SVG attributes and CSS). */
export function safeColor(color?: string | null): string | undefined {
  return color && HEX.test(color) ? color : undefined;
}

function rgb(hex: string): [number, number, number] {
  const h = hex.length === 4 ? hex.replace(/^#(.)(.)(.)$/, "#$1$1$2$2$3$3") : hex;
  const n = parseInt(h.slice(1), 16);
  return [n >> 16, (n >> 8) & 255, n & 255];
}

/** `a` blended towards `b` by `t` (0..1). */
export function mix(a: string, b: string, t: number): string {
  const [ar, ag, ab] = rgb(a);
  const [br, bg, bb] = rgb(b);
  const c = (x: number, y: number) => Math.round(x + (y - x) * t).toString(16).padStart(2, "0");
  return `#${c(ar, br)}${c(ag, bg)}${c(ab, bb)}`;
}

// ---------------------------------------------------------------- geometry (viewBox 0 0 160 440)

/** The bottle's outline: lip, long neck, shoulders, body. */
const SILHOUETTE =
  "M61 26H99V38H96L96.5 120C97 135 98 142 98 150C100 185 136 196 136 238V412Q136 428 120 428H40Q24 428 24 412V238C24 196 60 185 62 150C62 142 63 135 63.5 120L64 38H61Z";

/** The foam's top at empty (hidden behind the label) and full (just under the foil). */
const LEVEL_EMPTY = 292;
const LEVEL_FULL = 116;
/** Where the champagne proper starts, under the foam. */
const LIQUID_Y = 300;

/** The foam line for a charge 0..1. */
export const foamLevel = (charge: number) => LEVEL_EMPTY - (LEVEL_EMPTY - LEVEL_FULL) * Math.min(1, Math.max(0, charge));

/** A tiny seeded generator, so the bubbles and the spray look the same on every render. */
function seeded(seed: number) {
  let s = seed >>> 0;
  return () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 2 ** 32;
  };
}

interface Bubble { x: number; r: number; dur: number; delay: number; drift: number }
const BUBBLES: Bubble[] = (() => {
  const rnd = seeded(7);
  return Array.from({ length: 22 }, (_, i) => ({
    x: 70 + rnd() * 20 + (i % 3 === 0 ? 0 : (rnd() - 0.5) * 70),
    r: 1.2 + rnd() * 2.6,
    dur: 1.6 + rnd() * 1.8,
    delay: -rnd() * 3.4,
    drift: (rnd() - 0.5) * 10,
  }));
})();

interface Drop { dx: number; dy: number; r: number; delay: number; dur: number; color: string }
interface Streak { x: number; y: number; w: number; delay: number; color: string }
const SPRAY_COLORS = ["#fffdf4", "#fff0b8", "#f7d36a", "#ffffff"];
const DROPS: Drop[] = (() => {
  const rnd = seeded(42);
  return Array.from({ length: 34 }, (_, i) => {
    const a = (rnd() - 0.5) * 1.35; // ±39° from straight up
    const d = 110 + rnd() * 230;
    return { dx: Math.sin(a) * d, dy: -Math.cos(a) * d, r: 1.6 + rnd() * 3.6, delay: rnd() * 0.16, dur: 0.75 + rnd() * 0.35, color: SPRAY_COLORS[i % SPRAY_COLORS.length]! };
  });
})();
interface Puff { dx: number; dy: number; r: number; delay: number; dur: number }
const PUFFS: Puff[] = (() => {
  const rnd = seeded(5);
  // Emitted one after another for half a second, each flying the whole way: together a gushing column of foam.
  return Array.from({ length: 32 }, (_, i) => {
    const a = (rnd() - 0.5) * 0.6; // ±17°
    const d = 200 + rnd() * 140;
    return { dx: Math.sin(a) * d, dy: -Math.cos(a) * d, r: 11 + rnd() * 12, delay: i * 0.017, dur: 0.7 + rnd() * 0.3 };
  });
})();
const STREAKS: Streak[] = (() => {
  const rnd = seeded(9);
  return Array.from({ length: 13 }, (_, i) => {
    const a = (rnd() - 0.5) * 1.1;
    const d = 230 + rnd() * 120;
    return { x: Math.sin(a) * d, y: -Math.cos(a) * d, w: 1.2 + rnd() * 1.6, delay: rnd() * 0.1, color: SPRAY_COLORS[(i + 1) % SPRAY_COLORS.length]! };
  });
})();

export type CorkState = "in" | "flying" | "reseat";

export interface Spray {
  id: number;
  /** 0..1: scales the burst (the server's spray strength is max(charge, 0.15)). */
  strength: number;
}

/** The burst out of the mouth: a column of foam, streaks and droplets on arcs. Restarts when its key (the spray id) changes. */
function SprayBurst({ strength, jet }: { strength: number; jet: string }) {
  const scale = 0.5 + 0.65 * Math.min(1, Math.max(0, strength));
  return (
    <g transform={`translate(80 24) scale(${scale.toFixed(3)})`}>
      <g class="spray">
        <path class="jet" d="M-6 0C-10-50-20-120-30-200Q0-214 30-200C20-120 10-50 6 0Z" fill={`url(#${jet})`} />
        {PUFFS.map((p, i) => (
          <circle
            key={`p${i}`}
            class="puff"
            r={p.r}
            style={{ "--dx": `${p.dx.toFixed(1)}px`, "--dy": `${p.dy.toFixed(1)}px`, animationDelay: `${p.delay}s`, animationDuration: `${p.dur}s` }}
          />
        ))}
        {STREAKS.map((s, i) => (
          <path key={`s${i}`} class="streak" d={`M0 0L${s.x.toFixed(1)} ${s.y.toFixed(1)}`} pathLength={100} stroke={s.color} stroke-width={s.w} style={{ animationDelay: `${s.delay}s` }} />
        ))}
        {DROPS.map((d, i) => (
          <circle
            key={`d${i}`}
            class="drop"
            r={d.r}
            fill={d.color}
            style={{ "--dx": `${d.dx.toFixed(1)}px`, "--dy": `${d.dy.toFixed(1)}px`, animationDelay: `${d.delay}s`, animationDuration: `${d.dur}s` }}
          />
        ))}
      </g>
    </g>
  );
}

interface BottleProps {
  /** The glass colour (team accent). */
  color: string;
  /** 0..1: the foam level and the bubbles. */
  charge: number;
  cork: CorkState;
  spray: Spray | null;
}

/**
 * A tall champagne bottle in the team's colour: tinted glass, foam that rises with the charge and fizzes, a gold foil
 * neck with a chequered band, a cork in its wire cage, and a REDLINE label. On a spray the cork flies and the
 * champagne bursts out of the mouth.
 */
export function Bottle({ color, charge, cork, spray }: BottleProps) {
  const uid = useId().replace(/[^a-zA-Z0-9_-]/g, "");
  const id = (name: string) => `${uid}-${name}`;
  const url = (name: string) => `url(#${id(name)})`;
  const level = foamLevel(charge);
  const glassDark = mix(color, "#05050a", 0.6);
  const glassEdge = mix(color, "#000000", 0.55);
  const bubbles = Math.round(5 + charge * (BUBBLES.length - 5));
  const crest = Array.from({ length: 12 }, (_, i) => 22 + i * 10.5);

  return (
    <svg class="bottle" viewBox="0 0 160 440" aria-hidden="true">
      <defs>
        <clipPath id={id("glass")}><path d={SILHOUETTE} /></clipPath>
        <clipPath id={id("below")}><rect x="0" y={level} width="160" height={440 - level} /></clipPath>
        <linearGradient id={id("shade")} x1="0" x2="1">
          <stop offset="0" stop-color={glassEdge} stop-opacity="0.95" />
          <stop offset="0.12" stop-color={glassEdge} stop-opacity="0.35" />
          <stop offset="0.3" stop-color="#ffffff" stop-opacity="0.08" />
          <stop offset="0.55" stop-color="#ffffff" stop-opacity="0" />
          <stop offset="0.82" stop-color="#000000" stop-opacity="0.3" />
          <stop offset="1" stop-color="#000000" stop-opacity="0.75" />
        </linearGradient>
        <linearGradient id={id("liquid")} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="#ffe9a0" />
          <stop offset="1" stop-color="#d9981c" />
        </linearGradient>
        <linearGradient id={id("foam")} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="#ffffff" />
          <stop offset="0.35" stop-color="#fff7de" />
          <stop offset="1" stop-color="#f8dc8e" />
        </linearGradient>
        <linearGradient id={id("foil")} x1="0" x2="1">
          <stop offset="0" stop-color="#6b4a0c" />
          <stop offset="0.18" stop-color="#c9992f" />
          <stop offset="0.38" stop-color="#fbe6a2" />
          <stop offset="0.48" stop-color="#fff8dc" />
          <stop offset="0.62" stop-color="#e2b84c" />
          <stop offset="0.85" stop-color="#9a6f16" />
          <stop offset="1" stop-color="#5a3d08" />
        </linearGradient>
        <linearGradient id={id("cyl")} x1="0" x2="1">
          <stop offset="0" stop-color="#000" stop-opacity="0.45" />
          <stop offset="0.2" stop-color="#000" stop-opacity="0.05" />
          <stop offset="0.4" stop-color="#fff" stop-opacity="0.18" />
          <stop offset="0.6" stop-color="#000" stop-opacity="0" />
          <stop offset="1" stop-color="#000" stop-opacity="0.5" />
        </linearGradient>
        <radialGradient id={id("cork")} cx="0.38" cy="0.3" r="0.8">
          <stop offset="0" stop-color="#f3d59e" />
          <stop offset="0.55" stop-color="#d4a05c" />
          <stop offset="1" stop-color="#8d5e26" />
        </radialGradient>
        <linearGradient id={id("jet")} x1="0" y1="1" x2="0" y2="0">
          <stop offset="0" stop-color="#fffbf0" />
          <stop offset="0.55" stop-color="#fff6dc" stop-opacity="0.85" />
          <stop offset="1" stop-color="#fff6dc" stop-opacity="0" />
        </linearGradient>
        <radialGradient id={id("steel")} cx="0.35" cy="0.3" r="0.8">
          <stop offset="0" stop-color="#ffffff" />
          <stop offset="1" stop-color="#8e949c" />
        </radialGradient>
      </defs>

      <g class="bottle-body">
        {/* the glass and what is inside it */}
        <g clip-path={url("glass")}>
          <rect width="160" height="440" fill={glassDark} />
          <g clip-path={url("below")}>
            <rect x="0" y={level} width="160" height={440 - level} fill={url("foam")} />
            <rect x="0" y={Math.max(level, LIQUID_Y)} width="160" height={440 - Math.max(level, LIQUID_Y)} fill={url("liquid")} />
            <g class="bubbles">
              {BUBBLES.slice(0, bubbles).map((b, i) => (
                <circle
                  key={i}
                  class="bubble"
                  cx={b.x}
                  cy={424}
                  r={b.r}
                  style={{ animationDuration: `${b.dur}s`, animationDelay: `${b.delay}s`, "--drift": `${b.drift}px` }}
                />
              ))}
            </g>
          </g>
          <g class={`froth${charge >= 0.9 ? " boiling" : ""}`} fill="#ffffff">
            {crest.map((x, i) => (
              <circle key={i} cx={x} cy={level + 1} r={4 + ((i * 7) % 5)} style={{ animationDelay: `${-(i % 4) * 0.18}s` }} />
            ))}
          </g>
          <rect width="160" height="440" fill={color} opacity="0.22" />
          <rect width="160" height="440" fill={url("shade")} />
          {/* reflections */}
          <path d="M40 250C35 300 35 370 40 414" stroke="#fff" stroke-opacity="0.5" stroke-width="5" stroke-linecap="round" fill="none" />
          <path d="M50 214C56 196 64 184 68 160" stroke="#fff" stroke-opacity="0.35" stroke-width="3.5" stroke-linecap="round" fill="none" />
          <path d="M70 124V152" stroke="#fff" stroke-opacity="0.3" stroke-width="2.5" stroke-linecap="round" />
          <path d="M122 260V400" stroke="#fff" stroke-opacity="0.1" stroke-width="3" stroke-linecap="round" />
        </g>
        <path d={SILHOUETTE} fill="none" stroke="#fff" stroke-opacity="0.22" stroke-width="1.2" />

        {/* shoulder medallion */}
        <g transform="translate(80 214)">
          <circle r="13" fill={url("foil")} />
          <circle r="10.5" fill="#e10600" />
          <text y="5.2" text-anchor="middle" class="bottle-text" font-size="15" fill="#fff">R</text>
        </g>

        {/* label */}
        <g class="label">
          <path d="M24 284Q80 278 136 284V382Q80 388 24 382Z" fill="#f5eedc" />
          <path d="M24 289Q80 283 136 289" stroke="#c9a240" stroke-width="1.2" fill="none" />
          <text x="80" y="305" text-anchor="middle" class="bottle-text small" font-size="8.5" fill="#1b1b22">PODIUM · CUVÉE</text>
          <path d="M36 315H131L124 345H29Z" fill="#e10600" />
          <text x="80" y="338.5" text-anchor="middle" class="bottle-text" font-size="21" fill="#fff" textLength="84" lengthAdjust="spacingAndGlyphs">REDLINE</text>
          <text x="80" y="361" text-anchor="middle" class="bottle-text small" font-size="7.5" fill="#1b1b22">BRUT · CHAMPAGNE</text>
          <path d="M44 368H116" stroke="#c9a240" stroke-width="1" />
          <path d="M24 377Q80 383 136 377" stroke="#1b1b22" stroke-width="2.5" fill="none" />
          <path d="M24 284Q80 278 136 284V382Q80 388 24 382Z" fill={url("cyl")} />
        </g>

        {/* foil */}
        <g class="foil">
          <path d="M59 22H101V35L98 40L98.4 110L95 116L92 110L89 116L86 110L83 116L80 110L77 116L74 110L71 116L68 110L65 116L61.6 110L62 40L59 35Z" fill={url("foil")} />
          <path d="M68 42L67 100M77 40L78 104M86 41L85 100M93 44L94 102" stroke="#5a3d08" stroke-opacity="0.22" stroke-width="0.8" />
          <rect x="62" y="80" width="36" height="2" fill="#e10600" />
          {Array.from({ length: 12 }, (_, i) => (
            <rect key={i} x={62 + (i % 6) * 6} y={83 + Math.floor(i / 6) * 6} width="6" height="6" fill={(i + Math.floor(i / 6)) % 2 ? "#111" : "#fafafa"} />
          ))}
          <rect x="62" y="95" width="36" height="2" fill="#e10600" />
          <path d="M62 80H98V97H62Z" fill={url("cyl")} />
        </g>

        {/* open mouth after the pop: foam welling out and running down the foil */}
        {cork === "flying" ? (
          <g class="overflow" fill="#fffaf0">
            <circle cx="80" cy="21" r="10" />
            <circle cx="70" cy="25" r="6" />
            <circle cx="90" cy="25" r="6.5" />
            <path class="drip" d="M64 26C62 40 65 52 63 62C62 67 68 67 67 61C67 50 70 40 72 28Z" />
            <path class="drip late" d="M92 27C95 36 93 46 95 54C96 58 100 57 99 52C98 44 98 36 96 27Z" />
          </g>
        ) : null}

        {/* cork and wire cage */}
        <g class={`cork ${cork}`}>
          <path d="M64 25C61 25 60 18 62 12C64.5 4.5 72 1.5 80 1.5C88 1.5 95.5 4.5 98 12C100 18 99 25 96 25Z" fill={url("cork")} />
          <path d="M70 16H73M84 9H86M88 18H91M76 21H78" stroke="#8d5e26" stroke-width="1.2" stroke-linecap="round" opacity="0.6" />
          <path d="M80 7L64 22M80 7L73 22M80 7L87 22M80 7L96 22" stroke="#d7dbe0" stroke-width="1.1" fill="none" />
          <path d="M62.5 22H97.5" stroke="#d7dbe0" stroke-width="1.8" stroke-linecap="round" />
          <circle cx="80" cy="7" r="6" fill={url("steel")} stroke="#7d838b" stroke-width="0.6" />
          <circle cx="80" cy="7" r="3" fill="#e10600" />
        </g>
      </g>

      {spray ? <SprayBurst key={spray.id} strength={spray.strength} jet={id("jet")} /> : null}
    </svg>
  );
}

/** Pressure in bar at full charge: a real champagne bottle holds about 6. */
export const MAX_BAR = 6;

/** A round pressure dial, 0–6 bar, red from 5: the needle follows the charge and shakes when it is nearly full. */
export function Gauge({ charge }: { charge: number }) {
  const c = Math.min(1, Math.max(0, charge));
  const at = (bar: number, r: number) => {
    const a = ((-135 + (270 * bar) / MAX_BAR) * Math.PI) / 180;
    return [Math.sin(a) * r, -Math.cos(a) * r] as const;
  };
  const arc = (from: number, to: number, r: number) => {
    const [x1, y1] = at(from, r);
    const [x2, y2] = at(to, r);
    return `M${x1.toFixed(2)} ${y1.toFixed(2)}A${r} ${r} 0 0 1 ${x2.toFixed(2)} ${y2.toFixed(2)}`;
  };
  return (
    <div class={`gauge${c >= 0.9 ? " redline" : ""}`} role="meter" aria-label="Pressure" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(c * 100)}>
      <svg viewBox="0 0 120 120" aria-hidden="true">
        <g transform="translate(60 60)">
          <circle r="57" fill="#2a2b31" />
          <circle r="54" fill="#0c0c11" stroke="#4a4c55" stroke-width="1.5" />
          <path d={arc(0, MAX_BAR, 45)} stroke="#ffffff" stroke-opacity="0.12" stroke-width="5" fill="none" />
          <path d={arc(0, c * MAX_BAR, 45)} stroke="#ffffff" stroke-opacity="0.6" stroke-width="5" fill="none" class="gauge-fill" />
          <path d={arc(4, 5, 45)} stroke="#ffb020" stroke-width="5" fill="none" />
          <path d={arc(5, MAX_BAR, 45)} stroke="#e10600" stroke-width="5" fill="none" />
          {Array.from({ length: MAX_BAR * 2 + 1 }, (_, i) => {
            const bar = i / 2;
            const major = i % 2 === 0;
            const [x1, y1] = at(bar, 40);
            const [x2, y2] = at(bar, major ? 33 : 36);
            const [tx, ty] = at(bar, 25);
            return (
              <g key={i}>
                <line x1={x1} y1={y1} x2={x2} y2={y2} stroke="#fff" stroke-width={major ? 2 : 1} />
                {major ? <text x={tx} y={ty + 3.5} text-anchor="middle" class="gauge-num">{bar}</text> : null}
              </g>
            );
          })}
          <text y="40" text-anchor="middle" class="gauge-value">{(c * MAX_BAR).toFixed(1)}</text>
          <text y="48.5" text-anchor="middle" class="gauge-unit">BAR</text>
          <g class="needle-wrap">
            <g class="needle" style={{ transform: `rotate(${(-135 + 270 * c).toFixed(1)}deg)` }}>
              <path d="M-2.6 8L0-44L2.6 8Z" fill="#e10600" />
              <circle r="6" fill="#d9dce1" stroke="#7d838b" stroke-width="1" />
              <circle r="2" fill="#e10600" />
            </g>
          </g>
        </g>
      </svg>
    </div>
  );
}
