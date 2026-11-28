// 화면 오류 기록 띠 — 갈래 T 자체시험 (브라우저 · DB · API · 네트워크 0 · 순수 함수와 파일 글자만)
//
// 사용: node console-error-band.selftest.js   (CI ui-browser.yml 「[시험] S9b」 단계가 장면 앞에서 부른다 · 이 PC 에서도 돌려도 됨)
//   B1 장면 목록 꼴 — 실물 목록 문제 0 · 반례 10 개(키 중복 · 갈래 U · 주입 꼴 · 닫힌 화면 · update-status 가로챔 · 로그인 주소 · 표지 꼴 ·
//      주소 맞춤 둘 다/둘 다 없음 · 진짜 403 을 대표 창 · 갈래 어긋남) 하나씩 잡힘
//   B2 대조군 판정 — 참 1 · 거짓 6(띠 0 · 띠 못 읽음 · 표지 하나 빠짐 · 가로챔 0 · 원래 알림 0 · 진짜 403 0)
//   B3 고친 뒤 판정 — 참 1 · 거짓 6(+3초 띠 · +10초 띠 · 띠 못 읽음 · 경고 흔적 0 · 앱 표준오류 1 · 원래 알림 0)
//   B4 모드 가름 — 대조군 참 재료를 고친 뒤로 · 고친 뒤 참 재료를 대조군으로 넣으면 둘 다 거짓 · 모르는 모드 = 거짓
//   B5 모드 칸 — control · fixed 만 · 대소문자 · 앞뒤 빈칸 · 빈 칸 · 없음 · 다른 낱말 = null
//   B6 주소 맞춤 — 경로 정확히 · 쿼리 무시 · 비슷한 경로 아님 · 정규식 앞뒤 고정 · 깨진 주소 거짓
//   B7 콘솔 셈 — 표준오류 · 경고 따로 · 앱 표준오류 꼴(「[」 머리)만 · 자원 오류 셈 밖 · 정보 줄 셈 밖
//   B8 끝 코드 — 다 통과 0 · 키 빠짐 · 중복 · 장치 줄 · 거짓 · 모르는 키 · 로그아웃 실패 = 1
//   B9 판정기와 따로 — 이름이 K 본문 머리 아님(judge.classifyKNames 실행 목록 밖) · 결과표 경로 ≠ K 결과표 · 판정기 G 기대 키에 띠 키 0
//   B10 워크플로 글자 — S9b 단계가 S9 판정 뒤 · API 멈춤 앞 · always() · 모드 칸 · 자체시험 + 장면 두 줄 · S8 ~ S9 사이 글자 무접촉(K-C25g 자르기)
'use strict';

const fs = require('fs');
const path = require('path');
const B = require('./console-error-band');
const J = require('./judge');
const L = require('./lib');

const results = [];
function check(name, cond, detail) {
  results.push({ name, ok: !!cond });
  console.log(`${cond ? 'PASS' : 'FAIL'} ${name}${detail ? ` — ${detail}` : ''}`);
}
const clone = (o) => JSON.parse(JSON.stringify(o));
const S = (k) => B.SCENES.find((s) => s.key === k);

// ── B1 ──
{
  const real = B.sceneListProblems();
  const base = clone(B.SCENES);
  const cases = [
    ['키 중복', (l) => { l[1].key = l[0].key; }],
    ['갈래 U', (l) => { l[0].key = 'G-U1'; l[0].branch = 'U'; }],
    ['주입 꼴', (l) => { l[0].inject = '500'; }],
    ['닫힌 화면', (l) => { l[0].page = '/sales/profitability'; }],
    ['update-status 가로챔', (l) => { l[0].match = { path: '/api/auth/update-status' }; }],
    ['로그아웃 주소 정규식', (l) => { l[0].match = { re: '^/api/auth/log[a-z]+$' }; }],
    ['표지 꼴', (l) => { l[0].tags = ['CS 안 읽은']; }],
    ['주소 맞춤 둘 다', (l) => { l[0].match = { path: '/api/x', re: '^/api/x$' }; }],
    ['진짜 403 을 대표 창', (l) => { const s = l.find((x) => x.inject === 'real403'); s.who = 'owner'; }],
    ['갈래 어긋남', (l) => { l[0].branch = 'S'; }],
  ];
  const miss = [];
  for (const [n, f] of cases) {
    const l = clone(base);
    f(l);
    if (B.sceneListProblems(l).length === 0) miss.push(n);
  }
  check(`B1 장면 목록 꼴 — 실물 문제 0 · 반례 ${cases.length} 개 전부 잡힘`, real.length === 0 && miss.length === 0,
    `실물 문제 ${real.length}${real.length ? ` [${real.join(' · ')}]` : ''} · 못 잡은 반례 ${miss.length ? miss.join(' · ') : '0'}`);
}

