import * as THREE from "three";
import { packJson } from "../pack";
import { decodeMap, type MapFile } from "../map";
import { AREAS, type Area } from "./areas";
import { loadImage, loadManifests, type WorldManifests } from "./assets";
import { buildArea, type BuiltArea, type MapProp } from "./build";

/**
 * The flyover: a tour of the Vale's areas rendered by three.js at a few hundred lines of resolution, then upscaled by a
 * whole number with nearest-neighbour by the browser, so the 3D world keeps the pack's pixel art crisp (HD-2D).
 *
 * Per frame: backdrop (screen-space sky and parallax ranges) and world into a low-res target, then one full-screen pass
 * for the area's colour grade (grades set LUT), vignette and dimming into the canvas. Between areas the last frame is
 * kept as a still, the old scene is disposed before the next is built (one scene in memory at a time), and the new
 * one dissolves in through a threshold map from the transitions set.
 */

// The art's sRGB values are used as they are, end to end: no linearisation in, no encoding out.
THREE.ColorManagement.enabled = false;

/** Seconds per area, the dissolve into it, and its fly-in. */
export const AREA_SECONDS = 16;
export const FADE_SECONDS = 1.6;
const FLY_SECONDS = 5.5;

export interface FrameInfo {
  area: Area;
  /** Seconds since this area came on screen. */
  local: number;
  /** How visible the area's caption should be (0–1). */
  caption: number;
}

export interface FlyoverOptions {
  canvas: HTMLCanvasElement;
  /** Vertical resolution to aim for; the scale is the whole number closest to screen height / lines. */
  lines?: number;
  /** Darken the whole frame (0–1), for UI over it. */
  dim?: number;
  /** A fixed clock in seconds (screenshots, reduced motion): renders that moment of the tour and stops. */
  frozenAt?: number;
  /** The areas to tour, in order. Defaults to the whole Vale in story order. */
  areas?: Area[];
  /** Move on from area to area (default true when there's more than one). */
  tour?: boolean;
  /** The transitions-set threshold map to dissolve through. */
  transition?: string;
  /** Called after every rendered frame. */
  onFrame?(info: FrameInfo): void;
  /**
   * "title" (default) sits the island low in the frame, under a logo; "backdrop" centres it, for UI laid over the
   * whole screen.
   */
  framing?: "title" | "backdrop";
  /** Dissolve the first area in from the page's night blue (default true); false shows it at once. */
  introFade?: boolean;
}

interface MapFileWithProps extends MapFile { props?: MapProp[] }

export class Flyover {
  readonly canvas: HTMLCanvasElement;
  private renderer: THREE.WebGLRenderer;
  private target: THREE.WebGLRenderTarget;
  private still: THREE.WebGLRenderTarget;
  private blank = new THREE.DataTexture(new Uint8Array([10, 11, 26, 255]), 1, 1);
  private post: { scene: THREE.Scene; camera: THREE.OrthographicCamera; mat: THREE.ShaderMaterial };
  private camera = new THREE.PerspectiveCamera(24, 16 / 9, 0.5, 400);
  private ortho = new THREE.OrthographicCamera(0, 480, 270, 0, -10, 10);
  private area: BuiltArea | null = null;
  private info: Area | null = null;
  private index = 0;
  private areaStart = 0;
  private fadeFrom = -Infinity;
  private switching = false;
  private lut: THREE.Texture | null = null;
  private raf = 0;
  private start = performance.now();
  private size = { w: 480, h: 270, scale: 4 };
  private lines: number;
  private frozenAt: number | undefined;
  private areas: Area[];
  private touring: boolean;
  private onFrame: FlyoverOptions["onFrame"];
  private framing: "title" | "backdrop";
  private introFade: boolean;
  private resizeObserver: ResizeObserver | null = null;
  private disposed = false;
  /** Resolves once the first area is on screen. */
  readonly ready: Promise<boolean>;

