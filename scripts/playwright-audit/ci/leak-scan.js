// G1-3 갈래 H — S10 누출 검사 (작업지시서 §3-2 S10 · §0-9 ⑩⑪ · 보안상무 조건 ⑩·⑪)
//
// 사용: node leak-scan.js <업로드 바닥 폴더>   (바닥 아래 `main/` · `trace/` 를 따로 본다)
//
// 검색 대상 = S0 일곱 값 + 런 중 등록값(UI_SECRET_* — 시드 개인키·서명·증표 · 접속·갱신 토큰).
// 검색 꼴 = 원문 · base64(표준·URL 안전 · 시작 자리 3가지) · URL 인코딩(대·소문자 16진) — **3꼴**.
// trace 등 .zip 은 **풀어서** 안의 파일(resources/ 포함)까지 본다. 못 풀면 빨강(못 본 것 = 통과 아님).
//
// 빨강(종료 1)
//   ⑩ S0 값이 하나라도 비었다(빈 검색 = 거짓 통과) · 값이 8자 미만
//   한 꼴이라도 잡혔다 → 잡힌 폴더(main 또는 trace)를 **지우고** 빨강(업로드 전)
//   plant.flags 에 `leak` (측정 커밋): 서로 부분 문자열이 아닌 가짜 값 3개를 꼴마다 심는다 —
//     꼴마다 「잡음」이 찍혀야 장치 정상(그래도 누출이 있으므로 빨강 · 업로드 0). 하나라도 못 잡으면 [장치] 고장.
// 출력에는 값이 아니라 **표지·꼴·파일 경로만** 찍는다.
'use strict';

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const crypto = require('crypto');
const L = require('./lib');

const MIN_LEN = 8;

// ── 검색 꼴 ────────────────────────────────────────────────────────────────
// base64 는 앞에 0·1·2 바이트가 붙은 경우의 「값 바이트로만 정해지는 가운데 글자」를 찾는다.
function base64Needles(value) {
  const src = Buffer.from(value, 'utf8');
  const out = new Set();
  for (let k = 0; k < 3; k += 1) {
    const enc = Buffer.concat([Buffer.alloc(k), src]).toString('base64').replace(/=+$/, '');
    const start = Math.ceil((k * 8) / 6);
    const end = Math.floor(((k + src.length) * 8) / 6);
    const mid = enc.slice(start, end);
    if (mid.length >= MIN_LEN) {
      out.add(mid);
      out.add(mid.replace(/\+/g, '-').replace(/\//g, '_'));
    }
  }
  return [...out];
}
function urlNeedles(value) {
  const enc = encodeURIComponent(value);
  const out = new Set();
  if (enc !== value) {
    out.add(enc);
    out.add(enc.replace(/%[0-9A-F]{2}/g, (m) => m.toLowerCase()));
    out.add(enc.replace(/%20/g, '+'));
  }
  return [...out];
}
function needlesFor(value) {
  return [
    { form: 'raw', bufs: [Buffer.from(value, 'utf8')] },
    { form: 'base64', bufs: base64Needles(value).map((s) => Buffer.from(s, 'latin1')) },
    { form: 'url', bufs: urlNeedles(value).map((s) => Buffer.from(s, 'latin1')) },
  ];
}

// ── zip 풀기 (외부 도구 없이 · stored/deflate) ─────────────────────────────────
function unzipEntries(buf) {
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 65557); i -= 1) {
    if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error('zip 끝 표지 없음');
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  if (count === 0xffff || p === 0xffffffff) throw new Error('zip64 미지원');
  const entries = [];
  for (let n = 0; n < count; n += 1) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error('zip 목록 표지 오류');
    const method = buf.readUInt16LE(p + 10);
    const csize = buf.readUInt32LE(p + 20);
    const nlen = buf.readUInt16LE(p + 28);
    const elen = buf.readUInt16LE(p + 30);
    const clen = buf.readUInt16LE(p + 32);
    const lho = buf.readUInt32LE(p + 42);
    const name = buf.slice(p + 46, p + 46 + nlen).toString('utf8');
    p += 46 + nlen + elen + clen;
    if (name.endsWith('/')) continue;
    if (buf.readUInt32LE(lho) !== 0x04034b50) throw new Error('zip 항목 표지 오류');
    const dataStart = lho + 30 + buf.readUInt16LE(lho + 26) + buf.readUInt16LE(lho + 28);
    const raw = buf.slice(dataStart, dataStart + csize);
    let data;
    if (method === 0) data = raw;
    else if (method === 8) data = zlib.inflateRawSync(raw);
    else throw new Error(`zip 압축 방식 ${method} 미지원`);
    entries.push({ name, data });
  }
  return entries;
}

