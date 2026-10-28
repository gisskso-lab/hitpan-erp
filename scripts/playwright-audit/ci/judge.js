// G1-3 갈래 H — S9 판정 (작업지시서 §3-2 S9 · §4-2 · §4-3 ⓔ)
//
// 빨강 조건
//   ① 결과표 없음/깨짐 → ⚫ [장치]
//   ② 돈 시험(kind TEST) 0건 → 🔴 (db-gate 「SKIP 0」 과 같은 뜻)
//   ③ FAIL 1건+ → TEST 면 🔴 [시험] · DEVICE 뿐이면 ⚫ [장치]
//   ④ plant.flags 가 있는 측정 커밋 → 결과와 무관하게 표지만 크게 찍는다(머지 금지 표시)
// 외부 요청: external_blocked ≥ 1 은 🟡 표시만(§3-4 · 기준은 첫 런 실측 뒤 PM)
'use strict';

const fs = require('fs');
const L = require('./lib');

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
  const rows = Array.isArray(r.rows) ? r.rows : [];
  const tests = rows.filter((x) => x.kind === 'TEST');
  const failT = tests.filter((x) => !x.pass);
  const failD = rows.filter((x) => x.kind === 'DEVICE' && !x.pass);

  console.log('── 판정표 ──');
  for (const x of rows) {
    const mark = x.pass ? '🟢 PASS' : (x.kind === 'TEST' ? '🔴 FAIL [시험]' : '⚫ FAIL [장치]');
    console.log(`${mark} ${x.scope || '-'} ${x.id}${x.g ? ` ${x.g}` : ''}${x.path ? ` ${x.path}` : ''}${x.browser ? ` (${x.browser})` : ''}`);
  }
  console.log(`시드: ${r.seed ? `${r.seed.mode} · ${r.seed.path}` : '기록 없음'}`);
  console.log(`돈 시험 ${tests.length}건 · 시험 실패 ${failT.length} · 장치 실패 ${failD.length}`);
  if (r.external_blocked > 0) {
    console.log(`🟡 외부 요청 끊음 ${r.external_blocked}건 · 호스트 [${(r.external_hosts || []).join(', ')}] (기준 = 첫 런 뒤 PM · §8 ⑦)`);
  } else {
    console.log('외부 요청 끊음 0건');
  }
  if (Array.isArray(r.plant) && r.plant.length > 0) {
    console.log(`::warning::측정 커밋 런(plant=${r.plant.join(',')}) — 이 런 결과는 장치 검증용 · 머지 금지`);
  }

  if (tests.length === 0) {
    console.log('🔴 FAIL 돈 시험 0건 = 빨강 (§4-3 ⓔ)');
    process.exit(1);
  }
  if (failT.length > 0) {
    console.log('🔴 FAIL [시험] — 재실행 금지(§5) · 런 번호·결과표를 개발명세서에');
    process.exit(1);
  }
  if (failD.length > 0) {
    console.log('⚫ FAIL [장치] — 같은 커밋 재실행 1회까지(§5)');
    process.exit(1);
  }
  console.log('🟢 PASS 전부');
}

main();
