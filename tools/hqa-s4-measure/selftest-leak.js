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
  if (o.extra) for (const t of o.extra) parts.push(chunk(t, crypto.randomBytes(4)));
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
// 고침2 Y2 · Y1 · Y3 심은 값(런 안 난수 · 원문에 꼴 글자 0)
const LET = B62.slice(0, 52);
V.y2b = b64std();
V.y2nd = 'QZab' + rnd(8, LET) + '+' + rnd(8, LET) + '/' + rnd(8, LET); // 숫자 없는 base64(+ / 포함)
V.y1v = rndNonHex(10);
V.y1s = rnd(5, 'ghijkmnpqrstuvwxyz');
V.y3h = hex(40);
// 고침4 G7 심은 값 — 자동 id 꼴(판정 칸 밖) · 22자 토큰(판정 칸 안)
V.g7id = rnd(24, 'abcdefghijklmnopqrstuvwxyz0123456789');
V.g7tok = rndNonHex(22);
// 고침5 H2 심은 값 — 같은 길이 무작위 토큰(23 · 27 · 25 = 리허설 4 늘어난 3 의 길이 · 값 아님) · mud- + 22자 · verdict / 표본 토큰 · 무작위 64 16진
V.h2a = rndNonHex(23);
V.h2b = rndNonHex(27);
V.h2c = rndNonHex(25);
V.h2m = 'mud-' + rndNonHex(22, 'abcdefghijklmnopqrstuvwxyz0123456789');
V.h2x = rndNonHex(26);
V.h2y = rndNonHex(24);
V.h2r = hex(64);
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
  // 고침2 Y1 — 이스케이프된 JSON 키-값 · 5자 이하 값은 수용(기록 · 안 잡힘)
  ['y1-esc-json', { 'a.json': JSON.stringify(JSON.stringify({ [K1]: V.y1v })) }, ['credential']],
  ['y1-short-ok', { 'a.json': JSON.stringify({ [K2]: V.y1s }) }, []],
  // 고침2 Y3 — 16진 허용 칸은 장치 파일에서만
  ['y3-fixture-kv', { 'm4-stdout.txt': 'x-main=' + V.y3h + '\n' }, ['long-token']],
  ['y3-device-kv', { 'm1-bundle.json': JSON.stringify({ detail: 'got=' + V.y3h }) }, []],
  // 고침2 Y4 — PNG 덩어리 허용 목록
  ['y4-exif', { [path.join('shots', 'y1.png')]: png({ extra: ['eXIf'] }) }, ['png-chunk']],
  ['y4-iccp', { [path.join('shots', 'y2.png')]: png({ extra: ['iCCP'] }) }, ['png-chunk']],
  ['y4-private', { [path.join('shots', 'y3.png')]: png({ extra: ['prVt'] }) }, ['png-chunk']],
  ['y4-allowed', { [path.join('shots', 'y4.png')]: png({ extra: ['sRGB', 'gAMA', 'pHYs'] }) }, []],
  ['control-clean', {
    'run.json': JSON.stringify({
      sha: hex(40), head: hex(40), device_commit: knownSha, sha256: hex(64), tree_blob: hex(40),
      detail: 'got=' + hex(64) + ' expected=' + hex(64) + ' release=' + hex(64),
      mode_line: 'armed:' + hex(40) + ':r1', fixed: 'tree=47a909c8e7f7d11c89f455d8ff28ca2b9e8fc8e2',
      masked: '<가림:3>', path: '<러너>/x', asset: 'hqa-s4-fixture-linux-x64-88085da1', tokens_replaced: 5, distinct_tokens: 2,
      css: 'cursor: pointer', ver: '151.0.7922.34', kinds: ['long-token'],
      src: 'Components/After/CsStatusCell.razor', css2: '/css/common/backoffice-v2.css', mud: '/_content/MudBlazor/MudBlazor.min.js',
      url: 'https://github.com/gisskso-lab/hitpan-erp/releases/download/hqa-s4-fixture-88085da1',
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

// 고침2 Y2-c · Y2-d — 잡혀야 7(leak long-token · mask 가림 >= 1 · 결과 파일에 값 0) · 안 잡혀야 7(가림 0 · leak 0)
const Y2C = [
  ['y2.k.eq', 'k=' + V.y2b],
  ['y2.sig.amp', 'GET /x?sig=' + V.y2b + '&n=1'],
  ['y2.key.dot', 'key.' + V.y2b],
  ['y2.id.dash', 'id-' + V.y2b],
  ['y2.colon', 'v:' + V.y2b],
  ['y2.nodigit', 'v ' + V.y2nd],
  ['y2.esc.json', JSON.stringify(JSON.stringify({ v: V.y2b }))],
];
const Y2D = [
  ['y2n.css', 'GET /css/common/backoffice-v2.css 200'],
  ['y2n.content', 'GET /_content/MudBlazor/MudBlazor.min.js 200 /_content/MudBlazor/MudBlazor.min.css'],
  ['y2n.sel', 'sel .mud-popover.mud-popover-open #components-reconnect-modal .bo-status-chip [data-row="1"]'],
  ['y2n.shot', 'shots/N-1.before.d500.w1280h720.arrows.s5.n1-end.png'],
  ['y2n.hex', null],
  ['y2n.ver', 'chrome 151.0.7922.34'],
  ['y2n.runner', 'src ' + runnerTemp + '/hqa-work/Components/After/CsStatusCell.razor'],
];
// 고침2 I-a: 사례 이름은 22자 미만 · 점으로 잇기(원자료에 이름이 가림으로 지워지지 않게)
const shortName = (n) => n.replace(/^content-/, 'c.').replace(/^name-/, 'n.').replace(/-/g, '.');
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
    results.push({ case: shortName(name), expect, kinds, status: r.status, echoed_value: r.echoed, pass });
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
    results.push({ case: 'remove.withheld', expect: ['removed', 'name masked'], kinds: null, status: r.status, echoed_value: r.echoed, pass });
  }

  // 가림 왕복: mask.js 뒤에는 긴 토큰 · base64 · 문맥 없는 16진 · 러너 경로가 안 남고 알려진 칸의 SHA 는 남는다
  {
    const dir = path.join(root, 'mask');
    fs.mkdirSync(dir);
    const sha = hex(40);
    const sha2 = hex(40);
    // 고침2 Y3: 열쇠 붙은 SHA 는 장치 파일(m0-assert.json)에서만 남고 · 시험 앱 기록(m4-stdout) 안 x-main= 은 가림
    writeCase(dir, { 'run.json': '{}\n', 'm0-assert.json': JSON.stringify({ head: sha }) + '\n', 'm4-stdout.txt': 'root ' + V.envPath + ' id ' + V.longTok + ' x-main=' + sha2 + ' again ' + V.longTok + ' b ' + V.b64 + ' x ' + V.hex64 + '\n' });
    const m = spawnSync(process.execPath, [MASK, dir], { env: ENV, encoding: 'utf8' });
    const after = fs.readFileSync(path.join(dir, 'm4-stdout.txt'), 'utf8');
    const runJ = JSON.parse(fs.readFileSync(path.join(dir, 'run.json'), 'utf8'));
    const r = runLeak(dir);
    const pass = m.status === 0 && r.status === 0 && fs.readFileSync(path.join(dir, 'm0-assert.json'), 'utf8').includes(sha) && !after.includes(sha2) && !after.includes(V.longTok) && !after.includes(runnerTemp)
      && !after.includes(V.b64) && !after.includes(V.hex64)
      && (after.match(/<가림:1>/g) || []).length === 2 && after.includes('<러너>') && runJ.mask && runJ.mask.distinct_tokens === 4
      && !leaksValue(m.stdout + m.stderr) && !r.echoed;
    results.push({ case: 'mask-roundtrip', expect: ['token masked', 'b64 masked', 'unkeyed hex masked', 'path masked', 'keyed sha kept'], kinds: r.parsed ? r.parsed.files : null, status: r.status, echoed_value: r.echoed, pass });
  }

  const maskRun = (dir) => {
    const m = spawnSync(process.execPath, [MASK, dir], { env: ENV, encoding: 'utf8' });
    const j = JSON.parse(fs.readFileSync(path.join(dir, 'run.json'), 'utf8'));
    return { status: m.status, tokens: j.mask ? j.mask.tokens_replaced : -1, echoed: leaksValue((m.stdout || '') + (m.stderr || '')) };
  };
  Y2C.forEach(([name, text], i) => {
    const dir = path.join(root, 'y2c' + i);
    fs.mkdirSync(dir);
    const fname = name === 'y2.esc.json' ? 'a.json' : 'a.txt';
    writeCase(dir, { 'run.json': '{}\n', [fname]: text + (fname === 'a.txt' ? '\n' : '') });
    const before = runLeak(dir);
    const kinds = before.parsed && Array.isArray(before.parsed.files) ? [...new Set(before.parsed.files.flatMap((f) => f.kinds))].sort() : [];
    const m = maskRun(dir);
    const after = fs.readFileSync(path.join(dir, fname), 'utf8');
    const val = name === 'y2.nodigit' ? V.y2nd : V.y2b;
    const pass = kinds.includes('long-token') && m.status === 0 && m.tokens >= 1 && !after.includes(val) && !before.echoed && !m.echoed;
    results.push({ case: name, expect: ['long-token', 'masked'], kinds, status: before.status, echoed_value: before.echoed || m.echoed, pass });
  });
  Y2D.forEach(([name, text], i) => {
    const dir = path.join(root, 'y2d' + i);
    fs.mkdirSync(dir);
    const h = hex(40);
    if (text === null) writeCase(dir, { 'run.json': JSON.stringify({ head: h }) + '\n' });
    else writeCase(dir, { 'run.json': '{}\n', 'm4-stdout.txt': text + '\n' });
    const m = maskRun(dir);
    const r = runLeak(dir);
    const kept = text === null ? fs.readFileSync(path.join(dir, 'run.json'), 'utf8').includes(h) : true;
    const pass = m.status === 0 && m.tokens === 0 && r.status === 0 && kept && !r.echoed && !m.echoed;
    results.push({ case: name, expect: [], kinds: r.parsed ? r.parsed.files : null, status: r.status, echoed_value: r.echoed, pass });
  });

  // 고침4 G7 — 판정 칸 스키마(rules.json judgeSchema) 합성 대조군 2(작업지시서 §7 G13 ⑦ ⑧)
  //   ⑦ 자동 id 꼴 초점 기술(readings.focusAfter.id · 판정 칸 밖) → 판독기 충돌 0 · 가림은 가림대로(기록 = 칸 경로 · 꼴 · 길이 · 값 0) · 누출 0
  //   ⑧ 판정 칸(kbd.path[0].tag)에 심은 22자 토큰 → 판독기 「판정 칸 가림 충돌」 device_failure + 빨강 · 가림은 그 칸을 안 가림(충돌 기록 · 값 0) → 누출 검사가 파일째 잡음
  {
    const Jd = require('./judge.js');
    const rules = JSON.parse(fs.readFileSync(path.join(HERE, 'rules.json'), 'utf8'));
    const base = (o) => Object.assign({
      item: 'N-1', case: 'arrows', screen: 'after', delay: 300, window: { w: 1280, h: 720 }, cell: 'N-1|arrows|after|d300|1280x720', sampleNo: 1, order: 1, contextId: 1,
      valid: true, errors: [], deviceReading: null,
      circuit: { dead: false, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false, detail: { reconnectSeenAtMs: [], wsCloseAtMs: [], fromMs: 10 } },
      kbd: { reached: true, tabs: 3, path: [{ i: 1, row: '1', tag: 'button', role: null, tabIndex: 0, inRef: true, isStatusActivator: true, afterRef: false, body: false }] },
      readings: { opened: true, listClosed: true, focusSameRowBtn: true, otherRowsChanged: false, focusAfter: { tag: 'div', id: null, role: 'button', row: '1', isStatusActivator: true, inOpenList: false, text: null } }
    }, o || {});
    const runCase = (name, sample, want) => {
      const dir = path.join(root, name);
      fs.mkdirSync(dir);
      writeCase(dir, { 'run.json': '{}\n', 'samples.jsonl': JSON.stringify(sample) + '\n' });
      const conf = Jd.maskConflicts(rules, sample);
      const v = Jd.judge(rules, [sample]);
      const m = spawnSync(process.execPath, [MASK, dir], { env: ENV, encoding: 'utf8' });
      const runJ = JSON.parse(fs.readFileSync(path.join(dir, 'run.json'), 'utf8'));
      const after = fs.readFileSync(path.join(dir, 'samples.jsonl'), 'utf8');
      const r = runLeak(dir);
      const recTxt = JSON.stringify(runJ.mask || {}) + JSON.stringify(v.maskConflicts) + JSON.stringify(v.redReasons);
      const noVal = !leaksValue(recTxt) && !leaksValue(m.stdout + m.stderr) && !r.echoed;
      return want({ conf, v, m, mask: runJ.mask || {}, after, r, noVal });
    };
    const D = rules.deviceReading;
    const p7 = runCase('g7a', base({ readings: Object.assign(base().readings, { focusAfter: Object.assign(base().readings.focusAfter, { id: 'mudmenu-' + V.g7id }) }) }), (x) =>
      x.conf.length === 0 && !x.v.redReasons.some((t) => t.startsWith(D.maskConflict)) && x.v.totals.maskConflictSamples === 0
      && x.m.status === 0 && x.mask.tokens_replaced >= 1 && x.mask.judge_field_conflicts.length === 0
      && x.mask.records.some((rr) => rr.file === 'samples.jsonl' && rr.path === 'L1:readings.focusAfter.id' && rr.length === ('mudmenu-' + V.g7id).length && typeof rr.kind === 'string')
      && !x.after.includes(V.g7id) && x.r.status === 0 && x.noVal);
    results.push({ case: 'g7.autoid.schema', expect: ['conflict 0', 'masked', 'record path'], kinds: null, status: 0, echoed_value: false, pass: !!p7 });
    const planted = base({ kbd: Object.assign(base().kbd, { path: [Object.assign({}, base().kbd.path[0], { tag: V.g7tok })] }) });
    const p8 = runCase('g7b', planted, (x) =>
      x.conf.length === 1 && x.conf[0].path === 'kbd.path[0].tag' && x.conf[0].length === 22
      && x.v.red === true && x.v.redReasons.some((t) => t.startsWith(D.maskConflict)) && x.v.totals.maskConflictSamples === 1 && x.v.maskConflicts[0].path === 'kbd.path[0].tag'
      && x.m.status === 0 && x.mask.judge_field_conflicts.length === 1 && x.mask.judge_field_conflicts[0].path === 'L1:kbd.path[0].tag'
      && x.after.includes(V.g7tok) && x.r.status === 2 && x.noVal);
    results.push({ case: 'g7.planted.red', expect: ['device_failure', 'red', 'not masked', 'leak catches'], kinds: null, status: 0, echoed_value: false, pass: !!p8 });
  }
  // ── 고침5 H2 (작업지시서 20261116작 §8 · 결-6 甲 · 결-7 · 결-9 甲) — 새 시험 8 · 시험마다 대조군(일부러 틀린 입력 = 빨강) ──
  {
    const Mk = require('./mask.js');
    const H2 = [];
    const push = (name, expect, pass) => { H2.push(name); results.push({ case: name, expect, kinds: null, status: 0, echoed_value: false, pass: !!pass }); };
    const shaHex = (s) => crypto.createHash('sha256').update(s).digest('hex');
    // ① 목록 지문 · 개수(결-9 甲 출처 고정) — 대조군: 한 이름만 바꾼 목록은 지문이 달라야
    {
      const list = Mk.MUD_CLASS_ALLOW;
      const good = list.length === 641 && new Set(list).size === 641 && shaHex(list.join('\n') + '\n') === Mk.MUD_CLASS_ALLOW_SHA256
        && list.every((n) => /^mud-[a-z0-9_-]+$/.test(n) && n.length >= 22);
      const bent = list.slice(); bent[0] = bent[0] + 'x';
      push('h2.mud.sha', ['641', 'sha', 'control differs'], good && shaHex(bent.join('\n') + '\n') !== Mk.MUD_CLASS_ALLOW_SHA256);
    }
    // ② 목록 이름 전부 = 가림 0 · 누출 0
    {
      const dir = path.join(root, 'h2n'); fs.mkdirSync(dir);
      writeCase(dir, { 'run.json': '{}\n', 'probe.json': JSON.stringify({ screens: [{ r1: { candidates: [{ cls: Mk.MUD_CLASS_ALLOW }] } }] }) + '\n' });
      const m = maskRun(dir);
      const r = runLeak(dir);
      push('h2.mud.names', ['mask 0', 'leak 0'], m.status === 0 && m.tokens === 0 && r.status === 0 && !r.echoed && !m.echoed);
    }
    // ③ 같은 길이 무작위 토큰(대조군) = 가림 · 누출이 잡음
    {
      const dir = path.join(root, 'h2r'); fs.mkdirSync(dir);
      writeCase(dir, { 'probe.json': JSON.stringify({ cls: ['mud-button-root', V.h2a, V.h2b, V.h2c] }) + '\n' });
      const r = runLeak(dir);
      fs.writeFileSync(path.join(dir, 'run.json'), '{}\n');
      const m = maskRun(dir);
      const after = fs.readFileSync(path.join(dir, 'probe.json'), 'utf8');
      push('h2.mud.rndlen', ['leak', 'masked 3'], r.status === 2 && m.status === 0 && m.tokens === 3 && ![V.h2a, V.h2b, V.h2c].some((x) => after.includes(x)) && !r.echoed && !m.echoed);
    }
    // ④ mud- + 22자 무작위(목록 밖) = 가림 — 꼴 허용이 아님(대조군)
    {
      const dir = path.join(root, 'h2m'); fs.mkdirSync(dir);
      writeCase(dir, { 'probe.json': JSON.stringify({ cls: [V.h2m] }) + '\n' });
      const r = runLeak(dir);
      fs.writeFileSync(path.join(dir, 'run.json'), '{}\n');
      const m = maskRun(dir);
      const after = fs.readFileSync(path.join(dir, 'probe.json'), 'utf8');
      push('h2.mud.prefix', ['not in list', 'leak', 'masked'], !Mk.MUD_CLASS_ALLOW.includes(V.h2m) && r.status === 2 && m.status === 0 && m.tokens === 1 && !after.includes(V.h2m) && !r.echoed && !m.echoed);
    }
    // ⑤ mask --only(M8′): 그 파일만 · 옛 mask 칸 무변 · 번호는 앞 회차 다음 — 대조군: --summary-key mask · 폴더 밖 경로 = 거부(rc 1)
    {
      const dir = path.join(root, 'h2o'); fs.mkdirSync(dir);
      const prevMask = { files: 2, tokens_replaced: 3, distinct_tokens: 3 };
      writeCase(dir, { 'run.json': JSON.stringify({ mode: 'armed', mask: prevMask }) + '\n', 'verdict-device.json': JSON.stringify({ note: V.h2x }) + '\n', 'samples.jsonl': JSON.stringify({ x: V.h2y }) + '\n' });
      const m = spawnSync(process.execPath, [MASK, dir, '--only', 'verdict-device.json', '--summary-key', 'mask_verdict'], { env: ENV, encoding: 'utf8' });
      const runJ = JSON.parse(fs.readFileSync(path.join(dir, 'run.json'), 'utf8'));
      const vAfter = fs.readFileSync(path.join(dir, 'verdict-device.json'), 'utf8');
      const sAfter = fs.readFileSync(path.join(dir, 'samples.jsonl'), 'utf8');
      const bad1 = spawnSync(process.execPath, [MASK, dir, '--only', 'verdict-device.json', '--summary-key', 'mask'], { env: ENV, encoding: 'utf8' });
      const bad2 = spawnSync(process.execPath, [MASK, dir, '--only', '../x.json'], { env: ENV, encoding: 'utf8' });
      const runJ2 = JSON.parse(fs.readFileSync(path.join(dir, 'run.json'), 'utf8'));
      push('h2.only.mask', ['only file', 'mask kept', 'index base', 'refuse 2'], m.status === 0 && !vAfter.includes(V.h2x) && vAfter.includes('<가림:4>') && sAfter.includes(V.h2y)
        && JSON.stringify(runJ.mask) === JSON.stringify(prevMask) && runJ.mask_verdict && runJ.mask_verdict.tokens_replaced === 1 && runJ.mask_verdict.index_base === 3 && runJ.mask_verdict.only_present === true
        && bad1.status === 1 && bad2.status === 1 && JSON.stringify(runJ2.mask) === JSON.stringify(prevMask) && !leaksValue((m.stdout || '') + (m.stderr || '') + (bad1.stderr || '') + (bad2.stderr || '')));
    }
    // ⑥ leak-scan --only(M9′): 그 파일만 보고 지움 · 다른 파일 · 앞 회차 보류 칸 유지 — 대조군: --only 없이 = 두 파일 다 걸림
    {
      const dir = path.join(root, 'h2l'); fs.mkdirSync(dir);
      const files = { 'verdict-device.json': JSON.stringify({ note: V.h2x }) + '\n', 'samples.jsonl': JSON.stringify({ x: V.h2y }) + '\n', 'withheld.json': JSON.stringify({ note: 'n', stages: { measure: [{ name: 'a.txt', kinds: ['long-token'] }] }, files: [{ name: 'a.txt', kinds: ['long-token'] }] }) + '\n' };
      writeCase(dir, files);
      const dirC = path.join(root, 'h2lc'); fs.mkdirSync(dirC);
      writeCase(dirC, files);
      const ctl = runLeak(dirC); // 대조군은 따로 담은 사본에서(검사가 withheld.json 을 고쳐 쓰므로)
      const r = runLeak(dir, ['--stage', 'verdict', '--only', 'verdict-device.json', '--remove']);
      const w = JSON.parse(fs.readFileSync(path.join(dir, 'withheld.json'), 'utf8'));
      push('h2.only.leak', ['only file removed', 'other kept', 'stages kept', 'control 2 files'], ctl.status === 2 && ctl.parsed && ctl.parsed.files.length === 2
        && r.status === 2 && r.parsed && r.parsed.only === 'verdict-device.json' && r.parsed.files.length === 1
        && !fs.existsSync(path.join(dir, 'verdict-device.json')) && fs.existsSync(path.join(dir, 'samples.jsonl'))
        && Array.isArray(w.stages.measure) && w.stages.measure.length === 1 && Array.isArray(w.stages.verdict) && w.stages.verdict[0].name === 'verdict-device.json' && w.files.length === 2
        && !ctl.echoed && !r.echoed);
    }
    // ⑦ 판독 규칙 · 판독기 sha256(결-7): 정확 일치만 남음 — 대조군: 같은 길이 무작위 64 16진은 가림
    {
      const shas = Mk.ruleFileShas();
      const rulesSha = shaHex(fs.readFileSync(path.join(HERE, 'rules.json')));
      const judgeSha = shaHex(fs.readFileSync(path.join(HERE, 'judge.js')));
      const dir = path.join(root, 'h2s'); fs.mkdirSync(dir);
      writeCase(dir, {
        'run.json': JSON.stringify({ rules_sha256: rulesSha, judge_sha256: judgeSha, rules_sha256_bent: V.h2r }) + '\n',
        'verdict-device.json': JSON.stringify({ rulesSha256: rulesSha, judgeSha256: judgeSha, other: V.h2r }) + '\n',
      });
      const m = spawnSync(process.execPath, [MASK, dir], { env: ENV, encoding: 'utf8' });
      const runT = fs.readFileSync(path.join(dir, 'run.json'), 'utf8');
      const vT = fs.readFileSync(path.join(dir, 'verdict-device.json'), 'utf8');
      const r = runLeak(dir);
      push('h2.rule.sha', ['exact kept', 'random masked'], shas.length === 2 && shas.includes(rulesSha) && shas.includes(judgeSha) && m.status === 0
        && runT.includes(rulesSha) && runT.includes(judgeSha) && vT.includes(rulesSha) && vT.includes(judgeSha) && !runT.includes(V.h2r) && !vT.includes(V.h2r)
        && JSON.parse(runT).mask.tokens_replaced === 2 && r.status === 0 && !r.echoed && !leaksValue((m.stdout || '') + (m.stderr || '')));
    }
    // ⑧ 워크플로 배관(결-6 甲 · K-1 · K-2) — 단계 순서 · 인자 · 트리거 push 하나 · 쓰기 권한 잡 하나 · 외부 액션 40자 SHA
    //    대조군 3: M7 과 M8 이름 맞바꿈(순서 틀림) · --withheld 뺌 · 태그 고정 uses — 셋 다 거부여야
    {
      const wfPath = path.join(HERE, '..', '..', '.github', 'workflows', 'hqa-s4-measure.yml');
      const wf = fs.readFileSync(wfPath, 'utf8').replace(/\r\n/g, '\n');
      const ORDER = ["'[장치] run.json 조립 · 고정 머리 2회차'", "'[장치] M8 가림'", "'[장치] M9 누출 검사'", "'[판독] M7 장치 판독'",
        "'[장치] M8′ 가림 — verdict 만'", "'[장치] M9′ 누출 검사 — verdict 만'", "'[장치] run.json 회차 줄 덧붙임'", "'[장치] M10 해시 목록'"];
      const stepOf = (t, n) => { const i = t.indexOf('- name: ' + n); if (i < 0) return null; const j = t.indexOf('\n      - name: ', i + 1); return t.slice(i, j < 0 ? t.length : j); };
      const check = (t) => {
        const idx = ORDER.map((n) => t.indexOf('- name: ' + n));
        if (idx.some((i) => i < 0) || idx.some((v, k) => k > 0 && v <= idx[k - 1])) return 'order';
        if (!/node "\$f" --out "\$HQA_WORK\/selftest-judge\.json"/.test(stepOf(t, "'[장치] 자체시험 — 판독기'") || '')) return 'k1';
        if (!/--withheld "\$HQA_RAW\/withheld\.json"/.test(stepOf(t, ORDER[3]) || '')) return 'k2';
        if (!/--only verdict-device\.json/.test(stepOf(t, ORDER[4]) || '') || !/--only verdict-device\.json/.test(stepOf(t, ORDER[5]) || '')) return 'only';
        const on = /\non:\n([\s\S]*?)\n\S/.exec(t);
        if (!on || !/^  push:\n/.test(on[1]) || /^  (?!push:)[a-z_]+:/m.test(on[1])) return 'trigger';
        if ((t.match(/contents: write/g) || []).length !== 1) return 'write';
        const uses = t.match(/uses: \S+/g) || [];
        if (!uses.length || !uses.every((u) => /@[0-9a-f]{40}$/.test(u))) return 'uses';
        return 'ok';
      };
      const swapped = wf.split(ORDER[3]).join('@@T@@').split(ORDER[1]).join(ORDER[3]).split('@@T@@').join(ORDER[1]);
      const noW = wf.replace(/ \\\n\s*--withheld "\$HQA_RAW\/withheld\.json"/, '');
      const tag = wf.replace(/(uses: actions\/checkout)@[0-9a-f]{40}/, '$1@v7');
      push('h2.wf.pipe', ['order', 'k1', 'k2', 'only', 'push only', 'one write', 'sha pinned', 'controls 3'],
        check(wf) === 'ok' && check(swapped) === 'order' && check(noW) === 'k2' && check(tag) === 'uses');
    }
    results.h2 = H2.length;
  }
  // 고침7 수정 c5(P2-3): 판독기 새 칸(두 식 판별) — HQA 칸에 원자료 꼴 긴 글자를 심은 I-4 표본 → 판정 rows[].hqa = 값 꼬리표(참거짓 아님) · 판정 JSON · 요약 줄 어디에도 심은 글자 0 · 대조군 = 참/거짓 → 꼬리표 참/거짓
  {
    const Jd = require('./judge.js');
    const rules = JSON.parse(fs.readFileSync(path.join(HERE, 'rules.json'), 'utf8'));
    const cell = Jd.buildPlan(rules).cells.find((c) => c.item === 'I-4' && c.case === 'kbd-open' && c.screen === 'e1t3');
    const RAW = '가짜 요소 글자 상태 메뉴 단추 줄 하나 열림 닫힘 초점 남음 표시 ' + '가나다라마바사 '.repeat(12);
    const sample = (hqa, dev) => ({ item: 'I-4', case: cell.case, screen: cell.screen, delay: cell.delay, window: cell.window, cell: cell.key, sampleNo: 1, order: 1, contextId: 1, valid: true, errors: [], deviceReading: null, inputs: [],
      circuit: { dead: false, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false }, kbd: null,
      readings: { openCountAfterEsc: 0, focusOnSameRowBtn: dev === true, hqaFocusRow1Restored: hqa, menuZeroObserved: true } });
    const runK = (name, hqa, dev) => {
      const d = path.join(root, name);
      fs.mkdirSync(d);
      const rp = path.join(d, 'rules.json');
      fs.writeFileSync(rp, JSON.stringify(rules));
      const smp = path.join(d, 'samples.jsonl');
      fs.writeFileSync(smp, JSON.stringify(sample(hqa, dev)) + '\n');
      const op = path.join(d, 'verdict.json');
      const r = spawnSync(process.execPath, [path.join(HERE, 'judge.js'), '--rules', rp, '--samples', smp, '--out', op], { encoding: 'utf8' });
      const vt = fs.existsSync(op) ? fs.readFileSync(op, 'utf8') : '';
      const v = vt ? JSON.parse(vt) : null;
      const rows = v && v.items && v.items['I-4'] && v.items['I-4'].exprMismatch ? v.items['I-4'].exprMismatch.rows : [];
      return { rows, out: vt + (r.stdout || '') + (r.stderr || ''), sum: r.stdout || '' };
    };
    const kRaw = runK('k1raw', RAW);
    const kT = runK('k1t', true);
    const kF = runK('k1f', false, true);
    const pieces = [RAW.slice(0, 20), '가나다라마바사'];
    const pass = kRaw.rows.length === 1 && kRaw.rows[0].hqa === '참거짓 아님' && pieces.every((x) => !kRaw.out.includes(x)) && kRaw.sum.length > 0
      && kT.rows.length === 1 && kT.rows[0].hqa === true && kF.rows.length === 1 && kF.rows[0].hqa === false;
    results.push({ case: 'k1.hqa.raw', expect: ['참거짓 아님 · 글자 0'], kinds: null, status: 0, echoed_value: false, pass });
    results.k1 = 1;
  }
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}
results.push({ case: 'names.short', expect: ['< 22'], kinds: null, status: 0, echoed_value: false, pass: results.every((r) => !/[A-Za-z0-9_-]{22,}/.test(r.case)) });

// 고침4 G7: 옛 기대 수(CASES + 2 + Y2C + Y2D + 1)에 합성 대조군 2 를 더함 · 고침5 H2: 새 시험 8(results.h2 — 블록이 안 돌면 빨강)
// 고침7 수정 c5(P2-3): 새 칸 누출 시험 1(results.k1 — 블록이 안 돌면 빨강)
const ok = results.length === CASES.length + 2 + Y2C.length + Y2D.length + 2 + 8 + 1 + 1 && results.h2 === 8 && results.k1 === 1 && results.every((r) => r.pass);
const doc = { ok, cases: results.length, failed: results.filter((r) => !r.pass).map((r) => r.case), results };
const oi = process.argv.indexOf('--out');
if (oi >= 0 && process.argv[oi + 1]) fs.writeFileSync(process.argv[oi + 1], JSON.stringify(doc, null, 2) + '\n');
for (const r of results) console.log((r.pass ? '잡음/기대대로 ' : '다름 ') + r.case + (r.pass ? '' : ' kinds=' + JSON.stringify(r.kinds) + ' status=' + r.status));
console.log('[자체시험 누출] ' + (ok ? '통과' : '실패') + ' ' + results.filter((r) => r.pass).length + '/' + results.length);
process.exit(ok ? 0 : 1);
