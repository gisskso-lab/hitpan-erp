// G1-3 갈래 H — 가림 1회 측정 (보안상무 조건 ⑫ⓓ)
//
// 일반 node 가 표준출력에 `::add-mask::` 를 쓴 뒤 같은 값을 다시 찍는다.
// 런 로그에서 둘째 줄이 `가림 측정: ***` 로 보이면 가림이 먹은 것이다 — **사람이 런 로그로 읽는다**
// (러너가 로그를 가리는 것은 출력 뒤라 이 스크립트 스스로는 판정할 수 없다).
// 값은 비자격증명 표지 FAKE_MASK_PROBE_* · 누출 검사 심은 값들과 서로 부분 문자열이 아니다.
'use strict';

const crypto = require('crypto');
const L = require('./lib');

const probe = `FAKE_MASK_PROBE_${crypto.randomBytes(6).toString('hex')}`;
L.mask(probe);
process.stdout.write(`가림 측정: ${probe}\n`);
process.stdout.write('가림 측정: 위 줄이 *** 이면 가림 정상 · 값이 보이면 [장치] 빨강으로 기록(사람 판독)\n');
