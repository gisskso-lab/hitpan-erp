// G1-3 갈래 H — S9 판정 (작업지시서 §3-2 S9 · §4-2 · §4-3 ⓔ)
//
// 빨강 조건
//   ① 결과표 없음/깨짐 → ⚫ [장치]
//   ② 돈 시험(kind TEST) 0건 → 🔴 (db-gate 「SKIP 0」 과 같은 뜻)
//   ③ FAIL 1건+ → TEST 면 🔴 [시험] · DEVICE 뿐이면 ⚫ [장치]
//   ④ plant.flags 가 있는 측정 커밋 → 결과와 무관하게 표지만 크게 찍는다(머지 금지 표시)
// 외부 요청: external_blocked ≥ 1 은 🟡 표시만(§3-4 · 기준은 첫 런 실측 뒤 PM)
//
// 🔴 2차(10/29 · 작업지시서 §0-9 항목 5 · 검증팀장 R-3·R-2b) 엄격화
//   ⑤ 줄 꼴 이상 = ⚫ [장치] 빨강 — kind 없음 · 모르는 kind · 소문자(`test`) · `pass` 가 boolean 이 아님(`"false"`·1 등).
//      PASS 는 `pass === true` 만. (종전: TEST/DEVICE 가 아닌 줄은 어느 쪽에도 안 세어져 실패가 사라졌다 ·
//      `pass` 를 truthy 로 봐서 글자 "false" 가 통과였다.)
//   ⑥ K 본문(`k35-*.js`, 이 폴더)이 하나라도 있으면 **G 로 시작하는 시험**(id 또는 g 칸 · scope H 제외)이
//      1건 이상이어야 한다 — H 자체 점검 단언(H-T1·H-T2)이 「돈 시험 0건」 규칙을 채우지 못하게.
//
// 🟡 4차(10/29 · 대표 결재 「약관 결함은 별건 작업 · 장치는 분리」) — kind `OBS`(제품 관찰)
//   ⑦ OBS 는 **허용 목록(OBS_ALLOWED) 안의 id 만** 쓸 수 있다. 목록 밖 id 의 OBS · 소문자 `obs` · 모르는 kind 는
//      종전대로 ⑤ 줄 꼴 이상 ⚫ [장치] 빨강. OBS 는 PASS/FAIL 수 · 「돈 시험」 수에 안 세고 `🟡 OBS` 로 따로 찍는다.
//      OBS 줄의 pass 는 참거짓 꼴만 본다(판정에 안 씀 · device-check 는 「원래 게이트였다면 빨강」 = false 로 적는다).
//
// 🔴 F-PR(11/6 · 작업지시서 20261106작 F-PR §4 C1·C2·C4 · 대표 결재 D3·D8)
//   ⑧ K 본문 이름 — S8 실행기(run-k-bodies.js)와 이 판정기가 **같은 함수**(classifyKNames)로 고른다(N1).
//      K 본문 머리를 달았는데 엄격한 이름 규칙 밖이면 「이름 꼴 이상」 ⚫ [장치] 빨강(실행기도 아무것도 안 돌리고 실패).
//   ⑨ H 기대 키 틀(상신-L14) — 키 = 범위·이름표·G·경로·브라우저. 누락 · 중복 · 모르는 키 · 갈래 어긋남 = ⚫ [장치] 빨강.
//      G 기대 키 자리(EXPECT_G)는 비워 둔다 — K 본문이 있는데 비어 있으면 빨강(G 키는 K 연결 PR 이 채운다 · D3).
//   ⑩ 좋은 줄 · 시드 줄 · 외부 호스트 줄도 소독해서 찍는다(N3 경로 3·5·6).
//   판정 순서 = [시험] 실패 → 줄 꼴 이상 → 이름 꼴 이상 · 키 틀 → 돈 시험 0 → K·G 0 → [장치] 실패.
//
// 🔴 K 연결(11/6 · 작업지시서 20261106작 #691 재기반 §4-2 · PM 결재 A2 · C2) — G 기대 키 = 필수 목록
//   ⑪ G 기대 키 16(G 14 · N4 2)을 채우고 필수 집합에 넣는다 — K 본문 수와 무관. 빠지면 누락 빨강.
//      🔴 2차(A7) — G4 토스트 길 2 추가 → 기대 키 18(G 16 · N4 2).
//   ⑭ 2차(PM 결정 4V-1 ④) — N4 줄 거짓 = 끝 줄 「누출 의심 · 재실행 금지」(빨강 · [장치] 실패보다 먼저 분류 · kind 는 DEVICE 그대로).
//   ⑫ G 키 줄의 통과는 TEST 줄로만(N4 줄만 DEVICE) — DEVICE 통과 줄로 채우면 「G 통과 꼴 이상」 키 틀 빨강.
//   ⑬ K 본문 0 인데 G 기대 키가 있다 = 키 틀 빨강(이유 줄 「K 본문 0 · G 기대 n」).
//   중복(상한 1) · 목록 밖 비H 키(모르는 키) · G 빈 자리 검사는 그대로. 빨강 순서 그대로(G 줄은 키 틀 안).
//   (⑪ ⑫ ⑬ = 작업지시서 §4-2 ① ② ⑤ · 아래 줄 주석의 「K 연결 ①②⑤」 와 같은 것)
//   되돌릴 때: K 본문만 지우면 ⑬ 때문에 대상 PR 이 전부 빨강 — G 기대 키도 같이 비운다(작업지시서 §11).
'use strict';

