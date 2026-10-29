// G1-3 갈래 H — S10 누출 검사 단독 시험 (DB·API·브라우저 없이 · 가짜 폴더만)
//
// 사용: node leak-scan.selftest.js <빈 작업 폴더>
// ⬛ [낡은 줄 · 1~5차] 경우 7개 — 기대 종료 코드·출력으로 판정(대조군 포함 · 「항상 빨강」/「항상 초록」 아님을 같이 본다).
// 🔴 6차(10/29 · 작업지시서 §0-11-5) — 경우 C1~C11 · 기대 종료 코드·출력으로 판정(대조군 포함 · 「항상 빨강」/「항상 초록」 아님을 같이 본다).
//   C1 깨끗한 폴더 → 0(초록)                         ← 대조군
//   C2 S0 값 하나 비움 → 1 · 「대상 비었음」           ← 보안상무 ⑩
//   C3 원문 누출(main) → 1 · main 지워짐
//   C4 base64 누출(시작 자리 1·2 밀림) → 1
//   C5 URL 인코딩 누출 → 1
//   C6 trace zip 안 resources/ 의 원문 누출 → 1 · trace 지워짐 · main 남음
//   ⬛ [낡은 줄 · 1~5차] C7 심은 값 3꼴(UI_LEAK_PLANT=1) → 1 · 꼴마다 「잡음」 3줄 · 「못 잡음」 0
//   C7 심은 값 4개(UI_LEAK_PLANT=1 · 6차 ⑤ 이름에 심은 값 추가) → 1 · 「잡음」 4줄 · 「못 잡음」 0
//   C8 망가진 zip → 1 · 「못 푼 zip」
//   🔴 6차 — 아래는 lib 을 직접(또는 자식 프로세스로) 잰다. 고치기 전 lib·leak-scan 에 붙이면 빨강이어야 한다(대조군).
//   C9  ① sanitize·describeError 가 알고 있는 값의 3꼴(원문 · URL 대·소문자·+공백 · base64 자리 0·1·2 · url 안전)을 다 지움
//       └ 「원문」 줄은 고치기 전에도 초록 = 대조 줄(이것도 빨강이면 시험이 틀렸다)
//   C9b ① 인코딩 꼴이 자르기 경계(200자)에 걸쳐도 반쪽 0
//   C9c ① 값 한가운데 색 제어 글자 → 지운 뒤 이어 붙은 값 0
//   C10 ② rec 의 err 칸 — 예외 글자는 결과표 err 칸에만 · 콘솔은 「예외=<이름>」 · 콘솔 줄 수·순서 = 결과표
//       └ 「옛 꼴」(got 에 describeError) 줄은 콘솔에 표지가 보여야 한다 = 대조 줄(검사가 볼 수 있음)
//   C11 ⑤ 내용 깨끗한 파일의 이름(하위 폴더명 원문 · 파일명 URL 꼴 · zip 항목명 base64 꼴) → 「이름」 잡힘 · 폴더 지움 · 콘솔에 꼴 0
// 🔴 7차(10/29 · 작업지시서 §0-11-11-5) — 경우 7개 더함(26 → 33). 고치기 전 lib 에 붙이면 C13b 만 초록(대조 줄) · 나머지 6 빨강.
//   C9d  ⓓ 값 안 공백을 줄바꿈+두 칸으로 바꾼 메시지 → 공백 접기 뒤에도 원문 0 · *** 있음
//   C12  M1 maskProbeLines — 꼴 글자를 시험이 따로 계산해 대조(맨 글자 줄 정확히 한 줄씩 · url 대·소문자 둘 · 원문은 [원문] 줄에만 · 대조 값 그대로 · 문맥 줄 4)
//   C13a M3 자식 프로세스 CI 밖 L.mask → 고정 문구 1줄 · 값 0
//   C13b M3 자식 프로세스 GITHUB_ACTIONS=true → `::add-mask::<값>\n` 바이트 그대로 = 대조 줄(빨강이면 시험이 틀렸다)
//   C14a M4 심은 워크플로 글(비밀값·입력 칸·주소 각 1 + 미끼 통째 주석) → 잡힘 정확히 3 · 줄 번호 맞음 · 출력에 심은 글자 0
//   C14b M4 레포 .github/workflows 지금 그대로 → 대상 1(ui-browser.yml) · 잡힘 0(「항상 빨강」 아님)
//   C14c M4 장치 경로 없는 워크플로만 → 대상 0 = 빨강
// 가짜 값은 SELFTEST_ 머리 + 16진(+ 비번 꼴 기호)만 — 자격증명 모양 0.
'use strict';

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const crypto = require('crypto'); // 6차 — C9 가짜 값 16진
const { makeZip, plantValues } = require('./leak-scan');
const L = require('./lib'); // 6차 — C9 는 lib 의 sanitize·describeError 를 직접 잰다

