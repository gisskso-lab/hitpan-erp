// G1-3 갈래 H — S8 자리의 장치 점검 + 대조 단언 (작업지시서 §4-3 ⓒ · §6-1 H)
//
// 🔴 K35 시험 본문(G1~G5)은 여기 쓰지 않는다 — 그건 갈래 K(`k35-*.js` · PR #691).
// 하는 일
//   [장치] 브라우저 기동(Chromium·WebKit) · 화면 로그인(나) · 약관 화면 통과 — 단언 전 실패는 DEVICE.
//   [시험] 대조 단언 2개(scope H) — 「항상 초록」이 아님을 보이는 자리:
//     H-T1 녹음기: 화면 안에서 window.open('/chat-window/x','hitpanChat') 직접 1회 → 기록 1 · 이름 hitpanChat
//     H-T2 본 창 이동(결재 길 흉내 · /approval/pending 링크 클릭) → window.open 0회 (참 기대)
//     심은 빨강(plant.flags 에 `red`): H-T2 대신 H-PLANT 「결재 알림 클릭 시 window.open 1회」(거짓 기대) → 빨강.
//   외부 요청: 127.0.0.1 밖은 끊고 **호스트만** 센다(§3-4).
//   접속·갱신 토큰: 로그인·갱신 응답을 가로채 받는 즉시 가린다(§0-9 ⑫ · 가림이 첫 출력 · 본문 출력 0).
//   ⬛ [낡은 줄 · 1차] trace: 실패한 브라우저만 `trace/` 에 남긴다(§3-2 S11 · 포크 PR 제외는 워크플로가 한다).
//   🔴 2차(10/29 · 작업지시서 §0-9 항목 5 · 보안상무 안 ⓐ): trace 는 **기록도 업로드도 안 한다**.
//     로그인 칸 입력(비번 원문)·인증 헤더가 trace 에 남아 실패 런마다 S10 이 빨강 → 결과표·스크린샷까지 0 이 되기 때문.
//     실패 증거 = 결과표 · 스크린샷 · API 로그 · 단언 메시지(got).
//   🔴 2차 ②: 한 PC 로그인 규칙(AuthService.cs:418~464 · 살아 있는 pc 세션 있으면 409 other_pc_in_use)에
//     안 걸리게 — 브라우저마다 끝날 때 **그 로그인을 로그아웃**한다(화면과 같은 `POST api/auth/logout` ·
//     Web AuthService.cs:243). 그래서 WebKit 은 Chromium 이 로그아웃한 뒤 같은 「나」로 들어간다.
//   🔴 2차 ⑤: 화면 로그인은 통과했는데 가린 브라우저 토큰이 0건이면 [장치] 빨강(H3V-2 — 빈 검색 = 거짓 통과).
'use strict';

const fs = require('fs');
const path = require('path');
const L = require('./lib');

const BASE = process.env.UI_BASE || 'http://127.0.0.1:18080';
const ALLOWED_HOSTS = new Set(['127.0.0.1', 'localhost']);
let logoutFailures = 0; // 2차 ② — runBrowser finally 의 로그아웃 실패 수

function plantFlags() {
  const p = path.join(__dirname, 'plant.flags');
  if (!fs.existsSync(p)) return new Set();
  return new Set(fs.readFileSync(p, 'utf8').split(/\s+/).filter(Boolean));
}

// window.open 녹음기 — (이름·주소)를 기록하고 원래 함수로 넘긴다(§4-1 기록 방법).
const RECORDER = `(() => {
  const orig = window.open;
  window.__k35opens = [];
  window.open = function (url, name, features) {
    window.__k35opens.push({ url: String(url ?? ''), name: String(name ?? '') });
    return orig.call(window, url, name, features);
  };
})();`;

