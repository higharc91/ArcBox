import type { JSX } from "preact";
import { conditionName } from "./conditions";
import { usePackBase, usePackJson } from "./pack";
import type { UnitView } from "./types";

/**
 * Pixel art from the content pack (tools/tactics-assets/README.md): portraits, class crests, ability and status icons
 * and the Wardenflame logo. Each piece draws at a whole-number scale with nearest-neighbour sampling, and falls back
 * to a plain placeholder of the same size until the pack has loaded (or when there's no pack, as in tests).
 */

type Rect = [number, number, number, number];

interface IconManifest {
  icons: Record<string, { atlas: string; rect: Rect; category: string }>;
  abilityIcons: Record<string, { rect: Rect; rect2x: Rect; class: string }>;
}
interface PortraitManifest { presets: Record<string, Record<string, string>> }
interface CrestManifest { classes: { file: string; size: [number, number]; order: string[] } }

export const useIcons = () => usePackJson<IconManifest>("icons/manifest.json");
const usePortraits = () => usePackJson<PortraitManifest>("portraits/manifest.json");
const useCrests = () => usePackJson<CrestManifest>("crests/manifest.json");

/** True on high-DPI screens, where the natively drawn 2× atlases look right. */
const hiDpi = () => typeof window !== "undefined" && (window.devicePixelRatio ?? 1) >= 2;

/**
 * One rect of an atlas, drawn `scale`× (a whole number) in a box of exactly that size. The atlas image is drawn at its
 * natural size and moved and scaled with a transform, so the atlas's own size never needs to be known.
 */
export function Sprite({ src, rect, scale = 1, class: cls, label }: {
  src: string; rect: Rect; scale?: number; class?: string; label?: string;
}) {
  const [x, y, w, h] = rect;
  const box = { "--w": `${w * scale}px`, "--h": `${h * scale}px` } as JSX.CSSProperties;
  const img = { "--tx": `${-x * scale}px`, "--ty": `${-y * scale}px`, "--s": scale } as JSX.CSSProperties;
  return (
    <span class={`tx-sprite${cls ? ` ${cls}` : ""}`} style={box} role={label ? "img" : undefined} aria-label={label}
      aria-hidden={label ? undefined : true}>
      <img src={src} alt="" style={img} draggable={false} />
    </span>
  );
}

// ---- Ability icons -------------------------------------------------------------------------------------------------

/** An ability's icon (48 CSS px), by its id; an item's own icon first when it's an item use. Blank until loaded. */
export function AbilityIcon({ ability, item }: { ability: string; item?: string | null }) {
  const base = usePackBase();
  const icons = useIcons();
  const entry = (item ? icons?.abilityIcons[`item.${item}`] : undefined) ?? icons?.abilityIcons[ability];
  if (!base || !entry) return <span class="tx-icon blank" aria-hidden="true" />;
  return hiDpi()
    ? <Sprite src={`${base}icons/abilities_by_id@2x.png`} rect={entry.rect2x} scale={1} class="tx-icon" />
    : <Sprite src={`${base}icons/abilities_by_id.png`} rect={entry.rect} scale={2} class="tx-icon" />;
}

/** The class an ability belongs to (a subclass's abilities belong to its class), from the icon manifest. */
export const abilityClass = (icons: IconManifest | null, ability: string): string | undefined => icons?.abilityIcons[ability]?.class;

// ---- Status icons --------------------------------------------------------------------------------------------------

/** A condition's status icon (16 px art at 2×) and its name, as a chip. Conditions without art get a plain dot. */
export function ConditionChip({ id }: { id: string }) {
  const base = usePackBase();
  const icons = useIcons();
  const key = conditionName(id).replaceAll(" ", "_");
  const icon = icons?.icons[key] ?? icons?.icons[STATUS_ALIASES[key] ?? ""];
  return (
    <span class="tx-chip tx-cond">
      {base && icon?.category === "status"
        ? <Sprite src={`${base}icons/${icon.atlas}`} rect={icon.rect} scale={1} class="tx-cond-icon" />
        : <i class="tx-cond-dot" aria-hidden="true" />}
      {conditionName(id)}
    </span>
  );
}

const STATUS_ALIASES: Record<string, string> = {
  poison: "poisoned", burn: "burning", stun: "stunned", hasted: "hasted", haste: "hasted", slow: "slowed", bless: "blessed",
  shield: "shielded", shield_of_faith: "shielded", hunters_mark: "marked", hex: "marked", faerie_fire: "marked",
  invisible: "hidden", dodging: "shielded", restrained: "slowed", paralyzed: "stunned", incapacitated: "stunned",
  frozen: "chilled", concentration: "concentrating",
};

// ---- Portraits and crests ------------------------------------------------------------------------------------------

/**
 * A unit's portrait at `size` CSS px (a multiple of 20: portraits are 20 px art, shipped at 2×). Enemies and NPCs use
 * their sprite's baked portrait; player characters have no sprite, so they use their class's portrait when the class
 * is known. The hurt pose shows below a third of their HP. Otherwise, a framed initial.
 */
export function Portrait({ unit, cls, size, tone }: { unit: Pick<UnitView, "name" | "sprite" | "hp" | "maxHp" | "side">; cls?: string | null; size: 40 | 60 | 80; tone?: "ally" | "enemy" | "you" }) {
  const base = usePackBase();
  const portraits = usePortraits();
  const key = unit.sprite ? unit.sprite.split(".").pop()! : cls ? `class_${cls}` : null;
  const moods = key ? portraits?.presets[key] : undefined;
  const hurt = unit.maxHp > 0 && unit.hp * 3 < unit.maxHp;
  const file = moods ? (hurt ? moods.hurt : moods.idle) ?? moods.idle : undefined;
  const side = tone ?? (unit.side === "Enemy" ? "enemy" : "ally");
  return (
    <span class={`tx-portrait s${size} ${side}`}>
      {base && file
        ? <img src={`${base}portraits/${file}`} alt="" width={size} height={size} draggable={false} />
        : <b aria-hidden="true">{unit.name.slice(0, 1)}</b>}
    </span>
  );
}

/** A class's crest (32 px art) at `scale`×; blank until loaded. */
export function Crest({ cls, scale = 1 }: { cls: string; scale?: 1 | 2 }) {
  const base = usePackBase();
  const crests = useCrests();
  const i = crests?.classes.order.indexOf(cls) ?? -1;
  if (!base || !crests || i < 0) return <span class={`tx-crest blank x${scale}`} aria-hidden="true" />;
  const [w, h] = crests.classes.size;
  return <Sprite src={`${base}crests/${crests.classes.file}`} rect={[i * w, 0, w, h]} scale={scale} class="tx-crest" />;
}

/** The Wardenflame logo (278×108 art at 1×, so each art pixel is a whole number of device pixels), or the name in type. */
export function Logo({ small = false }: { small?: boolean }) {
  const base = usePackBase();
  return (
    <div class={`tx-logo${small ? " small" : ""}`}>
      {base ? <img src={`${base}title/${small ? "emblem" : "logo"}.png`} alt="Wardenflame" draggable={false} /> : <b>Wardenflame</b>}
    </div>
  );
}