  private constructor(opts: FlyoverOptions, private m: WorldManifests) {
    this.canvas = opts.canvas;
    this.lines = opts.lines ?? 270;
    this.frozenAt = opts.frozenAt;
    this.areas = opts.areas?.length ? opts.areas : AREAS;
    this.touring = opts.tour ?? this.areas.length > 1;
    this.onFrame = opts.onFrame;
    this.framing = opts.framing ?? "title";
    this.renderer = new THREE.WebGLRenderer({ canvas: this.canvas, antialias: false, alpha: false, powerPreference: "low-power" });
    this.renderer.setPixelRatio(1);
    this.renderer.outputColorSpace = THREE.LinearSRGBColorSpace;
    this.renderer.autoClear = false;
    const rt = () => new THREE.WebGLRenderTarget(480, 270, {
      minFilter: THREE.NearestFilter, magFilter: THREE.NearestFilter, depthBuffer: true, generateMipmaps: false,
    });
    this.target = rt();
    this.still = rt();
    this.blank.needsUpdate = true;
    this.post = makePost(this.target.texture, this.blank);
    this.post.mat.uniforms.dim!.value = opts.dim ?? 0;
    this.resize();
    if (typeof ResizeObserver !== "undefined" && this.canvas.parentElement) {
      this.resizeObserver = new ResizeObserver(() => this.resize());
      this.resizeObserver.observe(this.canvas.parentElement);
    }
    this.introFade = opts.introFade ?? true;
    this.ready = this.begin(opts.transition ?? "dissolve");
  }

  /** Makes a flyover on the canvas; null when WebGL or the content pack isn't available. */
  static async create(opts: FlyoverOptions): Promise<Flyover | null> {
    const m = await loadManifests();
    if (!m) return null;
    try {
      return new Flyover(opts, m);
    } catch (err) {
      console.warn("No WebGL for the flyover", err);
      return null;
    }
  }

  private async begin(transition: string): Promise<boolean> {
    const wipe = await this.loadWipe(transition);
    if (wipe) this.post.mat.uniforms.wipe!.value = wipe;
    const n = this.areas.length;
    if (this.frozenAt === undefined) {
      // Live: the first area dissolves in from the night-blue of the page.
      this.clearStill();
      const ok = await this.show(0);
      this.areaStart = this.time;
      this.fadeFrom = this.introFade ? this.areaStart : -Infinity;
      this.renderFrame();
      return ok;
    }
    // Frozen: the moment of the tour at that clock, the same as a live run would show it.
    const t = this.frozenAt;
    const cycle = this.touring ? Math.floor(t / AREA_SECONDS) : 0;
    const local = this.touring ? t - cycle * AREA_SECONDS : t;
    const index = ((cycle % n) + n) % n;
    if (local < FADE_SECONDS) {
      if (cycle > 0) {
        await this.show((index - 1 + n) % n);
        this.areaStart = t - AREA_SECONDS;
        this.fadeFrom = -Infinity;
        this.renderFrame();
        this.snapshot();
      } else if (this.introFade) {
        this.clearStill();
      }
      this.fadeFrom = cycle > 0 || this.introFade ? t - local : -Infinity;
    }
    const ok = await this.show(index);
    this.areaStart = t - local;
    this.renderFrame();
    return ok;
  }

  /** Builds an area and swaps it in, disposing the old one first so two scenes never sit in memory together. */
  private async show(index: number): Promise<boolean> {
    const info = this.areas[index]!;
    const file = await packJson<MapFileWithProps>(`rules/maps/${info.map}.json`);
    if (!file || this.disposed) return false;
    this.area?.dispose();
    this.area = null;
    const [built, lut] = await Promise.all([
      buildArea(info, decodeMap(file), file.props ?? [], this.m, hash(info.map)),
      this.loadLut(info.grade),
    ]);
    if (this.disposed) { built?.dispose(); lut?.dispose(); return false; }
    if (!built) return false;
    this.area = built;
    this.info = info;
    this.index = index;
    this.lut?.dispose();
    this.lut = lut;
    const u = this.post.mat.uniforms;
    u.lut!.value = lut ?? this.blank;
    u.strength!.value = lut ? info.gradeStrength : 0;
    built.resize(this.size.w, this.size.h);
    return true;
  }