// 재료 만들기 — 장면의 표지마다 같은 수
const mat = (scene, { band3, band10, err = 1, warn = 0, appErr = 0, routeHits = 1, real403 = 0, snackHit = scene.snack === null ? 0 : 1 }) => {
  const e = {};
  const w = {};
  for (const t of scene.tags) { e[t] = err; w[t] = warn; }
  return { routeHits, real403, band3, band10, err: e, warn: w, appErr, snackHit };
};
const R1 = S('G-R1a'); // 표지 둘 · 알림 없음
const P2 = S('G-P2'); // 알림 있음
const S1 = S('G-S1'); // 진짜 403
const CTRL_OK = mat(R1, { band3: true, band10: true, err: 1, appErr: 2 });
const FIX_OK = mat(R1, { band3: false, band10: false, err: 0, warn: 1, appErr: 0 });

// ── B2 ──
{
  const pass = B.judgeScene('control', R1, CTRL_OK).pass;
  const oneTag = clone(CTRL_OK);
  oneTag.err[R1.tags[1]] = 0;
  const bad = [
    ['띠 0', B.judgeScene('control', R1, mat(R1, { band3: false, band10: false }))],
    ['띠 못 읽음', B.judgeScene('control', R1, mat(R1, { band3: null, band10: null }))],
    ['표지 하나 빠짐', B.judgeScene('control', R1, oneTag)],
    ['가로챔 0', B.judgeScene('control', R1, mat(R1, { band3: true, band10: true, routeHits: 0 }))],
    ['원래 알림 0', B.judgeScene('control', P2, mat(P2, { band3: true, band10: true, snackHit: 0 }))],
    ['진짜 403 0', B.judgeScene('control', S1, mat(S1, { band3: true, band10: true, routeHits: 5, real403: 0 }))],
  ].filter(([, v]) => v.pass !== false || v.why.length === 0).map(([n]) => n);
  const s1ok = B.judgeScene('control', S1, mat(S1, { band3: false, band10: true, routeHits: 0, real403: 1 })).pass;
  check('B2 대조군 판정 — 참 2(띠 +10초만이어도 참 · 진짜 403 장면) · 거짓 6', pass === true && s1ok === true && bad.length === 0,
    `참 ${pass} · 진짜 403 참 ${s1ok} · 기대와 다른 거짓 반례 ${bad.length ? bad.join(' · ') : '0'}`);
}

// ── B3 ──
{
  const pass = B.judgeScene('fixed', R1, FIX_OK).pass;
  const p2ok = B.judgeScene('fixed', P2, mat(P2, { band3: false, band10: false, err: 0, warn: 2 })).pass;
  const bad = [
    ['+3초 띠', B.judgeScene('fixed', R1, mat(R1, { band3: true, band10: false, err: 0, warn: 1 }))],
    ['+10초 띠', B.judgeScene('fixed', R1, mat(R1, { band3: false, band10: true, err: 0, warn: 1 }))],
    ['띠 못 읽음', B.judgeScene('fixed', R1, mat(R1, { band3: null, band10: false, err: 0, warn: 1 }))],
    ['경고 흔적 0', B.judgeScene('fixed', R1, mat(R1, { band3: false, band10: false, err: 0, warn: 0 }))],
    ['앱 표준오류 1', B.judgeScene('fixed', R1, mat(R1, { band3: false, band10: false, err: 0, warn: 1, appErr: 1 }))],
    ['원래 알림 0', B.judgeScene('fixed', P2, mat(P2, { band3: false, band10: false, err: 0, warn: 1, snackHit: 0 }))],
  ].filter(([, v]) => v.pass !== false || v.why.length === 0).map(([n]) => n);
  check('B3 고친 뒤 판정 — 참 2 · 거짓 6', pass === true && p2ok === true && bad.length === 0,
    `참 ${pass} · 알림 장면 참 ${p2ok} · 기대와 다른 거짓 반례 ${bad.length ? bad.join(' · ') : '0'}`);
}

