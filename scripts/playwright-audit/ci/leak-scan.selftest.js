// G1-3 갈래 H — S10 누출 검사 단독 시험 (DB·API·브라우저 없이 · 가짜 폴더만)
//
// 사용: node leak-scan.selftest.js <빈 작업 폴더>
// 경우 7개 — 기대 종료 코드·출력으로 판정(대조군 포함 · 「항상 빨강」/「항상 초록」 아님을 같이 본다).
//   C1 깨끗한 폴더 → 0(초록)                         ← 대조군
//   C2 S0 값 하나 비움 → 1 · 「대상 비었음」           ← 보안상무 ⑩
//   C3 원문 누출(main) → 1 · main 지워짐
//   C4 base64 누출(시작 자리 1·2 밀림) → 1
//   C5 URL 인코딩 누출 → 1
//   C6 trace zip 안 resources/ 의 원문 누출 → 1 · trace 지워짐 · main 남음
//   C7 심은 값 3꼴(UI_LEAK_PLANT=1) → 1 · 꼴마다 「잡음」 3줄 · 「못 잡음」 0
//   C8 망가진 zip → 1 · 「못 푼 zip」
'use strict';

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const { makeZip, plantValues } = require('./leak-scan');

const work = process.argv[2];
if (!work) { console.log('사용: node leak-scan.selftest.js <빈 작업 폴더>'); process.exit(2); }
const SCAN = path.join(__dirname, 'leak-scan.js');

