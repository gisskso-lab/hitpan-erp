// K 연결(11/6 · 작업지시서 20261106작 #691 재기반 §5-4 · PM 결재 A3 ㉯ · A9 · C3 · C4 · 대표 Q1 「화면·API 바뀐 PR만」)
// ui-k35 잡을 돌릴지 정하는 거르기 규칙 — 순수 함수(decideRun) + 얇은 실행 껍데기.
//
//   입력  = 표준입력 · 바뀐 파일 이름 목록(NUL 구분 · 워크플로가 git diff -z 로 만든다 · 이름 따옴표 꼴 0)
//   출력  = 표준출력 `run=true` 또는 `run=false` 한 줄 + 이유 한 줄(범주별 수만 · 파일 이름 글자 0)
//   닫힌 쪽 = 도는 쪽 — 빈 목록 · 읽기 오류 · 입력 꼴 이상 = run=true. 수동 실행은 워크플로 조건이 따로 돌린다.
//   외부 코드 0 · 네트워크 0 · 파일 쓰기 0.
//
// 이 파일 이름은 K 본문 머리로 시작하지 않는다 — 실행기(run-k-bodies.js)가 K 본문으로 돌리지 않는다.
// 넓힘 경로를 바꾸려면 PM 먼저(작업지시서 PM 조건 C3).
'use strict';

const fs = require('fs');

// 폴더 머리는 `/` 로 끝난다(머리 일치) · 그 밖은 파일 하나(정확히 같음).
const RUN_HEADS = [
  ['화면', ['src/HitPan.Web/']],
  // API 와 API 가 참조하는 프로젝트(HitPan.API.csproj · Application.csproj · Infrastructure.csproj 참조 읽기) + 뿌리 빌드 설정
  ['API', [
    'src/HitPan.API/', 'src/HitPan.Application/', 'src/HitPan.Infrastructure/', 'src/HitPan.Domain/',
    'src/HitPan.Contracts/', 'src/HitPan.Analyzers/', 'Directory.Build.props',
  ]],
  ['출하 DDL', ['installer/hitpan_db_clean.sql']], // 장치 시드 단계가 싣는다(PM A9)
  ['장치', ['scripts/playwright-audit/ci/']],
  ['워크플로', ['.github/workflows/ui-browser.yml']],
];

function headHit(name, head) {
  return head.endsWith('/') ? name.startsWith(head) : name === head;
}

// 순수 함수 — 파일 시스템 0. 자체시험이 직접 부른다.
function decideRun(names) {
  if (!Array.isArray(names)) return { run: true, why: '돈다 — 바뀐 파일 목록 꼴 이상(목록 못 구함)' };
  const list = names.map((n) => String(n)).filter((n) => n.length > 0);
  if (list.length === 0) return { run: true, why: '돈다 — 바뀐 파일 목록 비었음(목록 못 구함)' };
  const counts = new Map(RUN_HEADS.map(([kind]) => [kind, 0]));
  let other = 0;
  for (const n of list) {
    const hit = RUN_HEADS.find(([, heads]) => heads.some((h) => headHit(n, h)));
    if (hit) counts.set(hit[0], counts.get(hit[0]) + 1);
    else other += 1;
  }
  const hits = [...counts.values()].reduce((a, b) => a + b, 0);
  const parts = [...counts].map(([kind, n]) => `${kind} ${n}`).join(' · ');
  return { run: hits > 0, why: `${hits > 0 ? '돈다' : '안 돈다'} — 바뀐 파일 ${list.length} · ${parts} · 그 밖 ${other}` };
}

// NUL 구분이 기본. 줄바꿈도 끊는다 — 이름이 쪼개져도 머리 쪽 조각은 그대로 남아 「돈다」 쪽만 늘어난다.
function parseList(text) {
  return String(text).split(/[\0\r\n]+/).filter((n) => n.length > 0);
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
module.exports = { RUN_HEADS, decideRun, parseList };
