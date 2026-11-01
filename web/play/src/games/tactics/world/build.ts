import * as THREE from "three";
import type { MapData } from "../map";
import type { Area, Dressing } from "./areas";
import {
  destreak, glowMask, loadImage, pixelColour, pixelTexture, resolveProp, sliceStrip,
  type ResolvedTexture, type WorldManifests,
} from "./assets";
import { columnLook, type ColumnLook } from "./terrain";

/**
 * One area of the Vale as a three.js scene: the battle map's tile columns (tops, cliff sides and lips, paired in
 * terrain.ts), its props and set dressing as camera-facing billboards, their point lights, and a screen-space backdrop
 * (sky, parallax ranges, clouds). Units: one tile = 1, one height step = 0.5 (32 px and 16 px), one sprite pixel =
 * 1/16. The map's x runs along world +X, its y along world +Z, centred on the origin.
 */

const STEP = 0.5;
const SPRITE_PX = 1 / 16;
/**
 * Tiles one ground texture covers. The manifest's world-aligned sampling uses 4 (32 texels a tile); the flyover's
 * low-res frame shows a tile at about 20 pixels, where 32 texels shimmer into noise, so it spreads each texture
 * over 6 tiles (about one texel per pixel). Sides and lips scale with it so cliffs keep matching their tops.
 */
const TEX_SPAN = 6;
const DENSITY = 4 / TEX_SPAN;
/** How far below the lowest top the island's cliffs reach. */
const ISLAND_DEPTH = 1.75;
/** A side texture's 32 texels cover this much depth at the flyover's density. */
const SIDE_TILE = 1 / DENSITY;
const LIP_DEPTH = (12 / 32) * SIDE_TILE;
/** A plank tile spanning a chasm is a bridge deck this thick, not a column. */
const DECK = 0.35;
/*
 * Light intensities in areas.ts are fractions of the albedo (1 = the colour as drawn); three's lights divide by pi.
 */
/** Point lights cost per pixel; an area keeps at most this many (props' first, then glowing ground). */
const MAX_LIGHTS = 12;

export interface Flicker { light: THREE.PointLight; base: number; curve: number[]; phase: number }

export interface BuiltArea {
  world: THREE.Scene;
  backdrop: THREE.Scene;
  /** Map size and its highest top, for the camera. */
  size: { w: number; h: number; top: number };
  /** The haze colour: the clear colour under the island and the fog. */
  horizon: THREE.Color;
  /** Called every frame: animation frames, flicker, billboards, backdrop scroll. */
  update(t: number, camera: THREE.PerspectiveCamera, yaw: number, pitch: number): void;
  /** Sizes the backdrop to the low-res target. */
  resize(w: number, h: number): void;
  dispose(): void;
}

interface Animated { frames: THREE.Texture[]; fps: number; materials: { mat: THREE.MeshLambertMaterial; key: "map" | "emissiveMap" }[] }

export interface MapProp { id: string; at: [number, number] }

