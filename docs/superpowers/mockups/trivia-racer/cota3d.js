// REDLINE realistic mockup: Circuit of the Americas at real scale (metres), PBR + physical sky.
// Race distance 0..FINISH maps to one lap; s = distance along the lap in metres from cota.json point 0.
import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { makeF1Car } from './f1car2026.js';

export const FINISH = 20;
export const TEAM = { JO: '#2f6fd6', SAM: '#d8001f', ALEX: '#ff7a00', RIA: '#1ec8b0', MO: '#b9bcc4', KAI: '#1f8f5f' };
const SLOT = { JO: 0, SAM: 1, ALEX: 2, RIA: 3, MO: 4, KAI: 5 };

export async function loadCircuit() {
  const data = await (await fetch('cota.json')).json();
  const curve = new THREE.CatmullRomCurve3(data.points.map(([x, z]) => new THREE.Vector3(x, 0, z)), true, 'centripetal');
  const L = curve.getLength();
  const step = 2;
  const M = Math.round(L / step);
  const keys = data.elevation;
  const elev = s => {
    s = ((s % L) + L) % L;
    let i = 0; while (i < keys.length - 2 && keys[i + 1][0] < s) i++;
    const [s0, h0] = keys[i], [s1, h1] = keys[i + 1];
    const t = Math.min(1, Math.max(0, (s - s0) / (s1 - s0)));
    return h0 + (h1 - h0) * (0.5 - 0.5 * Math.cos(Math.PI * t));
  };
  const S = [];
  for (let i = 0; i <= M; i++) {
    const u = (i % M) / M;
    const p = curve.getPointAt(u), t = curve.getTangentAt(u);
    S.push({ s: u * L, p, t, n: new THREE.Vector3(t.z, 0, -t.x), h: elev(u * L) });   // n = left of travel
  }
  const K = 15;
  for (let i = 0; i < M; i++) {
    const a = S[(i - K + M) % M].t, b = S[(i + K) % M].t;
    S[i].curv = a.angleTo(b);
    S[i].turn = Math.sign(a.x * b.z - a.z * b.x);       // >0 turning right (in x-east/z-south), <0 left
  }
  S[M].curv = S[0].curv; S[M].turn = S[0].turn;
  const width = s => 15 + 9 * smoothBump(s, 120, 200, 400, 470);   // Turn 1 is famously wide
  return { data, curve, L, M, S, elev, width };
}

function smoothBump(s, a, b, c, d) {
  if (s <= a || s >= d) return 0;
  if (s < b) return smooth((s - a) / (b - a));
  if (s > c) return 1 - smooth((s - c) / (d - c));
  return 1;
}
const smooth = t => t * t * (3 - 2 * t);

function noiseTex(size, colors, speck, repeatWorld) {
  const c = document.createElement('canvas'); c.width = c.height = size;
  const g = c.getContext('2d');
  g.fillStyle = colors[0]; g.fillRect(0, 0, size, size);
  let seed = 11; const r = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  for (let i = 0; i < speck; i++) {
    g.fillStyle = colors[1 + Math.floor(r() * (colors.length - 1))];
    g.globalAlpha = 0.15 + r() * 0.35;
    const s = 1 + r() * (size / 60);
    g.fillRect(r() * size, r() * size, s, s);
  }
  g.globalAlpha = 1;
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace; t.wrapS = t.wrapT = THREE.RepeatWrapping; t.anisotropy = 16;
  return t;
}

