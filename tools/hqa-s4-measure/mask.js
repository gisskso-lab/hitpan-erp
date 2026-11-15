'use strict';
// M8 [장치] 가림 — 설계 §5 · 고침1 F4 · F5
//   사용: node mask.js <원자료 폴더>
//   ① 러너 경로(GITHUB_WORKSPACE · RUNNER_TEMP · HOME 실값) → <러너>
//   ② 표준 base64 꼴(+ / = 를 포함 · 대문자 · 소문자 · 숫자가 다 있음 · 22자 이상) → <가림:n>   (F4)
//   ③ 영숫자 · '-' · '_' 22자 이상 토큰 → <가림:n>
//      단 40자 · 64자 16진은 「알려진 칸」 에 있을 때만 남긴다(F5 — 아래 hexAllowedAt):
//        JSON 키 HEX_KEYS 의 값 · 세부 글자의 key=값(HEX_KV) · MODE 글자 armed: 뒤 · SHA256SUMS.raw 줄머리
//        · 장치 고정값(KNOWN_HEX · expected-sums.txt · 환경 GITHUB_SHA)
//      (같은 토큰 = 같은 n · 값은 메모리에만 · 어디에도 안 씀)
//   ④ 텍스트 파일(.json · .jsonl · .txt)만 · PNG 는 leak-scan 이 머리 바이트 · 텍스트 덩어리를 본다(F6)
//   가린 개수는 run.json 의 mask 칸에 수로만. 종료 코드 0 = 끝 · 1 = 처리 오류
const fs = require('fs');
const path = require('path');

