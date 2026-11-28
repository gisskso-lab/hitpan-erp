// K 본문 — 쿨타임(설정 시각 자동 로그아웃) 화면 실측 3 (PM 결정 §288 ④ · 개발명세서 docs/개발/erp/20261128_쿨타임1b_화면실측3_개발명세서.md)
//
// 🔴 이 파일의 규칙(k35-popup.js 와 같은 꼴)
//   ① 기록은 kit35.recK 하나로만 — 키 = judge.js EXPECT_CT_ROWS(쿨타임 기대 키 5 = 시험 4 · N4 1). 밖 = 던짐.
//   ② 판정은 주소 · 팝업(물음 창) 보임 · 서버 응답 코드 · 서버 값으로만. 단언 전 실패(로그인 · 설정 저장 · 시간초과) = 그 키에 DEVICE 거짓.
//   ③ 그림은 kit35.guardedShot 하나로만(찍기 전·뒤 화면 글자 대조 · 그림 픽셀 = 미측정) → 쿨타임 N4 줄(K35-CT-N4) 한 줄.
//   ④ 시각 맞추기 — 러너 시계 = 서버 시계(같은 러너). 설정 시각 C = 지금+45초 뒤 첫 분 경계(첫 바퀴는 5분 눈금이 아니게 1분 더).
//      기다림은 늘 상한(물음 = C − 지금 + 40초 · 서버 끝냄 = 120초 · 고침 눈금 = 330초) · 무한 대기 0.
//   ⑤ 관리자(부모) 세션은 C 전에 저장하고 바로 로그아웃 → C 뒤에 다시 로그인해 끔 — 관리자 세션이 물음 대상이 되지 않게(login_at < C 만 물음).
//   ⑥ 몸 끝에(어느 길이든) 쿨타임을 끄고 · 연 세션을 모두 로그아웃하고 · 브라우저를 닫는다(세션 놓기).
//   ⑦ 이 PC 에서 돌리지 않는다(이 PC DB = 실물) — CI 의 S8 에서만. 이름 = k35-popup.js 뒤에 돈다(알파벳 순 · 앞 몸에 쿨타임 남김 0).
'use strict';

const fs = require('fs');
const L = require('./lib');
const K = require('./kit35');
const J = require('./judge');

const env = process.env;
const ID = { na: env.UI_LOGIN_NA || 'ci-na', ga: env.UI_LOGIN_GA || 'ci-ga', parent: env.UI_LOGIN_PARENT || 'ci-parent' };
const PW = { na: env.UI_PW_NA || '', ga: env.UI_PW_GA || '', parent: env.UI_PW_PARENT || '' };
// ⬛ [낡은 줄 · (가) 이전] const GA_NAME = '시험 가'; — CT3 재료가 다로 옮겨 안 쓰임
const DA_NAME = '시험 다'; // 🆕 (가) 시드② 쿨타임 몸 전용 자식 다(seed-children.js · 다른 몸은 안 씀)
const BROWSER = 'chromium';
const PROMPT_SEL = 'div[role="dialog"][aria-label="로그아웃 설정시간입니다."]'; // CooltimeScreenRules.PromptTitle
const BTN_CONTINUE = '예 — 계속 작업'; // CooltimeScreenRules.ContinueButton
const BTN_LOGOUT = '아니요 — 지금 로그아웃'; // CooltimeScreenRules.LogoutButton
const EDIT_OK = '퇴근 시각을 고쳤습니다.'; // HrAttendancePage 저장 성공 안내
const PROMPT_EXTRA_MS = 40000; // C 지난 뒤 물음 기다림 몫(서버 작업 15초 + 푸시 + 화면)
const PROMPT_CAP_MS = 230000; // 물음 기다림 절대 상한
const END_CAP_MS = 120000; // 무응답 끝냄(65초 + 15초 바퀴) 기다림 상한
const GRID_CAP_MS = 330000; // 고침 시각(5분 눈금) 기다림 상한
const HIDE_MS = 15000; // [계속] 뒤 두 탭 물음 닫힘 · 로그인 화면 이동 기다림
const SETTLE_MS = 2500;
const CT_TOTAL_MS = 14 * 60 * 1000; // 이 몸 전체 상한(워크플로 S8 단계 상한 안)

const key = (id, g, p) => ({ id, g, path: p, browser: BROWSER });
const KEY = {
  tab2: key('K35-CT1', 'CT1', 'tab2-continue'),
  offOut: key('K35-CT2', 'CT2', 'off-logout'),
  offCont: key('K35-CT2', 'CT2', 'off-continue'),
  reedit: key('K35-CT3', 'CT3', 'reedit'),
};
const N4_KEY = key('K35-CT-N4', 'N4', null);
const WHAT = {
  tab2: 'CT1 탭 2: 같은 계정 같은 브라우저 탭 둘 · 물음이 두 탭에 뜸 → 한 탭 [계속] → 다른 탭 물음도 닫힘 · 두 탭 로그인 유지 · 서버 state 200(물음 비움 · 미룸 있음)',
  offOut: 'CT2 물음 중 끔(지금 로그아웃): 물음 떠 있는 동안 관리자가 끔 → [아니요 — 지금 로그아웃] → /login · 옛 토큰 요청 401(서버 세션 끝)',
  offCont: 'CT2 물음 중 끔([계속]): 물음 떠 있는 동안 관리자가 끔 → [예 — 계속 작업] → 서버 409 → 다시 물음 → 물음만 닫힘 · 로그인 유지 · state 200',
  reedit: 'CT3 고친 퇴근 다시 고치기: 자동 로그아웃이 적은 퇴근(자동)을 근태 화면에서 고침 → 다시 고침 → 둘째 값이 화면 · API 같음 · 출처 edited · 고친 기록 2',
};