/** Builds an area. Resolves null if the art can't be had. */
export async function buildArea(
  area: Area, map: MapData, props: MapProp[], m: WorldManifests, seed = 1,
): Promise<BuiltArea | null> {
  const owned: { dispose(): void }[] = [];
  const own = <T extends { dispose(): void }>(x: T): T => (owned.push(x), x);
  const animated: Animated[] = [];
  const flickers: Flicker[] = [];
  const billboards: THREE.Object3D[] = [];
  const rand = mulberry(seed);

  const world = new THREE.Scene();
  const tex = async (r: ResolvedTexture | null, glow: string | null) => {
    if (!r) return null;
    const img = await loadImage(r.url);
    if (!img) return null;
    const canvases = sliceStrip(img, r.entry.frames);
    const frames = r.entry.frames > 1
      ? canvases.map((c) => own(pixelTexture(new THREE.CanvasTexture(c))))
      : [own(pixelTexture(new THREE.Texture(img)))];
    const masks = glow ? canvases.map((c) => own(pixelTexture(new THREE.CanvasTexture(glowMask(c, glow))))) : null;
    return { frames, masks, fps: r.entry.fps };
  };

  // ---- Terrain ----------------------------------------------------------------------------------------------------
  const looks = new Map<string, ColumnLook>();
  const lookOf = (i: number): ColumnLook => {
    const name = map.materials[map.tileMaterial[i]!] ?? "rock/grey";
    let l = looks.get(name);
    if (!l) looks.set(name, (l = columnLook(m, name, area.retexture)));
    return l;
  };
  const solid = map.heights.map((h, i) => (lookOf(i).pit ? Infinity : h * STEP - lookOf(i).drop));
  const bottom = Math.min(...solid.filter(Number.isFinite), 0) - ISLAND_DEPTH;
  // A chasm's floor is the island's underside, a little up so it reads as a floor and not a hole.
  const top = (i: number) => (lookOf(i).pit ? bottom + 0.25 : solid[i]!);
  const isPit = (x: number, y: number) => x >= 0 && y >= 0 && x < map.w && y < map.h && lookOf(y * map.w + x).pit;
  const isDeck = (x: number, y: number) => {
    const fam = lookOf(y * map.w + x).material.split("/")[0]!;
    return /planks|boards/.test(fam) && (isPit(x - 1, y) || isPit(x + 1, y) || isPit(x, y - 1) || isPit(x, y + 1));
  };
  const ox = -map.w / 2, oz = -map.h / 2;

  type Bucket = { quads: Quads; tex: Awaited<ReturnType<typeof tex>>; lip?: boolean };
  const buckets = new Map<string, Bucket>();
  const pending: Promise<void>[] = [];
  const bucket = (key: string, r: ResolvedTexture | null, glow: string | null, lip = false) => {
    let b = buckets.get(key);
    if (!b) {
      b = { quads: newQuads(), tex: null, lip };
      buckets.set(key, b);
      pending.push(tex(r, glow).then((t) => { b!.tex = t; }));
    }
    return b.quads;
  };
  const S = TEX_SPAN;

  for (let y = 0; y < map.h; y++) {
    for (let x = 0; x < map.w; x++) {
      const i = y * map.w + x;
      const look = lookOf(i);
      const Y = top(i);
      const X = ox + x, Z = oz + y;
      addQuad(bucket(`top:${look.top?.url}:${look.glow}`, look.top, look.glow),
        [[X, Y, Z], [X, Y, Z + 1], [X + 1, Y, Z + 1], [X + 1, Y, Z]], [0, 1, 0],
        [[x / S, -y / S], [x / S, -(y + 1) / S], [(x + 1) / S, -(y + 1) / S], [(x + 1) / S, -y / S]]);

      // A bridge deck is a thin slab; everything else is a column down to its lower neighbour or the island's foot.
      const floor = isDeck(x, y) ? Y - DECK : -Infinity;
      const sideGlow = look.side === look.top ? look.glow : look.side?.entry.emissive?.colour[look.side.variant] ?? null;
      for (const [dx, dz] of [[1, 0], [-1, 0], [0, 1], [0, -1]] as const) {
        const nx = x + dx, ny = y + dz;
        const inside = nx >= 0 && ny >= 0 && nx < map.w && ny < map.h;
        const Yn = Math.max(inside ? top(ny * map.w + nx) : bottom, floor);
        if (Yn >= Y - 1e-4) continue;
        // The face's edge, from one corner to the other, along the side.
        const [ax, az, bx, bz] =
          dx === 1 ? [X + 1, Z, X + 1, Z + 1] : dx === -1 ? [X, Z + 1, X, Z] : dz === 1 ? [X + 1, Z + 1, X, Z + 1] : [X, Z, X + 1, Z];
        const along0 = dx !== 0 ? (dx === 1 ? y : y + 1) : (dz === 1 ? x + 1 : x);
        const along1 = dx !== 0 ? (dx === 1 ? y + 1 : y) : (dz === 1 ? x : x + 1);
        const depth = Y - Yn;
        const n: V3 = [dx, 0, dz];
        // A side texture is one tile deep (at this density, SIDE_TILE); a top running down the edge keeps its scale.
        const v = look.sideIsTop ? depth / S : depth / SIDE_TILE;
        addQuad(bucket(`side:${look.side?.url}:${sideGlow}`, look.side, sideGlow),
          [[ax, Y, az], [ax, Yn, az], [bx, Yn, bz], [bx, Y, bz]], n,
          [[along0 / S, 1], [along0 / S, 1 - v], [along1 / S, 1 - v], [along1 / S, 1]]);
        if (look.lip && floor === -Infinity) {
          const d = Math.min(LIP_DEPTH, depth);
          const e = 0.004;
          addQuad(bucket(`lip:${look.lip.url}`, look.lip, null, true),
            [[ax + dx * e, Y, az + dz * e], [ax + dx * e, Y - d, az + dz * e], [bx + dx * e, Y - d, bz + dz * e], [bx + dx * e, Y, bz + dz * e]], n,
            [[along0 / S, 1], [along0 / S, 1 - d / LIP_DEPTH], [along1 / S, 1 - d / LIP_DEPTH], [along1 / S, 1]]);
        }
      }
    }
  }

// ---- Props and dressing -----------------------------------------------------------------------------------------
  const occupied = new Set<string>();
  const placements: Dressing[] = [];
  for (const p of props) {
    placements.push({ id: p.id, at: p.at });
    occupied.add(p.at.join(","));
  }
  for (const d of area.dressing) {
    if (occupied.has(d.at.join(",")) || isPit(d.at[0], d.at[1])) continue;
    placements.push(d);
    occupied.add(d.at.join(","));
  }

  const curves = m.props.lighting?.flicker ?? {};
  const propJobs = placements.map(async (p) => {
    const r = resolveProp(m, p.id, p.variant);
    if (!r) return;
    const [x, y] = p.at;
    if (x < 0 || y < 0 || x >= map.w || y >= map.h) return;
    const img = await loadImage(r.url);
    if (!img) return;
    const e = r.entry;
    const canvases = sliceStrip(img, e.frames);
    const frames = canvases.map((c) => own(pixelTexture(new THREE.CanvasTexture(c), false)));
    // Glowing pixels: the prop's emissive mask strip, or (dungeon props carry none) the pixels in its light's colour.
    const maskImg = r.emissiveUrl ? await loadImage(r.emissiveUrl) : null;
    const lightColour = e.light ? e.light.colour[r.variant] ?? Object.values(e.light.colour)[0] ?? null : null;
    const maskCanvases = maskImg ? sliceStrip(maskImg, e.frames) : lightColour ? canvases.map((c) => glowMask(c, lightColour)) : null;
    const masks = maskCanvases?.map((c) => own(pixelTexture(new THREE.CanvasTexture(c), false))) ?? null;

    const [fw, fh] = [canvases[0]!.width, canvases[0]!.height];
    const geo = own(new THREE.PlaneGeometry(fw * SPRITE_PX, fh * SPRITE_PX));
    // PlaneGeometry is centred; move it so the anchor pixel (the ground contact) sits on the origin.
    geo.translate((fw / 2 - e.anchor[0]) * SPRITE_PX, (e.anchor[1] - fh / 2) * SPRITE_PX, 0);
    const mat = own(glowing(new THREE.MeshLambertMaterial({
      map: frames[0], alphaTest: 0.5, side: THREE.DoubleSide,
      emissive: masks ? new THREE.Color(1, 1, 1) : new THREE.Color(0, 0, 0), emissiveMap: masks?.[0] ?? null,
    })));
    const mesh = new THREE.Mesh(geo, mat);
    const i = y * map.w + x;
    const ground = e.layer === "ground";
    mesh.position.set(ox + x + 0.5, top(i) + (ground ? 0.01 : 0), oz + y + 0.5);
    if (ground) mesh.rotation.x = -Math.PI / 2;
    else billboards.push(mesh);
    world.add(mesh);
    if (frames.length > 1) {
      const a: Animated = { frames, fps: e.fps || 8, materials: [{ mat, key: "map" }] };
      animated.push(a);
      if (masks) animated.push({ frames: masks, fps: e.fps || 8, materials: [{ mat, key: "emissiveMap" }] });
    }

    const light = e.light;
    if (light && flickers.length < MAX_LIGHTS) {
      const pts = (Array.isArray(light.at[0]?.[0]) ? (light.at as number[][][])[0] : (light.at as number[][])) ?? [];
      const at = pts[0] ?? e.anchor;
      const colour = light.colour[r.variant] ?? Object.values(light.colour)[0] ?? "#ffda65";
      const base = 1.3 * Math.PI;
      const pl = new THREE.PointLight(colour, base, light.radius * 1.6, 1.1);
      pl.position.set(mesh.position.x, mesh.position.y + (e.anchor[1] - at[1]!) * SPRITE_PX + 0.1, mesh.position.z);
      world.add(pl);
      flickers.push({ light: pl, base, curve: curves[light.flicker] ?? [1], phase: rand() * 8 });
    }
  });

  // Glowing ground scatters its own lights over its tiles (one per `every` x `every` block), after the props' so a fire
  // always gets one.
  const glowJobs = Promise.all(propJobs).then(() => {
    const placed = new Set<string>();
    for (let i = 0; i < map.w * map.h && flickers.length < MAX_LIGHTS; i++) {
      const look = lookOf(i);
      if (!look.glow) continue;
      const em = look.top?.entry.emissive?.light ?? { every: 3, radius: 2, lift: 4, flicker: "pulse", power: 0.6 };
      const x = i % map.w, y = Math.floor(i / map.w);
      const cell = `${look.material}:${Math.floor(x / em.every)},${Math.floor(y / em.every)}`;
      if (placed.has(cell)) continue;
      placed.add(cell);
      const base = 1.3 * Math.PI * em.power;
      const pl = new THREE.PointLight(look.glow, base, em.radius * 1.6, 1.1);
      pl.position.set(ox + x + 0.5, top(i) + 0.4, oz + y + 0.5);
      world.add(pl);
      flickers.push({ light: pl, base, curve: curves[em.flicker] ?? [1], phase: rand() * 8 });
    }
  });

  await Promise.all([...pending, ...propJobs, glowJobs]);

  for (const [key, b] of buckets) {
    if (!b.tex || b.quads.idx.length === 0) continue;
    const geo = own(toGeometry(b.quads));
    const t = b.tex;
    const mat = own(glowing(new THREE.MeshLambertMaterial({
      map: t.frames[0],
      alphaTest: b.lip ? 0.5 : 0,
      transparent: false,
      emissive: t.masks ? new THREE.Color(1, 1, 1) : new THREE.Color(0, 0, 0),
      emissiveMap: t.masks?.[0] ?? null,
      polygonOffset: b.lip,
      polygonOffsetFactor: b.lip ? -1 : 0,
    })));
    if (b.lip) for (const f of t.frames) { f.wrapT = THREE.ClampToEdgeWrapping; f.needsUpdate = true; }
    const mesh = new THREE.Mesh(geo, mat);
    mesh.name = key;
    world.add(mesh);
    if (t.frames.length > 1) {
      animated.push({ frames: t.frames, fps: t.fps || 8, materials: [{ mat, key: "map" }] });
      if (t.masks) animated.push({ frames: t.masks, fps: t.fps || 8, materials: [{ mat, key: "emissiveMap" }] });
    }
  }

  // ---- Light and air ------------------------------------------------------------------------------------------------
  const sky = await backdrop(area, m, own);
  world.fog = new THREE.Fog(sky.horizon, 34, 90);
  const hemi = new THREE.HemisphereLight(area.ambient.sky, area.ambient.ground, area.ambient.intensity * Math.PI);
  world.add(hemi);
  const sun = new THREE.DirectionalLight(area.sun.colour, area.sun.intensity * Math.PI);
  world.add(sun, sun.target);

  const maxTop = Math.max(...solid.filter(Number.isFinite));
  return {
    world,
    backdrop: sky.scene,
    size: { w: map.w, h: map.h, top: maxTop },
    horizon: sky.horizon,
    resize: sky.resize,
    update(t, _camera, yaw, pitch) {
      for (const a of animated) {
        const f = Math.floor(t * a.fps) % a.frames.length;
        for (const { mat, key } of a.materials) {
          if (mat[key] !== a.frames[f]) mat[key] = a.frames[f]!;
        }
      }
      for (const fl of flickers) {
        // The manifest's curves are 8 steps; ease between them so a slow camera doesn't see the light tick.
        const k = t * 8 + fl.phase;
        const c0 = fl.curve[Math.floor(k) % fl.curve.length]!, c1 = fl.curve[(Math.floor(k) + 1) % fl.curve.length]!;
        fl.light.intensity = fl.base * (c0 + (c1 - c0) * (k % 1));
      }
      for (const b of billboards) b.rotation.y = yaw;
      // The sun stays at the screen's top-left whatever the camera does: up, to the camera's left, a little behind it.
      const right = new THREE.Vector3(Math.cos(yaw), 0, -Math.sin(yaw));
      const back = new THREE.Vector3(Math.sin(yaw), 0, Math.cos(yaw));
      sun.position.copy(right.multiplyScalar(-6)).add(back.multiplyScalar(3)).add(new THREE.Vector3(0, 10, 0));
      sun.target.position.set(0, 0, 0);
      sky.update(t, yaw, pitch);
    },
    dispose() {
      world.traverse((o) => { if (o instanceof THREE.Light) o.dispose(); });
      for (const x of owned) x.dispose();
      world.clear();
      sky.scene.clear();
    },
  };
}