const fs = require('fs');
const L = require('./lib');

const KINDS = new Set(['TEST', 'DEVICE']);
// 4차 ⑦ — OBS 허용 id. 근거 docs/검증/선행/20261029_1V_약관미동의_본화면진입_원인.md (약관 미동의자가 본 화면에 자리 잡음 · 제품 결함 유력).
const OBS_ALLOWED = new Set([
  'H-OBS-TERMS', // 약관 결함 별건이 고쳐지면 이 줄을 지우고 H-D2 를 다시 [장치] 빨강으로
]);

function isObsRow(x) {
  return x.kind === 'OBS' && OBS_ALLOWED.has(x.id);
}

// ⬛ [낡은 줄 · 1~10차] 이 폴더의 K 본문 파일 수 — 워크플로 S8 의 셸 글롭과 같은 뜻.
// function kBodyFiles(dir = __dirname) {
//   return fs.readdirSync(dir).filter((f) => /^k35-.+\.js$/.test(f));
// }
//   └ 셸 글롭과 이 정규식이 다른 규칙이라 경계 꼴 이름이 「실행됨 · 0개로 셈」 으로 갈렸다(N1).
// 🔴 F-PR ⑧ — 한 규칙. K 본문 머리로 **시작하는** 이름만 대상 · 엄격한 꼴이면 실행 · 아니면 이상(머리 없는 이름은 무시).
const K_BODY_HEAD = 'k35-';
const K_BODY_STRICT = /^k35-[a-z0-9][a-z0-9-]*\.js$/;
// 순수 함수(파일 시스템 0) — 실행기·판정기·자체시험이 같은 함수를 부른다.
function classifyKNames(names) {
  const run = [];
  const odd = [];
  for (const n of names) {
    const s = String(n);
    if (!s.startsWith(K_BODY_HEAD)) continue;
    if (K_BODY_STRICT.test(s)) run.push(s);
    else odd.push(s);
  }
  return { run: run.sort(), odd: odd.sort() };
}
function kBodyScan(dir = __dirname) {
  return classifyKNames(fs.readdirSync(dir));
}
// 옛 이름 유지(내보내기 · 바깥 호출 호환) — 실행 대상 목록 = kBodyScan(dir).run
function kBodyFiles(dir = __dirname) {
  return kBodyScan(dir).run;
}

