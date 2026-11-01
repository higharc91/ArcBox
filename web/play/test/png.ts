/**
 * A small PNG reader for tests: 8-bit RGBA, not interlaced (what the pack's character sheets are). Inflates with the platform's
 * DecompressionStream and undoes the five row filters. Returns the top-left `w`×`h` pixels.
 */
export async function readPng(bytes: Uint8Array, w: number, h: number): Promise<Uint8ClampedArray> {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let at = 8;
  let width = 0, height = 0;
  const idat: Uint8Array[] = [];
  while (at < bytes.length) {
    const len = view.getUint32(at);
    const type = String.fromCharCode(...bytes.subarray(at + 4, at + 8));
    const data = bytes.subarray(at + 8, at + 8 + len);
    if (type === "IHDR") {
      width = view.getUint32(at + 8);
      height = view.getUint32(at + 12);
      if (data[8] !== 8 || data[9] !== 6 || data[12] !== 0) throw new Error("Only 8-bit RGBA, non-interlaced PNGs.");
    } else if (type === "IDAT") idat.push(data);
    at += 12 + len;
  }
  const joined = new Uint8Array(idat.reduce((n, d) => n + d.length, 0));
  idat.reduce((o, d) => (joined.set(d, o), o + d.length), 0);
  const raw = new Uint8Array(await new Response(new Blob([joined]).stream().pipeThrough(new DecompressionStream("deflate"))).arrayBuffer());
  const stride = width * 4;
  const rows = new Uint8Array(stride * height);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)]!;
    const src = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    for (let x = 0; x < stride; x++) {
      const a = x >= 4 ? rows[y * stride + x - 4]! : 0;
      const b = y > 0 ? rows[(y - 1) * stride + x]! : 0;
      const c = x >= 4 && y > 0 ? rows[(y - 1) * stride + x - 4]! : 0;
      const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
      const pred = filter === 1 ? a : filter === 2 ? b : filter === 3 ? (a + b) >> 1 : filter === 4 ? (pa <= pb && pa <= pc ? a : pb <= pc ? b : c) : 0;
      rows[y * stride + x] = (src[x]! + pred) & 255;
    }
  }
  const out = new Uint8ClampedArray(w * h * 4);
  for (let y = 0; y < h; y++) out.set(rows.subarray(y * stride, y * stride + w * 4), y * w * 4);
  return out;
}

/** A data URL's bytes (Vite's `?inline` imports). */
export const dataUrlBytes = (url: string) => Uint8Array.from(atob(url.slice(url.indexOf(",") + 1)), (c) => c.charCodeAt(0));