const pad2 = (n) => String(n).padStart(2, '0');
const hm = (d) => `${pad2(d.getHours())}:${pad2(d.getMinutes())}`;
const ymd = (d) => `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
// 설정 시각 C — 지금+45초 뒤 첫 분 경계. offGrid 면 5분 눈금(분 % 5 = 0)을 피한다(CT3 둘째 값과 겹치지 않게).
function pickC(nowMs, offGrid) {
  const c = new Date(nowMs + 45000);
  if (c.getSeconds() > 0 || c.getMilliseconds() > 0) {
    c.setSeconds(0, 0);
    c.setMinutes(c.getMinutes() + 1);
  }
  if (offGrid && c.getMinutes() % 5 === 0) c.setMinutes(c.getMinutes() + 1);
  return c;
}
// CT3 둘째 값 — (출근, 지금] 안의 5분 눈금 중 가장 늦은 것 · C1 과 다른 분. 없으면 null.
// ⬛ [낡은 머리 · 11/28 자정 넘김 고침 이전] function pickGrid(checkIn, nowMs, avoidHm) {
// 🆕 11/28 장치 고침 F1(지시서 20261128작_쿨타임1b_CT3자정넘김_장치고침) — 런 46080568966 시도 4: 출근 23:53(D) · 자동 퇴근 00:01(D+1) 인데
//   눈금을 「출근 날짜」(D) 에서 골라 23:55 → 고침 창은 날짜 칸 기본값이 「지금 퇴근 날짜」(D+1)라 D+1 23:55 로 보내져 서버가 거절(24시간 밖 · 맞는 동작).
//   → 눈금은 **고침 창 날짜 기본값(= 자동 퇴근 날짜 outDay)** 과 같은 날에서 고른다. outDay 를 안 주면 옛 동작(출근 날짜).
function pickGrid(checkIn, nowMs, avoidHm, outDay) {
  const day = outDay || ymd(checkIn);
  const g = new Date(nowMs);
  g.setSeconds(0, 0);
  g.setMinutes(g.getMinutes() - (g.getMinutes() % 5));
  for (let i = 0; i < 3; i += 1) {
    // ⬛ [낡은 줄 · 11/28 이전] if (g.getTime() > checkIn.getTime() && hm(g) !== avoidHm && ymd(g) === ymd(checkIn)) return new Date(g);
    if (g.getTime() > checkIn.getTime() && hm(g) !== avoidHm && ymd(g) === day) return new Date(g);
    g.setMinutes(g.getMinutes() - 5);
  }
  return null;
}
function readJ(j, a, b) {
  if (!j || typeof j !== 'object') return undefined;
  return j[a] !== undefined ? j[a] : j[b];
}
// 🆕 11/28 CT3 진단 — 출입증 가운데 토막에서 클레임 하나(값은 찍지 않는다 · 주소에만 쓴다).
function jwtClaim(token, name) {
  try {
    const mid = String(token || '').split('.')[1] || '';
    const json = Buffer.from(mid.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8');
    const v = JSON.parse(json)[name];
    return typeof v === 'string' && v ? v : null;
  } catch (e) {
    console.log(`K 쿨타임 출입증 클레임 읽기 실패 (${L.errorName(e)})`);
    return null;
  }
}
// 서버가 주는 로컬 시각 글자(시간대 표시 없음)를 그대로 HH:mm 로(시간대 바꿈 0)
function hmOfText(s) {
  const m = /T?(\d{2}):(\d{2})/.exec(String(s ?? ''));
  return m ? `${m[1]}:${m[2]}` : null;
}
// 🆕 11/28 F1 — 같은 글자에서 날짜(yyyy-MM-dd)만(시간대 바꿈 0). 없으면 null.
function ymdOfText(s) {
  const m = /^(\d{4}-\d{2}-\d{2})/.exec(String(s ?? ''));
  return m ? m[1] : null;
}
// 🆕 첫 CI 런 고침 — 거절 응답 본문의 안내 한 칸만(GlobalExceptionMiddleware `{ error }` · 그 밖 message/code) · 비번 · 토큰 가림(sanitize).
function bodyWhy(res) {
  const j = res && res.json && typeof res.json === 'object' ? res.json : null;
  const t = j ? (j.error || j.Error || j.message || j.Message || j.code || j.Code || '') : '';
  return t ? `본문 ${L.sanitize(String(t), 120)}` : '본문 안내 없음';
}
const whyOf = (e) => (e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${L.errorName(e)}`);
// 🆕 첫 CI 런 고침 — CT3 재료가 없어진 **첫 원인** 한 줄만 남긴다(뒤 실패가 앞 원인을 덮지 않게 · 사슬은 그대로).
function cause(st, text) {
  if (!st.ct3Pre) st.ct3Pre = text;
}

// ── 관리자(부모) — 설정 화면이 쓰는 같은 API(GET → 세 칸만 바꿔 PUT) · 저장 직후 로그아웃 ──
async function adminSave(st, patch, label) {
  const s = await K.apiLogin('PARENT', ID.parent, PW.parent);
  try {
    const g = await K.apiCall('GET', '/api/settings', { sess: s });
    if (g.status !== 200 || !g.json) throw new K.KitStepError(`${label} 설정 읽기 status=${g.status}`);
    const body = { ...g.json, ...patch };
    const p = await K.apiCall('PUT', '/api/settings', { sess: s, body });
    if (p.status !== 200) throw new K.KitStepError(`${label} 설정 저장 status=${p.status}`);
    st.saved.push(`${label} ${patch.cooltimeEnabled === false ? '끔' : `켬 ${patch.cooltimeAt || '-'}`}`);
  } finally {
    const lo = await K.apiLogout(s);
    if (!lo.ok) st.logoutFail += 1;
  }
}
async function adminCall(fn) {
  const s = await K.apiLogin('PARENT', ID.parent, PW.parent);
  try {
    return await fn(s);
  } finally {
    await K.apiLogout(s);
  }
}

// ── 브라우저 한 벌(컨텍스트) — 토큰 가림 · 외부 끊음 · 막기/녹음기 초기 스크립트(앞 몸과 같은 꼴) ──
async function openCtx(st, who) {
  const tally = { continue: 0, abort: 0 };
  const context = await st.br.newContext({ baseURL: K.BASE });
  await K.blockExternal(context, st.r, tally);
  const tok = { access: null, deviceId: null, masked: 0 };
  K.watchTokens(context, tok);
  await context.addInitScript(K.INIT_SCRIPT);
  const kb = {
    browser: BROWSER, who, flags: new Set(), plantN4: false, context, page: null, tok, tally,
    br: { seen: 0, closed: 0, errs: 0, lastErr: null }, n4: st.n4, hub: { ws: 0 }, consoleLines: 0, consoleBuf: [],
    sealed: false, home: null,
  };
  K.watchConsole(context, kb);
  st.kbs.push(kb);
  return kb;
}
async function login(kb, loginId, password) {
  kb.page = await kb.context.newPage();
  K.watchHub(kb.page, kb.hub);
  const s = await K.screenLogin(kb.page, loginId, password);
  // ⬛ [낡은 줄 · 첫 CI 런] if (s.where !== 'main') throw new K.KitStepError(`${kb.who} 로그인 뒤 자리=${s.where}`);
  //   → 「자리=null」 만 남아 409(같은 계정 PC 1대 · Login.razor 「다른 PC에서 사용 중입니다.」) · 약관 · 그 밖을 가르지 못했다.
  //   🆕 주소 경로 + 화면 안내 글자(제품 문구 · 첫 80자 · 가림) 를 같이 남긴다.
  if (s.where !== 'main') {
    const alert = await kb.page.locator('.mud-alert').first().innerText({ timeout: 2000 }).catch(() => '');
    const msg = alert ? L.sanitize(alert.replace(/\s+/g, ' ').trim(), 80) : '안내 없음';
    throw new K.KitStepError(`${kb.who} 로그인 뒤 자리=${s.where} · 경로 ${s.path || '-'} · 안내 「${msg}」`);
  }
  await K.waitMasked(kb.tok);
  K.flushConsole(kb);
  await K.briefOff(kb.page, kb, 3000);
  kb.home = K.pathOf(kb.page.url());
}
async function secondTab(kb) {
  const p = await kb.context.newPage();
  const hub = { ws: 0 };
  K.watchHub(p, hub);
  await p.goto(`${K.BASE}${kb.home || '/'}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  await K.briefOff(p, kb, 3000);
  if (!(await K.waitHub(hub))) throw new K.KitStepError(`${kb.who} 둘째 탭 알림 배관 연결 0`);
  return p;
}
const sessOf = (kb) => ({ who: kb.who, token: kb.tok.access, deviceId: kb.tok.deviceId });
async function stateOf(kb) {
  if (!kb.tok.access) return { status: null, json: null };
  return K.apiCall('GET', '/api/auth/cooltime/state', { sess: sessOf(kb) });
}
async function seeAll(pages, untilMs) {
  const seen = pages.map(() => false);
  while (Date.now() < untilMs && seen.some((x) => !x)) {
    for (let i = 0; i < pages.length; i += 1) {
      if (seen[i]) continue;
      seen[i] = await pages[i].locator(PROMPT_SEL).first().isVisible().catch(() => false);
    }
    if (seen.some((x) => !x)) await K.sleep(500);
  }
  return seen;
}
async function hidden(page, ms) {
  return page.locator(PROMPT_SEL).first().waitFor({ state: 'hidden', timeout: ms }).then(() => true, () => false);
}
async function clickIn(page, name) {
  await page.locator(PROMPT_SEL).first().getByRole('button', { name, exact: true }).click({ timeout: 10000 });
}
async function shot(kb, label) {
  try {
    await K.guardedShot(kb, label);
  } catch (e) {
    console.log(`K 쿨타임 그림 단계 예외 ${label} (${L.errorName(e)}) — 안 찍힌 것으로 본다`);
    st0.n4.errs += 1;
  }
}
function waitUntil(cMs) {
  return Math.min(Date.now() + PROMPT_CAP_MS, cMs + PROMPT_EXTRA_MS);
}
async function closeKb(st, kb, { logout = true } = {}) {
  if (kb.closed) return;
  kb.closed = true;
  K.flushConsole(kb);
  if (logout && kb.tok.access) {
    const lo = await K.apiLogout(sessOf(kb));
    console.log(`K 쿨타임 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  }
  await kb.context.close().catch((e) => console.log(`K 쿨타임 컨텍스트 닫기 실패 ${L.errorName(e)}`));
}

