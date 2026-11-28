// 화면 오류 기록 띠 — 갈래 T 장면 · 판정기 (20261128작 화면오류기록띠 PR-1 · 설계 docs/설계/erp/화면오류기록띠_설계_20261128.md §5-1 · §5-2 ·
//   작업지시서 docs/운영기록/20261128작_화면오류기록띠_작업지시서.md §2 갈래 T · §4 · §8-1 · 개발명세서 docs/개발/erp/20261128_화면오류기록띠_갈래T_개발명세서.md)
//
// 🔴 이 파일의 규칙
//   ① 이름이 K 본문 머리(k35-)로 시작하지 않는다 — 실행기(run-k-bodies.js)·판정기(judge.js)가 돌리지 않고 키 틀에도 안 들어간다.
//      결과표도 따로(ui-out/main/console-error-band-result.json) — judge.js · k35-ws03-closed.js 판정 줄 손 0(#1032 · #1011 겹침 줄임).
//      판정은 이 파일의 순수 함수 judgeScene 하나 · 끝 코드는 endCode 하나(자체시험 console-error-band.selftest.js 가 참 · 거짓 반례로 부른다).
//   ② 모드 스위치 = 환경 칸 UI_BAND_MODE — `control`(대조군 · 고치기 전 판 · 장면마다 흰 띠 1 + 그 줄 표지의 표준오류 1 이상 = 장면이 그 줄에 닿는다)
//      · `fixed`(고친 뒤 · 흰 띠 0(+3초 · +10초) · 그 줄 표지의 console.warn 1 이상 · 앱 표준오류 꼴 0). 그 밖 글자 · 빈 칸 = 아무것도 안 돌리고 실패.
//      두 모드 공통 = 실패 주입이 실제로 걸림(가로챔 1 이상 · 진짜 403 은 서버 403 응답 1 이상) · 원래 있던 알림 글 1 이상(있는 장면만).
//   ③ 실패 주입은 시험 장치의 가로채기(page.route)로만 — 앱 손 0. 진짜 403 장면(G-S1)은 가로채기 0 · 서버 판정 그대로.
//      `api/auth/update-status` 는 가로채지 않는다(R 과 U(#43) 가 같이 부름 · U 는 PR-2 몫 · 설계 §5-2 ⚠ 줄) — 장면 목록 꼴 검사가 막는다.
//   ④ 연 세션은 끝에 반드시 놓는다(로그아웃 · 11/27 계정 잠김 사고 선례) — 정상 · 예외 · 시간 상한 · 신호 모든 길에서 cleanup.
//   ⑤ 그림을 찍지 않는다 · 콘솔 원문은 메모리에서 수만 센다(표지 낱말 포함 여부) · 찍는 글자는 소독 뒤(K.consoleLine · L.sanitize).
//   ⑥ 이 PC 에서 돌리지 않는다(이 PC DB = 실물) — CI ui-browser.yml 「[시험] S9b」 단계에서만.
'use strict';

const fs = require('fs');
const path = require('path');
const L = require('./lib');
const K = require('./kit35');
const DC = require('./device-check'); // 머리 조건(require.main) 때문에 불러도 장치 본문은 안 돈다 — 내보냄만 쓴다

const env = process.env;
const BROWSER = 'chromium';
const SCOPE = 'BAND';
const MODES = Object.freeze(['control', 'fixed']);
const BAND_TOTAL_MS = 8 * 60 * 1000; // 이 몸 전체 상한(워크플로 S9b 단계 상한 10분 안)
const PAGE_MS = 60000;
const FORBID_TEXT = '이 기능에 접근할 권한이 없습니다'; // HitPanApiAuthHandler 평소 403 갈래 알림 글 머리(k35-ws03-closed.js 와 같은 글자 · 따로 둠)
// 닫힌 주소(BetaClosedRoutes.All · 2026-11-28 판) — 장면이 닫힌 화면을 고르면 안내 화면만 떠 그 줄에 안 닿는다(꼴 검사)
const CLOSED_PATHS = Object.freeze(['/settings', '/settings/ai-assistant', '/sales/profitability', '/tax-invoice-stats', '/accounting/export', '/welcome']);
// 가로채면 안 되는 주소 — U(#43) 공유 줄(설계 §5-2 ⚠) · 로그인 · 갱신 · 로그아웃(세션 놓기 길)
const NO_INTERCEPT = Object.freeze(['/api/auth/update-status', '/api/auth/login', '/api/auth/refresh', '/api/auth/logout']);
const INJECTS = Object.freeze({
  503: { status: 503 },
  404: { status: 404 },
  403: { status: 403 },
  abort: { abort: 'failed' }, // 네트워크 끊김
  real403: { real: true }, // 가로채기 0 · 서버가 진짜 403
});

