// Usage: node render.js jobs.json  -- jobs: [{svg, size|[w,h], out, scale?}]
const { Resvg } = require('@resvg/resvg-js');
const fs = require('fs');
const jobs = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const cache = {};
for (const j of jobs) {
  const svg = (cache[j.svg] ??= fs.readFileSync(j.svg, 'utf8'));
  const [w, h] = Array.isArray(j.size) ? j.size : [j.size, j.size];
  // Logo is square; fit it inside w x h scaled by j.fill (default 1), centered on a transparent canvas.
  const fill = j.fill ?? 1;
  const box = Math.round(Math.min(w, h) * fill);
  const logo = new Resvg(svg, { fitTo: { mode: 'width', value: box } }).render();
  const bg = new Resvg(`<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}"/>`).render();
  // compose via raw RGBA
  const out = Buffer.alloc(w * h * 4);
  const lp = logo.pixels, ox = Math.floor((w - box) / 2), oy = Math.floor((h - box) / 2);
  for (let y = 0; y < box; y++) for (let x = 0; x < box; x++) {
    const s = (y * box + x) * 4, d = ((y + oy) * w + x + ox) * 4;
    out[d] = lp[s]; out[d + 1] = lp[s + 1]; out[d + 2] = lp[s + 2]; out[d + 3] = lp[s + 3];
  }
  fs.writeFileSync(j.out, Buffer.from(rawToPng(out, w, h)));
}
function rawToPng(rgba, w, h) {
  const zlib = require('zlib');
  const crcT = []; for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crcT[n] = c >>> 0; }
  const crc = b => { let c = 0xffffffff; for (const x of b) c = crcT[(c ^ x) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 6;
  const raw = Buffer.alloc((w * 4 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 4 + 1)] = 0; rgba.copy(raw, y * (w * 4 + 1) + 1, y * w * 4, (y + 1) * w * 4); }
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}
