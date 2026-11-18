'use strict';
// W-G1-51 갈래 Z — 차림 그림 만들기 (시험 쪽 · 의존 0 · node zlib 만)
//   ① 로고 1장 = 4분면 밝기 0.90 · 0.15 · 0.65 · 0.40(왼위 · 오위 · 왼아래 · 오아래) · 색이 서로 다름 · 4:3(설계 §1-2 G11)
//   ② 종마다 양식 그림 1장 = 해시가 다름 · 그림 안에 종 표지 글자(5×7 점 글꼴) · 연한 4분면(종마다 순서 다름 · 2순위 판독용)
//   저작권 없음(코드로 그린 그림) · 바이트가 매번 같다(결정적) → 시험이 해시를 미리 안다.
const zlib = require('zlib');
const crypto = require('crypto');

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();
function crc32(buf) {
  let c = 0xffffffff;
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
/** RGB 8비트 → PNG 바이트(거르개 0 · zlib 수준 9 고정 = 결정적). */
function encodeRgb(w, h, rgb) {
  if (rgb.length !== w * h * 3) throw new Error('rgb 길이 다름');
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) {
    raw[y * (w * 3 + 1)] = 0;
    rgb.copy(raw, y * (w * 3 + 1) + 1, y * w * 3, (y + 1) * w * 3);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}
/** BT.709 휘도(판독기 PngLite 와 같은 식 · 0~1). */
const luma = (r, g, b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
/** 휘도 L 을 지키며 R · B 를 틴트로 정하고 G 를 푼다(0~1). */
function colorWithLuma(L, tr, tb) {
  const r = Math.min(1, Math.max(0, L + tr));
  const b = Math.min(1, Math.max(0, L + tb));
  const g = Math.min(1, Math.max(0, (L - 0.2126 * r - 0.0722 * b) / 0.7152));
  return [r, g, b].map((v) => Math.round(v * 255));
}

// 로고 4분면 — 왼위 · 오위 · 왼아래 · 오아래 (휘도 · 틴트 R · 틴트 B)
const LOGO_QUAD = [0.90, 0.15, 0.65, 0.40];
const LOGO_TINT = [[0.08, -0.25], [0.12, 0.30], [-0.20, 0.25], [0.30, -0.20]];
const LOGO_W = 160;
const LOGO_H = 120;
function logoPng() {
  const rgb = Buffer.alloc(LOGO_W * LOGO_H * 3);
  const cols = LOGO_QUAD.map((L, i) => colorWithLuma(L, LOGO_TINT[i][0], LOGO_TINT[i][1]));
  for (let y = 0; y < LOGO_H; y++) {
    for (let x = 0; x < LOGO_W; x++) {
      const q = (y < LOGO_H / 2 ? 0 : 2) + (x < LOGO_W / 2 ? 0 : 1);
      const i = (y * LOGO_W + x) * 3;
      rgb[i] = cols[q][0]; rgb[i + 1] = cols[q][1]; rgb[i + 2] = cols[q][2];
    }
  }
  const png = encodeRgb(LOGO_W, LOGO_H, rgb);
  return { png, sha: sha256(png), quad: quadMeans(rgb, LOGO_W, LOGO_H), w: LOGO_W, h: LOGO_H, aspect: LOGO_W / LOGO_H };
}

// 5×7 점 글꼴 — 종 표지에 쓰는 글자만
const GLYPH = {
  A: ['01110', '10001', '10001', '11111', '10001', '10001', '10001'],
  C: ['01111', '10000', '10000', '10000', '10000', '10000', '01111'],
  D: ['11110', '10001', '10001', '10001', '10001', '10001', '11110'],
  E: ['11111', '10000', '10000', '11110', '10000', '10000', '11111'],
  L: ['10000', '10000', '10000', '10000', '10000', '10000', '11111'],
  O: ['01110', '10001', '10001', '10001', '10001', '10001', '01110'],
  P: ['11110', '10001', '10001', '11110', '10000', '10000', '10000'],
  R: ['11110', '10001', '10001', '11110', '10100', '10010', '10001'],
  S: ['01111', '10000', '10000', '01110', '00001', '00001', '11110'],
  T: ['11111', '00100', '00100', '00100', '00100', '00100', '00100'],
  V: ['10001', '10001', '10001', '10001', '10001', '01010', '00100'],
  '-': ['00000', '00000', '00000', '11111', '00000', '00000', '00000'],
};
function drawText(rgb, w, x0, y0, text, scale, col) {
  let x = x0;
  for (const ch of text) {
    const g = GLYPH[ch];
    if (!g) throw new Error(`글꼴에 없는 글자 ${ch}`);
    for (let gy = 0; gy < 7; gy++) {
      for (let gx = 0; gx < 5; gx++) {
        if (g[gy][gx] !== '1') continue;
        for (let sy = 0; sy < scale; sy++) {
          for (let sx = 0; sx < scale; sx++) {
            const i = ((y0 + gy * scale + sy) * w + (x + gx * scale + sx)) * 3;
            rgb[i] = col[0]; rgb[i + 1] = col[1]; rgb[i + 2] = col[2];
          }
        }
      }
    }
    x += 6 * scale;
  }
}
// 양식 그림 4분면 연한 밝기(0.97 · 0.91 · 0.85 · 0.79) — 종마다 다른 순서(순열 표)
const BG_LEVELS = [0.97, 0.91, 0.85, 0.79];
const BG_PERM = [[0, 1, 2, 3], [1, 0, 3, 2], [2, 3, 0, 1], [3, 2, 1, 0], [0, 2, 1, 3], [3, 1, 2, 0], [1, 3, 0, 2]];
const BG_W = 420;
const BG_H = 594;
/** 종 i(0~6)의 양식 그림 — A4 비율 · 연한 4분면 · 10mm 격자 줄 · 왼위에 표지 글자. */
function formBgPng(i, label) {
  const rgb = Buffer.alloc(BG_W * BG_H * 3);
  const perm = BG_PERM[i % BG_PERM.length];
  const quadL = perm.map((p) => BG_LEVELS[p]);
  for (let y = 0; y < BG_H; y++) {
    for (let x = 0; x < BG_W; x++) {
      const q = (y < BG_H / 2 ? 0 : 2) + (x < BG_W / 2 ? 0 : 1);
      let v = Math.round(quadL[q] * 255);
      if (x % 20 === 0 || y % 20 === 0) v -= 20; // 격자(10mm 꼴)
      const k = (y * BG_W + x) * 3;
      rgb[k] = v; rgb[k + 1] = v; rgb[k + 2] = v;
    }
  }
  drawText(rgb, BG_W, 24, 24, label, 4, [40, 40, 40]);
  const png = encodeRgb(BG_W, BG_H, rgb);
  return { png, sha: sha256(png), quad: quadMeans(rgb, BG_W, BG_H), w: BG_W, h: BG_H, aspect: BG_W / BG_H, label };
}
/** 4분면 평균 휘도(판독기 PngLite.QuadrantMeans 와 같은 나눔 · 짝수 폭 · 높이). */
function quadMeans(rgb, w, h) {
  const s = [0, 0, 0, 0];
  const n = [0, 0, 0, 0];
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const q = (y < h / 2 ? 0 : 2) + (x < w / 2 ? 0 : 1);
      const i = (y * w + x) * 3;
      s[q] += luma(rgb[i] / 255, rgb[i + 1] / 255, rgb[i + 2] / 255);
      n[q] += 1;
    }
  }
  return s.map((v, q) => +(v / n[q]).toFixed(4));
}
function sha256(buf) {
  return crypto.createHash('sha256').update(buf).digest('hex');
}

module.exports = { encodeRgb, crc32, luma, quadMeans, colorWithLuma, logoPng, formBgPng, sha256, LOGO_QUAD, BG_PERM, GLYPH };
