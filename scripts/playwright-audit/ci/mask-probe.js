// G1-3 갈래 H — 가림 1회 측정 (보안상무 조건 ⑫ⓓ)
//
// 일반 node 가 표준출력에 `::add-mask::` 를 쓴 뒤 같은 값을 다시 찍는다.
// 런 로그에서 둘째 줄이 `가림 측정: ***` 로 보이면 가림이 먹은 것이다 — **사람이 런 로그로 읽는다**
// (러너가 로그를 가리는 것은 출력 뒤라 이 스크립트 스스로는 판정할 수 없다).
// 값은 비자격증명 표지 FAKE_MASK_PROBE_* · 누출 검사 심은 값들과 서로 부분 문자열이 아니다.
//
// 🔴 7차(10/29 · 작업지시서 §0-11-11-3) 순서
//   ① M4 실값 유입 검사 — 체크아웃된 .github/workflows 중 이 장치를 부르는 파일에 비밀값·입력 칸·러너 밖 주소가 있나.
//      잡힘 또는 대상 0 → 종료 코드 1 · 바로 끝(M1 생략 — 실값이 흐를 수 있는 런에서 더 찍지 않는다).
//   CI 밖(GITHUB_ACTIONS ≠ true)이면 ②③ 대신 고정 문구 한 줄(가짜라도 꼴 출력 0).
//   ② 지금 3줄 그대로(원문 가림 측정).
//   ③ M1 — 표지 FAKE_MASK_M1_* 를 가린 뒤 꼴별 줄(lib.maskProbeLines)을 찍는다 · 대조 FAKE_MASK_CTRL_* 는 가리지 않는다.
//      판정은 하지 않는다(사람이 런 로그로) — 종료 코드는 M4 와 측정 성립 검사(throw)만 정한다.
'use strict';

const crypto = require('crypto');
const path = require('path'); // 7차 M4
const L = require('./lib');

// ① M4 — 읽는 곳 = 체크아웃된 워크플로 폴더(지금 도는 워크플로 파일 그 자체)
const intake = L.intakeScan(path.resolve(__dirname, '../../../.github/workflows'));
for (const line of intake.lines) process.stdout.write(`${line}\n`);
if (!intake.ok) process.exit(1);

if (process.env.GITHUB_ACTIONS !== 'true') {
  process.stdout.write('가림 측정 생략 — CI 밖\n');
  process.exit(0);
}

// ② 지금 3줄 그대로

const probe = `FAKE_MASK_PROBE_${crypto.randomBytes(6).toString('hex')}`;
L.mask(probe);
process.stdout.write(`가림 측정: ${probe}\n`);
process.stdout.write('가림 측정: 위 줄이 *** 이면 가림 정상 · 값이 보이면 [장치] 빨강으로 기록(사람 판독)\n');

// ③ M1 — 표지 `#`(%23)·`/`(%2F) = url 대·소문자 꼴이 갈린다 · 공백 0 · 실행 중 16진
const m1Probe = `FAKE_MASK_M1_${crypto.randomBytes(6).toString('hex')}#/${crypto.randomBytes(4).toString('hex')}`;
const m1Control = `FAKE_MASK_CTRL_${crypto.randomBytes(6).toString('hex')}#/${crypto.randomBytes(4).toString('hex')}`;
const m1Lines = L.maskProbeLines(m1Probe, m1Control); // 측정 성립 검사 — 못 재면 throw → 종료 코드 ≠ 0 → S0 빨강
L.mask(m1Probe); // 대조 값은 가리지 않는다
for (const line of m1Lines) process.stdout.write(`${line}\n`);