// ── 장면 표(설계 §5-2 · 키 = 결과표 path 칸 · 줄 = 고치기 전 판 dc49c2d0 기준) ──
//   tags = 그 줄의 표지(옛 줄 글자 앞머리 · 설계 §2-2 「표지 그대로」) — 대조군은 console.error 에서 · 고친 뒤는 console.warn 에서 센다.
//   snack = 그 자리에 원래 있던 알림 글(없으면 null) — 두 모드 다 1 이상이어야 함(사라지면 빨강 · 설계 §5-1 알림 칸).
const SCENES = Object.freeze([
  Object.freeze({ key: 'G-R1a', branch: 'R', who: 'owner', page: '/', match: { path: '/api/cs/replies/unread-count' }, inject: '503',
    tags: ['[CS] 안 읽은 답 수 조회 실패', '[Sidebar] CS 안읽음 수 조회 실패'], snack: null,
    lines: 'CsReplyBadge.razor:88 · Sidebar.razor:660', what: '상주 · CS 안 읽은 수 503(배너 · 사이드바 둘 다)' }),
  Object.freeze({ key: 'G-R1b', branch: 'R', who: 'owner', page: '/', match: { path: '/api/cs/replies/unread-count' }, inject: 'abort',
    tags: ['[CS] 안 읽은 답 수 조회 실패', '[Sidebar] CS 안읽음 수 조회 실패'], snack: null,
    lines: 'CsReplyBadge.razor:88 · Sidebar.razor:660', what: '상주 · CS 안 읽은 수 끊김(배너 · 사이드바 둘 다)' }),
  Object.freeze({ key: 'G-R2', branch: 'R', who: 'owner', page: '/', match: { path: '/api/auth/terms/status' }, inject: '403',
    tags: ['[HP15]'], snack: FORBID_TEXT,
    lines: 'TermsConsentBanner.razor:69', what: '상주 · 약관 상태 403(처리기 알림 그대로)' }),
  Object.freeze({ key: 'G-S1', branch: 'S', who: 'staff', page: '/accounting/bills', match: { path: '/api/finance/bills' }, inject: 'real403',
    tags: ['[Bills.List]'], snack: FORBID_TEXT,
    lines: 'BillsCardsBankService.cs:21', what: '공용 서비스 · 직원 창 어음 목록 서버 403(SalesOnly · 가로채기 0)' }),
  Object.freeze({ key: 'G-S2', branch: 'S', who: 'owner', page: '/', match: { path: '/api/dashboard/unified-calendar' }, inject: 'abort',
    tags: ['[UnifiedCalendarService.'], snack: null,
    lines: 'UnifiedCalendarService.cs:55', what: '공용 서비스 · 달력 월 조회 끊김(404 · 503 은 조용히 null — 줄에 안 닿음)' }),
  Object.freeze({ key: 'G-S3', branch: 'S', who: 'owner', page: '/return-status', match: { path: '/api/purchase/returns' }, inject: '503',
    tags: ['[작18] 매입반품 목록 조회 실패'], snack: null,
    lines: 'DeliveryService.cs:593', what: '공용 서비스 · 매입 반품 목록 503(반품현황 전표별 기본 보기)' }),
  Object.freeze({ key: 'G-P1', branch: 'P', who: 'owner', page: '/purchase/statistics', match: { path: '/api/reports/purchase-statistics' }, inject: '503',
    tags: ['[HP15]'], snack: null,
    lines: 'PurchaseStatisticsPage.razor:155', what: '업무 현황 · 매입 통계 503' }),
  Object.freeze({ key: 'G-P2', branch: 'P', who: 'owner', page: '/sales/statistics', match: { path: '/api/reports/sales-statistics' }, inject: '503',
    tags: ['[HP15]'], snack: '판매현황 조회에 실패했습니다',
    lines: 'SalesStatisticsPage.razor:155', what: '업무 현황 · 판매 통계 503(알림 그대로)' }),
  Object.freeze({ key: 'G-P3', branch: 'P', who: 'owner', page: '/stock', match: { path: '/api/reports/stock-status' }, inject: '503',
    tags: ['[HP15]'], snack: '재고현황 조회에 실패했습니다',
    lines: 'Stock.razor:132', what: '업무 현황 · 재고현황 503(알림 그대로)' }),
  Object.freeze({ key: 'G-C1', branch: 'C', who: 'owner', page: '/support/cs/ci-band-none', match: { re: '^/api/cs/requests/[^/]+/replies$' }, inject: '503',
    tags: ['[CS] 답 목록 조회 실패'], snack: null,
    lines: 'CsInboxPage.razor:179', what: '그 밖 · CS 상세 답 목록 503' }),
  Object.freeze({ key: 'G-C2', branch: 'C', who: 'owner', page: '/print-settings', match: { path: '/api/settings/print-common' }, inject: '404',
    tags: ['[PrintSettings] 공통 설정을 못 읽었다'], snack: '워터마크 설정을 불러오지 못했습니다',
    lines: 'PrintSettingsPage.razor:395', what: '설정 · 인쇄 공통 설정 404(알림 그대로)' }),
]);

