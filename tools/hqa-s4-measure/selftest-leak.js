'use strict';
// 자체시험 [장치] 누출 · 가림 — 설계 §4-5 · 고침1 F3 · F4 · F5 · F6 · F11
//   사용: node selftest-leak.js [--out <결과 JSON>]
//   심은 꼴(긴 토큰 · 표준 base64 · 문맥 없는 16진 40/64 · 러너 경로 · 자격증명 꼴(JSON 키-값 포함) · 금지어 · PNG 머리 · PNG 텍스트 덩어리)
//   을 파일 내용 · 파일 이름 양쪽에 → 꼴마다 「잡음」이어야 정상
//   대조군(알려진 칸의 SHA 16진 · 짧은 낱말 · 가림 표지 · 허용 고정 이름 · 경로 · 주소 · 정상 PNG)은 「안 잡음」이어야 정상
//   심은 값은 이 런 안에서 난수로 만든다 · 원문 · 출력 · 결과 파일에 값 0 (꼴 종류 · 참/거짓만)
//   종료 코드: 0 = 전부 기대대로 · 1 = 하나라도 다름
const fs = require('fs');
const os = require('os');
const path = require('path');
const zlib = require('zlib');
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
// 표준 base64 꼴 — 영숫자 덩어리가 22자에 못 미치게 '/' '+' 로 끊고 '=' 로 끝낸다(옛 꼴로는 안 걸리는 값)
function b64std() {
  const part = () => rnd(1, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') + rnd(1, 'abcdefghijklmnopqrstuvwxyz') + rnd(1, '0123456789') + rnd(11);
  return part() + '/' + part() + '+' + rnd(6) + '=';
}

// 최소 PNG(1×1) — opt.text = tEXt 덩어리 · opt.trailing = IEND 뒤 덧붙임
const CRC_T = (() => { const t = []; for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
const crc32 = (b) => { let c = 0xffffffff; for (const x of b) c = CRC_T[(c ^ x) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
function png(opt) {
  const o = opt || {};
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(1, 0); ihdr.writeUInt32BE(1, 4); ihdr[8] = 8; ihdr[9] = 0;
  const parts = [Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr)];
  if (o.text) parts.push(chunk(o.text, Buffer.from('Comment\0' + rnd(8), 'latin1')));
  parts.push(chunk('IDAT', zlib.deflateSync(Buffer.from([0, 0]))), chunk('IEND', Buffer.alloc(0)));
  if (o.trailing) parts.push(crypto.randomBytes(16));
  return Buffer.concat(parts);
}

const runnerTemp = '/tmp/st-' + rndNonHex(12, 'abcdefghijklmnopqrstuvwxyz');
const knownSha = hex(40);
const ENV = Object.assign({}, process.env, { RUNNER_TEMP: runnerTemp, GITHUB_SHA: knownSha });

const V = {
  longTok: rndNonHex(28, B62 + '-_'),
  ghTok: J('gh', 'p', '_', rndNonHex(36)),
  cloudId: J('AK', 'IA', rnd(16, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567')),
  keyHead: J('-----BEGIN ', 'RSA ', 'PRI', 'VATE KEY', '-----'),
  jwt: J('e', 'yJ', rndNonHex(20), '.', 'e', 'yJ', rndNonHex(30), '.', rndNonHex(30)),
  bearer: J('Bear', 'er ', rndNonHex(32)),
  assign: J('pass', 'word=', rndNonHex(12)),
  kvVal1: rndNonHex(12),
  kvVal2: rndNonHex(14),
  kvVal3: rndNonHex(18),
  b64: b64std(),
  hex64: hex(64),
  hex40: hex(40),
  w1: C(67, 108, 97, 117, 100, 101),
  w2: C(67, 117, 114, 115, 111, 114),
  w3: C(65, 73),
  envPath: runnerTemp + '/x/' + rnd(6, 'abcdefgh') + '.txt',
  genPath: J('/home/', 'runner', '/work/', rnd(8, 'abcdefgh')),
};
const K1 = J('pass', 'word');
const K2 = J('access', 'Tok', 'en');
const K3 = J('client_', 'sec', 'ret');

// [사례 이름, 파일들 {이름: 내용}, 기대 꼴(빈 배열 = 안 잡혀야)]
const CASES = [
  ['content-long-token', { 'a.json': JSON.stringify({ v: V.longTok }) }, ['long-token']],
  ['content-runner-path-env', { 'a.txt': 'at ' + V.envPath + '\n' }, ['runner-path']],
  ['content-runner-path-generic', { 'a.txt': 'cwd ' + V.genPath + '\n' }, ['runner-path']],
  ['content-cred-gh', { 'a.txt': 'x ' + V.ghTok + ' y\n' }, ['credential', 'long-token']],
  ['content-cred-cloud-id', { 'a.txt': 'id ' + V.cloudId + '\n' }, ['credential']],
  ['content-cred-key-header', { 'a.txt': V.keyHead + '\n' }, ['credential']],
  ['content-cred-jwt', { 'a.jsonl': JSON.stringify({ h: V.jwt }) + '\n' }, ['credential', 'long-token']],
  ['content-cred-bearer', { 'a.txt': 'auth ' + V.bearer + '\n' }, ['credential', 'long-token']],
  ['content-cred-assign', { 'a.txt': V.assign + '\n' }, ['credential']],
  // F3 — JSON 키-값 꼴(22자 미만이라 긴 토큰 꼴로는 안 걸리는 값)
  ['content-cred-json-kv', { 'a.json': JSON.stringify({ [K1]: V.kvVal1 }) }, ['credential']],
  ['content-cred-json-kv-camel', { 'a.jsonl': JSON.stringify({ [K2]: V.kvVal2 }) + '\n' }, ['credential']],
  ['content-cred-json-kv-snake', { 'a.json': JSON.stringify({ [K3]: V.kvVal3 }, null, 2) }, ['credential']],
  // F4 — 표준 base64(+ / =)
  ['content-b64-text', { 'a.txt': 'v ' + V.b64 + '\n' }, ['long-token']],
  ['content-b64-json', { 'a.json': JSON.stringify({ v: V.b64 }) }, ['long-token']],
  // F5 — 알려진 칸 밖 16진
  ['content-hex64-unkeyed', { 'a.json': JSON.stringify({ k: V.hex64 }) }, ['long-token']],
  ['content-hex40-text', { 'a.txt': 'id ' + V.hex40 + '\n' }, ['long-token']],
  ['content-banned-1', { 'a.txt': 'made with ' + V.w1 + ' here\n' }, ['banned-word']],
  ['content-banned-2', { 'a.txt': 'made with ' + V.w2 + ' here\n' }, ['banned-word']],
  ['content-banned-3', { 'a.txt': 'made by ' + V.w3 + ' here\n' }, ['banned-word']],
  ['name-long-token', { [V.longTok + '.json']: '{}' }, ['long-token']],
  ['name-cred-gh', { [V.ghTok + '.txt']: 'ok\n' }, ['credential', 'long-token']],
  ['name-banned', { ['note-' + V.w1 + '.txt']: 'ok\n' }, ['banned-word']],
  ['name-subdir-token', { [path.join('shots', V.longTok + '.png')]: png() }, ['long-token']],
  ['name-hex64', { [V.hex64 + '.txt']: 'ok\n' }, ['long-token']],
  ['name-bad-extension', { 'data.log': 'ok\n' }, ['extension']],
  // F6 — PNG 는 이름이 아니라 머리 바이트 · 텍스트 덩어리 0
  ['png-bad-signature', { [path.join('shots', 'x1.png')]: crypto.randomBytes(64) }, ['png-signature']],
  ['png-text-chunk', { [path.join('shots', 'x2.png')]: png({ text: 'tEXt' }) }, ['png-text']],
  ['png-itxt-chunk', { [path.join('shots', 'x3.png')]: png({ text: 'iTXt' }) }, ['png-text']],
  ['png-ztxt-chunk', { [path.join('shots', 'x4.png')]: png({ text: 'zTXt' }) }, ['png-text']],
  ['png-trailing', { [path.join('shots', 'x5.png')]: png({ trailing: true }) }, ['png-structure']],
  ['control-clean', {
    'run.json': JSON.stringify({
      sha: hex(40), head: hex(40), device_commit: knownSha, sha256: hex(64), tree_blob: hex(40),
      detail: 'got=' + hex(64) + ' expected=' + hex(64) + ' release=' + hex(64),
      mode_line: 'armed:' + hex(40) + ':r1', fixed: 'tree=0b31f8692367ae28a6fac255d8eae5c931066d7b',
      masked: '<가림:3>', path: '<러너>/x', asset: 'hqa-s4-fixture-linux-x64-01161edb', tokens_replaced: 5, distinct_tokens: 2,
      css: 'cursor: pointer', ver: '151.0.7922.34', kinds: ['long-token'],
      src: 'Components/After/CsStatusCell.razor', css2: '/css/common/backoffice-v2.css', mud: '/_content/MudBlazor/MudBlazor.min.js',
      url: 'https://github.com/gisskso-lab/hitpan-erp/releases/download/hqa-s4-fixture-01161edb',
      sel: '#components-reconnect-modal', apt: '--no-install-recommends', key: 'expectedBrowserVersion',
    }),
    'm4-stdout.txt': 'commit ' + knownSha + ' ok\n',
    'SHA256SUMS.raw': hex(64) + '  run.json\n' + hex(64) + '  m4-stdout.txt\n',
    [path.join('shots', 'a1.png')]: png(),
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

  // 가림 왕복: mask.js 뒤에는 긴 토큰 · base64 · 문맥 없는 16진 · 러너 경로가 안 남고 알려진 칸의 SHA 는 남는다
  {
    const dir = path.join(root, 'mask');
    fs.mkdirSync(dir);
    const sha = hex(40);
    writeCase(dir, { 'run.json': '{}\n', 'm4-stdout.txt': 'root ' + V.envPath + ' id ' + V.longTok + ' tree=' + sha + ' again ' + V.longTok + ' b ' + V.b64 + ' x ' + V.hex64 + '\n' });
    const m = spawnSync(process.execPath, [MASK, dir], { env: ENV, encoding: 'utf8' });
    const after = fs.readFileSync(path.join(dir, 'm4-stdout.txt'), 'utf8');
    const runJ = JSON.parse(fs.readFileSync(path.join(dir, 'run.json'), 'utf8'));
    const r = runLeak(dir);
    const pass = m.status === 0 && r.status === 0 && after.includes(sha) && !after.includes(V.longTok) && !after.includes(runnerTemp)
      && !after.includes(V.b64) && !after.includes(V.hex64)
      && (after.match(/<가림:1>/g) || []).length === 2 && after.includes('<러너>') && runJ.mask && runJ.mask.distinct_tokens === 3
      && !leaksValue(m.stdout + m.stderr) && !r.echoed;
    results.push({ case: 'mask-roundtrip', expect: ['token masked', 'b64 masked', 'unkeyed hex masked', 'path masked', 'keyed sha kept'], kinds: r.parsed ? r.parsed.files : null, status: r.status, echoed_value: r.echoed, pass });
  }
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}

const ok = results.length === CASES.length + 2 && results.every((r) => r.pass);
const doc = { ok, cases: results.length, failed: results.filter((r) => !r.pass).map((r) => r.case), results };
const oi = process.argv.indexOf('--out');
if (oi >= 0 && process.argv[oi + 1]) fs.writeFileSync(process.argv[oi + 1], JSON.stringify(doc, null, 2) + '\n');
for (const r of results) console.log((r.pass ? '잡음/기대대로 ' : '다름 ') + r.case + (r.pass ? '' : ' kinds=' + JSON.stringify(r.kinds) + ' status=' + r.status));
console.log('[자체시험 누출] ' + (ok ? '통과' : '실패') + ' ' + results.filter((r) => r.pass).length + '/' + results.length);
process.exit(ok ? 0 : 1);
