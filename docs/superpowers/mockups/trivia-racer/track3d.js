// REDLINE 3D mockup scene: a low-poly diorama circuit and toy-proportioned F1 cars.
// Race distance 0..FINISH maps to one lap of the circuit, start/finish at u = 0.
import * as THREE from 'three';

export const FINISH = 20;
export const TEAM = { JO: '#3671c6', SAM: '#e8002d', ALEX: '#ff8000', RIA: '#27f4d2', MO: '#b6babd', KAI: '#229971' };
const SLOT = { JO: 0, SAM: 1, ALEX: 2, RIA: 3, MO: 4, KAI: 5 };
const W = 18;                       // track width

const CONTROL = [
  [-20, -55], [40, -55], [85, -50], [106, -24], [96, 6], [70, 16], [54, 40], [72, 62],
  [40, 74], [0, 60], [-30, 36], [-60, 56], [-96, 46], [-110, 10], [-96, -30], [-64, -52],
];

function canvasTex(w, h, draw, repeat) {
  const c = document.createElement('canvas'); c.width = w; c.height = h;
  draw(c.getContext('2d'), w, h);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 8;
  if (repeat) { t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(...repeat); }
  return t;
}

const mat = (color, extra = {}) => new THREE.MeshStandardMaterial({ color, roughness: 0.75, metalness: 0.05, flatShading: true, ...extra });

