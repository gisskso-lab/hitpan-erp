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
// ⬛ 7차 원문: //   C14a M4 심은 워크플로 글(비밀값·입력 칸·주소 각 1 + 미끼 통째 주석) → 잡힘 정확히 3 · 줄 번호 맞음 · 출력에 심은 글자 0
//   C14a M4 심은 워크플로 글(비밀값·입력 칸·주소 각 1 + `$(…)` 주소 1 + 미끼 통째 주석 + 맨 변수 `$이름` 1) → 잡힘 정확히 4 · 줄 번호 맞음 · 출력에 심은 글자 0
//       └ V7-F1(10/29 [3-V]): 「`$` 로 시작하면 건너뜀」이 `$(…)`·`${X:-…}` 까지 놓쳤다 → 맨 변수만 건너뜀. 고치기 전 lib 에 붙이면 C14a 빨강(대조군)
//   C14b M4 레포 .github/workflows 지금 그대로 → 대상 1(ui-browser.yml) · 잡힘 0(「항상 빨강」 아님)
//   C14c M4 장치 경로 없는 워크플로만 → 대상 0 = 빨강
// 🔴 8차(10/29 · 작업지시서 §0-12-6 + PM 조건 ②) — 경우 9개 더함(33 → 42). 외부 차단 처리기의 닫힌 창 처리 거절 판별(lib guardRoute).
//   고치기 전 lib 에 붙이면 C15g1 만 초록(대조 줄) · 나머지 8 빨강(함수 없음 → FAIL 로 세고 다음 경우로).
//   C15a 닫힘·continue → 이행 · continue 1 · 창 상태는 거절된 그때 한 번만 읽음   C15b 닫힘·abort → 이행 · abort 1
//   C15c 충돌(창 닫힘 false) → 같은 객체로 거절 · 셈 0                         C15d 다른 거절(Route is already handled!) → 거절 · 셈 0
//   C15e 글자 변형 셋(다른 API 이름 · 고쳐 쓴 꼴 · 같은 줄 끝 덧붙임) → 셋 다 거절 · 셈 0
//   C15f Error 아님(같은 글자 문자열 · undefined) → 거절 · 셈 0 · 판별 함수가 안 터짐
//   C15h 닫힘 + 여러 줄 꼬리(`\nCall log:` …) → 이행 · continue 1 (PM 조건 ② · 첫 줄만 비교)
//   C15g1 자식 프로세스 · 거절을 받는 곳 없이 → unhandledRejection 1 = 대조 줄(빨강이면 시험이 틀렸다)
//   C15g2 자식 프로세스 · 같은 거절을 guardRoute 로 감쌈 → unhandledRejection 0 · 종료 0
//   거절 글자는 Playwright 문구뿐(런 40818528074 결과표 err 칸의 1.60.0 실물) — 자격증명 모양 0.
// 🔴 9차(10/29 · 작업지시서 §0-13-5 + §0-13-PM 조건 ①) — 경우 6개 더함(42 → 48) · C15e 에 V8-R1 2꼴(개수 그대로).
//   C16 = Playwright 1.60.0 원본 순서(F1~F9)를 옮긴 오프라인 모형(브라우저·네트워크 0) · 창 상태는 모형 메시지가 배달된 결과로만 정한다.
//   C16a 대조 줄 · 옛 흐름(열자마자 화면 안 닫기 · lib 안 씀 · 처리기 = continue 그대로) → 서버 차례 S1·S3·S4 셋 다 받는 곳 없는 거절 1
//   C16b 고침 흐름(lib settlePopup · 팝업 하위 요청 2 날던 중 닫음) → 거절 0 · 녹음 1(hitpanChat) · 알림·적재·닫음 참 · 닫기 전 첫 요청 continue 이행
//   C16c 모형 민감도(C16b 와 같되 서버 순서만 실패 응답 → Close) → 거절 ≥1 이 나야 PASS
//   C16d 8차 전제 반증(옛 흐름 + guardRoute · 창 상태 읽기) → S1 다시 던짐 · S3 다시 던짐(읽기 실패) · S4 삼킴
//   C16e PM 조건 ① open 이 던짐(동기·비동기) → 같은 객체로 거절 · 'popup' 처리기 0 · 받는 곳 없는 거절 0 · 상한 안 기다림
//   C16f PM 조건 ① 안 열림(window.open 이 null) → opened 거짓 · 셋 다 거짓 · 처리기 0 · 거절 0 · 상한 안 기다림 · 시계 남김 0
//   고치기 전 lib(1a142dd3) = C16a 만 초록 · 8차 lib = C16a·C16d 초록.
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
    // V7-F1 대조 2줄 — `$(` 는 실행 중에 이어 붙인다(원문에 그 꼴 0). 빨강 기대 1 · 건너뜀 기대 1(맨 변수)
    '      - run: echo "ALT_BASE=' + '$' + '(cat FAKE_M4_SUB_' + plantHex + '.invalid)"',
    '      - run: echo "VAR_BASE=$FAKE_M4_VAR_' + plantHex + '"',
  ];
  fs.writeFileSync(path.join(d, 'm4-plant.yml'), yml.join('\n'));
  const want = [
    `m4-plant.yml:${yml.indexOf('    inputs:') + 1}:입력 칸`,
    `m4-plant.yml:${yml.findIndex((l) => l.includes('secrets.FAKE_M4_PLANT_')) + 1}:비밀값`,
    `m4-plant.yml:${yml.findIndex((l) => l.includes('UI_BASE=')) + 1}:주소`,
    `m4-plant.yml:${yml.findIndex((l) => l.includes('ALT_BASE=')) + 1}:주소`,
  ].sort();
  let ok = false;
  let why = '함수 없음';
  if (hasIntake) {
    const r = L.intakeScan(d);
    const got = r.hits.map((h) => `${h.file}:${h.line}:${h.kind}`).sort();
    const out = r.lines.join('\n');
    // ⬛ 7차: const noLeak = !out.includes('plant.invalid') && !out.includes('FAKE_M4_PLANT');
    const noLeak = !out.includes('plant.invalid') && !out.includes('FAKE_M4_PLANT')
      && !out.includes('FAKE_M4_SUB') && !out.includes('FAKE_M4_VAR') && !out.includes(plantHex);
    // ⬛ 7차: ok = … && r.lines.filter(…).length === 3;
    ok = !r.ok && r.targets.length === 1 && got.join('|') === want.join('|') && noLeak
      && r.lines.filter((l) => l.startsWith('⚫ FAIL [장치] M4 실값 유입 — m4-plant.yml:')).length === 4;
    why = `잡힘 ${r.hits.length} · 기대와 같음=${got.join('|') === want.join('|')} · 출력에 심은 글자 0=${noLeak}`;
  }
  // ⬛ 7차: check('C14a M4 심은 비밀값·입력 칸·주소 정확히 3 · 미끼 주석 0 · 출력에 심은 글자 0', ok, why);
  check('C14a M4 심은 비밀값·입력 칸·주소(+`$(…)`) 정확히 4 · 미끼 주석·맨 변수 0 · 출력에 심은 글자 0', ok, why);
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

