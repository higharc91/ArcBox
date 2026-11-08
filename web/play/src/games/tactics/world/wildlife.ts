import * as THREE from "three";
import type { Area, Dressing } from "./areas";
import { loadImage, pixelTexture, sliceStrip, type WorldManifests } from "./assets";

interface Creature {
  mesh: THREE.Mesh<THREE.PlaneGeometry, THREE.MeshBasicMaterial>;
  frames: THREE.Texture[];
  fps: number;
  phase: number;
  motion(t: number, mesh: THREE.Mesh): void;
}

/** Places the pack's native-density ambient art in the flyover, without adding map rules. */
export async function addWildlife(
  world: THREE.Scene,
  area: Area,
  placements: Dressing[],
  width: number,
  height: number,
  ground: (x: number, y: number) => number | null,
  m: WorldManifests,
  own: <T extends { dispose(): void }>(resource: T) => T,
  rand: () => number,
): Promise<(time: number, yaw: number) => void> {
  if (!m.ambient || area.sky === "ashfall") return () => {};

  const creatures: Creature[] = [];
  const textures = new Map<string, Promise<THREE.Texture[] | null>>();
  const jobs: Promise<void>[] = [];
  const ox = -width / 2, oz = -height / 2;
  const wooded = area.sky === "day" || area.sky === "swamp";

  const framesFor = (name: string, variant: string): Promise<THREE.Texture[] | null> => {
    const key = `${name}:${variant}`;
    let pending = textures.get(key);
    if (!pending) {
      pending = (async () => {
        const asset = m.ambient!.assets[name];
        const file = asset?.variants[variant];
        if (!asset || !file) return null;
        const img = await loadImage(`${m.base}ambient/${file}`);
        return img ? sliceStrip(img, asset.frames).map((frame) => own(pixelTexture(new THREE.CanvasTexture(frame), false))) : null;
      })();
      textures.set(key, pending);
    }
    return pending;
  };

  const add = (name: string, variant: string, x: number, y: number, z: number,
    motion: Creature["motion"], phase = rand() * 8) => {
    jobs.push((async () => {
      const asset = m.ambient!.assets[name];
      const frames = await framesFor(name, variant);
      if (!asset || !frames) return;
      const [w, h] = asset.frame;
      const mesh = new THREE.Mesh(
        own(new THREE.PlaneGeometry(w / 16, h / 16)),
        own(new THREE.MeshBasicMaterial({
          map: frames[0], alphaTest: asset.blend === "add" ? 0 : 0.5,
          transparent: asset.blend === "add", blending: asset.blend === "add" ? THREE.AdditiveBlending : THREE.NormalBlending,
          depthWrite: asset.blend !== "add", side: THREE.DoubleSide,
        })),
      );
      mesh.position.set(x, y, z);
      world.add(mesh);
      creatures.push({ mesh, frames, fps: asset.fps, phase, motion });
    })());
  };

  const trees = placements.filter((p) => /^(tree|swamp_tree|pine|pine_snowy)$/.test(p.id) && ground(...p.at) !== null);
  if (wooded) {
    // The squirrel stays on a real tree trunk; each cycle ends where it started.
    for (const tree of trees.filter((p) => p.id === "tree" || p.id === "swamp_tree").slice(0, 2)) {
      const [tx, ty] = tree.at;
      const base = ground(tx, ty)!;
      const x = ox + tx + 0.62;
      const z = oz + ty + 0.66;
      const start = rand() * 5;
      add("squirrel_climb", "russet", x, base + 2.1, z, (t, mesh) => {
        const cycle = ((t + start) % 8 + 8) % 8;
        const descent = cycle < 3 ? cycle / 3 : cycle < 5 ? 1 : cycle < 7 ? 1 - (cycle - 5) / 2 : 0;
        mesh.position.y = base + 2.1 - descent * 1.45;
        mesh.position.x = x + Math.sin(cycle * 2.5) * 0.035;
        mesh.scale.x = cycle >= 5 && cycle < 7 ? -1 : 1;
      }, start);
    }
    for (const tree of trees.slice(0, 4)) {
      const [tx, ty] = tree.at;
      const base = ground(tx, ty)!;
      for (let n = 0; n < 2; n++) {
        const x = ox + tx + 0.5 + (rand() - 0.5) * 1.6;
        const z = oz + ty + 0.5 + (rand() - 0.5) * 0.9;
        const phase = rand();
        const variant = tree.variant === "autumn" ? "autumn" : "green";
        add("leaf", variant, x, base + 2.9, z, (t, mesh) => {
          const fall = ((t * 0.34 + phase) % 1 + 1) % 1;
          mesh.position.y = base + 3.1 - fall * 2.85;
          mesh.position.x = x + Math.sin(t * 2.2 + phase * 6) * 0.27 + fall * 0.3;
          mesh.rotation.z = Math.sin(t * 3 + phase * 6) * 0.25;
        }, phase * 4);
      }
    }
  }

  const bird = area.sky === "day" ? "swallow" : area.sky === "overcast" ? "gull" : "crow";
  for (let n = 0; n < 3; n++) {
    const bx = width * (0.18 + n * 0.27), by = height * (n % 2 ? 0.68 : 0.27);
    const x = ox + bx, z = oz + by;
    const base = ground(Math.floor(bx), Math.floor(by)) ?? 0;
    const phase = rand() * Math.PI * 2;
    add("bird", bird, x, base + 3.6 + n * 0.35, z, (t, mesh) => {
      mesh.position.x = x + Math.sin(t * 0.48 + phase) * 2.1;
      mesh.position.z = z + Math.cos(t * 0.42 + phase) * 1.15;
      mesh.position.y = base + 3.6 + n * 0.35 + Math.sin(t * 1.6 + phase) * 0.2;
      mesh.scale.x = Math.cos(t * 0.48 + phase) < 0 ? -1 : 1;
    }, phase);
  }

  if (wooded) {
    const spots = placements.filter((p) => /^(tree|swamp_tree|bush|fern|reeds|tall_grass)$/.test(p.id) && ground(...p.at) !== null);
    for (const p of spots.slice(0, 7)) {
      const [tx, ty] = p.at;
      const base = ground(tx, ty)!;
      const x = ox + tx + 0.5 + (rand() - 0.5) * 0.8;
      const z = oz + ty + 0.5 + (rand() - 0.5) * 0.8;
      const phase = rand() * Math.PI * 2;
      const name = area.sky === "swamp" ? "fireflies" : "butterfly";
      const variant = area.sky === "swamp" ? "wisp" : rand() < 0.5 ? "yellow" : "monarch";
      add(name, variant, x, base + 0.9, z, (t, mesh) => {
        mesh.position.x = x + Math.sin(t * 1.25 + phase) * 0.42;
        mesh.position.z = z + Math.cos(t * 0.94 + phase) * 0.28;
        mesh.position.y = base + 0.8 + Math.sin(t * 2.4 + phase) * 0.25;
      }, phase);
    }
  }

  await Promise.all(jobs);
  return (time, yaw) => {
    for (const c of creatures) {
      c.motion(time, c.mesh);
      c.mesh.rotation.y = yaw;
      const frame = c.frames[Math.floor((time + c.phase) * c.fps) % c.frames.length]!;
      if (c.mesh.material.map !== frame) c.mesh.material.map = frame;
    }
  };
}