// ── 파일 모으기 (zip 은 풀어서 · 안의 zip 도) ──────────────────────────────────
function collectBlobs(root) {
  const blobs = [];
  const errors = [];
  function addBuffer(label, buf, depth) {
    blobs.push({ label, buf });
    if (label.toLowerCase().endsWith('.zip') && depth < 3) {
      try {
        for (const e of unzipEntries(buf)) addBuffer(`${label}!/${e.name}`, e.data, depth + 1);
      } catch (e) {
        errors.push(`${label}: ${e.message}`);
      }
    }
  }
  function walk(dir) {
    for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, ent.name);
      if (ent.isDirectory()) walk(full);
      else if (ent.isFile()) addBuffer(path.relative(root, full), fs.readFileSync(full), 0);
    }
  }
  if (fs.existsSync(root)) walk(root);
  return { blobs, errors };
}

function scanFolder(folder, secrets) {
  const { blobs, errors } = collectBlobs(folder);
  const hits = [];
  for (const s of secrets) {
    for (const { form, bufs } of needlesFor(s.value)) {
      for (const b of blobs) {
        if (bufs.some((nb) => nb.length > 0 && b.buf.indexOf(nb) >= 0)) {
          hits.push({ label: s.label, form, file: b.label });
        }
      }
    }
  }
  return { hits, errors, files: blobs.length };
}

// ── 심은 값 (측정 커밋 · ⑪) ─────────────────────────────────────────────────
const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n += 1) {
    let c = n;
    for (let k = 0; k < 8; k += 1) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();
function crc32(buf) {
  let c = 0xffffffff;
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}
function makeZip(files) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const { name, data } of files) {
    const nameBuf = Buffer.from(name, 'utf8');
    const comp = zlib.deflateRawSync(data);
    const crc = crc32(data);
    const lh = Buffer.alloc(30);
    lh.writeUInt32LE(0x04034b50, 0); lh.writeUInt16LE(20, 4); lh.writeUInt16LE(0, 6); lh.writeUInt16LE(8, 8);
    lh.writeUInt32LE(crc, 14); lh.writeUInt32LE(comp.length, 18); lh.writeUInt32LE(data.length, 22);
    lh.writeUInt16LE(nameBuf.length, 26); lh.writeUInt16LE(0, 28);
    const ch = Buffer.alloc(46);
    ch.writeUInt32LE(0x02014b50, 0); ch.writeUInt16LE(20, 4); ch.writeUInt16LE(20, 6); ch.writeUInt16LE(8, 10);
    ch.writeUInt32LE(crc, 16); ch.writeUInt32LE(comp.length, 20); ch.writeUInt32LE(data.length, 24);
    ch.writeUInt16LE(nameBuf.length, 28); ch.writeUInt32LE(offset, 42);
    locals.push(lh, nameBuf, comp);
    centrals.push(ch, nameBuf);
    offset += 30 + nameBuf.length + comp.length;
  }
  const cd = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(files.length, 8); end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(cd.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, cd, end]);
}
function plantValues() {
  const h = () => crypto.randomBytes(6).toString('hex');
  const v = {
    PLANT_RAW: `FAKE_LEAK_A_${h()}`,
    PLANT_B64: `FAKE_LEAK_B_${h()}`,
    PLANT_URL: `FAKE LEAK/C+${h()}`,
  };
  const vals = Object.values(v);
  for (const a of vals) for (const b of vals) {
    if (a !== b && (a.includes(b) || b.includes(a))) throw new Error('심은 값이 서로 부분 문자열이다(⑪ 위반)');
  }
  return v;
}
function plant(root, v) {
  const main = path.join(root, 'main');
  const trace = path.join(root, 'trace');
  fs.mkdirSync(main, { recursive: true });
  fs.mkdirSync(trace, { recursive: true });
  fs.writeFileSync(path.join(main, 'plant-raw.txt'), `note ${v.PLANT_RAW} end`);
  fs.writeFileSync(path.join(main, 'plant-url.txt'), `GET /x?q=${encodeURIComponent(v.PLANT_URL)}&z=1`);
  // base64 는 trace 꼴 그대로 zip 안 resources/ 에, 시작 자리를 1바이트 밀어서 심는다(가운데 글자 검색 확인).
  const body = Buffer.from(`x${v.PLANT_B64}y`, 'utf8').toString('base64');
  fs.writeFileSync(path.join(trace, 'plant-trace.zip'), makeZip([
    { name: 'trace.network', data: Buffer.from(`{"body":"${body}"}`, 'utf8') },
    { name: 'resources/plant.json', data: Buffer.from(`{"b":"${body}"}`, 'utf8') },
  ]));
}