  /** On to the next area: keep the last frame, swap scenes, dissolve. */
  private async next(): Promise<void> {
    if (this.switching) return;
    this.switching = true;
    this.snapshot();
    this.fadeFrom = Infinity;   // hold the still while the next scene builds
    await this.show((this.index + 1) % this.areas.length);
    this.fadeFrom = this.areaStart = this.time;
    this.switching = false;
  }

  /** Copies what's on screen into the still target, for the dissolve. */
  private snapshot(): void {
    const u = this.post.mat.uniforms;
    u.prev!.value = this.blank;   // never sample the target being drawn into
    const fade = u.progress!.value;
    u.progress!.value = 0;
    this.renderer.setRenderTarget(this.still);
    this.renderer.render(this.post.scene, this.post.camera);
    this.renderer.setRenderTarget(null);
    u.prev!.value = this.still.texture;
    u.progress!.value = fade;
  }

  private clearStill(): void {
    this.renderer.setRenderTarget(this.still);
    this.renderer.setClearColor(0x0a0b1a, 1);
    this.renderer.clear(true, true, true);
    this.renderer.setRenderTarget(null);
    this.post.mat.uniforms.prev!.value = this.still.texture;
  }

  private async loadLut(name: string): Promise<THREE.Texture | null> {
    const file = this.m.grades?.grades[name];
    if (!file) return null;
    const img = await loadImage(this.m.base + "grades/" + file);
    if (!img) return null;
    const t = new THREE.Texture(img);
    // The LUT is read by its own layout (slice = blue, x = red, y = green from the top): no flip, filtered.
    t.flipY = false;
    t.magFilter = t.minFilter = THREE.LinearFilter;
    t.generateMipmaps = false;
    t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
    t.colorSpace = THREE.NoColorSpace;
    t.needsUpdate = true;
    return t;
  }

  private async loadWipe(name: string): Promise<THREE.Texture | null> {
    const manifest = await packJson<{ transitions: Record<string, string> }>("transitions/manifest.json");
    const file = manifest?.transitions[name];
    if (!file) return null;
    const img = await loadImage(this.m.base + "transitions/" + file);
    if (!img) return null;
    const t = new THREE.Texture(img);
    t.magFilter = t.minFilter = THREE.NearestFilter;
    t.generateMipmaps = false;
    t.colorSpace = THREE.NoColorSpace;
    t.needsUpdate = true;
    return t;
  }

  /** Starts the animation loop (no-op when frozen). */
  play(): void {
    if (this.raf || this.frozenAt !== undefined || this.disposed) return;
    const tick = () => {
      this.raf = requestAnimationFrame(tick);
      this.renderFrame();
      if (this.touring && !this.switching && this.area && this.time - this.areaStart > AREA_SECONDS) void this.next();
    };
    this.raf = requestAnimationFrame(tick);
  }

  pause(): void {
    cancelAnimationFrame(this.raf);
    this.raf = 0;
  }

  get playing(): boolean {
    return this.raf !== 0;
  }

  set dim(v: number) {
    this.post.mat.uniforms.dim!.value = v;
    if (this.frozenAt !== undefined) this.renderFrame();
  }

  /** Seconds on the flyover's clock. */
  get time(): number {
    return this.frozenAt ?? (performance.now() - this.start) / 1000;
  }

