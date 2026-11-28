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
//   B11 (고침1) G-S1 = 시험 장치 가로채기 403 · 어음 목록 주소만 · 실물 목록 real403 장면 0 · 가로챔 0 = 무효(B1 · B2 의 진짜 403 반례는 만든 장면으로)
//   B12 (넓힘X1 · 지시서 §8-3 대표 ④) G-X1 = 백업 설정 조회 503 · 알림 글 = LoadFailedNotice.Text 앞머리 · [설정 저장] 막힘 · 고친 뒤에만(대조군 목록 밖)
//   (B10 모드 칸 정규식 — 넓힘X1 에서 값 뒤 꼬리 주석 허용으로 고침 · 둘째 푸시 d54b6e9f 의 꼬리 주석 때문에 거짓이었음)
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
    // ⬛ 옛 줄: ['진짜 403 을 대표 창', (l) => { const s = l.find((x) => x.inject === 'real403'); s.who = 'owner'; }],
    //   고침1 — 실물 목록에 real403 장면 0 → 첫 장면을 real403 · 대표 창으로 바꿔 규칙만 잰다
    ['진짜 403 을 대표 창', (l) => { l[0].inject = 'real403'; l[0].who = 'owner'; }],
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
// ⬛ 옛 줄: const S1 = S('G-S1'); // 진짜 403
//   고침1 — G-S1 = 가로채기 403 으로 바뀜 → 진짜 403 판정 틀은 만든 장면(REAL)으로 잰다
const S1 = S('G-S1'); // 가로채기 403 · 직원 창 · 처리기 알림
const REAL = Object.freeze({ ...S1, key: 'G-S9', inject: 'real403' }); // 진짜 403 틀(실물 목록 밖)
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
    // ⬛ 옛 줄: ['진짜 403 0', B.judgeScene('control', S1, mat(S1, { band3: true, band10: true, routeHits: 5, real403: 0 }))],
    ['진짜 403 0', B.judgeScene('control', REAL, mat(REAL, { band3: true, band10: true, routeHits: 5, real403: 0 }))],
  ].filter(([, v]) => v.pass !== false || v.why.length === 0).map(([n]) => n);
  // ⬛ 옛 줄: const s1ok = B.judgeScene('control', S1, mat(S1, { band3: false, band10: true, routeHits: 0, real403: 1 })).pass;
  const s1ok = B.judgeScene('control', REAL, mat(REAL, { band3: false, band10: true, routeHits: 0, real403: 1 })).pass;
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
    // ⬛ 옛 줄: ['모드 칸', /^\s*UI_BAND_MODE: (control|fixed)\s*$/m],
    //   넓힘X1 — 둘째 푸시(d54b6e9f)가 값 뒤에 「# ⬛ 옛: control(…)」 꼬리 주석을 달아 옛 정규식이 못 잡았다(B10 거짓 · S9b rc=1).
    //   값은 그대로 control · fixed 둘 중 하나만 · 뒤에 「 # …」 주석만 허용.
    ['모드 칸', /^\s*UI_BAND_MODE: (control|fixed)(\s+#.*)?\s*$/m],
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

// ── B11 (고침1 · 지시서 §8-2) — G-S1 = 시험 장치 가로채기 403 · 어음 목록 주소 · 서버 403 에 기대지 않음 ──
{
  const ok1 = S1 && S1.inject === '403' && S1.match && S1.match.path === '/api/finance/bills' && S1.page === '/accounting/bills'
    && S1.tags.length === 1 && S1.tags[0] === '[Bills.List]' && S1.snack === B.FORBID_TEXT;
  const realUsed = B.SCENES.filter((s) => s.inject === 'real403').map((s) => s.key);
  const hitQs = B.routeMatch(S1, 'http://127.0.0.1:18080/api/finance/bills?from=2026-11-01&to=2026-11-28') === true
    && B.routeMatch(S1, 'http://127.0.0.1:18080/api/finance/card-payments') === false
    && B.routeMatch(S1, 'http://127.0.0.1:18080/api/finance/bills/abc/status') === false;
  // 가로챔이 걸렸으면 서버 403 0 이어도 참 · 가로챔 0 이면 서버 403 이 있어도 거짓(장면 무효)
  const passIntercept = B.judgeScene('control', S1, mat(S1, { band3: true, band10: true, routeHits: 1, real403: 0 })).pass;
  const noHit = B.judgeScene('control', S1, mat(S1, { band3: true, band10: true, routeHits: 0, real403: 3 }));
  const fixedOk = B.judgeScene('fixed', S1, mat(S1, { band3: false, band10: false, err: 0, warn: 1 })).pass;
  check('B11 G-S1 고침1 — 가로채기 403 · 어음 목록 주소만(쿼리 무시 · 카드 · 상태 바꿈 아님) · real403 쓰는 장면 0 · 가로챔 0 = 무효',
    ok1 && realUsed.length === 0 && hitQs && passIntercept === true && noHit.pass === false && fixedOk === true,
    `꼴 ${ok1} · real403 장면 ${realUsed.length ? realUsed.join(',') : '0'} · 주소 ${hitQs} · 가로챔 참 ${passIntercept} · 가로챔 0 거짓 ${noHit.pass === false} · 고친 뒤 참 ${fixedOk}`);
}

// ── B12 (넓힘X1 · 지시서 §8-3 대표 ④) — G-X1 = 백업 설정 조회 503 · 쉬운 알림 · [설정 저장] 막힘 · 고친 뒤에만 ──
{
  const X1 = S('G-X1');
  const noticeSrc = path.join(__dirname, '..', '..', '..', 'src', 'HitPan.Web', 'Services', 'LoadFailedNotice.cs');
  const src = fs.existsSync(noticeSrc) ? fs.readFileSync(noticeSrc, 'utf8') : '';
  const m = src.match(/public const string Text = "([^"]+)";/);
  const ok1 = !!X1 && X1.branch === 'X' && X1.who === 'owner' && X1.page === '/data/backup' && X1.inject === '503'
    && X1.match && X1.match.path === '/api/backup/settings' && X1.tags.length === 1 && X1.tags[0] === '[BackupService.GetSettingsAsync]'
    && X1.block === '설정 저장' && X1.fixedOnly === true;
  // 알림 글 = LoadFailedNotice.Text 앞머리(원문 글자와 맞춤 — 글이 바뀌면 여기서 먼저 잡힘)
  const textOk = !!m && !!X1 && typeof X1.snack === 'string' && m[1].startsWith(X1.snack);
  // 모드 목록 — 대조군엔 G-X1 없음 · 고친 뒤엔 전부
  const ctrlKeys = B.scenesFor('control').map((s) => s.key);
  const fixKeys = B.scenesFor('fixed').map((s) => s.key);
  const modeOk = !ctrlKeys.includes('G-X1') && fixKeys.includes('G-X1') && fixKeys.length === B.SCENES.length
    && ctrlKeys.length === B.SCENES.filter((s) => s.fixedOnly !== true).length && B.scenesFor(null).every((s) => s.fixedOnly !== true);
  // 목록 꼴 반례 — 넓힘인데 fixedOnly 없음 · 막을 단추 빈 글자 · fixedOnly 글자
  const base = clone(B.SCENES);
  const ix = base.findIndex((s) => s.key === 'G-X1');
  const listCases = [
    ['fixedOnly 없음', (l) => { delete l[ix].fixedOnly; }],
    ['막을 단추 빈 글자', (l) => { l[ix].block = '  '; }],
    ['fixedOnly 글자', (l) => { l[ix].fixedOnly = 'yes'; }],
    ['갈래 X 키 어긋남', (l) => { l[ix].key = 'G-S7'; }],
  ];
  const miss = [];
  for (const [n, f] of listCases) {
    const l = clone(base);
    f(l);
    if (B.sceneListProblems(l).length === 0) miss.push(n);
  }
  // 판정 — 참 1 · 거짓 5(단추 안 막힘 · 단추 못 읽음 · 쉬운 알림 0 · 경고 흔적 0 · +10초 띠)
  const fx = (o) => ({ ...mat(X1, { band3: false, band10: false, err: 0, warn: 1 }), blocked: true, ...o });
  const pass = B.judgeScene('fixed', X1, fx({})).pass;
  const bad = [
    ['단추 안 막힘', B.judgeScene('fixed', X1, fx({ blocked: false }))],
    ['단추 못 읽음', B.judgeScene('fixed', X1, fx({ blocked: null }))],
    ['쉬운 알림 0', B.judgeScene('fixed', X1, fx({ snackHit: 0 }))],
    ['경고 흔적 0', B.judgeScene('fixed', X1, { ...mat(X1, { band3: false, band10: false, err: 0, warn: 0 }), blocked: true })],
    ['+10초 띠', B.judgeScene('fixed', X1, fx({ band10: true }))],
  ].filter(([, v]) => v.pass !== false || v.why.length === 0).map(([n]) => n);
  // 막을 단추 없는 장면은 blocked 를 안 봄(G-R1a 고친 뒤 참 재료에 blocked 없음 → 참 그대로)
  const noBlockOk = B.judgeScene('fixed', R1, FIX_OK).pass === true;
  // 주소 — 백업 설정만(이력 · 복원 이력 · 실행 아님)
  const hitOk = B.routeMatch(X1, 'http://127.0.0.1:18080/api/backup/settings') === true
    && B.routeMatch(X1, 'http://127.0.0.1:18080/api/backup/history?limit=50') === false
    && B.routeMatch(X1, 'http://127.0.0.1:18080/api/backup/run') === false;
  check('B12 G-X1 넓힘X1 — 꼴 · 알림 글 = LoadFailedNotice.Text 앞머리 · 대조군 목록 밖 · 목록 반례 4 · 판정 참 1 거짓 5 · 막을 단추 없는 장면 무영향 · 주소',
    ok1 && textOk && modeOk && miss.length === 0 && pass === true && bad.length === 0 && noBlockOk && hitOk,
    `꼴 ${ok1} · 알림 글 ${textOk} · 모드 목록 ${modeOk} · 못 잡은 목록 반례 ${miss.length ? miss.join(' · ') : '0'} · 참 ${pass}`
    + ` · 기대와 다른 거짓 반례 ${bad.length ? bad.join(' · ') : '0'} · 무영향 ${noBlockOk} · 주소 ${hitOk}`);
}
// ── B13 (넓힘 X2 · 지시서 §8-3 대표 ④) — G-P4 발주 현황 = 503 가로채기 · 쉬운 알림 글 = 앱 LoadFailedNotice.Text 앞머리 · 띠 0 · 경고 흔적 · 알림 0 이면 거짓 ──
{
  const P4 = S('G-P4');
  const svc = path.join(__dirname, '..', '..', '..', 'src', 'HitPan.Web', 'Services', 'LoadFailedNotice.cs');
  const svcText = fs.existsSync(svc) ? fs.readFileSync(svc, 'utf8') : '';
  const m = /public const string Text = "([^"]+)";/.exec(svcText);
  const page = path.join(__dirname, '..', '..', '..', 'src', 'HitPan.Web', 'Pages', 'Purchase', 'PurchaseOrderStatusPage.razor');
  const pageText = fs.existsSync(page) ? fs.readFileSync(page, 'utf8') : '';
  const shape = !!P4 && P4.inject === '503' && P4.page === '/purchase-order-status' && P4.match.path === '/api/reports/purchase-orders'
    && P4.tags.length === 1 && pageText.includes(`Logger.LogWarning(_hp15Ex, "${P4.tags[0]}");`) && pageText.includes('@page "/purchase-order-status"')
    && pageText.includes('"api/reports/purchase-orders?"') && pageText.includes('LoadFailedNotice.Show(Snackbar, _hp15Ex);');
  const snackOk = !!P4 && !!m && m[1].startsWith(P4.snack);
  const last = B.SCENES.indexOf(P4) > B.SCENES.findIndex((s) => s.key === 'G-C2'); // 기존 11 장면 뒤에 덧붙임(X1 장면이 더 뒤에 와도 참)
  const ok = P4 && B.judgeScene('fixed', P4, mat(P4, { band3: false, band10: false, err: 0, warn: 1, snackHit: 1 })).pass === true;
  const bad = P4 ? [
    ['알림 0', B.judgeScene('fixed', P4, mat(P4, { band3: false, band10: false, err: 0, warn: 1, snackHit: 0 }))],
    ['띠 +3초', B.judgeScene('fixed', P4, mat(P4, { band3: true, band10: false, err: 0, warn: 1 }))],
    ['경고 흔적 0', B.judgeScene('fixed', P4, mat(P4, { band3: false, band10: false, err: 0, warn: 0 }))],
    ['앱 표준오류 1', B.judgeScene('fixed', P4, mat(P4, { band3: false, band10: false, err: 0, warn: 1, appErr: 1 }))],
    ['가로챔 0', B.judgeScene('fixed', P4, mat(P4, { band3: false, band10: false, err: 0, warn: 1, routeHits: 0 }))],
  ].filter(([, v]) => v.pass !== false).map(([n]) => n) : ['장면 없음'];
  const hit = P4 && B.routeMatch(P4, 'http://127.0.0.1:18080/api/reports/purchase-orders?view=date&from=2026-11-01') === true
    && B.routeMatch(P4, 'http://127.0.0.1:18080/api/reports/purchase-statistics?view=date') === false;
  check('B13 G-P4 넓힘 X2 — 꼴 · 화면 원문 대조(표지 · 주소 · 알림 줄) · 알림 글 = 앱 글 앞머리 · G-C2 뒤 · 고친 뒤 참 1 · 거짓 5 · 주소 맞춤',
    shape && snackOk && last && ok && bad.length === 0 && hit,
    `꼴 ${shape} · 알림 글 ${snackOk} · 순서 ${last} · 참 ${ok} · 기대와 다른 거짓 반례 ${bad.length ? bad.join(' · ') : '0'} · 주소 ${hit}`);
}

const bad = results.filter((x) => !x.ok).length;
console.log(`띠 자체시험 합계 ${results.length - bad}/${results.length} PASS`);
process.exit(bad === 0 ? 0 : 1);