const TEXT_EXT = new Set(['.json', '.jsonl', '.txt']);
const TOKEN_RE = /(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{22,}(?![A-Za-z0-9_-])/g;
// 표준 base64: 앞뒤에 base64 글자 · 점 · '-' · '_' 가 붙지 않은 덩어리(경로 · 주소는 점 · '-' 에서 끊긴다)
const B64_RE = /(?<![A-Za-z0-9+/=._-])[A-Za-z0-9+/]{22,}={0,2}(?![A-Za-z0-9+/=._-])/g;
const HEX_RE = /^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$/;
// 공개된 고정 이름만(릴리스 자산 · 꾸러미 · 환경 변수 이름). 값이 아니라 이름이다.
const FIXED_ALLOW = [
  'chrome-headless-shell-linux64',
  'hqa-s4-fixture-01161edb',
  'hqa-s4-fixture-linux-x64-01161edb',
  'hqa-s4-fixture-src-01161edb',
  'fonts-noto-color-emoji',
  'ASPNETCORE_ENVIRONMENT',
  'PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD',
];
// F5 — 16진을 남기는 칸. 장치 스크립트가 git SHA · sha256 을 쓰는 칸 이름만(m0-assert · boot · publish 가 쓰는 이름과 1:1).
const HEX_KEYS = new Set([
  'sha', 'head', 'device_commit', 'fixture_tree', 'fixture_tree_expected', 'base_expected', 'feat_expected',
  'fixture_blob', 'src_blob', 'tree_blob', 'raw_blob', 'norm_blob', 'sha256', 'commit',
]);
const HEX_KV = new Set([
  'got', 'want', 'expected', 'release', 'HEAD', 'event', 'parent', 'mode', 'tree', 'base', 'feat', 'fixture', 'main',
]);
// 장치 원문에 이미 공개된 고정 SHA(시험 앱 나무 · 시험 앱 머리 · 기능 머리)
const KNOWN_HEX = [
  '0b31f8692367ae28a6fac255d8eae5c931066d7b',
  '01161edb5618b5e34ea2c5e655dcaaf2333d3a68',
  '16e88c01092d33eb643c6cac454703f00efa02ac',
];

function loadAllow() {
  const allow = new Set(FIXED_ALLOW);
  // 판독 선택자 이름 등 공개 고정 이름: rules.json 의 mask_allow (문자열 배열 · 16진은 받지 않음)
  const rules = path.join(__dirname, 'rules.json');
  if (fs.existsSync(rules)) {
    const j = JSON.parse(fs.readFileSync(rules, 'utf8'));
    if (Array.isArray(j.mask_allow)) {
      for (const s of j.mask_allow) if (typeof s === 'string' && !HEX_RE.test(s)) allow.add(s);
    }
  }
  return allow;
}

function knownHex(env) {
  const set = new Set(KNOWN_HEX);
  const sums = path.join(__dirname, 'expected-sums.txt');
  if (fs.existsSync(sums)) {
    for (const l of fs.readFileSync(sums, 'utf8').split('\n')) {
      const m = /^([0-9a-f]{64})\s/.exec(l);
      if (m) set.add(m[1]);
    }
  }
  if (env && typeof env.GITHUB_SHA === 'string' && /^[0-9a-f]{40}$/.test(env.GITHUB_SHA)) set.add(env.GITHUB_SHA);
  return set;
}

// fileBase: 파일 이름(SHA256SUMS.raw 줄머리 판단용) · 이름 검사 때는 null
function hexCtx(env, fileBase) {
  return { known: knownHex(env), sumsFile: fileBase === 'SHA256SUMS.raw' };
}

function hexAllowedAt(text, idx, tok, hctx) {
  if (hctx.known.has(tok.toLowerCase())) return true;
  const pre = text.slice(Math.max(0, idx - 64), idx);
  let m = /"([A-Za-z0-9_]+)"\s*:\s*"$/.exec(pre);
  if (m && HEX_KEYS.has(m[1])) return true;
  m = /(?:^|[^A-Za-z0-9_])([A-Za-z0-9_]+)=$/.exec(pre);
  if (m && HEX_KV.has(m[1])) return true;
  if (/armed:$/.test(pre)) return true;
  if (hctx.sumsFile && (idx === 0 || text[idx - 1] === '\n') && text.startsWith('  ', idx + tok.length)) return true;
  return false;
}

function isB64Secret(tok) {
  const body = tok.replace(/=+$/, '');
  if (body.length < 22) return false;
  if (!/[+/]/.test(body) && body.length === tok.length) return false; // 순수 영숫자는 TOKEN_RE 몫
  return /[A-Z]/.test(body) && /[a-z]/.test(body) && /[0-9]/.test(body);
}

// 가림 · 누출 공용 — 걸리는 덩어리 [{s, e, tok}] (앞에서부터)
function findSecrets(text, allow, hctx) {
  const out = [];
  for (const m of text.matchAll(B64_RE)) {
    if (isB64Secret(m[0]) && !allow.has(m[0])) out.push({ s: m.index, e: m.index + m[0].length, tok: m[0] });
  }
  for (const m of text.matchAll(TOKEN_RE)) {
    const s = m.index;
    const e = s + m[0].length;
    if (out.some((o) => s < o.e && e > o.s)) continue;
    const tok = m[0];
    if (allow.has(tok)) continue;
    if (HEX_RE.test(tok) && hexAllowedAt(text, s, tok, hctx)) continue;
    out.push({ s, e, tok });
  }
  return out.sort((a, b) => a.s - b.s);
}

// 옛 호출 꼴(문맥 없는 낱말 판정) — 16진 40/64 는 문맥이 없으니 걸린다
function isLongToken(tok, allow) {
  return tok.length >= 22 && !allow.has(tok);
}

function runnerPaths(env) {
  const vals = ['GITHUB_WORKSPACE', 'RUNNER_TEMP', 'HOME', 'RUNNER_TOOL_CACHE', 'RUNNER_WORKSPACE']
    .map((k) => env[k])
    .filter((v) => typeof v === 'string' && v.length >= 5 && v !== '/');
  return [...new Set(vals)].sort((a, b) => b.length - a.length);
}

function listFiles(dir) {
  const out = [];
  const walk = (d) => {
    for (const ent of fs.readdirSync(d, { withFileTypes: true })) {
      const p = path.join(d, ent.name);
      if (ent.isDirectory()) walk(p);
      else out.push(p);
    }
  };
  walk(dir);
  return out.sort();
}

function maskText(text, paths, allow, tokenIndex, counts, hctx) {
  let t = text;
  for (const p of paths) {
    const parts = t.split(p);
    if (parts.length > 1) { counts.paths += parts.length - 1; t = parts.join('<러너>'); }
  }
  const hits = findSecrets(t, allow, hctx);
  for (const h of hits) if (!tokenIndex.has(h.tok)) tokenIndex.set(h.tok, tokenIndex.size + 1);
  for (let i = hits.length - 1; i >= 0; i--) {
    const h = hits[i];
    counts.tokens += 1;
    t = t.slice(0, h.s) + '<가림:' + tokenIndex.get(h.tok) + '>' + t.slice(h.e);
  }
  return t;
}

function maskDir(dir, env) {
  const allow = loadAllow();
  const paths = runnerPaths(env);
  const tokenIndex = new Map();
  const counts = { files: 0, paths: 0, tokens: 0, json_broken: [] };
  for (const f of listFiles(dir)) {
    if (!TEXT_EXT.has(path.extname(f))) continue;
    const before = fs.readFileSync(f, 'utf8');
    const after = maskText(before, paths, allow, tokenIndex, counts, hexCtx(env, path.basename(f)));
    counts.files += 1;
    if (after !== before) {
      fs.writeFileSync(f, after);
      const rel = path.relative(dir, f);
      if (f.endsWith('.json')) {
        try { JSON.parse(after); } catch (e) { counts.json_broken.push({ file: rel, error: String(e.message).slice(0, 120) }); }
      } else if (f.endsWith('.jsonl')) {
        after.split('\n').forEach((l, i) => {
          if (!l.trim()) return;
          try { JSON.parse(l); } catch (e) { counts.json_broken.push({ file: rel, line: i + 1, error: String(e.message).slice(0, 120) }); }
        });
      }
    }
  }
  const summary = { files: counts.files, paths_replaced: counts.paths, tokens_replaced: counts.tokens, distinct_tokens: tokenIndex.size, json_broken: counts.json_broken };
  const runJson = path.join(dir, 'run.json');
  if (fs.existsSync(runJson)) {
    const j = JSON.parse(fs.readFileSync(runJson, 'utf8'));
    j.mask = summary;
    fs.writeFileSync(runJson, JSON.stringify(j, null, 2) + '\n');
  }
  tokenIndex.clear();
  return summary;
}

module.exports = {
  TEXT_EXT, TOKEN_RE, B64_RE, HEX_RE, HEX_KEYS, HEX_KV, KNOWN_HEX, FIXED_ALLOW,
  loadAllow, knownHex, hexCtx, hexAllowedAt, isB64Secret, findSecrets, runnerPaths, isLongToken, listFiles, maskText, maskDir,
};

if (require.main === module) {
  const dir = process.argv[2];
  if (!dir || !fs.existsSync(dir)) { console.error('usage: node mask.js <raw-dir>'); process.exit(1); }
  try {
    const s = maskDir(dir, process.env);
    console.log('[M8] 가림 끝 ' + JSON.stringify(s));
    process.exit(0);
  } catch (e) {
    console.error('[M8] 가림 오류: ' + String(e && e.message ? e.message : e).slice(0, 300));
    process.exit(1);
  }
}