// ── 순수 함수(자체시험 대상 · 화면 · 브라우저 0) ──
function modeOf(v) {
  return typeof v === 'string' && MODES.includes(v) ? v : null;
}
function urlPath(u) {
  try {
    return (u instanceof URL ? u : new URL(String(u))).pathname;
  } catch (e) {
    console.log(`띠 장면 주소 해석 실패 (${L.errorName(e)})`);
    return null;
  }
}
// 장면 주소 맞춤 — 경로만(쿼리 뗌) · 정확히 같음 또는 정규식(앞뒤 고정)
function routeMatch(scene, u) {
  const p = urlPath(u);
  if (p === null || !scene || !scene.match) return false;
  if (typeof scene.match.path === 'string') return p === scene.match.path;
  if (typeof scene.match.re === 'string') return new RegExp(scene.match.re).test(p);
  return false;
}
// 장면 목록 꼴 — 문제 글자 목록(빈 목록 = 정상). 판정 시작 전에 부르고, 문제가 있으면 아무것도 안 돌린다.
function sceneListProblems(list = SCENES) {
  const out = [];
  if (!Array.isArray(list) || list.length === 0) return ['장면 0'];
  const seen = new Set();
  list.forEach((s, i) => {
    const n = `${i + 1}번`;
    if (!s || typeof s !== 'object') { out.push(`${n} 꼴 아님`); return; }
    if (!/^G-[RSPC][0-9]+[a-z]?$/.test(String(s.key))) out.push(`${n} 키 꼴`);
    else if (s.key.charAt(2) !== s.branch) out.push(`${n} 갈래 어긋남`);
    if (seen.has(s.key)) out.push(`${n} 키 중복`);
    seen.add(s.key);
    if (!['R', 'S', 'P', 'C'].includes(s.branch)) out.push(`${n} 갈래 밖(R · S · P · C 만 — U 는 PR-2)`);
    if (!['owner', 'staff'].includes(s.who)) out.push(`${n} 창 주인 꼴`);
    if (!Object.prototype.hasOwnProperty.call(INJECTS, s.inject)) out.push(`${n} 주입 꼴`);
    if (typeof s.page !== 'string' || !s.page.startsWith('/')) out.push(`${n} 화면 주소 꼴`);
    else if (CLOSED_PATHS.includes(s.page.replace(/\/+$/, '') || '/')) out.push(`${n} 닫힌 화면`);
    const m = s.match || {};
    const hasPath = typeof m.path === 'string' && m.path.startsWith('/api/');
    const hasRe = typeof m.re === 'string' && m.re.startsWith('^/api/') && m.re.endsWith('$');
    if (hasPath === hasRe) out.push(`${n} 주소 맞춤 꼴(경로 하나 또는 앞뒤 고정 정규식 하나)`);
    for (const bad of NO_INTERCEPT) {
      if (routeMatch(s, `http://127.0.0.1${bad}`)) out.push(`${n} 가로채면 안 되는 주소(${bad})`);
    }
    if (!Array.isArray(s.tags) || s.tags.length === 0 || s.tags.some((t) => typeof t !== 'string' || !t.startsWith('[') || t.length < 3)) out.push(`${n} 표지 꼴`);
    if (!(s.snack === null || (typeof s.snack === 'string' && s.snack.length > 0))) out.push(`${n} 알림 글 꼴`);
    if (s.inject === 'real403' && s.who !== 'staff') out.push(`${n} 진짜 403 은 직원 창만`);
  });
  return out;
}
// 콘솔 한 줄 셈(원문은 넘겨받기만 · 저장 0). cur = { err: {표지: 수}, warn: {표지: 수}, appErr }
function newTally(tags) {
  const err = {};
  const warn = {};
  for (const t of tags) { err[t] = 0; warn[t] = 0; }
  return { err, warn, appErr: 0 };
}
function tallyLine(cur, type, text, tags) {
  const t = String(text ?? '');
  if (type === 'error' && t.trimStart().startsWith('[')) cur.appErr += 1; // 앱 표준오류 꼴(브라우저 자원 오류 「Failed to load …」 는 셈 밖 · 선례 G10)
  for (const tag of tags) {
    if (!t.includes(tag)) continue;
    if (type === 'error') cur.err[tag] += 1;
    else if (type === 'warning') cur.warn[tag] += 1;
  }
  return cur;
}
const cnt = (m, k) => (m && Object.prototype.hasOwnProperty.call(m, k) && Number.isInteger(m[k]) ? m[k] : 0);
// 장면 판정 — o = { routeHits, real403, band3, band10, err, warn, appErr, snackHit }
function judgeScene(mode, scene, o) {
  const why = [];
  if (!scene || !o) return { pass: false, why: ['재료 없음'] };
  const real = scene.inject === 'real403';
  const hits = real ? o.real403 : o.routeHits;
  if (!(Number.isInteger(hits) && hits >= 1)) why.push(real ? '서버 403 응답 0(직원 역할 · 장면 무효)' : '가로챔 0(화면이 그 주소를 안 부름 · 장면 무효)');
  if (scene.snack !== null && !(Number.isInteger(o.snackHit) && o.snackHit >= 1)) why.push('원래 알림 글 0');
  if (mode === 'control') {
    if (!(o.band3 === true || o.band10 === true)) why.push('흰 띠 0(대조군은 1 이어야 — 장면이 그 줄에 안 닿음)');
    for (const t of scene.tags) if (cnt(o.err, t) < 1) why.push(`표준오류 표지 0 ${t}`);
  } else if (mode === 'fixed') {
    if (o.band3 !== false || o.band10 !== false) why.push(`흰 띠 +3초 ${o.band3} · +10초 ${o.band10}(둘 다 거짓이어야)`);
    for (const t of scene.tags) if (cnt(o.warn, t) < 1) why.push(`경고 흔적 0 ${t}`);
    if (o.appErr !== 0) why.push(`앱 표준오류 꼴 ${o.appErr}`);
  } else {
    why.push('모드 꼴 이상');
  }
  return { pass: why.length === 0, why };
}
// 끝 코드 — 장면 키마다 정확히 한 줄 · 시험 줄 · 통과 · 로그아웃 실패 0 일 때만 0
function endCode(rows, keys = SCENES.map((s) => s.key), logoutFail = 0) {
  const mine = (Array.isArray(rows) ? rows : []).filter((x) => x && x.scope === SCOPE);
  for (const k of keys) {
    const r = mine.filter((x) => x.path === k);
    if (r.length !== 1 || r[0].kind !== 'TEST' || r[0].pass !== true) return 1;
  }
  if (mine.some((x) => !keys.includes(x.path))) return 1;
  return logoutFail === 0 ? 0 : 1;
}
function bandResultPath() {
  return path.join(L.outDir(), 'main', 'console-error-band-result.json');
}
function saveBand(r) {
  const p = bandResultPath();
  fs.mkdirSync(path.dirname(p), { recursive: true });
  const tmp = `${p}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(r, null, 2));
  fs.renameSync(tmp, p);
}

// ── 뜬 적 있는 알림 글(문서마다 새로 · 글자만 · 50 상한 · 보기 실패는 셈으로) ──
const SNACK_INIT = `(() => {
  try {
    if (window.__bandSnack) return;
    const seen = [];
    const done = new WeakSet();
    window.__bandSnack = seen;
    window.__bandSnackErr = 0;
    const scan = () => {
      try {
        const els = document.querySelectorAll('.mud-snackbar');
        for (let i = 0; i < els.length; i += 1) {
          const el = els[i];
          if (done.has(el)) continue;
          const t = String(el.innerText || el.textContent || '').replace(/\\s+/g, ' ').trim();
          if (!t) continue;
          done.add(el);
          if (seen.length < 50) seen.push(t.slice(0, 300));
        }
      } catch (e) {
        window.__bandSnackErr += 1;
      }
    };
    new MutationObserver(scan).observe(document, { childList: true, subtree: true, characterData: true });
  } catch (e) {
    window.__bandSnackErr = (window.__bandSnackErr || 0) + 1;
  }
})();`;

// ── 기록 ──
function recScene(st, scene, { pass, got, kind = 'TEST', err = null }) {
  if (st.r.rows.some((x) => x.scope === SCOPE && x.path === scene.key)) return; // 한 키 한 줄(먼저 쓴 줄이 이긴다)
  L.rec(st.r, { id: 'BAND', g: scene.branch, path: scene.key, browser: BROWSER, what: `${scene.what} · 모드 ${st.mode}`, got, pass, kind, scope: SCOPE, err });
  saveBand(st.r);
}
function devScene(st, scene, step, e) {
  const why = e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${L.errorName(e)}`;
  console.log(`띠 장면 ${scene.key} 단언 전 실패 — 단계=${step} · ${why}`);
  recScene(st, scene, { pass: false, got: `단언 전 실패 — 단계=${step} · ${why}`, kind: 'DEVICE', err: e instanceof K.KitStepError ? null : e });
}

