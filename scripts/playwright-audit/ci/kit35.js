// K35 본문 공용 도우미 (작업지시서 20261106작 #691 재기반 §5-2 · §5-3 · PM 결재 A4 · C5 · PM 결정 3V-1)
//
// 🔴 이 파일 이름은 K 본문 머리로 시작하지 않는다 — 실행기(run-k-bodies.js)·판정기가 본문으로 돌리지 않는다(판정기 ⑧ 규칙).
// 🔴 이 파일의 규칙
//   ① K 의 기록은 recK 하나로만 — 범위 칸 K 고정 · G 기대 키(judge.js EXPECT_G) 밖이면 던진다 · 같은 키 두 번이면 던진다.
//      G 키 통과는 TEST 로만 · N4 키는 DEVICE 로만(판정기 규칙과 같은 꼴을 기록 앞에서 한 번 더 막는다).
//   ② K 의 모든 그림은 guardedShot 하나를 지난다 — 찍기 직전 화면 글자 대조 → 걸리거나 예외면 안 찍음 →
//      찍음 → 찍은 직후 다시 대조 → 걸리거나 예외면 지움(지우기 실패 = K 그림 폴더 통째 지움).
//      대조 = 모든 창(본 창 + 열린 팝업 전부) · 모든 프레임 · 보이는 글자 + 입력 값 + 자리표시 글자 + 고른 선택 글자 · 대소문자 무시.
//      비밀번호 칸(가림형) = 값을 대조에서 빼고, 비밀번호 칸으로 보이는데 가림형이 아닌 칸에 값이 있으면 걸림.
//      🔴 그림 픽셀 = 미측정(이 대조가 못 보는 자리의 목록 = 개발 세부 · 로컬).
//      「화면 글자 대조 통과 = 그림 무누출」 이 아니다.
//   ③ 브라우저 로그인 응답의 접속·갱신 토큰은 받는 즉시 가리고 누출 검사 대상으로 넘긴다(가린 토큰 0 = N4 거짓).
//   ④ N4 거짓이면 다음 단계에 표지(UI_N4_HIT=1)를 넘긴다 — 업로드 단계가 그 표지면 올리지 않는다(워크플로 조건 한 줄).
//   ⑤ 브라우저 콘솔 글자를 찍을 때는 소독 → 제어 글자 가림 순서. 예외는 이름만 콘솔에(빈 catch 0 · #15).
// 🔴 3차(PM 결정 3V-2 · 4V-2) — 위 규칙에 더한다
//   ②′ 그림은 업로드 자리 밖 임시 자리(stageDir)에 찍고, 찍은 직후 대조를 지난 파일만 업로드 자리(shotsDir)로 옮긴다.
//      찍기 직전 · 찍을 때 · 찍은 직후의 열린 창 집합이 하나라도 다르면 그 묶음은 확인 안 됨(옮기지 않음 · 대조 못 함).
//      N4 줄을 적기 시작하면(정상 길 · 예외 길 모두) 봉인 — 그 뒤 옮김 0. 강제 종료 · 시간초과 · 취소에도 업로드 자리엔 대조 지난 그림만.
//   ④′ N4 거짓 이유 = 「누출 신호」(걸림 · 가림형 아닌 비밀번호 칸 · 지우기 실패) / 「대조 못 함(장치)」(가린 토큰 0 · 통과 그림 0 ·
//      대조 예외 · 창 집합 다름 · 예외 길 강제 거짓). 표지(UI_N4_HIT=1) 쓰기를 결과표 기록보다 **먼저**.
//   ⑤′ 콘솔 줄은 쌓아 두었다가 찍는 그 시점 등록 목록으로 소독한다(토큰 등록 전에 온 줄도 등록 뒤 소독).
//   ⑥ 화면 평가(evaluate)마다 시간 상한 · 막기 · 녹음기 초기 스크립트는 한 덩어리(막기 먼저 · 순서를 코드로 고정).
'use strict';

const fs = require('fs');
const path = require('path');
const L = require('./lib');
const J = require('./judge');
const DC = require('./device-check'); // 머리 조건(require.main) 때문에 불러도 장치 본문은 안 돈다 — 내보냄만 쓴다

const BASE = process.env.UI_BASE || 'http://127.0.0.1:18080';
const ALLOWED_HOSTS = new Set(['127.0.0.1', 'localhost']);
const UA = 'Mozilla/5.0 (X11; Linux x86_64) HitPanCiSeed/1'; // 시드 API 로그인과 같은 꼴(같은 기기로 읽히게)
const POPUP_NAME = 'hitpanChat'; // 상단바 메신저 · 알림 팝업 공통 창 이름(hitpan-chat-window.js)

// ── ① 기록 ─────────────────────────────────────────────────────────────
const K_ALLOWED = new Set(J.EXPECT_G);
const K_DEVICE = J.EXPECT_G_DEVICE;
class KitContractError extends Error {
  constructor(message) {
    super(message);
    this.name = 'KitContractError';
  }
}
function kKey(key) {
  return J.rowKey({ scope: 'K', id: key.id, g: key.g, path: key.path ?? null, browser: key.browser });
}
const written = new WeakMap();
function writtenSet(r) {
  let s = written.get(r);
  if (!s) {
    s = new Set();
    written.set(r, s);
  }
  return s;
}
// recK(r, { key: { id, g, path, browser }, what, got, pass, kind, err })
function recK(r, { key, what, got, pass, kind, err = null } = {}) {
  if (!key || typeof key !== 'object') throw new KitContractError('recK: 키 없음');
  const k = kKey(key);
  if (!K_ALLOWED.has(k)) throw new KitContractError(`recK: G 기대 키 밖 (${L.sanitize(k, L.META_CAP)})`);
  if (kind !== 'TEST' && kind !== 'DEVICE') throw new KitContractError(`recK: kind 는 TEST/DEVICE 뿐 (${L.sanitize(String(kind), 40)})`);
  if (K_DEVICE.has(k) ? kind !== 'DEVICE' : (kind === 'DEVICE' && pass === true)) {
    throw new KitContractError(`recK: G 통과 꼴 이상 — G 줄 통과는 TEST · N4 줄은 DEVICE (${L.sanitize(k, L.META_CAP)})`);
  }
  const done = writtenSet(r);
  if (done.has(k)) throw new KitContractError(`recK: 같은 키 두 번 (${L.sanitize(k, L.META_CAP)})`);
  const row = L.rec(r, { id: key.id, g: key.g, path: key.path ?? null, browser: key.browser, scope: 'K', what, got, pass, kind, err });
  done.add(k);
  return row;
}
function isWritten(r, key) {
  return writtenSet(r).has(kKey(key));
}
// 단언 전에 섰을 때 — 아직 안 적은 키를 DEVICE 거짓으로 채운다(키 누락으로 이유가 가려지지 않게).
function fillDevice(r, keys, why, err = null) {
  let n = 0;
  for (const key of keys) {
    if (isWritten(r, key)) continue;
    recK(r, { key, what: '단언 전 실패(로그인 · 알림 만들기 · 시간초과 등) — 이 줄은 시험 결과가 아니다', got: why, pass: false, kind: 'DEVICE', err });
    n += 1;
  }
  return n;
}
// 판정기 기대 키(브라우저별) — K 본문이 따로 목록을 들지 않는다.
function expectKeys(browser) {
  return J.EXPECT_G_ROWS.filter((x) => x[3] === browser).map(([id, g, p, b]) => ({ id, g, path: p, browser: b }));
}