  /** Sizes the low-res target: screen height / lines, rounded to a whole scale, and the canvas shown at that scale. */
  resize(): void {
    const box = this.canvas.parentElement?.getBoundingClientRect();
    const cssW = box?.width || window.innerWidth, cssH = box?.height || window.innerHeight;
    const dpr = window.devicePixelRatio || 1;
    const scale = Math.max(1, Math.round((cssH * dpr) / this.lines));
    const w = Math.max(16, Math.ceil((cssW * dpr) / scale)), h = Math.max(16, Math.ceil((cssH * dpr) / scale));
    if (w === this.size.w && h === this.size.h && scale === this.size.scale && this.area) return;
    this.size = { w, h, scale };
    this.renderer.setSize(w, h, false);
    this.canvas.style.width = `${(w * scale) / dpr}px`;
    this.canvas.style.height = `${(h * scale) / dpr}px`;
    this.target.setSize(w, h);
    this.still.setSize(w, h);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.ortho.right = w;
    this.ortho.top = h;
    this.ortho.updateProjectionMatrix();
    this.area?.resize(w, h);
    if (!this.raf) this.renderFrame();
  }

  private renderFrame(): void {
    const r = this.renderer;
    const u = this.post.mat.uniforms;
    const t = this.time;
    const fade = (t - this.fadeFrom) / FADE_SECONDS;
    u.progress!.value = this.fadeFrom === Infinity ? 1.01 : fade >= 0 && fade < 1 ? 1.01 * (1 - fade) : 0;

    const a = this.area;
    if (a && this.info) {
      const local = t - this.areaStart;
      const { yaw, pitch, dist } = this.drift(local, a);
      // Fog follows the camera's distance, so the island's far edge softens the same way however close it is.
      if (a.world.fog instanceof THREE.Fog) {
        a.world.fog.near = dist * 0.85;
        a.world.fog.far = dist * 2.1;
      }
      a.update(t, this.camera, yaw, pitch);
      r.setRenderTarget(this.target);
      r.setClearColor(a.horizon, 1);
      r.clear(true, true, true);
      r.render(a.backdrop, this.ortho);
      r.clearDepth();
      r.render(a.world, this.camera);
    }
    r.setRenderTarget(null);
    r.render(this.post.scene, this.post.camera);
    if (this.info && this.onFrame) {
      const local = t - this.areaStart;
      this.onFrame({ area: this.info, local, caption: this.switching ? 0 : captionAlpha(local, this.touring) });
    }
  }

  /**
   * The camera for a moment of an area: it flies in (from further out, higher and swung round, easing out over the
   * first seconds), then holds in a slow cinematic drift (a gentle orbit, a breathing tilt and a slow push-in, on
   * incommensurate periods so it never visibly repeats). Returns the yaw and pitch the backdrop and billboards use.
   */
  private drift(local: number, a: BuiltArea): { yaw: number; pitch: number; dist: number } {
    const span = Math.max(a.size.w, a.size.h);
    const k = Math.min(1, Math.max(0, local / FLY_SECONDS));
    const away = (1 - k) ** 3;   // ease-out: fast at first, settling into the hold
    const flip = this.index % 2 ? -1 : 1;   // alternate the side each area swings in from
    const yaw = Math.PI / 4 + flip * (0.22 * Math.sin(local * 0.05) - 0.75 * away) + 0.05 * Math.sin(local * 0.11 + 1.3);
    const pitch = 0.42 + 0.04 * Math.sin(local * 0.07 + 0.7) + 0.38 * away;
    // Framed for 16:9; a narrower screen (a phone held upright) pulls back, though less than the full width would
    // need: on a phone the island's corners may leave the frame, but it fills the screen rather than floating small.
    const narrow = Math.max(1, (16 / 9) / this.camera.aspect) ** (this.framing === "backdrop" ? 0.45 : 0.55);
    const push = 1 - 0.07 * Math.min(1, local / AREA_SECONDS);
    const dist = span * 3.05 * narrow * push * (1 + 1.1 * away);
    // For the title, aim above the map's middle so the island sits in the lower part of the frame, under the logo.
    const lift = this.framing === "backdrop" ? span * 0.08 : span * 0.32;
    const target = new THREE.Vector3(0.5 * Math.sin(local * 0.05), a.size.top * 0.4 + lift, 0.5 * Math.cos(local * 0.07));
    this.camera.position.set(
      target.x + Math.sin(yaw) * Math.cos(pitch) * dist,
      target.y + Math.sin(pitch) * dist,
      target.z + Math.cos(yaw) * Math.cos(pitch) * dist,
    );
    this.camera.lookAt(target);
    return { yaw, pitch, dist };
  }