export function buildScene({ canvas, width, height, cam, drivers, trails = [], smoke = [] }) {
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true });
  renderer.setPixelRatio(1.5);
  renderer.setSize(width, height, false);
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.05;

  const scene = new THREE.Scene();
  const sky = cam.sky ? '#a9cdee' : '#14141c';
  scene.background = new THREE.Color(sky);
  scene.fog = new THREE.Fog(sky, cam.fogNear ?? 400, cam.fogFar ?? 700);

  // --- lights
  scene.add(new THREE.HemisphereLight('#dfe9ff', '#3d5a2c', 1.1));
  const sun = new THREE.DirectionalLight('#fff1dc', 2.6);
  sun.position.set(90, 170, 70);
  sun.castShadow = true;
  sun.shadow.mapSize.set(4096, 4096);
  Object.assign(sun.shadow.camera, { left: -170, right: 170, top: 130, bottom: -130, near: 10, far: 450 });
  sun.shadow.bias = -0.0004;
  scene.add(sun);

  // --- circuit samples (arc-length spaced)
  const curve = new THREE.CatmullRomCurve3(CONTROL.map(([x, z]) => new THREE.Vector3(x, 0, z)), true, 'centripetal');
  const N = 900, len = curve.getLength();
  const S = [];
  for (let i = 0; i <= N; i++) {
    const u = (i % N) / N;
    const p = curve.getPointAt(u), t = curve.getTangentAt(u);
    S.push({ p, t, n: new THREE.Vector3(-t.z, 0, t.x), s: u * len });
  }
  const curv = S.map((_, i) => S[(i + 10) % N].t.angleTo(S[(i - 10 + N) % N].t));

  const ribbon = (a, b, y, color, keep = () => true, colorAt) => {
    const pos = [], col = [];
    const c = new THREE.Color();
    for (let i = 0; i < N; i++) {
      if (!keep(i)) continue;
      const s0 = S[i], s1 = S[i + 1];
      const q = (s, o) => [s.p.x + s.n.x * o, y, s.p.z + s.n.z * o];
      const v = [q(s0, a), q(s0, b), q(s1, a), q(s1, b)];
      pos.push(...v[0], ...v[1], ...v[2], ...v[1], ...v[3], ...v[2]);
      c.set(colorAt ? colorAt(i) : color);
      for (let k = 0; k < 6; k++) col.push(c.r, c.g, c.b);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
    g.computeVertexNormals();
    const m = new THREE.Mesh(g, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.9, side: THREE.DoubleSide }));
    m.receiveShadow = true;
    scene.add(m);
    return m;
  };

  // --- ground slab (diorama island)
  const box = new THREE.Box3().setFromPoints(S.map(s => s.p)).expandByScalar(22);
  const gw = box.max.x - box.min.x, gd = box.max.z - box.min.z;
  const grass = canvasTex(256, 256, (g, w, h) => {
    for (let i = 0; i < 8; i++) { g.fillStyle = i % 2 ? '#5d9a46' : '#64a34c'; g.fillRect(i * w / 8, 0, w / 8, h); }
  }, [gw / 64, gd / 64]);
  const dirt = mat('#5b4232');
  const slab = new THREE.Mesh(new THREE.BoxGeometry(gw, 12, gd),
    [dirt, dirt, new THREE.MeshStandardMaterial({ map: grass, roughness: 1 }), dirt, dirt, dirt]);
  slab.position.set((box.min.x + box.max.x) / 2, -6, (box.min.z + box.max.z) / 2);
  slab.receiveShadow = true;
  scene.add(slab);

  // --- track surfaces
  ribbon(-W / 2 - 4, W / 2 + 4, 0.03, '#8d9196');                      // runoff apron
  ribbon(-W / 2, W / 2, 0.06, '#2f3138', undefined, i => (i % 2 ? '#2f3138' : '#31333a'));
  ribbon(W / 2 - 0.9, W / 2 - 0.4, 0.09, '#f2f2f2');                    // edge lines
  ribbon(-W / 2 + 0.4, -W / 2 + 0.9, 0.09, '#f2f2f2');
  const stripe = i => (Math.floor(S[i].s / 2.4) % 2 ? '#d7261e' : '#f4f4f4');
  const corner = i => curv[i] > 0.16;
  ribbon(W / 2, W / 2 + 2.2, 0.1, null, corner, stripe);                // curbs on corners
  ribbon(-W / 2 - 2.2, -W / 2, 0.1, null, corner, stripe);

  // start / finish: chequered strip + gantry
  const s0 = S[0];
  const chequer = canvasTex(256, 32, (g, w, h) => {
    for (let x = 0; x < 16; x++) for (let y = 0; y < 2; y++) { g.fillStyle = (x + y) % 2 ? '#111' : '#fff'; g.fillRect(x * 16, y * 16, 16, 16); }
  });
  const line = new THREE.Mesh(new THREE.PlaneGeometry(W, 2.2), new THREE.MeshStandardMaterial({ map: chequer, roughness: 0.8 }));
  line.rotation.x = -Math.PI / 2;
  line.rotation.z = Math.atan2(-s0.t.z, s0.t.x) + Math.PI / 2;
  line.position.set(s0.p.x, 0.12, s0.p.z);
  line.receiveShadow = true;
  scene.add(line);
  const banner = canvasTex(1024, 128, (g, w, h) => {
    g.fillStyle = '#e10600'; g.fillRect(0, 0, w, h);
    g.fillStyle = '#fff'; g.font = 'italic 900 92px "Titillium Web", sans-serif'; g.textAlign = 'center'; g.textBaseline = 'middle';
    g.fillText('REDLINE', w / 2, h / 2 + 6);
  });
  const gantry = new THREE.Group();
  const postM = mat('#24242c');
  for (const side of [-1, 1]) {
    const post = new THREE.Mesh(new THREE.BoxGeometry(1.2, 13, 1.2), postM);
    post.position.set(0, 6.5, side * (W / 2 + 3)); post.castShadow = true; gantry.add(post);
  }
  const beam = new THREE.Mesh(new THREE.BoxGeometry(1.4, 2.6, W + 7.2),
    [new THREE.MeshStandardMaterial({ map: banner }), new THREE.MeshStandardMaterial({ map: banner }), postM, postM, postM, postM]);
  beam.position.y = 12; beam.rotation.y = Math.PI / 2; beam.castShadow = true;
  const beamHolder = new THREE.Group(); beamHolder.rotation.y = -Math.PI / 2; beamHolder.add(beam); gantry.add(beamHolder);
  gantry.position.copy(s0.p);
  gantry.rotation.y = Math.atan2(-s0.t.z, s0.t.x);
  scene.add(gantry);

  // distance boards every 5 units
  for (const d of [5, 10, 15]) {
    const { p, n, t } = at(d);
    const board = canvasTex(128, 128, (g, w, h) => {
      g.fillStyle = '#fff'; g.fillRect(0, 0, w, h); g.fillStyle = '#e10600'; g.fillRect(0, 0, w, 18);
      g.fillStyle = '#111'; g.font = '900 72px "Titillium Web", sans-serif'; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText(String(d), w / 2, h / 2 + 10);
    });
    const b = new THREE.Mesh(new THREE.BoxGeometry(0.4, 4, 4), [mat('#ddd'), mat('#ddd'), mat('#ddd'), mat('#ddd'), mat('#ddd'), mat('#ddd')]);
    b.material[0] = b.material[1] = new THREE.MeshStandardMaterial({ map: board });
    b.position.set(p.x + n.x * (W / 2 + 7), 3.2, p.z + n.z * (W / 2 + 7));
    b.rotation.y = Math.atan2(-t.z, t.x);
    b.castShadow = true;
    scene.add(b);
  }

  // grandstand behind the main straight
  const stand = new THREE.Group();
  for (let r = 0; r < 5; r++) {
    const step = new THREE.Mesh(new THREE.BoxGeometry(84, 1.6 + r * 1.6, 3.4), mat(r % 2 ? '#d9dbe0' : '#c9ccd3'));
    step.position.set(0, (1.6 + r * 1.6) / 2, -r * 3.4); step.castShadow = step.receiveShadow = true; stand.add(step);
  }
  const roof = new THREE.Mesh(new THREE.BoxGeometry(88, 0.8, 20), mat('#e10600'));
  roof.position.set(0, 14, -7); roof.castShadow = true; stand.add(roof);
  const sign = new THREE.Mesh(new THREE.BoxGeometry(40, 5, 0.6),
    [postM, postM, postM, postM, new THREE.MeshStandardMaterial({ map: banner }), postM]);
  sign.position.set(0, 17.2, 2.6); sign.castShadow = true; stand.add(sign);
  for (const x of [-42, -14, 14, 42]) { const pst = new THREE.Mesh(new THREE.BoxGeometry(0.8, 14, 0.8), postM); pst.position.set(x, 7, -15); stand.add(pst); }
  const crowd = new THREE.InstancedMesh(new THREE.BoxGeometry(1, 1.3, 1), new THREE.MeshStandardMaterial({ roughness: 1 }), 5 * 70);
  const tmp = new THREE.Object3D(), cc = new THREE.Color();
  const palette = ['#f2f2f2', '#e10600', '#3671c6', '#ffcf40', '#27f4d2', '#ff8000', '#222', '#b04cff'];
  let k = 0;
  for (let r = 0; r < 5; r++) for (let i = 0; i < 70; i++) {
    tmp.position.set(-40 + i * 1.16 + (Math.random() - .5) * .3, 1.6 + r * 1.6 + .65, -r * 3.4 - .4);
    tmp.updateMatrix(); crowd.setMatrixAt(k, tmp.matrix); crowd.setColorAt(k++, cc.set(palette[(i * 7 + r * 3) % palette.length]));
  }
  stand.add(crowd);
  stand.position.set(10, 0, -55 - W / 2 - 9);
  scene.add(stand);

  // trees (deterministic scatter outside the track and stand)
  let seed = 7; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  const trunkG = new THREE.CylinderGeometry(0.5, 0.7, 3, 6), crownG = new THREE.ConeGeometry(3.2, 8, 7);
  const greens = ['#3f7d3a', '#47883f', '#356f34', '#4f9445'];
  for (let i = 0; i < 260; i++) {
    const x = box.min.x + 4 + rnd() * (gw - 8), z = box.min.z + 4 + rnd() * (gd - 8);
    let near = Infinity;
    for (let j = 0; j < N; j += 6) near = Math.min(near, Math.hypot(S[j].p.x - x, S[j].p.z - z));
    if (near < W / 2 + 10) continue;
    if (x > -40 && x < 60 && z < -60) continue;
    const sc = 0.8 + rnd() * 0.7;
    const tr = new THREE.Mesh(trunkG, mat('#6b4a2f')); tr.position.set(x, 1.5 * sc, z); tr.scale.setScalar(sc);
    const cr = new THREE.Mesh(crownG, mat(greens[i % 4])); cr.position.set(x, (3 + 4) * sc, z); cr.scale.setScalar(sc);
    tr.castShadow = cr.castShadow = true;
    scene.add(tr, cr);
  }

  // --- helpers
  function at(d) {
    const u = (((d % FINISH) + FINISH) % FINISH) / FINISH;
    const t = curve.getTangentAt(u);
    return { p: curve.getPointAt(u), t, n: new THREE.Vector3(-t.z, 0, t.x) };
  }
  function place(name, d) {
    const { p, t, n } = at(d);
    const slot = SLOT[name];
    const lat = [-5.4, -1.8, 1.8, 5.4][slot % 4];
    const back = slot >= 4 ? -4 / len * FINISH * 2.2 : 0;
    const q = at(d + back);
    return { pos: new THREE.Vector3(q.p.x + q.n.x * lat, 0.06, q.p.z + q.n.z * lat), yaw: Math.atan2(-t.z, t.x) };
  }

  // tracking trails (team-colored ribbons on the asphalt between two distances)
  for (const tr of trails) {
    const lat = [-5.4, -1.8, 1.8, 5.4][SLOT[tr.name] % 4];
    const i0 = Math.round(tr.from / FINISH * N), i1 = Math.round(tr.to / FINISH * N);
    ribbon(lat - 1.4, lat + 1.4, 0.13, TEAM[tr.name], i => i >= i0 && i < i1 - 6);
  }

  // cars
  const cars = {};
  for (const d of drivers) {
    const car = makeCar(TEAM[d.name]);
    const { pos, yaw } = place(d.name, d.dist);
    car.position.copy(pos);
    car.rotation.y = yaw + (d.spin || 0);
    scene.add(car);
    cars[d.name] = car;
  }

  // tyre smoke puffs
  const puffM = new THREE.MeshStandardMaterial({ color: '#e8e8ea', transparent: true, opacity: 0.55, roughness: 1, flatShading: true });
  for (const sm of smoke) {
    const { pos } = place(sm.name, sm.dist);
    let s2 = 3;
    for (let i = 0; i < 14; i++) {
      s2 = (s2 * 48271) % 2147483647;
      const r = 1.2 + (s2 % 100) / 40;
      const puff = new THREE.Mesh(new THREE.IcosahedronGeometry(r, 0), puffM);
      puff.position.set(pos.x - 3 + (s2 % 70) / 10, 0.8 + (s2 % 30) / 12, pos.z - 3 + ((s2 >> 3) % 70) / 10);
      scene.add(puff);
    }
  }

  const camera = new THREE.PerspectiveCamera(cam.fov, width / height, 1, 1500);
  if (cam.chase) {
    // Car-relative rig: offsets are in the subject car's own frame (fwd = heading, side = its left).
    const c = cam.chase, car = cars[c.name];
    const yaw = Math.atan2(-at(drivers.find(d => d.name === c.name).dist).t.z, at(drivers.find(d => d.name === c.name).dist).t.x);
    const fwd = new THREE.Vector3(Math.cos(yaw), 0, -Math.sin(yaw)), left = new THREE.Vector3(-fwd.z, 0, fwd.x);
    camera.position.copy(car.position).addScaledVector(fwd, c.fwd).addScaledVector(left, c.side).setY(c.up);
    camera.lookAt(car.position.clone().addScaledVector(fwd, c.lookFwd).addScaledVector(left, c.lookSide ?? 0).setY(c.lookY ?? 1.5));
  } else {
    camera.position.set(...cam.pos);
    camera.lookAt(...cam.target);
  }
  renderer.render(scene, camera);

  return {
    project(name, lift = 5) {
      const v = cars[name].position.clone(); v.y += lift; v.project(camera);
      return { x: (v.x + 1) / 2 * width, y: (1 - v.y) / 2 * height, visible: v.z < 1 };
    },
  };
}