export function buildScene({ canvas, width, height, circuit, cam, drivers, trails = [], smoke = [] }) {
  const { S, M, L, width: trackW } = circuit;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true });
  renderer.setPixelRatio(1.5);
  renderer.setSize(width, height, false);
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.toneMapping = THREE.NeutralToneMapping;   // Khronos PBR Neutral: keeps livery hues true (ACES skews saturated reds/oranges)
  renderer.toneMappingExposure = 0.78;

  const scene = new THREE.Scene();
  scene.fog = new THREE.FogExp2('#cfdbe6', 0.00038);

  // --- physical sky + image-based lighting from it
  const sunDir = new THREE.Vector3().setFromSphericalCoords(1, THREE.MathUtils.degToRad(90 - (cam.sunElevation ?? 24)), THREE.MathUtils.degToRad(cam.sunAzimuth ?? 215));
  // Visible sky: gradient dome with a sun glow (the physical Sky shader is too bright for track exposure).
  // The same dome feeds image-based lighting, so clear-coat paint reflects a blue sky, not white haze.
  const skyMat = new THREE.ShaderMaterial({
    side: THREE.BackSide, depthWrite: false, fog: false,
    uniforms: { sun: { value: sunDir }, zenith: { value: new THREE.Color('#3f74c4') }, horizon: { value: new THREE.Color('#cfdbe6') } },
    vertexShader: 'varying vec3 vDir; void main(){ vDir = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
    fragmentShader: `uniform vec3 sun; uniform vec3 zenith; uniform vec3 horizon; varying vec3 vDir;
      void main(){ float h = clamp(vDir.y, 0.0, 1.0); vec3 c = mix(horizon, zenith, pow(h, 0.55));
        float g = max(dot(normalize(vDir), normalize(sun)), 0.0); c += vec3(1.0, 0.85, 0.6) * (pow(g, 8.0) * 0.35 + pow(g, 400.0) * 2.0);
        gl_FragColor = vec4(c, 1.0); }`,
  });
  const dome = new THREE.Mesh(new THREE.SphereGeometry(8000, 48, 24), skyMat);
  scene.add(dome);
  const pmrem = new THREE.PMREMGenerator(renderer);
  const skyScene = new THREE.Scene(); skyScene.add(new THREE.Mesh(new THREE.SphereGeometry(100, 48, 24), skyMat));
  const ground = new THREE.Mesh(new THREE.CircleGeometry(90, 32), new THREE.MeshBasicMaterial({ color: '#5d6338' }));
  ground.rotation.x = -Math.PI / 2; ground.position.y = -2; skyScene.add(ground);
  scene.environment = pmrem.fromScene(skyScene, 0.02).texture;
  scene.environmentIntensity = 0.75;

  scene.add(new THREE.HemisphereLight('#c9dcf2', '#6b6440', 0.35));
  const sun = new THREE.DirectionalLight('#ffe7c4', 3.2);
  sun.castShadow = true;
  sun.shadow.mapSize.set(4096, 4096);
  sun.shadow.bias = -0.0002; sun.shadow.normalBias = 0.4;
  scene.add(sun, sun.target);

  // --- helpers on the lap
  const sample = s => {
    s = ((s % L) + L) % L;
    const f = s / L * M, i = Math.floor(f), k = f - i;
    const a = S[i], b = S[i + 1];
    const p = a.p.clone().lerp(b.p, k), t = a.t.clone().lerp(b.t, k).normalize();
    return { p, t, n: new THREE.Vector3(t.z, 0, -t.x), h: a.h + (b.h - a.h) * k, w: trackW(s) };
  };

  // spatial hash of samples for terrain/tree distance queries
  const CELL = 40, grid = new Map();
  S.forEach((q, i) => { if (i === M) return; const key = (Math.floor(q.p.x / CELL) + 512) * 4096 + Math.floor(q.p.z / CELL) + 512; (grid.get(key) ?? grid.set(key, []).get(key)).push(i); });
  const nearest = (x, z, reach = 3) => {
    const cx = Math.floor(x / CELL), cz = Math.floor(z / CELL);
    let best = Infinity, bi = 0;
    for (let r = 0; r <= reach; r++) {
      for (let dx = -r; dx <= r; dx++) for (let dz = -r; dz <= r; dz++) {
        if (Math.max(Math.abs(dx), Math.abs(dz)) !== r) continue;
        for (const i of grid.get((cx + dx + 512) * 4096 + cz + dz + 512) ?? []) {
          const d = (S[i].p.x - x) ** 2 + (S[i].p.z - z) ** 2;
          if (d < best) { best = d; bi = i; }
        }
      }
      if (best < (r * CELL) ** 2) break;
    }
    return { d: Math.sqrt(best), i: bi };
  };
  const terrainH = (x, z) => {
    const { d, i } = nearest(x, z, 12);
    const near = S[i].h - 0.18;
    const far = S[i].h * 0.55 + 4 + 3 * Math.sin(x * 0.004) * Math.cos(z * 0.005);
    const w = smooth(Math.min(1, Math.max(0, (d - (trackW(S[i].s) / 2 + 20)) / 260)));
    return near + (far - near) * w;
  };

  // --- terrain
  const box = new THREE.Box3().setFromPoints(S.map(q => q.p)).expandByScalar(700);
  const TW = box.max.x - box.min.x, TD = box.max.z - box.min.z, seg = 10;
  const tg = new THREE.PlaneGeometry(TW, TD, Math.round(TW / seg), Math.round(TD / seg));
  tg.rotateX(-Math.PI / 2);
  tg.translate((box.min.x + box.max.x) / 2, 0, (box.min.z + box.max.z) / 2);
  const tp = tg.attributes.position, tuv = tg.attributes.uv;
  for (let i = 0; i < tp.count; i++) {
    const x = tp.getX(i), z = tp.getZ(i);
    tp.setY(i, terrainH(x, z));
    tuv.setXY(i, x / 24, z / 24);
  }
  tg.computeVertexNormals();
  const grassTex = noiseTex(512, ['#7c8a47', '#6c7b3c', '#8d9853', '#9c955f', '#5f6f35', '#a8a06a'], 9000);
  const terrain = new THREE.Mesh(tg, new THREE.MeshStandardMaterial({ map: grassTex, roughness: 0.96, color: '#d6dcc4' }));
  terrain.receiveShadow = true;
  scene.add(terrain);

  // --- ribbons along the lap (a, b are lateral offsets; functions of the sample allowed)
  const asphaltTex = noiseTex(512, ['#3a3b3f', '#2c2d31', '#46474c', '#323337', '#55565b'], 26000);
  const ribbon = ({ a, b, lift, color, map, keep = () => true, colorAt, rough = 0.9, uvScale = 6 }) => {
    const pos = [], col = [], uv = [];
    const c = new THREE.Color();
    for (let i = 0; i < M; i++) {
      if (!keep(i)) continue;
      const q0 = S[i], q1 = S[i + 1];
      const A0 = typeof a === 'function' ? a(q0) : a, B0 = typeof b === 'function' ? b(q0) : b;
      const A1 = typeof a === 'function' ? a(q1) : a, B1 = typeof b === 'function' ? b(q1) : b;
      const P = (q, o) => [q.p.x + q.n.x * o, q.h + lift, q.p.z + q.n.z * o];
      const v0 = P(q0, A0), v1 = P(q0, B0), v2 = P(q1, A1), v3 = P(q1, B1);
      pos.push(...v0, ...v1, ...v2, ...v1, ...v3, ...v2);
      const u0 = q0.s / uvScale, u1 = (q0.s + (L / M)) / uvScale;
      uv.push(u0, A0 / uvScale, u0, B0 / uvScale, u1, A1 / uvScale, u0, B0 / uvScale, u1, B1 / uvScale, u1, A1 / uvScale);
      c.set(colorAt ? colorAt(i) : color);
      for (let k = 0; k < 6; k++) col.push(c.r, c.g, c.b);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    g.computeVertexNormals();
    const m = new THREE.Mesh(g, new THREE.MeshStandardMaterial({ vertexColors: true, map: map ?? null, roughness: rough, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: -1 - lift * 4, polygonOffsetUnits: -1 }));
    m.receiveShadow = true;
    scene.add(m);
    return m;
  };
  const half = q => trackW(q.s) / 2;
  const corner = i => S[i].curv > 0.22;
  // outside of a corner: turning right (turn > 0) means the outside is the left (+n) side
  const outsideSign = i => (S[i].turn > 0 ? 1 : -1);

  ribbon({ a: q => -half(q) - 1.2, b: q => half(q) + 1.2, lift: 0.02, color: '#9a9a96', rough: 0.95 });   // verge
  ribbon({ a: q => -half(q), b: q => half(q), lift: 0.06, color: '#ffffff', map: asphaltTex, rough: 0.88, uvScale: 9 });
  ribbon({ a: q => half(q) - 0.45, b: q => half(q) - 0.1, lift: 0.08, color: '#f1f1ee', rough: 0.6 });
  ribbon({ a: q => -half(q) + 0.1, b: q => -half(q) + 0.45, lift: 0.08, color: '#f1f1ee', rough: 0.6 });
  const stripe = (len, c1, c2) => i => (Math.floor(S[i].s / len) % 2 ? c1 : c2);
  ribbon({ a: q => half(q), b: q => half(q) + 1.6, lift: 0.1, keep: corner, colorAt: stripe(1.5, '#c8141e', '#f2f2f2'), rough: 0.55 });
  ribbon({ a: q => -half(q) - 1.6, b: q => -half(q), lift: 0.1, keep: corner, colorAt: stripe(1.5, '#c8141e', '#f2f2f2'), rough: 0.55 });
  // COTA's painted red/blue asphalt runoff on the outside of corners
  ribbon({ a: q => half(q) + 1.6, b: q => half(q) + 16, lift: 0.07, keep: i => corner(i) && outsideSign(i) > 0, colorAt: stripe(4, '#1d4f9e', '#b3122a'), rough: 0.8 });
  ribbon({ a: q => -half(q) - 16, b: q => -half(q) - 1.6, lift: 0.07, keep: i => corner(i) && outsideSign(i) < 0, colorAt: stripe(4, '#1d4f9e', '#b3122a'), rough: 0.8 });
  // start/finish line
  ribbon({ a: q => -half(q), b: q => half(q), lift: 0.09, keep: i => S[i].s < 1.6 || S[i].s > L - 0.4, color: '#f4f4f4', rough: 0.6 });

  // advertising wall + catch fence along both sides (vertical ribbons)
  {
    const adTex = (() => {
      const c = document.createElement('canvas'); c.width = 512; c.height = 64; const g = c.getContext('2d');
      g.fillStyle = '#f4f4f4'; g.fillRect(0, 0, 256, 64); g.fillStyle = '#e10600'; g.fillRect(256, 0, 256, 64);
      g.font = 'italic 900 46px "Titillium Web", sans-serif'; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillStyle = '#e10600'; g.fillText('REDLINE', 128, 34); g.fillStyle = '#fff'; g.fillText('ARCBOX', 384, 34);
      const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.wrapS = THREE.RepeatWrapping; t.anisotropy = 8; return t;
    })();
    const wall = (sign, off, y0, y1, material, uLen) => {
      const pos = [], uv = [];
      for (let i = 0; i < M; i++) {
        const q0 = S[i], q1 = S[i + 1];
        // An offset larger than the local radius of curvature folds back on itself: no wall on the tight inside.
        const R = (2 * 15 * (L / M)) / Math.max(q0.curv, 1e-3), inner = sign !== (q0.turn > 0 ? 1 : -1);
        if (inner && trackW(q0.s) / 2 + off > 0.7 * R) continue;
        const o0 = sign * (trackW(q0.s) / 2 + off), o1 = sign * (trackW(q1.s) / 2 + off);
        const a = [q0.p.x + q0.n.x * o0, q0.h, q0.p.z + q0.n.z * o0], b = [q1.p.x + q1.n.x * o1, q1.h, q1.p.z + q1.n.z * o1];
        // winding (a0, b0, a1) faces -n (right of travel): right for the left wall, flipped for the right wall,
        // so FrontSide always faces the track
        const A0 = [a[0], a[1] + y0, a[2]], B0 = [b[0], b[1] + y0, b[2]], A1 = [a[0], a[1] + y1, a[2]], B1 = [b[0], b[1] + y1, b[2]];
        if (sign > 0) pos.push(...A0, ...B0, ...A1, ...B0, ...B1, ...A1);
        else pos.push(...A0, ...A1, ...B0, ...B0, ...A1, ...B1);
        // read left-to-right from the track side: the right-hand wall runs its u against travel
        const u0 = sign * q0.s / uLen, u1 = sign * (q0.s + L / M) / uLen;
        if (sign > 0) uv.push(u0, 0, u1, 0, u0, 1, u1, 0, u1, 1, u0, 1);
        else uv.push(u0, 0, u0, 1, u1, 0, u1, 0, u0, 1, u1, 1);
      }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
      g.computeVertexNormals();
      const m = new THREE.Mesh(g, material); m.castShadow = material.opacity === 1; m.receiveShadow = true; scene.add(m);
      if (material.map) { const back = new THREE.Mesh(g, new THREE.MeshStandardMaterial({ color: '#8c9096', roughness: 0.8, side: THREE.BackSide })); back.receiveShadow = true; scene.add(back); }
    };
    const adMat = new THREE.MeshStandardMaterial({ map: adTex, roughness: 0.6, side: THREE.FrontSide });
    const fenceMat = new THREE.MeshStandardMaterial({ color: '#b8bdc4', roughness: 0.6, metalness: 0.5, transparent: true, opacity: 0.22, side: THREE.DoubleSide, depthWrite: false });
    for (const sign of [-1, 1]) { wall(sign, 19, 0, 1.1, adMat, 16); wall(sign, 19.4, 1.1, 4.2, fenceMat, 16); }
  }

  const std = (color, rough = 0.7, metal = 0) => new THREE.MeshStandardMaterial({ color, roughness: rough, metalness: metal });
  const place = (obj, s, lateral, up = 0) => {
    const q = sample(s);
    obj.position.set(q.p.x + q.n.x * lateral, q.h + up, q.p.z + q.n.z * lateral);
    obj.rotation.y = Math.atan2(-q.t.z, q.t.x);
    scene.add(obj);
    return obj;
  };
  const shadowAll = o => o.traverse(c => { if (c.isMesh) { c.castShadow = true; c.receiveShadow = true; } });

  // --- grandstand builder (length along x, faces -z i.e. toward its local -z side)
  const crowdPalette = ['#d9d9d6', '#cfc9bd', '#3b4a63', '#5a6170', '#e2ddd2', '#8f2a2a', '#2d2f36', '#a9b4c2', '#c96a3a', '#6d7f5a'];
  const grandstand = (len, rows, roofColor = '#e8e8ea') => {
    const g = new THREE.Group();
    for (let r = 0; r < rows; r++) {
      const st = new THREE.Mesh(new THREE.BoxGeometry(len, 1 + r * 0.9, 1.2), std(r % 2 ? '#c9cbd0' : '#bfc2c8', 0.8));
      st.position.set(0, (1 + r * 0.9) / 2, r * 1.2); g.add(st);
    }
    const n = Math.floor(len / 0.9) * rows;
    const crowd = new THREE.InstancedMesh(new THREE.CapsuleGeometry(0.22, 0.45, 2, 6), std('#ffffff', 0.95), n);
    const o = new THREE.Object3D(), c = new THREE.Color(); let k = 0, sd = 5;
    for (let r = 0; r < rows; r++) for (let x = -len / 2 + 0.6; x < len / 2 - 0.4 && k < n; x += 0.9) {
      sd = (sd * 48271) % 2147483647;
      o.position.set(x + (sd % 10) / 40, 1 + r * 0.9 + 0.45, r * 1.2 - 0.1); o.updateMatrix();
      crowd.setMatrixAt(k, o.matrix); crowd.setColorAt(k++, c.set(crowdPalette[sd % crowdPalette.length]));
    }
    crowd.count = k; g.add(crowd);
    const roof = new THREE.Mesh(new THREE.BoxGeometry(len + 4, 0.5, rows * 1.2 + 6), std(roofColor, 0.5, 0.3));
    roof.position.set(0, 4 + rows * 0.9, rows * 0.6 + 1); g.add(roof);
    for (let x = -len / 2; x <= len / 2; x += 24) { const p = new THREE.Mesh(new THREE.BoxGeometry(0.5, 4 + rows * 0.9, 0.5), std('#d0d2d6', 0.4, 0.6)); p.position.set(x, (4 + rows * 0.9) / 2, rows * 1.2 + 2); g.add(p); }
    shadowAll(g);
    return g;
  };

  // main grandstand (right side of the main straight) and pit building (left side)
  {
    const s = L - 190, q = sample(s), sideR = -1;
    const gs = grandstand(300, 22, '#f0f0f2');
    gs.position.set(q.p.x + q.n.x * sideR * (half(q) + 9), q.h, q.p.z + q.n.z * sideR * (half(q) + 9));
    gs.rotation.y = Math.atan2(-q.t.z, q.t.x);  // local +z is the right-hand side, so rows climb away from the track
    scene.add(gs);
    const pit = new THREE.Group();
    const body = new THREE.Mesh(new THREE.BoxGeometry(320, 12, 16), std('#e9eaec', 0.6)); body.position.y = 6; pit.add(body);
    const glass = new THREE.Mesh(new THREE.BoxGeometry(321, 3.5, 16.2), new THREE.MeshStandardMaterial({ color: '#1d2c44', roughness: 0.08, metalness: 0.9 })); glass.position.y = 9; pit.add(glass);
    shadowAll(pit);
    pit.position.set(q.p.x + q.n.x * (half(q) + 22), q.h - 0.2, q.p.z + q.n.z * (half(q) + 22));
    pit.rotation.y = Math.atan2(-q.t.z, q.t.x);
    scene.add(pit);
    ribbon({ a: q2 => half(q2) + 2, b: q2 => half(q2) + 13, lift: 0.05, keep: i => S[i].s > L - 360 || S[i].s < 20, color: '#56575c', map: asphaltTex, uvScale: 9 });
  }
  // start gantry with the five red lights
  {
    const g = new THREE.Group(), q = sample(0), w = half(q) + 3;
    for (const z of [-w, w]) { const p = new THREE.Mesh(new THREE.BoxGeometry(1, 9, 1), std('#2a2b30', 0.5, 0.6)); p.position.set(0, 4.5, z); g.add(p); }
    const beam = new THREE.Mesh(new THREE.BoxGeometry(1.2, 1.6, 2 * w + 1), std('#1b1c20', 0.4, 0.5)); beam.position.y = 9; g.add(beam);
    for (let i = -2; i <= 2; i++) { const l = new THREE.Mesh(new THREE.CylinderGeometry(0.28, 0.28, 0.2, 16), new THREE.MeshStandardMaterial({ color: '#330000', emissive: '#ff1a1a', emissiveIntensity: 0.6 })); l.rotation.z = Math.PI / 2; l.position.set(-0.7, 9, i * 1.1); g.add(l); }
    shadowAll(g);
    place(g, 0, 0);
  }
  // Turn 1 grandstand on the hill, on the outside of the hairpin
  {
    const s = 335, q = sample(s), side = S[Math.round(s / L * M)].turn > 0 ? 1 : -1;
    const gs = grandstand(120, 16, '#e10600');
    gs.position.set(q.p.x + q.n.x * side * (half(q) + 30), q.h - 0.5, q.p.z + q.n.z * side * (half(q) + 30));
    gs.rotation.y = Math.atan2(-q.t.z, q.t.x) + (side > 0 ? Math.PI : 0);
    scene.add(gs);
  }
  // Observation tower inside the Turns 16-18 complex
  {
    const pts = circuit.data.points.slice(128, 152);
    const cx = pts.reduce((a, p) => a + p[0], 0) / pts.length, cz = pts.reduce((a, p) => a + p[1], 0) / pts.length;
    const base = terrainH(cx, cz);
    const t = new THREE.Group();
    const col = new THREE.Mesh(new THREE.CylinderGeometry(1.4, 2.4, 72, 20), std('#eef0f2', 0.4, 0.2)); col.position.y = 36; t.add(col);
    const deck = new THREE.Mesh(new THREE.CylinderGeometry(8, 6.5, 3.5, 32), new THREE.MeshStandardMaterial({ color: '#cfd6de', roughness: 0.1, metalness: 0.8 })); deck.position.y = 70; t.add(deck);
    const red = std('#d0101c', 0.5, 0.2);
    for (let i = 0; i < 36; i++) {
      const a = i / 36 * Math.PI * 2;
      const from = new THREE.Vector3(Math.cos(a) * 1.6, 76, Math.sin(a) * 1.6), to = new THREE.Vector3(Math.cos(a + 0.5) * 26, 0, Math.sin(a + 0.5) * 26);
      const len = from.distanceTo(to);
      const c = new THREE.Mesh(new THREE.CylinderGeometry(0.16, 0.16, len, 6), red);
      c.position.copy(from).lerp(to, 0.5); c.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), to.clone().sub(from).normalize()); t.add(c);
    }
    shadowAll(t);
    t.position.set(cx, base, cz);
    scene.add(t);
  }

  // --- trees (live-oak clusters), away from the track
  {
    const geo = new THREE.IcosahedronGeometry(1, 2);
    const mat = new THREE.MeshStandardMaterial({ color: '#4d5a2a', roughness: 0.95 });
    const N = 2600, mesh = new THREE.InstancedMesh(geo, mat, N);
    const o = new THREE.Object3D(), c = new THREE.Color();
    let seed = 99; const r = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    let k = 0;
    for (let tries = 0; tries < 30000 && k < N; tries++) {
      // clustered placement: pick a cluster centre, scatter around it
      const x = box.min.x + 200 + r() * (TW - 400), z = box.min.z + 200 + r() * (TD - 400);
      const { d } = nearest(x, z, 4);
      if (d < 70) continue;
      const cl = 3 + Math.floor(r() * 9);
      for (let j = 0; j < cl && k < N; j++) {
        const x2 = x + (r() - 0.5) * 40, z2 = z + (r() - 0.5) * 40;
        const sc = 4 + r() * 5;
        o.position.set(x2, terrainH(x2, z2) + sc * 0.55, z2);
        o.scale.set(sc * (1 + r() * 0.4), sc * 0.75, sc * (1 + r() * 0.4));
        o.rotation.y = r() * 6; o.updateMatrix();
        mesh.setMatrixAt(k, o.matrix); mesh.setColorAt(k++, c.setHSL(0.2 + r() * 0.05, 0.32 + r() * 0.1, 0.2 + r() * 0.08));
      }
    }
    mesh.count = k; mesh.castShadow = true; mesh.receiveShadow = true;
    scene.add(mesh);
  }

  // --- cars
  const groups = {};
  for (const d of drivers) (groups[d.dist] ??= []).push(d);
  const carPose = d => {
    const grp = groups[d.dist], idx = grp.indexOf(d);
    // packs: 1 = centre, 2 = side by side, 3+ = three abreast with each further row 9 m back
    const n = grp.length;
    const lateral = d.lat ?? (n === 1 ? 0 : n === 2 ? [-3.4, 3.4][idx] : [-4.4, 0, 4.4][idx % 3]);
    const row = d.lat === undefined && n > 2 ? Math.floor(idx / 3) : 0;
    const s = d.dist / FINISH * L - row * 9 + (d.nudge ?? 0);
    const q = sample(s), q2 = sample(s + 2);
    const pos = new THREE.Vector3(q.p.x + q.n.x * lateral, q.h + 0.08, q.p.z + q.n.z * lateral);
    const fwd = new THREE.Vector3(q2.p.x - q.p.x, q2.h - q.h, q2.p.z - q.p.z).normalize();
    return { pos, fwd, s, lateral };
  };
  const cars = {};
  for (const d of drivers) {
    const car = d.team ? makeF1Car(d.team, d.driver) : makeCar(TEAM[d.name]);
    const { pos, fwd } = carPose(d);
    const up = new THREE.Vector3(0, 1, 0);
    const side = new THREE.Vector3().crossVectors(fwd, up).normalize();     // x * y = z (right-handed basis)
    const upC = new THREE.Vector3().crossVectors(side, fwd).normalize();
    car.quaternion.setFromRotationMatrix(new THREE.Matrix4().makeBasis(fwd, upC, side));
    if (d.spin) car.rotateY(d.spin);
    car.position.copy(pos);
    scene.add(car);
    cars[d.name] = car;
  }
  for (const tr of trails) {
    const d = drivers.find(x => x.name === tr.name), lat = carPose(d).lateral;
    const s0 = tr.from / FINISH * L, s1 = tr.to / FINISH * L - 8;
    ribbon({ a: lat - 0.9, b: lat + 0.9, lift: 0.11, keep: i => S[i].s >= s0 && S[i].s <= s1, color: d.color ?? TEAM[tr.name], rough: 0.4 });
  }
  const puffM = new THREE.MeshStandardMaterial({ color: '#eceef0', transparent: true, opacity: 0.5, roughness: 1, depthWrite: false });
  for (const sm of smoke) {
    const pos = cars[sm.name].position; let sd = 7;
    for (let i = 0; i < 22; i++) {
      sd = (sd * 48271) % 2147483647;
      const puff = new THREE.Mesh(new THREE.IcosahedronGeometry(0.8 + (sd % 100) / 45, 1), puffM);
      puff.position.set(pos.x - 4 + (sd % 80) / 10, pos.y + 0.6 + (sd % 30) / 14, pos.z - 4 + ((sd >> 4) % 80) / 10);
      scene.add(puff);
    }
  }

  // --- camera
  const camera = new THREE.PerspectiveCamera(cam.fov, width / height, 0.5, 9000);
  let aim;
  if (cam.track) {
    // Track-relative rig: position and aim given as (metres along the lap, lateral offset, height above the track).
    const c = cam.track, a = sample(c.s), b = sample(c.lookS);
    camera.position.set(a.p.x + a.n.x * c.lateral, a.h + c.up, a.p.z + a.n.z * c.lateral);
    aim = new THREE.Vector3(b.p.x + b.n.x * c.lookLateral, b.h + c.lookUp, b.p.z + b.n.z * c.lookLateral);
  } else if (cam.chase) {
    const c = cam.chase, car = cars[c.name];
    const fwd = new THREE.Vector3(1, 0, 0).applyQuaternion(car.quaternion); fwd.y = 0; fwd.normalize();
    const left = new THREE.Vector3(fwd.z, 0, -fwd.x);
    camera.position.copy(car.position).addScaledVector(fwd, c.fwd).addScaledVector(left, c.side); camera.position.y += c.up;
    aim = car.position.clone().addScaledVector(fwd, c.lookFwd).addScaledVector(left, c.lookSide ?? 0); aim.y += c.lookY ?? 1;
  } else {
    const names = cam.frame ?? Object.keys(cars);
    aim = names.reduce((a, n) => a.add(cars[n].position), new THREE.Vector3()).divideScalar(names.length);
    if (cam.aimOffset) aim.add(new THREE.Vector3(...cam.aimOffset));
    const off = new THREE.Vector3().setFromSphericalCoords(cam.dist, THREE.MathUtils.degToRad(90 - cam.elevation), THREE.MathUtils.degToRad(cam.azimuth));
    camera.position.copy(aim).add(off);
  }
  camera.lookAt(aim);
  sun.position.copy(aim).addScaledVector(sunDir, 600);
  sun.target.position.copy(aim);
  const sz = cam.shadowSize ?? 260;
  Object.assign(sun.shadow.camera, { left: -sz, right: sz, top: sz, bottom: -sz, near: 100, far: 1400 });
  sun.shadow.camera.updateProjectionMatrix();

  renderer.render(scene, camera);
  return {
    project(name, lift = 3) {
      const v = cars[name].position.clone(); v.y += lift; v.project(camera);
      return { x: (v.x + 1) / 2 * width, y: (1 - v.y) / 2 * height, visible: v.z < 1 && Math.abs(v.x) < 1.05 && Math.abs(v.y) < 1.05 };
    },
    carS: name => carPose(drivers.find(d => d.name === name)).s,
  };
}

