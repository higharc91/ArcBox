// 2026-regulation F1 car, built procedurally at real scale (metres).
// +x forward, origin on the ground midway between the axles. Wheelbase 3.4 m, width 1.9 m.
// Bodywork is lofted through superellipse cross-sections; wings are NACA-profile extrusions;
// tyres are lathed. Livery is painted with per-vertex colour zones. No logos.
import * as THREE from 'three';

const WB = 3.4, FX = WB / 2, RX = -WB / 2;
const TYRE_R = 0.355, FRONT_W = 0.28, REAR_W = 0.375, HALF_WIDTH = 0.95;

// --- geometry helpers ------------------------------------------------------------------------

// Catmull-Rom densify every numeric field of the stations so the loft is smooth along x.
function densify(st, per = 5) {
  const keys = Object.keys(st[0]).filter(k => typeof st[0][k] === 'number');
  const out = [];
  for (let i = 0; i < st.length - 1; i++) {
    const p0 = st[Math.max(0, i - 1)], p1 = st[i], p2 = st[i + 1], p3 = st[Math.min(st.length - 1, i + 2)];
    for (let j = 0; j < per; j++) {
      const t = j / per, t2 = t * t, t3 = t2 * t, o = {};
      for (const k of keys) {
        const a = p0[k] ?? p1[k], b = p1[k], c = p2[k], d = p3[k] ?? p2[k];
        o[k] = 0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
      }
      out.push(o);
    }
  }
  out.push({ ...st[st.length - 1] });
  return out;
}

// Loft a closed superellipse tube through stations {x, y, z?, w (half-width), h (half-height), n (squareness), bottom?}.
// paint(color, x, y, z, rel) sets each vertex colour; rel is -1 (bottom) .. +1 (top) within the section.
function loft(stations, paint, segs = 40) {
  const st = densify(stations);
  const pos = [], col = [], idx = [], c = new THREE.Color();
  for (const s of st) {
    const e = 2 / (s.n ?? 2.6);
    for (let k = 0; k < segs; k++) {
      const th = (k / segs) * Math.PI * 2, cs = Math.cos(th), sn = Math.sin(th);
      const zz = s.w * Math.sign(cs) * Math.abs(cs) ** e;
      let yy = s.h * Math.sign(sn) * Math.abs(sn) ** e;
      if (yy < 0) yy *= s.bottom ?? 0.55;
      const x = s.x, y = s.y + yy, z = (s.z ?? 0) + zz;
      pos.push(x, y, z);
      paint(c, x, y, z, Math.sign(sn) * Math.abs(sn) ** e);
      col.push(c.r, c.g, c.b);
    }
  }
  const R = st.length;
  for (let i = 0; i < R - 1; i++) for (let k = 0; k < segs; k++) {
    const a = i * segs + k, b = i * segs + ((k + 1) % segs), d = (i + 1) * segs + k, e = (i + 1) * segs + ((k + 1) % segs);
    idx.push(a, b, d, b, e, d);
  }
  for (const [i, flip] of [[0, true], [R - 1, false]]) {
    const s = st[i], ci = pos.length / 3;
    pos.push(s.x, s.y, s.z ?? 0); col.push(col[i * segs * 3], col[i * segs * 3 + 1], col[i * segs * 3 + 2]);
    for (let k = 0; k < segs; k++) { const a = i * segs + k, b = i * segs + ((k + 1) % segs); flip ? idx.push(ci, b, a) : idx.push(ci, a, b); }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  g.setIndex(idx);
  g.computeVertexNormals();
  return { geometry: g, top: x => sampleTop(st, x) };
}
function sampleTop(st, x) {
  for (let i = 0; i < st.length - 1; i++) {
    const a = st[i], b = st[i + 1];
    if ((x <= a.x && x >= b.x) || (x >= a.x && x <= b.x)) { const t = (x - a.x) / (b.x - a.x || 1); return (a.y + a.h) + ((b.y + b.h) - (a.y + a.h)) * t; }
  }
  return st[0].y + st[0].h;
}

// NACA 4-digit style airfoil (inverted camber for downforce), leading edge at local x = 0, chord towards -x.
function airfoil(chord, span, thick = 0.1, camber = 0.06) {
  const N = 18, up = [], lo = [];
  for (let i = 0; i <= N; i++) {
    const x = (1 - Math.cos((i / N) * Math.PI)) / 2;
    const yt = 5 * thick * (0.2969 * Math.sqrt(x) - 0.126 * x - 0.3516 * x * x + 0.2843 * x ** 3 - 0.1036 * x ** 4);
    const p = 0.4, yc = -(x < p ? camber / (p * p) * (2 * p * x - x * x) : camber / ((1 - p) ** 2) * ((1 - 2 * p) + 2 * p * x - x * x));
    up.push([-x * chord, (yc + yt) * chord]); lo.push([-x * chord, (yc - yt) * chord]);
  }
  const shape = new THREE.Shape();
  shape.moveTo(...up[0]); up.slice(1).forEach(p => shape.lineTo(...p)); lo.reverse().forEach(p => shape.lineTo(...p));
  const g = new THREE.ExtrudeGeometry(shape, { depth: span, bevelEnabled: false, curveSegments: 1 });
  g.translate(0, 0, -span / 2);
  return g;
}

function plate(points, thickness) {
  const s = new THREE.Shape(); s.moveTo(...points[0]); points.slice(1).forEach(p => s.lineTo(...p));
  const g = new THREE.ExtrudeGeometry(s, { depth: thickness, bevelEnabled: true, bevelThickness: 0.004, bevelSize: 0.004, bevelSegments: 1 });
  g.translate(0, 0, -thickness / 2);
  return g;
}

function decal(text, color, w, h, font = 'italic 900 150px "Titillium Web", sans-serif') {
  const c = document.createElement('canvas'); c.width = 256; c.height = Math.round(256 * h / w);
  const g = c.getContext('2d'); g.font = font; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillStyle = color;
  g.fillText(text, c.width / 2, c.height / 2 + 8);
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
  return new THREE.Mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshStandardMaterial({ map: t, transparent: true, alphaTest: 0.35, roughness: 0.4, polygonOffset: true, polygonOffsetFactor: -2 }));
}

