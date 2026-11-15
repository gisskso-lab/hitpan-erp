'use strict';
// 자체시험 [장치] 누출 · 가림 — 설계 §4-5
//   사용: node selftest-leak.js [--out <결과 JSON>]
//   심은 꼴(긴 토큰 · 러너 경로 · 자격증명 꼴 · 금지어)을 파일 내용 · 파일 이름 양쪽에 → 꼴마다 「잡음」이어야 정상
//   대조군(SHA 16진 · 짧은 낱말 · 가림 표지 · 허용 고정 이름)은 「안 잡음」이어야 정상
//   심은 값은 이 런 안에서 난수로 만든다 · 원문 · 출력 · 결과 파일에 값 0 (꼴 종류 · 참/거짓만)
//   종료 코드: 0 = 전부 기대대로 · 1 = 하나라도 다름
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');

const HERE = __dirname;
const LEAK = path.join(HERE, 'leak-scan.js');
const MASK = path.join(HERE, 'mask.js');
const J = (...a) => a.join('');
const C = (...n) => String.fromCharCode(...n);

const B62 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
function rnd(n, alphabet) {
  const a = alphabet || B62;
  const bytes = crypto.randomBytes(n);
  let s = '';
  for (let i = 0; i < n; i++) s += a[bytes[i] % a.length];
  return s;
}
// 16진으로 오인되지 않게 g~z 글자를 하나 섞는다
function rndNonHex(n, alphabet) { return rnd(n - 1, alphabet) + rnd(1, 'ghijklmnopqrstuvwxyz'); }
const hex = (n) => crypto.randomBytes(n / 2).toString('hex');

const runnerTemp = '/tmp/st-' + rndNonHex(12, 'abcdefghijklmnopqrstuvwxyz');
const ENV = Object.assign({}, process.env, { RUNNER_TEMP: runnerTemp });