// ── 🔴 8차(10/29 · §0-12-6 + PM 조건 ②) C15 — 외부 차단 처리기 닫힌 창 처리 거절 ─────────────────
//   Promise 를 기다려야 하므로 합계 줄은 C15 뒤(아래 async 묶음 끝)로 옮긴다.
// ⬛ [낡은 줄 · 6~7차] const bad = results.filter((x) => !x.ok).length;
// ⬛ [낡은 줄 · 6~7차] console.log(`합계 ${results.length - bad}/${results.length} PASS`);
// ⬛ [낡은 줄 · 6~7차] process.exit(bad === 0 ? 0 : 1);
const C15_CLOSED_C = 'route.continue: Target page, context or browser has been closed';
const C15_CLOSED_A = 'route.abort: Target page, context or browser has been closed';
const hasGuard = typeof L.guardRoute === 'function' && typeof L.isClosedRouteReject === 'function';
async function c15Settle(p) {
  try {
    return { ok: true, v: await p };
  } catch (e) {
    return { ok: false, e };
  }
}
// 거절을 나중에 일으켜 「창 상태는 거절된 그때 한 번만 읽는다(결정 때 0)」 를 같이 잰다.
async function c15Guard(reason, closed, kind = 'continue') {
  const tally = { continue: 0, abort: 0 };
  let reads = 0;
  let reject = null;
  const p = new Promise((_, rej) => { reject = rej; });
  const g = L.guardRoute(p, () => { reads += 1; return closed; }, tally, kind);
  const readsBefore = reads;
  reject(reason);
  const s = await c15Settle(g);
  return { s, tally, reads, readsBefore };
}
const c15Zero = (t) => t.continue === 0 && t.abort === 0;
const c15Why = (x) => `이행=${x.s.ok} · continue ${x.tally.continue} · abort ${x.tally.abort} · 읽음 ${x.readsBefore}→${x.reads}`;
function c15Child(useGuard) {
  // Playwright 1.60.0 RouteHandler 흉내 — 처리기가 돌려준 Promise 를 기다렸다가 거절이면 고쳐 쓴 글로 다시 던진다(받는 곳 0).
  const child = [
    'let n = 0;',
    "process.on('unhandledRejection', () => { n += 1; });",
    'const err = new Error(process.env.SELFTEST_MSG);',
    "async function handle(p) { try { await p; } catch (e) { throw new Error('\"' + e.message + '\" while running route callback.'); } }",
    'const tally = { continue: 0, abort: 0 };',
    useGuard
      ? "handle(require(process.env.SELFTEST_LIB).guardRoute(Promise.reject(err), () => true, tally, 'continue'));"
      : 'handle(Promise.reject(err));',
    "setTimeout(() => { console.log('UR=' + n + ' T=' + tally.continue); }, 50);",
  ].join('\n');
  const env = { PATH: process.env.PATH, SystemRoot: process.env.SystemRoot, SELFTEST_LIB: path.join(__dirname, 'lib.js'), SELFTEST_MSG: C15_CLOSED_C };
  const c = spawnSync(process.execPath, ['-e', child], { env, encoding: 'utf8', timeout: 20000 });
  return { code: c.status, out: (c.stdout || '').trim() };
}