function main() {
  const root = process.argv[2];
  if (!root) {
    console.log('⚫ FAIL [장치] 누출 검사: 업로드 바닥 폴더 인자 없음');
    process.exit(1);
  }
  const flagsPath = path.join(__dirname, 'plant.flags');
  const flags = fs.existsSync(flagsPath) ? new Set(fs.readFileSync(flagsPath, 'utf8').split(/\s+/).filter(Boolean)) : new Set();
  if (process.env.UI_LEAK_PLANT === '1') flags.add('leak'); // 로컬 단독 시험용

  // ⑩ 빈 검색 대상 = 빨강
  const missing = L.S0_NAMES.filter((n) => !process.env[n]);
  if (missing.length > 0) {
    console.log(`⚫ FAIL [장치] 누출 검사 대상 비었음: ${missing.join(', ')} — 빈 검색은 거짓 통과(보안상무 ⑩)`);
    process.exit(1);
  }
  let plants = null;
  if (flags.has('leak')) {
    plants = plantValues();
    for (const [label, value] of Object.entries(plants)) L.mask(value);
    plant(root, plants);
    console.log('측정 커밋 — 가짜 값 3개(서로 부분 문자열 아님)를 원문·base64(trace zip 안)·URL 인코딩으로 심음');
  }
  const secrets = L.collectSecrets();
  if (plants) for (const [label, value] of Object.entries(plants)) secrets.push({ label, value });
  const short = secrets.filter((s) => s.value.length < MIN_LEN);
  if (short.length > 0) {
    console.log(`⚫ FAIL [장치] 검색 대상 ${short.map((s) => s.label).join(', ')} 가 ${MIN_LEN}자 미만`);
    process.exit(1);
  }
  console.log(`검색 대상 ${secrets.length}개 (S0 ${L.S0_NAMES.length} + 런 중 ${secrets.length - L.S0_NAMES.length})`);

  let red = false;
  const allHits = [];
  for (const sub of ['main', 'trace']) {
    const folder = path.join(root, sub);
    if (!fs.existsSync(folder)) { console.log(`${sub}/ 없음 — 건너뜀(올릴 것도 없음)`); continue; }
    const { hits, errors, files } = scanFolder(folder, secrets);
    console.log(`${sub}/ 파일 ${files}개(풀어 낸 것 포함) · 잡힘 ${hits.length} · 못 푼 zip ${errors.length}`);
    for (const e of errors) console.log(`  못 푼 zip: ${e}`);
    for (const h of hits) console.log(`  잡힘: ${h.label} · 꼴=${h.form} · ${sub}/${h.file}`);
    if (hits.length > 0 || errors.length > 0) {
      red = true;
      fs.rmSync(folder, { recursive: true, force: true });
      console.log(`  → ${sub}/ 지움(업로드 0)`);
    }
    allHits.push(...hits);
  }

  if (plants) {
    const expect = { PLANT_RAW: 'raw', PLANT_B64: 'base64', PLANT_URL: 'url' };
    let deviceOk = true;
    for (const [label, form] of Object.entries(expect)) {
      const caught = allHits.some((h) => h.label === label && h.form === form);
      console.log(`심은 값 ${label}: 꼴 ${form} ${caught ? '잡음' : '못 잡음'}`);
      if (!caught) deviceOk = false;
    }
    if (!deviceOk) {
      console.log('⚫ FAIL [장치] 누출 검사가 심은 꼴을 놓쳤다 — 장치 고장');
      process.exit(1);
    }
    console.log('🔴 FAIL (의도된 심은 빨강) — 꼴 3개 다 잡음 · 잡힌 폴더 지움 · 업로드 0 이어야 한다');
    process.exit(1);
  }
  if (red) {
    console.log('🔴 FAIL [장치] 누출 발견 — 해당 폴더 지움 · 업로드 0');
    process.exit(1);
  }
  console.log('🟢 PASS 누출 0');
}

if (require.main === module) main();
module.exports = { base64Needles, urlNeedles, unzipEntries, makeZip, scanFolder, plantValues };