// ── B4 ──
{
  const a = B.judgeScene('fixed', R1, CTRL_OK).pass;
  const b = B.judgeScene('control', R1, FIX_OK).pass;
  const c = B.judgeScene('Control', R1, CTRL_OK).pass;
  const d = B.judgeScene(null, R1, FIX_OK).pass;
  const e = B.judgeScene('control', null, CTRL_OK).pass;
  check('B4 모드 가름 — 대조군 참 재료 → 고친 뒤 거짓 · 고친 뒤 참 재료 → 대조군 거짓 · 모르는 모드 · 장면 없음 거짓',
    a === false && b === false && c === false && d === false && e === false, `${a} · ${b} · ${c} · ${d} · ${e}`);
}

// ── B5 ──
{
  const ok = B.modeOf('control') === 'control' && B.modeOf('fixed') === 'fixed';
  const nul = ['', undefined, null, 'CONTROL', 'Fixed', ' control', 'control ', 'fix', 'ctrl', 'control,fixed', 1].filter((v) => B.modeOf(v) !== null);
  check('B5 모드 칸 — control · fixed 만 · 나머지 11 꼴 null', ok && nul.length === 0, `정상 ${ok} · null 아닌 것 ${nul.length}`);
}

// ── B6 ──
{
  const C1 = S('G-C1');
  const rows = [
    [R1, 'http://127.0.0.1:18080/api/cs/replies/unread-count', true],
    [R1, 'http://127.0.0.1:18080/api/cs/replies/unread-count?x=1', true],
    [R1, new URL('http://127.0.0.1:18080/api/cs/replies/unread-count'), true],
    [R1, 'http://127.0.0.1:18080/api/cs/replies/unread-count/x', false],
    [R1, 'http://127.0.0.1:18080/xapi/cs/replies/unread-count', false],
    [C1, 'http://127.0.0.1:18080/api/cs/requests/abc-1/replies', true],
    [C1, 'http://127.0.0.1:18080/api/cs/requests/abc-1/rating', false],
    [C1, 'http://127.0.0.1:18080/api/cs/requests/a/b/replies', false],
    [C1, 'http://127.0.0.1:18080/x/api/cs/requests/a/replies', false],
    [R1, '이건 주소가 아님', false],
  ];
  const bad = rows.filter(([s, u, want]) => B.routeMatch(s, u) !== want).map((x, i) => i + 1);
  check(`B6 주소 맞춤 — ${rows.length} 줄 기대대로`, bad.length === 0, bad.length ? `다른 줄 ${bad.join(',')}` : '');
}

// ── B7 ──
{
  const tags = ['[HP15]', '[CS] 답 목록 조회 실패'];
  const cur = B.newTally(tags);
  B.tallyLine(cur, 'error', '[HP15] HttpRequestException: 503', tags);
  B.tallyLine(cur, 'error', '  [CS] 답 목록 조회 실패', tags);
  B.tallyLine(cur, 'warning', 'warn: HitPan.Web.X[0]\n      [HP15] 조회 실패', tags);
  B.tallyLine(cur, 'error', 'Failed to load resource: the server responded with a status of 503 ()', tags);
  B.tallyLine(cur, 'info', '[HP15] 정보 줄', tags);
  B.tallyLine(cur, 'error', '[Other] 다른 줄', tags);
  B.tallyLine(cur, 'warning', 'HP15 괄호 없음', tags);
  const ok = cur.err['[HP15]'] === 1 && cur.err['[CS] 답 목록 조회 실패'] === 1 && cur.warn['[HP15]'] === 1 && cur.warn['[CS] 답 목록 조회 실패'] === 0 && cur.appErr === 3;
  check('B7 콘솔 셈 — 표준오류 · 경고 따로 · 앱 표준오류 꼴 3(자원 오류 · 정보 줄 셈 밖)', ok, JSON.stringify(cur));
}