// ── 줄 쓰기(경우마다 한 번 · 이미 쓴 키는 건너뜀) ──
function rec(st, name, res) {
  K.recK(st.r, { key: KEY[name], what: WHAT[name], got: res.got, pass: res.pass, kind: 'TEST' });
}
function dev(st, name, step, e) {
  const why = e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${L.errorName(e)}`;
  console.log(`K 쿨타임 ${name} 단언 전 실패 — 단계=${step.now} · ${why}`);
  if (!K.isWritten(st.r, KEY[name])) {
    K.recK(st.r, { key: KEY[name], what: WHAT[name], got: `단언 전 실패 — 단계=${step.now} · ${why}`, pass: false, kind: 'DEVICE', err: e instanceof K.KitStepError ? null : e });
  }
}

// ── 바퀴 A: CT1(탭 2 · [계속]) + CT3 재료(🆕 (가) 다 = 시드 세션 무응답 → 자동 퇴근 · ⬛ 옛 = 가) ──
// ⬛ [낡은 몸 · 첫 CI 런 고침 1차 bca4567f] prepGa — 가 API 로그인 + 오늘 근태 행 읽기(없으면 손 출근). 가의 오늘 행은 시드 · 앞 몸의
//   로그아웃 자동 퇴근(AuthController.Logout → CheckOutAsync · 출처 NULL)이 이미 닫고 하루 1행이라 다시 못 연다 → CT3 재료가 설 수 없었다.
//   출처 NULL 행은 제품이 고치기를 막는다(HrService.EditableCheckOutSources · 화면 칩 없음) → PM 결정 (가)(CTO 전결 11/28).
// 🆕 (가) — 쿨타임 몸 전용 자식 다(seed-children.js). 시드가 다를 로그인한 채 두어(로그아웃 0) 오늘 출근 행이 열린 채 넘어온다.
//   이 몸은 다의 비번 · 세션을 쓰지 않는다 — 시드 세션(login_at < C1)이 물음 무응답 대상이고, 관리자 API 로 행만 읽는다.
//   [원인 기록] 로그인 그 자체가 출근(AuthController 로그인 → CheckInAsync 「자동출근」) · 같은 날 손 출근 = 400 「오늘 이미 출근 처리되었습니다.」
//     (HrService.CheckInAsync → GlobalExceptionMiddleware) — 첫 CI 런 CT1 「가 출근 status=400」 의 원인.
async function prepDa(st) {
  await adminCall(async (s) => {
    const emps = await K.apiCall('GET', '/api/chat/employees', { sess: s });
    const e = Array.isArray(emps.json) ? emps.json.find((x) => (x.empName || x.EmpName) === DA_NAME) : null;
    if (!e) throw new K.KitStepError(`chat/employees 에 「${DA_NAME}」 없음 status=${emps.status}(시드② 다 만들기 확인)`);
    st.daEmpId = e.employeeId || e.EmployeeId;
    const now = new Date();
    const from = ymd(new Date(now.getTime() - 24 * 3600 * 1000)); // 자정 넘김이면 시드 출근 행이 어제 날짜
    const q = `/api/hr/attendance?from=${from}&to=${ymd(now)}&employeeId=${encodeURIComponent(st.daEmpId)}`;
    const a = await K.apiCall('GET', q, { sess: s });
    if (a.status !== 200 || !Array.isArray(a.json)) throw new K.KitStepError(`다 근태 읽기 status=${a.status} · ${bodyWhy(a)}`);
    const open = a.json.find((x) => readJ(x, 'checkIn', 'CheckIn') && !readJ(x, 'checkOut', 'CheckOut'));
    if (!open) {
      const r0 = a.json[0];
      if (!r0) throw new K.KitStepError(`다 근태 행 없음(시드 다 로그인 자동 출근이 안 됨 · 사원 연결 확인)`);
      throw new K.KitStepError(`다 근태 행이 이미 퇴근 처리됨(출근 ${hmOfText(readJ(r0, 'checkIn', 'CheckIn')) || '-'} · 퇴근 ${hmOfText(readJ(r0, 'checkOut', 'CheckOut')) || '-'} · 출처 ${L.sanitize(String(readJ(r0, 'checkOutSource', 'CheckOutSource') ?? '-'), 30)}) — 다를 누가 로그아웃함(다른 몸은 다를 쓰면 안 됨)`);
    }
    const inText = readJ(open, 'checkIn', 'CheckIn');
    const inAt = new Date(String(inText)); // 시간대 표시 없는 로컬 글자 = 러너 로컬(서버와 같은 러너)
    if (Number.isNaN(inAt.getTime())) throw new K.KitStepError(`다 출근 시각 해석 못 함(${L.sanitize(String(inText), 30)})`);
    st.checkIn = inAt;
    st.daAttId = readJ(open, 'attendanceId', 'AttendanceId');
    console.log(`K 쿨타임 다 준비 — 열린 출근 행 ${hmOfText(inText)}(시드 로그인 자동 출근)`);
  });
}

async function cycleA(st) {
  const step = { now: '나 로그인(탭 1)' };
  let na = null;
  try {
    na = await openCtx(st, '나A');
    await login(na, ID.na, PW.na);
    if (!(await K.waitHub(na.hub))) throw new K.KitStepError('나 탭 1 알림 배관 연결 0');
    step.now = '나 탭 2 열기';
    const tab2 = await secondTab(na);
    // ⬛ [낡은 줄 · 첫 CI 런] step.now = '가 API 로그인 · 근태 권한 · 출근'; (가 준비 실패가 CT1 을 장치 실패로 세웠다)
    // ⬛ [낡은 줄 · 1차 고침 bca4567f] step.now = '가 준비(API 로그인 · 근태 권한 · 열린 출근 행)'; await prepGa(st);
    step.now = '다 준비(시드 세션 · 열린 출근 행 읽기)';
    try {
      await prepDa(st);
    } catch (e) {
      const w = whyOf(e);
      console.log(`K 쿨타임 다 준비 실패(CT3 재료 · CT1 은 이어 감) — ${w}`);
      cause(st, `다 준비 실패 — ${w}`);
    }

    step.now = '관리자 저장(켬 · C1)';
    const c1 = pickC(Date.now(), true);
    st.c1 = hm(c1);
    // 정시 퇴근 = C1 — 가의 무응답 끝냄이 C1 을 「자동」 퇴근으로 적는다(출근 < C1 ≤ 물은 순간)
    await adminSave(st, { cooltimeEnabled: true, cooltimeAt: st.c1, regularLeaveTime: st.c1 }, 'A');
    console.log(`K 쿨타임 바퀴 A — C1=${st.c1} · 물음 기다림 상한 ${Math.round((waitUntil(c1.getTime()) - Date.now()) / 1000)}초`);

    step.now = '물음 기다림(두 탭)';
    const t0 = Date.now();
    const [seenA, seenB] = await seeAll([na.page, tab2], waitUntil(c1.getTime()));
    const waited = Math.round((Date.now() - t0) / 1000);
    if (!seenA && !seenB) {
      await shot(na, 'CT1-no-prompt');
      throw new K.KitStepError(`물음이 두 탭 모두 상한 안에 안 뜸(기다림 ${waited}초 · C1 ${st.c1})`);
    }
    step.now = '탭 1 [계속]';
    if (seenA) await clickIn(na.page, BTN_CONTINUE);
    const aHidden = seenA ? await hidden(na.page, HIDE_MS) : false;
    const bHidden = await hidden(tab2, HIDE_MS);
    await K.sleep(SETTLE_MS);
    const urlA = K.pathOf(na.page.url());
    const urlB = K.pathOf(tab2.url());
    const s1 = await stateOf(na);
    const asked = readJ(s1.json, 'asked', 'Asked');
    const snooze = readJ(s1.json, 'snoozeUntilUtc', 'SnoozeUntilUtc');
    const atLogin = (u) => /^\/login(\?|$)/.test(u);
    const pass = seenA && seenB && aHidden && bHidden && !atLogin(urlA) && !atLogin(urlB) && s1.status === 200 && asked === false && !!snooze;
    rec(st, 'tab2', { pass, got: { promptTab1: seenA, promptTab2: seenB, waitedSec: waited, tab1Closed: aHidden, tab2Closed: bHidden, tab1: urlA, tab2: urlB, state: s1.status, asked: asked ?? null, snoozeSet: !!snooze } });
    await shot(na, 'CT1-tab2');
    await closeKb(st, na);
  } catch (e) {
    dev(st, 'tab2', step, e);
    if (!st.c1) cause(st, `C1 저장 전 실패 — 바퀴 A 단계=${step.now} · ${whyOf(e)}`);
  } finally {
    // 🆕 첫 CI 런 고침 — 실패 길에서도 나A 세션을 놓는다(안 놓으면 바퀴 B 의 나 화면 로그인이 같은 계정 PC 1대 409 에 걸린다).
    if (na) await closeKb(st, na);
  }

  // ⬛ [낡은 몸 · 1차 고침 bca4567f] 가(API · 무응답) 끝남 기다림 — 가 세션의 cooltime/state 가 401 · active:false 가 될 때까지(최대 120초).
  // 🆕 (가) 다(시드 세션 · 무응답) 끝남 기다림 → 열린 출근 행에 자동 퇴근(출처 cooltime) = CT3 재료. 관리자 API 로 그 행만 본다(한 세션 · C1 뒤 로그인 = 물음 대상 아님).
  //   실패는 CT3 단언 전 실패의 첫 원인으로 넘긴다. 이미 첫 원인이 있으면 기다리지 않는다.
  if (st.ct3Pre) return;
  try {
    if (!st.daEmpId || !st.daAttId) throw new K.KitStepError('다 준비 전 실패');
    if (!st.c1) throw new K.KitStepError('C1 저장 전 실패');
    const from = ymd(st.checkIn);
    const q = `/api/hr/attendance?from=${from}&to=${ymd(new Date())}&employeeId=${encodeURIComponent(st.daEmpId)}`;
    const last = await adminCall(async (s) => {
      const until = Date.now() + END_CAP_MS;
      let row = null;
      while (Date.now() < until) {
        const a = await K.apiCall('GET', q, { sess: s });
        row = Array.isArray(a.json) ? a.json.find((x) => readJ(x, 'attendanceId', 'AttendanceId') === st.daAttId) || null : null;
        if (row && readJ(row, 'checkOut', 'CheckOut')) break;
        await K.sleep(5000);
      }
      return row;
    });
    const src = readJ(last, 'checkOutSource', 'CheckOutSource');
    if (!last || !readJ(last, 'checkOut', 'CheckOut')) throw new K.KitStepError(`다 무응답 끝냄(자동 퇴근)이 상한 ${END_CAP_MS / 1000}초 안에 안 옴(출처 ${L.sanitize(String(src ?? '-'), 30)} · 시드 다 세션이 살아 있었나 확인)`);
    if (src !== 'cooltime') throw new K.KitStepError(`다 퇴근 출처가 cooltime 아님(${L.sanitize(String(src ?? '-'), 30)})`);
    st.daEnded = true;
    console.log(`K 쿨타임 다 자동 퇴근 ${hmOfText(readJ(last, 'checkOut', 'CheckOut'))}(출처 cooltime · C1 ${st.c1})`);
  } catch (e) {
    cause(st, `다 무응답 끝냄 — ${whyOf(e)}`);
  }
}

// ── 바퀴 B: CT2(물음 중 끔 · 가 = 지금 로그아웃 · 나 = [계속] 409) ──
async function cycleB(st) {
  const step = { now: '가 · 나 화면 로그인' };
  let ga = null;
  let na = null;
  // 🆕 첫 CI 런 고침(런 46064948848 · CT2 「가B 로그인 뒤 자리=null」) — 바퀴 A 의 가 API 세션이 살아 있으면 놓고 들어간다.
  //   [원인 · 코드] 같은 계정 PC 동시 로그인 차단(AuthService 축 B · 먼저 들어온 PC 가 이긴다 → ConcurrentPcLoginException → 409 ·
  //     Login.razor 「다른 PC에서 사용 중입니다.」 · /login 에 머묾 → settleScreen 표지 없음 = 자리 null).
  //     가 API 로그인은 deviceType 'pc' · UA 있음 = PC 세션이다. 바퀴 A 가 C1 전에 넘어지면 서버 끝냄이 오지 않아 그 세션이 남았다.
  //   스펙 뜻(물음 떠 있는 동안 관리자가 끔)은 그대로 — 바퀴 B 의 가 · 나는 새 화면 로그인(login_at < C2)이다.
  if (st.ga) {
    const lo = await K.apiLogout(st.ga);
    console.log(`K 쿨타임 바퀴 B 앞 — 가 API 세션 놓음(같은 계정 PC 1대 · 화면 로그인 409 막음) ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
    st.ga = null;
  }
  try {
    ga = await openCtx(st, '가B');
    await login(ga, ID.ga, PW.ga);
    if (!(await K.waitHub(ga.hub))) throw new K.KitStepError('가 알림 배관 연결 0');
    na = await openCtx(st, '나B');
    await login(na, ID.na, PW.na);
    if (!(await K.waitHub(na.hub))) throw new K.KitStepError('나 알림 배관 연결 0');
    step.now = '관리자 저장(시각 바꿈 · C2)';
    const c2 = pickC(Date.now(), false);
    st.c2 = hm(c2);
    await adminSave(st, { cooltimeEnabled: true, cooltimeAt: st.c2, regularLeaveTime: st.c1 || st.c2 }, 'B');
    console.log(`K 쿨타임 바퀴 B — C2=${st.c2} · 물음 기다림 상한 ${Math.round((waitUntil(c2.getTime()) - Date.now()) / 1000)}초`);
    step.now = '물음 기다림(가 · 나)';
    const t0 = Date.now();
    const [seenGa, seenNa] = await seeAll([ga.page, na.page], waitUntil(c2.getTime()));
    const waited = Math.round((Date.now() - t0) / 1000);
    if (!seenGa && !seenNa) {
      await shot(ga, 'CT2-no-prompt');
      throw new K.KitStepError(`물음이 가 · 나 모두 상한 안에 안 뜸(기다림 ${waited}초 · C2 ${st.c2})`);
    }
    step.now = '관리자 끔(C2 뒤 새 로그인)';
    await adminSave(st, { cooltimeEnabled: false, cooltimeAt: st.c2, regularLeaveTime: st.c1 || st.c2 }, 'B-끔');
    st.disabled = true;

    // 가 — [아니요 — 지금 로그아웃] → /login · 옛 토큰 401
    step.now = '가 [아니요 — 지금 로그아웃]';
    try {
      if (!seenGa) throw new K.KitStepError(`가 물음 안 뜸(기다림 ${waited}초)`);
      const oldTok = sessOf(ga);
      await clickIn(ga.page, BTN_LOGOUT);
      const toLogin = await ga.page.waitForURL((u) => u.pathname === '/login', { timeout: 20000, waitUntil: 'commit' }).then(() => true, () => false);
      // 서버 세션 끝 = 옛 토큰 401(세션 확인 미들웨어) 또는 state 200 active:false(세션 행 없음) — 어느 쪽인지 got 에 남긴다
      let code = null;
      let active = null;
      const until = Date.now() + 20000;
      while (Date.now() < until) {
        const s = await K.apiCall('GET', '/api/auth/cooltime/state', { sess: oldTok });
        code = s.status;
        active = readJ(s.json, 'active', 'Active') ?? null;
        if (code === 401 || active === false) break;
        await K.sleep(1000);
      }
      const main = K.pathOf(ga.page.url());
      rec(st, 'offOut', { pass: toLogin && (code === 401 || active === false), got: { prompt: seenGa, waitedSec: waited, toLogin, main, oldTokenState: code, oldTokenActive: active } });
      // 🆕 11/28 CT2 그림 장치 고침 — 런 46068258519 · 46074001780 둘 다 「그림 안 찍음 CT2-off-logout · 예외 1(Error) · 프레임 0」
      //   → 쿨타임 N4 「대조 못 함」. 지금 로그아웃 = NavigateTo("/login", forceLoad: true)(CooltimePrompt.razor) = 전체 다시 읽기라
      //   주소가 /login 으로 바뀐(commit) 직후엔 문서가 아직 뜨는 중 → 화면 글자 대조(evaluate)가 넘어짐. 문서가 다 뜨고
      //   로그인 칸이 보인 뒤에 찍는다(판정 · 대조 규칙 그대로 · 기다림만 더함).
      await ga.page.waitForLoadState('load', { timeout: 20000 }).catch((e) => console.log(`K 쿨타임 CT2 로그인 화면 문서 기다림 끝 (${L.errorName(e)})`));
      await ga.page.locator('input[type="password"]').first().waitFor({ state: 'visible', timeout: 20000 })
        .catch((e) => console.log(`K 쿨타임 CT2 로그인 칸 기다림 끝 (${L.errorName(e)})`));
      await K.sleep(SETTLE_MS);
      await shot(ga, 'CT2-off-logout');
      ga.tok.access = null; // 손 로그아웃으로 이미 끝남 — 다시 부르지 않는다
    } catch (e) {
      dev(st, 'offOut', step, e);
    }

    // 나 — [예 — 계속 작업] → 409 → 다시 물음 → 물음만 닫힘 · 로그인 유지
    step.now = '나 [예 — 계속 작업]';
    try {
      if (!seenNa) throw new K.KitStepError(`나 물음 안 뜸(기다림 ${waited}초)`);
      if (!(await na.page.locator(PROMPT_SEL).first().isVisible().catch(() => false))) {
        throw new K.KitStepError('나 물음이 [계속] 전에 사라짐(60초 넘김 의심)');
      }
      const respP = na.page.waitForResponse((r) => /\/api\/auth\/cooltime\/continue(\?|$)/.test(r.url()), { timeout: 15000 }).catch(() => null);
      await clickIn(na.page, BTN_CONTINUE);
      const resp = await respP;
      const closed = await hidden(na.page, HIDE_MS);
      await K.sleep(SETTLE_MS);
      const main = K.pathOf(na.page.url());
      const s = await stateOf(na);
      const contStatus = resp ? resp.status() : null;
      const pass = contStatus === 409 && closed && !/^\/login(\?|$)/.test(main) && s.status === 200 && readJ(s.json, 'asked', 'Asked') === false;
      rec(st, 'offCont', { pass, got: { prompt: seenNa, continueStatus: contStatus, closed, main, state: s.status, asked: readJ(s.json, 'asked', 'Asked') ?? null } });
      await shot(na, 'CT2-off-continue');
    } catch (e) {
      dev(st, 'offCont', step, e);
    }
  } catch (e) {
    dev(st, 'offOut', step, e);
    dev(st, 'offCont', step, e);
  } finally {
    if (ga) await closeKb(st, ga);
    if (na) await closeKb(st, na);
  }
}

