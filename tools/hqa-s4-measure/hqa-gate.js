'use strict';
// [장치] HQA 회신 대기 칸 장전 관문 — 고침1 F8 (C4) · F9 (C2)
//   사용: node hqa-gate.js <rules.json>
//   종료 코드: 0 = 필수 칸 전부 확정 · 4 = 하나라도 미확정 · 1 = 읽기 오류
//   부르는 곳: m0-assert.sh (장전 때 · M0 단계에서 거부) · run-items.js (측정 직전 다시 거부)
//   · 필수 칸 목록은 이 파일에 고정한다 — rules.json 에서 칸을 지우면 「missing」 으로 거부(키 삭제 우회 0)
//   · 확정 꼴 = status 가 정확히 「HQA 회신 반영 · 댓글 <숫자 6자 이상>」(앞뒤 덧붙임 0)
//               + value 가 null · 빈 글자 · 빈 객체가 아님 + 목록 밖 칸 0(밑줄로 시작하는 표지 칸 제외)
//   · 출력은 칸 이름 · 사유만(값 0)
const fs = require('fs');

const REQUIRED = ['S1toS4', 'rawFormat', 'samplesPerCell', 'delayUnit', 'browserCondition', 'T3'];
const CONFIRMED_RE = /^HQA 회신 반영 · 댓글 [0-9]{6,}$/;

function emptyValue(v) {
  if (v === null || v === undefined) return true;
  if (typeof v === 'string' && v.trim() === '') return true;
  if (typeof v === 'object' && !Array.isArray(v) && Object.keys(v).length === 0) return true;
  if (Array.isArray(v) && v.length === 0) return true;
  return false;
}

function check(rules) {
  const problems = [];
  const hp = rules && rules.hqaPending;
  if (!hp || typeof hp !== 'object' || Array.isArray(hp)) {
    return { ok: false, required: REQUIRED, problems: [{ key: 'hqaPending', why: 'missing' }] };
  }
  for (const k of REQUIRED) {
    const e = hp[k];
    if (!e || typeof e !== 'object' || Array.isArray(e)) { problems.push({ key: k, why: 'missing' }); continue; }
    if (typeof e.status !== 'string' || !CONFIRMED_RE.test(e.status)) problems.push({ key: k, why: 'status' });
    if (emptyValue(e.value)) problems.push({ key: k, why: 'value' });
  }
  for (const k of Object.keys(hp)) {
    if (!k.startsWith('_') && !REQUIRED.includes(k)) problems.push({ key: k, why: 'unknown-key' });
  }
  return { ok: problems.length === 0, required: REQUIRED, problems };
}

module.exports = { REQUIRED, CONFIRMED_RE, check };

if (require.main === module) {
  let rc;
  try {
    const f = process.argv[2];
    if (!f || !fs.existsSync(f)) throw new Error('usage: node hqa-gate.js <rules.json>');
    const r = check(JSON.parse(fs.readFileSync(f, 'utf8')));
    process.stdout.write(JSON.stringify({ hqa_gate: r.ok ? 'confirmed' : 'refused', problems: r.problems }) + '\n');
    rc = r.ok ? 0 : 4;
  } catch (e) {
    process.stdout.write(JSON.stringify({ hqa_gate: 'error', error: String(e && e.message ? e.message : e).slice(0, 160) }) + '\n');
    rc = 1;
  }
  process.exit(rc);
}