// ── G3 비교식 · 자체 점검 ───────────────────────────────────────────
function sameWindowName(a, b) {
  return typeof a === 'string' && a.length > 0 && a === b;
}
// 이름을 바꾼 가짜 기록 1건으로 비교식이 빨강을 내는지 먼저 본다(원 오더 §4-3 ⓓ). 참이면 비교식이 일한다.
function g3SelfCheck(compare = sameWindowName) {
  const same = compare(POPUP_NAME, POPUP_NAME);
  const renamed = compare(POPUP_NAME, `${POPUP_NAME}-renamed`);
  return { pass: same === true && renamed === false, got: { 같은이름: same, 바꾼이름: renamed } };
}

// ── N4 대조(순수) ───────────────────────────────────────────────────
// 등록값마다 소독과 같은 3꼴(원문 · base64 · url) 글자를 만들고, 대소문자를 무시해 글자 안에 있나 본다. 걸린 값 수만 돌린다(글자 0).
function domLeakHits(text, registered) {
  const hay = String(text ?? '').toLowerCase();
  let hits = 0;
  for (const v of registered || []) {
    if (typeof v !== 'string' || v.length < 4) continue; // 소독(sanitize)과 같은 하한
    let hit = false;
    for (const { strs } of L.needleForms(v)) {
      for (const s of strs) {
        if (s && hay.includes(s.toLowerCase())) { hit = true; break; }
      }
      if (hit) break;
    }
    if (hit) hits += 1;
  }
  return hits;
}
function registeredValues() {
  return L.collectSecrets().map((x) => x.value).filter((v) => typeof v === 'string' && v.length >= 4);
}

// ── ⑤ 콘솔 줄 ──────────────────────────────────────────────────────
function ctl(s) {
  return String(s).replace(/[\u0000-\u001f\u007f-\u009f\u2028\u2029]/g, (c) => `\\u${c.charCodeAt(0).toString(16).padStart(4, '0')}`);
}
// 소독 → 제어 글자 가림(순서 고정 · 값 안 제어 글자로 소독 대조가 깨지지 않게)
function consoleLine(text, cap = 300) {
  return ctl(L.sanitize(text, cap));
}
// 🔴 3차(3V-2 · 3V2-05) — 쌓아 둔 콘솔 줄을 지금 등록 목록으로 소독해 찍고 비운다(원문은 메모리에만 · 상한 20줄은 쌓을 때).
function flushConsole(kb) {
  const buf = Array.isArray(kb.consoleBuf) ? kb.consoleBuf.splice(0) : [];
  for (const x of buf) {
    // ⬛ [낡은 줄 · R1/R2] if (x.kind === 'pageerror') console.log(`K 브라우저 화면 예외(${kb.browser}) ${x.name}`);
    // 🔴 R2(11/7 · 작업지시서 20261107작 §5) — 받을 때 1회(watchConsole) + 찍는 그 시점 등록 목록으로 이름 한 번 더(받은 뒤 등록된 값과 같으면 Error).
    if (x.kind === 'pageerror') console.log(`K 브라우저 화면 예외(${kb.browser}) ${L.errorName({ name: x.name })}`);
    else console.log(`K 브라우저 콘솔(오류 · ${kb.browser}) ${consoleLine(x.text)}`);
  }
  return buf.length;
}

// ── ⑥ 시간 상한 ────────────────────────────────────────────────────
const EVAL_MS = 10000; // 화면 평가 한 번
const SHOT_MS = 15000; // 그림 한 장
// 상한 안에 안 끝나면 KitStepError(그 단계 = 단언 전 실패 · DEVICE). 원래 약속은 경주가 받아 주므로 거절이 버려지지 않는다.
function withTimeout(p, ms, what) {
  let t = null;
  const limit = new Promise((_, no) => {
    t = setTimeout(() => no(new KitStepError(`시간 상한 ${Math.round(ms / 1000)}초 — ${what}`)), ms);
  });
  return Promise.race([Promise.resolve(p), limit]).finally(() => clearTimeout(t));
}

// ── window.open 막기(G2) · 녹음기는 device-check 내보냄 그대로 ─────────
// 막기 스크립트를 **먼저** 건다 → 녹음기가 그 위를 감싼다(녹음기 기록 → 막기 → null). 막기는 탭 저장소 표지가 '1' 일 때만.
const BLOCKER = `(() => {
  const orig = window.open;
  window.open = function (url, name, features) {
    let on = null;
    try { on = window.sessionStorage.getItem('__k35block'); } catch (e) { on = null; }
    if (on === '1') return null;
    return orig.call(window, url, name, features);
  };
})();`;
// 🔴 3차(PM 결정 4V-2 P2-c) — 초기 스크립트 둘을 따로 걸면 실행 순서가 정해져 있지 않다(Playwright 공개 문서).
//   한 덩어리로 합쳐 순서를 코드로 고정한다: 막기 먼저 → 녹음기가 그 위를 감싼다(녹음기 기록 → 막기 → null).
function initScript(recorder) {
  return `${BLOCKER}\n${recorder}`;
}
const INIT_SCRIPT = initScript(DC.RECORDER);