// --- the car --------------------------------------------------------------------------------

export function makeF1Car(team, driver) {
  const L = team.livery, matte = team.finish === 'matte';
  const g = new THREE.Group();
  const mats = new Map();
  const paintMat = (color) => {
    const key = color ?? 'vc';
    if (!mats.has(key)) mats.set(key, new THREE.MeshPhysicalMaterial({
      color: color ?? '#ffffff', vertexColors: !color, side: THREE.DoubleSide,
      roughness: matte ? 0.62 : 0.3, metalness: matte ? 0 : 0.05, clearcoat: matte ? 0 : 0.7, clearcoatRoughness: 0.06, envMapIntensity: 0.55,
    }));
    return mats.get(key);
  };
  const carbon = new THREE.MeshStandardMaterial({ color: '#151518', roughness: 0.38, metalness: 0.35 });
  const add = (geo, m, x = 0, y = 0, z = 0, rx = 0, ry = 0, rz = 0) => { const o = new THREE.Mesh(geo, m); o.position.set(x, y, z); o.rotation.set(rx, ry, rz); g.add(o); return o; };
  const col = hex => new THREE.Color(hex);
  const C = Object.fromEntries(Object.entries(L).map(([k, v]) => [k, col(v)]));

  // Chassis: nose tip -> survival cell -> cockpit -> airbox -> engine cover -> gearbox
  const chassis = [
    { x: 2.98, y: 0.205, w: 0.05, h: 0.045, n: 2.2 },
    { x: 2.75, y: 0.235, w: 0.11, h: 0.08, n: 2.4 },
    { x: 2.3, y: 0.30, w: 0.17, h: 0.12, n: 2.6 },
    { x: 1.75, y: 0.38, w: 0.23, h: 0.17, n: 3.0 },
    { x: 1.2, y: 0.45, w: 0.29, h: 0.215, n: 3.4 },
    { x: 0.7, y: 0.5, w: 0.34, h: 0.255, n: 3.8 },
    { x: 0.3, y: 0.48, w: 0.37, h: 0.235, n: 4.0 },
    { x: -0.15, y: 0.48, w: 0.38, h: 0.25, n: 4.0 },
    { x: -0.42, y: 0.62, w: 0.27, h: 0.4, n: 3.0 },
    { x: -0.75, y: 0.6, w: 0.22, h: 0.35, n: 2.8 },
    { x: -1.2, y: 0.5, w: 0.17, h: 0.25, n: 2.6 },
    { x: -1.65, y: 0.4, w: 0.13, h: 0.17, n: 2.4 },
    { x: -2.0, y: 0.345, w: 0.1, h: 0.11, n: 2.4 },
    { x: -2.2, y: 0.34, w: 0.06, h: 0.06, n: 2.2 },
  ];
  const body = loft(chassis, (c, x, y, z, rel) => {
    if (rel < -0.55) return c.copy(col('#141417'));
    if (x > 2.5) return c.copy(C.nose);
    if (rel > 0.02 && rel < 0.16) return c.copy(C.stripe);
    return c.copy(rel <= 0.02 ? C.side : C.top);
  });
  add(body.geometry, paintMat());

  // Sidepods (2026 downwash ramps), each side
  for (const s of [-1, 1]) {
    const pod = loft([
      { x: 0.92, y: 0.43, z: s * 0.56, w: 0.15, h: 0.15, n: 3.2 },
      { x: 0.7, y: 0.44, z: s * 0.6, w: 0.19, h: 0.17, n: 3.4 },
      { x: 0.2, y: 0.42, z: s * 0.59, w: 0.19, h: 0.16, n: 3.4 },
      { x: -0.4, y: 0.36, z: s * 0.52, w: 0.15, h: 0.13, n: 3.0 },
      { x: -0.95, y: 0.29, z: s * 0.4, w: 0.1, h: 0.09, n: 2.8 },
      { x: -1.35, y: 0.24, z: s * 0.3, w: 0.05, h: 0.05, n: 2.4 },
    ], (c, x, y, z, rel) => c.copy(rel > 0.3 ? C.top : rel > 0.12 ? C.stripe : C.side), 32);
    add(pod.geometry, paintMat());
    const inlet = add(new THREE.CircleGeometry(1, 28), carbon, 0.925, 0.43, s * 0.56, 0, Math.PI / 2, 0);
    inlet.scale.set(0.13, 0.12, 1);
  }

  // Cockpit opening, headrest, helmet + visor, halo
  const opening = add(new THREE.SphereGeometry(1, 24, 10), carbon, 0.32, 0.72, 0); opening.scale.set(0.34, 0.03, 0.21);
  add(new THREE.SphereGeometry(0.142, 24, 16), new THREE.MeshPhysicalMaterial({ color: '#f2f2f2', roughness: 0.25, clearcoat: 1 }), 0.15, 0.83, 0);
  add(new THREE.SphereGeometry(0.144, 24, 8, -Math.PI / 2.6, Math.PI / 1.3, 1.25, 0.42), new THREE.MeshStandardMaterial({ color: '#0b0b10', roughness: 0.05, metalness: 0.9 }), 0.15, 0.83, 0, 0, Math.PI / 2, 0);
  add(new THREE.SphereGeometry(0.145, 24, 6, 0, Math.PI * 2, 0.9, 0.18), paintMat(L.stripe), 0.15, 0.83, 0);   // helmet band in team accent
  const haloPath = new THREE.CatmullRomCurve3([[-0.2, 0.74, 0.34], [0.05, 0.9, 0.33], [0.42, 0.94, 0.17], [0.54, 0.945, 0], [0.42, 0.94, -0.17], [0.05, 0.9, -0.33], [-0.2, 0.74, -0.34]].map(p => new THREE.Vector3(...p)));
  add(new THREE.TubeGeometry(haloPath, 60, 0.034, 12), paintMat(L.halo));
  const pillar = new THREE.CatmullRomCurve3([[0.54, 0.945, 0], [0.66, 0.86, 0], [0.72, 0.76, 0]].map(p => new THREE.Vector3(...p)));
  add(new THREE.TubeGeometry(pillar, 16, 0.03, 10), paintMat(L.halo));

  // Airbox inlet, T-cam (black = team's first car, yellow = second), engine-cover fin, exhaust
  const ab = add(new THREE.CircleGeometry(1, 28), carbon, -0.27, 0.91, 0, 0, Math.PI / 2, 0); ab.scale.set(0.09, 0.075, 1);
  add(new THREE.BoxGeometry(0.07, 0.035, 0.28), new THREE.MeshStandardMaterial({ color: driver.tcam === 'yellow' ? '#f6e600' : '#101012', roughness: 0.5 }), -0.42, 1.04, 0);
  add(plate([[-0.55, 0.98], [-1.55, 0.62], [-1.55, 0.7], [-0.6, 1.03]], 0.012), paintMat(L.top));
  add(new THREE.CylinderGeometry(0.045, 0.05, 0.12, 16), new THREE.MeshStandardMaterial({ color: '#2a2a2d', roughness: 0.3, metalness: 0.9 }), -2.22, 0.42, 0, 0, 0, Math.PI / 2);

  // Floor (narrows ahead of the rear tyres), floor-edge fences, diffuser
  const floorShape = [[1.3, 0.3], [0.9, 0.7], [-1.15, 0.72], [-1.42, 0.52], [-2.05, 0.52], [-2.05, -0.52], [-1.42, -0.52], [-1.15, -0.72], [0.9, -0.7], [1.3, -0.3]];
  const floor = new THREE.Shape(); floor.moveTo(...floorShape[0]); floorShape.slice(1).forEach(p => floor.lineTo(...p));
  const fg = new THREE.ExtrudeGeometry(floor, { depth: 0.025, bevelEnabled: false }); fg.rotateX(Math.PI / 2);
  add(fg, carbon, 0, 0.075, 0);
  for (const s of [-1, 1]) add(plate([[0.9, 0.05], [-1.1, 0.05], [-1.1, 0.12], [0.6, 0.16]], 0.012), carbon, 0, 0, s * 0.71);
  add(new THREE.BoxGeometry(0.6, 0.012, 1.02), carbon, -1.78, 0.16, 0, 0, 0, -0.36);

  // Front wing: main plane + two active flaps, endplates, nose pylons
  add(airfoil(0.44, 1.72, 0.09, 0.05), paintMat(L.wing), 3.05, 0.1, 0);
  add(airfoil(0.27, 1.62, 0.1, 0.06), paintMat(L.top), 2.72, 0.155, 0, 0, 0, -0.26);
  add(airfoil(0.2, 1.56, 0.1, 0.06), paintMat(L.wing), 2.56, 0.215, 0, 0, 0, -0.5);
  for (const s of [-1, 1]) add(plate([[3.1, 0.04], [2.4, 0.04], [2.33, 0.3], [2.55, 0.33], [3.02, 0.17]], 0.016), paintMat(L.endplate), 0, 0, s * 0.865);
  for (const s of [-1, 1]) add(plate([[2.85, 0.12], [2.6, 0.12], [2.65, 0.24], [2.82, 0.2]], 0.01), carbon, 0, 0, s * 0.12);

  // Rear wing: main plane + active flap, endplates with rain-light LEDs, swan-neck pylon. (No beam wing in 2026.)
  add(airfoil(0.36, 0.98, 0.11, 0.07), paintMat(L.wing), -2.05, 0.84, 0, 0, 0, -0.08);
  add(airfoil(0.25, 0.98, 0.1, 0.06), paintMat(L.top), -2.2, 0.98, 0, 0, 0, -0.5);
  const led = new THREE.MeshStandardMaterial({ color: '#330000', emissive: '#ff1010', emissiveIntensity: 1.2 });
  for (const s of [-1, 1]) {
    add(plate([[-1.97, 0.36], [-2.62, 0.36], [-2.64, 1.1], [-2.12, 1.12], [-1.94, 0.9]], 0.02), paintMat(L.endplate), 0, 0, s * 0.5);
    add(new THREE.BoxGeometry(0.015, 0.28, 0.025), led, -2.645, 0.75, s * 0.5);
    const n = decal(String(driver.number), L.number === L.endplate ? L.top : L.number, 0.34, 0.24);
    n.position.set(-2.3, 0.62, s * 0.513); n.rotation.y = s > 0 ? 0 : Math.PI; g.add(n);
  }
  add(plate([[-2.0, 0.33], [-2.12, 0.33], [-2.2, 0.82], [-2.1, 0.82]], 0.03), carbon);
  add(new THREE.BoxGeometry(0.03, 0.06, 0.12), led, -2.24, 0.33, 0);   // rear rain light

  // Nose number (on the nose top, following its slope)
  {
    const x = 2.05, y = body.top(x), slope = Math.atan2(body.top(x - 0.1) - body.top(x + 0.1), 0.2);
    const n = decal(String(driver.number), L.number, 0.36, 0.24);
    // text 'up' points to the rear so the number reads upright from in front of the car
    n.position.set(x, y + 0.004, 0); n.rotation.set(-Math.PI / 2, 0, Math.PI / 2); n.rotateX(slope); g.add(n);
  }

  // Mirrors on stalks
  for (const s of [-1, 1]) {
    add(new THREE.BoxGeometry(0.06, 0.07, 0.16), paintMat(L.top), 0.6, 0.72, s * 0.52);
    add(new THREE.BoxGeometry(0.004, 0.055, 0.14), new THREE.MeshStandardMaterial({ color: '#9fb3c8', roughness: 0.05, metalness: 1 }), 0.567, 0.72, s * 0.52);
    add(new THREE.CylinderGeometry(0.012, 0.012, 0.2, 8), carbon, 0.62, 0.64, s * 0.46, s * 0.9, 0, 0);
  }

  // Wheels: lathed tyre with shoulders, compound stripe, spoked rim, brake duct
  const tyreMat = new THREE.MeshStandardMaterial({ color: '#141416', roughness: 0.88 });
  const rimMat = new THREE.MeshStandardMaterial({ color: '#202024', roughness: 0.25, metalness: 0.95 });
  const stripe = new THREE.MeshStandardMaterial({ color: '#f2c200', roughness: 0.6 });
  const wheel = (x, w, s) => {
    const z = s * (HALF_WIDTH - w / 2), R = TYRE_R, Rr = 0.235;
    const prof = [[Rr, -w / 2], [R - 0.05, -w / 2], [R - 0.016, -w / 2 + 0.018], [R, -w / 2 + 0.055], [R, w / 2 - 0.055], [R - 0.016, w / 2 - 0.018], [R - 0.05, w / 2], [Rr, w / 2]].map(([a, b]) => new THREE.Vector2(a, b));
    add(new THREE.LatheGeometry(prof, 72), tyreMat, x, R, z, Math.PI / 2);
    add(new THREE.TorusGeometry(R - 0.055, 0.008, 6, 72), stripe, x, R, z + s * (w / 2 + 0.001));
    add(new THREE.CylinderGeometry(Rr, Rr, w - 0.02, 48), rimMat, x, R, z, Math.PI / 2);
    for (let k = 0; k < 10; k++) {
      const sp = add(new THREE.BoxGeometry(0.03, Rr * 0.92, 0.012), rimMat, x, R, z + s * (w / 2 - 0.004));
      sp.rotation.z = (k / 10) * Math.PI * 2; sp.geometry.translate(0, Rr * 0.46, 0);
    }
    add(new THREE.CylinderGeometry(0.05, 0.05, 0.03, 20), rimMat, x, R, z + s * (w / 2 + 0.005), Math.PI / 2);
    add(new THREE.CylinderGeometry(0.16, 0.16, 0.1, 24), carbon, x, R, z - s * (w / 2 + 0.03), Math.PI / 2);
  };
  for (const s of [-1, 1]) { wheel(FX, FRONT_W, s); wheel(RX, REAR_W, s); }

  // Suspension: wishbones + push/pull rods (carbon aero-section tubes)
  const arm = (a, b, r = 0.016) => {
    const A = new THREE.Vector3(...a), B = new THREE.Vector3(...b);
    const o = add(new THREE.CylinderGeometry(r, r, A.distanceTo(B), 8), carbon);
    o.position.copy(A).lerp(B, 0.5); o.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), B.sub(A).normalize()); o.scale.set(1.8, 1, 0.6);
  };
  for (const s of [-1, 1]) {
    const fz = s * (HALF_WIDTH - FRONT_W / 2 - 0.12), rz = s * (HALF_WIDTH - REAR_W / 2 - 0.13);
    arm([FX - 0.25, 0.3, s * 0.2], [FX, 0.27, fz]); arm([FX + 0.2, 0.3, s * 0.18], [FX, 0.27, fz]);
    arm([FX - 0.2, 0.5, s * 0.22], [FX, 0.47, fz]); arm([FX + 0.22, 0.48, s * 0.16], [FX, 0.47, fz]);
    arm([FX - 0.05, 0.6, s * 0.2], [FX, 0.3, fz * 0.9], 0.012);
    arm([RX + 0.25, 0.3, s * 0.3], [RX, 0.28, rz]); arm([RX - 0.25, 0.3, s * 0.25], [RX, 0.28, rz]);
    arm([RX + 0.2, 0.5, s * 0.3], [RX, 0.5, rz]); arm([RX - 0.2, 0.48, s * 0.2], [RX, 0.5, rz]);
  }

  g.traverse(o => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; } });
  return g;
}
