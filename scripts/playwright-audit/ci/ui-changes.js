// K 연결(11/6 · 작업지시서 20261106작 #691 재기반 §5-4 · PM 결재 A3 ㉯ · C4 · 대표 Q1 「문서만 바꾼 PR 은 안 돈다」)
// ui-k35 잡을 돌릴지 정하는 거르기 규칙 — 순수 함수(decideRun) + 얇은 실행 껍데기.
//
// 🔴 PM 결정 3V-1(11/6 · [3-V] 1차 뒤 · §5-4 를 덮음) — 닫힌 쪽 규칙:
// ⬛ [낡은 줄 · 3V-1] //   「안 돈다」 = 바뀐 파일이 **전부 문서**(docs/ 아래 · 또는 확장자 .md)일 때만. 그 밖 파일이 하나라도 있으면 돈다(모르는 파일 = 돈다).
// 🔴 PM 결정 4V-1 ①(11/6 · [4] 1차 P2-1) — 문서 = **docs/ 아래만**(.md 라도 docs/ 밖이면 돈다) · 제어 글자(줄바꿈 등)가 든 이름 = 문서 아님(돈다).
//   「안 돈다」 = 바뀐 파일이 전부 docs/ 아래일 때만. 그 밖 파일이 하나라도 있으면 돈다(모르는 파일 = 돈다).
//   경로 허용 목록은 쓰지 않는다 — 목록에서 빠진 자리가 원천적으로 없다.
//   장치 폴더 · 워크플로 폴더가 바뀐 PR 은 워크플로가 이 스크립트를 부르기 **전에** 고정으로 「돈다」 로 정한다(이 파일은 PR 쪽 사본).
//
//   입력  = 표준입력 · 바뀐 파일 이름 목록(NUL 구분 · 워크플로가 git diff -z 로 만든다 · 🔴 4V-1 ② NUL 로만 나눈다 — 줄바꿈은 이름의 일부)
//   출력  = 표준출력 `run=true` 또는 `run=false` 한 줄 + 이유 한 줄(범주별 수만 · 파일 이름 글자 0)
//   닫힌 쪽 = 도는 쪽 — 빈 목록 · 읽기 오류 · 입력 꼴 이상 = run=true.
//   외부 코드 0 · 네트워크 0 · 파일 쓰기 0.
//
// 이 파일 이름은 K 본문 머리로 시작하지 않는다 — 실행기(run-k-bodies.js)가 K 본문으로 돌리지 않는다.
'use strict';

const fs = require('fs');

// ⬛ [낡은 꼴 · 갈래 A 첫 꼴 · PM 결정 3V-1 로 폐기] 경로 허용 목록(열린 쪽 — 목록 밖 = 안 돈다)
// const RUN_HEADS = [
//   ['화면', ['src/HitPan.Web/']],
//   ['API', ['src/HitPan.API/', 'src/HitPan.Application/', 'src/HitPan.Infrastructure/', 'src/HitPan.Domain/',
//     'src/HitPan.Contracts/', 'src/HitPan.Analyzers/', 'Directory.Build.props']],
//   ['출하 DDL', ['installer/hitpan_db_clean.sql']],
//   ['장치', ['scripts/playwright-audit/ci/']],
//   ['워크플로', ['.github/workflows/ui-browser.yml']],
// ];
// function headHit(name, head) { return head.endsWith('/') ? name.startsWith(head) : name === head; }

// ⬛ [낡은 줄 · 3V-1] 문서 판정 — docs/ 아래 · 또는 확장자 .md(소문자 그대로 · 그 밖 꼴은 문서로 안 봄 = 돈다 쪽)
const DOC_DIR = 'docs/';
const DOC_EXT = '.md'; // ⬛ 4V-1 ① 뒤로 판정에 안 쓴다(내보냄 호환으로만 남김)
// ⬛ [낡은 줄 · 3V-1] function isDoc(name) {
// ⬛ [낡은 줄 · 3V-1]   return name.startsWith(DOC_DIR) || name.endsWith(DOC_EXT);
// ⬛ [낡은 줄 · 3V-1] }
// 🔴 PM 결정 4V-1 ① — docs/ 아래만 문서 · 제어 글자가 든 이름은 해석 못 함 = 문서 아님(돈다 쪽)
const CTL_CHARS = /[\u0000-\u001f\u007f-\u009f\u2028\u2029]/;
function isDoc(name) {
  return name.startsWith(DOC_DIR) && !CTL_CHARS.test(name);
}
// 워크플로가 스크립트 전에 고정으로 「돈다」 로 정하는 머리(자체시험 · 워크플로 읽기 확인용으로 함께 둔다)
const FIXED_RUN_HEADS = ['scripts/playwright-audit/ci/', '.github/workflows/'];

// 순수 함수 — 파일 시스템 0. 자체시험이 직접 부른다.
function decideRun(names) {
  if (!Array.isArray(names)) return { run: true, why: '돈다 — 바뀐 파일 목록 꼴 이상(목록 못 구함)' };
  const list = names.map((n) => String(n)).filter((n) => n.length > 0);
  if (list.length === 0) return { run: true, why: '돈다 — 바뀐 파일 목록 비었음(목록 못 구함)' };
  // ⬛ [낡은 줄 · 갈래 A 첫 꼴] 범주별(화면 · API · 출하 DDL · 장치 · 워크플로) 수 · 하나라도 걸리면 돈다
  const docs = list.filter(isDoc).length;
  const fixed = list.filter((n) => FIXED_RUN_HEADS.some((h) => n.startsWith(h))).length;
  const other = list.length - docs;
  const run = other > 0;
  return { run, why: `${run ? '돈다' : '안 돈다'} — 바뀐 파일 ${list.length} · 문서 ${docs} · 문서 밖 ${other}(장치·워크플로 ${fixed})` };
}

// ⬛ [낡은 줄 · 3V-1] NUL 구분이 기본. 줄바꿈도 끊는다 — 이름이 쪼개지면 조각이 문서 꼴이 아닐 수 있어 「돈다」 쪽만 늘어난다.
// ⬛ [낡은 줄 · 3V-1] function parseList(text) {
// ⬛ [낡은 줄 · 3V-1]   return String(text).split(/[\0\r\n]+/).filter((n) => n.length > 0);
// ⬛ [낡은 줄 · 3V-1] }
//   └ [4] 1차 P2-2 반영(세부 = 로컬 기록).
// 🔴 PM 결정 4V-1 ② — NUL 로만 나눈다. 줄바꿈은 이름의 일부(→ isDoc 이 제어 글자로 문서 아님 = 돈다).
function parseList(text) {
  return String(text).split('\0').filter((n) => n.length > 0);
}

function main() {
  let d;
  try {
    d = decideRun(parseList(fs.readFileSync(0, 'utf8')));
  } catch (e) {
    d = { run: true, why: `돈다 — 바뀐 파일 목록 읽기 실패(${e && e.name ? e.name : 'Error'})` };
  }
  process.stdout.write(`run=${d.run ? 'true' : 'false'}\n`);
  process.stdout.write(`거르기: ${d.why}\n`);
}

if (require.main === module) main();
// ⬛ [낡은 줄 · 갈래 A 첫 꼴] module.exports = { RUN_HEADS, decideRun, parseList };
// ⬛ [낡은 줄 · 3V-1] module.exports = { DOC_DIR, DOC_EXT, FIXED_RUN_HEADS, isDoc, decideRun, parseList };
module.exports = { DOC_DIR, DOC_EXT, CTL_CHARS, FIXED_RUN_HEADS, isDoc, decideRun, parseList };