// tok = { access, deviceId, masked } — 2차: 가린 개수(⑤)와 로그아웃(②)에 쓴다. 값은 메모리에만.
async function watchTokens(context, tok) {
  context.on('response', async (res) => {
    const u = res.url();
    if (!/\/api\/auth\/(login|refresh)(\?|$)/i.test(u)) return;
    try {
      const j = await res.json();
      const access = j && (j.accessToken || j.AccessToken);
      const refresh = j && (j.refreshToken || j.RefreshToken);
      if (access) { L.maskAndRegister('TOKEN_ACCESS_BROWSER', access); tok.access = access; tok.masked += 1; }
      if (refresh) { L.maskAndRegister('TOKEN_REFRESH_BROWSER', refresh); tok.masked += 1; }
      const dev = j && (j.deviceId || j.DeviceId);
      if (dev) tok.deviceId = String(dev);
    } catch (e) {
      console.log(`토큰 가로채기: 본문 읽기 실패 (원문 생략 · ${e.name})`);
    }
  });
}

// 2차 ⑤ — 응답 처리기는 비동기라 이동 뒤 조금 늦게 끝날 수 있다. 최대 10초 기다린다.
async function waitMasked(tok) {
  for (let i = 0; i < 20 && tok.access === null; i += 1) {
    await new Promise((ok) => setTimeout(ok, 500));
  }
  return tok.masked;
}

