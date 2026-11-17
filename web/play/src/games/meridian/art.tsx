import type { JSX } from "preact";
import { usePackBase, usePackJson } from "./pack";

/**
 * The phones' share of the Meridian art (#334, #335; M5 plan, Task 5), read from the content pack's manifests. Every
 * piece is optional: without it, the screen keeps its plain look, and nothing ever carries text (#331).
 */

interface Entry { file: string; insets?: number[] }
interface CardsManifest { frames?: Record<string, Entry> }
interface IconsManifest { icons?: Record<string, Entry> }
interface UiManifest { ui?: Record<string, Entry> }

/** A card frame for a deck, as CSS: a 9-slice border image from the frame's insets. Empty without art. */
export function useCardFrame(kind: string): JSX.CSSProperties {
  const base = usePackBase();
  const frame = usePackJson<CardsManifest>("cards/manifest.json")?.frames?.[kind];
  if (!base || !frame?.insets || frame.insets.length !== 4) return {};
  const [l, t, r, b] = frame.insets;
  return {
    borderStyle: "solid",
    borderWidth: `${t}px ${r}px ${b}px ${l}px`,
    borderImage: `url("${base}cards/${frame.file}") ${t} ${r} ${b} ${l} fill / ${t}px ${r}px ${b}px ${l}px stretch`,
    imageRendering: "pixelated",
  };
}

/** A card's 24 px icon (a Salvage or Anomaly card), or nothing. */
export function ItemIcon({ card }: { card: string }) {
  const base = usePackBase();
  const icon = usePackJson<IconsManifest>("icons/manifest.json")?.icons?.[card];
  return base && icon ? <img class="mer-icon" src={`${base}icons/${icon.file}`} alt="" /> : null;
}

/** The game's logo (#335), or null. */
export function useLogo(): string | null {
  const base = usePackBase();
  const logo = usePackJson<UiManifest>("ui/manifest.json")?.ui?.logo;
  return base && logo ? `${base}ui/${logo.file}` : null;
}