// ── 🔴 9차(10/29 · §0-13-5 + PM 조건 ①) C16 — Playwright 1.60.0 순서 모형 ─────────────────────────────
//   원본(읽기만) = scripts/playwright-audit/node_modules/playwright-core/lib/coreBundle.js (package.json 1.60.0 · CI 와 같은 판).
//   아래 각 부분 위의 `:줄` = 그 원본의 줄. 레포 뿌리 node_modules(1.62.1)는 근거 아님. 실물 브라우저가 아니다(한계 = 작업지시서 §0-13-5).
const EventEmitter = require('events');
const hasSettle = typeof L.settlePopup === 'function';
const c16Tick = () => new Promise((ok) => setTimeout(ok, 30));
let c16UR = 0; // 받는 곳 없는 거절 셈(C16 들어갈 때 그물을 건다)

// F1 — :3308~3354 LongStandingScope · :3334~3336 safeRace · :3337~3353 _race
class C16Scope {
  constructor() {
    this.closed = false;
    this.waiters = new Set();
  }
  // :3319~3324 close — 기다리던 경주 전부 풀림
  close() {
    this.closed = true;
    for (const w of this.waiters) w();
  }
  // :3342·3343 이미 닫힌 범위면 곧바로 · :3347 safe → defaultValue(undefined) 로 **이행**(거절 아님)
  safeRace(p) {
    let done = null;
    const term = new Promise((ok) => { done = ok; });
    if (this.closed) done();
    this.waiters.add(done);
    return Promise.race([term.then(() => undefined), p]).finally(() => this.waiters.delete(done));
  }
}

