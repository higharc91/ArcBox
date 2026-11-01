/**
 * Player colours on the phone (the user's answer 14): the same contrast rule as the server's `PlayerColors`, so the picker can show,
 * as the thumb moves, the colour the server will keep. The server is the authority: it applies the same rule to whatever it's sent.
 * The grounds and the contrast needed come from the portal view (`options.colors`), never from here.
 */

export interface PaletteColor { hex: string; name: string }
export interface Ground { name: string; hex: string }
/** `taken`: the colours other members have, by their names; absent from an older server. */
export interface ColorOptions { palette: PaletteColor[]; grounds: Ground[]; minContrast: number; current: string; taken?: { hex: string; name: string }[] }

/** "#rrggbb" (any case) as lower case, or null. */
export function parseHex(hex: string | null | undefined): string | null {
  return typeof hex === "string" && /^#[0-9a-fA-F]{6}$/.test(hex) ? hex.toLowerCase() : null;
}

const rgb = (hex: string): [number, number, number] => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16)) as [number, number, number];
const linear = (c: number) => {
  const v = c / 255;
  return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
};

/** WCAG relative luminance, 0 to 1. */
export function luminance(hex: string): number {
  const [r, g, b] = rgb(hex);
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b);
}

/** WCAG contrast ratio, 1 to 21. */
export function contrast(a: string, b: string): number {
  const [x, y] = [luminance(a), luminance(b)];
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
}

export const worstContrast = (hex: string, grounds: Ground[]) => Math.min(...grounds.map((g) => contrast(hex, g.hex)));

function hsl(hex: string): [number, number, number] {
  const [r, g, b] = rgb(hex).map((c) => c / 255) as [number, number, number];
  const max = Math.max(r, g, b), min = Math.min(r, g, b);
  const l = (max + min) / 2;
  if (max === min) return [0, 0, l];
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  const h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return [h * 60, s, l];
}

function fromHsl(h: number, s: number, l: number): string {
  const c = (1 - Math.abs(2 * l - 1)) * s;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = l - c / 2;
  const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
  // C#'s Math.Round rounds halves to even; so does this, so both sides keep the same byte.
  const byte = (v: number) => {
    const n = v * 255;
    const f = Math.floor(n);
    const r = n - f === 0.5 ? (f % 2 === 0 ? f : f + 1) : Math.round(n);
    return Math.max(0, Math.min(255, r)).toString(16).padStart(2, "0");
  };
  return `#${byte(r! + m)}${byte(g! + m)}${byte(b! + m)}`;
}

/**
 * The colour made readable on every ground: unchanged when it reaches `min`, else lightened a percent at a time (hue and saturation
 * kept) until it does. Null for anything but "#rrggbb".
 */
export function readable(hex: string | null | undefined, grounds: Ground[], min: number): string | null {
  const colour = parseHex(hex);
  if (!colour) return null;
  if (worstContrast(colour, grounds) >= min) return colour;
  const [h, s, l] = hsl(colour);
  for (let step = Math.floor(l * 100) + 1; step <= 100; step++) {
    const tried = fromHsl(h, s, step / 100);
    if (worstContrast(tried, grounds) >= min) return tried;
  }
  return "#ffffff";
}

/** A screen reader's name for a colour: the palette's, else its hex. */
export const colorName = (hex: string, palette: PaletteColor[]) => palette.find((p) => p.hex === hex)?.name ?? hex;