/**
 * The pack's emissive rule: a glowing pixel skips lighting and shows its own colour (a little over, for the grade and
 * the eye to read as light). three's stock emissive adds a flat colour instead, which bleaches flames white, so the
 * emissive map here is a mask over the albedo.
 */
function glowing(mat: THREE.MeshLambertMaterial): THREE.MeshLambertMaterial {
  if (!mat.emissiveMap) return mat;
  mat.onBeforeCompile = (shader) => {
    shader.fragmentShader = shader.fragmentShader.replace("#include <emissivemap_fragment>", `
      #ifdef USE_EMISSIVEMAP
        float wfGlow = texture2D(emissiveMap, vEmissiveMapUv).r;
        totalEmissiveRadiance = diffuseColor.rgb * wfGlow * emissive * 1.12;
        diffuseColor.rgb *= 1.0 - wfGlow;
      #endif`);
  };
  return mat;
}

// ---- The backdrop: sky gradient, two parallax ranges and drifting clouds, drawn in low-res screen pixels -----------

/** Where each range's bottom edge sits, as a fraction of the screen from the top. */
const FAR_BASE = 0.66, NEAR_BASE = 0.74;

async function backdrop(area: Area, m: WorldManifests, own: <T extends { dispose(): void }>(x: T) => T) {
  const scene = new THREE.Scene();
  const bg = m.backgrounds;
  const skyImg = await loadImage(m.base + "backgrounds/" + (bg.sky.files[area.sky] ?? bg.sky.files.day));
  const horizon = skyImg ? pixelColour(skyImg, 0, skyImg.height - 1) : new THREE.Color("#c8c0b0");
  scene.background = horizon;

  const skyTex = skyImg ? own(pixelTexture(new THREE.Texture(skyImg))) : null;
  const skyMat = own(new THREE.MeshBasicMaterial({ map: skyTex, depthTest: false, depthWrite: false }));
  const skyMesh = new THREE.Mesh(own(new THREE.PlaneGeometry(1, 1)), skyMat);
  scene.add(skyMesh);

  const layer = async (file: string | undefined, parallax: number) => {
    if (!file) return null;
    const raw = await loadImage(m.base + "backgrounds/" + file);
    if (!raw) return null;
    const img = destreak(raw);
    const t = own(pixelTexture(new THREE.CanvasTexture(img)));
    t.wrapT = THREE.ClampToEdgeWrapping;
    const mat = own(new THREE.MeshBasicMaterial({ map: t, transparent: true, alphaTest: 0.5, depthTest: false, depthWrite: false }));
    const mesh = new THREE.Mesh(own(new THREE.PlaneGeometry(1, 1)), mat);
    scene.add(mesh);
    return { mesh, tex: t, img, parallax };
  };
  const far = await layer(bg.ranges.files[`${area.ranges}_far`], bg.ranges.parallax?.far ?? 0.1);
  const near = await layer(bg.ranges.files[`${area.ranges}_near`], bg.ranges.parallax?.near ?? 0.25);

  // Valley haze under the near range: its foot colour fading into the sky's horizon, so the island floats in air
  // rather than over a hard edge. The fog uses the same colour, so the island's far side sinks into it.
  const foot = near ? pixelColour(near.img, Math.floor(near.img.width / 2), near.img.height - 1) : horizon.clone();
  const haze = foot.clone().lerp(horizon, 0.35);
  const hazeGeo = own(new THREE.PlaneGeometry(1, 1));
  const cols: number[] = [];
  const pos = hazeGeo.attributes.position!;
  for (let i = 0; i < pos.count; i++) {
    const c = pos.getY(i) > 0 ? foot : haze.clone().lerp(horizon, 0.45);
    cols.push(c.r, c.g, c.b);
  }
  hazeGeo.setAttribute("color", new THREE.Float32BufferAttribute(cols, 3));
  const hazeMesh = new THREE.Mesh(hazeGeo, own(new THREE.MeshBasicMaterial({ vertexColors: true, depthTest: false, depthWrite: false })));
  hazeMesh.renderOrder = 4;
  scene.add(hazeMesh);

  const clouds: { mesh: THREE.Mesh; x: number; y: number; speed: number; w: number }[] = [];
  const cloudFiles = area.sky === "ashfall" ? [] : bg.clouds ?? [];
  for (let i = 0; i < 5 && cloudFiles.length; i++) {
    const img = await loadImage(m.base + "backgrounds/" + cloudFiles[i % cloudFiles.length]);
    if (!img) continue;
    const t = own(pixelTexture(new THREE.Texture(img), false));
    const mat = own(new THREE.MeshBasicMaterial({ map: t, transparent: true, alphaTest: 0.5, depthTest: false, depthWrite: false, opacity: 1 }));
    const mesh = new THREE.Mesh(own(new THREE.PlaneGeometry(img.width, img.height)), mat);
    mesh.renderOrder = 1;
    scene.add(mesh);
    clouds.push({ mesh, x: (i * 0.23 + 0.07) % 1, y: 0.1 + ((i * 0.37) % 1) * 0.28, speed: 1.2 + (i % 3) * 0.7, w: img.width });
  }
  skyMesh.renderOrder = 0;
  if (far) far.mesh.renderOrder = 2;
  if (near) near.mesh.renderOrder = 3;

  let W = 480, H = 270;
  const skyH = () => (skyImg ? skyImg.height * Math.max(1, Math.floor(H / skyImg.height)) : H);
  // Ranges sit just above the middle of the screen so the island covers their feet.
  const place = (l: typeof far, baseline: number, yaw: number, pitch: number) => {
    if (!l) return;
    const h = l.img.height;
    l.mesh.scale.set(W, h, 1);
    const shift = (yaw * 180 / Math.PI) * l.parallax * 6;
    l.tex.repeat.set(W / l.img.width, 1);
    l.tex.offset.set(Math.round(shift) / l.img.width, 0);
    const lift = Math.round((pitch - 0.3) * 120 * l.parallax);
    // `baseline` is where the image's bottom edge sits, as a fraction of the screen from the top (y runs up here).
    l.mesh.position.set(W / 2, Math.round(H * (1 - baseline)) + lift + h / 2, 0);
  };
  return {
    scene,
    horizon: haze,
    resize(w: number, h: number) {
      W = w; H = h;
      const sh = skyH();
      skyMesh.scale.set(W, sh, 1);
      skyMesh.position.set(W / 2, H - sh / 2, 0);
      if (skyTex) skyTex.repeat.set(W / (skyImg!.width), 1);
    },
    update(t: number, yaw: number, pitch: number) {
      place(far, FAR_BASE, yaw, pitch);
      place(near, NEAR_BASE, yaw, pitch);
      const foot = Math.round(H * (1 - NEAR_BASE)) + Math.round((pitch - 0.3) * 120 * (near?.parallax ?? 0.25));
      hazeMesh.scale.set(W, foot + 1, 1);
      hazeMesh.position.set(W / 2, (foot + 1) / 2 - 0.5, 0);
      for (const c of clouds) {
        const span = W + c.w * 2;
        const x = ((c.x * span + t * c.speed - (yaw * 180 / Math.PI) * 0.4) % span + span) % span - c.w;
        c.mesh.position.set(Math.round(x), Math.round(H - c.y * H), 0);
      }
    },
  };
}