// flow 'old' = 화면 안에서 열자마자 닫음(1~8차 H-T1 꼴) · 'new' = 우리 쪽이 기다렸다 닫음(9차)
// closeOrder = 알려진 창이 닫힐 때 서버 차례('close-first' 원본 · 'fail-first' 민감도용) · unknownOrder = 알리기 전 닫힘 차례(S1·S3·S4)
// engine 'webkit'(기본 · F4 늦은 알림) · 'chromium'(:35886~35889 세션 준비 뒤 곧바로 reportAsNew — 첫 이동을 안 기다림 ·
//   모형은 evaluate 응답보다 먼저 알림 = PM 조건 ①「open() 전에 받을 준비」 를 재는 차례 · 실물 차례 ⚠️미측정)
function c16World({ flow, closeOrder = 'close-first', unknownOrder = 'S3', handler, engine = 'webkit' }) {
  const w = { log: [], readFail: 0, tally: { continue: 0, abort: 0 } };
  // F6 — :58937·58938 in-process 연결: 메시지 하나마다 setImmediate(차례 그대로)
  const toClient = (fn) => setImmediate(fn);
  const toServer = (fn) => setImmediate(fn);
  const calls = new Map();
  let callNo = 0;
  const command = (name, arg) => new Promise((ok, no) => {
    callNo += 1;
    const id = callNo;
    calls.set(id, { ok, no });
    toServer(() => srv.onCommand(id, name, arg));
  });
  const reply = (id, err, value) => toClient(() => {
    const c = calls.get(id);
    calls.delete(id);
    if (err) c.no(new Error(err)); else c.ok(value);
  });

  // ── 손님 쪽 ──
  class C16Page extends EventEmitter {
    constructor(id) {
      super();
      this.id = id;
      this.closedNow = false;
      this.closeCalled = false;
      this.loaded = false;
      this.scope = new C16Scope();
      this.once('close', () => this.scope.close()); // F7 :56438 Close → 범위 close
    }
    isClosed() { return this.closedNow; }
    waitForLoadState(state) {
      const closedErr = () => new Error('page.waitForLoadState: Target page, context or browser has been closed');
      if (state !== 'domcontentloaded') return Promise.reject(new Error(`모형: 적재 상태 ${state} 모름`));
      if (this.loaded) return Promise.resolve();
      if (this.closedNow) return Promise.reject(closedErr());
      return new Promise((ok, no) => {
        const onLoad = () => { this.off('close', onClose); ok(); };
        const onClose = () => { this.off('domcontentloaded', onLoad); no(closedErr()); };
        this.once('domcontentloaded', onLoad);
        this.once('close', onClose);
      });
    }
    // F8 :56850~56853 page.close() — 부르면 _closeWasCalled → 이후 그 창의 경로 사건은 처리기를 안 부름(:57365)
    close() {
      this.closeCalled = true;
      w.log.push('close-called');
      return command('close', this.id);
    }
  }
  const opener = new C16Page('opener');
  const frames = new Map(); // F3 :50304~50309 — 창이 알려지기 전에도 프레임은 문맥 아래 먼저 붙는다
  const pages = new Map();
  const frameOf = (fid) => {
    if (!frames.has(fid)) frames.set(fid, { pageObj: null });
    return frames.get(fid);
  };
  const client = {
    // F3 :56412 창 생성(알림) 때 mainFrame._page = this · 문맥 Page 사건 → 연 창에 popup
    onPage(pid, fid) {
      const pg = new C16Page(pid);
      pages.set(pid, pg);
      frameOf(fid).pageObj = pg;
      w.log.push('announced');
      opener.emit('popup', pg);
    },
    // F7 :56514~56519 손님 _onClose — 그 매크로태스크에서 _closed=true → Close 사건(→ 범위 close)
    onClose(pid) {
      const pg = pages.get(pid);
      pg.closedNow = true;
      w.log.push('close-delivered');
      pg.emit('close', pg);
    },
    onDcl(pid) {
      const pg = pages.get(pid);
      pg.loaded = true;
      pg.emit('domcontentloaded');
    },
    onRoute(rid, fid) {
      const frame = frameOf(fid);
      const req = {
        id: rid,
        // F3 :54789~54802 Request.frame() — 창이 손님에 아직 안 알려졌으면 던진다
        frame() {
          if (!frame.pageObj) throw new Error('Frame for this navigation request is not available, because the request was issued before the frame is created.');
          return { page: () => frame.pageObj };
        },
      };
      const route = {
        request: () => req,
        // F2 :54969~54977 continue → :54987~ _innerContinue → :54876~54878 _raceWithTargetClose → :54860~54862 _targetClosedScope
        //   부르는 **그때** 범위를 잡는다 — 손님 쪽 창 객체가 없으면 새 범위(영영 안 닫힘) · 서버 실패 응답이면 그대로 거절
        continue() {
          const scope = frame.pageObj ? frame.pageObj.scope : new C16Scope();
          const ch = command('continue', rid);
          ch.then(() => w.log.push(`ok:${rid}`), () => w.log.push(`fail:${rid}`));
          return scope.safeRace(ch);
        },
      };
      // F8 :57360~57366 문맥 _onRoute — 우리 쪽 close() 를 부른 창이면 처리기를 안 부르고 건너뜀
      if (frame.pageObj && frame.pageObj.closeCalled) {
        w.log.push(`skip:${rid}`);
        return;
      }
      // F9 :55307~55322 RouteHandler — 처리기 거절은 고쳐 다시 던져지고 받는 곳 0(→ 받는 곳 없는 거절)
      (async () => {
        try {
          await handler(route, w);
        } catch (e) {
          throw new Error(`"${e && e.message}" while running route callback.`);
        }
      })();
    },
  };

  // ── 서버 쪽 ──
  let nextReq = 0;
  const srv = {
    popup: null,
    onCommand(id, name, arg) {
      if (name === 'open') return this.open(id);
      if (name === 'continue') return this.onContinue(id, arg);
      if (name === 'close') return this.close(id);
      return reply(id, `모형: 모르는 명령 ${name}`);
    },
    route(p, kind) {
      nextReq += 1;
      const rid = `${kind}${nextReq}`;
      toClient(() => client.onRoute(rid, p.fid));
    },
    // 화면 안 window.open 1회 — 첫 문서 요청을 가로채 손님에 보내고 evaluate 응답(녹음기 목록)
    open(id) {
      const p = { id: 'popup1', fid: 'f1', open: true, announced: false, pending: new Map() };
      this.popup = p;
      if (engine === 'chromium') { // :35886~35889 — 첫 이동 전에 알림
        p.announced = true;
        toClient(() => client.onPage(p.id, p.fid));
      }
      this.route(p, 'doc');
      reply(id, null, [{ name: 'hitpanChat' }]);
    },
    onContinue(id, rid) {
      const p = this.popup;
      if (!p.open) return reply(id, C15_CLOSED_C); // 닫힌 뒤 온 명령
      p.pending.set(id, rid);
      if (rid.startsWith('doc')) {
        if (flow === 'old') return this.closeTarget(); // 1~8차: 열자마자 닫음 → 첫 이동 확정 **전에** 닫힘(F4)
        setImmediate(() => this.commitDoc(p, id)); // 브라우저가 첫 문서 요청을 마침
      }
      return undefined; // 하위 요청 = 브라우저 진행 중(닫힐 때까지 응답 없음) = 「닫는 때 날던 중」
    },
    // F4 :45261~45273 WebKit 팝업은 **첫 이동 확정 뒤에야** 손님에 알린다(reportAsNew) → 하위 요청 2 → domcontentloaded
    commitDoc(p, id) {
      if (!p.open) return;
      p.pending.delete(id);
      reply(id, null);
      if (!p.announced) {
        p.announced = true;
        toClient(() => client.onPage(p.id, p.fid));
      }
      this.route(p, 'sub');
      this.route(p, 'sub');
      toClient(() => client.onDcl(p.id));
    },
    close(id) {
      this.closeTarget();
      reply(id, null);
    },
    // F5 :45199~45211 didClose · :44313 WKSession.dispose · :49016~49018 PageDispatcher
    closeTarget() {
      const p = this.popup;
      if (!p.open) return;
      p.open = false;
      const pend = [...p.pending.keys()];
      p.pending.clear();
      const fail = () => { for (const id of pend) reply(id, C15_CLOSED_C); };
      const closeMsg = () => toClient(() => client.onClose(p.id));
      if (p.announced) {
        if (closeOrder === 'fail-first') { // C16c 민감도 — 순서만 뒤집음(원본 아님)
          fail();
          closeMsg();
          return;
        }
        Promise.resolve().then(fail); // :44313 dispose — 대기 명령 거절 → 실패 응답은 microtask 뒤
        closeMsg(); // :45211 _page._didClose() 동기 → :49016~49018 Close 동기 송신(실패 응답보다 먼저)
        return;
      }
      // F4 :45210·:45266~45273 · :19780~19803 — 알리기 전 닫힘: reportAsNew(오류) → 창 알림 + 이어 Close(:19800·19801).
      //   실패 응답과의 차례는 서버 microtask 깊이에 달림(실물 ⚠️미측정) → 셋: S1 알림·실패·Close / S3 실패·알림·Close / S4 알림·Close·실패
      const announce = () => {
        p.announced = true;
        toClient(() => client.onPage(p.id, p.fid));
      };
      const seq = { S1: [announce, fail, closeMsg], S3: [fail, announce, closeMsg], S4: [announce, closeMsg, fail] }[unknownOrder];
      Promise.resolve().then(() => { for (const f of seq) f(); });
    },
  };
  w.opener = opener;
  w.command = command;
  return w;
}
// 처리기 — 1~7차 꼴(continue 그대로)
const c16Plain = async (route) => { await route.continue(); };
// 처리기 — 8차 꼴(guardRoute + 거절된 그때 창 상태 읽기 · device-check.js routePageClosed 와 같은 읽기)
const c16Guarded = (route, w) => {
  const read = () => {
    try {
      return route.request().frame().page().isClosed();
    } catch (e) {
      w.readFail += 1;
      w.log.push(`read-fail:${L.errorName(e)}`);
      return false;
    }
  };
  return L.guardRoute(route.continue(), read, w.tally, 'continue');
};
async function c16Old(order, handler) {
  const w = c16World({ flow: 'old', unknownOrder: order, handler });
  const ur0 = c16UR;
  await w.command('open');
  await c16Tick();
  w.ur = c16UR - ur0;
  return w;
}
async function c16New(closeOrder, engine = 'webkit') {
  const w = c16World({ flow: 'new', closeOrder, handler: c16Plain, engine });
  const ur0 = c16UR;
  try {
    w.s = await L.settlePopup(w.opener, async () => ({ value: await w.command('open'), opened: true }), 2000);
  } catch (e) {
    w.err = e;
  }
  await c16Tick();
  w.ur = c16UR - ur0;
  w.listeners = w.opener.listenerCount('popup');
  return w;
}
const c16Timers = () => (typeof process.getActiveResourcesInfo === 'function' ? process.getActiveResourcesInfo().filter((x) => x === 'Timeout').length : null);