// ── CT3: 근태 화면에서 자동 퇴근 고침 → 다시 고침 ──
// ⬛ [낡은 머리 · 11/28 이전] async function pickTimeInDialog(page, target) {
async function pickTimeInDialog(page, target, step = { now: '' }) {
  const base = step.now;
  step.now = `${base} > 시각 칸 열기`;
  const input = page.getByLabel('퇴근 시각', { exact: true });
  await input.click({ timeout: 10000 });
  const nums = page.locator('.mud-popover-open .mud-clock-number');
  await nums.first().waitFor({ state: 'visible', timeout: 10000 });
  const clickNum = async (v) => {
    const n = await nums.count();
    for (let i = 0; i < n; i += 1) {
      const el = nums.nth(i);
      const t = (await el.textContent().catch(() => '')) || '';
      if (/^\s*\d{1,2}\s*$/.test(t) && parseInt(t, 10) === v && (await el.isVisible().catch(() => false))) {
        // ⬛ [낡은 줄 · 11/28 이전] await el.click({ timeout: 5000 });
        // 🆕 11/28 런 46076686554 「다시 고침 > 값 넣고 저장 · TimeoutError」 — MudBlazor 9.3 시계는 숫자 위에 바늘 누름 자리
        //   (`.mud-picker-stick*` :after 48px 원)가 덮여 있어 Playwright 의 「가려짐」 검사가 5초를 다 씀(가설 · 같은 런에서 하위 단계로 잼).
        //   사람 손과 같게 숫자 한가운데를 누른다(force = 가려짐 검사만 건너뜀 · 맨 위 바늘 자리가 받음 = 실제 사용자와 같은 길).
        await el.click({ timeout: 5000, force: true });
        return true;
      }
    }
    return false;
  };
  step.now = `${base} > 시 숫자`;
  if (!(await clickNum(target.getHours()))) throw new K.KitStepError(`시각 고르기 — 시 숫자 ${target.getHours()} 못 찾음`);
  await K.sleep(600);
  step.now = `${base} > 분 숫자`;
  if (!(await clickNum(target.getMinutes()))) throw new K.KitStepError(`시각 고르기 — 분 숫자 ${target.getMinutes()} 못 찾음`);
  await K.sleep(600);
  step.now = `${base} > 시각 확정`;
  if (await page.locator('.mud-popover-open .mud-clock-number').first().isVisible().catch(() => false)) {
    await page.mouse.click(4, 4); // 고르기 창 바깥(덮개) 한 번 — 값 확정 · 닫힘
    await K.sleep(600);
  }
  const v = await input.inputValue().catch(() => '');
  if (hmOfText(v) !== hm(target)) throw new K.KitStepError(`시각 고르기 — 칸 값 ${L.sanitize(v, 20)} ≠ ${hm(target)}`);
}
// 🆕 11/28 CT3 장치 고침(인계51 §3-2) — 첫 CI 런 46068258519 「첫 고침 · TimeoutError」 는 어느 기다림이 넘어졌는지 안 남았다.
//   하위 단계를 step 에 적고, 고치기 칸 대신 「권한을 요청」 안내가 뜨면 기다림 상한을 다 쓰지 않고 그 사실을 바로 남긴다
//   (개발명세서 「못 한 것 7 — 부모의 HR_PROXY 수정 권한 미측정」 가설을 이 런에서 잰다 · 제품 0줄).
class EditFieldsHidden extends Error {}
const EDIT_DENY_TEXT = '권한을 요청'; // HrAttendancePage 「고치려면 관리자에게 [근태 대리입력] 권한을 요청해 주세요.」
// ⬛ [낡은 머리 · 11/28 CT3 장치 고침 이전] async function editOnce(page, rowSel, chip, reason, target) {
async function editOnce(page, rowSel, chip, reason, target, step = { now: '' }) {
  const base = step.now;
  step.now = `${base} > 표시 누름`;
  await page.locator(rowSel).first().locator('.mud-chip', { hasText: chip }).first().click({ timeout: 15000 });
  step.now = `${base} > 작은 창 뜸`;
  const dlg = page.locator('.mud-dialog', { hasText: '퇴근 기록' }).first();
  await dlg.waitFor({ state: 'visible', timeout: 15000 });
  step.now = `${base} > 고치기 칸`;
  // ⬛ [낡은 줄 · 11/28 이전] await dlg.getByLabel('고친 까닭 (꼭 적어 주세요)', { exact: true }).waitFor({ state: 'visible', timeout: 15000 });
  const reasonBox = dlg.getByLabel('고친 까닭 (꼭 적어 주세요)', { exact: true });
  const deny = dlg.locator('.mud-alert', { hasText: EDIT_DENY_TEXT });
  await reasonBox.or(deny).first().waitFor({ state: 'visible', timeout: 15000 });
  if (await deny.isVisible().catch(() => false)) {
    throw new EditFieldsHidden('고치기 칸 대신 「권한을 요청」 안내가 떴다(화면 권한 판정이 고치기 칸을 숨김)');
  }
  step.now = `${base} > 값 넣고 저장`;
  // ⬛ [낡은 줄 · 11/28 이전] if (target) await pickTimeInDialog(page, target);
  if (target) { step.now = `${base} > 시각 고르기`; await pickTimeInDialog(page, target, step); }
  step.now = `${base} > 까닭 넣기`;
  await dlg.getByLabel('고친 까닭 (꼭 적어 주세요)', { exact: true }).fill(reason);
  // 🆕 11/28 장치 고침 F2 — 런 46080568966 시도 4: 둘째 저장이 서버에서 거절됐는데(창 그대로 · 오류 안내) 장치가 **첫 고침 때 뜬**
  //   「퇴근 시각을 고쳤습니다.」 안내에 걸려 지나쳤다. 제품은 성공일 때만 창을 닫는다(SaveCheckOutAsync `_editOpen = false`).
  //   → 성공 = 창 닫힘 + 성공 안내 보임 · 실패 = 누른 뒤 새로 뜬 다른 안내(서버 까닭) → 그 글을 담아 바로 멈춤 · 상한 20초.
  const snackTexts = async () => (await page.locator('.mud-snackbar').allInnerTexts().catch(() => [])).map((s) => s.trim()).filter(Boolean);
  const before = new Set(await snackTexts());
  step.now = `${base} > 저장 누름`;
  await dlg.getByRole('button', { name: '저장', exact: true }).click({ timeout: 10000 });
  step.now = `${base} > 저장 안내`;
  // ⬛ [낡은 줄 · 11/28 F2 이전] await page.locator('.mud-snackbar', { hasText: EDIT_OK }).first().waitFor({ state: 'visible', timeout: 20000 });
  // ⬛ [낡은 줄 · 11/28 F2 이전] await dlg.waitFor({ state: 'hidden', timeout: 15000 }).catch((e) => console.log(`K 쿨타임 고침 창 닫힘 기다림 끝 (${L.errorName(e)})`));
  const saveUntil = Date.now() + 20000;
  let saved = false;
  while (Date.now() < saveUntil) {
    const now = await snackTexts();
    const open = await dlg.isVisible().catch(() => true);
    if (!open && now.some((t) => t.includes(EDIT_OK))) { saved = true; break; }
    // 거절 판정은 창이 그대로일 때만(성공 뒤 목록 다시 읽기 등 딴 안내를 거절로 읽지 않게)
    const fresh = now.filter((t) => !before.has(t) && !t.includes(EDIT_OK));
    if (open && fresh.length > 0) throw new K.KitStepError(`저장 거절 — 안내 「${L.sanitize(fresh[0], 80)}」(창 그대로)`);
    await K.sleep(300);
  }
  if (!saved) throw new K.KitStepError(`저장 결과 못 봄 — 20초 안에 창 닫힘 + 성공 안내 없음(창 ${(await dlg.isVisible().catch(() => false)) ? '그대로' : '닫힘'})`);
  await K.sleep(1500);
}
async function ct3(st) {
  const step = { now: '재료 확인' };
  let adm = null;
  try {
    if (st.ct3Pre) throw new K.KitStepError(`재료 없음 — ${st.ct3Pre}`);
    // ⬛ [낡은 줄 · 1차 고침 이전] 재료 = 가(gaEmpId · GA_NAME) — (가) 로 다(daEmpId · DA_NAME)
    if (!st.daEmpId || !st.checkIn) throw new K.KitStepError('재료 없음 — 다 준비 전 실패');
    const day = ymd(st.checkIn);
    const q = `/api/hr/attendance?from=${day}&to=${day}&employeeId=${encodeURIComponent(st.daEmpId)}`;
    const row0 = await adminCall(async (s) => {
      const a = await K.apiCall('GET', q, { sess: s });
      return Array.isArray(a.json) ? (a.json.find((x) => readJ(x, 'attendanceId', 'AttendanceId') === st.daAttId) || a.json[0] || null) : null;
    });
    const src0 = readJ(row0, 'checkOutSource', 'CheckOutSource');
    if (!row0 || src0 !== 'cooltime') throw new K.KitStepError(`자동 퇴근 행 없음(출처 ${L.sanitize(String(src0 ?? '-'), 30)})`);
    const attId = readJ(row0, 'attendanceId', 'AttendanceId');
    const out0 = hmOfText(readJ(row0, 'checkOut', 'CheckOut'));
    // 🆕 11/28 F1 — 고침 창 「퇴근 날짜」 기본값 = 지금 퇴근의 날짜(HrAttendancePage.OpenCheckOutDialogAsync). 장치는 시각만 고르므로 눈금도 그 날에서.
    const outDay = ymdOfText(readJ(row0, 'checkOut', 'CheckOut'));
    if (!outDay) throw new K.KitStepError('자동 퇴근 날짜 못 읽음(퇴근 글자 꼴 다름)');

    step.now = '둘째 값(5분 눈금) 기다림';
    const gridUntil = Date.now() + GRID_CAP_MS;
    // ⬛ [낡은 줄 · 11/28 이전] let t2 = pickGrid(st.checkIn, Date.now(), out0);
    let t2 = pickGrid(st.checkIn, Date.now(), out0, outDay);
    while (!t2 && Date.now() < gridUntil) {
      await K.sleep(5000);
      // ⬛ [낡은 줄 · 11/28 이전] t2 = pickGrid(st.checkIn, Date.now(), out0);
      t2 = pickGrid(st.checkIn, Date.now(), out0, outDay);
    }
    // ⬛ [낡은 줄 · 11/28 이전] if (!t2) throw new K.KitStepError(`둘째 값 눈금 없음(출근 ${hm(st.checkIn)} · 자정 넘김이면 미측정)`);
    if (!t2) throw new K.KitStepError(`둘째 값 눈금 없음(출근 ${hm(st.checkIn)} · 퇴근 ${outDay} ${out0})`);
    if (ymd(st.checkIn) !== outDay) console.log(`K 쿨타임 CT3 자정 넘김 꼴 — 출근 ${ymd(st.checkIn)} ${hm(st.checkIn)} · 퇴근 ${outDay} ${out0} · 둘째 값 ${ymd(t2)} ${hm(t2)}`);

    // 🆕 11/28 진단 — 부모 화면의 고치기 칸은 /api/permissions/{user_id} 의 HR_PROXY 「수정」 으로만 그려진다(서버는 tenant_admin 전권).
    //   그 응답을 판정 밖 한 줄로 남긴다(사용자 키 · 토큰은 안 찍음).
    step.now = '부모 화면 권한 읽기(진단)';
    const permDiag = await adminCall(async (s) => {
      const uid = jwtClaim(s.token, 'user_id');
      if (!uid) return { status: null, hrProxyUpdate: null, note: '토큰에 user_id 없음' };
      const a = await K.apiCall('GET', `/api/permissions/${encodeURIComponent(uid)}`, { sess: s });
      const list = a.json && (a.json.permissions || a.json.Permissions);
      const hp = Array.isArray(list) ? list.find((p) => readJ(p, 'menuCode', 'MenuCode') === 'HR_PROXY') : null;
      return { status: a.status, hrProxyUpdate: hp ? readJ(hp, 'canUpdate', 'CanUpdate') === true : null, note: hp ? '' : 'HR_PROXY 줄 없음' };
    }).catch((e) => ({ status: null, hrProxyUpdate: null, note: `읽기 실패 ${L.errorName(e)}` }));
    console.log(`K 쿨타임 CT3 진단 — 부모 화면 권한 읽기 status=${permDiag.status} · HR_PROXY 수정=${permDiag.hrProxyUpdate} ${permDiag.note}`);
    step.now = '관리자 화면 로그인';
    adm = await openCtx(st, '부모');
    await login(adm, ID.parent, PW.parent);
    step.now = '근태 화면';
    if (!(await K.navigateInApp(adm.page, '/hr/attendance'))) throw new K.KitStepError('앱 안 이동 함수 없음');
    await adm.page.waitForURL((u) => u.pathname === '/hr/attendance', { timeout: 15000, waitUntil: 'commit' });
    const rowSel = `tr:has-text("${DA_NAME}"):has(.mud-chip:has-text("자동"))`;
    await adm.page.locator(rowSel).first().waitFor({ state: 'visible', timeout: 20000 });
    step.now = '첫 고침(값 그대로 · 까닭)';
    // ⬛ [낡은 줄 · 11/28 이전] await editOnce(adm.page, rowSel, '자동', 'CI 첫 고침', null);
    await editOnce(adm.page, rowSel, '자동', 'CI 첫 고침', null, step);
    step.now = '다시 고침(둘째 값)';
    const rowSel2 = `tr:has-text("${DA_NAME}"):has(.mud-chip:has-text("고침"))`;
    await adm.page.locator(rowSel2).first().waitFor({ state: 'visible', timeout: 20000 });
    // ⬛ [낡은 줄 · 11/28 이전] await editOnce(adm.page, rowSel2, '고침', 'CI 두 번째 고침', t2);
    await editOnce(adm.page, rowSel2, '고침', 'CI 두 번째 고침', t2, step);
    step.now = '화면 · API 대조';
    const cell = (await adm.page.locator(rowSel2).first().innerText({ timeout: 10000 }).catch(() => '')) || '';
    const screenHas = cell.includes(hm(t2));
    // ⬛ [낡은 줄 · 11/28 이전] const api = await adminCall(async (s) => {
    // 🆕 11/28 런 46078641222 「화면 · API 대조 · PARENT 로그인 status=409」 — 부모가 브라우저로 로그인해 있는 동안 API 로 한 번 더
    //   로그인하면 같은 계정 · 다른 기기라 409(Login 「다른 PC에서 사용 중」). → 이 대조는 **브라우저의 부모 세션 그대로**(같은 사람 · 같은 출입증) 읽는다.
    const readApi = async (s) => {
      const a = await K.apiCall('GET', q, { sess: s });
      const h = await K.apiCall('GET', `/api/hr/attendance/${encodeURIComponent(attId)}/history`, { sess: s });
      return { row: Array.isArray(a.json) ? (a.json.find((x) => readJ(x, 'attendanceId', 'AttendanceId') === attId) || null) : null, hist: Array.isArray(h.json) ? h.json.length : null, histStatus: h.status };
    };
    if (!adm.tok.access) throw new K.KitStepError('부모 브라우저 세션 출입증 없음(대조 못 함)');
    const api = await readApi(sessOf(adm));
    const apiOut = hmOfText(readJ(api.row, 'checkOut', 'CheckOut'));
    const apiSrc = readJ(api.row, 'checkOutSource', 'CheckOutSource');
    const pass = screenHas && apiOut === hm(t2) && apiSrc === 'edited' && api.hist === 2;
    rec(st, 'reedit', { pass, got: { autoOut: out0, second: hm(t2), screenHasSecond: screenHas, apiOut, apiSource: apiSrc ?? null, history: api.hist, historyStatus: api.histStatus } });
    await shot(adm, 'CT3-reedit');
  } catch (e) {
    if (adm && adm.page) await shot(adm, 'CT3-fail');
    // 🆕 11/28 — 고치기 칸이 숨은 것은 장치가 아니라 시험 대상의 동작이다 → 시험 실패(TEST)로 적는다.
    if (e instanceof EditFieldsHidden) {
      console.log(`K 쿨타임 reedit 시험 실패 — 단계=${step.now} · ${e.message}`);
      rec(st, 'reedit', { pass: false, got: { step: step.now, editFieldsHidden: true, why: e.message } });
    } else {
      dev(st, 'reedit', step, e);
    }
  } finally {
    if (adm) await closeKb(st, adm);
  }
}

