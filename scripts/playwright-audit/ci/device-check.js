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
//   trace: 실패한 브라우저만 `trace/` 에 남긴다(§3-2 S11 · 포크 PR 제외는 워크플로가 한다).
'use strict';

const fs = require('fs');
const path = require('path');
const L = require('./lib');

const BASE = process.env.UI_BASE || 'http://127.0.0.1:18080';
const ALLOWED_HOSTS = new Set(['127.0.0.1', 'localhost']);

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

async function watchTokens(context) {
  context.on('response', async (res) => {
    const u = res.url();
    if (!/\/api\/auth\/(login|refresh)(\?|$)/i.test(u)) return;
    try {
      const j = await res.json();
      const access = j && (j.accessToken || j.AccessToken);
      const refresh = j && (j.refreshToken || j.RefreshToken);
      if (access) L.maskAndRegister('TOKEN_ACCESS_BROWSER', access);
      if (refresh) L.maskAndRegister('TOKEN_REFRESH_BROWSER', refresh);
    } catch (e) {
      console.log(`토큰 가로채기: 본문 읽기 실패 (원문 생략 · ${e.name})`);
    }
  });
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

async function screenLogin(page, loginId, password) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  await page.getByLabel('아이디').fill(loginId, { timeout: 60000 });
  await page.getByLabel('비밀번호').fill(password);
  await page.getByRole('button', { name: '로그인' }).click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 });
  if (new URL(page.url()).pathname.startsWith('/terms')) {
    for (const label of ['서비스 이용 약관', '개인정보 처리 방침', '유료 구독·환불 약관', '데이터 주권·책임 분산']) {
      await page.getByText(label, { exact: false }).first().click();
    }
    await page.getByRole('button', { name: '동의하고 계속' }).click();
    await page.waitForURL((u) => !u.pathname.startsWith('/terms'), { timeout: 60000 });
    return 'terms-agreed';
  }
  return 'no-terms';
}

async function runBrowser(pw, browserName, r, flags, full) {
  const shots = path.join(L.outDir(), 'main', 'shots');
  const traces = path.join(L.outDir(), 'trace');
  fs.mkdirSync(shots, { recursive: true });
  let browser;
  let context;
  let failed = false;
  const tag = { browser: browserName, scope: 'H' };
  try {
    browser = await pw[browserName].launch();
    context = await browser.newContext({ baseURL: BASE });
    L.rec(r, { ...tag, id: 'H-D1', what: '브라우저 기동', got: browserName, pass: true, kind: 'DEVICE' });
  } catch (e) {
    L.rec(r, { ...tag, id: 'H-D1', what: '브라우저 기동', got: `실패 ${e.name}`, pass: false, kind: 'DEVICE' });
    return false;
  }
  await context.tracing.start({ screenshots: true, snapshots: true });
  try {
    await blockExternal(context, r);
    await watchTokens(context);
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
    try {
      if (failed) {
        fs.mkdirSync(traces, { recursive: true });
        await context.tracing.stop({ path: path.join(traces, `H-device-${browserName}.zip`) });
      } else {
        await context.tracing.stop();
      }
    } catch (e) {
      console.log(`trace 마무리 실패 (${e.name})`);
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
  const okC = await runBrowser(pw, 'chromium', r, flags, true);
  const okW = await runBrowser(pw, 'webkit', r, flags, false);
  L.saveResult(r);
  process.exit(okC && okW ? 0 : 1);
}

main();