// 🔴 F-PR ⑨ — H 기대 키 틀(참고 런 24행 기준 · 상신-L14). 키 칸이 비면 '-'.
function rowKey(x) {
  return [x.scope ?? '-', x.id ?? '-', x.g ?? '-', x.path ?? '-', x.browser ?? '-'].join('|');
}
const EXPECT_BROWSERS = ['chromium', 'webkit']; // 장치가 도는 순서 그대로(첫 = chromium)
const EXPECT_H = {
  global: ['H-S5a', 'H-S5b', 'H-S5c', 'H-RC', 'H-NA-END'], // 브라우저 없음 필수
  perBrowser: ['H-D1', 'H-D2c', 'H-BRIEF', 'H-D3', 'H-T1', 'H-T1w', 'H-D4'], // 브라우저마다 필수
  firstBrowser: ['H-T2r', 'H-T2'], // 첫 브라우저만 필수
  // ⬛ [낡은 꼴 · F-PR 설계 세부 첫 꼴] 갈래 묶음 둘 = 관찰 길 · 이미 동의된 길(약관 화면 길 없음 · 「묶음 둘 중 정확히 하나」)
  //   └ [3-V] 01(P1 · PM 11/6 승인): 약관 결함이 고쳐지면 첫 브라우저가 약관 화면 길을 타 세 줄을 적는다 → 거짓 빨강.
  // 🔴 F-PR [3-V] 01 — device-check 의 약관 문(termsGate · observeTermsThenAgree)이 **통과 경로**에서 실제로 적는 줄 집합에서 뽑았다.
  //   관찰 길 · 약관 화면 길 · 이미 동의된 길. 브라우저마다 갈래 id 집합이 묶음 하나와 **정확히 같을 때만** 통과.
  branchPerBrowser: [
    ['H-OBS-TERMS', 'H-D2b'], // 관찰 길(자리=본 화면 · 동의 요구 → API 동의 → 다시 로그인)
    ['H-D2', 'H-D2a', 'H-D2b'], // 약관 화면 길(자리=약관 화면 → 동의 전 API 대조 → 화면 동의)
    ['H-D2'], // 이미 동의된 길(앞 브라우저가 동의함)
  ],
  // 🔴 F-PR [4] P1(PM 11/6) — 정상 경로에서 같은 키가 두 줄 생기는 자리: 장치가 본 화면 진입 직후 한 줄 +
  //   첫 브라우저(결재 길 흉내를 도는 쪽)만 결재 길 클릭 직전 늦게 뜬 팝업을 닫으면 한 줄 더(통과 줄).
  //   그 id 만 첫 브라우저에서 2줄까지 허용 · 3줄 이상 = 중복 빨강 · 다른 브라우저는 1줄(그 길을 안 돈다).
  //   둘째 줄이 실패 줄이면 기존 [장치] 실패 규칙으로 빨강(키 틀이 가리지 않음).
  //   다른 id 의 다중 줄 · 조건 줄 전수 = 로컬 세부 표(공개 문서엔 범주만).
  twoRowsFirstBrowser: ['H-BRIEF'],
};
// ⬛ [낡은 줄 · F-PR] G 기대 키 자리 — 비워 둔다(K 연결 PR 이 rowKey 꼴로 채운다 · D3). K 본문이 있는데 비어 있으면 빨강.
// ⬛ [낡은 줄 · F-PR] const EXPECT_G = [];
// 🔴 K 연결(11/6 · 작업지시서 20261106작 #691 재기반 §4-2 · PM C2) — G 기대 키 16 = G 14 + N4 2.
//   키 = 범위|id|G|길|브라우저(rowKey 그대로 · 범위 = K). G 칸 N4 줄만 DEVICE · 나머지 G 줄 통과는 TEST 만.
const EXPECT_G_ROWS = [
  ['K35-G1', 'G1', 'bell', 'chromium'], ['K35-G1', 'G1', 'bell', 'webkit'],
  ['K35-G1', 'G1', 'toast', 'chromium'], ['K35-G1', 'G1', 'toast', 'webkit'],
  ['K35-G2', 'G2', 'bell', 'chromium'], ['K35-G2', 'G2', 'bell', 'webkit'],
  ['K35-G2', 'G2', 'toast', 'chromium'], ['K35-G2', 'G2', 'toast', 'webkit'],
  ['K35-G3', 'G3', 'bell', 'chromium'],
  ['K35-G3-SELF', 'G3', 'self', 'chromium'],
  ['K35-G4-APPR', 'G4', 'bell', 'chromium'],
  ['K35-G4-CHATAPPR', 'G4', 'bell', 'chromium'],
  // 🔴 K 연결 2차(PM 결재 A7 · PM Q3) — G4 토스트 길이 실제 흐름으로 선다(코드 읽기: 결재 상신 → 알림 배관 → 토스트 OnClick → 본 창 이동)
  //   → 키 2 추가(16 → 18 = G 16 · N4 2). 선택 키 0 — 빠지면 누락 빨강. 자체시험 K-C22k 고정 수도 같은 커밋에서 18.
  ['K35-G4-APPR', 'G4', 'toast', 'chromium'],
  ['K35-G4-CHATAPPR', 'G4', 'toast', 'chromium'],
  ['K35-G5', 'G5', 'bell', 'chromium'], ['K35-G5', 'G5', 'toast', 'chromium'],
  ['K35-N4', 'N4', null, 'chromium'], ['K35-N4', 'N4', null, 'webkit'],
];
const EXPECT_G = EXPECT_G_ROWS.map(([id, g, p, browser]) => rowKey({ scope: 'K', id, g, path: p, browser }));
// N4 키(화면 글자 대조 · DEVICE 줄) — 나머지 G 키 줄 통과는 TEST 만(§4-2 ②)
const EXPECT_G_DEVICE = new Set(EXPECT_G_ROWS.filter(([, g]) => g === 'N4')
  .map(([id, g, p, browser]) => rowKey({ scope: 'K', id, g, path: p, browser })));
