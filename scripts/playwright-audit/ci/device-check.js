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
//   🔴 3차 Q-1(리뷰서 §재확인 R-⑤): 로그인 뒤 **화면이 자리 잡을 때까지** 기다린다(약관 화면 표지 또는 본 화면 표지가
//     같은 주소에서 2초 이상 그대로). 1~2차는 주소가 /login 을 벗어난 **즉시** 봐서 /terms 강제 이동 전에 no-terms 를 찍었다.
//     「나」는 시드에서 약관 미동의(seed-children.js 는 부모·가만 동의) → 이 런 첫 브라우저는 약관 문이 **서야** 한다.
//     약관 문 대조(동작): 동의 전 인증 API 403 forbidden_terms_consent · 동의 뒤 같은 API 200 — 브라우저 문맥 fetch.
//     약관 문이 안 서거나 동의 전에 200 이면 [장치] 빨강(약관 미들웨어가 CI 에서 안 도는 것 = 보고 대상).
//   🟡 4차(10/29 · 대표 결재 「약관 결함은 별건 · 장치는 분리」): 자리=main + 로그인 응답 requiresTermsConsent=true 면
//     H-OBS-TERMS(kind OBS · 빨강 아님)에 관찰(문서 이동 순서·약관 상태 응답·인증 API 상태·알림 글자)을 남기고
//     화면과 같은 동의 API → 로그아웃 → 다시 화면 로그인 → 본 화면 → H-D2c 200 으로 닫는다. 끝 상태 = H-NA-END 한 줄.
//   🔴 5차(10/29 · 런6 40750810674 — H-T2 클릭이 일일 브리핑 덮개 `div.hp-brief-backdrop` 에 막혀 TimeoutError →
//     프로세스가 죽어 결과표 0 줄 · 콘솔엔 PASS 7 줄 · judge 「돈 시험 0건」):
//     ① 결과표는 줄마다 바로 파일에(lib bindAutosave) + 흐름이 어디서 끊겨도 `H-DX` [장치] 한 줄(단계·예외 이름·메시지 앞 200자 · 값·토큰 0).
//     ② 본 화면 진입 직후 브리핑 팝업을 사람처럼 [닫기](단추 줄) → 덮개가 사라질 때까지 · 안 뜨면 지나감 · `H-BRIEF` 한 줄.
//     ③ 결재 길 클릭 직전에 같은 함수로 한 번 더(늦게 뜬 몫).
//   🔴 3차 Q-2: 녹음기 기록을 sessionStorage 에 둔다(전체 새로고침에 살아남는다) + 이동 전 표지를 적고 이동 뒤 그대로인지
//     단언(아니면 [시험] 빨강 「녹음기 끊김」) + 이동이 전체 새로고침이었는지 got 에 사실로 남긴다.
'use strict';

const fs = require('fs');
const path = require('path');
const L = require('./lib');

const BASE = process.env.UI_BASE || 'http://127.0.0.1:18080';
const ALLOWED_HOSTS = new Set(['127.0.0.1', 'localhost']);
let logoutFailures = 0; // 2차 ② — runBrowser finally 의 로그아웃 실패 수
// 3차 Q-1 — 이 런에서 앞 브라우저가 「나」의 약관 동의를 끝냈나(그러면 뒤 브라우저는 약관 문이 안 서는 게 맞다).
let naAgreedThisRun = false;
const SEED_FACT_NA = '시드=나 약관 미동의(seed-children.js 는 부모·가만 동의)';
// 3차 Q-1 — 화면 자리 표지(Web 소스의 실제 요소).
//   약관 화면 = Terms.razor:11 `<div class="hitpan-terms-page">` (EmptyLayout · Terms.razor:2 → 본 화면 틀 없음)
//   본 화면   = MainLayout.razor:15 `<aside class="hitpan-sidebar-aside" aria-label="메인 메뉴">` ([Authorize] 틀에만 있다)
const TERMS_MARK = '.hitpan-terms-page';
const MAIN_MARK = 'aside.hitpan-sidebar-aside';
// 약관 문 대조에 쓰는 인증 API — 읽기 전용 GET · 시드②가 같은 길로 200 을 받았다(seed-children.js:97) ·
//   TermsConsentMiddleware.cs:14~25 우회 목록 밖 → 미동의면 403 forbidden_terms_consent(:97~103).
const TERMS_PROBE_API = '/api/chat/employees';
// 🟡 4차(10/29 · 대표 결재 「약관 결함은 별건 작업 · 장치는 분리」 · [1-V] 20261029_1V_약관미동의_본화면진입_원인.md)
//   자리=main 인데 로그인 응답 requiresTermsConsent=true 면(약관 미동의자가 본 화면에 자리 잡음 = 제품 결함 유력)
//   H-D2 빨강 대신 H-OBS-TERMS(kind OBS · judge.js OBS_ALLOWED) 한 줄에 관찰을 남기고, **화면과 같은 동의 API** 로 동의를 마친 뒤
//   다시 화면 로그인 → 본 화면 · 인증 API 200(H-D2c) 으로 장치를 닫는다. 동의 API·재로그인 실패는 [장치] 빨강(H-D2b).
//   결함이 고쳐지면 judge.js 의 허용 줄을 지운다 → 이 줄이 「꼴 이상」 빨강이 되고 H-D2 를 다시 빨강 게이트로 켠다.
const OBS_TERMS_ID = 'H-OBS-TERMS';
// 화면 동의 단추와 같은 끝점·같은 본문(Terms.razor:117~125 · 판 = Terms.razor:68 · 선택 마케팅은 화면에서도 안 누름 = false).
const TERMS_AGREE_API = '/api/auth/terms/agree';
const TERMS_VERSION = 'v2.0.0';
// 화면 로그아웃이 지우는 저장소 칸(Web AuthService.cs:279~281 · AuthModels.cs:7~9) — 제품 함수 hitpanStorage_remove(storage.js:87)로 지운다.
const AUTH_STORAGE_KEYS = ['hitpan_access_token', 'hitpan_refresh_token', 'hitpan_user_name'];
// 4차 ③ — 끝 상태(다음 갈래 K 가 「나」를 브라우저로 쓸 수 있나). 마지막으로 H-D2c 를 통과한 브라우저가 적는다.
let naEnd = null;

function plantFlags() {
  const p = path.join(__dirname, 'plant.flags');
  if (!fs.existsSync(p)) return new Set();
  return new Set(fs.readFileSync(p, 'utf8').split(/\s+/).filter(Boolean));
}