// 2차 ② — 화면과 같은 로그아웃 길(Web AuthService.cs:243 `api/auth/logout` · AuthController.cs:353 [Authorize]).
//   서버는 JWT `sid` 의 refresh 폐기 + 세션 행 삭제(AuthController.cs:409~426) → 다음 로그인이 409 에 안 걸린다.
async function logoutViaApi(tok) {
  if (!tok.access) return { ok: false, got: '로그아웃할 토큰 없음' };
  const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${tok.access}` };
  if (tok.deviceId) headers['X-HitPan-Device-Id'] = tok.deviceId;
  try {
    const res = await fetch(`${BASE}/api/auth/logout`, { method: 'POST', headers, body: '{}' });
    return { ok: res.status === 200, got: `status=${res.status}` };
  } catch (e) {
    return { ok: false, got: `요청 실패 ${e.name}` };
  }
}

async function blockExternal(context, r) {
  await context.route('**/*', (route) => {
    let host = '';
    try {
      host = new URL(route.request().url()).hostname;
    } catch (e) {
      console.log(`외부 차단: 주소 해석 실패 (${e.name})`);
    }
    if (ALLOWED_HOSTS.has(host) || route.request().url().startsWith('data:') || route.request().url().startsWith('blob:')) {
      return route.continue();
    }
    r.external_blocked += 1;
    if (host && !r.external_hosts.includes(host)) r.external_hosts.push(host);
    return route.abort('blockedbyclient');
  });
}

// 2차 ⑦ — 화면 소스의 실제 글자 **전문**으로 좁힌다(부분 일치 · .first() 제거).
//   약관 체크상자 라벨 = Terms.razor:31·35·39·43 의 Label 그대로(필수 4개 · 선택 마케팅 :47 은 안 누른다).
const TERMS_REQUIRED_LABELS = [
  '(필수) 서비스 이용 약관에 동의합니다.',
  '(필수) 개인정보 처리 방침에 동의합니다.',
  '(필수) 유료 구독·환불 약관에 동의합니다.',
  '(필수) 데이터 주권·책임 분산에 동의합니다. (헌법 #22·#24)',
];

async function screenLogin(page, loginId, password) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  // ⬛ [낡은 줄 · 1차] getByLabel('아이디') · getByLabel('비밀번호') · getByRole('button', { name: '로그인' }) — 부분 일치.
  // 🔴 2차 ⑦: Login.razor:35·45 Label · :102 단추 글자 — 전문 일치(exact). 「최신버젼업데이트」 단추(:121~150)와 안 겹친다.
  await page.getByLabel('아이디', { exact: true }).fill(loginId, { timeout: 60000 });
  await page.getByLabel('비밀번호', { exact: true }).fill(password);
  // MudTextField 는 기본이 change(칸을 떠날 때) 반영 — 단추 Disabled(:95) 가 풀리게 칸을 떠난다(⚠️ 미측정 · 첫 런에서 확인).
  await page.getByLabel('비밀번호', { exact: true }).press('Tab');
  await page.getByRole('button', { name: '로그인', exact: true }).click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 });
  if (new URL(page.url()).pathname.startsWith('/terms')) {
    // ⬛ [낡은 줄 · 1차] for 짧은 이름 4개 → page.getByText(label, { exact: false }).first().click()
    for (const label of TERMS_REQUIRED_LABELS) {
      await page.getByRole('checkbox', { name: label, exact: true }).check();
    }
    // Terms.razor:56 — 바쁠 때 글자가 「처리 중…」으로 바뀌므로 누르기 전의 전문.
    await page.getByRole('button', { name: '동의하고 계속', exact: true }).click();
    await page.waitForURL((u) => !u.pathname.startsWith('/terms'), { timeout: 60000 });
    return 'terms-agreed';
  }
  return 'no-terms';
}

async function runBrowser(pw, browserName, r, flags, full) {
  const shots = path.join(L.outDir(), 'main', 'shots');
  // ⬛ [낡은 줄 · 1차] const traces = path.join(L.outDir(), 'trace'); — 2차 ① trace 제거
  fs.mkdirSync(shots, { recursive: true });
  let browser;
  let context;
  let failed = false;
  const tag = { browser: browserName, scope: 'H' };
  const tok = { access: null, deviceId: null, masked: 0 };
  try {
    browser = await pw[browserName].launch();
    context = await browser.newContext({ baseURL: BASE });
    L.rec(r, { ...tag, id: 'H-D1', what: '브라우저 기동', got: browserName, pass: true, kind: 'DEVICE' });
  } catch (e) {
    L.rec(r, { ...tag, id: 'H-D1', what: '브라우저 기동', got: `실패 ${e.name}`, pass: false, kind: 'DEVICE' });
    return false;
  }
  // ⬛ [낡은 줄 · 1차] await context.tracing.start({ screenshots: true, snapshots: true }); — 2차 ① 기록 안 함
  try {
    await blockExternal(context, r);
    await watchTokens(context, tok);
    await context.addInitScript(RECORDER);
    const page = await context.newPage();

    let how;
    try {
      how = await screenLogin(page, process.env.UI_LOGIN_NA || 'ci-na', process.env.UI_PW_NA || '');
      L.rec(r, { ...tag, id: 'H-D2', what: '화면 로그인(나) · 약관 화면 그대로', got: `${how} → ${new URL(page.url()).pathname}`, pass: true, kind: 'DEVICE' });
    } catch (e) {
      failed = true;
      L.rec(r, { ...tag, id: 'H-D2', what: '화면 로그인(나) · 약관 화면 그대로', got: `실패 ${e.name} @ ${new URL(page.url()).pathname}`, pass: false, kind: 'DEVICE' });
      await page.screenshot({ path: path.join(shots, `H-device-${browserName}.png`) }).catch((se) => console.log(`스크린샷 실패 ${se.name}`));
      return false;
    }

    // 2차 ⑤ (H3V-2) — 화면 로그인 통과 + 가린 브라우저 토큰 0건 = 런 중 값이 S10 검색 대상에 안 들어갔다 → [장치] 빨강.
    const masked = await waitMasked(tok);
    const maskedOk = masked > 0 && tok.access !== null;
    L.rec(r, { ...tag, id: 'H-D3', what: '화면 로그인 뒤 브라우저 접속 토큰을 가리고 S10 검색 대상에 넣음', got: `가린 토큰 ${masked}건`, pass: maskedOk, kind: 'DEVICE' });
    if (!maskedOk) {
      failed = true;
      await page.screenshot({ path: path.join(shots, `H-device-${browserName}.png`) }).catch((se) => console.log(`스크린샷 실패 ${se.name}`));
      return false;
    }

    // H-T1 녹음기 대조 — 직접 1회 부르면 1건 · 이름 hitpanChat 이어야 한다.
    const t1 = await page.evaluate(() => {
      window.__k35opens.length = 0;
      const w = window.open('/chat-window/ci-probe', 'hitpanChat');
      if (w) w.close();
      return window.__k35opens.slice();
    });
    const t1pass = t1.length === 1 && t1[0].name === 'hitpanChat';
    if (!t1pass) failed = true;
    L.rec(r, { ...tag, id: 'H-T1', what: '녹음기: window.open 직접 1회 → 기록 1 · 이름 hitpanChat', got: t1, pass: t1pass, kind: 'TEST' });

    if (full) {
      // 결재 길 흉내 — 본 창 링크 클릭(Blazor 길잡이가 본 창에서 이동시킨다).
      const before = context.pages().length;
      await page.evaluate(() => {
        window.__k35opens.length = 0;
        const a = document.createElement('a');
        a.href = '/approval/pending';
        a.id = 'ci-approval-probe';
        a.textContent = 'ci';
        document.body.appendChild(a);
      });
      await page.click('#ci-approval-probe');
      await page.waitForURL((u) => u.pathname.startsWith('/approval/pending'), { timeout: 30000 }).catch((we) => console.log(`결재 길 이동 대기 끝 (${we.name})`));
      const opens = await page.evaluate(() => window.__k35opens.length);
      const got = { opens, path: new URL(page.url()).pathname, pages: context.pages().length - before };
      if (flags.has('red')) {
        // 🔴 심은 빨강(§4-3 ⓒ) — 거짓 기대. 이 줄이 초록이면 장치가 「항상 초록」이다.
        const pass = opens === 1;
        if (!pass) failed = true;
        L.rec(r, { ...tag, id: 'H-PLANT', what: '[심은 빨강] 결재 알림 클릭 시 window.open 1회 (거짓 기대)', got, pass, kind: 'TEST' });
      } else {
        const pass = opens === 0 && got.path.startsWith('/approval/pending');
        if (!pass) failed = true;
        L.rec(r, { ...tag, id: 'H-T2', what: '본 창 이동(결재 길 흉내) → window.open 0회 · 본 창 /approval/pending', got, pass, kind: 'TEST' });
      }
    }
    await page.screenshot({ path: path.join(shots, `H-device-${browserName}.png`) }).catch((se) => console.log(`스크린샷 실패 ${se.name}`));
    return !failed;
  } finally {
    // ⬛ [낡은 갈래 · 1차] 실패면 context.tracing.stop({ path: trace/H-device-<브라우저>.zip }) · 아니면 stop() — 2차 ① 제거.
    // 🔴 2차 ② — 이 브라우저의 로그인을 로그아웃한다(성공·실패 무관 · 토큰을 받았을 때만).
    //   다음 브라우저(WebKit)·K 본문이 같은 「나」로 들어갈 때 한 PC 로그인 409 에 안 걸리게.
    if (tok.access) {
      const lo = await logoutViaApi(tok);
      L.rec(r, { ...tag, id: 'H-D4', what: '로그아웃(다음 로그인이 한 PC 규칙 409 에 안 걸리게)', got: lo.got, pass: lo.ok, kind: 'DEVICE' });
      // finally 안이라 이미 정해진 return 값을 못 바꾼다 → 모듈 칸으로 main 에 알린다.
      if (!lo.ok) logoutFailures += 1;
    }
    await browser.close().catch((ce) => console.log(`브라우저 닫기 실패 ${ce.name}`));
  }
}

async function main() {
  const r = L.loadResult();
  const flags = plantFlags();
  if (flags.size > 0) {
    console.log(`::warning::측정 커밋 — plant.flags = [${[...flags].join(',')}] · 머지 금지`);
    r.plant = [...flags];
  }
  let pw;
  try {
    pw = require('playwright');
  } catch (e) {
    L.rec(r, { id: 'H-D0', what: 'playwright 불러오기', got: `실패 ${e.code || e.name}`, pass: false, kind: 'DEVICE' });
    L.saveResult(r);
    process.exit(1);
  }
  // Chromium 전부 · WebKit 1회(§4-1) — WebKit 은 기동·로그인·녹음기만.
  // 2차 ②: Chromium 의 finally 가 로그아웃을 끝낸 **뒤**에 WebKit 이 같은 「나」로 들어간다(순차 await).
  const okC = await runBrowser(pw, 'chromium', r, flags, true);
  const okW = await runBrowser(pw, 'webkit', r, flags, false);
  L.saveResult(r);
  process.exit(okC && okW && logoutFailures === 0 ? 0 : 1);
}

// 2차 ⑧ — 로컬 확인(plant.flags 해석)용으로 함수만 꺼내 쓸 수 있게 한다. CI 는 `node device-check.js` 직접 실행.
if (require.main === module) main();
module.exports = { plantFlags, TERMS_REQUIRED_LABELS };