// ── 쿨타임 N4 줄(이 몸 그림 전부 · DEVICE) — kit35.recN4 와 같은 순서: 봉인 → 판정 → (거짓이면) 표지 먼저 → 기록 ──
function recCtN4(st, forcedWhy = null) {
  for (const kb of st.kbs) kb.sealed = true;
  st.sealed = true;
  const masked = st.kbs.reduce((s, kb) => s + kb.tok.masked, 0);
  const v = K.n4Verdict(st.n4, masked, !!forcedWhy);
  if (!v.pass) {
    K.markN4Hit('쿨타임');
    console.log(`K 쿨타임 N4 거짓(${v.why === 'leak' ? '누출 신호' : '대조 못 함 · 장치'}) — 재실행 대상 아님 · 다음 업로드 단계 닫힘 표지 넘김`);
  }
  const n4 = st.n4;
  const head = v.pass ? 'N4 통과' : J.N4_WHY[v.why];
  const got = `${head}${forcedWhy ? ` · 강제 거짓(${forcedWhy})` : ''} · 가린 K 토큰 ${masked} · 대조 통과 그림 묶음 ${n4.verified}(파일 ${n4.shots}) · 안 찍음 ${n4.blocked} · 찍고 지움 ${n4.removed} · 걸림 ${n4.hits} · 대조 예외 ${n4.errs} · 창 집합 다름 ${n4.winDiff || 0} · 대조 중 등록 늘어남/등록 대기 중 ${n4.regGrow || 0} · 옮기기 실패 ${n4.moveFail || 0} · 지우기 실패 ${n4.delFail} · 그림 픽셀 = 미측정`;
  return K.recK(st.r, {
    key: N4_KEY,
    what: 'N4(쿨타임 몸) 그림 전 화면 글자 대조 — 앞 몸 N4 와 같은 대조 · 거짓이면 누출 신호 또는 대조 못 함 · 재실행 대상 아님',
    got, pass: v.pass, kind: 'DEVICE',
  });
}

