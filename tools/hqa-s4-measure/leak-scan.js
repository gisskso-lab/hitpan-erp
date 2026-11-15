'use strict';
// M9 [장치] 누출 검사 — 설계 §5 (measure 잡 · publish 잡에서 다시)
//   사용: node leak-scan.js <폴더> [--stage measure|publish] [--remove] [--withheld-write] [--json]
//   검사: 파일 내용(.json · .jsonl · .txt · SHA256SUMS.raw) + 파일 · 폴더 이름(전부)
//   꼴 종류: runner-path · long-token · credential · banned-word · extension · png-signature · png-text · png-chunk · png-structure
//   고침1: F3 JSON 키-값 자격증명 · F4 표준 base64 · F5 알려진 칸 밖 16진 40/64 · F6 PNG 머리 바이트 · 텍스트 덩어리
//   걸린 파일: --remove 면 지움(공개 · 운반 0) · withheld.json 에 이름 · 꼴 종류만(값 0 · 이름이 걸리면 이름도 가림)
//   출력에 걸린 값은 절대 안 찍는다. 종료 코드: 0 = 깨끗 · 2 = 걸림 · 1 = 처리 오류
const fs = require('fs');
const path = require('path');
const { loadAllow, listFiles, findSecrets, hexCtx } = require('./mask.js');

const CONTENT_EXT = new Set(['.json', '.jsonl', '.txt']);
const ALLOWED_EXT = new Set(['.json', '.jsonl', '.txt', '.png']);
const SUMS_NAME = 'SHA256SUMS.raw';
const WITHHELD = 'withheld.json';

// 자격증명 꼴 — 조각을 이어 만든다(원문에 꼴 글자 0)
const J = (...a) => a.join('');
const CREDENTIAL_RES = [
  new RegExp(J('(?<![A-Za-z0-9])', 'gh', '[pousr]', '_', '[A-Za-z0-9]{30,}')),
  new RegExp(J('github', '_pat', '_', '[A-Za-z0-9_]{20,}')),
  new RegExp(J('(?<![A-Z0-9])', '(?:AK', '|AS)', 'IA', '[0-9A-Z]{16}(?![A-Z0-9])')),
  new RegExp(J('-{5}BEGIN [A-Z ]*', 'PRI', 'VATE KEY', '-{5}')),
  new RegExp(J('e', 'yJ', '[A-Za-z0-9_-]{8,}\\.', 'e', 'yJ', '[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}')),
  new RegExp(J('[Bb]ear', 'er\\s+[A-Za-z0-9._~+/-]{20,}')),
  new RegExp(J('xo', 'x[baprs]-[A-Za-z0-9-]{10,}')),
  // 고침1 F3: 키 뒤 따옴표 허용 — JSON 키-값("키": "값") 꼴 · 고침2 Y1: 이스케이프된 JSON(\"키\":\"값\") 꼴도 · 5자 이하 값은 수용(기록)
  new RegExp(J('(?:pass', 'word|pwd|sec', 'ret|to', 'ken|api[_-]?key)(?:\\\\?["\'])?\\s*[=:]\\s*(?:\\\\?["\'])?[^\\s"\';,\\\\]{6,}'), 'i'),
];
// 금지어(헌법 #23) — 글자 번호로 만든다
const C = (...n) => String.fromCharCode(...n);
const BANNED_RES = [
  new RegExp('(?<![A-Za-z])' + C(67, 108, 97, 117, 100, 101) + '(?![A-Za-z])', 'i'),
  new RegExp('(?<![A-Za-z])' + C(67, 117, 114, 115, 111, 114) + '(?![A-Za-z])'),
  new RegExp('(?<![A-Za-z0-9])' + C(65, 73) + '(?![A-Za-z0-9])'),
];
const GENERIC_RUNNER_RES = [
  /\/home\/runner(?:admin)?(?![A-Za-z0-9_])/,
  /\/opt\/hostedtoolcache/,
  /\/__[wt]\//,
];

function envRunnerPaths(env) {
  return ['GITHUB_WORKSPACE', 'RUNNER_TEMP', 'HOME', 'RUNNER_TOOL_CACHE', 'RUNNER_WORKSPACE']
    .map((k) => env[k]).filter((v) => typeof v === 'string' && v.length >= 5 && v !== '/');
}

function kindsInText(text, env, allow, withPaths, fileBase) {
  const kinds = new Set();
  if (withPaths) {
    for (const p of envRunnerPaths(env)) if (text.includes(p)) kinds.add('runner-path');
    for (const re of GENERIC_RUNNER_RES) if (re.test(text)) kinds.add('runner-path');
  }
  // 고침1 F4 · F5: 표준 base64 · 알려진 칸 밖 16진 40/64 도 긴 토큰(가림과 같은 판정 함수 findSecrets)
  if (findSecrets(text, allow, hexCtx(env, fileBase || null)).length) kinds.add('long-token');
  for (const re of CREDENTIAL_RES) if (re.test(text)) { kinds.add('credential'); break; }
  for (const re of BANNED_RES) if (re.test(text)) { kinds.add('banned-word'); break; }
  return kinds;
}