// window.open 녹음기 — (이름·주소)를 기록하고 원래 함수로 넘긴다(§4-1 기록 방법).
// ⬛ [낡은 녹음기 · 1~2차] 문서 메모리 배열 — 전체 새로고침이면 새 배열로 시작해 opens:0 이 무엇이 일어나도 0(리뷰서 Q-2).
// ⬛ const RECORDER = `(() => { const orig = window.open; window.__k35opens = [];
// ⬛   window.open = function (url, name, features) { window.__k35opens.push({ url: String(url ?? ''), name: String(name ?? '') });
// ⬛     return orig.call(window, url, name, features); }; })();`;
// 🔴 3차 Q-2 — 기록을 sessionStorage(탭 단위 · 같은 탭 이동·새로고침에 살아남는다)에 둔다.
//   __k35opens = 기록 · __k35mark = 이동 전 표지(이동 뒤 그대로여야 「녹음기 이어짐」) · __k35docs = 이 탭에서 뜬 문서 수(새로고침 여부).
//   sessionStorage 를 못 쓰는 문서(about:blank 등)는 녹음기를 안 건다 → window.__k35err 에 까닭을 남긴다(본 창에서 없으면 단언이 빨강).
const RECORDER = `(() => {
  let store = null;
  try { store = window.sessionStorage; store.getItem('__k35docs'); } catch (e) { window.__k35err = 'sessionStorage ' + e.name; store = null; }
  if (!store) return;
  const KEY = '__k35opens';
  const read = () => {
    try { return JSON.parse(store.getItem(KEY) || '[]'); } catch (e) { return [{ url: '', name: '', err: 'read ' + e.name }]; }
  };
  const orig = window.open;
  window.open = function (url, name, features) {
    try {
      const a = read();
      a.push({ url: String(url ?? ''), name: String(name ?? '') });
      store.setItem(KEY, JSON.stringify(a));
    } catch (e) { window.__k35err = 'write ' + e.name; }
    return orig.call(window, url, name, features);
  };
  const doc = Number(store.getItem('__k35docs') || '0') + 1;
  store.setItem('__k35docs', String(doc));
  window.__k35 = {
    doc,
    list: read,
    clear: () => store.setItem(KEY, '[]'),
    mark: (v) => store.setItem('__k35mark', String(v)),
    markNow: () => store.getItem('__k35mark'),
  };
})();`;

// tok = { access, deviceId, masked } — 2차: 가린 개수(⑤)와 로그아웃(②)에 쓴다. 값은 메모리에만.
//   3차: + loginTerms(로그인 응답의 약관 요구 값 · 사실 기록용) · access 는 약관 문 대조 API(브라우저 문맥)에도 쓴다.
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
      // 3차 Q-1 — 로그인 응답의 약관 요구 값(사실 기록용 · 판정은 화면과 API 동작으로 한다).
      if (/\/api\/auth\/login(\?|$)/i.test(u) && j && typeof j === 'object') {
        const t = j.requiresTermsConsent ?? j.RequiresTermsConsent ?? (j.data && (j.data.requiresTermsConsent ?? j.data.RequiresTermsConsent));
        tok.loginTerms = t === undefined ? '응답에 칸 없음' : String(t);
      }
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

// 3차 — Login.razor:427~443 새 기기 통지(ConfirmDialog · 제목 「기기 등록 안내」 · 확인 단추 「확인」 · ConfirmDialog.razor:14~33).
//   떠 있으면 [확인] 을 누르고 센다(note.closed). 못 누르면 센 뒤 다음 회차에 다시 본다(결국 자리 잡기 시간초과가 빨강으로 잡는다).
const DEVICE_NOTICE_TITLE = '기기 등록 안내';
async function closeDeviceNotice(page, note) {
  try {
    const dlg = page.locator('.mud-dialog', { hasText: DEVICE_NOTICE_TITLE });
    if ((await dlg.count()) === 0) return false;
    await dlg.first().getByRole('button', { name: '확인', exact: true }).click({ timeout: 10000 });
    note.closed += 1;
    return true;
  } catch (e) {
    note.errs += 1;
    console.log(`기기 등록 안내 창 닫기 실패 (${e.name}) — 다음 회차에 다시 본다`);
    return false;
  }
}

// 🔴 5차(10/29 · 런6 40750810674) — 일일 브리핑 팝업(DailyBriefingModal.razor · Dashboard.razor:219·296~308).
//   대시보드(`/`·`/dashboard`) 첫 그리기 뒤 localStorage `hitpan_briefing_dismissed_YYYYMMDD` 가 비어 있으면 뜬다(CI 브라우저는 매번 빈 저장소 → 매번 뜬다).
//   덮개 = `div.hp-brief-backdrop`(:27 · position:fixed inset:0 z-index:9100 :173~178) → 아래 화면의 모든 클릭을 가로챈다(런6 H-T2 TimeoutError).
//   사람처럼 닫는다: 아래 단추 줄 `.hp-brief-footer` 의 [닫기](:158 · Close → OnClosed 만 · 저장소 안 건드림).
//     · 머리 X 단추(:36 aria-label 「닫기」)와 이름이 같아서 단추 줄 안으로 좁힌다.
//     · 「오늘 그만 보기」(:157 DismissForToday)는 저장소에 날짜 키를 써서 다음 상태를 바꾸므로 **안 누른다**.
//     · 강제 클릭·덮개 지우기 같은 우회 없음 — [닫기] 를 누르고 덮개가 사라질 때까지 기다린다(@if (IsOpen) → 사라짐 :25).
//   안 뜨면 그냥 지나간다(없음 ≠ 실패). 닫으려 했는데 못 닫으면 [장치] 빨강.
const BRIEF_BACKDROP = '.hp-brief-backdrop';
const BRIEF_FOOTER = '.hp-brief-footer';
const BRIEF_CLOSE_TEXT = '닫기';
// 본 화면 진입 직후 팝업이 늦게 뜨는 몫까지 기다리는 시간(뜨면 바로 닫는다 · 안 뜨면 이 시간 뒤 지나간다).
const BRIEF_APPEAR_MS = 3000;

// br = { seen, closed, errs, lastErr } — 이 브라우저에서 본 팝업 수 · 닫은 수 · 못 닫은 수(값·토큰 0).
async function closeBriefing(page, br, appearMs = 0) {
  const backdrop = page.locator(BRIEF_BACKDROP);
  const end = Date.now() + appearMs;
  try {
    let n = await backdrop.count();
    while (n === 0 && Date.now() < end) {
      await new Promise((ok) => setTimeout(ok, 250));
      n = await backdrop.count();
    }
    if (n === 0) return true; // 안 뜸 → 지나간다
    br.seen += 1;
    await page.locator(BRIEF_FOOTER).getByRole('button', { name: BRIEF_CLOSE_TEXT, exact: true }).click({ timeout: 15000 });
    await backdrop.first().waitFor({ state: 'hidden', timeout: 15000 });
    const left = await backdrop.count();
    if (left > 0) {
      br.errs += 1;
      br.lastErr = `닫기 뒤 덮개 ${left}개 남음`;
      return false;
    }
    br.closed += 1;
    return true;
  } catch (e) {
    br.errs += 1;
    br.lastErr = L.describeError(e, 120);
    console.log(`브리핑 팝업 닫기 실패 (${e.name})`);
    return false;
  }
}

