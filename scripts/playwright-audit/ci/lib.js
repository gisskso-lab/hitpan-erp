// G1-3 갈래 H — CI 화면 시험 장치 공용 도구 (작업지시서 20261028작G1-3 §3-2 S0·S10 · §0-9 ⑫ⓐ~ⓒ)
//
// 🔴 이 파일의 규칙
//   ① 가림(add-mask) 줄은 **일반 node 가 표준출력에 그대로** 쓴다(process.stdout.write). 시험 러너를 끼우지 않는다(⑫ⓐ).
//   ② 생성값은 화면·파일에 원문으로 쓰지 않는다. 런 중에 생긴 값은 받는 즉시 가리고 $GITHUB_ENV 의
//      `UI_SECRET_<표지>_<번호>` 로만 넘긴다 — S10 누출 검사가 그 이름들을 모아 검색한다(§3-2 S0 ⓐⓑ).
//   ③ 빈 catch 금지(#15) — 원문(응답 본문·예외 글자)은 찍지 않고 고정 문구만 찍는다(⑫ⓒ).
'use strict';

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const SECRET_PREFIX = 'UI_SECRET_';
// S0 이 만드는 값의 이름 — S10 은 이 일곱 개가 전부 있어야 검사를 시작한다(보안상무 ⑩: 비면 빨강).
const S0_NAMES = [
  'UI_DB_PASSWORD',
  'UI_JWT_SECRET',
  'UI_ENC_KEY',
  'UI_BOOT_KEY',
  'UI_PW_PARENT',
  'UI_PW_GA',
  'UI_PW_NA',
];

let registerCounter = 0;
const knownSecrets = new Set();

// 가림 — 반드시 그 값에 대한 첫 출력이어야 한다. 한 줄 값만 받는다(여러 줄은 GitHub 가 첫 줄만 가린다).
function mask(value) {
  if (typeof value !== 'string' || value.length === 0) return;
  if (/[\r\n]/.test(value)) {
    throw new Error('mask: 여러 줄 값은 가릴 수 없다 — 한 줄 꼴(base64 등)로 바꿔 넘겨라');
  }
  process.stdout.write(`::add-mask::${value}\n`);
  knownSecrets.add(value);
}

// 가리고 + S10 검색 대상으로 넘긴다. label 은 영문 대문자·숫자만(값이 아니다).
function maskAndRegister(label, value) {
  mask(value);
  if (typeof value !== 'string' || value.length === 0) return;
  const envFile = process.env.GITHUB_ENV;
  if (!envFile) return; // 로컬 문법 확인 등 CI 밖 — 넘길 곳이 없다(값은 이 프로세스 메모리에만).
  registerCounter += 1;
  const tag = crypto.randomBytes(3).toString('hex').toUpperCase();
  const safeLabel = String(label).toUpperCase().replace(/[^A-Z0-9]/g, '_');
  fs.appendFileSync(envFile, `${SECRET_PREFIX}${safeLabel}_${registerCounter}_${tag}=${value}\n`, { mode: 0o600 });
}

// 비밀이 아닌 값(방 번호·아이디)을 다음 단계로 넘긴다.
function exportPlain(name, value) {
  const envFile = process.env.GITHUB_ENV;
  if (!envFile) return;
  if (/[\r\n]/.test(String(value))) throw new Error('exportPlain: 한 줄 값만');
  fs.appendFileSync(envFile, `${name}=${value}\n`);
}

// 지금 알고 있는 모든 생성값(S0 + 런 중 등록분 + 이 프로세스가 가린 값).
function collectSecrets(env = process.env) {
  const out = [];
  for (const n of S0_NAMES) {
    if (env[n]) out.push({ label: n, value: env[n] });
  }
  for (const [k, v] of Object.entries(env)) {
    if (k.startsWith(SECRET_PREFIX) && v) out.push({ label: k.replace(/_[0-9A-F]{6}$/, ''), value: v });
  }
  for (const v of knownSecrets) out.push({ label: 'PROCESS_LOCAL', value: v });
  return out;
}

// 출력 전 소독 — 알고 있는 생성값이 섞였으면 *** 로 바꾼다(가림 위에 한 겹 더).
// 4차(10/29) — 길이 상한을 고를 수 있게(기본 300 그대로). OBS 줄은 관찰 항목이 여럿이라 더 길게 남긴다.
function sanitize(text, cap = 300) {
  let s = String(text ?? '');
  for (const { value } of collectSecrets()) {
    if (value && value.length >= 4) s = s.split(value).join('***');
  }
  return s.length > cap ? `${s.slice(0, cap)}…(이하 생략)` : s;
}

// ── 결과표 ui-k35-result.json (§4-2 · kind TEST / DEVICE) ─────────────────────────
function outDir() {
  const base = process.env.UI_OUT || path.join(process.cwd(), 'ui-out');
  return base;
}
function resultPath() {
  return path.join(outDir(), 'main', 'ui-k35-result.json');
}
function loadResult() {
  const p = resultPath();
  if (!fs.existsSync(p)) {
    return { schema: 'ui-k35-result/1', seed: null, external_blocked: 0, external_hosts: [], rows: [] };
  }
  return JSON.parse(fs.readFileSync(p, 'utf8'));
}
function saveResult(r) {
  const p = resultPath();
  fs.mkdirSync(path.dirname(p), { recursive: true });
  fs.writeFileSync(p, JSON.stringify(r, null, 2));
}
// 한 줄 기록 — 기존 audit 스크립트의 rec(id, what, got, pass) 꼴 + kind·scope.
// 4차(10/29) — kind `OBS`(제품 관찰) 추가. 기록만 허용하고 **허용 id 판정은 judge.js 한 곳**(OBS_ALLOWED)이 한다
//   — 목록 밖 id 의 OBS 줄은 judge 가 「줄 꼴 이상」 ⚫[장치] 빨강으로 세운다.
function rec(r, { id, what, got, pass, kind, scope = 'H', browser = null, path: way = null, g = null }) {
  // ⬛ [낡은 줄 · 1~3차] if (kind !== 'TEST' && kind !== 'DEVICE') throw new Error(`rec: kind 는 TEST/DEVICE 뿐 (${kind})`);
  if (kind !== 'TEST' && kind !== 'DEVICE' && kind !== 'OBS') throw new Error(`rec: kind 는 TEST/DEVICE/OBS 뿐 (${kind})`);
  const cap = kind === 'OBS' ? 1200 : 300;
  const row = { id, g, path: way, browser, what, got: sanitize(typeof got === 'string' ? got : JSON.stringify(got), cap), pass: !!pass, kind, scope };
  r.rows.push(row);
  if (kind === 'OBS') {
    console.log(`🟡 OBS ${id}${browser ? ` (${browser})` : ''} — ${what} · got=${row.got}`);
    return row;
  }
  const mark = row.pass ? '🟢 PASS' : (kind === 'TEST' ? '🔴 FAIL [시험]' : '⚫ FAIL [장치]');
  console.log(`${mark} ${id}${browser ? ` (${browser})` : ''} — ${what} · got=${row.got}`);
  return row;
}

module.exports = {
  S0_NAMES, SECRET_PREFIX,
  mask, maskAndRegister, exportPlain, collectSecrets, sanitize,
  outDir, resultPath, loadResult, saveResult, rec,
};
