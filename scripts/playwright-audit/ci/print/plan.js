'use strict';
// W-G1-51 갈래 Z — 런 계획 읽기 (개발명세서 §3 ⑥ · ⑧ · 작업지시서 §4-1)
//   plan.json 의 `run`(이번에 쏠 런 이름)과 그 런의 잡 목록 → matrix JSON 한 줄.
//   🔴 워크플로는 이 출력만 받는다 — 런마다 yml 을 고치지 않고 plan.json 만 커밋으로 바꾼다(바뀐 줄 = 리뷰 · 대표 push 직전 보고).
//   🔴 잡 이름 · 판 · 패치 경로는 좁은 꼴만 받는다(셸 · 식 글자로 새는 자리 0). 꼴이 틀리면 matrix 를 내지 않고 실패.
//   🔴 PM §6-a 3 — qpdf 는 ⑨ 를 재는 잡(고침 R1 ㉮ · 고침 R3 ⑨ⓐⓑⓒ)에만. Z 런 잡에 qpdf 참이면 계획 거절.
//   쓰는 법: node plan.js [GITHUB_OUTPUT 파일]   — 파일이 있으면 `matrix=…` 한 줄을 덧붙인다.
const fs = require('fs');
const path = require('path');

const RUN_NAMES = ['Z1', 'Z2', 'Z3', 'Z4', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6'];
const ID_RE = /^[a-z0-9][a-z0-9-]{0,39}$/;
const PATCH_RE = /^scripts\/playwright-audit\/ci\/print\/patches\/[a-z0-9][a-z0-9-]{0,59}\.patch$/;
const BOARDS = ['main', 'merged'];
const TENANTS = ['A', 'AB'];
const PHASES = ['read', 'old-rows', 'nine', 'retry'];

function validate(plan) {
  const why = [];
  if (!plan || plan.schema !== 'print-plan/1') why.push('schema 다름');
  if (!plan || !RUN_NAMES.includes(plan.run)) why.push(`run 이름 다름(${plan && plan.run})`);
  const runs = (plan && plan.runs) || {};
  for (const [name, r] of Object.entries(runs)) {
    if (!RUN_NAMES.includes(name)) { why.push(`모르는 런 ${name}`); continue; }
    if (!Array.isArray(r.jobs)) { why.push(`${name} jobs 없음`); continue; }
    const seen = new Set();
    for (const j of r.jobs) {
      if (!ID_RE.test(String(j.id))) why.push(`${name} 잡 이름 꼴 다름`);
      if (seen.has(j.id)) why.push(`${name} 잡 이름 겹침 ${j.id}`);
      seen.add(j.id);
      if (!BOARDS.includes(j.board)) why.push(`${name}/${j.id} 판 다름`);
      if (!TENANTS.includes(j.tenant)) why.push(`${name}/${j.id} 테넌트 꼴 다름`);
      if (!PHASES.includes(j.phase)) why.push(`${name}/${j.id} 단계 다름`);
      if (j.patch !== '' && !PATCH_RE.test(String(j.patch))) why.push(`${name}/${j.id} 패치 경로 꼴 다름`);
      if (typeof j.qpdf !== 'boolean') why.push(`${name}/${j.id} qpdf 칸 참/거짓 아님`);
      if (name.startsWith('Z') && j.qpdf === true) why.push(`${name}/${j.id} Z 런에 qpdf(PM §6-a 3 · CTO §4-2 조건 7)`);
      if (j.qpdf === true && j.phase !== 'nine') why.push(`${name}/${j.id} qpdf 는 ⑨ 재는 잡만`);
    }
  }
  return { ok: why.length === 0, why: why.join(' · ') };
}

function matrixFor(plan) {
  const r = plan.runs && plan.runs[plan.run];
  if (!r || !Array.isArray(r.jobs) || r.jobs.length === 0) return null;
  return {
    include: r.jobs.map((j) => ({ id: j.id, board: j.board, tenant: j.tenant, phase: j.phase, patch: j.patch, qpdf: j.qpdf, run: plan.run })),
  };
}

function main(outFile) {
  const plan = JSON.parse(fs.readFileSync(path.join(__dirname, 'plan.json'), 'utf8'));
  const v = validate(plan);
  if (!v.ok) {
    console.log(`⚫ FAIL [장치] 런 계획 꼴 — ${v.why}`);
    return 1;
  }
  const m = matrixFor(plan);
  if (!m) {
    console.log(`⚫ FAIL [장치] 런 ${plan.run} 잡 0 — 발사 금지(${(plan.runs[plan.run] || {}).blocked || '계획 없음'})`);
    return 1;
  }
  const line = `matrix=${JSON.stringify(m)}`;
  if (outFile) fs.appendFileSync(outFile, `${line}\n`);
  console.log(`런 계획: ${plan.run} · 잡 ${m.include.length} · ${m.include.map((j) => `${j.id}(${j.board}${j.patch ? ' + 패치' : ''}${j.qpdf ? ' + qpdf' : ''})`).join(' · ')}`);
  return 0;
}

if (require.main === module) process.exitCode = main(process.argv[2]);

module.exports = { validate, matrixFor, RUN_NAMES };