function briefText(br) {
  if (br.seen === 0) return '브리핑 팝업 안 뜸(지나감)';
  return `브리핑 팝업 뜸 ${br.seen}회 → 단추 줄 [닫기] 누름 · 덮개 사라짐 ${br.closed}회 · 못 닫음 ${br.errs}회${br.lastErr ? `(${br.lastErr})` : ''} · 「오늘 그만 보기」 안 누름`;
}

// 🔴 5차 ③ — 본 화면 진입 직후 공통 처리 한 곳. 이 뒤의 모든 클릭(결재 길 등)은 덮개 없는 화면에서 한다.
//   약관 화면(EmptyLayout)·로그인 화면에는 대시보드가 없어 팝업이 안 뜬다 — 약관 동의 클릭·로그인 클릭은 해당 없음(개발명세서 §10).
async function enterMainScreen(page, r, tag, step, br) {
  step.now = '본 화면 진입(브리핑 팝업 확인)';
  const ok = await closeBriefing(page, br, BRIEF_APPEAR_MS);
  L.rec(r, { ...tag, id: 'H-BRIEF', what: '본 화면 진입 직후 일일 브리핑 팝업: 뜨면 [닫기] 로 닫고 덮개가 사라짐 · 안 뜨면 지나감', got: `${briefText(br)} · 주소 ${new URL(page.url()).pathname}`, pass: ok, kind: 'DEVICE' });
  return ok;
}

// ⬛ [낡은 머리 · 1~2차] async function screenLogin(page, loginId, password) {
async function screenLogin(page, loginId, password, note) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  // ⬛ [낡은 줄 · 1차] getByLabel('아이디') · getByLabel('비밀번호') · getByRole('button', { name: '로그인' }) — 부분 일치.
  // 🔴 2차 ⑦: Login.razor:35·45 Label · :102 단추 글자 — 전문 일치(exact). 「최신버젼업데이트」 단추(:121~150)와 안 겹친다.
  await page.getByLabel('아이디', { exact: true }).fill(loginId, { timeout: 60000 });
  await page.getByLabel('비밀번호', { exact: true }).fill(password);
  // MudTextField 는 기본이 change(칸을 떠날 때) 반영 — 단추 Disabled(:95) 가 풀리게 칸을 떠난다(⚠️ 미측정 · 첫 런에서 확인).
  await page.getByLabel('비밀번호', { exact: true }).press('Tab');
  // 4차 ② — 관찰 기록기를 로그인 클릭 직전에 켠다(로그인부터 자리 잡을 때까지 · termsGate 가 끈다).
  if (note && note.nav) note.nav.arm();
  await page.getByRole('button', { name: '로그인', exact: true }).click();
  // ⬛ [낡은 줄 · 1~2차] await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 });
  // 🔴 3차 — 약관이 없는 로그인(이 런의 두 번째 브라우저 · 1~4런 미실행 길)은 새 기기면 Login.razor:427~443 「기기 등록 안내」 창을
  //   띄우고 **닫힐 때까지** 다음 이동을 기다린다(ConfirmDialog.ShowAsync await). 사람처럼 [확인] 을 누르며 /login 을 벗어나길 기다린다.
  const end = Date.now() + 60000;
  while (new URL(page.url()).pathname.startsWith('/login')) {
    if (Date.now() > end) {
      const err = new Error('로그인 뒤 60초 동안 /login 에 머묾');
      err.name = 'LoginStuckTimeout';
      throw err;
    }
    await closeDeviceNotice(page, note);
    await new Promise((ok) => setTimeout(ok, 500));
  }
  // ⬛ [낡은 갈래 · 1~2차 — 리뷰서 Q-1 🔴 거짓 PASS] 주소가 /login 을 벗어난 **즉시** 보고 갈랐다 → /terms 강제 이동 전에 no-terms.
  // ⬛ if (new URL(page.url()).pathname.startsWith('/terms')) {
  // ⬛   for (const label of TERMS_REQUIRED_LABELS) await page.getByRole('checkbox', { name: label, exact: true }).check();
  // ⬛   await page.getByRole('button', { name: '동의하고 계속', exact: true }).click();
  // ⬛   await page.waitForURL((u) => !u.pathname.startsWith('/terms'), { timeout: 60000 });
  // ⬛   return 'terms-agreed';
  // ⬛ }
  // ⬛ return 'no-terms';
  // 🔴 3차 — 판단은 termsGate(아래)가 화면이 자리 잡은 뒤에 한다. 여기서는 「로그인 요청이 받아들여졌다」까지만.
}

// 3차 Q-1 — 지금 화면이 어디인가(표지 기준). 이동 중이면 문맥이 사라져 evaluate 가 실패한다 → 센 뒤 null(다음 회차에 다시 본다).
async function whereNow(page, probe) {
  try {
    return await page.evaluate(([t, m]) => {
      if (document.querySelector(t)) return 'terms';
      if (document.querySelector(m)) return 'main';
      return null;
    }, [TERMS_MARK, MAIN_MARK]);
  } catch (e) {
    probe.navErrors += 1;
    probe.lastErr = e.name;
    return null;
  }
}

// 3차 Q-1 — 화면이 자리 잡을 때까지: 같은 표지·같은 주소가 1초 간격 3회(=2초 이상) 이어지면 자리 잡음.
//   로그인 직후 인증 방송으로 잠깐 본 화면이 그려졌다가 /terms 강제 이동(Login.razor:398~401)이 오는 경합을 2초 창으로 거른다.
async function settleScreen(page, timeoutMs = 90000, note = { closed: 0, errs: 0 }) {
  const probe = { navErrors: 0, lastErr: null };
  const end = Date.now() + timeoutMs;
  let last = null;
  let streak = 0;
  while (Date.now() < end) {
    // 새 기기 통지 창이 본 화면 위에 떠 있으면 사람처럼 닫는다(닫히면 Login 이 이어서 이동한다 → 자리 잡기를 새로 센다).
    if (await closeDeviceNotice(page, note)) { last = null; streak = 0; }
    const w = await whereNow(page, probe);
    const p = new URL(page.url()).pathname;
    const key = w ? `${w}@${p}` : null;
    if (key && key === last) streak += 1;
    else { last = key; streak = key ? 1 : 0; }
    if (streak >= 3) return { where: w, path: p, navErrors: probe.navErrors };
    await new Promise((ok) => setTimeout(ok, 1000));
  }
  return { where: null, path: new URL(page.url()).pathname, last, navErrors: probe.navErrors, lastErr: probe.lastErr };
}