// ---- Geometry helpers --------------------------------------------------------------------------------------------

type V3 = [number, number, number];
interface Quads { pos: number[]; nor: number[]; uv: number[]; idx: number[] }
const newQuads = (): Quads => ({ pos: [], nor: [], uv: [], idx: [] });

/** Adds a quad, winding it so it faces `normal` (three culls back faces). */
function addQuad(q: Quads, corners: V3[], normal: V3, uvs: [number, number][]): void {
  const [a, b, c] = corners as [V3, V3, V3];
  const u = [b[0] - a[0], b[1] - a[1], b[2] - a[2]], v = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];
  const cross = [u[1]! * v[2]! - u[2]! * v[1]!, u[2]! * v[0]! - u[0]! * v[2]!, u[0]! * v[1]! - u[1]! * v[0]!];
  const flip = cross[0]! * normal[0] + cross[1]! * normal[1] + cross[2]! * normal[2] < 0;
  const order = flip ? [0, 3, 2, 1] : [0, 1, 2, 3];
  const base = q.pos.length / 3;
  for (const k of order) {
    q.pos.push(...corners[k]!);
    q.nor.push(...normal);
    q.uv.push(...uvs[k]!);
  }
  q.idx.push(base, base + 1, base + 2, base, base + 2, base + 3);
}

function toGeometry(q: Quads): THREE.BufferGeometry {
  const g = new THREE.BufferGeometry();
  g.setAttribute("position", new THREE.Float32BufferAttribute(q.pos, 3));
  g.setAttribute("normal", new THREE.Float32BufferAttribute(q.nor, 3));
  g.setAttribute("uv", new THREE.Float32BufferAttribute(q.uv, 2));
  g.setIndex(q.idx);
  return g;
}

/** A small seeded random source, so every visit to an area flickers the same way. */
function mulberry(seed: number): () => number {
  let s = seed >>> 0;
  return () => {
    s = (s + 0x6d2b79f5) >>> 0;
    let t = s;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