// ── 브라우저 한 벌(창 주인 하나 · 로그인 한 번 · 장면마다 새 탭 = 새 문서) ──
async function openWho(st, who) {
  const tally = { continue: 0, abort: 0 };
  const context = await st.br.newContext({ baseURL: K.BASE });
  await K.blockExternal(context, st.r, tally);
  const tok = { access: null, deviceId: null, masked: 0 };
  K.watchTokens(context, tok);
  await context.addInitScript(SNACK_INIT);
  const kb = { who, context, tok, tally, closed: false, login: { seen: 0, status: null, terms: null, err: null } };
  st.kbs.push(kb);
  return kb;
}
async function loginWho(kb, loginId, password) {
  const page = await kb.context.newPage();
  page.on('response', async (res) => {
    if (!/\/api\/auth\/login(\?|$)/i.test(res.url()) || res.request().method() !== 'POST') return;
    try {
      const j = await res.json();
      kb.login.status = res.status();
      kb.login.terms = !!(j && (j.requiresTermsConsent ?? j.RequiresTermsConsent));
      kb.login.seen += 1;
    } catch (e) {
      kb.login.err = L.errorName(e);
    }
  });
  try {
    await page.goto(`${K.BASE}/login`, { waitUntil: 'domcontentloaded', timeout: PAGE_MS });
    await page.getByLabel('아이디', { exact: true }).fill(loginId, { timeout: PAGE_MS });
    await page.getByLabel('비밀번호', { exact: true }).fill(password);
    await page.getByLabel('비밀번호', { exact: true }).press('Tab');
    await page.getByRole('button', { name: '로그인', exact: true }).click();
    const s = await DC.settleScreen(page, 90000);
    const end = Date.now() + 10000;
    while (kb.login.seen === 0 && Date.now() < end) await K.sleep(250);
    await K.waitMasked(kb.tok);
    if (kb.login.seen === 0 || kb.login.status !== 200 || kb.login.terms !== false || !kb.tok.access || s.where !== 'main') {
      throw new K.KitStepError(`못 만듦 — ${kb.who} 로그인 상태 이상(응답 ${kb.login.seen}회 · status=${kb.login.status} · 약관 필요=${kb.login.terms} · 출입증 ${kb.tok.access ? '있음' : '없음'} · 착지 ${s.where}@${L.sanitize(String(s.path), 60)})`);
    }
  } finally {
    await page.close().catch((e) => console.log(`띠 장면 로그인 탭 닫기 실패 ${L.errorName(e)}`));
  }
}
async function closeWho(st, kb) {
  if (kb.closed) return;
  kb.closed = true;
  if (kb.tok.access) {
    const lo = await K.apiLogout({ who: `BAND-${kb.who}`, token: kb.tok.access, deviceId: kb.tok.deviceId });
    console.log(`띠 장면 ${kb.who} 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  }
  await kb.context.close().catch((e) => console.log(`띠 장면 컨텍스트 닫기 실패 ${L.errorName(e)}`));
}

async function look(page) {
  return K.withTimeout(page.evaluate(() => {
    const b = document.getElementById('blazor-error-ui');
    const band = !!b && getComputedStyle(b).display !== 'none';
    const snacks = Array.isArray(window.__bandSnack) ? window.__bandSnack.slice(0, 50) : null;
    return { band, snacks, snackErr: Number(window.__bandSnackErr || 0) };
  }), K.EVAL_MS, '화면 보기').catch((e) => {
    console.log(`띠 장면 화면 보기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e)}`);
    return { band: null, snacks: null, snackErr: null };
  });
}

// 장면 하나 — 새 탭 · 가로채기 걸기 · 화면 열기 · 착지 +3초 · +10초 보기 · 판정 · 탭 닫기
async function runScene(st, kb, scene) {
  const step = { now: '새 탭' };
  let page = null;
  try {
    page = await kb.context.newPage();
    const cur = newTally(scene.tags);
    const hit = { route: 0, real403: 0, statuses: [] };
    page.on('console', (msg) => tallyLine(cur, msg.type(), msg.text(), scene.tags));
    page.on('response', (res) => {
      if (!routeMatch(scene, res.url())) return;
      if (res.status() === 403) hit.real403 += 1;
      if (hit.statuses.length < 6) hit.statuses.push(res.status());
    });
    const inj = INJECTS[scene.inject];
    if (!inj.real) {
      step.now = '가로채기 걸기';
      await page.route((u) => routeMatch(scene, u), (route) => {
        hit.route += 1;
        // 탭을 닫을 때 날던 요청의 처리 거절은 이름만 찍고 받는다(받는 곳 없는 거절이 프로세스 밖 예외로 번지지 않게 · 빈 catch 0)
        const done = inj.abort ? route.abort(inj.abort) : route.fulfill({ status: inj.status, contentType: 'application/json', body: '{}' });
        return done.catch((e) => console.log(`띠 장면 ${scene.key} 가로채기 처리 거절 ${L.errorName(e)}`));
      });
    }
    step.now = '화면 열기';
    await page.goto(`${K.BASE}${scene.page}`, { waitUntil: 'domcontentloaded', timeout: PAGE_MS });
    const s = await DC.settleScreen(page, PAGE_MS);
    const landAt = Date.now();
    step.now = '브리핑 덮개 · 착지 보기';
    await DC.closeBriefing(page, { seen: 0, closed: 0, errs: 0, lastErr: null }, 3000);
    await K.sleep(Math.max(0, landAt + 3000 - Date.now()));
    const a = await look(page);
    await K.sleep(Math.max(0, landAt + 10000 - Date.now()));
    const b = await look(page);
    const snacks = [...new Set([...(a.snacks || []), ...(b.snacks || [])])];
    const snackHit = b.snacks === null && a.snacks === null ? null : (scene.snack === null ? 0 : snacks.filter((t) => t.includes(scene.snack)).length);
    const other = snacks.filter((t) => scene.snack === null || !t.includes(scene.snack));
    const o = { routeHits: hit.route, real403: hit.real403, band3: a.band, band10: b.band, err: cur.err, warn: cur.warn, appErr: cur.appErr, snackHit };
    const v = judgeScene(st.mode, scene, o);
    const showMap = (m) => scene.tags.map((t) => cnt(m, t)).join('/');
    // 기록만(판정 아님) — 다른 알림 글 수 · 첫 셋(소독) · 착지 · 응답 숫자
    console.log(`띠 장면 ${scene.key} 판정=${v.pass} · 모드 ${st.mode} · 착지 ${s.where}@${L.sanitize(String(s.path), 60)} · 가로챔 ${hit.route} · 서버403 ${hit.real403}`
      + ` · 응답 ${hit.statuses.join(',') || '없음'} · 흰 띠 +3초 ${a.band} +10초 ${b.band} · 표준오류 표지 ${showMap(cur.err)} · 경고 표지 ${showMap(cur.warn)}`
      + ` · 앱 표준오류 ${cur.appErr} · 원래 알림 ${snackHit} · 다른 알림 ${other.length} ${other.slice(0, 3).map((t) => K.consoleLine(t, 50)).join(' | ') || '-'}`
      + `${v.pass ? '' : ` · 이유 ${v.why.join(' · ')}`}`);
    const got = { m: st.mode, hit: scene.inject === 'real403' ? hit.real403 : hit.route, b3: a.band, b10: b.band, e: showMap(cur.err), w: showMap(cur.warn), app: cur.appErr, sn: snackHit, other: other.length, at: `${s.where}@${s.path}` };
    recScene(st, scene, { pass: v.pass, got: v.pass ? got : { ...got, why: v.why } });
  } catch (e) {
    devScene(st, scene, step.now, e);
  } finally {
    if (page) {
      await page.unrouteAll({ behavior: 'ignoreErrors' }).catch((e) => console.log(`띠 장면 가로채기 풀기 실패 ${L.errorName(e)}`));
      await page.close().catch((e) => console.log(`띠 장면 탭 닫기 실패 ${L.errorName(e)}`));
    }
  }
}

async function runWho(st, who, loginId, password) {
  const list = SCENES.filter((s) => s.who === who);
  if (list.length === 0) return;
  let kb = null;
  try {
    if (!loginId || !password) throw new K.KitStepError(`못 만듦 — ${who} 로그인 환경 칸 비어 있음`);
    kb = await openWho(st, who);
    await loginWho(kb, loginId, password);
  } catch (e) {
    for (const s of list) devScene(st, s, `${who} 로그인`, e);
    if (kb) await closeWho(st, kb);
    return;
  }
  try {
    for (const s of list) await runScene(st, kb, s);
  } finally {
    await closeWho(st, kb);
  }
}

async function cleanup(st) {
  if (st.cleaned) return;
  st.cleaned = true;
  for (const kb of st.kbs) await closeWho(st, kb);
  if (st.br) await st.br.close().catch((e) => console.log(`띠 장면 브라우저 닫기 실패 ${L.errorName(e)}`));
}
function fillLeft(st, why) {
  for (const s of SCENES) recScene(st, s, { pass: false, got: why, kind: 'DEVICE' });
}

async function main() {
  const mode = modeOf(env.UI_BAND_MODE);
  const st = { mode, r: { schema: 'console-error-band/1', mode, external_blocked: 0, external_hosts: [], rows: [] }, br: null, kbs: [], logoutFail: 0, cleaned: false };
  if (!mode) {
    console.log(`⚫ FAIL [장치] 띠 장면 — 모드 칸 UI_BAND_MODE 꼴 이상(${MODES.join(' · ')} 만) · 아무것도 안 돌린다`);
    process.exit(1);
  }
  const bad = sceneListProblems();
  if (bad.length > 0) {
    console.log(`⚫ FAIL [장치] 띠 장면 목록 꼴 이상 — ${bad.join(' · ')} · 아무것도 안 돌린다`);
    process.exit(1);
  }
  saveBand(st.r);
  console.log(`띠 장면 시작: 모드 ${mode} · 장면 ${SCENES.length}(${SCENES.map((s) => s.key).join(' ')}) · 결과표 따로(판정기 judge.js 무접촉)`);
  let fatalOnce = false;
  const fatal = (where) => (e) => {
    if (fatalOnce) return;
    fatalOnce = true;
    try {
      console.log(`띠 장면 프로세스 밖 예외(${where}) ${L.errorName(e)}`);
      fillLeft(st, `단언 전 실패 — 프로세스 밖 예외(${where})`);
    } catch (e2) {
      console.log(`⚫ 띠 장면 끊긴 자리 기록 실패 (${L.errorName(e2)})`);
    }
    setTimeout(() => process.exit(1), 10000).unref(); // 세션 놓기 — 10초 상한 뒤 무조건 종료
    cleanup(st).then(() => process.exit(1), () => process.exit(1));
  };
  process.on('unhandledRejection', fatal('unhandledRejection'));
  process.on('uncaughtException', fatal('uncaughtException'));
  for (const sig of ['SIGTERM', 'SIGINT']) process.on(sig, () => fatal(`신호 ${sig}`)(new K.KitStepError(`신호 ${sig}`)));
  setTimeout(() => fatal('띠 장면 전체 시간 상한')(new K.KitStepError(`띠 장면 전체 시간 상한 ${BAND_TOTAL_MS / 60000}분`)), BAND_TOTAL_MS).unref();

  let pre = null;
  try {
    const pw = require('playwright');
    st.br = await pw[BROWSER].launch();
  } catch (e) {
    pre = `브라우저 기동 실패 ${L.errorName(e)}`;
  }
  if (pre) {
    console.log(`띠 장면 시작 전 실패 — ${pre}`);
    fillLeft(st, `단언 전 실패 — ${pre}`);
  } else {
    await runWho(st, 'owner', env.UI_LOGIN_PARENT || 'ci-parent', env.UI_PW_PARENT || '');
    await runWho(st, 'staff', env.UI_LOGIN_NA || 'ci-na', env.UI_PW_NA || '');
  }
  fillLeft(st, '장면 끝 뒤 남은 키(있으면 장치 오류)');
  await cleanup(st);
  const mine = st.r.rows.filter((x) => x.scope === SCOPE);
  const code = endCode(st.r.rows, SCENES.map((s) => s.key), st.logoutFail);
  console.log(`띠 장면 끝: 모드 ${mode} · 줄 ${mine.length}/${SCENES.length} · 통과 ${mine.filter((x) => x.pass === true).length} · 거짓 ${mine.filter((x) => x.pass !== true).length}`
    + ` · 로그아웃 실패 ${st.logoutFail} · 외부 차단 ${st.r.external_blocked} · 끝 코드 ${code}`);
  saveBand(st.r);
  process.exit(code);
}

if (require.main === module) main();
module.exports = {
  SCENES, MODES, SCOPE, INJECTS, CLOSED_PATHS, NO_INTERCEPT, FORBID_TEXT,
  modeOf, routeMatch, sceneListProblems, newTally, tallyLine, judgeScene, endCode, bandResultPath,
};