// 3차 Q-1 — 브라우저 문맥에서 인증 API 한 번(화면이 쓰는 같은 머리글 · 응답 본문은 밖으로 안 낸다 · 표지 여부만).
async function apiInPage(page, tok) {
  if (!tok.access) return { status: null, err: '토큰 없음' };
  try {
    return await page.evaluate(async ({ api, access, dev }) => {
      const headers = { Authorization: `Bearer ${access}` };
      if (dev) headers['X-HitPan-Device-Id'] = dev;
      const res = await fetch(api, { headers, cache: 'no-store' });
      const text = await res.text();
      return { status: res.status, termsBlock: text.includes('forbidden_terms_consent') };
    }, { api: TERMS_PROBE_API, access: tok.access, dev: tok.deviceId });
  } catch (e) {
    return { status: null, err: `요청 실패 ${e.name}` };
  }
}

// 4차 ② — 로그인 클릭부터 자리 잡을 때까지의 관찰 기록기(켜진 동안만 쌓는다 · 값·토큰 0).
//   doc  = 주 프레임 문서 이동 요청의 **경로만**(쿼리·조각 버림) + 끝(완료 상태 코드 / 실패 사유 = 취소 여부)
//   st   = `api/auth/terms/status` 응답(상태 코드 · hasAgreed 값만)
function watchNav(page, context) {
  const nav = { armed: false, doc: [], st: [], noFrame: 0, noFrameErr: null };
  const pathOf = (u) => {
    try { return new URL(u).pathname; } catch (e) { return `(주소 해석 실패 ${e.name})`; }
  };
  const find = (req) => nav.doc.find((x) => x.req === req);
  page.on('request', (req) => {
    if (!nav.armed) return;
    let mainDoc = false;
    try {
      mainDoc = req.isNavigationRequest() && req.frame() === page.mainFrame();
    } catch (e) {
      // 서비스 워커 요청은 frame() 이 던진다(프레임 없음) — 문서 이동이 아니므로 세기만 한다.
      nav.noFrame += 1;
      nav.noFrameErr = e.name;
      return;
    }
    if (mainDoc) nav.doc.push({ req, path: pathOf(req.url()), end: '대기' });
  });
  page.on('requestfinished', (req) => {
    const x = find(req);
    if (!x) return;
    x.end = '완료';
    req.response().then((res) => { if (res) x.end = `완료 ${res.status()}`; }).catch((e) => console.log(`문서 이동 응답 읽기 실패 (${e.name})`));
  });
  page.on('requestfailed', (req) => {
    const x = find(req);
    if (!x) return;
    const f = req.failure();
    x.end = `실패 ${f && f.errorText ? f.errorText : '사유 없음'}`;
  });
  context.on('response', async (res) => {
    if (!nav.armed || !/\/api\/auth\/terms\/status(\?|$)/i.test(res.url())) return;
    const one = { status: res.status(), hasAgreed: '못 읽음' };
    nav.st.push(one);
    try {
      const j = await res.json();
      const v = j && (j.hasAgreed ?? j.HasAgreed);
      one.hasAgreed = v === undefined ? '칸 없음' : String(v);
    } catch (e) {
      one.hasAgreed = `본문 못 읽음 ${e.name}`;
    }
  });
  nav.arm = () => { nav.doc = []; nav.st = []; nav.noFrame = 0; nav.armed = true; };
  nav.stop = () => { nav.armed = false; };
  nav.docText = () => (nav.doc.length ? nav.doc.map((x) => `${x.path}(${x.end})`).join(' → ') : '기록 0');
  nav.stText = () => `${nav.st.length}회${nav.st.length ? ` [${nav.st.map((x) => `${x.status}:hasAgreed=${x.hasAgreed}`).join(', ')}]` : ''}`;
  return nav;
}

// 4차 ② — 화면 위 알림 띠(MudAlert)·토스트(MudSnackbar) 글자. 하나당 앞 120자 · 최대 6개(값·토큰 아님 · 기록 전 sanitize).
async function noticeTexts(page) {
  try {
    return await page.evaluate(() => {
      const pick = (sel) => [...document.querySelectorAll(sel)]
        .map((e) => String(e.innerText || e.textContent || '').replace(/\s+/g, ' ').trim())
        .filter(Boolean);
      return [...pick('.mud-alert').map((s) => `띠:${s}`), ...pick('.mud-snackbar').map((s) => `토스트:${s}`)]
        .slice(0, 6).map((s) => s.slice(0, 120));
    });
  } catch (e) {
    return [`읽기 실패 ${e.name}`];
  }
}

