// G1-3 갈래 H — S8 K 본문 실행기 (작업지시서 20261106작 F-PR §4 C1 · N1)
//
// 🔴 이 파일의 규칙
//   ① 고르는 규칙 = 판정기(judge.js)의 kBodyScan 하나뿐 — 셸 글롭과 판정기 정규식이 따로 있던 자리(N1)를 한 함수로 묶는다.
//   ② 이상한 이름이 하나라도 있으면 **아무것도 돌리지 않고** 실패(종료 코드 1) · 이름은 찍지 않고 수만 찍는다.
//   ③ 정상 이름은 하나씩 돌린다 · 하나라도 실패면 끝까지 돈 뒤 실패. 표준출력은 그대로 흘린다(tee 0 · 업로드 폴더에 쓰기 0).
//   ④ 인자 `--dir <폴더>` 는 자체시험용(기본 = 이 폴더). 판정기에는 폴더 인자를 주지 않는다(판정 입력면 늘리지 않음).
'use strict';

const path = require('path');
const { spawnSync } = require('child_process');
const L = require('./lib');
const { kBodyScan } = require('./judge');

function targetDir(argv) {
  const i = argv.indexOf('--dir');
  if (i < 0) return __dirname;
  const v = argv[i + 1];
  if (!v) {
    console.log('⚫ FAIL [장치] K 본문 실행기 — --dir 뒤 폴더 없음');
    process.exit(1);
  }
  return path.resolve(v);
}

function main() {
  const dir = targetDir(process.argv.slice(2));
  const { run, odd } = kBodyScan(dir);
  if (odd.length > 0) {
    console.log(`⚫ FAIL [장치] K 본문 이름 꼴 이상 ${odd.length}개 — 아무것도 안 돌린다(이름은 판정기 줄에서 소독 뒤)`);
    process.exit(1);
  }
  console.log(`K 본문 실행기: 실행 ${run.length}개 · 이상 0`);
  let failed = 0;
  for (const f of run) {
    console.log(`── ${f}`);
    const x = spawnSync(process.execPath, [path.join(dir, f)], { stdio: 'inherit' });
    if (x.status !== 0) {
      failed += 1;
      const how = x.status === null ? `신호 ${x.signal || '-'}` : `종료 코드 ${x.status}`;
      console.log(`⚫ ${f} ${how}${x.error ? ` · 실행 오류 ${L.errorName(x.error)}` : ''}`);
    }
  }
  process.exit(failed > 0 ? 1 : 0);
}

if (require.main === module) main();
module.exports = { targetDir };