function makeCar(color) {
  const g = new THREE.Group();
  const body = mat(color, { roughness: 0.35, metalness: 0.25 });
  const carbon = mat('#18181d', { roughness: 0.6 });
  const tyre = mat('#0d0d10', { roughness: 0.9 });
  const add = (geo, m, x, y, z, ry = 0) => { const o = new THREE.Mesh(geo, m); o.position.set(x, y, z); o.rotation.y = ry; o.castShadow = true; o.receiveShadow = true; g.add(o); return o; };

  add(new THREE.BoxGeometry(8.4, 0.3, 2.9), carbon, -0.2, 0.45, 0);              // floor
  add(new THREE.BoxGeometry(4.6, 1.0, 1.5), body, -0.2, 1.05, 0);                // tub
  const nose = add(new THREE.CylinderGeometry(0.28, 0.62, 3.4, 6), body, 3.6, 0.85, 0);
  nose.rotation.z = -Math.PI / 2;                                                 // tapered nose
  add(new THREE.BoxGeometry(1.0, 0.14, 4.0), body, 5.1, 0.45, 0);                // front wing
  add(new THREE.BoxGeometry(1.0, 0.5, 0.12), carbon, 5.1, 0.6, 2.0);             // front endplates
  add(new THREE.BoxGeometry(1.0, 0.5, 0.12), carbon, 5.1, 0.6, -2.0);
  add(new THREE.BoxGeometry(2.8, 0.8, 0.9), body, -0.6, 0.95, 1.2);              // sidepods
  add(new THREE.BoxGeometry(2.8, 0.8, 0.9), body, -0.6, 0.95, -1.2);
  add(new THREE.BoxGeometry(2.6, 1.1, 1.0), body, -2.6, 1.35, 0);                // engine cover
  add(new THREE.BoxGeometry(1.0, 0.9, 0.7), carbon, -1.0, 1.9, 0);               // airbox
  add(new THREE.SphereGeometry(0.5, 10, 8), mat('#f5f5f5', { roughness: 0.3 }), 0.4, 1.75, 0); // helmet
  add(new THREE.BoxGeometry(0.15, 0.15, 1.1), carbon, 1.1, 2.1, 0);              // halo bar
  add(new THREE.BoxGeometry(0.9, 0.16, 3.4), body, -4.3, 2.55, 0);               // rear wing
  add(new THREE.BoxGeometry(1.1, 1.5, 0.14), carbon, -4.3, 1.95, 1.7);           // rear endplates
  add(new THREE.BoxGeometry(1.1, 1.5, 0.14), carbon, -4.3, 1.95, -1.7);
  add(new THREE.BoxGeometry(0.3, 1.2, 0.3), carbon, -4.0, 1.7, 0);               // wing pillar
  const wheel = (x, r, w, z) => {
    const t = add(new THREE.CylinderGeometry(r, r, w, 14), tyre, x, r + 0.06, z);
    t.rotation.x = Math.PI / 2;
    const rim = add(new THREE.CylinderGeometry(r * 0.55, r * 0.55, w + 0.04, 10), mat('#9a9aa6', { metalness: 0.6, roughness: 0.4 }), x, r + 0.06, z);
    rim.rotation.x = Math.PI / 2;
  };
  wheel(2.9, 0.82, 0.85, 1.85); wheel(2.9, 0.82, 0.85, -1.85);
  wheel(-2.7, 0.95, 1.05, 1.85); wheel(-2.7, 0.95, 1.05, -1.85);
  return g;
}