// 4차 ② — 화면 동의 단추와 같은 API 로 필수 4 동의(브라우저 문맥 · 화면이 쓰는 같은 머리글 · 응답 본문은 밖으로 안 낸다).
async function agreeViaApi(page, tok) {
  if (!tok.access) return { status: null, err: '토큰 없음' };
  try {
    return await page.evaluate(async ({ api, access, dev, ver }) => {
      const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${access}` };
      if (dev) headers['X-HitPan-Device-Id'] = dev;
      const body = { agreeService: true, agreePrivacy: true, agreeSubscription: true, agreeDataOwnership: true, agreeMarketing: false, termsVersion: ver };
      const res = await fetch(api, { method: 'POST', headers, body: JSON.stringify(body), cache: 'no-store' });
      return { status: res.status };
    }, { api: TERMS_AGREE_API, access: tok.access, dev: tok.deviceId, ver: TERMS_VERSION });
  } catch (e) {
    return { status: null, err: `요청 실패 ${e.name}` };
  }
}

// 4차 ② — 화면 로그아웃과 같은 두 가지: 서버 로그아웃(logoutViaApi) + 이 브라우저 저장소의 로그인 칸 지움(제품 함수).
async function forgetBrowserLogin(page) {
  try {
    return await page.evaluate((keys) => {
      if (typeof window.hitpanStorage_remove !== 'function') return { ok: false, got: 'hitpanStorage_remove 없음' };
      for (const k of keys) window.hitpanStorage_remove(k);
      return { ok: true, got: `저장소 칸 ${keys.length}개 지움` };
    }, AUTH_STORAGE_KEYS);
  } catch (e) {
    return { ok: false, got: `저장소 지우기 실패 ${e.name}` };
  }
}

// 4차 ② — 자리=main + requiresTermsConsent=true: 관찰(OBS) 기록 → API 동의 → 로그아웃 → 다시 화면 로그인 → 본 화면.
//   돌려주는 값 = 동의·재로그인이 끝났나(실패 = H-D2b [장치] 빨강 · 그 뒤 H-D2c 는 termsGate 가 잰다).
async function observeTermsThenAgree(page, r, tag, tok, step, note, s1, d2got) {
  step.now = '약관 관찰(자리=main · 동의 요구)';
  const probe = await apiInPage(page, tok);
  const texts = await noticeTexts(page);
  const obsGot = {
    사실: d2got,
    문서이동: note.nav ? note.nav.docText() : '기록기 없음',
    약관상태: note.nav ? note.nav.stText() : '기록기 없음',
    인증API: `${TERMS_PROBE_API} status=${probe.status}${probe.err ? ` ${probe.err}` : ''} forbidden_terms_consent=${probe.termsBlock === true} (403 기대 ${probe.status === 403 && probe.termsBlock === true ? '맞음' : '어긋남'})`,
    알림: texts.length ? texts : ['없음'],
  };
  // pass=false = 「원래 게이트였다면 빨강」(judge 는 OBS 의 pass 를 판정에 안 쓴다 · 꼴만 본다).
  L.rec(r, { ...tag, id: OBS_TERMS_ID, what: '[제품 관찰 · 빨강 아님] 약관 미동의(로그인 응답 requiresTermsConsent=true)인데 화면이 본 화면에 자리 잡음 — 별건 약관 결함([1-V] 20261029)', got: obsGot, pass: false, kind: 'OBS' });

  // 🔴 4차 PM 결정 ⑤ — 관찰로 돌린 것은 화면 이동 결함뿐. 서버가 미동의자 API 를 막는 것은 헌법상 필수 → 403 아니면 H-D2a 와 같은 [장치] 빨강.
  if (!(probe.status === 403 && probe.termsBlock === true)) {
    L.rec(r, { ...tag, id: 'H-D2a', what: `약관 문 대조: 동의 전 인증 API ${TERMS_PROBE_API} = 403 forbidden_terms_consent (브라우저 문맥)`, got: `관찰 갈래 · 동의 전 API=${probe.status}${probe.err ? ` ${probe.err}` : ''} · forbidden_terms_consent=${probe.termsBlock === true}`, pass: false, kind: 'DEVICE' });
    return false;
  }
  step.now = '약관 동의(화면과 같은 API)';
  const ag = await agreeViaApi(page, tok);
  const agOk = typeof ag.status === 'number' && ag.status >= 200 && ag.status < 300;
  if (!agOk) {
    L.rec(r, { ...tag, id: 'H-D2b', what: `약관 필수 4개 동의(화면과 같은 API ${TERMS_AGREE_API}) → 다시 로그인 → 본 화면`, got: `동의 API status=${ag.status}${ag.err ? ` ${ag.err}` : ''}`, pass: false, kind: 'DEVICE' });
    return false;
  }
  step.now = '로그아웃(동의 뒤 다시 로그인하려고)';
  const lo = await logoutViaApi(tok);
  const fg = await forgetBrowserLogin(page);
  if (!lo.ok || !fg.ok) {
    L.rec(r, { ...tag, id: 'H-D2b', what: `약관 필수 4개 동의(화면과 같은 API ${TERMS_AGREE_API}) → 다시 로그인 → 본 화면`, got: `동의 API status=${ag.status} · 로그아웃 ${lo.got} · ${fg.got}`, pass: false, kind: 'DEVICE' });
    if (!lo.ok) logoutFailures += 1;
    return false;
  }
  // 옛 토큰은 서버에서 끝났다 — 새 로그인 응답을 다시 기다리게 비운다(H-D4 는 새 토큰을 로그아웃한다).
  tok.access = null;
  tok.loginTerms = '응답 못 봄';
  step.now = '다시 화면 로그인(동의 뒤)';
  await screenLogin(page, process.env.UI_LOGIN_NA || 'ci-na', process.env.UI_PW_NA || '', note);
  const s2 = await settleScreen(page, 90000, note);
  if (note.nav) note.nav.stop();
  await waitMasked(tok);
  const s2pass = s2.where === 'main' && tok.loginTerms === 'false';
  L.rec(r, { ...tag, id: 'H-D2b', what: `약관 필수 4개 동의(화면과 같은 API ${TERMS_AGREE_API}) → 로그아웃 → 다시 화면 로그인 → 본 화면 · 로그인 응답 requiresTermsConsent=false`, got: `동의 API status=${ag.status} · 로그아웃 ${lo.got} · ${fg.got} · 재로그인 requiresTermsConsent=${tok.loginTerms} · 자리=${s2.where}@${s2.path}${s2.where ? '' : ` · 시간초과(마지막 ${s2.last} · ${s2.lastErr})`} · 문서 이동 ${note.nav ? note.nav.docText() : '-'}`, pass: s2pass, kind: 'DEVICE' });
  return s2pass;
}

// 3차 Q-1 — 약관 문 판정 + 동의 + 동작 확인. 기록하고 성공 여부를 돌려준다(실패 = [장치] 빨강).
//   기대: 이 런에서 아직 아무도 「나」를 동의시키지 않았으면 약관 화면 · 앞 브라우저가 동의했으면 본 화면.
async function termsGate(page, r, tag, tok, step, note) {
  const expect = naAgreedThisRun ? 'main' : 'terms';
  naEnd = null; // 4차 ③ — 끝 상태는 **마지막 브라우저**가 다시 증명해야 한다(앞 브라우저 기록을 물려받지 않음)
  step.now = '화면 자리 잡기(로그인 뒤)';
  const s1 = await settleScreen(page, 90000, note);
  if (note.nav) note.nav.stop(); // 4차 ② — 관찰은 「자리 잡을 때까지」
  await waitMasked(tok); // 응답 처리기가 늦게 끝날 수 있다 — API 대조에 토큰이 필요하다
  const d2got = `${SEED_FACT_NA} · 앞 브라우저 동의=${naAgreedThisRun} · 기대=${expect} · 로그인 응답 requiresTermsConsent=${tok.loginTerms} · 자리=${s1.where}@${s1.path} · 이동 중 재시도 ${s1.navErrors} · 기기 등록 안내 닫음 ${note.closed}(실패 ${note.errs})`;
  // 🟡 4차 ② — 자리=main 이면서 로그인 응답이 약관 동의를 요구 = 제품 관찰(H-OBS-TERMS) → API 동의 → 재로그인 → 아래 H-D2c.
  //   (H-D2 는 이 갈래에서 안 적는다 — 결함이 고쳐지면 judge.js 허용 줄을 지워 H-OBS-TERMS 가 빨강이 되고, 이 갈래도 함께 걷는다)
  const obsCase = s1.where === 'main' && tok.loginTerms === 'true';
  if (obsCase) {
    const agreed = await observeTermsThenAgree(page, r, tag, tok, step, note, s1, d2got);
    if (!agreed) return false;
    naAgreedThisRun = true;
  }
  // ⬛ [낡은 줄 · 3차] const d2pass = s1.where === expect;
  const d2pass = obsCase || s1.where === expect;
  // 4차 — 끝에 문서 이동 기록을 덧붙인다([1-V] §5 대조군: 동의 끝난 로그인이면 `/` 한 번뿐이어야).
  if (!obsCase) L.rec(r, { ...tag, id: 'H-D2', what: '화면 로그인(나) → 화면 자리 잡음 · 약관 문이 기대대로(시드상 미동의면 선다)', got: `문서 이동 ${note.nav ? note.nav.docText() : '-'} · ${s1.where ? d2got : `${d2got} · 시간초과(마지막 ${s1.last} · ${s1.lastErr})`}`, pass: d2pass, kind: 'DEVICE' });
  if (!d2pass) return false;

  if (s1.where === 'terms') {
    step.now = '약관 문 대조(동의 전 API)';
    const pre = await apiInPage(page, tok);
    const prePass = pre.status === 403 && pre.termsBlock === true;
    L.rec(r, { ...tag, id: 'H-D2a', what: `약관 문 대조: 동의 전 인증 API ${TERMS_PROBE_API} = 403 forbidden_terms_consent (브라우저 문맥)`, got: pre, pass: prePass, kind: 'DEVICE' });
    if (!prePass) return false;

    step.now = '약관 4개 동의(화면 그대로)';
    const how = [];
    for (const label of TERMS_REQUIRED_LABELS) {
      const cb = page.getByRole('checkbox', { name: label, exact: true });
      try {
        await cb.check({ timeout: 30000 });
        how.push('check');
      } catch (e) {
        // 첫 실행 길(1~4런 미실행) — 접근성 이름이 안 맞으면 화면 글자를 누르는 사람 동작으로 한 번 더. 최종 판정은 아래 단추·API 동작.
        //   check 가 눌렀는데 상태 확인만 늦은 경우 다시 누르면 꺼진다 → 켜져 있지 않을 때만 누른다.
        console.log(`약관 체크상자 역할 선택 실패 (${e.name}) → 켜짐 확인 뒤 글자 클릭`);
        const on = await cb.isChecked({ timeout: 2000 }).catch((ie) => {
          console.log(`약관 체크상자 상태 읽기 실패 (${ie.name}) → 꺼짐으로 본다`);
          return false;
        });
        if (on) {
          how.push('check-late');
        } else {
          await page.getByText(label, { exact: true }).click({ timeout: 30000 });
          how.push('text');
        }
      }
    }
    // Terms.razor:53~56 — 필수 4개가 다 켜져야 단추가 풀린다(Disabled) → click 이 풀릴 때까지 기다리는 것 자체가 동의 상태 확인.
    await page.getByRole('button', { name: '동의하고 계속', exact: true }).click({ timeout: 30000 });
    step.now = '화면 자리 잡기(동의 뒤)';
    const s2 = await settleScreen(page, 90000, note);
    const s2pass = s2.where === 'main';
    L.rec(r, { ...tag, id: 'H-D2b', what: '약관 필수 4개 화면에서 동의 → 본 화면 자리 잡음', got: `누름=${how.join(',')} · 자리=${s2.where}@${s2.path}${s2.where ? '' : ` · 시간초과(마지막 ${s2.last} · ${s2.lastErr})`}`, pass: s2pass, kind: 'DEVICE' });
    if (!s2pass) return false;
    naAgreedThisRun = true;
  }

  step.now = '인증 API 200(약관 통과 뒤)';
  const post = await apiInPage(page, tok);
  const postPass = post.status === 200;
  L.rec(r, { ...tag, id: 'H-D2c', what: `인증 API ${TERMS_PROBE_API} = 200 (브라우저 문맥 · 약관 미들웨어 통과)`, got: post, pass: postPass, kind: 'DEVICE' });
  // 4차 ③ — 끝 상태 후보(동의 완료 = 이 런에서 동의를 마쳤거나 로그인 응답이 동의를 요구하지 않음).
  if (postPass) naEnd = { browser: tag.browser, agreed: naAgreedThisRun || tok.loginTerms === 'false', loginTerms: tok.loginTerms, api: post.status, via: obsCase ? 'API 동의(관찰 갈래)' : (s1.where === 'terms' ? '화면 동의' : '이미 동의') };
  return postPass;
}

async function runBrowser(pw, browserName, r, flags, full) {
  const shots = path.join(L.outDir(), 'main', 'shots');
  // ⬛ [낡은 줄 · 1차] const traces = path.join(L.outDir(), 'trace'); — 2차 ① trace 제거
  fs.mkdirSync(shots, { recursive: true });
  let browser;
  let context;
  let failed = false;
  const tag = { browser: browserName, scope: 'H' };
  // ⬛ [낡은 줄 · 2차] const tok = { access: null, deviceId: null, masked: 0 };
  const tok = { access: null, deviceId: null, masked: 0, loginTerms: '응답 못 봄' }; // 3차 Q-1 loginTerms = 사실 기록용
  // 🔴 5차 ① — 끊긴 자리 기록용: 단계(step)·화면(page)을 try 밖에 둔다(catch 가 읽는다) · br = 브리핑 팝업 셈(5차 ②)
  const step = { now: '브라우저 문맥 준비' };
  const br = { seen: 0, closed: 0, errs: 0, lastErr: null };
  let page = null;
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
    // ⬛ [낡은 줄 · 1~4차] const page = await context.newPage();
    page = await context.newPage();

    // ⬛ [낡은 줄 · 1~2차] how = await screenLogin(...) → H-D2 「화면 로그인(나) · 약관 화면 그대로」 got=`${how} → 주소` pass:true
    //   — 약관 문을 재기 전에 지나쳐 「나」 미동의인데 no-terms PASS(리뷰서 Q-1 🔴). 3차: termsGate 가 H-D2·H-D2a·b·c 를 기록.
    // ⬛ [낡은 줄 · 1~4차] const step = { now: '화면 로그인' }; — 5차: try 밖으로(위)
    step.now = '화면 로그인';
    // ⬛ [낡은 줄 · 3차] const note = { closed: 0, errs: 0 }; // 3차 — 기기 등록 안내 창 닫은 수
    // 4차 ② — + nav(로그인부터 자리 잡을 때까지 문서 이동·약관 상태 응답 관찰 기록기)
    const note = { closed: 0, errs: 0, nav: watchNav(page, context) };
    try {
      await screenLogin(page, process.env.UI_LOGIN_NA || 'ci-na', process.env.UI_PW_NA || '', note);
      const gateOk = await termsGate(page, r, tag, tok, step, note);
      if (!gateOk) {
        failed = true;
        await page.screenshot({ path: path.join(shots, `H-device-${browserName}.png`) }).catch((se) => console.log(`스크린샷 실패 ${se.name}`));
        return false;
      }
    } catch (e) {
      failed = true;
      L.rec(r, { ...tag, id: 'H-D2x', what: `화면 로그인·약관 단계=${step.now}`, got: `실패 ${e.name} @ ${new URL(page.url()).pathname} · ${SEED_FACT_NA}`, pass: false, kind: 'DEVICE' });
      await page.screenshot({ path: path.join(shots, `H-device-${browserName}.png`) }).catch((se) => console.log(`스크린샷 실패 ${se.name}`));
      return false;
    }

    // 2차 ⑤ (H3V-2) — 화면 로그인 통과 + 가린 브라우저 토큰 0건 = 런 중 값이 S10 검색 대상에 안 들어갔다 → [장치] 빨강.
    // 🔴 5차 ②③ — 본 화면 진입 직후 브리핑 팝업 공통 처리(뜨면 [닫기] · 안 뜨면 지나감 · H-BRIEF 한 줄).
    if (!(await enterMainScreen(page, r, tag, step, br))) {
      failed = true;
      await page.screenshot({ path: path.join(shots, `H-device-${browserName}.png`) }).catch((se) => console.log(`스크린샷 실패 ${se.name}`));
      return false;
    }

    step.now = '토큰 가림 확인(H-D3)';
    const masked = await waitMasked(tok);
    const maskedOk = masked > 0 && tok.access !== null;
    L.rec(r, { ...tag, id: 'H-D3', what: '화면 로그인 뒤 브라우저 접속 토큰을 가리고 S10 검색 대상에 넣음', got: `가린 토큰 ${masked}건`, pass: maskedOk, kind: 'DEVICE' });
    if (!maskedOk) {
      failed = true;
      await page.screenshot({ path: path.join(shots, `H-device-${browserName}.png`) }).catch((se) => console.log(`스크린샷 실패 ${se.name}`));
      return false;
    }

    // H-T1 녹음기 대조 — 직접 1회 부르면 1건 · 이름 hitpanChat 이어야 한다.
    // ⬛ [낡은 줄 · 1~2차] window.__k35opens.length = 0 … return window.__k35opens.slice() — 3차 Q-2 sessionStorage 녹음기로.
    step.now = '녹음기 대조(H-T1)';
    const t1 = await page.evaluate(() => {
      if (!window.__k35) return { err: window.__k35err || '녹음기 없음' };
      window.__k35.clear();
      const w = window.open('/chat-window/ci-probe', 'hitpanChat');
      if (w) w.close();
      return window.__k35.list();
    });
    const t1pass = Array.isArray(t1) && t1.length === 1 && t1[0].name === 'hitpanChat';
    if (!t1pass) failed = true;
    L.rec(r, { ...tag, id: 'H-T1', what: '녹음기: window.open 직접 1회 → 기록 1 · 이름 hitpanChat', got: t1, pass: t1pass, kind: 'TEST' });

    if (full) {
      step.now = '결재 길 흉내 준비(H-T2)';
      // 결재 길 흉내 — 본 창 링크 클릭(Blazor 길잡이가 본 창에서 이동시킨다).
      const before = context.pages().length;
      // ⬛ [낡은 줄 · 1~2차] window.__k35opens.length = 0 (문서 메모리) → 이동 뒤 window.__k35opens.length — 3차 Q-2.
      // 🔴 3차 Q-2 — 이동 전 표지를 sessionStorage 에 적는다. 이동 뒤 그대로면 「녹음기 이어짐」(기록이 같은 곳에 쌓였다).
      const mark = `ci-mark-${Date.now()}`;
      const pre = await page.evaluate((m) => {
        if (!window.__k35) return { err: window.__k35err || '녹음기 없음' };
        window.__k35.clear();
        window.__k35.mark(m);
        const a = document.createElement('a');
        a.href = '/approval/pending';
        a.id = 'ci-approval-probe';
        a.textContent = 'ci';
        // 런7(40754109103) H-DX: body 끝에 붙인 이 시험용 링크가 본 화면 아래쪽 고정 띠 밑에 깔려 클릭 30초 시간초과.
        //   시험이 만든 링크 자신만 화면 위쪽 보이는 자리에 둔다(제품 요소·덮개는 무접촉 · 강제 클릭 0) — PM 장치 고침 · [4] 확인 대상(SoD).
        a.style.cssText = 'position:fixed;top:8px;left:50%;z-index:2147483647;padding:2px 6px;background:#fff;';
        document.body.appendChild(a);
        return { doc: window.__k35.doc };
      }, mark);
      // 🔴 5차 ③ — 클릭 직전에 한 번 더(팝업이 진입 확인 뒤 늦게 떴을 몫 · 같은 함수 · 기다림 0).
      const seenBefore = br.seen;
      if (!(await closeBriefing(page, br, 0))) {
        failed = true;
        L.rec(r, { ...tag, id: 'H-BRIEF', what: '결재 길 클릭 직전 일일 브리핑 팝업을 [닫기] 로 닫음', got: briefText(br), pass: false, kind: 'DEVICE' });
        await page.screenshot({ path: path.join(shots, `H-device-${browserName}.png`) }).catch((se) => console.log(`스크린샷 실패 ${se.name}`));
        return false;
      }
      if (br.seen > seenBefore) L.rec(r, { ...tag, id: 'H-BRIEF', what: '결재 길 클릭 직전 일일 브리핑 팝업(늦게 뜸)을 [닫기] 로 닫음', got: briefText(br), pass: true, kind: 'DEVICE' });
      step.now = '결재 길 링크 클릭(H-T2)';
      await page.click('#ci-approval-probe');
      step.now = '결재 길 이동 뒤 읽기(H-T2)';
      // 런8(40755189494) H-T2: 제품은 /approval/pending 을 ApprovalRedirectPage 가 받아 결재관리 /approval 로 넘긴다
      //   (ApprovalRedirectPage.razor:1 · ApprovalPendingPage.razor:4 설명) — 본 창이 결재 화면에 닿았는지는 /approval 머리로 본다. PM 고침 · [4] 확인 대상(SoD).
      await page.waitForURL((u) => u.pathname.startsWith('/approval'), { timeout: 30000 }).catch((we) => console.log(`결재 길 이동 대기 끝 (${we.name})`));
      await page.waitForLoadState('load', { timeout: 30000 }).catch((le) => console.log(`결재 길 문서 적재 대기 끝 (${le.name})`));
      const post = await page.evaluate(() => {
        if (!window.__k35) return { err: window.__k35err || '녹음기 없음' };
        return { opens: window.__k35.list().length, mark: window.__k35.markNow(), doc: window.__k35.doc };
      }).catch((pe) => ({ err: `읽기 실패 ${pe.name}` }));
      const alive = !pre.err && !post.err && post.mark === mark;
      const opens = alive ? post.opens : null;
      // reload = 이동이 전체 새로고침이었나(문서 번호가 바뀌었나) — 사실 기록 · 판정에는 안 쓴다(어느 쪽이든 기록은 이어진다).
      const got = { brief: br.seen === 0 ? '안 뜸' : `닫음 ${br.closed}/${br.seen}`, opens, recorder: alive ? '이어짐' : `끊김 ${pre.err || post.err || '표지 다름'}`, reload: alive ? post.doc !== pre.doc : null, path: new URL(page.url()).pathname, pages: context.pages().length - before };
      L.rec(r, { ...tag, id: 'H-T2r', what: '녹음기 이어짐: 이동 전 표지가 이동 뒤 그대로(끊기면 window.open 0회 단언이 아무것도 안 잰다)', got, pass: alive, kind: 'TEST' });
      if (!alive) failed = true;
      if (flags.has('red')) {
        // 🔴 심은 빨강(§4-3 ⓒ) — 거짓 기대. 이 줄이 초록이면 장치가 「항상 초록」이다.
        const pass = opens === 1;
        if (!pass) failed = true;
        L.rec(r, { ...tag, id: 'H-PLANT', what: '[심은 빨강] 결재 알림 클릭 시 window.open 1회 (거짓 기대)', got, pass, kind: 'TEST' });
      } else {
        // ⬛ [낡은 줄 · 런8 빨강] const pass = opens === 0 && got.path.startsWith('/approval/pending');
        const pass = opens === 0 && got.path.startsWith('/approval') && got.pages === 0;
        if (!pass) failed = true;
        L.rec(r, { ...tag, id: 'H-T2', what: '본 창 이동(결재 길 흉내 · /approval/pending → 제품이 /approval 로 넘김) → window.open 0회 · 새 창 0 · 본 창 결재 화면', got, pass, kind: 'TEST' });
      }
    }
    await page.screenshot({ path: path.join(shots, `H-device-${browserName}.png`) }).catch((se) => console.log(`스크린샷 실패 ${se.name}`));
    return !failed;
  } catch (e) {
    // 🔴 5차 ① — 어떤 예외로 흐름이 끊겨도: 앞 줄들은 이미 파일에 있다(lib bindAutosave) + 끊긴 자리를 [장치] 한 줄로.
    //   런6: H-T2 클릭이 브리핑 덮개에 막혀 TimeoutError → 프로세스가 죽어 결과표 0 줄(콘솔엔 PASS 7).
    failed = true;
    const at = page ? new URL(page.url()).pathname : '화면 없음';
    L.rec(r, { ...tag, id: 'H-DX', what: `[장치] 브라우저 흐름 끊김 — 단계=${step.now}`, got: `${L.describeError(e)} @ ${at} · ${briefText(br)}`, pass: false, kind: 'DEVICE' });
    if (page) await page.screenshot({ path: path.join(shots, `H-device-${browserName}.png`) }).catch((se) => console.log(`스크린샷 실패 ${se.name}`));
    return false;
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

// 🔴 5차 ① — 프로세스 밖으로 새는 예외(처리기 없는 Promise 거절 등)도 결과표에 [장치] 한 줄을 남기고 빨강으로 끝낸다.
//   런6 은 처리기 없는 거절(triggerUncaughtException)로 죽었다 — 그때 결과표는 저장 전이라 0 줄이었다.
function onFatal(r, where) {
  return (e) => {
    try {
      L.rec(r, { id: 'H-DX', what: `[장치] 브라우저 흐름 끊김 — 단계=프로세스 밖 예외(${where})`, got: L.describeError(e), pass: false, kind: 'DEVICE' });
    } catch (e2) {
      console.log(`⚫ FAIL [장치] 끊긴 자리 기록 실패 (${e2.name})`);
    }
    process.exit(1);
  };
}

async function main() {
  const r = L.loadResult();
  // 🔴 5차 ① — 줄마다 바로 파일에(이 뒤 rec 는 넣는 즉시 저장 · 콘솔 = 결과표).
  L.bindAutosave(r);
  process.on('unhandledRejection', onFatal(r, 'unhandledRejection'));
  process.on('uncaughtException', onFatal(r, 'uncaughtException'));
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
  // ⬛ [낡은 줄 · 1~4차] const okC = await runBrowser(pw, 'chromium', r, flags, true);
  // ⬛ [낡은 줄 · 1~4차] const okW = await runBrowser(pw, 'webkit', r, flags, false);
  // 🔴 5차 ① — runBrowser 안 catch 가 못 잡은 것(finally 안 예외 등)도 [장치] 한 줄로 남기고 다음 브라우저로 간다.
  const safeRun = async (name, full) => {
    try {
      return await runBrowser(pw, name, r, flags, full);
    } catch (e) {
      L.rec(r, { browser: name, scope: 'H', id: 'H-DX', what: '[장치] 브라우저 흐름 끊김 — 단계=브라우저 정리(finally)', got: L.describeError(e), pass: false, kind: 'DEVICE' });
      return false;
    }
  };
  const okC = await safeRun('chromium', true);
  const okW = await safeRun('webkit', false);
  // 🟡 4차 ③ — 끝 상태 한 줄: 「나 = 동의 완료 · 본 화면 · 인증 API 200」(마지막 브라우저가 증명) → 다음 갈래 K 가 「나」를 브라우저로 쓴다.
  //   각 브라우저는 끝에 로그아웃(H-D4)하므로 K 는 화면 로그인부터 시작한다(약관 문 없이 본 화면이어야 한다).
  const endOk = !!(naEnd && naEnd.agreed && naEnd.api === 200);
  L.rec(r, { id: 'H-NA-END', what: '끝 상태: 나 = 약관 동의 완료 · 본 화면 · 인증 API 200 (다음 갈래 K 가 브라우저로 쓸 수 있다 · 끝에 로그아웃함)', got: naEnd ? `동의 완료=${naEnd.agreed}(${naEnd.via}) · 마지막 로그인 응답 requiresTermsConsent=${naEnd.loginTerms} · 본 화면 · ${TERMS_PROBE_API} ${naEnd.api} · 증명 브라우저 ${naEnd.browser} · 로그아웃 실패 ${logoutFailures}` : '마지막 브라우저가 H-D2c 까지 못 감', pass: endOk, kind: 'DEVICE' });
  L.saveResult(r);
  // ⬛ [낡은 줄 · 2~3차] process.exit(okC && okW && logoutFailures === 0 ? 0 : 1);
  process.exit(okC && okW && logoutFailures === 0 && endOk ? 0 : 1);
}

// 2차 ⑧ — 로컬 확인(plant.flags 해석)용으로 함수만 꺼내 쓸 수 있게 한다. CI 는 `node device-check.js` 직접 실행.
if (require.main === module) main();
// ⬛ [낡은 줄 · 2차] module.exports = { plantFlags, TERMS_REQUIRED_LABELS };
// 3차 — 로컬 확인(가짜 화면 단독 실행)에서 같은 녹음기·자리 표지를 쓰도록 함께 꺼낸다(CI 동작 무관).
// ⬛ [낡은 줄 · 3차] module.exports = { plantFlags, TERMS_REQUIRED_LABELS, RECORDER, TERMS_MARK, MAIN_MARK, settleScreen };
// ⬛ [낡은 줄 · 4차] module.exports = { plantFlags, TERMS_REQUIRED_LABELS, RECORDER, TERMS_MARK, MAIN_MARK, settleScreen, OBS_TERMS_ID };
module.exports = { plantFlags, TERMS_REQUIRED_LABELS, RECORDER, TERMS_MARK, MAIN_MARK, settleScreen, OBS_TERMS_ID, closeBriefing, BRIEF_BACKDROP };