// Modern F1 car at real scale: +x forward, origin on the ground between the axles. 5.6 m long, 2.0 m wide.
function makeCar(color) {
  const g = new THREE.Group();
  const paint = new THREE.MeshPhysicalMaterial({ color, metalness: 0.08, roughness: 0.38, clearcoat: 0.8, clearcoatRoughness: 0.05 });
  const carbon = new THREE.MeshStandardMaterial({ color: '#141416', roughness: 0.45, metalness: 0.3 });
  const white = new THREE.MeshPhysicalMaterial({ color: '#f4f4f4', roughness: 0.3, clearcoat: 1 });
  const rubber = new THREE.MeshStandardMaterial({ color: '#111113', roughness: 0.92 });
  const rim = new THREE.MeshStandardMaterial({ color: '#2a2a2e', roughness: 0.3, metalness: 0.9 });
  const stripe = new THREE.MeshStandardMaterial({ color: '#f2c200', roughness: 0.6 });
  const add = (geo, m, x, y, z, rx = 0, ry = 0, rz = 0) => { const o = new THREE.Mesh(geo, m); o.position.set(x, y, z); o.rotation.set(rx, ry, rz); g.add(o); return o; };

  // floor and plank
  add(new THREE.BoxGeometry(4.3, 0.04, 1.55), carbon, -0.25, 0.07, 0);
  // survival cell + engine cover: extruded side profile
  const prof = new THREE.Shape();
  [[-2.0, 0.12], [1.15, 0.14], [1.15, 0.55], [0.62, 0.68], [0.2, 0.62], [-0.18, 0.66], [-0.32, 1.02], [-0.62, 1.06], [-1.05, 0.86], [-1.95, 0.5], [-2.05, 0.3]]
    .forEach(([x, y], i) => (i ? prof.lineTo(x, y) : prof.moveTo(x, y)));
  const tub = new THREE.ExtrudeGeometry(prof, { depth: 0.62, bevelEnabled: true, bevelThickness: 0.07, bevelSize: 0.06, bevelSegments: 3, curveSegments: 4 });
  tub.translate(0, 0, -0.31);
  add(tub, paint, 0, 0, 0);
  // shark fin
  add(new THREE.BoxGeometry(0.9, 0.22, 0.015), paint, -1.35, 0.86, 0, 0, 0, -0.18);
  // nose: flattened taper from the tub to the front wing
  const nose = add(new THREE.CylinderGeometry(0.09, 0.27, 1.75, 16), paint, 2.0, 0.36, 0, 0, 0, -Math.PI / 2 + 0.08);
  nose.scale.set(0.62, 1, 1);   // local x is world y after the z-rotation: flatten the height
  // sidepods with rounded edges
  for (const z of [-0.62, 0.62]) {
    const pod = add(new RoundedBoxGeometry(1.8, 0.48, 0.5, 4, 0.14), paint, -0.25, 0.38, z);
    pod.scale.set(1, 1, 1);
    add(new RoundedBoxGeometry(0.5, 0.36, 0.42, 3, 0.1), carbon, 0.55, 0.42, z * 1.02);   // inlet
  }
  // halo
  const haloPath = new THREE.CatmullRomCurve3([[-0.2, 0.72, 0.36], [0.1, 0.9, 0.34], [0.48, 0.93, 0.16], [0.58, 0.93, 0], [0.48, 0.93, -0.16], [0.1, 0.9, -0.34], [-0.2, 0.72, -0.36]].map(p => new THREE.Vector3(...p)));
  add(new THREE.TubeGeometry(haloPath, 40, 0.035, 8), carbon, 0, 0, 0);
  add(new THREE.CylinderGeometry(0.025, 0.035, 0.32, 8), carbon, 0.7, 0.78, 0, 0, 0, 0.55);
  // driver helmet + visor
  add(new THREE.SphereGeometry(0.15, 20, 14), white, 0.12, 0.84, 0);
  add(new THREE.SphereGeometry(0.152, 20, 6, 0, Math.PI * 2, 1.15, 0.45), new THREE.MeshStandardMaterial({ color: '#0c0c10', roughness: 0.05, metalness: 0.8 }), 0.12, 0.84, 0, 0, Math.PI / 2, 0);
  // mirrors
  for (const z of [-0.5, 0.5]) { add(new THREE.BoxGeometry(0.1, 0.07, 0.16), paint, 0.72, 0.7, z); add(new THREE.CylinderGeometry(0.012, 0.012, 0.25, 6), carbon, 0.72, 0.6, z * 0.86, 0.9 * Math.sign(z), 0, 0); }
  // front wing: main plane + two flaps + endplates
  add(new THREE.BoxGeometry(0.55, 0.025, 1.95), paint, 2.78, 0.1, 0);
  add(new THREE.BoxGeometry(0.34, 0.02, 1.85), white, 2.72, 0.16, 0, 0, 0, -0.32);
  add(new THREE.BoxGeometry(0.28, 0.02, 1.8), carbon, 2.62, 0.22, 0, 0, 0, -0.55);
  for (const z of [-0.98, 0.98]) add(new THREE.BoxGeometry(0.62, 0.24, 0.02), carbon, 2.72, 0.17, z);
  // rear wing: main plane, DRS flap, endplates, beam wing, pylon
  add(new THREE.BoxGeometry(0.34, 0.03, 1.02), paint, -2.32, 0.9, 0, 0, 0, 0.1);
  add(new THREE.BoxGeometry(0.24, 0.025, 1.0), white, -2.18, 0.99, 0, 0, 0, 0.45);
  for (const z of [-0.52, 0.52]) add(new THREE.BoxGeometry(0.72, 0.52, 0.025), paint, -2.28, 0.77, z);
  add(new THREE.BoxGeometry(0.22, 0.02, 0.9), carbon, -2.2, 0.42, 0);
  add(new THREE.BoxGeometry(0.08, 0.5, 0.05), carbon, -2.2, 0.66, 0);
  // wheels (r 0.36): tyre, rim, wheel cover, medium-compound sidewall stripe
  const wheel = (x, z, w) => {
    const side = Math.sign(z);
    add(new THREE.CylinderGeometry(0.36, 0.36, w, 36), rubber, x, 0.36, z, Math.PI / 2);
    add(new THREE.CylinderGeometry(0.235, 0.235, w + 0.006, 28), rim, x, 0.36, z, Math.PI / 2);
    add(new THREE.CylinderGeometry(0.2, 0.2, 0.02, 28), carbon, x, 0.36, z + side * (w / 2 + 0.006), Math.PI / 2);
    add(new THREE.TorusGeometry(0.3, 0.012, 6, 40), stripe, x, 0.36, z + side * (w / 2 + 0.002));
  };
  wheel(1.8, 0.8, 0.3); wheel(1.8, -0.8, 0.3); wheel(-1.82, 0.76, 0.4); wheel(-1.82, -0.76, 0.4);
  // suspension wishbones
  const arm = (a, b) => {
    const A = new THREE.Vector3(...a), B = new THREE.Vector3(...b), len = A.distanceTo(B);
    const o = add(new THREE.CylinderGeometry(0.018, 0.018, len, 6), carbon, 0, 0, 0);
    o.position.copy(A).lerp(B, 0.5); o.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), B.clone().sub(A).normalize());
  };
  for (const s of [-1, 1]) {
    arm([1.65, 0.28, s * 0.3], [1.8, 0.3, s * 0.62]); arm([1.95, 0.3, s * 0.3], [1.8, 0.3, s * 0.62]);
    arm([1.6, 0.5, s * 0.32], [1.8, 0.48, s * 0.62]); arm([2.0, 0.48, s * 0.28], [1.8, 0.48, s * 0.62]);
    arm([-1.65, 0.3, s * 0.36], [-1.82, 0.3, s * 0.52]); arm([-1.6, 0.5, s * 0.36], [-1.82, 0.5, s * 0.52]);
  }
  g.traverse(c => { if (c.isMesh) { c.castShadow = true; c.receiveShadow = true; } });
  return g;
}
