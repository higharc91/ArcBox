import type { JSX } from "preact";
import { useEffect, useLayoutEffect, useRef, useState } from "preact/hooks";
import { Sheet } from "./details";
import { drawing, paint, screen, snap, tileAt, useRegion } from "./regionMap";
import type { MapPinData, WalkMapData } from "./types";

/** Each landmark's mark on the map, by `kind`, and the word the list of places gives it. */
export const PIN_MARKS: Record<string, [mark: string, word: string]> = {
  story: ["!", "Story"], quest: ["?", "Quest"], turnIn: ["✓", "Quest done"], pinned: ["★", "Pinned quest"], shop: ["◆", "Shop"],
  inn: ["⌂", "Inn"], temple: ["✚", "Temple"], rest: ["▲", "Campfire"], place: ["●", "Place"], person: ["•", "Person"],
};

/** How much the zoomed map is magnified over the whole region fitted to the screen. */
const ZOOM = 3;

const pct = (n: number) => `${(n * 100).toFixed(3)}%`;

/**
 * The leader's map of the region (tap to travel): the region's tiles drawn as the TV sees them, so up is up on both, with the party, where
 * it's going, and the landmarks the server sends. A tap on a landmark, an entry in the list of places, or any ground asks the server to walk
 * the party there (`onPick`); a tap off the ground goes to the nearest ground the map knows of, and the server finds the nearest it can
 * reach. The whole region fits the screen; Zoom magnifies it round the party, and it scrolls (or drags) from there.
 */
export function MapSheet({ map, regionName, onPick, onClose }: {
  map: WalkMapData | undefined; regionName: string | null; onPick(tile: [number, number]): void; onClose(): void;
}) {
  const region = useRegion(map?.region);
  const [zoom, setZoom] = useState(false);
  const canvas = useRef<HTMLCanvasElement>(null);
  const scroller = useRef<HTMLDivElement>(null);
  const [w, h] = map?.size ?? (region ? [region.w, region.h] : [0, 0]);
  const size = drawing(w, h);

  useEffect(() => {
    const ctx = region && canvas.current?.getContext?.("2d");
    if (region && ctx) paint(ctx, region);
  }, [region]);

  // Zoomed in, the party starts in the middle of the screen.
  useLayoutEffect(() => {
    const box = scroller.current;
    if (!zoom || !map || !box || w === 0) return;
    const { sx, sy } = screen(h, map.at[0], map.at[1]);
    box.scrollLeft = (sx / size.width) * box.scrollWidth - box.clientWidth / 2;
    box.scrollTop = (sy / size.height) * box.scrollHeight - box.clientHeight / 2;
  }, [zoom, w === 0]);

  const place = ([x, y]: [number, number]): JSX.CSSProperties => {
    const { sx, sy } = screen(h, x, y);
    return { left: pct(sx / size.width), top: pct(sy / size.height) };
  };

  const tap = (e: JSX.TargetedMouseEvent<HTMLDivElement>) => {
    if (w === 0) return;
    const box = e.currentTarget.getBoundingClientRect();
    if (box.width === 0 || box.height === 0) return;
    const [x, y] = tileAt(h, ((e.clientX - box.left) / box.width) * size.width, ((e.clientY - box.top) / box.height) * size.height);
    if (x < 0 || y < 0 || x >= w || y >= h) return;
    onPick(snap(region, x, y));
  };

  const pins = map?.pins ?? [];
  return (
    <Sheet title="Map" eyebrow={regionName ?? undefined} onClose={onClose} wide
      foot={<p class="tx-map-hint">Tap a place, or anywhere on the map, and the party walks there. Press the pad to stop.</p>}>
      {!map ? <p class="tx-map-wait" role="status">Opening the map…</p> : (
        <>
          <div class="tx-map-tools">
            <button type="button" class="tx-btn ghost" aria-pressed={zoom} onClick={() => setZoom((z) => !z)}>{zoom ? "Show it all" : "Zoom in"}</button>
          </div>
          <div class={`tx-map-scroll${zoom ? " zoomed" : ""}`} ref={scroller}>
            <div class="tx-map-plane" style={{ "--zoom": zoom ? ZOOM : 1, aspectRatio: `${size.width} / ${size.height}` } as JSX.CSSProperties}
              onClick={tap} aria-label="The region: tap where the party should go" role="group">
              <canvas ref={canvas} width={size.width} height={size.height} aria-hidden="true" />
              {map.to ? <span class="tx-map-to" style={place(map.to)} aria-hidden="true" /> : null}
              {pins.map((p) => <Pin key={`${p.at[0]},${p.at[1]}`} pin={p} style={place(p.at)} onPick={onPick} />)}
              <span class="tx-map-party" style={place(map.at)} role="img" aria-label="The party" />
            </div>
          </div>
          {pins.length > 0 ? (
            <ul class="tx-map-places" aria-label="Places">
              {pins.map((p) => (
                <li key={`${p.at[0]},${p.at[1]}`}>
                  <button type="button" class="tx-btn ghost" onClick={() => onPick(p.at)}>
                    <span class={`tx-map-mark ${p.kind}`} aria-hidden="true">{PIN_MARKS[p.kind]?.[0] ?? "●"}</span>
                    <span class="tx-map-name">
                      <b>{p.label}</b>
                      <span class="tx-map-kind">{PIN_MARKS[p.kind]?.[1] ?? ""}</span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </>
      )}
    </Sheet>
  );
}

function Pin({ pin, style, onPick }: { pin: MapPinData; style: JSX.CSSProperties; onPick(tile: [number, number]): void }) {
  return (
    <button type="button" class={`tx-map-pin ${pin.kind}`} style={style} aria-label={`${pin.label}: go there`} title={pin.label}
      onClick={(e) => { e.stopPropagation(); onPick(pin.at); }}>
      <span aria-hidden="true">{PIN_MARKS[pin.kind]?.[0] ?? "●"}</span>
    </button>
  );
}