// ── API(가 · 부모) ─────────────────────────────────────────────────
class KitStepError extends Error {
  constructor(message) {
    super(message);
    this.name = 'KitStepError';
  }
}
async function apiCall(method, p, { sess = null, body } = {}) {
  const headers = { 'Content-Type': 'application/json', 'User-Agent': UA };
  if (sess && sess.token) headers.Authorization = `Bearer ${sess.token}`;
  if (sess && sess.deviceId) headers['X-HitPan-Device-Id'] = sess.deviceId;
  const res = await fetch(`${BASE}${p}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await res.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch (e) {
    console.log(`응답 JSON 아님 ${method} (원문 생략 · ${L.errorName(e)})`);
  }
  return { status: res.status, json };
}
// 로그인 — 응답을 받는 즉시 토큰부터 가린다(첫 출력 · 본문 출력 0). 시드와 같은 기기 꼴.
async function apiLogin(who, loginId, password) {
  const res = await apiCall('POST', '/api/auth/login', {
    body: { email: loginId, password, deviceType: 'pc', deviceName: `ci-${who}`, deviceFingerprint: `ci-${who}-fp` },
  });
  const j = res.json || {};
  const access = j.accessToken || j.AccessToken;
  const refresh = j.refreshToken || j.RefreshToken;
  if (access) L.maskAndRegister(`TOKEN_ACCESS_K_${who}`, access);
  if (refresh) L.maskAndRegister(`TOKEN_REFRESH_K_${who}`, refresh);
  if (res.status !== 200 || !access) throw new KitStepError(`${who} 로그인 status=${res.status}`);
  if (j.deviceAwaitingApproval || j.DeviceAwaitingApproval) throw new KitStepError(`${who} 기기 승인 대기`);
  return { who, token: access, deviceId: j.deviceId || j.DeviceId || null };
}
async function apiLogout(sess) {
  if (!sess || !sess.token) return { ok: true, got: '연 세션 없음' };
  try {
    const res = await apiCall('POST', '/api/auth/logout', { sess, body: {} });
    return { ok: res.status === 200, got: `${sess.who} status=${res.status}` };
  } catch (e) {
    return { ok: false, got: `${sess.who} 요청 실패 ${L.errorName(e)}` };
  }
}

// ── 브라우저 ────────────────────────────────────────────────────────
const sleep = (ms) => new Promise((ok) => setTimeout(ok, ms));
function pathOf(u) {
  try {
    const x = new URL(u);
    return `${x.pathname}${x.search}`;
  } catch (e) {
    return `(주소 해석 실패 ${L.errorName(e)})`;
  }
}

// 외부 요청 끊기 — 장치와 같은 꼴(호스트만 센다 · 닫힌 창 거절만 삼킴).
async function blockExternal(context, r, tally) {
  const closed = (route) => {
    try {
      return route.request().frame().page().isClosed();
    } catch (e) {
      console.log(`K 외부 차단: 창 상태 못 읽음 (${L.errorName(e)})`);
      return false;
    }
  };
  await context.route('**/*', (route) => {
    let host = '';
    const u = route.request().url();
    try {
      host = new URL(u).hostname;
    } catch (e) {
      console.log(`K 외부 차단: 주소 해석 실패 (${L.errorName(e)})`);
    }
    if (ALLOWED_HOSTS.has(host) || u.startsWith('data:') || u.startsWith('blob:')) {
      return L.guardRoute(route.continue(), () => closed(route), tally, 'continue');
    }
    r.external_blocked = (r.external_blocked || 0) + 1;
    if (!Array.isArray(r.external_hosts)) r.external_hosts = [];
    if (host && !r.external_hosts.includes(host)) r.external_hosts.push(host);
    return L.guardRoute(route.abort('blockedbyclient'), () => closed(route), tally, 'abort');
  });
}

// 🔴 R1 · P2(11/7 · 작업지시서 20261107작 §9 P2 · C5 창 「화면 먼저 · 등록 나중」) — 로그인·갱신 응답 본문을 아직 읽는 중인 손잡이 수(모듈 칸 · 값 0 · 수만).
//   guardedShot 이 찍기 직전 대조 앞 · 찍은 직후 대조 뒤 두 번 보고 0 이 아니면 「대조 못 함」(그 응답의 토큰이 화면에 먼저 그려졌을 수 있다).
let pendingTokenReads = 0;
// ③ 로그인·갱신 응답 토큰 — 받는 즉시 가림·등록.
function watchTokens(context, tok) {
  context.on('response', async (res) => {
    if (!/\/api\/auth\/(login|refresh)(\?|$)/i.test(res.url())) return;
    pendingTokenReads += 1; // 🔴 P2 — 토큰 길의 첫 줄 · 첫 await 전(거른 응답은 위 줄에서 동기로 돌아가 셈과 무관)
    try {
      const j = await res.json();
      const access = j && (j.accessToken || j.AccessToken);
      const refresh = j && (j.refreshToken || j.RefreshToken);
      if (access) { L.maskAndRegister('TOKEN_ACCESS_K_BROWSER', access); tok.access = access; tok.masked += 1; }
      if (refresh) { L.maskAndRegister('TOKEN_REFRESH_K_BROWSER', refresh); tok.masked += 1; }
      const dev = j && (j.deviceId || j.DeviceId);
      if (dev) tok.deviceId = String(dev);
    } catch (e) {
      console.log(`K 토큰 가로채기: 본문 읽기 실패 (원문 생략 · ${L.errorName(e)})`);
    } finally {
      pendingTokenReads -= 1; // 🔴 P2 — 등록(또는 실패) 뒤에만 내린다
    }
  });
}
async function waitMasked(tok, ms = 10000) {
  const end = Date.now() + ms;
  while (tok.access === null && Date.now() < end) await sleep(250);
  return tok.masked;
}

// ⑤ 브라우저 콘솔 오류 줄(창마다 · 상한 20줄)
// ⬛ [낡은 꼴 · 2차] 받는 즉시 소독해 찍었다 — 토큰 등록 전에 온 줄은 그 토큰을 모르는 소독을 지났다(3V2-05).
// 🔴 3차 — 받으면 쌓기만 하고(원문은 메모리에만) 찍기는 flushConsole(찍는 그 시점 등록 목록으로 소독).
function watchConsole(context, kb) {
  if (!Array.isArray(kb.consoleBuf)) kb.consoleBuf = [];
  const attach = (p) => {
    p.on('console', (msg) => {
      if (msg.type() !== 'error' || kb.consoleLines >= 20) return;
      kb.consoleLines += 1;
      // ⬛ [낡은 줄 · 2차] console.log(`K 브라우저 콘솔(오류 · ${kb.browser}) ${consoleLine(msg.text())}`);
      kb.consoleBuf.push({ kind: 'console', text: String(msg.text()) });
    });
    p.on('pageerror', (e) => {
      if (kb.consoleLines >= 20) return;
      kb.consoleLines += 1;
      // ⬛ [낡은 줄 · 2차] console.log(`K 브라우저 화면 예외(${kb.browser}) ${L.errorName(e)}`);
      kb.consoleBuf.push({ kind: 'pageerror', name: L.errorName(e) });
    });
  };
  for (const p of context.pages()) attach(p);
  context.on('page', attach);
}

// 화면 로그인(나) — 장치와 같은 화면 동작 · 자리 잡기는 장치 내보냄(settleScreen · 기기 등록 안내 창 닫기 포함).
async function screenLogin(page, loginId, password) {
  const note = { closed: 0, errs: 0 };
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  await page.getByLabel('아이디', { exact: true }).fill(loginId, { timeout: 60000 });
  await page.getByLabel('비밀번호', { exact: true }).fill(password);
  await page.getByLabel('비밀번호', { exact: true }).press('Tab');
  await page.getByRole('button', { name: '로그인', exact: true }).click();
  const s = await DC.settleScreen(page, 90000, note);
  return { ...s, note };
}

// 알림 배관(SignalR) 연결 확인 — 웹소켓이 열렸나만 센다(주소 · 질의 글자 0).
function watchHub(page, hub) {
  page.on('websocket', (ws) => {
    if (/\/hubs\/notify/i.test(ws.url())) hub.ws += 1;
  });
}
async function waitHub(hub, ms = 30000) {
  const end = Date.now() + ms;
  while (hub.ws === 0 && Date.now() < end) await sleep(250);
  if (hub.ws > 0) await sleep(1500); // 연결 직후 그룹 가입 몫
  return hub.ws > 0;
}

// 브리핑 덮개 — 장치 내보냄(closeBriefing) 그대로 · H-BRIEF 줄은 쓰지 않는다(키 상한).
async function briefOff(page, kb, appearMs = 0) {
  const ok = await DC.closeBriefing(page, kb.br, appearMs);
  if (!ok) throw new KitStepError(`브리핑 덮개 못 닫음(${kb.br.lastErr || '-'})`);
}

// 토스트(스낵바)가 다 사라질 때까지 — 마우스는 화면 가운데 빈 자리로 옮겨 둔다(토스트 위에 머물지 않게).
async function waitNoToasts(page, ms = 25000) {
  await page.mouse.move(420, 420).catch((e) => console.log(`K 마우스 옮기기 실패 ${L.errorName(e)}`));
  const end = Date.now() + ms;
  while (Date.now() < end) {
    if ((await page.locator('.mud-snackbar').count()) === 0) return true;
    await sleep(250);
  }
  return false;
}
async function waitToast(page, title, ms = 25000) {
  const t = page.locator('.mud-snackbar', { hasText: title }).first();
  await t.waitFor({ state: 'visible', timeout: ms });
  return t;
}
async function clickToast(page, title) {
  const t = await waitToast(page, title, 10000);
  await t.locator('.mud-snackbar-content-message').click({ timeout: 10000 });
}
// 상단바 오른쪽 단추(TopHeader.razor · NotificationBell.razor aria-label 「알림」 · ChatButton.razor aria-label 「메신저」)
const BELL_BUTTON = '.hitpan-top-header-right button[aria-label="알림"]';
const MESSENGER_BUTTON = '.hitpan-top-header-right button[aria-label="메신저"]';
async function clickMessenger(page) {
  await page.locator(MESSENGER_BUTTON).first().click({ timeout: 15000 });
}
// ⬛ [낡은 몸통 · 1~3차] 종 클릭 → 항목 보임 → 항목 클릭 한 줄기 · 실패하면 예외 이름만 남았다(첫 K 런: 종 길 8 전부 TimeoutError · 어느 줄인지 · 왜인지 모름).
// async function clickBellItem(page, text) {
//   await page.locator(BELL_BUTTON).first().click({ timeout: 15000 });
//   const item = page.locator('.mud-menu-item', { hasText: text }).first();
//   await item.waitFor({ state: 'visible', timeout: 15000 });
//   await item.click({ timeout: 15000 });
// }
// 🔴 4차(첫 K 런 뒤) — 「누르기까지」 장치 단계만 단단하게 + 진단. 무엇을 재는지(G 단언)는 그대로.
//   하위 단계 = 종 단추 찾기 → 종 클릭 → 메뉴 열림 → 항목 보임 → 항목 클릭. 메뉴가 이미 열려 있으면 종을 다시 누르지 않는다(닫힘 방지).
//   항목 클릭 전 단계에서 실패하면 다시 열기 1회. 항목 클릭에서 실패하면 다시 안 한다(두 번 누름 = 창 2 → 단언이 거짓 빨강).
//   팝업 기다림(arm)은 항목 클릭 **바로 전**에 건다 — 메뉴 여는 시간이 팝업 상한을 먹지 않게(clickVia 주석의 뜻 그대로).
//   실패하면 KitStepError 글 = 하위 단계 이름 + 소독한 짧은 사유 + 그 순간 숫자(항목 글자 원문 0 · 수만).
const BELL_MENU = '.mud-popover-open:has(.mud-menu-item), .mud-popover-open:has(.mud-menu-list)';
const BELL_ITEM = '.mud-popover-open .mud-menu-item';
// 사유(순수) — Playwright 오류 글에서 고정 낱말과 「가로막은 요소」 의 태그·클래스 이름만 꺼낸다(원문 줄 0).
//   클래스 낱말 = 영숫자 · - · _ 만 · 낱말 40자 · 최대 3개 → 제어 글자 0 → 소독(등록값 가림) → 길이 상한.
const BELL_WHY_WORDS = [
  [/intercepts pointer events/i, '가로막음'],
  [/not attached|detached/i, '떨어짐'],
  [/not visible/i, '안 보임'],
  [/not stable/i, '안정 안 됨'],
  [/not enabled|is disabled/i, '안 켜짐'],
  [/outside of the viewport/i, '화면 밖'],
  [/Timeout \d+ms exceeded|TimeoutError/i, '시간초과'],
];
function bellWhy(msg, cap = 60) {
  const s = String(msg ?? '');
  const words = BELL_WHY_WORDS.filter(([re]) => re.test(s)).map(([, w]) => w);
  let who = '';
  const line = s.split(/\r?\n/).find((x) => /intercepts pointer events/i.test(x)) || '';
  const m = line.match(/<([A-Za-z][A-Za-z0-9-]{0,20})[\s>]/);
  if (m) {
    const cls = (line.slice(m.index).match(/\sclass="([^"]*)"/) || [])[1] || ''; // 가로막은 요소의 첫 class="…"(값 안 > 에 안 끊기게)
    const toks = cls.split(/\s+/).filter((t) => /^[A-Za-z_][A-Za-z0-9_-]{0,39}$/.test(t)).slice(0, 3);
    who = [m[1].toLowerCase(), ...toks].join('.');
  }
  const out = `${words.length ? words.join('·') : '기타'}${who ? ` ${who}` : ''}`.replace(/[\u0000-\u001f\u007f-\u009f\u2028\u2029]/g, ' ');
  return L.sanitize(out, cap);
}
// 그 순간 숫자 → 글(순수) · 정수가 아니면 「?」(글자 값이 들어와도 찍지 않는다).
const BELL_COUNT_KEYS = [['bell', '종'], ['act', '감쌈'], ['menus', '열린메뉴'], ['items', '항목'], ['allItems', '전체항목'], ['overlays', '덮개'], ['dialogs', '대화상자'], ['top', '종맨위']];
function bellCountsText(c) {
  if (!c || typeof c !== 'object') return '숫자 못 셈';
  if (c.err !== undefined) return `숫자 못 셈(${/^[A-Za-z0-9_]{1,40}$/.test(String(c.err)) ? c.err : 'Error'})`;
  return `숫자 ${BELL_COUNT_KEYS.map(([k, n]) => `${n} ${Number.isInteger(c[k]) ? c[k] : '?'}`).join(' ')}`;
}
const BELL_SUBS = new Set(['종 단추 찾기', '종 클릭', '메뉴 열림', '항목 보임', '항목 클릭']);
function bellStepMessage(sub, why, tries, counts) {
  const s = BELL_SUBS.has(sub) ? sub : '?';
  const n = Number.isInteger(tries) ? tries : '?';
  return `종 길 하위=${s} · 사유=${L.sanitize(String(why ?? ''), 80)} · 열기 ${n}번 · ${bellCountsText(counts)}`;
}
// 그 순간 숫자 — 종 단추 · 메뉴 감쌈 · 열린 메뉴 · 열린 메뉴 항목 · 전체 메뉴 항목 · 보이는 덮개 · 대화상자 · 종 가운데 맨 위가 종인가(1/0 · 종 없음 -1)
async function bellCounts(page) {
  try {
    return await withTimeout(page.evaluate(([b, menu, item]) => {
      const q = (s) => document.querySelectorAll(s).length;
      const vis = (s) => [...document.querySelectorAll(s)].filter((e) => {
        const r = e.getBoundingClientRect();
        const cs = window.getComputedStyle(e);
        return r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && cs.display !== 'none';
      }).length;
      const el = document.querySelector(b);
      let top = -1;
      if (el) {
        const r = el.getBoundingClientRect();
        const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
        top = hit && (hit === el || el.contains(hit)) ? 1 : 0;
      }
      return { bell: q(b), act: q('.hitpan-top-header-right .mud-menu-activator'), menus: q(menu), items: q(item), allItems: q('.mud-menu-item'), overlays: vis('.mud-overlay'), dialogs: q('.mud-dialog'), top };
    }, [BELL_BUTTON, BELL_MENU, BELL_ITEM]), EVAL_MS, '종 길 숫자');
  } catch (e) {
    return { err: L.errorName(e) };
  }
}
async function clickBellItem(page, text, { title = null, arm = null } = {}) {
  const bell = page.locator(BELL_BUTTON).first();
  const menu = page.locator(BELL_MENU);
  let item = page.locator(BELL_ITEM, { hasText: text });
  if (title && title !== text) item = item.filter({ hasText: title }); // 제목 + 본문 일부
  item = item.first();
  let sub = '종 단추 찾기';
  let tries = 0;
  let last = null;
  try {
    await bell.waitFor({ state: 'visible', timeout: 10000 });
  } catch (e) {
    last = e;
  }
  if (!last) {
    for (tries = 1; tries <= 2; tries += 1) {
      last = null;
      try {
        if ((await menu.count()) === 0) {
          sub = '종 클릭';
          await bell.click({ timeout: 8000 });
          sub = '메뉴 열림';
          await menu.first().waitFor({ state: 'visible', timeout: 4000 });
        }
        sub = '항목 보임';
        await item.waitFor({ state: 'visible', timeout: 6000 });
        sub = '항목 클릭';
        const popP = typeof arm === 'function' ? arm() : null;
        await item.click({ timeout: 8000 });
        console.log(`K 종 길 누름 — 열기 ${tries}번`);
        return popP;
      } catch (e) {
        last = e;
        if (sub === '항목 클릭') break;
        if (tries < 2) {
          console.log(`K 종 길 ${tries}번째 실패 · 다시 열기 — ${bellStepMessage(sub, bellWhy(e && e.message), tries, await bellCounts(page))}`);
          await page.mouse.move(420, 420).catch((e2) => console.log(`K 마우스 옮기기 실패 ${L.errorName(e2)}`));
          await sleep(800);
        }
      }
    }
  }
  throw new KitStepError(bellStepMessage(sub, bellWhy(last && last.message), tries, await bellCounts(page)));
}
async function toastTexts(page) {
  try {
    // ⬛ [낡은 줄 · 2차] return await page.evaluate(() => [...document.querySelectorAll('.mud-snackbar')]
    return await withTimeout(page.evaluate(() => [...document.querySelectorAll('.mud-snackbar')]
      .map((e) => String(e.innerText || '').replace(/\s+/g, ' ').trim().slice(0, 80)).slice(0, 4)), EVAL_MS, '토스트 글자 읽기');
  } catch (e) {
    return [`읽기 실패 ${L.errorName(e)}`];
  }
}

// 녹음기(장치 RECORDER) 읽기 · 비우기 · 막기 표지
async function recorder(page, op) {
  // ⬛ [낡은 줄 · 2차] return page.evaluate((o) => {
  return withTimeout(page.evaluate((o) => {
    if (!window.__k35) return { err: window.__k35err || '녹음기 없음' };
    if (o === 'clear') { window.__k35.clear(); return { ok: true }; }
    if (o === 'block-on') { window.sessionStorage.setItem('__k35block', '1'); return { ok: true }; }
    if (o === 'block-off') { window.sessionStorage.removeItem('__k35block'); return { ok: true }; }
    return { list: window.__k35.list() };
  }, op), EVAL_MS, `녹음기 ${op}`);
}
// 🔴 3차(PM 결정 4V-2 P1-a) — G4 경우마다 본 창을 중립 주소(로그인 뒤 자리)로 되돌린다. 판단은 순수 함수.
//   중립 주소를 모르거나 중립 = 목표면 되돌려도 「시작 ≠ 목표」 를 못 세운다 → 진행 안 함(그 경우 DEVICE).
//   지금 자리 ≠ 중립이면 옮긴다(지금 = 목표인 경우 포함 · 앞 경우 도착 주소에 묶이지 않게).
function g4Reset(cur, home, want) {
  if (typeof home !== 'string' || home.length === 0) return { ok: false, nav: false, why: '중립 주소 모름' };
  if (typeof want !== 'string' || want.length === 0) return { ok: false, nav: false, why: '목표 주소 모름' };
  if (home === want) return { ok: false, nav: false, why: '중립 주소 = 목표 주소' };
  return { ok: true, nav: cur !== home, why: cur === want ? '시작 = 목표 → 되돌림' : (cur === home ? '이미 중립' : '중립 아님 → 되돌림') };
}
// 되돌린 뒤 확인 — 시작 주소 = 중립 그리고 ≠ 목표일 때만 단언으로 간다(아니면 DEVICE · [시험] 아님).
function g4StartOk(start, home, want) {
  return typeof start === 'string' && start === home && start !== want;
}
// 앱 안 이동(제품 팝업 쪽이 쓰는 같은 공개 함수 Blazor.navigateTo · 새로고침 0 · 알림 배관 그대로)
async function navigateInApp(page, url) {
  return withTimeout(page.evaluate((u) => {
    const b = window.Blazor;
    if (!b || typeof b.navigateTo !== 'function') return false;
    b.navigateTo(u);
    return true;
  }, url), EVAL_MS, '앱 안 이동');
}
// 본 창 밖 창(팝업) 전부 닫기 — 다음 경우가 깨끗한 바닥에서 시작하게.
async function closePopups(context, main) {
  let n = 0;
  for (const p of context.pages()) {
    if (p === main || p.isClosed()) continue;
    await p.close().catch((e) => console.log(`K 팝업 닫기 실패 ${L.errorName(e)}`));
    n += 1;
  }
  return n;
}
function popupsOf(context, main) {
  return context.pages().filter((p) => p !== main && !p.isClosed());
}
// 클릭 전에 걸어 둔다 → 상한 안에 팝업이 오면 그 창 · 안 오면 null(안 옴 = 단언 쪽에서 판정).
function expectPopup(page, ms) {
  return page.waitForEvent('popup', { timeout: ms }).then((p) => p, (e) => {
    if (e && e.name !== 'TimeoutError') console.log(`K 팝업 기다림 끝 (${L.errorName(e)})`);
    return null;
  });
}

// ── ② 그림(N4) ─────────────────────────────────────────────────────
// 창 하나 · 프레임 하나에서 모은다(브라우저 문맥). 비밀번호 칸 값은 꺼내지 않는다.
const COLLECT = () => {
  const out = { text: '', vals: [], ph: [], pw: 0, pwOpen: 0 };
  const b = document.body;
  out.text = b ? String(b.innerText || '') : '';
  for (const el of document.querySelectorAll('input, textarea, select')) {
    if (el.tagName === 'SELECT') {
      for (const o of el.selectedOptions || []) out.vals.push(String(o.text || ''));
      continue;
    }
    if (el.placeholder) out.ph.push(String(el.placeholder));
    const type = String(el.getAttribute('type') || '').toLowerCase();
    if (type === 'password') { if (el.value) out.pw += 1; continue; }
    const hint = [el.name, el.id, el.autocomplete, el.getAttribute('aria-label')].join(' ').toLowerCase();
    if (el.value && /password|비밀번호/.test(hint)) out.pwOpen += 1; // 비밀번호 칸으로 보이는데 가림형이 아님
    if (el.value) out.vals.push(String(el.value));
  }
  return out;
};
// 🔴 3차(3V2-04) — 창마다 번호(이 프로세스 안 · 글자 0)를 붙여 찍기 직전 · 찍을 때 · 찍은 직후 창 집합을 비교한다.
const pageIds = new WeakMap();
let pageSeq = 0;
function pageId(p) {
  let n = pageIds.get(p);
  if (n === undefined) {
    pageSeq += 1;
    n = pageSeq;
    pageIds.set(p, n);
  }
  return n;
}
function sameSet(a, b) {
  const x = [...new Set(a)].sort((m, n) => m - n).join(',');
  const y = [...new Set(b)].sort((m, n) => m - n).join(',');
  return x === y;
}
async function scanAll(context, registered) {
  // ⬛ [낡은 줄 · 2차] const res = { pages: 0, frames: 0, hits: 0, pwOpen: 0, errs: 0, errName: null };
  const res = { pages: 0, frames: 0, hits: 0, pwOpen: 0, errs: 0, errName: null, ids: [] };
  for (const p of context.pages()) {
    if (p.isClosed()) continue;
    res.pages += 1;
    res.ids.push(pageId(p));
    for (const f of p.frames()) {
      try {
        // ⬛ [낡은 줄 · 2차] const c = await f.evaluate(COLLECT);
        const c = await withTimeout(f.evaluate(COLLECT), EVAL_MS, '화면 글자 모으기');
        res.frames += 1;
        res.hits += domLeakHits([c.text, ...c.vals, ...c.ph].join('\n'), registered);
        res.pwOpen += c.pwOpen;
      } catch (e) {
        res.errs += 1;
        res.errName = L.errorName(e);
      }
    }
  }
  return res;
}
function shotsDir() {
  return path.join(L.outDir(), 'main', 'shots', 'k35');
}
// 🔴 3차(PM 결정 3V-2 · 3V2-01 ㉠) — 임시 자리 = 결과 폴더(UI_OUT)의 **옆**(업로드 경로 · 누출 검사 경로 밖 · 같은 파일계라 옮김은 이름 바꾸기).
function stageDir(browser = 'x') {
  const b = String(browser).replace(/[^a-z0-9]/gi, '_');
  return path.join(path.dirname(path.resolve(L.outDir())), 'ui-k35-stage', b);
}
// 임시 자리가 업로드 자리(결과 폴더 main) 밖인지 — 안이면 계약 오류(찍기 전에 던진다).
function stageOutside(stage = stageDir(), upload = path.join(path.resolve(L.outDir()), 'main')) {
  const rel = path.relative(path.resolve(upload), path.resolve(stage));
  return rel.startsWith('..') || path.isAbsolute(rel);
}
function rmQuiet(f, what) {
  try {
    fs.rmSync(f, { force: true });
    return !fs.existsSync(f);
  } catch (e) {
    console.log(`K ${what} 실패 (${L.errorName(e)})`);
    return false;
  }
}
// n4 = { verified, shots, blocked, removed, delFail, hits, errs, winDiff, moveFail, planted }
// ⬛ [낡은 꼴 · 2차 · 55c5131b] guardedShot — 그림을 처음부터 업로드 자리에 찍고(const dir = shotsDir(); const f = path.join(dir, name)),
// ⬛   찍은 직후 대조에 걸리면 지웠다 → 찍기와 직후 대조 사이에 죽으면 대조 안 된 그림이 업로드 자리에 남았다(3V2-01 · 4V-2 P1-b).
// 🔴 3차 — 순서: (봉인이면 안 찍음) → 찍기 직전 대조 → 임시 자리에 찍음 → 찍은 직후 대조 → 창 집합 셋 비교 →
//   통과 묶음만 업로드 자리로 이름 바꾸기(봉인 확인은 바꾸기 바로 앞 · 같은 동기 구간) → 걸림 · 예외 · 집합 다름 = 임시 자리에서 지움.
async function guardedShot(kb, label) {
  const { context, page } = kb;
  const n4 = kb.n4;
  if (kb.sealed) {
    console.log(`K 그림 안 찍음 ${label} — N4 줄 적은 뒤(봉인)`);
    return false;
  }
  const stage = stageDir(kb.browser);
  if (!stageOutside(stage)) throw new KitContractError('guardedShot: 임시 자리가 업로드 자리 안');
  const registered = registeredValues();
  const reg0 = L.collectSecrets().length; // 🔴 R1 ①(§4-1) — 시작 등록 수(위 줄과 같은 동기 구간 · await 0 · 수만)
  // 측정 가지 표지(plant.flags 에 n4) — 등록값 하나를 본 창 글자로 심는다(값은 화면에만 · 출력 0). 기대 = 안 찍힘 · N4 거짓.
  if (kb.plantN4 && !n4.planted && page && !page.isClosed()) {
    const v = process.env.UI_PW_GA || '';
    // ⬛ [낡은 줄 · 2차] await page.evaluate((x) => {
    await withTimeout(page.evaluate((x) => {
      const d = document.createElement('div');
      d.id = 'k35-n4-plant';
      d.textContent = x;
      d.style.cssText = 'position:fixed;bottom:4px;left:4px;z-index:2147483647;background:#fff;';
      document.body.appendChild(d);
    }, v), EVAL_MS, 'N4 심기').then(() => { n4.planted = true; }, (e) => console.log(`K N4 심기 실패 ${L.errorName(e)}`));
  }
  // 🔴 P2(C5) — 찍기 직전 대조 앞: 토큰 응답 본문 읽는 중이면 안 찍음 · 「대조 못 함」(R1 ③ 과 같은 셈 regGrow · 로그는 수만).
  if (pendingTokenReads > 0) {
    n4.blocked += 1;
    n4.regGrow = (n4.regGrow || 0) + 1;
    console.log(`K 그림 안 찍음 ${label} — 등록 대기 중 ${pendingTokenReads} (대조 못 함)`);
    return false;
  }
  const pre = await scanAll(context, registered);
  if (pre.hits > 0 || pre.pwOpen > 0 || pre.errs > 0 || pre.frames === 0) {
    n4.blocked += 1;
    if (pre.hits > 0 || pre.pwOpen > 0) n4.hits += 1;
    if (pre.errs > 0 || pre.frames === 0) n4.errs += 1;
    console.log(`K 그림 안 찍음 ${label} — 찍기 직전 대조 걸림 ${pre.hits} · 가림형 아닌 비밀번호 칸 ${pre.pwOpen} · 예외 ${pre.errs}${pre.errName ? `(${pre.errName})` : ''} · 프레임 ${pre.frames}`);
    return false;
  }
  fs.mkdirSync(stage, { recursive: true });
  const staged = []; // { src, name }
  const shotIds = [];
  let i = 0;
  for (const p of context.pages()) {
    if (p.isClosed()) continue;
    shotIds.push(pageId(p));
    const name = p === page ? `${label}-${kb.browser}.png` : `${label}-${kb.browser}-popup${++i}.png`;
    const src = path.join(stage, name);
    try {
      await withTimeout(p.screenshot({ path: src, timeout: SHOT_MS }), SHOT_MS + 2000, '그림 찍기');
      staged.push({ src, name });
    } catch (e) {
      console.log(`K 스크린샷 실패 ${label} (${L.errorName(e)}) — 그 장은 옮기지 않는다`);
      if (fs.existsSync(src) && !rmQuiet(src, '실패한 그림 지우기')) n4.delFail += 1;
    }
  }
  // ⬛ [낡은 줄 · R1/R2] const post = await scanAll(context, registered);
  // 🔴 R1 ②(§4-2) — 직후 대조 목록은 지금 다시 모은다(찍는 도중 등록된 값도 대조 · C2).
  const post = await scanAll(context, registeredValues());
  // 🔴 R1 ③(§4-3) · P2 — 직후 대조 await 바로 다음 줄. 이 판정 뒤 이름 바꾸기까지 await 0 → 판정 뒤 등록은 이번 그림에 끼어들 수 없다.
  const grew = L.collectSecrets().length > reg0;
  const waitN = pendingTokenReads;
  const winSame = sameSet(pre.ids, shotIds) && sameSet(shotIds, post.ids);
  // ⬛ [낡은 줄 · R1/R2] const bad = post.hits > 0 || post.pwOpen > 0 || post.errs > 0 || post.frames === 0 || !winSame;
  const bad = post.hits > 0 || post.pwOpen > 0 || post.errs > 0 || post.frames === 0 || !winSame || grew || waitN > 0;
  if (bad) {
    if (post.hits > 0 || post.pwOpen > 0) n4.hits += 1;
    if (post.errs > 0 || post.frames === 0) n4.errs += 1;
    if (!winSame) n4.winDiff += 1;
    if (grew || waitN > 0) n4.regGrow = (n4.regGrow || 0) + 1; // 🔴 R1 ④ · P2 — 초깃값 없는 자리 대비(k35-popup.js 무접촉)
    let fail = 0;
    for (const s of staged) if (!rmQuiet(s.src, '그림 지우기')) fail += 1;
    if (fail > 0) {
      n4.delFail += 1;
      try {
        fs.rmSync(stage, { recursive: true, force: true });
      } catch (e) {
        console.log(`K 임시 그림 폴더 통째 지우기 실패 (${L.errorName(e)})`);
      }
    }
    n4.removed += 1;
    // ⬛ [낡은 줄 · R1/R2] console.log(`K 그림 지움 ${label} — 찍은 직후 대조 걸림 ${post.hits} · 가림형 아닌 비밀번호 칸 ${post.pwOpen} · 예외 ${post.errs} · 창 집합 ${winSame ? '같음' : '다름'} · 지우기 실패 ${fail} (업로드 자리 옮김 0)`);
    console.log(`K 그림 지움 ${label} — 찍은 직후 대조 걸림 ${post.hits} · 가림형 아닌 비밀번호 칸 ${post.pwOpen} · 예외 ${post.errs} · 창 집합 ${winSame ? '같음' : '다름'} · 지우기 실패 ${fail} (업로드 자리 옮김 0) · 대조 중 등록 늘어남 ${grew ? '예' : '아니오'} · 등록 대기 중 ${waitN}`);
    return false;
  }
  // 옮김 — 봉인 확인과 이름 바꾸기 사이에 기다림(await) 0
  if (kb.sealed) {
    for (const s of staged) if (!rmQuiet(s.src, '봉인 뒤 그림 지우기')) n4.delFail += 1;
    console.log(`K 그림 안 옮김 ${label} — 대조 중 봉인`);
    return false;
  }
  const dir = shotsDir();
  fs.mkdirSync(dir, { recursive: true });
  let moved = 0;
  for (const s of staged) {
    try {
      fs.renameSync(s.src, path.join(dir, s.name));
      moved += 1;
    } catch (e) {
      n4.moveFail += 1;
      console.log(`K 그림 옮기기 실패 ${label} (${L.errorName(e)}) — 그 장은 업로드 자리에 없다`);
      if (fs.existsSync(s.src) && !rmQuiet(s.src, '못 옮긴 그림 지우기')) n4.delFail += 1;
    }
  }
  if (staged.length > 0 && moved === staged.length) n4.verified += 1;
  n4.shots += moved;
  return moved > 0 && moved === staged.length;
}
// 🔴 3차(3V-2 · 4V-2 P2-b) — N4 판정(순수). 이유 = 누출 신호(걸림 · 지우기 실패) 먼저 · 그 밖 거짓 = 대조 못 함(장치).
//   forced = 예외 길(프로세스 밖 예외 · 시간 상한 · 신호) — 셈과 무관하게 거짓.
function n4Verdict(n4, masked, forced = false) {
  const leak = n4.hits > 0 || n4.delFail > 0;
  // ⬛ [낡은 줄 · R1/R2] const noCheck = masked === 0 || n4.verified === 0 || n4.errs > 0 || (n4.winDiff || 0) > 0;
  // 🔴 R1 ⑥(§4-6) · P2 — 대조 중 등록 늘어남 · 등록 대기 중(regGrow) 도 「대조 못 함(장치)」. 칸 없는 자리 = 0.
  const noCheck = masked === 0 || n4.verified === 0 || n4.errs > 0 || (n4.winDiff || 0) > 0 || (n4.regGrow || 0) > 0;
  const pass = !forced && !leak && !noCheck;
  const why = pass ? null : (leak ? 'leak' : 'device');
  return { pass, why };
}
function n4Got(kb, v, forcedWhy) {
  const n4 = kb.n4;
  const head = v.pass ? 'N4 통과' : J.N4_WHY[v.why];
  // ⬛ [낡은 줄 · R1/R2] return `${head}${forcedWhy ? ` · 강제 거짓(${forcedWhy})` : ''} · 가린 K 토큰 ${kb.tok.masked} · 대조 통과 그림 묶음 ${n4.verified}(파일 ${n4.shots}) · 안 찍음 ${n4.blocked} · 찍고 지움 ${n4.removed} · 걸림 ${n4.hits} · 대조 예외 ${n4.errs} · 창 집합 다름 ${n4.winDiff || 0} · 옮기기 실패 ${n4.moveFail || 0} · 지우기 실패 ${n4.delFail}${n4.planted ? ' · 심은 값 있음(측정)' : ''} · 그림 픽셀 = 미측정`;
  // 🔴 R1 ⑥(§4-6) · P2 — 창 집합 다름 뒤에 한 칸(수만)
  return `${head}${forcedWhy ? ` · 강제 거짓(${forcedWhy})` : ''} · 가린 K 토큰 ${kb.tok.masked} · 대조 통과 그림 묶음 ${n4.verified}(파일 ${n4.shots}) · 안 찍음 ${n4.blocked} · 찍고 지움 ${n4.removed} · 걸림 ${n4.hits} · 대조 예외 ${n4.errs} · 창 집합 다름 ${n4.winDiff || 0} · 대조 중 등록 늘어남/등록 대기 중 ${n4.regGrow || 0} · 옮기기 실패 ${n4.moveFail || 0} · 지우기 실패 ${n4.delFail}${n4.planted ? ' · 심은 값 있음(측정)' : ''} · 그림 픽셀 = 미측정`;
}
// 표지 쓰기 — 실패해도 던지지 않는다(뒤 기록이 이어지게 · 업로드 자리엔 이미 대조 지난 그림만 있다).
function markN4Hit(where) {
  try {
    L.exportPlain('UI_N4_HIT', '1');
    return true;
  } catch (e) {
    console.log(`K N4 표지 쓰기 실패(${where}) ${L.errorName(e)}`);
    return false;
  }
}
// N4 줄(브라우저마다 한 줄 · DEVICE) — 거짓이면 다음 단계 업로드를 닫는 표지를 넘긴다.
// ⬛ [낡은 꼴 · 2차] recN4 — pass 를 셈으로 계산 → recK 먼저 → 표지(UI_N4_HIT) 나중. recK 가 던지면 표지 없이 넘어갔다(3V2-01 ④).
// 🔴 3차 — 봉인 먼저 → 판정 → (거짓이면) 표지 먼저 → 결과표 기록. forcedWhy 가 있으면 예외 길(강제 거짓).
function recN4(r, kb, forcedWhy = null) {
  kb.sealed = true;
  const v = n4Verdict(kb.n4, kb.tok.masked, !!forcedWhy);
  if (!v.pass) {
    markN4Hit(kb.browser);
    console.log(`K N4 거짓(${v.why === 'leak' ? '누출 신호' : '대조 못 함 · 장치'}) — 재실행 대상 아님 · 다음 업로드 단계 닫힘 표지 넘김`);
  }
  return recK(r, {
    key: { id: 'K35-N4', g: 'N4', path: null, browser: kb.browser },
    what: 'N4 그림 전 화면 글자 대조(모든 창 · 모든 프레임 · 입력·자리표시 글자 · 대소문자 무시 · 찍기 직전·직후 · 임시 자리 → 통과분만 옮김) — 거짓이면 누출 신호 또는 대조 못 함 · 재실행 대상 아님',
    got: n4Got(kb, v, forcedWhy), pass: v.pass, kind: 'DEVICE',
  });
}

// ⬛ [낡은 꼴 원문 · 2차 55c5131b] guardedShot · recN4 — 3차에서 위 새 꼴로 바뀜(3V2-01 · 3V2-02 · 4V-2 P1-b · P2-b). 지우지 않고 남긴다.
// ⬛ // n4 = { verified, shots, blocked, removed, delFail, hits, errs, planted }
// ⬛ async function guardedShot(kb, label) {
// ⬛   const { context, page } = kb;
// ⬛   const n4 = kb.n4;
// ⬛   const registered = registeredValues();
// ⬛   // 측정 가지 표지(plant.flags 에 n4) — 등록값 하나를 본 창 글자로 심는다(값은 화면에만 · 출력 0). 기대 = 안 찍힘 · N4 거짓.
// ⬛   if (kb.plantN4 && !n4.planted && page && !page.isClosed()) {
// ⬛     const v = process.env.UI_PW_GA || '';
// ⬛     await page.evaluate((x) => {
// ⬛       const d = document.createElement('div');
// ⬛       d.id = 'k35-n4-plant';
// ⬛       d.textContent = x;
// ⬛       d.style.cssText = 'position:fixed;bottom:4px;left:4px;z-index:2147483647;background:#fff;';
// ⬛       document.body.appendChild(d);
// ⬛     }, v).then(() => { n4.planted = true; }, (e) => console.log(`K N4 심기 실패 ${L.errorName(e)}`));
// ⬛   }
// ⬛   const pre = await scanAll(context, registered);
// ⬛   if (pre.hits > 0 || pre.pwOpen > 0 || pre.errs > 0 || pre.frames === 0) {
// ⬛     n4.blocked += 1;
// ⬛     if (pre.hits > 0 || pre.pwOpen > 0) n4.hits += 1;
// ⬛     if (pre.errs > 0 || pre.frames === 0) n4.errs += 1;
// ⬛     console.log(`K 그림 안 찍음 ${label} — 찍기 직전 대조 걸림 ${pre.hits} · 가림형 아닌 비밀번호 칸 ${pre.pwOpen} · 예외 ${pre.errs}${pre.errName ? `(${pre.errName})` : ''} · 프레임 ${pre.frames}`);
// ⬛     return false;
// ⬛   }
// ⬛   const dir = shotsDir();
// ⬛   fs.mkdirSync(dir, { recursive: true });
// ⬛   const files = [];
// ⬛   let i = 0;
// ⬛   for (const p of context.pages()) {
// ⬛     if (p.isClosed()) continue;
// ⬛     const name = p === page ? `${label}-${kb.browser}.png` : `${label}-${kb.browser}-popup${++i}.png`;
// ⬛     const f = path.join(dir, name);
// ⬛     try {
// ⬛       await p.screenshot({ path: f });
// ⬛       files.push(f);
// ⬛     } catch (e) {
// ⬛       console.log(`K 스크린샷 실패 ${label} (${L.errorName(e)})`);
// ⬛       if (fs.existsSync(f)) files.push(f);
// ⬛     }
// ⬛   }
// ⬛   const post = await scanAll(context, registered);
// ⬛   if (post.hits > 0 || post.pwOpen > 0 || post.errs > 0 || post.frames === 0) {
// ⬛     if (post.hits > 0 || post.pwOpen > 0) n4.hits += 1;
// ⬛     if (post.errs > 0 || post.frames === 0) n4.errs += 1;
// ⬛     let fail = 0;
// ⬛     for (const f of files) {
// ⬛       try {
// ⬛         fs.rmSync(f, { force: true });
// ⬛         if (fs.existsSync(f)) fail += 1;
// ⬛       } catch (e) {
// ⬛         fail += 1;
// ⬛         console.log(`K 그림 지우기 실패 (${L.errorName(e)})`);
// ⬛       }
// ⬛     }
// ⬛     if (fail > 0) {
// ⬛       n4.delFail += 1;
// ⬛       try {
// ⬛         fs.rmSync(dir, { recursive: true, force: true });
// ⬛       } catch (e) {
// ⬛         console.log(`K 그림 폴더 통째 지우기 실패 (${L.errorName(e)})`);
// ⬛       }
// ⬛     }
// ⬛     n4.removed += 1;
// ⬛     console.log(`K 그림 지움 ${label} — 찍은 직후 대조 걸림 ${post.hits} · 가림형 아닌 비밀번호 칸 ${post.pwOpen} · 예외 ${post.errs} · 지우기 실패 ${fail}`);
// ⬛     return false;
// ⬛   }
// ⬛   n4.verified += 1;
// ⬛   n4.shots += files.length;
// ⬛   return true;
// ⬛ }
// ⬛ // N4 줄(브라우저마다 한 줄 · DEVICE) — 거짓이면 다음 단계 업로드를 닫는 표지를 넘긴다.
// ⬛ function recN4(r, kb) {
// ⬛   const n4 = kb.n4;
// ⬛   const pass = kb.tok.masked > 0 && n4.verified > 0 && n4.hits === 0 && n4.errs === 0 && n4.delFail === 0;
// ⬛   const got = `가린 K 토큰 ${kb.tok.masked} · 대조 통과 그림 묶음 ${n4.verified}(파일 ${n4.shots}) · 안 찍음 ${n4.blocked} · 찍고 지움 ${n4.removed} · 걸림 ${n4.hits} · 대조 예외 ${n4.errs} · 지우기 실패 ${n4.delFail}${n4.planted ? ' · 심은 값 있음(측정)' : ''} · 그림 픽셀 = 미측정`;
// ⬛   const row = recK(r, {
// ⬛     key: { id: 'K35-N4', g: 'N4', path: null, browser: kb.browser },
// ⬛     what: 'N4 그림 전 화면 글자 대조(모든 창 · 모든 프레임 · 입력·자리표시 글자 · 대소문자 무시 · 찍기 직전·직후) — 거짓이면 누출 신호 · 재실행 대상 아님',
// ⬛     got, pass, kind: 'DEVICE',
// ⬛   });
// ⬛   if (!pass) {
// ⬛     L.exportPlain('UI_N4_HIT', '1');
// ⬛     console.log('K N4 거짓 — 누출 신호(재실행 대상 아님) · 다음 업로드 단계 닫힘 표지 넘김');
// ⬛   }
// ⬛   return row;
// ⬛ }

module.exports = {
  BASE, POPUP_NAME, BLOCKER, INIT_SCRIPT, initScript,
  KitContractError, KitStepError,
  kKey, recK, isWritten, fillDevice, expectKeys,
  sameWindowName, g3SelfCheck,
  domLeakHits, registeredValues, ctl, consoleLine, flushConsole,
  EVAL_MS, SHOT_MS, withTimeout,
  apiCall, apiLogin, apiLogout,
  sleep, pathOf, blockExternal, watchTokens, waitMasked, watchConsole, screenLogin, watchHub, waitHub,
  briefOff, waitNoToasts, waitToast, clickToast, clickBellItem, clickMessenger, toastTexts,
  bellWhy, bellCountsText, bellStepMessage,
  recorder, closePopups, popupsOf, expectPopup,
  g4Reset, g4StartOk, navigateInApp,
  COLLECT, pageId, sameSet, scanAll, shotsDir, stageDir, stageOutside, guardedShot, n4Verdict, markN4Hit, recN4,
};
