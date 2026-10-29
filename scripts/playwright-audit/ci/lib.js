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
// 🔴 7차 M3 — CI 밖에서 가림 줄 대신 찍는 고정 문구(값과 무관 · 늘 같은 글자).
const MASK_SKIPPED_LINE = '가림 줄 생략 — CI 밖(값 출력 0)';

// 가림 — 반드시 그 값에 대한 첫 출력이어야 한다. 한 줄 값만 받는다(여러 줄은 GitHub 가 첫 줄만 가린다).
function mask(value) {
  if (typeof value !== 'string' || value.length === 0) return;
  if (/[\r\n]/.test(value)) {
    throw new Error('mask: 여러 줄 값은 가릴 수 없다 — 한 줄 꼴(base64 등)로 바꿔 넘겨라');
  }
  // ⬛ [낡은 줄 · 1~6차] 러너 판별 없이 가림 줄을 그대로 찍었다 → CI 밖에선 값 원문이 화면에(보안상무 A③ⓓ).
  // process.stdout.write(`::add-mask::${value}\n`);
  // 🔴 7차 M3(10/29 · §0-11-11-3) — GitHub 러너 안에서만 가림 줄(바이트 그대로). 밖에선 고정 문구 한 줄(값 글자·길이·앞뒤 일부 0).
  if (process.env.GITHUB_ACTIONS === 'true') {
    process.stdout.write(`::add-mask::${value}\n`);
  } else {
    process.stdout.write(`${MASK_SKIPPED_LINE}\n`);
  }
  knownSecrets.add(value); // 소독은 CI 밖에서도 돈다 — 등록은 둘 다
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

// ── 🔴 6차(10/29 · 작업지시서 §0-11-3 ①) 3꼴 한 벌 — S10(leak-scan.js)과 소독(sanitize)이 같은 꼴 목록을 쓴다 ──
// leak-scan.js 에서 본문 그대로 옮겨 왔다(결과 글자 같음). 방향 = leak-scan → lib 만(lib → leak-scan require 0 · 순환 금지).
// base64 가운데 글자 최소 길이(leak-scan 의 옛 MIN_LEN · 값 8 그대로).
const NEEDLE_MIN_LEN = 8;
// base64 는 앞에 0·1·2 바이트가 붙은 경우의 「값 바이트로만 정해지는 가운데 글자」를 찾는다.
function base64Needles(value) {
  const src = Buffer.from(value, 'utf8');
  const out = new Set();
  for (let k = 0; k < 3; k += 1) {
    const enc = Buffer.concat([Buffer.alloc(k), src]).toString('base64').replace(/=+$/, '');
    const start = Math.ceil((k * 8) / 6);
    const end = Math.floor(((k + src.length) * 8) / 6);
    const mid = enc.slice(start, end);
    if (mid.length >= NEEDLE_MIN_LEN) {
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
// 값 → 꼴별 찾을 글자 목록(raw · base64 · url). 바이트로 바꾸는 일은 쓰는 쪽이 한다(leak-scan: raw = utf8 · 나머지 = latin1).
function needleForms(value) {
  return [
    { form: 'raw', strs: [value] },
    { form: 'base64', strs: base64Needles(value) },
    { form: 'url', strs: urlNeedles(value) },
  ];
}

// ── 🔴 7차 M1(10/29 · §0-11-11-3 · K1 ㉡) GitHub 가림이 인코딩 꼴을 가리는지 재는 줄 목록 — 출력 0 · 줄만 돌려준다 ──
// 맨 글자 줄 = `가림 측정 M1 [<꼴>] <글자>` (needleForms 글자 하나에 한 줄 · 같은 글자는 한 줄 · 꼴 이름을 `·` 로 잇는다)
// 문맥 줄   = `가림 측정 M1-문맥 [<꼴>] <글자>` (K1 ㉡ · base64 = 앞 k바이트+값+뒤 1바이트 전체 3줄 · url = 요청 줄 1줄)
//   → 줄 머리가 달라 판독이 두 모양(맨 글자 가림 ≠ 문맥 속 가림)을 따로 센다.
// 끝 줄     = `가림 측정 M1 [대조·안 가림] <control>` (가리지 않은 값 — 로그가 글자를 보여 주는지 확인하는 대조)
// 판정은 하지 않는다(가림은 출력 뒤 러너가 한다 → 사람이 런 로그로 읽는다).
const M1_HEAD = '가림 측정 M1';
const M1_CTX_HEAD = '가림 측정 M1-문맥';
function maskProbeLines(probe, control) {
  for (const v of [probe, control]) {
    if (typeof v !== 'string' || v.length === 0 || /\s/.test(v)) {
      throw new Error('maskProbeLines: 표지·대조는 공백 없는 한 줄 글자만');
    }
  }
  const enc = encodeURIComponent(probe);
  const encLower = enc.replace(/%[0-9A-F]{2}/g, (m) => m.toLowerCase());
  // 측정 성립 검사 — 대·소문자 꼴이 갈리지 않으면 이 표지로는 인코딩 꼴을 못 잰다(→ 호출한 쪽이 빨강으로 끝난다).
  if (enc === encLower) throw new Error('maskProbeLines: url 대문자 꼴 = 소문자 꼴 — 이 표지로는 못 잰다');
  const labeled = new Map(); // 글자 → 꼴 이름들(넣은 순서)
  const put = (s, name) => {
    if (!labeled.has(s)) labeled.set(s, []);
    if (!labeled.get(s).includes(name)) labeled.get(s).push(name);
  };
  put(probe, '원문');
  put(enc, 'url-대문자');
  put(encLower, 'url-소문자');
  put(enc.replace(/%20/g, '+'), 'url-+공백');
  const src = Buffer.from(probe, 'utf8');
  for (let k = 0; k < 3; k += 1) {
    const full = Buffer.concat([Buffer.alloc(k), src]).toString('base64').replace(/=+$/, '');
    const mid = full.slice(Math.ceil((k * 8) / 6), Math.floor(((k + src.length) * 8) / 6));
    if (mid.length < NEEDLE_MIN_LEN) continue;
    put(mid, `base64-자리${k}`);
    put(mid.replace(/\+/g, '-').replace(/\//g, '_'), `base64-자리${k}-url안전`);
  }
  // 꼴 목록이 소독(needleForms)과 같은 글자 묶음인지 — 어긋나면 장치가 틀렸다(재는 것 ≠ 지우는 것).
  const fromForms = new Set();
  for (const { strs } of needleForms(probe)) for (const s of strs) if (s) fromForms.add(s);
  if (fromForms.size !== labeled.size || [...fromForms].some((s) => !labeled.has(s))) {
    throw new Error('maskProbeLines: 꼴 목록이 needleForms 와 다르다 — 장치 오류');
  }
  const lines = [];
  for (const [s, names] of labeled) lines.push(`${M1_HEAD} [${names.join('·')}] ${s}`);
  for (let k = 0; k < 3; k += 1) {
    const ctx = Buffer.concat([Buffer.from('x'.repeat(k), 'latin1'), src, Buffer.from('.', 'latin1')]).toString('base64');
    lines.push(`${M1_CTX_HEAD} [base64-자리${k}-앞${k}바이트·뒤1바이트] ${ctx}`);
  }
  lines.push(`${M1_CTX_HEAD} [url-대문자-요청줄] GET /x?q=${enc}&n=1`);
  lines.push(`${M1_HEAD} [대조·안 가림] ${control}`);
  return lines;
}

// ── 🔴 7차 M4(10/29 · §0-11-11-3) 실값 유입 기계 검사 — 읽기만 · 출력 0 · 줄 목록을 돌려준다 ──
// 대상 = dir 의 *.yml·*.yaml 중 본문에 장치 경로가 든 파일. 대상 0 = 빨강(빈 검색은 거짓 통과).
// 건너뛰는 줄 = 통째 주석(첫 글자 #)이면서 `${{` 없는 줄뿐(셸 주석 속 식도 GitHub 가 값으로 바꾼다 → 검사).
// 잡힘 줄에는 파일명·줄 번호·종류만(줄 글자 0 — 주소·식 안에 값이 섞일 수 있다).
// 한계(K3·K4 결재): 실수로 생긴 유입 감지까지 · 위변조는 범위 밖 · 낱말 = 비밀값·입력 칸·주소 3종만.
const INTAKE_MARK = 'scripts/playwright-audit/ci/';
const INTAKE_LOCAL_HOSTS = new Set(['127.0.0.1', 'localhost']);
function intakeHostOf(s) {
  let h = String(s).replace(/^[A-Za-z][A-Za-z0-9+.-]*:\/\//, ''); // scheme://
  h = h.split(/[/?#\s"'`;,)]/)[0]; // 경로·꼬리 뗌
  h = h.slice(h.lastIndexOf('@') + 1); // 사용자@ 뗌
  h = h.replace(/:\d*$/, ''); // 포트 뗌
  return h.toLowerCase();
}
function intakeAddressBad(line) {
  // ㉮ scheme://호스트 전부
  for (const m of line.matchAll(/[A-Za-z][A-Za-z0-9+.-]*:\/\/[^\s"'`;,)]*/g)) {
    if (!INTAKE_LOCAL_HOSTS.has(intakeHostOf(m[0]))) return true;
  }
  // ㉯ 이름이 _HOST·_URL·_URLS·_BASE 로 끝나는 칸의 값(KEY=값 · KEY: 값)
  for (const m of line.matchAll(/(?<![A-Za-z0-9_])[A-Za-z0-9_]*(?:_HOST|_URLS|_URL|_BASE)\s*[=:][ \t]*(\S*)/gi)) {
    const v = m[1].replace(/^["']+/, '').replace(/["',;]+$/, '');
    if (v === '') continue; // 값 없음(YAML 칸 머리 등)
    if (v.startsWith('${{')) return true; // 밖에서 정해지는 주소
    if (v.startsWith('$')) continue; // 셸 변수 — 원 값이 따로 검사된다
    if (!INTAKE_LOCAL_HOSTS.has(intakeHostOf(v))) return true;
  }
  return false;
}
function intakeScan(dir) {
  const targets = [];
  const hits = [];
  const names = fs.existsSync(dir) && fs.statSync(dir).isDirectory()
    ? fs.readdirSync(dir).filter((n) => /\.ya?ml$/i.test(n)).sort()
    : [];
  for (const name of names) {
    const p = path.join(dir, name);
    if (!fs.statSync(p).isFile()) continue;
    const text = fs.readFileSync(p, 'utf8');
    if (!text.includes(INTAKE_MARK)) continue;
    targets.push(name);
    text.split(/\r?\n/).forEach((line, i) => {
      const t = line.trim();
      if (t.startsWith('#') && !t.includes('${{')) return; // 통째 주석이면서 식 없음
      const kinds = new Set();
      if (/\bsecrets\b/i.test(line)) kinds.add('비밀값');
      if (/\binputs\b/i.test(line)) kinds.add('입력 칸');
      if (intakeAddressBad(line)) kinds.add('주소');
      for (const kind of kinds) hits.push({ file: name, line: i + 1, kind });
    });
  }
  const ok = targets.length > 0 && hits.length === 0;
  const lines = [];
  if (targets.length === 0) lines.push('⚫ FAIL [장치] M4 실값 유입 — 대상 0개(장치를 부르는 워크플로를 못 찾음 · 빈 검색은 거짓 통과)');
  for (const h of hits) lines.push(`⚫ FAIL [장치] M4 실값 유입 — ${h.file}:${h.line} · 종류=${h.kind}`);
  if (ok) lines.push(`🟢 PASS [장치] M4 실값 유입 0 — 대상 ${targets.length}개`);
  return { ok, targets, hits, lines };
}

// 출력 전 소독 — 알고 있는 생성값이 섞였으면 *** 로 바꾼다(가림 위에 한 겹 더).
// 4차(10/29) — 길이 상한을 고를 수 있게(기본 300 그대로). OBS 줄은 관찰 항목이 여럿이라 더 길게 남긴다.
// ⬛ [낡은 몸통 · 1~5차] 원문만 지웠다 — 인코딩 꼴(URL·base64)은 그대로 남았다([4-X] 1/3 X702-H1).
// function sanitize(text, cap = 300) {
//   let s = String(text ?? '');
//   for (const { value } of collectSecrets()) {
//     if (value && value.length >= 4) s = s.split(value).join('***');
//   }
//   return s.length > cap ? `${s.slice(0, cap)}…(이하 생략)` : s;
// }
// 🔴 6차 ① — 값마다 3꼴 전부(원문 기준 길이 ≥4 는 그대로) · 모든 값의 모든 꼴을 **긴 것부터** 지운다 · 지운 **뒤에** 자른다.
function sanitize(text, cap = 300) {
  let s = String(text ?? '');
  const needles = new Set();
  for (const { value } of collectSecrets()) {
    if (!value || value.length < 4) continue;
    for (const { strs } of needleForms(value)) {
      for (const n of strs) if (n) needles.add(n);
    }
  }
  for (const n of [...needles].sort((a, b) => b.length - a.length)) s = s.split(n).join('***');
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
// ⬛ [낡은 몸통 · 1~4차] fs.writeFileSync(p, JSON.stringify(r, null, 2)); — 쓰는 도중 프로세스가 죽으면 반쪽 파일이 남는다.
// 🔴 5차(10/29 · 런6 40750810674 「콘솔 PASS 7 · 결과표 0」) — 임시 파일에 다 쓴 뒤 이름을 바꾼다(반쪽 결과표 0).
function saveResult(r) {
  const p = resultPath();
  fs.mkdirSync(path.dirname(p), { recursive: true });
  const tmp = `${p}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(r, null, 2));
  fs.renameSync(tmp, p);
}

// 🔴 5차 ① — 줄마다 바로 쓰기. bindAutosave(r) 를 부른 결과표는 rec 가 줄을 넣을 때마다 파일에 쓴다
//   → 브라우저 흐름이 어떤 예외로 끊겨도(프로세스가 죽어도) 그때까지 콘솔에 찍힌 줄은 결과표에도 있다(콘솔 = 결과표).
const autosaved = new WeakSet();
function bindAutosave(r) {
  autosaved.add(r);
  saveResult(r);
}

// 🔴 5차 ① — 끊긴 자리 한 줄용 예외 설명: 「이름: 메시지 앞 200자」. 값·토큰 0 —
//   알고 있는 생성값(sanitize) · 인증 머리글 꼴 · 점 셋 토큰 꼴을 **자르기 전에** 지운다(잘린 반쪽 값이 남지 않게).
function describeError(e, cap = 200) {
  const name = e && e.name ? String(e.name) : typeof e;
  let msg = e && e.message !== undefined ? String(e.message) : String(e);
  // ⬛ [낡은 순서 · 5차] 소독 → 색 글자 제거 — 값 사이에 색 글자가 끼면 소독이 놓치고, 지운 뒤 값이 다시 이어졌다.
  // msg = sanitize(msg, Number.MAX_SAFE_INTEGER)
  //   .replace(/\u001b\[[0-9;]*m/g, '') // 색 제어 글자(Playwright 호출 기록)
  // ⬛ [낡은 순서 · 6차] 색 글자 제거 → 3꼴 소독 → Bearer·점 셋 → 공백 접기 → 자르기
  //   — 접기가 값 안 공백과 다른 공백(줄바꿈·두 칸)을 한 칸으로 바꿔 값이 접힌 **뒤에** 다시 생겼다([4-X] X702-H1 ⓓ).
  // 🔴 7차 ⓓ(10/29 · §0-11-11-3) — 순서 = 색 글자 제거 → 3꼴 소독 → Bearer·점 셋 → 공백 접기 → 3꼴 소독(둘째 · 자르지 않음) → 자르기.
  msg = msg.replace(/\u001b\[[0-9;]*m/g, ''); // 색 제어 글자(Playwright 호출 기록)
  msg = sanitize(msg, Number.MAX_SAFE_INTEGER)
    .replace(/Bearer\s+\S+/gi, 'Bearer ***')
    .replace(/[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}/g, '***')
    .replace(/\s+/g, ' ')
    .trim();
  msg = sanitize(msg, Number.MAX_SAFE_INTEGER); // 7차 ⓓ — 접은 뒤 한 번 더(자르지 않음)
  return `${name}: ${msg.length > cap ? `${msg.slice(0, cap)}…` : msg}`;
}

// 🔴 6차 ② — 콘솔에 찍는 예외 **이름**. `[A-Za-z0-9_]` 1~40자일 때만 그대로 · 아니면 고정 `Error`.
//   (한 겹 더: 이름 안에 알고 있는 값의 꼴이 들어 있으면 소독에서 글자가 바뀐다 → 그때도 `Error`.)
function errorName(e) {
  const n = e && typeof e === 'object' && e.name !== undefined ? String(e.name) : '';
  return /^[A-Za-z0-9_]{1,40}$/.test(n) && sanitize(n, 40) === n ? n : 'Error';
}
// 🔴 6차 ② — 결과표 err 칸 상한(PM 확정 §0-11-9 · 1000자). 콘솔 상한은 지금 그대로(got 300 · OBS 1200).
const ERR_CAP = 1000;
// 한 줄 기록 — 기존 audit 스크립트의 rec(id, what, got, pass) 꼴 + kind·scope.
// 4차(10/29) — kind `OBS`(제품 관찰) 추가. 기록만 허용하고 **허용 id 판정은 judge.js 한 곳**(OBS_ALLOWED)이 한다
//   — 목록 밖 id 의 OBS 줄은 judge 가 「줄 꼴 이상」 ⚫[장치] 빨강으로 세운다.
// ⬛ [낡은 줄 · 1~5차] function rec(r, { id, what, got, pass, kind, scope = 'H', browser = null, path: way = null, g = null }) {
// 🔴 6차 ② — err 칸: 예외(하나 또는 배열 · null 은 건너뜀)를 받으면 **결과표 줄의 err 칸에만** 소독본(describeError · 1000자)으로 남기고,
//   콘솔 줄에는 `예외=<이름>` 만 붙인다. rec 1회 = 콘솔 1줄 · 순서 = 결과표 줄 순서(바뀌는 것은 글자뿐).
function rec(r, { id, what, got, pass, kind, scope = 'H', browser = null, path: way = null, g = null, err = null }) {
  // ⬛ [낡은 줄 · 1~3차] if (kind !== 'TEST' && kind !== 'DEVICE') throw new Error(`rec: kind 는 TEST/DEVICE 뿐 (${kind})`);
  if (kind !== 'TEST' && kind !== 'DEVICE' && kind !== 'OBS') throw new Error(`rec: kind 는 TEST/DEVICE/OBS 뿐 (${kind})`);
  const cap = kind === 'OBS' ? 1200 : 300;
  const row = { id, g, path: way, browser, what, got: sanitize(typeof got === 'string' ? got : JSON.stringify(got), cap), pass: !!pass, kind, scope };
  const errs = (Array.isArray(err) ? err : [err]).filter((x) => x !== null && x !== undefined);
  if (errs.length > 0) {
    // 하나씩 소독(자르기 전 3꼴 제거) → 이어 붙인 것을 한 번 더 소독하며 1000자에서 자른다.
    row.err = sanitize(errs.map((x) => describeError(x, ERR_CAP)).join(' | '), ERR_CAP);
  }
  const errTail = errs.length > 0 ? ` · 예외=${errs.map(errorName).join(',')}` : '';
  r.rows.push(row);
  if (autosaved.has(r)) saveResult(r); // 5차 ① — 콘솔에 찍기 전에 파일에 먼저(찍혔는데 표에 없는 줄 0)
  if (kind === 'OBS') {
    // ⬛ [낡은 줄 · 1~5차] console.log(`🟡 OBS ${id}${browser ? ` (${browser})` : ''} — ${what} · got=${row.got}`);
    console.log(`🟡 OBS ${id}${browser ? ` (${browser})` : ''} — ${what} · got=${row.got}${errTail}`);
    return row;
  }
  const mark = row.pass ? '🟢 PASS' : (kind === 'TEST' ? '🔴 FAIL [시험]' : '⚫ FAIL [장치]');
  // ⬛ [낡은 줄 · 1~5차] console.log(`${mark} ${id}${browser ? ` (${browser})` : ''} — ${what} · got=${row.got}`);
  console.log(`${mark} ${id}${browser ? ` (${browser})` : ''} — ${what} · got=${row.got}${errTail}`);
  return row;
}

module.exports = {
  S0_NAMES, SECRET_PREFIX,
  mask, maskAndRegister, exportPlain, collectSecrets, sanitize,
  outDir, resultPath, loadResult, saveResult, rec,
  bindAutosave, describeError, // 5차 ①
  NEEDLE_MIN_LEN, base64Needles, urlNeedles, needleForms, errorName, ERR_CAP, // 6차 ①②
  MASK_SKIPPED_LINE, maskProbeLines, intakeScan, // 7차 M3·M1·M4
};