const work = process.argv[2];
if (!work) { console.log('사용: node leak-scan.selftest.js <빈 작업 폴더>'); process.exit(2); }
const SCAN = path.join(__dirname, 'leak-scan.js');

function fakeEnv() {
  return {
    UI_DB_PASSWORD: 'SELFTEST_DBPW_7f3a9c2e',
    UI_JWT_SECRET: 'SELFTEST_JWT_1b8d4e6f',
    UI_ENC_KEY: 'SELFTEST_ENC_9e2c7a1d',
    UI_BOOT_KEY: 'SELFTEST_BOOT_4a6f8b3c',
    UI_PW_PARENT: 'SELFTEST#PWP/5d2e+9a',
    UI_PW_GA: 'SELFTEST_PWGA_3c7e1f8b',
    UI_PW_NA: 'SELFTEST_PWNA_8a1d5c2e',
    UI_SECRET_TOKEN_ACCESS_NA_1_ABC123: 'SELFTEST_TOKEN_e4b7c9a2d1',
  };
}
function fresh(name) {
  const root = path.join(work, name);
  fs.rmSync(root, { recursive: true, force: true });
  fs.mkdirSync(path.join(root, 'main', 'shots'), { recursive: true });
  fs.mkdirSync(path.join(root, 'trace'), { recursive: true });
  fs.writeFileSync(path.join(root, 'main', 'ui-k35-result.json'), '{"rows":[]}');
  fs.writeFileSync(path.join(root, 'main', 'shots', 'a.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47, 1, 2, 3]));
  return root;
}
function run(root, envPatch = {}) {
  const base = { PATH: process.env.PATH, SystemRoot: process.env.SystemRoot };
  const env = { ...base, ...fakeEnv(), ...envPatch };
  for (const [k, v] of Object.entries(env)) if (v === undefined) delete env[k];
  const r = spawnSync(process.execPath, [SCAN, root], { env, encoding: 'utf8' });
  return { code: r.status, out: r.stdout + r.stderr };
}

const results = [];
function check(name, cond, detail) {
  results.push({ name, ok: !!cond });
  console.log(`${cond ? 'PASS' : 'FAIL'} ${name}${detail ? ` — ${detail}` : ''}`);
}

const E = fakeEnv();
{ // C1
  const root = fresh('c1');
  const r = run(root);
  check('C1 깨끗한 폴더 = 초록', r.code === 0 && /PASS 누출 0/.test(r.out), `code=${r.code}`);
}
{ // C2
  const root = fresh('c2');
  const r = run(root, { UI_PW_NA: undefined });
  check('C2 S0 값 비면 빨강(⑩)', r.code === 1 && /대상 비었음: UI_PW_NA/.test(r.out), `code=${r.code}`);
}
{ // C3
  const root = fresh('c3');
  fs.writeFileSync(path.join(root, 'main', 'api.log'), `conn pwd=${E.UI_DB_PASSWORD};`);
  const r = run(root);
  check('C3 원문 누출 잡음 · main 지움', r.code === 1 && /UI_DB_PASSWORD · 꼴=raw/.test(r.out) && !fs.existsSync(path.join(root, 'main')), `code=${r.code}`);
}
{ // C4
  const root = fresh('c4');
  const b1 = Buffer.from(`x${E.UI_JWT_SECRET}`).toString('base64');
  const b2 = Buffer.from(`xy${E.UI_ENC_KEY}z`).toString('base64').replace(/\+/g, '-').replace(/\//g, '_');
  fs.writeFileSync(path.join(root, 'main', 'b.txt'), `${b1}\n${b2}`);
  const r = run(root);
  check('C4 base64 누출(자리 1·2 · url 안전) 잡음', r.code === 1 && /UI_JWT_SECRET · 꼴=base64/.test(r.out) && /UI_ENC_KEY · 꼴=base64/.test(r.out), `code=${r.code}`);
}
{ // C5
  const root = fresh('c5');
  fs.writeFileSync(path.join(root, 'main', 'u.txt'), `GET /a?p=${encodeURIComponent(E.UI_PW_PARENT)}`);
  const r = run(root);
  check('C5 URL 인코딩 누출 잡음', r.code === 1 && /UI_PW_PARENT · 꼴=url/.test(r.out) && !/UI_PW_PARENT · 꼴=raw/.test(r.out), `code=${r.code}`);
}
{ // C6
  const root = fresh('c6');
  fs.writeFileSync(path.join(root, 'trace', 't.zip'), makeZip([
    { name: 'trace.trace', data: Buffer.from('{"type":"before"}') },
    { name: 'resources/abc.json', data: Buffer.from(`{"accessToken":"${E.UI_SECRET_TOKEN_ACCESS_NA_1_ABC123}"}`) },
  ]));
  const r = run(root);
  check('C6 trace zip 안 resources 잡음 · trace 만 지움', r.code === 1 && /resources\/abc\.json/.test(r.out)
    && !fs.existsSync(path.join(root, 'trace')) && fs.existsSync(path.join(root, 'main')), `code=${r.code}`);
}
{ // C7
  const root = fresh('c7');
  const r = run(root, { UI_LEAK_PLANT: '1' });
  const caught = (r.out.match(/심은 값 PLANT_[A-Z0-9]+: 꼴 [a-z0-9]+ 잡음/g) || []).length;
  // ⬛ [낡은 줄 · 1~5차] check('C7 심은 값 3꼴 꼴마다 잡음', r.code === 1 && caught === 3 && !/못 잡음/.test(r.out) && /의도된 심은 빨강/.test(r.out), `code=${r.code} 잡음=${caught}`);
  // ⬛ [낡은 줄 · 1~5차] check('C7b 출력에 심은 값 원문 0(가림 줄 제외)', !r.out.split('\n').some((l) => !l.startsWith('::add-mask::') && /FAKE_LEAK_[AB]_|FAKE LEAK\/C\+/.test(l)));
  // 🔴 6차 ⑤ — 심은 값 4개(PLANT_NAME = 내용 깨끗한 파일의 이름 · 꼴 raw · 자리 이름).
  check('C7 심은 값 4개 꼴마다 잡음(이름 포함)', r.code === 1 && caught === 4 && /심은 값 PLANT_NAME: 꼴 raw 잡음 · 자리=이름/.test(r.out)
    && !/못 잡음/.test(r.out) && /의도된 심은 빨강/.test(r.out), `code=${r.code} 잡음=${caught}`);
  check('C7b 출력에 심은 값 원문 0(가림 줄 제외)', !r.out.split('\n').some((l) => !l.startsWith('::add-mask::') && /FAKE_LEAK_[ABD]_|FAKE LEAK\/C\+/.test(l)));
}
{ // C8
  const root = fresh('c8');
  fs.writeFileSync(path.join(root, 'trace', 'broken.zip'), Buffer.from('PK not a zip'));
  const r = run(root);
  check('C8 못 푼 zip = 빨강', r.code === 1 && /못 푼 zip/.test(r.out), `code=${r.code}`);
}
{ // ⑪ 서로 부분 문자열 아님 — 100회
  let ok = true;
  for (let i = 0; i < 100; i += 1) {
    const v = Object.values(plantValues());
    for (const a of v) for (const b of v) if (a !== b && (a.includes(b) || b.includes(a))) ok = false;
  }
  // ⬛ [낡은 줄 · 1~5차] check('⑪ 심은 값 3개 서로 부분 문자열 아님(100회)', ok);
  check('⑪ 심은 값끼리 서로 부분 문자열 아님(100회)', ok);
}

// ── 🔴 6차(10/29 · §0-11-5) C9~C11 ─────────────────────────────────────────────
// 시험 안에서 꼴을 따로 계산한다(lib 의 함수로 lib 을 재지 않는다 — 독립 잣대).
function b64Mid(value, k) {
  const src = Buffer.from(value, 'utf8');
  const enc = Buffer.concat([Buffer.alloc(k), src]).toString('base64').replace(/=+$/, '');
  return { full: enc, mid: enc.slice(Math.ceil((k * 8) / 6), Math.floor(((k + src.length) * 8) / 6)) };
}
const hex = () => crypto.randomBytes(4).toString('hex');
// C9 값 — 비번 꼴 기호(공백 · # · / · + · ~) 포함. 「~」 는 base64 자리 하나에서 「+」 를 만든다(url 안전 꼴이 달라지게).
const C9_VALUE = `SELFTEST_C9 #${hex()}/${hex()}~${hex()}+${hex()}`;
process.env.UI_SECRET_SELFTEST_C9_1_ABCDEF = C9_VALUE; // 이 프로세스 안에서만(자식 run() 은 env 를 따로 만든다)

function c9Check(name, msg, needle) {
  const d = L.describeError(new Error(msg));
  const s = L.sanitize(msg, Number.MAX_SAFE_INTEGER);
  const gone = !d.includes(needle) && !s.includes(needle);
  check(name, gone && d.includes('***') && s.includes('***'), `describeError 남음=${d.includes(needle)} · sanitize 남음=${s.includes(needle)}`);
}
{ // C9 ① 꼴마다
  const enc = encodeURIComponent(C9_VALUE);
  const encLower = enc.replace(/%[0-9A-F]{2}/g, (m) => m.toLowerCase());
  const encPlus = enc.replace(/%20/g, '+');
  c9Check('C9 원문(대조 줄 · 고치기 전에도 초록)', `note ${C9_VALUE} end`, C9_VALUE);
  c9Check('C9 URL 대문자', `GET /x?q=${enc}&z=1`, enc);
  c9Check('C9 URL 소문자', `GET /x?q=${encLower}&z=1`, encLower);
  c9Check('C9 URL +공백', `GET /x?q=${encPlus}&z=1`, encPlus);
  for (let k = 0; k < 3; k += 1) {
    const { full, mid } = b64Mid(C9_VALUE, k);
    c9Check(`C9 base64 자리 ${k}`, `note ${full} end`, mid);
  }
  const ks = [0, 1, 2].find((k) => /[+/]/.test(b64Mid(C9_VALUE, k).mid));
  if (ks === undefined) {
    check('C9 base64 url 안전', false, '시험 값이 url 안전 꼴을 못 만듦 — 시험이 틀렸다');
  } else {
    const { full, mid } = b64Mid(C9_VALUE, ks);
    const safe = (t) => t.replace(/\+/g, '-').replace(/\//g, '_');
    c9Check(`C9 base64 url 안전(자리 ${ks})`, `note ${safe(full)} end`, safe(mid));
  }
}
{ // C9b ① 자르기 경계 — describeError 기본 200자 경계에 인코딩 꼴 앞 6자가 걸치게
  const forms = [
    ['URL', encodeURIComponent(C9_VALUE)],
    ['base64', b64Mid(C9_VALUE, 0).mid],
  ];
  for (const [label, needle] of forms) {
    const msg = `${'x'.repeat(193)} ${needle} end`; // needle 이 194번째 글자부터 → 200자에서 앞 6자만 남는 자리
    const d = L.describeError(new Error(msg));
    const half = needle.slice(0, 6);
    check(`C9b 자르기 경계 ${label} 반쪽 0`, !d.includes(half), `앞 6자 남음=${d.includes(half)}`);
  }
}
{ // C9c ① 색 제어 글자가 값 한가운데
  const cut = Math.floor(C9_VALUE.length / 2);
  const msg = `note ${C9_VALUE.slice(0, cut)}\u001b[31m${C9_VALUE.slice(cut)} end`;
  const d = L.describeError(new Error(msg));
  check('C9c 색 글자 사이 값 이어 붙음 0', !d.includes(C9_VALUE) && d.includes('***'), `이어 붙은 값 남음=${d.includes(C9_VALUE)}`);
}
{ // C10 ② 자식 프로세스 — 결과표(UI_OUT 임시) · 콘솔 줄 대조
  const out = path.join(work, 'c10');
  fs.rmSync(out, { recursive: true, force: true });
  fs.mkdirSync(out, { recursive: true });
  const M = { OLD: `SELFTEST_MARK_${hex()}`, NEW: `SELFTEST_MARK_${hex()}`, TWO_A: `SELFTEST_MARK_${hex()}`, TWO_B: `SELFTEST_MARK_${hex()}` };
  const child = [
    "const L = require(process.env.SELFTEST_LIB);",
    "const r = L.loadResult(); L.bindAutosave(r);",
    "L.rec(r, { id: 'C10-OLD', what: '옛 꼴', got: L.describeError(new Error('note ' + process.env.SELFTEST_M_OLD)), pass: false, kind: 'DEVICE' });",
    "L.rec(r, { id: 'C10-NEW', what: '새 꼴', got: '끊김', pass: false, kind: 'DEVICE', err: new Error('note ' + process.env.SELFTEST_M_NEW) });",
    "const e2 = new TypeError('note ' + process.env.SELFTEST_M_TWO_B);",
    "L.rec(r, { id: 'C10-TWO', what: '새 꼴 둘', got: '끊김', pass: false, kind: 'DEVICE', err: [new Error('note ' + process.env.SELFTEST_M_TWO_A), e2, null] });",
  ].join('\n');
  const env = {
    PATH: process.env.PATH, SystemRoot: process.env.SystemRoot, UI_OUT: out,
    SELFTEST_LIB: path.join(__dirname, 'lib.js'),
    SELFTEST_M_OLD: M.OLD, SELFTEST_M_NEW: M.NEW, SELFTEST_M_TWO_A: M.TWO_A, SELFTEST_M_TWO_B: M.TWO_B,
  };
  const c = spawnSync(process.execPath, ['-e', child], { env, encoding: 'utf8' });
  const lines = (c.stdout || '').split('\n').filter((l) => /^(🟢 PASS|🔴 FAIL|⚫ FAIL|🟡 OBS) /.test(l));
  const consoleIds = lines.map((l) => (l.match(/ (C10-[A-Z]+) /) || [])[1]);
  let rows = [];
  const resFile = path.join(out, 'main', 'ui-k35-result.json');
  if (fs.existsSync(resFile)) rows = JSON.parse(fs.readFileSync(resFile, 'utf8')).rows || [];
  const oldLine = lines.find((l) => l.includes('C10-OLD')) || '';
  check('C10 옛 꼴 = 콘솔에 표지 보임(대조 줄 · 검사가 볼 수 있음)', c.status === 0 && oldLine.includes(M.OLD), `code=${c.status}`);
  const newLine = lines.find((l) => l.includes('C10-NEW')) || '';
  const twoLine = lines.find((l) => l.includes('C10-TWO')) || '';
  const errOf = (id) => String((rows.find((x) => x.id === id) || {}).err || '');
  const sameOrder = lines.length === rows.length && consoleIds.join(',') === rows.map((x) => x.id).join(',');
  const okNew = c.status === 0
    && !newLine.includes(M.NEW) && /예외=Error$/.test(newLine.trimEnd())
    && !twoLine.includes(M.TWO_A) && !twoLine.includes(M.TWO_B) && /예외=Error,TypeError$/.test(twoLine.trimEnd())
    && errOf('C10-NEW').includes(M.NEW) && errOf('C10-TWO').includes(M.TWO_A) && errOf('C10-TWO').includes(M.TWO_B)
    && !(c.stdout || '').includes(M.NEW) && sameOrder;
  check('C10 새 꼴 = 예외 글자 콘솔 0 · 결과표 err 칸에 있음 · 콘솔 줄 수·순서 = 결과표', okNew,
    `code=${c.status} 콘솔 ${lines.length}줄 · 결과표 ${rows.length}줄 · 순서 같음=${sameOrder} · err 칸=${errOf('C10-NEW') !== ''}`);
}
{ // C11 ⑤ 이름에 든 값(내용은 깨끗)
  const root = fresh('c11');
  const raw = E.UI_DB_PASSWORD; // 하위 폴더명에 원문
  const url = encodeURIComponent(E.UI_PW_PARENT); // 파일명에 URL 꼴
  const b64 = b64Mid(E.UI_JWT_SECRET, 1); // zip 항목명에 base64 꼴(자리 1)
  fs.mkdirSync(path.join(root, 'main', raw), { recursive: true });
  fs.writeFileSync(path.join(root, 'main', raw, 'clean.txt'), 'clean');
  fs.writeFileSync(path.join(root, 'main', `u-${url}.txt`), 'clean');
  fs.writeFileSync(path.join(root, 'trace', 'n.zip'), makeZip([
    { name: `resources/x${b64.full}.json`, data: Buffer.from('{"clean":1}') },
  ]));
  const r = run(root);
  const mainGone = !fs.existsSync(path.join(root, 'main'));
  const traceGone = !fs.existsSync(path.join(root, 'trace'));
  check('C11 이름 원문(하위 폴더명) 잡힘 · main 지움 · 콘솔 0', r.code === 1 && /UI_DB_PASSWORD · 꼴=raw · 자리=이름/.test(r.out) && mainGone && !r.out.includes(raw), `code=${r.code}`);
  check('C11 이름 URL 꼴(파일명) 잡힘 · main 지움 · 콘솔 0', r.code === 1 && /UI_PW_PARENT · 꼴=url · 자리=이름/.test(r.out) && mainGone && !r.out.includes(url), `code=${r.code}`);
  check('C11 이름 base64 꼴(zip 항목명) 잡힘 · trace 지움 · 콘솔 0', r.code === 1 && /UI_JWT_SECRET · 꼴=base64 · 자리=이름/.test(r.out) && traceGone && !r.out.includes(b64.mid), `code=${r.code}`);
}

// ── 🔴 7차(10/29 · §0-11-11-5) C9d · C12 · C13a·b · C14a·b·c ─────────────────────────
{ // C9d ⓓ 값 안 공백(1칸)을 줄바꿈+두 칸으로 — 첫 소독은 못 보고, 접기가 값을 다시 만든다
  const msg = `note ${C9_VALUE.replace(/ /g, '\n  ')} end`;
  const d = L.describeError(new Error(msg));
  check('C9d 접은 뒤 원문 0(값 공백 → 줄바꿈+두 칸)', !d.includes(C9_VALUE) && d.includes('***'), `접힌 원문 남음=${d.includes(C9_VALUE)}`);
}
{ // C12 M1 maskProbeLines — 시험이 꼴 글자를 따로 계산(b64Mid · encodeURIComponent)
  const probe = `FAKE_MASK_M1_${crypto.randomBytes(6).toString('hex')}#/${hex()}`;
  const control = `FAKE_MASK_CTRL_${crypto.randomBytes(6).toString('hex')}#/${hex()}`;
  let lines = null;
  let why = '';
  if (typeof L.maskProbeLines !== 'function') {
    why = '함수 없음';
  } else {
    try {
      lines = L.maskProbeLines(probe, control);
    } catch (e) {
      why = `throw ${e && e.name ? e.name : 'Error'}`;
    }
  }
  let ok = Array.isArray(lines);
  if (ok) {
    const enc = encodeURIComponent(probe);
    const encLower = enc.replace(/%[0-9A-F]{2}/g, (m) => m.toLowerCase());
    const expect = new Set([probe, enc, encLower, enc.replace(/%20/g, '+')]);
    const mids = [];
    for (let k = 0; k < 3; k += 1) {
      const { mid } = b64Mid(probe, k);
      mids.push(mid);
      if (mid.length >= 8) {
        expect.add(mid);
        expect.add(mid.replace(/\+/g, '-').replace(/\//g, '_'));
      }
    }
    const ctrlLine = `가림 측정 M1 [대조·안 가림] ${control}`;
    const bare = lines.filter((l) => l.startsWith('가림 측정 M1 [') && l !== ctrlLine);
    const glyphs = bare.map((l) => l.slice(l.indexOf('] ') + 2));
    const ctx = lines.filter((l) => l.startsWith('가림 측정 M1-문맥 ['));
    const oneEach = glyphs.length === expect.size && [...expect].every((s) => glyphs.filter((g) => g === s).length === 1);
    const urlTwo = enc !== encLower && glyphs.includes(enc) && glyphs.includes(encLower);
    const rawLines = lines.filter((l) => l.includes(probe));
    const rawOnly = rawLines.length === 1 && rawLines[0] === `가림 측정 M1 [원문] ${probe}`;
    const ctrlOk = lines.filter((l) => l === ctrlLine).length === 1;
    const ctxOk = ctx.length === 4 && [0, 1, 2].every((k) => ctx.some((l) => l.includes(`[base64-자리${k}-`) && l.includes(mids[k])))
      && ctx.some((l) => l.includes(`GET /x?q=${enc}&n=1`));
    const allKnown = lines.length === bare.length + ctx.length + 1;
    ok = oneEach && urlTwo && rawOnly && ctrlOk && ctxOk && allKnown;
    why = `꼴 ${expect.size}개 · 맨 글자 줄 ${bare.length} · 한 줄씩=${oneEach} · url 둘=${urlTwo} · 원문 [원문]만=${rawOnly} · 대조=${ctrlOk} · 문맥 4=${ctxOk}`;
  }
  check('C12 M1 꼴 글자마다 한 줄 · url 대·소문자 둘 · 원문은 [원문] 줄에만 · 대조 값 그대로 · 문맥 줄 4', ok, why);
}
{ // C13a·C13b M3 — 자식 프로세스(env 를 새로 만든다 · GITHUB_ENV 0 → 파일 쓰기 0)
  const v = `SELFTEST_${crypto.randomBytes(6).toString('hex')}`;
  const child = 'require(process.env.SELFTEST_LIB).mask(process.env.SELFTEST_V);';
  const env = { PATH: process.env.PATH, SystemRoot: process.env.SystemRoot, SELFTEST_LIB: path.join(__dirname, 'lib.js'), SELFTEST_V: v };
  const a = spawnSync(process.execPath, ['-e', child], { env, encoding: 'utf8' });
  const aOut = a.stdout || '';
  const aLines = aOut.split('\n').filter((l) => l !== '');
  check('C13a M3 CI 밖 가림 줄 = 고정 문구 1줄 · 값 0', a.status === 0 && aLines.length === 1 && !aOut.includes(v) && !aOut.includes('::add-mask::'),
    `code=${a.status} 줄=${aLines.length} 값 보임=${aOut.includes(v)}`);
  const b = spawnSync(process.execPath, ['-e', child], { env: { ...env, GITHUB_ACTIONS: 'true' }, encoding: 'utf8' });
  check('C13b M3 CI 안 가림 줄 바이트 그대로(대조 줄 · 고치기 전에도 초록)', b.status === 0 && b.stdout === `::add-mask::${v}\n`, `code=${b.status}`);
}
// C14 — 임시 폴더만(레포 .github/ 쓰기 0). 워크플로 글은 따옴표 문자열(백틱이면 `${{` 가 보간으로 읽힌다).
function c14Dir(name) {
  const d = path.join(work, name);
  fs.rmSync(d, { recursive: true, force: true });
  fs.mkdirSync(d, { recursive: true });
  return d;
}
const hasIntake = typeof L.intakeScan === 'function';
{ // C14a M4 심은 1건씩
  const d = c14Dir('c14a');
  const plantHex = hex();
  const yml = [
    'name: m4-plant',
    'on:',
    '  workflow_dispatch:',
    '    inputs:',
    '# secrets inputs http://plant.invalid',
    'jobs:',
    '  plant:',
    '    runs-on: ubuntu-latest',
    '    steps:',
    '      - run: node scripts/playwright-audit/ci/mask-probe.js',
    '      - run: echo "${{ secrets.FAKE_M4_PLANT_' + plantHex + ' }}"',
    '      - run: echo "UI_BASE=http://plant.invalid"',
  ];
  fs.writeFileSync(path.join(d, 'm4-plant.yml'), yml.join('\n'));
  const want = [
    `m4-plant.yml:${yml.indexOf('    inputs:') + 1}:입력 칸`,
    `m4-plant.yml:${yml.findIndex((l) => l.includes('secrets.FAKE_M4_PLANT_')) + 1}:비밀값`,
    `m4-plant.yml:${yml.findIndex((l) => l.includes('UI_BASE=')) + 1}:주소`,
  ].sort();
  let ok = false;
  let why = '함수 없음';
  if (hasIntake) {
    const r = L.intakeScan(d);
    const got = r.hits.map((h) => `${h.file}:${h.line}:${h.kind}`).sort();
    const out = r.lines.join('\n');
    const noLeak = !out.includes('plant.invalid') && !out.includes('FAKE_M4_PLANT');
    ok = !r.ok && r.targets.length === 1 && got.join('|') === want.join('|') && noLeak
      && r.lines.filter((l) => l.startsWith('⚫ FAIL [장치] M4 실값 유입 — m4-plant.yml:')).length === 3;
    why = `잡힘 ${r.hits.length} · 기대와 같음=${got.join('|') === want.join('|')} · 출력에 심은 글자 0=${noLeak}`;
  }
  check('C14a M4 심은 비밀값·입력 칸·주소 정확히 3 · 미끼 주석 0 · 출력에 심은 글자 0', ok, why);
}
{ // C14b M4 레포 워크플로 지금 그대로
  let ok = false;
  let why = '함수 없음';
  if (hasIntake) {
    const r = L.intakeScan(path.resolve(__dirname, '../../../.github/workflows'));
    ok = r.ok && r.targets.length === 1 && r.targets[0] === 'ui-browser.yml' && r.hits.length === 0
      && r.lines.length === 1 && r.lines[0] === '🟢 PASS [장치] M4 실값 유입 0 — 대상 1개';
    why = `대상=${r.targets.join(',')} · 잡힘 ${r.hits.length}${r.hits.length ? ` (${r.hits.map((h) => `${h.file}:${h.line}:${h.kind}`).join(' ')})` : ''}`;
  }
  check('C14b M4 실물 워크플로 = 대상 1(ui-browser.yml) · 잡힘 0', ok, why);
}
{ // C14c M4 대상 0
  const d = c14Dir('c14c');
  fs.writeFileSync(path.join(d, 'other.yml'), ['name: other', 'on: push', 'jobs:', '  a:', '    runs-on: ubuntu-latest'].join('\n'));
  let ok = false;
  let why = '함수 없음';
  if (hasIntake) {
    const r = L.intakeScan(d);
    ok = !r.ok && r.targets.length === 0 && r.lines.some((l) => l.startsWith('⚫ FAIL [장치] M4 실값 유입 — 대상 0개'));
    why = `ok=${r.ok} · 대상 ${r.targets.length}`;
  }
  check('C14c M4 대상 0 = 빨강(빈 검색은 거짓 통과)', ok, why);
}

const bad = results.filter((x) => !x.ok).length;
console.log(`합계 ${results.length - bad}/${results.length} PASS`);
process.exit(bad === 0 ? 0 : 1);