  get current(): Area | null {
    return this.info;
  }

  dispose(): void {
    this.disposed = true;
    this.pause();
    this.resizeObserver?.disconnect();
    this.area?.dispose();
    this.area = null;
    this.lut?.dispose();
    this.blank.dispose();
    (this.post.mat.uniforms.wipe!.value as THREE.Texture | null)?.dispose();
    this.target.dispose();
    this.still.dispose();
    this.post.mat.dispose();
    this.renderer.dispose();
  }
}

/** The caption fades in once the fly-in has settled and out before the next area. */
export function captionAlpha(local: number, touring: boolean): number {
  const fadeIn = Math.min(1, Math.max(0, (local - 2) / 1));
  if (!touring) return fadeIn;
  const fadeOut = Math.min(1, Math.max(0, (AREA_SECONDS - 1.2 - local) / 1));
  return Math.min(fadeIn, fadeOut);
}

function makePost(scene: THREE.Texture, blank: THREE.Texture) {
  const mat = new THREE.ShaderMaterial({
    uniforms: {
      scene: { value: scene },
      prev: { value: blank },
      wipe: { value: blank },
      progress: { value: 0 },
      lut: { value: blank },
      strength: { value: 0 },
      dim: { value: 0 },
      vignette: { value: 0.35 },
    },
    vertexShader: /* glsl */ `
      varying vec2 vUv;
      void main() { vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }`,
    // The grade is the pack's shader (grades/manifest.json godotShader), ported: a 16-cube laid out as 16 slices. The
    // dissolve is the transitions set's: a pixel shows the old frame while its threshold is under the progress.
    fragmentShader: /* glsl */ `
      uniform sampler2D scene;
      uniform sampler2D prev;
      uniform sampler2D wipe;
      uniform float progress;
      uniform sampler2D lut;
      uniform float strength;
      uniform float dim;
      uniform float vignette;
      varying vec2 vUv;
      vec3 grade(vec3 c) {
        float n = 16.0;
        float b = c.b * (n - 1.0);
        float b0 = floor(b), b1 = min(b0 + 1.0, n - 1.0);
        vec2 uv0 = vec2((b0 * n + c.r * (n - 1.0) + 0.5) / (n * n), (c.g * (n - 1.0) + 0.5) / n);
        vec2 uv1 = vec2((b1 * n + c.r * (n - 1.0) + 0.5) / (n * n), uv0.y);
        return mix(texture2D(lut, uv0).rgb, texture2D(lut, uv1).rgb, b - b0);
      }
      void main() {
        if (progress > 0.0 && texture2D(wipe, vUv).r < progress) {
          gl_FragColor = vec4(texture2D(prev, vUv).rgb, 1.0);
          return;
        }
        vec3 c = clamp(texture2D(scene, vUv).rgb, 0.0, 1.0);
        if (strength > 0.0) c = mix(c, grade(c), strength);
        float d = distance(vUv, vec2(0.5));
        c *= 1.0 - vignette * smoothstep(0.38, 0.9, d);
        c *= 1.0 - dim;
        gl_FragColor = vec4(c, 1.0);
      }`,
    depthTest: false,
    depthWrite: false,
  });
  const quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), mat);
  const s = new THREE.Scene();
  s.add(quad);
  return { scene: s, camera: new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1), mat };
}

function hash(s: string): number {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 16777619);
  return h >>> 0;
}