// 끝 정리 — 쿨타임 끔(관리자 새 로그인) · 연 세션 로그아웃 · 브라우저 닫기. 실패는 수만 센다(던지지 않음).
async function cleanup(st) {
  if (st.cleaned) return;
  st.cleaned = true;
  if (st.saved.length > 0 && !st.disabled) {
    try {
      await adminSave(st, { cooltimeEnabled: false }, '끝-끔');
      st.disabled = true;
    } catch (e) {
      st.logoutFail += 1;
      console.log(`⚫ K 쿨타임 끝 정리 — 끄기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 200) : L.errorName(e)}`);
    }
  }
  for (const kb of st.kbs) await closeKb(st, kb);
  if (st.ga) {
    const lo = await K.apiLogout(st.ga);
    console.log(`K 쿨타임 가 API 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
    st.ga = null;
  }
  if (st.br) await st.br.close().catch((e) => console.log(`K 쿨타임 브라우저 닫기 실패 ${L.errorName(e)}`));
  try {
    fs.rmSync(K.stageDir(BROWSER), { recursive: true, force: true });
  } catch (e) {
    console.log(`K 쿨타임 임시 그림 자리 정리 실패 ${L.errorName(e)}`);
  }
}

const st0 = {
  r: null, br: null, kbs: [], ga: null, daEmpId: null, daAttId: null, checkIn: null, daEnded: false, ct3Pre: null,
  c1: null, c2: null, saved: [], disabled: false, cleaned: false, sealed: false, logoutFail: 0,
  n4: { verified: 0, shots: 0, blocked: 0, removed: 0, delFail: 0, hits: 0, errs: 0, winDiff: 0, moveFail: 0, planted: false },
};

async function main() {
  const st = st0;
  st.r = L.loadResult();
  L.bindAutosave(st.r);
  const allKeys = [KEY.tab2, KEY.offOut, KEY.offCont, KEY.reedit];
  let fatalOnce = false;
  const fatal = (where) => (e) => {
    if (fatalOnce) return;
    fatalOnce = true;
    for (const kb of st.kbs) kb.sealed = true;
    K.markN4Hit(`쿨타임 예외 길 ${where}`);
    try {
      console.log(`K 쿨타임 프로세스 밖 예외(${where}) ${L.errorName(e)}`);
      K.fillDevice(st.r, allKeys, `단언 전 실패 — 프로세스 밖 예외(${where})`, e);
      if (!K.isWritten(st.r, N4_KEY)) recCtN4(st, where);
      L.saveResult(st.r);
    } catch (e2) {
      console.log(`⚫ K 쿨타임 끊긴 자리 기록 실패 (${L.errorName(e2)})`);
    }
    // 쿨타임 끄기 · 세션 놓기 — 10초 상한 뒤 무조건 종료
    setTimeout(() => process.exit(1), 10000).unref();
    cleanup(st).then(() => process.exit(1), () => process.exit(1));
  };
  process.on('unhandledRejection', fatal('unhandledRejection'));
  process.on('uncaughtException', fatal('uncaughtException'));
  for (const sig of ['SIGTERM', 'SIGINT']) process.on(sig, () => fatal(`신호 ${sig}`)(new K.KitStepError(`신호 ${sig}`)));
  setTimeout(() => fatal('쿨타임 전체 시간 상한')(new K.KitStepError(`쿨타임 전체 시간 상한 ${CT_TOTAL_MS / 60000}분`)), CT_TOTAL_MS).unref();

  let pre = null;
  try {
    const pw = require('playwright');
    st.br = await pw[BROWSER].launch();
  } catch (e) {
    pre = `브라우저 기동 실패 ${L.errorName(e)}`;
  }
  if (pre) {
    console.log(`K 쿨타임 시작 전 실패 — ${pre}`);
    K.fillDevice(st.r, allKeys, `단언 전 실패 — ${pre}`);
  } else {
    await cycleA(st);
    await cycleB(st);
    await ct3(st);
    K.fillDevice(st.r, allKeys, '경우 끝 뒤 남은 키(있으면 장치 오류)');
  }
  await cleanup(st);
  recCtN4(st);
  const mine = st.r.rows.filter((x) => x.scope === 'K' && String(x.id || '').startsWith('K35-CT'));
  const bad = mine.filter((x) => x.pass !== true).length;
  console.log(`K 쿨타임 본문 끝: 줄 ${mine.length} · 거짓 ${bad} · 로그아웃·끄기 실패 ${st.logoutFail} · 저장 ${st.saved.join(' / ') || '-'}`);
  L.saveResult(st.r);
  process.exit(bad === 0 && st.logoutFail === 0 ? 0 : 1);
}

if (require.main === module) main();
// ⬛ [낡은 줄 · 11/28 F1 이전] module.exports = { pickC, pickGrid, hmOfText, KEY, N4_KEY };
module.exports = { pickC, pickGrid, hmOfText, ymdOfText, KEY, N4_KEY };