const V = {
  token: rndNonHex(28, B62 + '-_'),
  ghTok: J('gh', 'p', '_', rndNonHex(36)),
  cloudId: J('AK', 'IA', rnd(16, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567')),
  keyHead: J('-----BEGIN ', 'RSA ', 'PRI', 'VATE KEY', '-----'),
  jwt: J('e', 'yJ', rndNonHex(20), '.', 'e', 'yJ', rndNonHex(30), '.', rndNonHex(30)),
  bearer: J('Bear', 'er ', rndNonHex(32)),
  assign: J('pass', 'word=', rndNonHex(12)),
  w1: C(67, 108, 97, 117, 100, 101),
  w2: C(67, 117, 114, 115, 111, 114),
  w3: C(65, 73),
  envPath: runnerTemp + '/x/' + rnd(6, 'abcdefgh') + '.txt',
  genPath: J('/home/', 'runner', '/work/', rnd(8, 'abcdefgh')),
};

// [사례 이름, 파일들 {이름: 내용}, 기대 꼴(빈 배열 = 안 잡혀야)]
const CASES = [
  ['content-long-token', { 'a.json': JSON.stringify({ v: V.token }) }, ['long-token']],
  ['content-runner-path-env', { 'a.txt': 'at ' + V.envPath + '\n' }, ['runner-path']],
  ['content-runner-path-generic', { 'a.txt': 'cwd ' + V.genPath + '\n' }, ['runner-path']],
  ['content-cred-gh', { 'a.txt': 'x ' + V.ghTok + ' y\n' }, ['credential', 'long-token']],
  ['content-cred-cloud-id', { 'a.txt': 'id ' + V.cloudId + '\n' }, ['credential']],
  ['content-cred-key-header', { 'a.txt': V.keyHead + '\n' }, ['credential']],
  ['content-cred-jwt', { 'a.jsonl': JSON.stringify({ h: V.jwt }) + '\n' }, ['credential', 'long-token']],
  ['content-cred-bearer', { 'a.txt': 'auth ' + V.bearer + '\n' }, ['credential', 'long-token']],
  ['content-cred-assign', { 'a.txt': V.assign + '\n' }, ['credential']],
  ['content-banned-1', { 'a.txt': 'made with ' + V.w1 + ' here\n' }, ['banned-word']],
  ['content-banned-2', { 'a.txt': 'made with ' + V.w2 + ' here\n' }, ['banned-word']],
  ['content-banned-3', { 'a.txt': 'made by ' + V.w3 + ' here\n' }, ['banned-word']],
  ['name-long-token', { [V.token + '.json']: '{}' }, ['long-token']],
  ['name-cred-gh', { [V.ghTok + '.txt']: 'ok\n' }, ['credential', 'long-token']],
  ['name-banned', { ['note-' + V.w1 + '.txt']: 'ok\n' }, ['banned-word']],
  ['name-subdir-token', { [path.join('shots', V.token + '.png')]: crypto.randomBytes(64) }, ['long-token']],
  ['name-bad-extension', { 'data.log': 'ok\n' }, ['extension']],
  ['control-clean', {
    'run.json': JSON.stringify({ sha40: hex(40), sha64: hex(64), masked: '<가림:3>', path: '<러너>/x', asset: 'hqa-s4-fixture-linux-x64-01161edb', tokens_replaced: 5, css: 'cursor: pointer', ver: '151.0.7922.34' }),
    'SHA256SUMS.raw': hex(64) + '  run.json\n',
    [path.join('shots', 'a1.png')]: crypto.randomBytes(64),
  }, []],
];

function writeCase(dir, files) {
  for (const [name, body] of Object.entries(files)) {
    const p = path.join(dir, name);
    fs.mkdirSync(path.dirname(p), { recursive: true });
    fs.writeFileSync(p, body);
  }
}
const planted = Object.values(V);
const leaksValue = (s) => planted.some((v) => v.length >= 6 && s.includes(v));

function runLeak(dir, extra) {
  const r = spawnSync(process.execPath, [LEAK, dir, '--json'].concat(extra || []), { env: ENV, encoding: 'utf8' });
  let parsed = null;
  const line = (r.stdout || '').trim().split('\n').pop();
  if (line) { try { parsed = JSON.parse(line); } catch (e) { parsed = { parse_error: String(e.message).slice(0, 100) }; } }
  return { status: r.status, parsed, echoed: leaksValue((r.stdout || '') + (r.stderr || '')) };
}

const results = [];
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'hqa-st-'));
try {
  CASES.forEach(([name, files, expect], i) => {
    const dir = path.join(root, 'c' + i);
    fs.mkdirSync(dir);
    writeCase(dir, files);
    const r = runLeak(dir);
    const kinds = r.parsed && Array.isArray(r.parsed.files) ? [...new Set(r.parsed.files.flatMap((f) => f.kinds))].sort() : null;
    const wantStatus = expect.length ? 2 : 0;
    const pass = r.status === wantStatus && kinds !== null && JSON.stringify(kinds) === JSON.stringify([...expect].sort()) && !r.echoed;
    results.push({ case: name, expect, kinds, status: r.status, echoed_value: r.echoed, pass });
  });

  // --remove · withheld.json: 이름이 걸린 파일은 지워지고 withheld.json 에 값 0
  {
    const dir = path.join(root, 'remove');
    fs.mkdirSync(dir);
    writeCase(dir, { [V.ghTok + '.txt']: 'ok\n', 'keep.json': '{}' });
    const r = runLeak(dir, ['--remove', '--withheld-write', '--stage', 'measure']);
    const left = fs.readdirSync(dir).sort();
    const w = fs.existsSync(path.join(dir, 'withheld.json')) ? fs.readFileSync(path.join(dir, 'withheld.json'), 'utf8') : '';
    const pass = r.status === 2 && JSON.stringify(left) === JSON.stringify(['keep.json', 'withheld.json']) && w.includes('<이름 가림:1>') && !leaksValue(w) && !r.echoed;
    results.push({ case: 'remove-and-withheld', expect: ['removed', 'name masked'], kinds: null, status: r.status, echoed_value: r.echoed, pass });
  }

  // 가림 왕복: mask.js 뒤에는 긴 토큰 · 러너 경로가 안 남고 SHA 는 남는다
  {
    const dir = path.join(root, 'mask');
    fs.mkdirSync(dir);
    const sha = hex(40);
    writeCase(dir, { 'run.json': '{}\n', 'm4-stdout.txt': 'root ' + V.envPath + ' id ' + V.token + ' sha ' + sha + ' again ' + V.token + '\n' });
    const m = spawnSync(process.execPath, [MASK, dir], { env: ENV, encoding: 'utf8' });
    const after = fs.readFileSync(path.join(dir, 'm4-stdout.txt'), 'utf8');
    const runJ = JSON.parse(fs.readFileSync(path.join(dir, 'run.json'), 'utf8'));
    const r = runLeak(dir);
    const pass = m.status === 0 && r.status === 0 && after.includes(sha) && !after.includes(V.token) && !after.includes(runnerTemp)
      && (after.match(/<가림:1>/g) || []).length === 2 && after.includes('<러너>') && runJ.mask && runJ.mask.distinct_tokens === 1
      && !leaksValue(m.stdout + m.stderr) && !r.echoed;
    results.push({ case: 'mask-roundtrip', expect: ['token masked', 'path masked', 'sha kept'], kinds: r.parsed ? r.parsed.files : null, status: r.status, echoed_value: r.echoed, pass });
  }
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}

const ok = results.length === CASES.length + 2 && results.every((r) => r.pass);
const doc = { ok, cases: results.length, failed: results.filter((r) => !r.pass).map((r) => r.case), results };
const oi = process.argv.indexOf('--out');
if (oi >= 0 && process.argv[oi + 1]) fs.writeFileSync(process.argv[oi + 1], JSON.stringify(doc, null, 2) + '\n');
for (const r of results) console.log((r.pass ? '잡음/기대대로 ' : '다름 ') + r.case);
console.log('[자체시험 누출] ' + (ok ? '통과' : '실패') + ' ' + results.filter((r) => r.pass).length + '/' + results.length);
process.exit(ok ? 0 : 1);