(async () => {
  { // C15a 닫힘·continue
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const x = await c15Guard(new Error(C15_CLOSED_C), true, 'continue');
      ok = x.s.ok && x.tally.continue === 1 && x.tally.abort === 0 && x.readsBefore === 0 && x.reads === 1;
      why = c15Why(x);
    }
    check('C15a 닫힘·continue → 이행 · continue 1 · 창 상태는 거절 때 한 번', ok, why);
  }
  { // C15b 닫힘·abort
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const x = await c15Guard(new Error(C15_CLOSED_A), true, 'abort');
      ok = x.s.ok && x.tally.abort === 1 && x.tally.continue === 0 && x.readsBefore === 0 && x.reads === 1;
      why = c15Why(x);
    }
    check('C15b 닫힘·abort → 이행 · abort 1', ok, why);
  }
  { // C15c 충돌 — 같은 글자 · 창 닫힘 false
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const err = new Error(C15_CLOSED_C);
      const x = await c15Guard(err, false);
      ok = !x.s.ok && x.s.e === err && c15Zero(x.tally);
      why = `${c15Why(x)} · 같은 객체=${x.s.e === err}`;
    }
    check('C15c 충돌(창 닫힘 false) → 같은 객체로 거절 · 셈 0', ok, why);
  }
  { // C15d 다른 거절
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const err = new Error('route.continue: Route is already handled!');
      const x = await c15Guard(err, true);
      ok = !x.s.ok && x.s.e === err && c15Zero(x.tally);
      why = `${c15Why(x)} · 같은 객체=${x.s.e === err}`;
    }
    check('C15d 다른 거절 → 같은 객체로 거절 · 셈 0', ok, why);
  }
  { // C15e 글자 변형 셋(앞뒤 고정 확인)
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const errs = [
        new Error('route.fulfill: Target page, context or browser has been closed'),
        new Error(`"${C15_CLOSED_C}" while running route callback.`),
        new Error(`${C15_CLOSED_C}!`),
        // 🔴 9차 V8-R1(§0-13-5 · [4] V8-4b) — 앞만 다른 꼴 · 대소문자 바꾼 꼴(같은 경우 안 · 셈 그대로) → `^` 뺌·`/i` 변이를 잡는다
        new Error(`x${C15_CLOSED_C}`),
        new Error('Route.continue: Target page, context or browser has been closed'),
      ];
      const xs = [];
      for (const err of errs) xs.push(await c15Guard(err, true));
      ok = xs.every((x, k) => !x.s.ok && x.s.e === errs[k] && c15Zero(x.tally));
      why = xs.map((x, k) => `${k + 1}:거절=${!x.s.ok}·셈0=${c15Zero(x.tally)}`).join(' ');
    }
    // ⬛ [낡은 줄 · 8차] check('C15e 글자 변형 셋(다른 API · 고쳐 쓴 꼴 · 끝 덧붙임) → 셋 다 거절 · 셈 0', ok, why);
    check('C15e 글자 변형 다섯(다른 API · 고쳐 쓴 꼴 · 끝 덧붙임 · 앞 덧붙임 · 대소문자) → 다섯 다 거절 · 셈 0', ok, why);
  }
  { // C15f Error 아님
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const vals = [C15_CLOSED_C, undefined];
      const xs = [];
      for (const v of vals) xs.push(await c15Guard(v, true));
      let pureOk = false;
      why = '판별 함수가 Error 아닌 값을 참으로 봄';
      try {
        pureOk = L.isClosedRouteReject(C15_CLOSED_C, true) === false && L.isClosedRouteReject(undefined, true) === false
          && L.isClosedRouteReject(null, true) === false;
      } catch (e) {
        why = `판별 함수 예외 ${L.errorName(e)}`;
      }
      ok = pureOk && xs.every((x, k) => !x.s.ok && x.s.e === vals[k] && c15Zero(x.tally));
      if (pureOk) why = xs.map((x, k) => `${k + 1}:거절=${!x.s.ok}·셈0=${c15Zero(x.tally)}`).join(' ');
    }
    check('C15f Error 아님(문자열 · undefined) → 거절 · 셈 0 · 판별 함수 안 터짐', ok, why);
  }
  { // C15h 닫힘 + 여러 줄 꼬리(PM 조건 ②)
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const x = await c15Guard(new Error(`${C15_CLOSED_C}\nCall log:\n  - route handler running`), true, 'continue');
      ok = x.s.ok && x.tally.continue === 1 && x.tally.abort === 0;
      why = c15Why(x);
    }
    check('C15h 닫힘 + 여러 줄 꼬리 → 이행 · continue 1(첫 줄만 비교)', ok, why);
  }
  { // C15g1 기제 재현(대조 줄 · lib 안 씀)
    const c = c15Child(false);
    check('C15g1 받는 곳 없는 닫힘 거절 = unhandledRejection 1(대조 줄 · 고치기 전에도 초록)', c.code === 0 && c.out === 'UR=1 T=0', `code=${c.code} · ${c.out}`);
  }
  { // C15g2 기제 고침
    const c = hasGuard ? c15Child(true) : null;
    check('C15g2 guardRoute 로 감쌈 = unhandledRejection 0 · 종료 0', !!c && c.code === 0 && c.out === 'UR=0 T=1', c ? `code=${c.code} · ${c.out}` : '함수 없음');
  }
  // ── 🔴 9차 C16 — 받는 곳 없는 거절 그물(셈만 · 이 뒤 끝까지 둔다) ──
  process.on('unhandledRejection', () => { c16UR += 1; });
  { // C16a 대조 줄 · 옛 흐름(lib 안 씀)
    const ws = [];
    for (const o of ['S1', 'S3', 'S4']) ws.push(await c16Old(o, c16Plain));
    const ok = ws.every((x) => x.ur === 1);
    check('C16a 옛 흐름(열자마자 닫기 · continue 그대로) = 받는 곳 없는 거절 1(S1·S3·S4 · 대조 줄 · 고치기 전에도 초록)', ok, ws.map((x, k) => `${['S1', 'S3', 'S4'][k]}:UR=${x.ur}`).join(' '));
  }
  { // C16b 고침 흐름
    let ok = false;
    let why = '함수 없음';
    if (hasSettle) {
      // WebKit(지시서 모형) + Chromium 차례(알림이 evaluate 응답보다 먼저 · 「open() 전에 받을 준비」 를 잰다) — 둘 다 맞아야 PASS
      const parts = [];
      for (const engine of ['webkit', 'chromium']) {
        const w = await c16New('close-first', engine);
        const s = w.s || {};
        const iOk = w.log.indexOf('ok:doc1');
        const iClose = w.log.indexOf('close-called');
        const subInFlight = w.log.includes('fail:sub2') && w.log.includes('fail:sub3'); // 닫을 때 날던 중이었다(서버 실패 응답이 왔다)
        const one = !w.err && w.ur === 0 && Array.isArray(s.value) && s.value.length === 1 && s.value[0].name === 'hitpanChat'
          && s.opened === true && s.reported === true && s.loaded === true && s.closed === true
          && iOk >= 0 && iClose > iOk && subInFlight && w.listeners === 0;
        parts.push({ one, txt: `${engine}: 예외=${w.err ? L.errorName(w.err) : '없음'} · UR=${w.ur} · 알림·적재·닫음=${s.reported}·${s.loaded}·${s.closed} · 첫 요청 이행→닫기=${iOk}→${iClose} · 하위 2 날던 중=${subInFlight} · 처리기=${w.listeners}` });
      }
      ok = parts.every((x) => x.one);
      why = parts.map((x) => x.txt).join(' | ');
    }
    check('C16b 고침 흐름(settlePopup) → 거절 0 · 녹음 1 · 알림·적재·닫음 참 · 닫기 전 첫 요청 continue 이행', ok, why);
  }
  { // C16c 모형 민감도 — 서버 순서만 뒤집음
    let ok = false;
    let why = '함수 없음';
    if (hasSettle) {
      const w = await c16New('fail-first');
      ok = w.ur >= 1;
      why = `UR=${w.ur}`;
    }
    check('C16c 모형 민감도(실패 응답 → Close) → 거절 ≥1(결과가 서버 순서에 따라 바뀐다)', ok, why);
  }
  { // C16d 8차 전제 반증 — 옛 흐름 + guardRoute
    let ok = false;
    let why = '함수 없음';
    if (hasGuard) {
      const s1 = await c16Old('S1', c16Guarded);
      const s3 = await c16Old('S3', c16Guarded);
      const s4 = await c16Old('S4', c16Guarded);
      ok = s1.ur === 1 && s1.tally.continue === 0 && s1.readFail === 0
        && s3.ur === 1 && s3.tally.continue === 0 && s3.readFail === 1
        && s4.ur === 0 && s4.tally.continue === 1 && s4.readFail === 0;
      why = [['S1', s1], ['S3', s3], ['S4', s4]].map(([n, x]) => `${n}:UR=${x.ur}·삼킴=${x.tally.continue}·읽기실패=${x.readFail}`).join(' ');
    }
    check('C16d 8차 전제 반증 → S1 다시 던짐 · S3 다시 던짐(읽기 실패) · S4 삼킴(8차는 S4 만 덮는다)', ok, why);
  }
  { // C16e PM 조건 ① — open 이 던짐(동기 · 비동기)
    let ok = false;
    let why = '함수 없음';
    if (hasSettle) {
      const forms = [(boom) => () => { throw boom; }, (boom) => () => Promise.reject(boom)];
      const xs = [];
      for (const make of forms) {
        const w = c16World({ flow: 'new', handler: c16Plain });
        const boom = new Error('모형: open 던짐');
        const ur0 = c16UR;
        const t0 = Date.now();
        let got = null;
        try {
          await L.settlePopup(w.opener, make(boom), 5000);
        } catch (e) {
          got = e;
        }
        const ms = Date.now() - t0;
        await c16Tick();
        xs.push({ same: got === boom, listeners: w.opener.listenerCount('popup'), ur: c16UR - ur0, ms });
      }
      ok = xs.every((x) => x.same && x.listeners === 0 && x.ur === 0 && x.ms < 1000);
      why = xs.map((x, k) => `${k ? '비동기' : '동기'}:같은 객체=${x.same}·처리기=${x.listeners}·UR=${x.ur}·${x.ms < 1000 ? '안 기다림' : `${x.ms}ms 기다림`}`).join(' ');
    }
    check('C16e open 이 던짐(동기·비동기) → 같은 객체로 거절 · popup 처리기 0 · 받는 곳 없는 거절 0 · 상한 안 기다림', ok, why);
  }
  { // C16f PM 조건 ① — 안 열림(window.open 이 null)
    let ok = false;
    let why = '함수 없음';
    if (hasSettle) {
      const w = c16World({ flow: 'new', handler: c16Plain });
      const ur0 = c16UR;
      const timers0 = c16Timers();
      const t0 = Date.now();
      let s = null;
      let err = null;
      try {
        s = await L.settlePopup(w.opener, async () => ({ value: [{ name: 'hitpanChat' }], opened: false }), 5000);
      } catch (e) {
        err = e;
      }
      const ms = Date.now() - t0;
      const timers1 = c16Timers();
      const listeners = w.opener.listenerCount('popup');
      w.opener.emit('popup', {}); // 늦게 온 popup — 거둔 뒤라 아무 일 없어야 한다
      await c16Tick();
      const ur = c16UR - ur0;
      const timersOk = timers0 === null || timers1 === timers0;
      ok = !err && !!s && s.opened === false && s.reported === false && s.loaded === false && s.closed === false
        && Array.isArray(s.value) && listeners === 0 && ur === 0 && ms < 1000 && timersOk;
      why = `예외=${err ? L.errorName(err) : '없음'} · opened=${s && s.opened} · 셋=${s && s.reported}·${s && s.loaded}·${s && s.closed} · 처리기=${listeners} · UR=${ur} · ${ms < 1000 ? '안 기다림' : `${ms}ms`} · 시계 ${timers0 === null ? '못 셈' : `${timers0}→${timers1}`}`;
    }
    check('C16f 안 열림(window.open null) → opened 거짓 · 셋 다 거짓 · 처리기 0 · 거절 0 · 상한 안 기다림 · 시계 남김 0', ok, why);
  }
  const bad = results.filter((x) => !x.ok).length;
  console.log(`합계 ${results.length - bad}/${results.length} PASS`);
  process.exit(bad === 0 ? 0 : 1);
})().catch((e) => {
  console.log(`FAIL C15 시험 자체 예외 — ${L.errorName(e)}`);
  process.exit(1);
});