// 고침1 F6: PNG 는 이름이 아니라 머리 바이트(서명 8바이트)로 · 텍스트 덩어리(tEXt · iTXt · zTXt) 0 · IEND 로 끝남 · 뒤 덧붙임 0
const PNG_SIG = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
const PNG_TEXT = new Set(['tEXt', 'iTXt', 'zTXt']);
// 고침2 Y4: 덩어리 허용 목록 — 그 밖(eXIf · iCCP · 사설 덩어리 등) = png-chunk(보류)
const PNG_OK = new Set(['IHDR', 'IDAT', 'IEND', 'PLTE', 'tRNS', 'sRGB', 'gAMA', 'pHYs']);
function pngKinds(buf) {
  if (buf.length < 8 || !buf.subarray(0, 8).equals(PNG_SIG)) return ['png-signature'];
  const kinds = new Set();
  let off = 8;
  let end = false;
  while (off + 12 <= buf.length) {
    const len = buf.readUInt32BE(off);
    const type = buf.toString('latin1', off + 4, off + 8);
    if (off + 12 + len > buf.length) { kinds.add('png-structure'); return [...kinds]; }
    if (PNG_TEXT.has(type)) kinds.add('png-text');
    else if (!PNG_OK.has(type)) kinds.add('png-chunk');
    off += 12 + len;
    if (type === 'IEND') { end = true; break; }
  }
  if (!end || off !== buf.length) kinds.add('png-structure');
  return [...kinds];
}

function scan(dir, env) {
  const allow = loadAllow();
  const hits = [];
  for (const f of listFiles(dir)) {
    const rel = path.relative(dir, f).split(path.sep).join('/');
    const kinds = new Set();
    let nameHit = false;
    for (const part of rel.split('/')) {
      const k = kindsInText(part, env, allow, false);
      if (k.size) { nameHit = true; for (const x of k) kinds.add(x); }
    }
    const base = path.basename(f);
    const ext = path.extname(f);
    if (base !== SUMS_NAME && !ALLOWED_EXT.has(ext)) kinds.add('extension');
    if (base === SUMS_NAME || CONTENT_EXT.has(ext)) {
      const text = fs.readFileSync(f, 'utf8');
      for (const x of kindsInText(text, env, allow, true, base)) kinds.add(x);
    }
    if (ext === '.png') for (const x of pngKinds(fs.readFileSync(f))) kinds.add(x);
    if (kinds.size) hits.push({ file: f, rel, nameHit, kinds: [...kinds].sort() });
  }
  return hits;
}

function main(argv, env) {
  const dir = argv[0];
  if (!dir || !fs.existsSync(dir)) { console.error('usage: node leak-scan.js <dir> [--stage s] [--remove] [--withheld-write] [--json]'); return 1; }
  const si = argv.indexOf('--stage');
  const stage = si >= 0 ? argv[si + 1] : 'measure';
  const remove = argv.includes('--remove');
  const writeW = argv.includes('--withheld-write');
  const asJson = argv.includes('--json');
  const hits = scan(dir, env);
  let k = 0;
  const entries = hits.map((h) => ({ name: h.nameHit ? '<이름 가림:' + (++k) + '>' : h.rel, kinds: h.kinds }));
  if (remove) for (const h of hits) fs.unlinkSync(h.file);
  if (writeW || hits.length) {
    const wf = path.join(dir, WITHHELD);
    let doc = { note: 'names and pattern kinds only - values 0', stages: {} };
    if (fs.existsSync(wf) && !hits.some((h) => h.rel === WITHHELD)) doc = JSON.parse(fs.readFileSync(wf, 'utf8'));
    doc.stages = doc.stages || {};
    doc.stages[stage] = entries;
    doc.files = Object.values(doc.stages).flat();
    fs.writeFileSync(wf, JSON.stringify(doc, null, 2) + '\n');
  }
  if (asJson) console.log(JSON.stringify({ stage, hits: hits.length, files: entries }));
  else console.log('[M9] 누출 검사 ' + stage + ' · 걸린 파일 ' + hits.length + (hits.length ? ' · ' + JSON.stringify(entries) : ''));
  return hits.length ? 2 : 0;
}

module.exports = { scan, kindsInText, pngKinds, CREDENTIAL_RES, BANNED_RES, PNG_OK };

if (require.main === module) {
  let rc;
  try { rc = main(process.argv.slice(2), process.env); } catch (e) {
    console.error('[M9] 누출 검사 오류: ' + String(e && e.message ? e.message : e).slice(0, 300));
    rc = 1;
  }
  process.exit(rc);
}