// 🔴 F-PR [3-V] 03 — 결과표에서 온 글자를 찍기 전 제어 글자(줄바꿈 · 색 제어 등)를 \uXXXX 글자로 가린다
//   (한 줄이 두 줄로 갈라져 러너 명령 줄처럼 읽히는 길 0). 소독 **뒤에** 가린다 — 값 안 제어 글자로 소독 대조가 깨지지 않게.
function showCtl(s) {
  return String(s).replace(/[\u0000-\u001f\u007f-\u009f\u2028\u2029]/g, (c) => `\\u${c.charCodeAt(0).toString(16).padStart(4, '0')}`);
}
function checkExpectKeys(rows, { kFiles = 0 } = {}) {
  const hKey = (id, browser = null) => rowKey({ scope: 'H', id, browser });
  const counts = new Map();
  const scopeOf = new Map();
  for (const x of rows) {
    const k = rowKey(x);
    counts.set(k, (counts.get(k) || 0) + 1);
    scopeOf.set(k, x.scope);
  }
  const required = new Set();
  for (const id of EXPECT_H.global) required.add(hKey(id));
  for (const b of EXPECT_BROWSERS) for (const id of EXPECT_H.perBrowser) required.add(hKey(id, b));
  for (const id of EXPECT_H.firstBrowser) required.add(hKey(id, EXPECT_BROWSERS[0]));
  // 🔴 K 연결 ① — 필수 집합 = H 필수 ∪ G 기대 키 전부(K 본문 수와 무관 · §4-2 A2)
  for (const k of EXPECT_G) required.add(k);
  const branchKeys = new Set();
  for (const b of EXPECT_BROWSERS) for (const grp of EXPECT_H.branchPerBrowser) for (const id of grp) branchKeys.add(hKey(id, b));
  const gKeys = new Set(EXPECT_G);
  const missing = [...required].filter((k) => !counts.has(k)).sort();
  // ⬛ [낡은 줄 · F-PR 첫 꼴] const dup = [...counts].filter(([, n]) => n > 1).map(([k]) => k).sort();
  const cap = new Map(EXPECT_H.twoRowsFirstBrowser.map((id) => [hKey(id, EXPECT_BROWSERS[0]), 2])); // F-PR [4] P1
  const dup = [...counts].filter(([k, n]) => n > (cap.get(k) || 1)).map(([k]) => k).sort();
  const unknown = [...counts.keys()].filter((k) => (scopeOf.get(k) === 'H'
    ? !required.has(k) && !branchKeys.has(k)
    : !gKeys.has(k))).sort();
  const branch = [];
  // ⬛ [낡은 줄 · F-PR 첫 꼴] 묶음마다 「다 있음」 수 = 1 · 일부만 있는 묶음 0 — 묶음끼리 id 가 겹치면(세 묶음) 셀 수 없다.
  // 🔴 F-PR [3-V] 01 — 브라우저마다 「갈래 id 집합」 이 묶음 하나와 정확히 같을 때만 통과.
  const branchIds = [...new Set(EXPECT_H.branchPerBrowser.flat())];
  for (const b of EXPECT_BROWSERS) {
    const have = branchIds.filter((id) => counts.has(hKey(id, b))).sort().join(',');
    const same = EXPECT_H.branchPerBrowser.some((grp) => [...grp].sort().join(',') === have);
    if (!same) branch.push(b);
  }
  const gEmpty = kFiles > 0 && EXPECT_G.length === 0; // ⑥ 그대로(G 자리를 채운 뒤에는 닿지 않음 · 기록)
  // 🔴 K 연결 ② — G 기대 키 줄의 통과는 TEST 줄로만(N4 줄만 DEVICE). DEVICE 통과 줄로 G 키를 채우면 키 틀 빨강.
  //   G 키의 DEVICE 실패 줄(단언 전 실패)은 꼴 이상이 아니다 — 아래 [장치] 실패 규칙이 빨강으로 잡는다.
  //   ③ 중복 = 위 dup(상한 1 · G 키는 두 줄 허용 목록에 없음) · ④ 목록 밖 비H 키 = 위 unknown 그대로.
  const gForm = [];
  for (const x of rows) {
    const k = rowKey(x);
    if (x.scope === 'H' || !gKeys.has(k)) continue;
    const okForm = EXPECT_G_DEVICE.has(k) ? x.kind === 'DEVICE' : (x.kind === 'TEST' || x.pass !== true);
    if (!okForm && !gForm.includes(k)) gForm.push(k);
  }
  gForm.sort();
  // 🔴 K 연결 ⑤ — K 본문 0 인데 G 기대 키가 정의됨 = 키 틀 빨강(이유 줄 「K 본문 0 · G 기대 n」)
  const kZero = kFiles === 0 && EXPECT_G.length > 0;
  // ⬛ [낡은 줄 · F-PR] return { missing, dup, unknown, branch, gEmpty };
  return { missing, dup, unknown, branch, gEmpty, gForm, kZero };
}