// ── B8 ──
{
  const keys = B.SCENES.map((s) => s.key);
  const row = (k, o = {}) => ({ scope: B.SCOPE, path: k, kind: 'TEST', pass: true, ...o });
  const all = keys.map((k) => row(k));
  const c0 = B.endCode(all, keys, 0);
  const bad = [
    ['키 빠짐', B.endCode(all.slice(1), keys, 0)],
    ['중복', B.endCode([...all, row(keys[0])], keys, 0)],
    ['장치 줄', B.endCode([row(keys[0], { kind: 'DEVICE' }), ...all.slice(1)], keys, 0)],
    ['거짓', B.endCode([row(keys[0], { pass: false }), ...all.slice(1)], keys, 0)],
    ['모르는 키', B.endCode([...all, row('G-X9')], keys, 0)],
    ['로그아웃 실패', B.endCode(all, keys, 1)],
    ['줄 0', B.endCode([], keys, 0)],
  ].filter(([, v]) => v !== 1).map(([n]) => n);
  const other = B.endCode([...all, { scope: 'K', path: 'x', kind: 'TEST', pass: false }], keys, 0);
  check('B8 끝 코드 — 다 통과 0 · 다른 범위 줄 무시 0 · 반례 7 개 1', c0 === 0 && other === 0 && bad.length === 0,
    `다 통과 ${c0} · 다른 범위 ${other} · 1 아닌 반례 ${bad.length ? bad.join(' · ') : '0'}`);
}

// ── B9 ──
{
  const names = ['console-error-band.js', 'console-error-band.selftest.js'];
  const cls = J.classifyKNames(names);
  const notK = Array.isArray(cls.run) && cls.run.length === 0 && Array.isArray(cls.odd) && cls.odd.length === 0;
  const sep = path.resolve(B.bandResultPath()) !== path.resolve(L.resultPath());
  const keys = new Set(B.SCENES.map((s) => s.key));
  const inG = (J.EXPECT_G || []).filter((k) => [...keys].some((x) => String(k).includes(`|${x}|`)));
  check('B9 판정기와 따로 — K 본문 실행 목록 밖 · 결과표 따로 · G 기대 키에 띠 키 0', notK && sep && inG.length === 0,
    `K 목록 밖 ${notK} · 결과표 따로 ${sep} · G 기대 키 안 띠 키 ${inG.length}`);
}

// ── B10 ──
{
  const ymlPath = path.join(__dirname, '..', '..', '..', '.github', 'workflows', 'ui-browser.yml');
  const yml = fs.existsSync(ymlPath) ? fs.readFileSync(ymlPath, 'utf8') : '';
  const iS8 = yml.indexOf('[시험] S8');
  const iS9 = yml.indexOf('[시험] S9 판정');
  const iS9b = yml.indexOf('[시험] S9b');
  const iStop = yml.indexOf('[장치] API 멈춤');
  const block = iS9b >= 0 && iStop > iS9b ? yml.slice(iS9b, iStop) : '';
  const order = iS8 >= 0 && iS9 > iS8 && iS9b > iS9 && iStop > iS9b;
  const between = iS8 >= 0 && iS9 > iS8 ? yml.slice(iS8, iS9) : '';
  const lines = [
    ['always', /^\s*if: always\(\)\s*$/m],
    ['모드 칸', /^\s*UI_BAND_MODE: (control|fixed)\s*$/m],
    ['자체시험 줄', /node scripts\/playwright-audit\/ci\/console-error-band\.selftest\.js \|\| rc=1/],
    ['장면 줄', /node scripts\/playwright-audit\/ci\/console-error-band\.js \|\| rc=1/],
    ['단계 상한', /^\s*timeout-minutes: \d+\s*$/m],
  ];
  const miss = lines.filter(([, re]) => !re.test(block)).map(([n]) => n);
  const s8Clean = between.length > 0 && !between.includes('console-error-band');
  const once = (yml.match(/console-error-band\.js \|\| rc=1/g) || []).length === 1;
  check('B10 워크플로 글자 — S9b = S9 판정 뒤 · API 멈춤 앞 · 칸 5 · S8 ~ S9 사이 무접촉 · 장면 줄 한 번', order && miss.length === 0 && s8Clean && once,
    `순서 ${order} · 빠진 칸 ${miss.length ? miss.join(' · ') : '0'} · S8~S9 무접촉 ${s8Clean} · 한 번 ${once}`);
}

const bad = results.filter((x) => !x.ok).length;
console.log(`띠 자체시험 합계 ${results.length - bad}/${results.length} PASS`);
process.exit(bad === 0 ? 0 : 1);
