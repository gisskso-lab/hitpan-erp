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

// 이 폴더의 K 본문 파일 수 — 워크플로 S8 의 `scripts/playwright-audit/ci/k35-*.js` 와 같은 뜻.
function kBodyFiles(dir = __dirname) {
  return fs.readdirSync(dir).filter((f) => /^k35-.+\.js$/.test(f));
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
  // ⬛ [낡은 줄 · 2~3차] const bad = rows.filter((x) => !x || typeof x !== 'object' || !KINDS.has(x.kind) || typeof x.pass !== 'boolean');
  // 4차 ⑦ — OBS 는 허용 id 일 때만 꼴이 맞다.
  const bad = rows.filter((x) => !x || typeof x !== 'object' || !(KINDS.has(x.kind) || isObsRow(x)) || typeof x.pass !== 'boolean');
  const good = rows.filter((x) => !bad.includes(x));
  const obs = good.filter((x) => x.kind === 'OBS');
  const tests = good.filter((x) => x.kind === 'TEST');
  const failT = tests.filter((x) => x.pass !== true);
  const failD = good.filter((x) => x.kind === 'DEVICE' && x.pass !== true);
  const kFiles = kBodyFiles();
  const gTests = tests.filter(isGTest);

  console.log('── 판정표 ──');
  for (const x of good) {
    if (x.kind === 'OBS') {
      console.log(`🟡 OBS ${x.scope || '-'} ${x.id}${x.browser ? ` (${x.browser})` : ''} — 제품 관찰(빨강 아님 · 별건) · ${L.sanitize(x.got, 1200)}`);
      continue;
    }
    const mark = x.pass === true ? '🟢 PASS' : (x.kind === 'TEST' ? '🔴 FAIL [시험]' : '⚫ FAIL [장치]');
    console.log(`${mark} ${x.scope || '-'} ${x.id}${x.g ? ` ${x.g}` : ''}${x.path ? ` ${x.path}` : ''}${x.browser ? ` (${x.browser})` : ''}`);
  }
  for (const x of bad) {
    const id = x && typeof x === 'object' ? String(x.id ?? '-') : '-';
    const kind = x && typeof x === 'object' ? JSON.stringify(x.kind ?? null) : '-';
    const pass = x && typeof x === 'object' ? `${typeof x.pass}` : '-';
    const why = x && typeof x === 'object' && x.kind === 'OBS' && !OBS_ALLOWED.has(x.id) ? ' · OBS 허용 목록 밖 id' : '';
    console.log(`⚫ FAIL [장치] 줄 꼴 이상 ${L.sanitize(id)} · kind=${L.sanitize(kind)} · pass 형=${pass}${why}`);
  }
  console.log(`시드: ${r && r.seed ? `${r.seed.mode} · ${r.seed.path}` : '기록 없음'}`);
  // ⬛ [낡은 줄 · 2~3차] 끝에 「· 꼴 이상 ${bad.length}」 까지 — 4차: + 관찰 수(빨강 아님)
  console.log(`돈 시험 ${tests.length}건(G ${gTests.length} · K 본문 파일 ${kFiles.length}) · 시험 실패 ${failT.length} · 장치 실패 ${failD.length} · 꼴 이상 ${bad.length} · 🟡 관찰 ${obs.length}(빨강 아님)`);
  if (r && r.external_blocked > 0) {
    console.log(`🟡 외부 요청 끊음 ${r.external_blocked}건 · 호스트 [${(r.external_hosts || []).join(', ')}] (기준 = 첫 런 뒤 PM · §8 ⑦)`);
  } else {
    console.log('외부 요청 끊음 0건');
  }
  if (r && Array.isArray(r.plant) && r.plant.length > 0) {
    console.log(`::warning::측정 커밋 런(plant=${r.plant.join(',')}) — 이 런 결과는 장치 검증용 · 머지 금지`);
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
  if (tests.length === 0) {
    console.log('🔴 FAIL 돈 시험 0건 = 빨강 (§4-3 ⓔ)');
    process.exit(1);
  }
  if (kFiles.length > 0 && gTests.length === 0) {
    console.log(`🔴 FAIL 돈 시험 0건 = 빨강 — K 본문 ${kFiles.length}개가 있는데 G 시험 0건(H 자체 점검은 안 센다 · §4-3 ⓔ)`);
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
module.exports = { kBodyFiles, isGTest, OBS_ALLOWED, isObsRow };