function isGTest(x) {
  if (x.scope === 'H') return false;
  return /^G/.test(String(x.id ?? '')) || /^G/.test(String(x.g ?? ''));
}

function main() {
  const p = L.resultPath();
  if (!fs.existsSync(p)) {
    console.log('⚫ FAIL [장치] 결과표 없음 — 앞 단계(시드·기동·브라우저)가 결과표를 만들기 전에 섰다');
    process.exit(1);
  }
  let r;
  try {
    r = JSON.parse(fs.readFileSync(p, 'utf8'));
  } catch (e) {
    console.log(`⚫ FAIL [장치] 결과표 해석 실패 (${e.name})`);
    process.exit(1);
  }
  const rows = r && Array.isArray(r.rows) ? r.rows : [];
  // 2차 ⑤ — 꼴이 맞는 줄만 센다. 꼴이 틀린 줄은 따로 모아 빨강.
  //   🔴 F-PR D8 — 보호 범위 = 결과표에 직접 기록된 줄. 기록 함수(lib rec)를 거친 줄은 F-PR N2(참거짓 아니면 던지기)부터 보호된다
  //   (그 전에는 rec 가 결과 칸을 참거짓으로 바꿔 저장해 이 형 검사에 닿지 않았다).
  // ⬛ [낡은 줄 · 2~3차] const bad = rows.filter((x) => !x || typeof x !== 'object' || !KINDS.has(x.kind) || typeof x.pass !== 'boolean');
  // 4차 ⑦ — OBS 는 허용 id 일 때만 꼴이 맞다.
  const bad = rows.filter((x) => !x || typeof x !== 'object' || !(KINDS.has(x.kind) || isObsRow(x)) || typeof x.pass !== 'boolean');
  const good = rows.filter((x) => !bad.includes(x));
  const obs = good.filter((x) => x.kind === 'OBS');
  const tests = good.filter((x) => x.kind === 'TEST');
  const failT = tests.filter((x) => x.pass !== true);
  const failD = good.filter((x) => x.kind === 'DEVICE' && x.pass !== true);
  // ⬛ [낡은 줄 · 1~10차] const kFiles = kBodyFiles();
  const kScan = kBodyScan(); // 🔴 F-PR ⑧ — 실행기와 같은 함수
  const kFiles = kScan.run;
  const gTests = tests.filter(isGTest);
  const keys = checkExpectKeys(good, { kFiles: kFiles.length }); // 🔴 F-PR ⑨
  const m = (v) => showCtl(L.sanitize(v, 120)); // 🔴 F-PR ⑩ — 메타 칸(키 칸 상한과 같은 120) · 제어 글자 가림([3-V] 03)
  const showList = (list) => showCtl(L.sanitize(list.join(', '), 600)); // 🔴 F-PR [3-V] 03 — 키 목록

  console.log('── 판정표 ──');
  for (const x of good) {
    if (x.kind === 'OBS') {
      // ⬛ [낡은 줄 · 4~10차] console.log(`🟡 OBS ${x.scope || '-'} ${x.id}${x.browser ? ` (${x.browser})` : ''} — 제품 관찰(빨강 아님 · 별건) · ${L.sanitize(x.got, 1200)}`);
      // ⬛ [낡은 줄 · F-PR 첫 꼴] console.log(`🟡 OBS ${m(x.scope || '-')} ${m(x.id)}${x.browser ? ` (${m(x.browser)})` : ''} — 제품 관찰(빨강 아님 · 별건) · ${L.sanitize(x.got, 1200)}`);
      // 🔴 F-PR [3-V] 2차 P2 — 관찰 결과 칸도 소독 뒤 제어 글자 가림
      console.log(`🟡 OBS ${m(x.scope || '-')} ${m(x.id)}${x.browser ? ` (${m(x.browser)})` : ''} — 제품 관찰(빨강 아님 · 별건) · ${showCtl(L.sanitize(x.got, 1200))}`);
      continue;
    }
    // ⬛ [낡은 줄 · F-PR] const mark = x.pass === true ? '🟢 PASS' : (x.kind === 'TEST' ? '🔴 FAIL [시험]' : '⚫ FAIL [장치]');
    // 🔴 PM 결정 4V-1 ④ — N4 거짓 줄은 「누출 의심」 표지(재실행 대상 아님 · kind 는 DEVICE 그대로)
    const n4Bad = x.pass !== true && x.kind === 'DEVICE' && x.scope !== 'H' && EXPECT_G_DEVICE.has(rowKey(x));
    const mark = x.pass === true ? '🟢 PASS' : (x.kind === 'TEST' ? '🔴 FAIL [시험]' : (n4Bad ? '🔴 FAIL [장치 · 누출 의심]' : '⚫ FAIL [장치]'));
    // ⬛ [낡은 줄 · 1~10차] console.log(`${mark} ${x.scope || '-'} ${x.id}${x.g ? ` ${x.g}` : ''}${x.path ? ` ${x.path}` : ''}${x.browser ? ` (${x.browser})` : ''}`);
    console.log(`${mark} ${m(x.scope || '-')} ${m(x.id)}${x.g ? ` ${m(x.g)}` : ''}${x.path ? ` ${m(x.path)}` : ''}${x.browser ? ` (${m(x.browser)})` : ''}`);
  }
  for (const x of bad) {
    const id = x && typeof x === 'object' ? String(x.id ?? '-') : '-';
    const kind = x && typeof x === 'object' ? JSON.stringify(x.kind ?? null) : '-';
    const pass = x && typeof x === 'object' ? `${typeof x.pass}` : '-';
    const why = x && typeof x === 'object' && x.kind === 'OBS' && !OBS_ALLOWED.has(x.id) ? ' · OBS 허용 목록 밖 id' : '';
    // ⬛ [낡은 줄 · 1~10차 · F-PR 첫 꼴 그대로] console.log(`⚫ FAIL [장치] 줄 꼴 이상 ${L.sanitize(id)} · kind=${L.sanitize(kind)} · pass 형=${pass}${why}`);
    // 🔴 F-PR [3-V] 2차 P2 — 줄 꼴 이상 줄도 소독 뒤 제어 글자 가림(일관성)
    console.log(`⚫ FAIL [장치] 줄 꼴 이상 ${showCtl(L.sanitize(id))} · kind=${showCtl(L.sanitize(kind))} · pass 형=${pass}${why}`);
  }
  // ⬛ [낡은 줄 · 1~10차] console.log(`시드: ${r && r.seed ? `${r.seed.mode} · ${r.seed.path}` : '기록 없음'}`);
  console.log(`시드: ${r && r.seed ? `${m(r.seed.mode)} · ${m(r.seed.path)}` : '기록 없음'}`);
  // ⬛ [낡은 줄 · 2~3차] 끝에 「· 꼴 이상 ${bad.length}」 까지 — 4차: + 관찰 수(빨강 아님)
  console.log(`돈 시험 ${tests.length}건(G ${gTests.length} · K 본문 파일 ${kFiles.length}) · 시험 실패 ${failT.length} · 장치 실패 ${failD.length} · 꼴 이상 ${bad.length} · 🟡 관찰 ${obs.length}(빨강 아님)`);
  // 🔴 F-PR ⑧⑨ — 이름 · 키 틀 한 줄(키 글자는 빨강 줄에서 소독 뒤)
  console.log(`K 본문 이름: 실행 ${kFiles.length} · 이상 ${kScan.odd.length}`);
  // ⬛ [낡은 줄 · F-PR] console.log(`키 틀: 누락 … · 갈래 ${keys.branch.length} · G 자리 ${EXPECT_G.length}${keys.gEmpty ? '(비었음 · K 본문 있음)' : ''}`);
  // 🔴 K 연결 — 끝에 G 통과 꼴 수를 더한다(앞 칸 꼴 그대로)
  console.log(`키 틀: 누락 ${keys.missing.length} · 중복 ${keys.dup.length} · 모르는 ${keys.unknown.length} · 갈래 ${keys.branch.length} · G 자리 ${EXPECT_G.length}${keys.gEmpty ? '(비었음 · K 본문 있음)' : ''} · G 통과 꼴 ${keys.gForm.length}`);
  if (r && r.external_blocked > 0) {
    // ⬛ [낡은 줄 · 1~10차] console.log(`🟡 외부 요청 끊음 ${r.external_blocked}건 · 호스트 [${(r.external_hosts || []).join(', ')}] (기준 = 첫 런 뒤 PM · §8 ⑦)`);
    // ⬛ [낡은 줄 · F-PR 첫 꼴] console.log(`🟡 외부 요청 끊음 ${r.external_blocked}건 · 호스트 [${L.sanitize((r.external_hosts || []).join(', '), 600)}] (기준 = 첫 런 뒤 PM · §8 ⑦)`);
    // 🔴 F-PR [3-V] 2차 P2 — 외부 호스트 줄도 소독 뒤 제어 글자 가림
    console.log(`🟡 외부 요청 끊음 ${r.external_blocked}건 · 호스트 [${showCtl(L.sanitize((r.external_hosts || []).join(', '), 600))}] (기준 = 첫 런 뒤 PM · §8 ⑦)`);
  } else {
    console.log('외부 요청 끊음 0건');
  }
  if (r && Array.isArray(r.plant) && r.plant.length > 0) {
    // ⬛ [낡은 줄 · 1~10차] console.log(`::warning::측정 커밋 런(plant=${r.plant.join(',')}) — 이 런 결과는 장치 검증용 · 머지 금지`);
    // 🔴 F-PR [3-V] 03 — 표지 줄도 소독 · 제어 글자 가림
    console.log(`::warning::측정 커밋 런(plant=${showCtl(L.sanitize(r.plant.join(','), 120))}) — 이 런 결과는 장치 검증용 · 머지 금지`);
  }

  // 순서(2차): [시험] 실패(재실행 금지가 걸리는 쪽) → 줄 꼴 이상(원인이 0건으로 가려지지 않게) → 0건 → K 0건 → [장치]
  if (failT.length > 0) {
    console.log('🔴 FAIL [시험] — 재실행 금지(§5) · 런 번호·결과표를 개발명세서에');
    process.exit(1);
  }
  if (bad.length > 0) {
    console.log('⚫ FAIL [장치] 결과표 줄 꼴 이상 — kind 는 TEST/DEVICE(OBS 는 허용 id 만 · 4차 ⑦) · pass 는 참거짓만(2차 ⑤)');
    process.exit(1);
  }
  // 🔴 F-PR ⑧ — 이름 꼴 이상(줄 꼴 이상 바로 뒤 · 키 틀과 같은 층)
  if (kScan.odd.length > 0) {
    console.log(`⚫ FAIL [장치] K 본문 이름 꼴 이상 ${kScan.odd.length}개 — 실행기도 아무것도 안 돌린다 · ${showList(kScan.odd.map((n) => JSON.stringify(n)))}`);
    process.exit(1);
  }
  // 🔴 F-PR ⑨ — H 기대 키 틀
  // ⬛ [낡은 줄 · F-PR] if (keys.missing.length + keys.dup.length + keys.unknown.length + keys.branch.length > 0 || keys.gEmpty) {
  if (keys.missing.length + keys.dup.length + keys.unknown.length + keys.branch.length + keys.gForm.length > 0 || keys.gEmpty || keys.kZero) {
    const why = [];
    if (keys.missing.length) why.push(`누락 [${showList(keys.missing)}]`);
    if (keys.dup.length) why.push(`중복 [${showList(keys.dup)}]`);
    if (keys.unknown.length) why.push(`모르는 [${showList(keys.unknown)}]`);
    if (keys.branch.length) why.push(`갈래 어긋난 브라우저 [${showList(keys.branch)}]`);
    if (keys.gEmpty) why.push(`K 본문 ${kFiles.length}개가 있는데 G 자리 비었음(G 기대 키 = K 연결 PR 몫)`);
    if (keys.gForm.length) why.push(`G 통과 꼴 이상(G 줄 통과는 TEST · N4 줄은 DEVICE) [${showList(keys.gForm)}]`); // K 연결 ②
    if (keys.kZero) why.push(`K 본문 0 · G 기대 ${EXPECT_G.length}`); // K 연결 ⑤
    console.log(`⚫ FAIL [장치] 키 틀 — ${why.join(' · ')}`);
    process.exit(1);
  }
  if (tests.length === 0) {
    console.log('🔴 FAIL 돈 시험 0건 = 빨강 (§4-3 ⓔ)');
    process.exit(1);
  }
  if (kFiles.length > 0 && gTests.length === 0) {
    console.log(`🔴 FAIL 돈 시험 0건 = 빨강 — K 본문 ${kFiles.length}개가 있는데 G 시험 0건(H 자체 점검은 안 센다 · §4-3 ⓔ)`);
    process.exit(1);
  }
  // 🔴 PM 결정 4V-1 ④(11/6 · [4] 1차 P2-4) — N4 줄(DEVICE 그대로)이 거짓이면 [장치] 재실행 꼴이 아니라 누출 의심 끝 줄.
  //   [장치] 실패보다 먼저 분류한다(다른 장치 실패와 섞여도 끝 줄이 「재실행 1회」 로 읽히지 않게).
  const failN4 = failD.filter((x) => EXPECT_G_DEVICE.has(rowKey(x)));
  if (failN4.length > 0) {
    console.log(`🔴 FAIL [장치] N4 거짓 ${failN4.length}줄 — 누출 의심 · 재실행 금지(업로드 닫힘 · 원인부터 본다)`);
    process.exit(1);
  }
  if (failD.length > 0) {
    console.log('⚫ FAIL [장치] — 같은 커밋 재실행 1회까지(§5)');
    process.exit(1);
  }
  console.log('🟢 PASS 전부');
}

if (require.main === module) main();
// ⬛ [낡은 줄 · 2~3차] module.exports = { kBodyFiles, isGTest };
// ⬛ [낡은 줄 · 4~10차] module.exports = { kBodyFiles, isGTest, OBS_ALLOWED, isObsRow };
module.exports = {
  kBodyFiles, isGTest, OBS_ALLOWED, isObsRow,
  K_BODY_STRICT, classifyKNames, kBodyScan, // F-PR ⑧
  rowKey, EXPECT_BROWSERS, EXPECT_H, EXPECT_G, checkExpectKeys, // F-PR ⑨
  EXPECT_G_ROWS, EXPECT_G_DEVICE, // K 연결 ⑪⑫
};
