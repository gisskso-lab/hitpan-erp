'use strict';
// M8 [장치] 가림 — 설계 §5
//   사용: node mask.js <원자료 폴더>
//   ① 러너 경로(GITHUB_WORKSPACE · RUNNER_TEMP · HOME 실값) → <러너>
//   ② 영숫자 · '-' · '_' 22자 이상 토큰 중 40자 · 64자 16진(SHA)이 아닌 것 → <가림:n>
//      (같은 토큰 = 같은 n · 값은 메모리에만 · 어디에도 안 씀)
//   ③ 텍스트 파일(.json · .jsonl · .txt)만 · PNG 는 안 건드림
//   가린 개수는 run.json 의 mask 칸에 수로만. 종료 코드 0 = 끝 · 1 = 처리 오류
const fs = require('fs');
const path = require('path');

const TEXT_EXT = new Set(['.json', '.jsonl', '.txt']);
const TOKEN_RE = /(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{22,}(?![A-Za-z0-9_-])/g;
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

function loadAllow() {
  const allow = new Set(FIXED_ALLOW);
  // 갈래 B 가 판독 선택자 이름 등 공개 고정 이름을 더할 자리: rules.json 의 mask_allow (문자열 배열)
  const rules = path.join(__dirname, 'rules.json');
  if (fs.existsSync(rules)) {
    const j = JSON.parse(fs.readFileSync(rules, 'utf8'));
    if (Array.isArray(j.mask_allow)) {
      for (const s of j.mask_allow) if (typeof s === 'string' && !HEX_RE.test(s)) allow.add(s);
    }
  }
  return allow;
}

function runnerPaths(env) {
  const vals = ['GITHUB_WORKSPACE', 'RUNNER_TEMP', 'HOME', 'RUNNER_TOOL_CACHE', 'RUNNER_WORKSPACE']
    .map((k) => env[k])
    .filter((v) => typeof v === 'string' && v.length >= 5 && v !== '/');
  return [...new Set(vals)].sort((a, b) => b.length - a.length);
}

function isLongToken(tok, allow) {
  return tok.length >= 22 && !HEX_RE.test(tok) && !allow.has(tok);
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

function maskText(text, paths, allow, tokenIndex, counts) {
  let t = text;
  for (const p of paths) {
    const parts = t.split(p);
    if (parts.length > 1) { counts.paths += parts.length - 1; t = parts.join('<러너>'); }
  }
  t = t.replace(TOKEN_RE, (tok) => {
    if (!isLongToken(tok, allow)) return tok;
    if (!tokenIndex.has(tok)) tokenIndex.set(tok, tokenIndex.size + 1);
    counts.tokens += 1;
    return '<가림:' + tokenIndex.get(tok) + '>';
  });
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
    const after = maskText(before, paths, allow, tokenIndex, counts);
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

module.exports = { TEXT_EXT, TOKEN_RE, HEX_RE, FIXED_ALLOW, loadAllow, runnerPaths, isLongToken, listFiles, maskDir };

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
