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

// ③ 로그인·갱신 응답 토큰 — 받는 즉시 가림·등록.
function watchTokens(context, tok) {
  context.on('response', async (res) => {
    if (!/\/api\/auth\/(login|refresh)(\?|$)/i.test(res.url())) return;
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
    }
  });
}
async function waitMasked(tok, ms = 10000) {
  const end = Date.now() + ms;
  while (tok.access === null && Date.now() < end) await sleep(250);
  return tok.masked;
}

// ⑤ 브라우저 콘솔 오류 줄(창마다 · 상한 20줄)
function watchConsole(context, kb) {
  const attach = (p) => {
    p.on('console', (msg) => {
      if (msg.type() !== 'error' || kb.consoleLines >= 20) return;
      kb.consoleLines += 1;
      console.log(`K 브라우저 콘솔(오류 · ${kb.browser}) ${consoleLine(msg.text())}`);
    });
    p.on('pageerror', (e) => {
      if (kb.consoleLines >= 20) return;
      kb.consoleLines += 1;
      console.log(`K 브라우저 화면 예외(${kb.browser}) ${L.errorName(e)}`);
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
async function clickBellItem(page, text) {
  await page.locator(BELL_BUTTON).first().click({ timeout: 15000 });
  const item = page.locator('.mud-menu-item', { hasText: text }).first();
  await item.waitFor({ state: 'visible', timeout: 15000 });
  await item.click({ timeout: 15000 });
}
async function toastTexts(page) {
  try {
    return await page.evaluate(() => [...document.querySelectorAll('.mud-snackbar')]
      .map((e) => String(e.innerText || '').replace(/\s+/g, ' ').trim().slice(0, 80)).slice(0, 4));
  } catch (e) {
    return [`읽기 실패 ${L.errorName(e)}`];
  }
}

// 녹음기(장치 RECORDER) 읽기 · 비우기 · 막기 표지
async function recorder(page, op) {
  return page.evaluate((o) => {
    if (!window.__k35) return { err: window.__k35err || '녹음기 없음' };
    if (o === 'clear') { window.__k35.clear(); return { ok: true }; }
    if (o === 'block-on') { window.sessionStorage.setItem('__k35block', '1'); return { ok: true }; }
    if (o === 'block-off') { window.sessionStorage.removeItem('__k35block'); return { ok: true }; }
    return { list: window.__k35.list() };
  }, op);
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
async function scanAll(context, registered) {
  const res = { pages: 0, frames: 0, hits: 0, pwOpen: 0, errs: 0, errName: null };
  for (const p of context.pages()) {
    if (p.isClosed()) continue;
    res.pages += 1;
    for (const f of p.frames()) {
      try {
        const c = await f.evaluate(COLLECT);
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
// n4 = { verified, shots, blocked, removed, delFail, hits, errs, planted }
async function guardedShot(kb, label) {
  const { context, page } = kb;
  const n4 = kb.n4;
  const registered = registeredValues();
  // 측정 가지 표지(plant.flags 에 n4) — 등록값 하나를 본 창 글자로 심는다(값은 화면에만 · 출력 0). 기대 = 안 찍힘 · N4 거짓.
  if (kb.plantN4 && !n4.planted && page && !page.isClosed()) {
    const v = process.env.UI_PW_GA || '';
    await page.evaluate((x) => {
      const d = document.createElement('div');
      d.id = 'k35-n4-plant';
      d.textContent = x;
      d.style.cssText = 'position:fixed;bottom:4px;left:4px;z-index:2147483647;background:#fff;';
      document.body.appendChild(d);
    }, v).then(() => { n4.planted = true; }, (e) => console.log(`K N4 심기 실패 ${L.errorName(e)}`));
  }
  const pre = await scanAll(context, registered);
  if (pre.hits > 0 || pre.pwOpen > 0 || pre.errs > 0 || pre.frames === 0) {
    n4.blocked += 1;
    if (pre.hits > 0 || pre.pwOpen > 0) n4.hits += 1;
    if (pre.errs > 0 || pre.frames === 0) n4.errs += 1;
    console.log(`K 그림 안 찍음 ${label} — 찍기 직전 대조 걸림 ${pre.hits} · 가림형 아닌 비밀번호 칸 ${pre.pwOpen} · 예외 ${pre.errs}${pre.errName ? `(${pre.errName})` : ''} · 프레임 ${pre.frames}`);
    return false;
  }
  const dir = shotsDir();
  fs.mkdirSync(dir, { recursive: true });
  const files = [];
  let i = 0;
  for (const p of context.pages()) {
    if (p.isClosed()) continue;
    const name = p === page ? `${label}-${kb.browser}.png` : `${label}-${kb.browser}-popup${++i}.png`;
    const f = path.join(dir, name);
    try {
      await p.screenshot({ path: f });
      files.push(f);
    } catch (e) {
      console.log(`K 스크린샷 실패 ${label} (${L.errorName(e)})`);
      if (fs.existsSync(f)) files.push(f);
    }
  }
  const post = await scanAll(context, registered);
  if (post.hits > 0 || post.pwOpen > 0 || post.errs > 0 || post.frames === 0) {
    if (post.hits > 0 || post.pwOpen > 0) n4.hits += 1;
    if (post.errs > 0 || post.frames === 0) n4.errs += 1;
    let fail = 0;
    for (const f of files) {
      try {
        fs.rmSync(f, { force: true });
        if (fs.existsSync(f)) fail += 1;
      } catch (e) {
        fail += 1;
        console.log(`K 그림 지우기 실패 (${L.errorName(e)})`);
      }
    }
    if (fail > 0) {
      n4.delFail += 1;
      try {
        fs.rmSync(dir, { recursive: true, force: true });
      } catch (e) {
        console.log(`K 그림 폴더 통째 지우기 실패 (${L.errorName(e)})`);
      }
    }
    n4.removed += 1;
    console.log(`K 그림 지움 ${label} — 찍은 직후 대조 걸림 ${post.hits} · 가림형 아닌 비밀번호 칸 ${post.pwOpen} · 예외 ${post.errs} · 지우기 실패 ${fail}`);
    return false;
  }
  n4.verified += 1;
  n4.shots += files.length;
  return true;
}
// N4 줄(브라우저마다 한 줄 · DEVICE) — 거짓이면 다음 단계 업로드를 닫는 표지를 넘긴다.
function recN4(r, kb) {
  const n4 = kb.n4;
  const pass = kb.tok.masked > 0 && n4.verified > 0 && n4.hits === 0 && n4.errs === 0 && n4.delFail === 0;
  const got = `가린 K 토큰 ${kb.tok.masked} · 대조 통과 그림 묶음 ${n4.verified}(파일 ${n4.shots}) · 안 찍음 ${n4.blocked} · 찍고 지움 ${n4.removed} · 걸림 ${n4.hits} · 대조 예외 ${n4.errs} · 지우기 실패 ${n4.delFail}${n4.planted ? ' · 심은 값 있음(측정)' : ''} · 그림 픽셀 = 미측정`;
  const row = recK(r, {
    key: { id: 'K35-N4', g: 'N4', path: null, browser: kb.browser },
    what: 'N4 그림 전 화면 글자 대조(모든 창 · 모든 프레임 · 입력·자리표시 글자 · 대소문자 무시 · 찍기 직전·직후) — 거짓이면 누출 신호 · 재실행 대상 아님',
    got, pass, kind: 'DEVICE',
  });
  if (!pass) {
    L.exportPlain('UI_N4_HIT', '1');
    console.log('K N4 거짓 — 누출 신호(재실행 대상 아님) · 다음 업로드 단계 닫힘 표지 넘김');
  }
  return row;
}

module.exports = {
  BASE, POPUP_NAME, BLOCKER,
  KitContractError, KitStepError,
  kKey, recK, isWritten, fillDevice, expectKeys,
  sameWindowName, g3SelfCheck,
  domLeakHits, registeredValues, ctl, consoleLine,
  apiCall, apiLogin, apiLogout,
  sleep, pathOf, blockExternal, watchTokens, waitMasked, watchConsole, screenLogin, watchHub, waitHub,
  briefOff, waitNoToasts, waitToast, clickToast, clickBellItem, clickMessenger, toastTexts,
  recorder, closePopups, popupsOf, expectPopup,
  COLLECT, scanAll, shotsDir, guardedShot, recN4,
};
