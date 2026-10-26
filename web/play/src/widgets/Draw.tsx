import { useEffect, useRef, useState } from "preact/hooks";
import type { DrawView, Stroke } from "../protocol";
import { Prompt, useSendOnce, useViewState, type WidgetProps } from "./common";

/** Stroke width as a fraction of the canvas width, so it scales with the screen like the points do. */
export const STROKE_WIDTH = 0.012;
/** Total coordinates kept across all strokes; keeps the payload well under the hub's 256 KB message limit. */
export const MAX_COORDS = 16000;

const round = (n: number) => Math.round(Math.min(1, Math.max(0, n)) * 10000) / 10000;

export function Draw({ view, send }: WidgetProps<DrawView>) {
  const colors = view.colors && view.colors.length > 0 ? view.colors : ["#111"];
  const [locked, fire] = useSendOnce(view.viewId, send);
  const [color, setColor] = useViewState(view.viewId, colors[0]!);
  const [strokes, setStrokes] = useViewState<Stroke[]>(view.viewId, []);
  const [, setTick] = useState(0);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const current = useRef<Stroke | null>(null);

  const coords = strokes.reduce((n, s) => n + s.points.length, 0) + (current.current?.points.length ?? 0);

  const redraw = () => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    let ctx: CanvasRenderingContext2D | null = null;
    try {
      ctx = canvas.getContext("2d");
    } catch {
      ctx = null;
    }
    if (!ctx) return;
    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const w = Math.max(1, Math.round(rect.width * dpr));
    const h = Math.max(1, Math.round(rect.height * dpr));
    if (canvas.width !== w || canvas.height !== h) {
      canvas.width = w;
      canvas.height = h;
    }
    ctx.clearRect(0, 0, w, h);
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
    for (const s of current.current ? [...strokes, current.current] : strokes) {
      ctx.strokeStyle = s.color;
      ctx.lineWidth = s.width * w;
      ctx.beginPath();
      for (let i = 0; i < s.points.length; i += 2) {
        const x = s.points[i]! * w;
        const y = s.points[i + 1]! * h;
        if (i === 0) ctx.moveTo(x, y);
        else ctx.lineTo(x, y);
      }
      if (s.points.length === 2) ctx.lineTo(s.points[0]! * w + 0.1, s.points[1]! * h); // a dot
      ctx.stroke();
    }
  };

  useEffect(redraw);
  useEffect(() => {
    window.addEventListener("resize", redraw);
    return () => window.removeEventListener("resize", redraw);
  });

  const point = (e: PointerEvent): [number, number] => {
    const rect = canvasRef.current!.getBoundingClientRect();
    return [round((e.clientX - rect.left) / (rect.width || 1)), round((e.clientY - rect.top) / (rect.height || 1))];
  };

  const down = (e: PointerEvent) => {
    if (locked || coords >= MAX_COORDS) return;
    e.preventDefault();
    try {
      canvasRef.current?.setPointerCapture(e.pointerId);
    } catch {
      // not supported everywhere
    }
    current.current = { color, width: STROKE_WIDTH, points: point(e) };
    redraw();
  };
  const move = (e: PointerEvent) => {
    const stroke = current.current;
    if (!stroke || coords >= MAX_COORDS) return;
    const [x, y] = point(e);
    const n = stroke.points.length;
    if (stroke.points[n - 2] === x && stroke.points[n - 1] === y) return;
    stroke.points.push(x, y);
    redraw();
  };
  const up = () => {
    const stroke = current.current;
    if (!stroke) return;
    current.current = null;
    setStrokes([...strokes, stroke]);
    setTick((t) => t + 1);
  };

  return (
    <div class="w-draw">
      <Prompt text={view.prompt} />
      <canvas
        ref={canvasRef}
        class="draw-canvas"
        onPointerDown={down}
        onPointerMove={move}
        onPointerUp={up}
        onPointerCancel={up}
      />
      <div class="palette">
        {colors.map((c) => (
          <button
            type="button"
            key={c}
            class={`swatch${c === color ? " picked" : ""}`}
            style={{ background: c }}
            aria-label={`Colour ${c}`}
            aria-pressed={c === color ? "true" : "false"}
            disabled={locked}
            onClick={() => setColor(c)}
          />
        ))}
      </div>
      <div class="actions two">
        <button type="button" disabled={locked || strokes.length === 0} onClick={() => setStrokes(strokes.slice(0, -1))}>
          Undo
        </button>
        <button type="button" class="primary" disabled={locked || strokes.length === 0} onClick={() => fire({ strokes })}>
          Submit
        </button>
      </div>
    </div>
  );
}