function fakeEnv() {
  return {
    UI_DB_PASSWORD: 'SELFTEST_DBPW_7f3a9c2e',
    UI_JWT_SECRET: 'SELFTEST_JWT_1b8d4e6f',
    UI_ENC_KEY: 'SELFTEST_ENC_9e2c7a1d',
    UI_BOOT_KEY: 'SELFTEST_BOOT_4a6f8b3c',
    UI_PW_PARENT: 'SELFTEST#PWP/5d2e+9a',
    UI_PW_GA: 'SELFTEST_PWGA_3c7e1f8b',
    UI_PW_NA: 'SELFTEST_PWNA_8a1d5c2e',
    UI_SECRET_TOKEN_ACCESS_NA_1_ABC123: 'SELFTEST_TOKEN_e4b7c9a2d1',
  };
}
function fresh(name) {
  const root = path.join(work, name);
  fs.rmSync(root, { recursive: true, force: true });
  fs.mkdirSync(path.join(root, 'main', 'shots'), { recursive: true });
  fs.mkdirSync(path.join(root, 'trace'), { recursive: true });
  fs.writeFileSync(path.join(root, 'main', 'ui-k35-result.json'), '{"rows":[]}');
  fs.writeFileSync(path.join(root, 'main', 'shots', 'a.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47, 1, 2, 3]));
  return root;
}
function run(root, envPatch = {}) {
  const base = { PATH: process.env.PATH, SystemRoot: process.env.SystemRoot };
  const env = { ...base, ...fakeEnv(), ...envPatch };
  for (const [k, v] of Object.entries(env)) if (v === undefined) delete env[k];
  const r = spawnSync(process.execPath, [SCAN, root], { env, encoding: 'utf8' });
  return { code: r.status, out: r.stdout + r.stderr };
}

const results = [];
function check(name, cond, detail) {
  results.push({ name, ok: !!cond });
  console.log(`${cond ? 'PASS' : 'FAIL'} ${name}${detail ? ` — ${detail}` : ''}`);
}

const E = fakeEnv();
{ // C1
  const root = fresh('c1');
  const r = run(root);
  check('C1 깨끗한 폴더 = 초록', r.code === 0 && /PASS 누출 0/.test(r.out), `code=${r.code}`);
}
{ // C2
  const root = fresh('c2');
  const r = run(root, { UI_PW_NA: undefined });
  check('C2 S0 값 비면 빨강(⑩)', r.code === 1 && /대상 비었음: UI_PW_NA/.test(r.out), `code=${r.code}`);
}
{ // C3
  const root = fresh('c3');
  fs.writeFileSync(path.join(root, 'main', 'api.log'), `conn pwd=${E.UI_DB_PASSWORD};`);
  const r = run(root);
  check('C3 원문 누출 잡음 · main 지움', r.code === 1 && /UI_DB_PASSWORD · 꼴=raw/.test(r.out) && !fs.existsSync(path.join(root, 'main')), `code=${r.code}`);
}
{ // C4
  const root = fresh('c4');
  const b1 = Buffer.from(`x${E.UI_JWT_SECRET}`).toString('base64');
  const b2 = Buffer.from(`xy${E.UI_ENC_KEY}z`).toString('base64').replace(/\+/g, '-').replace(/\//g, '_');
  fs.writeFileSync(path.join(root, 'main', 'b.txt'), `${b1}\n${b2}`);
  const r = run(root);
  check('C4 base64 누출(자리 1·2 · url 안전) 잡음', r.code === 1 && /UI_JWT_SECRET · 꼴=base64/.test(r.out) && /UI_ENC_KEY · 꼴=base64/.test(r.out), `code=${r.code}`);
}
{ // C5
  const root = fresh('c5');
  fs.writeFileSync(path.join(root, 'main', 'u.txt'), `GET /a?p=${encodeURIComponent(E.UI_PW_PARENT)}`);
  const r = run(root);
  check('C5 URL 인코딩 누출 잡음', r.code === 1 && /UI_PW_PARENT · 꼴=url/.test(r.out) && !/UI_PW_PARENT · 꼴=raw/.test(r.out), `code=${r.code}`);
}
{ // C6
  const root = fresh('c6');
  fs.writeFileSync(path.join(root, 'trace', 't.zip'), makeZip([
    { name: 'trace.trace', data: Buffer.from('{"type":"before"}') },
    { name: 'resources/abc.json', data: Buffer.from(`{"accessToken":"${E.UI_SECRET_TOKEN_ACCESS_NA_1_ABC123}"}`) },
  ]));
  const r = run(root);
  check('C6 trace zip 안 resources 잡음 · trace 만 지움', r.code === 1 && /resources\/abc\.json/.test(r.out)
    && !fs.existsSync(path.join(root, 'trace')) && fs.existsSync(path.join(root, 'main')), `code=${r.code}`);
}
{ // C7
  const root = fresh('c7');
  const r = run(root, { UI_LEAK_PLANT: '1' });
  const caught = (r.out.match(/심은 값 PLANT_[A-Z0-9]+: 꼴 [a-z0-9]+ 잡음/g) || []).length;
  check('C7 심은 값 3꼴 꼴마다 잡음', r.code === 1 && caught === 3 && !/못 잡음/.test(r.out) && /의도된 심은 빨강/.test(r.out), `code=${r.code} 잡음=${caught}`);
  check('C7b 출력에 심은 값 원문 0(가림 줄 제외)', !r.out.split('\n').some((l) => !l.startsWith('::add-mask::') && /FAKE_LEAK_[AB]_|FAKE LEAK\/C\+/.test(l)));
}
{ // C8
  const root = fresh('c8');
  fs.writeFileSync(path.join(root, 'trace', 'broken.zip'), Buffer.from('PK not a zip'));
  const r = run(root);
  check('C8 못 푼 zip = 빨강', r.code === 1 && /못 푼 zip/.test(r.out), `code=${r.code}`);
}
{ // ⑪ 서로 부분 문자열 아님 — 100회
  let ok = true;
  for (let i = 0; i < 100; i += 1) {
    const v = Object.values(plantValues());
    for (const a of v) for (const b of v) if (a !== b && (a.includes(b) || b.includes(a))) ok = false;
  }
  check('⑪ 심은 값 3개 서로 부분 문자열 아님(100회)', ok);
}

const bad = results.filter((x) => !x.ok).length;
console.log(`합계 ${results.length - bad}/${results.length} PASS`);
process.exit(bad === 0 ? 0 : 1);
