// K35 본문 — 알림 → 메신저 팝업창 G1~G5 (작업지시서 20261106작 #691 재기반 §4-2 · 원 오더 20261028작G1-3 §4-1 · PM 결재 A7)
//
// 🔴 이 파일의 규칙
//   ① 기록은 kit35.recK 하나로만(범위 K 고정 · G 기대 키 밖 = 던짐). 진행 단계는 콘솔에만(결과표에 다른 키 0).
//   ② 판정은 주소 · window.open 호출 수 · 창 수로만(글자 존재로 판정 0). 단언 전 실패(로그인 · 알림 만들기 · 시간초과) = 그 키에 DEVICE 거짓.
//   ③ 브라우저 칸 = §4-2 표 그대로 — G1·G2 = chromium · webkit · G3~G5 = chromium. N4 = 브라우저마다 한 줄(DEVICE).
//   ④ 그림은 kit35.guardedShot 하나로만(찍기 전·뒤 화면 글자 대조 · 그림 픽셀 = 미측정).
//   ⑤ 브리핑 덮개 = 장치 내보냄 closeBriefing · H-BRIEF 줄은 쓰지 않는다.
//   ⑥ 알림은 실제 흐름으로만 만든다 — 가(API)가 방에 글 · 부모(API)가 결재선에 나를 두고 상신(결재 알림 · 채팅방 결재 안내). DB 주입 0.
//   ⑦ 이 PC 에서 돌리지 않는다(이 PC DB = 실물) — CI 의 S8 에서만.
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const L = require('./lib');
const K = require('./kit35');
const DC = require('./device-check');

const env = process.env;
const ROOM_A = env.UI_ROOM_A || '';
const ROOM_B = env.UI_ROOM_B || '';
const ID = { na: env.UI_LOGIN_NA || 'ci-na', ga: env.UI_LOGIN_GA || 'ci-ga', parent: env.UI_LOGIN_PARENT || 'ci-parent' };
const NA_NAME = '시험 나'; // 시드② 표시 이름(seed-children.js)
const DOC_TYPE = 'report_daily'; // 결재 문서 종류(ApprovalTriggerHelper 이름표 「일일보고서」)
const T_CHAT = '새 메시지'; // ChatService 알림 제목
const T_APPR = '결재가 올라왔습니다'; // ApprovalTriggerHelper 알림 제목
const T_CHATAPPR = '결재 안내'; // ChatService 결재 안내 제목
const POP_MS = 15000;
const NAV_MS = 15000;
const SETTLE_MS = 1500;
// 🔴 3차(PM 결정 4V-2 P2-a) — 경우 하나 · K 본문 전체 시간 상한. 넘으면 그 경우 DEVICE 거짓 · 그 브라우저 흐름 끊음 /
//   전체 상한은 예외 길(남은 키 DEVICE · N4 강제 거짓 · 표지 먼저 · 종료). 워크플로 S8 단계 상한(20분) 안 · 잡 30분 안.
const CASE_MS = 150000;
const K_TOTAL_MS = 12 * 60 * 1000;
const BROWSERS = ['chromium', 'webkit'];

const key = (id, g, p, browser) => ({ id, g, path: p, browser });
const nowait = (p, what) => p.catch((e) => console.log(`K ${what} 기다림 끝 (${L.errorName(e)})`));

// ── 알림 만들기(실제 흐름) ─────────────────────────────────────────
async function sendChat(st, room, tag) {
  const body = `K35 ${tag}`;
  const res = await K.apiCall('POST', `/api/chat/rooms/${encodeURIComponent(room)}/messages`, { sess: st.ga, body: { body } });
  if (res.status !== 200 && res.status !== 201) throw new K.KitStepError(`가 메시지 보내기 status=${res.status}`);
  return body;
}
// 결재 준비(한 번) — 화면의 권한설정 · 결재선 저장과 같은 API(부모 = 관리자). 실패는 기억해 뒤 G4 경우도 같은 이유로 선다.
async function approvalReady(st) {
  if (st.appr) {
    if (st.appr.err) throw new K.KitStepError(st.appr.err);
    return st.appr;
  }
  try {
    st.parent = await K.apiLogin('PARENT', ID.parent, env.UI_PW_PARENT || '');
    const perms = await K.apiCall('GET', '/api/permissions', { sess: st.parent });
    if (perms.status !== 200 || !Array.isArray(perms.json)) throw new K.KitStepError(`권한 목록 status=${perms.status}`);
    const u = perms.json.find((x) => (x.empName || x.EmpName) === NA_NAME || (x.userName || x.UserName) === NA_NAME);
    if (!u) throw new K.KitStepError('권한 목록에 「시험 나」 없음');
    const save = await K.apiCall('POST', '/api/permissions', {
      sess: st.parent,
      body: { userId: u.userId || u.UserId, permissions: [{ menuCode: 'APPROVAL', menuName: '결재', canView: true, canCreate: false, canUpdate: false, canDelete: false, canExport: false }] },
    });
    if (save.status !== 200) throw new K.KitStepError(`결재 권한 저장 status=${save.status}`);
    const emps = await K.apiCall('GET', '/api/chat/employees', { sess: st.parent });
    if (emps.status !== 200 || !Array.isArray(emps.json)) throw new K.KitStepError(`chat/employees status=${emps.status}`);
    const na = emps.json.find((e) => (e.empName || e.EmpName) === NA_NAME);
    if (!na) throw new K.KitStepError('chat/employees 에 「시험 나」 없음');
    const naEmpId = na.employeeId || na.EmployeeId;
    const lines = await K.apiCall('POST', '/api/approval/lines', {
      sess: st.parent,
      body: { docType: DOC_TYPE, lines: [{ seqNo: 1, approverId: naEmpId, approverName: NA_NAME, roleLabel: '시험' }] },
    });
    if (lines.status !== 200) throw new K.KitStepError(`결재선 저장 status=${lines.status}`);
    st.appr = { naEmpId };
    return st.appr;
  } catch (e) {
    st.appr = { err: e instanceof K.KitStepError ? e.message : `결재 준비 예외 ${L.errorName(e)}` };
    throw e;
  }
}
async function submitApproval(st, tag) {
  await approvalReady(st);
  const res = await K.apiCall('POST', '/api/approval/documents', {
    sess: st.parent,
    body: { docType: DOC_TYPE, refId: crypto.randomUUID(), title: `K35 ${tag}`, amount: 0 },
  });
  if (res.status !== 201 && res.status !== 200) throw new K.KitStepError(`결재 상신 status=${res.status}`);
}
// 채팅방 결재 안내가 가는 방(부모 ↔ 나 1:1) — 부모 방 목록의 1:1 방 하나
async function roomParentNa(st) {
  if (st.roomPN) return st.roomPN;
  const res = await K.apiCall('GET', '/api/chat/rooms', { sess: st.parent });
  if (res.status !== 200 || !Array.isArray(res.json)) throw new K.KitStepError(`부모 방 목록 status=${res.status}`);
  const d = res.json.filter((x) => (x.roomType || x.RoomType) === 'direct');
  if (d.length !== 1) throw new K.KitStepError(`부모 1:1 방 ${d.length}개(1 기대)`);
  st.roomPN = d[0].roomId || d[0].RoomId;
  return st.roomPN;
}

// ── 경우마다 바닥 정리 ─────────────────────────────────────────────
async function prep(kb, step) {
  step.now = '팝업 정리';
  await K.closePopups(kb.context, kb.page);
  step.now = '막기 끔';
  const off = await K.recorder(kb.page, 'block-off');
  if (off.err) throw new K.KitStepError(`녹음기 ${off.err}`);
  step.now = '토스트 비움';
  if (!(await K.waitNoToasts(kb.page))) throw new K.KitStepError('토스트가 상한 안에 안 사라짐');
  step.now = '브리핑 덮개';
  await K.briefOff(kb.page, kb, 0);
  step.now = '녹음기 비움';
  const c = await K.recorder(kb.page, 'clear');
  if (c.err) throw new K.KitStepError(`녹음기 ${c.err}`);
}
async function opens(kb) {
  const x = await K.recorder(kb.page, 'list');
  if (x.err) throw new K.KitStepError(`녹음기 ${x.err}`);
  return Array.isArray(x.list) ? x.list : [];
}
// 알림 도착(토스트가 뜸) → 길에 따라 누름. 종 길은 토스트가 다 사라진 뒤 목록을 열어 그 알림을 누른다.
//   팝업 기다림은 누르기 **바로 전**에 건다(popupMs) — 종 길의 토스트 사라짐 기다림이 팝업 상한을 먹지 않게. 돌려주는 값 = 팝업 또는 null.
async function clickVia(kb, step, way, title, itemText, popupMs) {
  step.now = '알림 도착(토스트)';
  await K.waitToast(kb.page, title);
  let popP;
  if (way === 'bell') {
    step.now = '토스트 사라짐 기다림(종 길)';
    if (!(await K.waitNoToasts(kb.page))) throw new K.KitStepError('토스트가 상한 안에 안 사라짐');
    await K.briefOff(kb.page, kb, 0);
    step.now = '종 목록 클릭';
    popP = K.expectPopup(kb.page, popupMs);
    await K.clickBellItem(kb.page, itemText);
  } else {
    step.now = '토스트 클릭';
    popP = K.expectPopup(kb.page, popupMs);
    await K.clickToast(kb.page, title);
  }
  return popP;
}

// ── G1 알림 → 팝업 1회 · 본 창 그대로 ─────────────────────────────
async function g1(kb, st, step, way) {
  const room = ROOM_A;
  const want = `/chat-window/${room}`;
  step.now = '알림 만들기';
  const before = kb.page.url();
  const body = await sendChat(st, room, `G1 ${way} ${kb.browser}`);
  const popP = await clickVia(kb, step, way, T_CHAT, body, POP_MS);
  step.now = '팝업 기다림';
  const pop = await popP;
  let popPath = null;
  if (pop) {
    await nowait(pop.waitForLoadState('domcontentloaded', { timeout: 30000 }), '팝업 적재');
    popPath = K.pathOf(pop.url());
  }
  await K.sleep(SETTLE_MS);
  const list = await opens(kb);
  const after = kb.page.url();
  const wins = K.popupsOf(kb.context, kb.page).length;
  const first = list[0] || {};
  const pass = list.length === 1 && first.name === K.POPUP_NAME && first.url === want && !!pop && popPath === want && wins === 1 && after === before;
  return {
    pass,
    got: { opens: list.length, name: first.name ?? null, openUrl: first.url === want ? '방 팝업 주소 같음' : (first.url ?? null), popup: !!pop, popupPath: popPath === want ? '같음' : popPath, windows: wins, mainSame: after === before, main: K.pathOf(after) },
  };
}

// ── G2 window.open 이 null → 본 창 /chat/{방} + 안내 ──────────────
async function g2(kb, st, step, way) {
  const room = way === 'bell' ? ROOM_A : ROOM_B; // 두 길이 같은 방이면 둘째 길이 앞 길의 도착 주소에서 시작한다(거짓 통과 0)
  const want = `/chat/${room}`;
  step.now = '막기 켬';
  const on = await K.recorder(kb.page, 'block-on');
  if (on.err) throw new K.KitStepError(`녹음기 ${on.err}`);
  const before = K.pathOf(kb.page.url());
  step.now = '알림 만들기';
  const body = await sendChat(st, room, `G2 ${way} ${kb.browser}`);
  const popP = await clickVia(kb, step, way, T_CHAT, body, 5000);
  step.now = '본 창 이동 기다림';
  await nowait(kb.page.waitForURL((u) => u.pathname === want, { timeout: NAV_MS, waitUntil: 'commit' }), '본 창 이동');
  const pop = await popP;
  await K.sleep(SETTLE_MS);
  const list = await opens(kb);
  const main = K.pathOf(kb.page.url());
  const wins = K.popupsOf(kb.context, kb.page).length;
  const notice = await K.toastTexts(kb.page); // 관찰만(판정에 안 씀)
  await K.recorder(kb.page, 'block-off');
  const first = list[0] || {};
  const pass = list.length === 1 && first.name === K.POPUP_NAME && !pop && wins === 0 && main === want && before !== want;
  return { pass, got: { opens: list.length, name: first.name ?? null, popup: !!pop, windows: wins, mainAtRoom: main === want, startedAtTarget: before === want, main, notice } };
}

// ── G3 창 이름 같음 · 두 번 눌러 창 1 ──────────────────────────────
async function g3(kb, st, step) {
  step.now = '상단바 메신저 클릭(첫째)';
  const p1P = K.expectPopup(kb.page, POP_MS);
  await K.clickMessenger(kb.page);
  const pop1 = await p1P;
  if (pop1) await nowait(pop1.waitForLoadState('domcontentloaded', { timeout: 30000 }), '상단바 팝업 적재');
  const l1 = await opens(kb);
  const name1 = (l1[0] || {}).name ?? null;
  if (pop1) await pop1.close().catch((e) => console.log(`K 상단바 팝업 닫기 실패 ${L.errorName(e)}`));
  await K.sleep(500);
  await K.recorder(kb.page, 'clear');
  step.now = '알림 만들기';
  const body = await sendChat(st, ROOM_A, `G3 bell ${kb.browser}`);
  const p2P = await clickVia(kb, step, 'bell', T_CHAT, body, POP_MS);
  const pop2 = await p2P;
  if (pop2) await nowait(pop2.waitForLoadState('domcontentloaded', { timeout: 30000 }), '알림 팝업 적재');
  const l2 = await opens(kb);
  const name2 = (l2[0] || {}).name ?? null;
  step.now = '상단바 메신저 클릭(둘째 · 창 하나 그대로여야)';
  const p3P = K.expectPopup(kb.page, 4000);
  await K.clickMessenger(kb.page);
  const pop3 = await p3P;
  await K.sleep(SETTLE_MS);
  const wins = K.popupsOf(kb.context, kb.page).length;
  const pass = !!pop1 && !!pop2 && !pop3 && name1 === K.POPUP_NAME && K.sameWindowName(name1, name2) && wins === 1;
  return { pass, got: { topBarName: name1, noticeName: name2, same: K.sameWindowName(name1, name2), topBarPopup: !!pop1, noticePopup: !!pop2, secondClickNewWindow: !!pop3, windows: wins } };
}

// ── G4 결재 알림 · 채팅방 결재 안내 → 팝업 0 · 본 창(주소 정확히 일치) ──
// ⬛ [낡은 꼴 · 2차] 시작 주소 = 앞 경우 도착 주소 그대로(before) → 앞 경우가 목표에 남기면 다음 경우가 제품 잘못 없이 [시험] 빨강(4V-2 P1-a).
// 🔴 3차 — 목표 주소를 먼저 구하고 → 본 창을 중립 주소(로그인 뒤 자리)로 되돌려 확인 → 시작 = 목표면 DEVICE(단언 전 실패) → 상신.
async function toNeutral(kb, step, want) {
  step.now = '본 창 중립 주소로 되돌림';
  const cur = K.pathOf(kb.page.url());
  const plan = K.g4Reset(cur, kb.home, want);
  if (!plan.ok) throw new K.KitStepError(`되돌림 판단 — ${plan.why}`);
  if (plan.nav) {
    if (!(await K.navigateInApp(kb.page, kb.home))) throw new K.KitStepError('앱 안 이동 함수 없음');
    await nowait(kb.page.waitForURL((u) => `${u.pathname}${u.search}` === kb.home, { timeout: NAV_MS, waitUntil: 'commit' }), '중립 주소 이동');
    await K.sleep(SETTLE_MS);
    await K.briefOff(kb.page, kb, 2000);
  }
  const start = K.pathOf(kb.page.url());
  if (!K.g4StartOk(start, kb.home, want)) throw new K.KitStepError(start === want ? '시작 주소 = 목표 주소' : '되돌린 뒤 중립 주소 아님');
  return start;
}
async function g4(kb, st, step, way, which) {
  // ⬛ [낡은 줄 · 2차] step.now = '결재 상신(실제 흐름)';
  // ⬛ [낡은 줄 · 2차] const before = K.pathOf(kb.page.url());
  // ⬛ [낡은 줄 · 2차] await submitApproval(st, `G4 ${which} ${way}`);
  step.now = '결재 준비';
  await approvalReady(st);
  const title = which === 'APPR' ? T_APPR : T_CHATAPPR;
  let want = '/approval?scope=pending'; // 제품이 /approval/pending 을 결재관리로 넘긴다(ApprovalRedirectPage)
  if (which === 'CHATAPPR') {
    step.now = '부모 ↔ 나 1:1 방 찾기';
    want = `/chat/${await roomParentNa(st)}`;
  }
  const before = await toNeutral(kb, step, want);
  step.now = '결재 상신(실제 흐름)';
  await submitApproval(st, `G4 ${which} ${way}`);
  const popP = await clickVia(kb, step, way, title, title, 5000);
  step.now = '본 창 이동 기다림';
  await nowait(kb.page.waitForURL((u) => `${u.pathname}${u.search}` === want, { timeout: NAV_MS, waitUntil: 'commit' }), '본 창 이동');
  const pop = await popP;
  await K.sleep(SETTLE_MS);
  const list = await opens(kb);
  const main = K.pathOf(kb.page.url());
  const wins = K.popupsOf(kb.context, kb.page).length;
  const pass = list.length === 0 && !pop && wins === 0 && main === want && before !== want;
  return { pass, got: { opens: list.length, popup: !!pop, windows: wins, mainExact: main === want, startedAtTarget: before === want, main } };
}

// ── G5 팝업 방 A 열린 채 방 B 알림 → 팝업 /chat-window/B · 창 1 ────
async function g5(kb, st, step, way) {
  const wantA = `/chat-window/${ROOM_A}`;
  const wantB = `/chat-window/${ROOM_B}`;
  step.now = '팝업 방 A 준비(hitpanChat.openRoom)';
  const p0P = K.expectPopup(kb.page, POP_MS);
  // ⬛ [낡은 줄 · 2차] const opened = await kb.page.evaluate((u) => !!(window.hitpanChat && window.hitpanChat.openRoom(u)), wantA);
  const opened = await K.withTimeout(kb.page.evaluate((u) => !!(window.hitpanChat && window.hitpanChat.openRoom(u)), wantA), K.EVAL_MS, '팝업 방 A 열기');
  const pop = await p0P;
  if (!opened || !pop) throw new K.KitStepError(`팝업 방 A 준비 실패(열림 ${opened} · 팝업 ${!!pop})`);
  await nowait(pop.waitForLoadState('load', { timeout: 30000 }), '팝업 A 적재');
  await K.sleep(3000); // 팝업 안 화면 기동 몫
  if (K.pathOf(pop.url()) !== wantA) throw new K.KitStepError('팝업 방 A 주소 아님');
  await K.recorder(kb.page, 'clear');
  const before = kb.page.url();
  step.now = '알림 만들기(방 B)';
  const body = await sendChat(st, ROOM_B, `G5 ${way} ${kb.browser}`);
  const newP = await clickVia(kb, step, way, T_CHAT, body, 5000);
  step.now = '팝업 이동 기다림';
  await nowait(pop.waitForURL((u) => u.pathname === wantB, { timeout: NAV_MS, waitUntil: 'commit' }), '팝업 이동');
  const extra = await newP;
  await K.sleep(SETTLE_MS);
  const list = await opens(kb);
  const popPath = pop.isClosed() ? '닫힘' : K.pathOf(pop.url());
  const wins = K.popupsOf(kb.context, kb.page).length;
  const after = kb.page.url();
  const pass = !pop.isClosed() && popPath === wantB && !extra && wins === 1 && after === before;
  return { pass, got: { popupAtB: popPath === wantB, popupStillA: popPath === wantA, newWindow: !!extra, windows: wins, opens: list.length, mainSame: after === before, main: K.pathOf(after) } };
}

// ── 경우 목록(§4-2 표 그대로) ──────────────────────────────────────
function casesFor(browser) {
  const c = (id, g, way, label, what, fn) => ({ key: key(id, g, way, browser), label, what, fn });
  const list = [
    c('K35-G1', 'G1', 'bell', 'G1-bell', 'G1 종 길: 새 메시지 알림 → window.open 1회 · hitpanChat · /chat-window/{방} · 창 +1 · 본 창 주소 그대로', (kb, st, s) => g1(kb, st, s, 'bell')),
    c('K35-G1', 'G1', 'toast', 'G1-toast', 'G1 토스트 길: 새 메시지 알림 → window.open 1회 · hitpanChat · /chat-window/{방} · 창 +1 · 본 창 주소 그대로', (kb, st, s) => g1(kb, st, s, 'toast')),
    c('K35-G2', 'G2', 'bell', 'G2-bell', 'G2 종 길: window.open 이 null → 새 창 0 · 본 창 /chat/{방}', (kb, st, s) => g2(kb, st, s, 'bell')),
    c('K35-G2', 'G2', 'toast', 'G2-toast', 'G2 토스트 길: window.open 이 null → 새 창 0 · 본 창 /chat/{방}', (kb, st, s) => g2(kb, st, s, 'toast')),
  ];
  if (browser !== 'chromium') return list;
  return [
    ...list,
    c('K35-G3', 'G3', 'bell', 'G3-bell', 'G3: 상단바 메신저 창 이름 == 알림 창 이름 == hitpanChat · 상단바 한 번 더 눌러도 창 1', (kb, st, s) => g3(kb, st, s)),
    c('K35-G5', 'G5', 'bell', 'G5-bell', 'G5 종 길: 팝업 방 A 열린 채 방 B 알림 → 같은 팝업이 /chat-window/B · 창 1 · 본 창 그대로', (kb, st, s) => g5(kb, st, s, 'bell')),
    c('K35-G5', 'G5', 'toast', 'G5-toast', 'G5 토스트 길: 팝업 방 A 열린 채 방 B 알림 → 같은 팝업이 /chat-window/B · 창 1 · 본 창 그대로', (kb, st, s) => g5(kb, st, s, 'toast')),
    c('K35-G4-APPR', 'G4', 'bell', 'G4-appr-bell', 'G4 종 길: 결재 알림 → window.open 0 · 새 창 0 · 본 창 /approval?scope=pending(정확히)', (kb, st, s) => g4(kb, st, s, 'bell', 'APPR')),
    c('K35-G4-CHATAPPR', 'G4', 'bell', 'G4-chatappr-bell', 'G4 종 길: 채팅방 결재 안내(chat_approval) → window.open 0 · 새 창 0 · 본 창 /chat/{방}(정확히)', (kb, st, s) => g4(kb, st, s, 'bell', 'CHATAPPR')),
    c('K35-G4-APPR', 'G4', 'toast', 'G4-appr-toast', 'G4 토스트 길: 결재 알림 → window.open 0 · 새 창 0 · 본 창 /approval?scope=pending(정확히)', (kb, st, s) => g4(kb, st, s, 'toast', 'APPR')),
    c('K35-G4-CHATAPPR', 'G4', 'toast', 'G4-chatappr-toast', 'G4 토스트 길: 채팅방 결재 안내(chat_approval) → window.open 0 · 새 창 0 · 본 창 /chat/{방}(정확히)', (kb, st, s) => g4(kb, st, s, 'toast', 'CHATAPPR')),
  ];
}

// 🔴 3차(4V-2 P2-a) — 경우 하나에 시간 상한. 넘으면 그 경우 DEVICE 거짓 · 그림 안 찍음 · 돌려주는 값 true = 그 브라우저 흐름 끊음.
async function runCase(kb, st, c) {
  // ⬛ [낡은 줄 · 2차] if (K.isWritten(st.r, c.key)) return;
  if (K.isWritten(st.r, c.key)) return false;
  const step = { now: '준비' };
  let timedOut = false;
  try {
    if (!kb.page || kb.page.isClosed()) throw new K.KitStepError('본 창 닫힘');
    // ⬛ [낡은 줄 · 2차] await prep(kb, step);
    // ⬛ [낡은 줄 · 2차] const res = await c.fn(kb, st, step);
    const body = (async () => {
      await prep(kb, step);
      return c.fn(kb, st, step);
    })();
    const res = await K.withTimeout(body, CASE_MS, `경우 ${c.label}`).catch((e) => {
      if (e instanceof K.KitStepError && e.message.startsWith('시간 상한 ') && e.message.endsWith(`경우 ${c.label}`)) timedOut = true;
      throw e;
    });
    K.recK(st.r, { key: c.key, what: c.what, got: res.got, pass: res.pass, kind: 'TEST' });
  } catch (e) {
    const why = e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${L.errorName(e)}`;
    console.log(`K ${c.label} (${kb.browser}) 단언 전 실패 — 단계=${step.now} · ${why}`);
    if (!K.isWritten(st.r, c.key)) {
      K.recK(st.r, { key: c.key, what: c.what, got: `단언 전 실패 — 단계=${step.now} · ${why}`, pass: false, kind: 'DEVICE', err: e instanceof K.KitStepError ? null : e });
    }
  }
  K.flushConsole(kb);
  if (timedOut) return true; // 멈춘 동작이 뒤에서 돌 수 있다 — 그림 안 찍고 흐름을 끊는다
  try {
    await K.guardedShot(kb, c.label);
  } catch (e) {
    console.log(`K 그림 단계 예외 ${c.label} (${L.errorName(e)}) — 안 찍힌 것으로 본다`);
    kb.n4.errs += 1;
  }
  return false;
}

function newKb(browser, flags) {
  return {
    browser, flags, plantN4: flags.has('n4'),
    br: { seen: 0, closed: 0, errs: 0, lastErr: null },
    tok: { access: null, deviceId: null, masked: 0 },
    // ⬛ [낡은 줄 · 2차] n4: { verified: 0, shots: 0, blocked: 0, removed: 0, delFail: 0, hits: 0, errs: 0, planted: false },
    n4: { verified: 0, shots: 0, blocked: 0, removed: 0, delFail: 0, hits: 0, errs: 0, winDiff: 0, moveFail: 0, planted: false },
    // ⬛ [낡은 줄 · 2차] hub: { ws: 0 }, consoleLines: 0, tally: { continue: 0, abort: 0 },
    hub: { ws: 0 }, consoleLines: 0, consoleBuf: [], tally: { continue: 0, abort: 0 },
    // ⬛ [낡은 줄 · 2차] pw: null, context: null, page: null, done: false,
    pw: null, context: null, page: null, done: false, sealed: false, home: null,
  };
}
// 브라우저 하나 끝 — 남은 키 DEVICE 거짓 · N4 줄 · 로그아웃 · 닫기(어느 길이든 한 번)
async function finishBrowser(st, kb, why) {
  if (kb.done) return;
  kb.done = true;
  const keys = K.expectKeys(kb.browser).filter((k) => k.g !== 'N4');
  K.fillDevice(st.r, keys, why);
  K.recN4(st.r, kb); // 3차 — 안에서 봉인 → 판정 → 표지 먼저 → 기록
  K.flushConsole(kb);
  if (kb.tok.access) {
    const lo = await K.apiLogout({ who: `나(${kb.browser})`, token: kb.tok.access, deviceId: kb.tok.deviceId });
    console.log(`K 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  }
  if (kb.pw) await kb.pw.close().catch((e) => console.log(`K 브라우저 닫기 실패 ${L.errorName(e)}`));
  try {
    fs.rmSync(K.stageDir(kb.browser), { recursive: true, force: true }); // 3차 — 임시 자리 정리(업로드 밖 · 실패해도 올라가지 않는다)
  } catch (e) {
    console.log(`K 임시 그림 자리 정리 실패 ${L.errorName(e)}`);
  }
  console.log(`K ${kb.browser} 외부 차단 처리기 닫힌 창 거절 삼킴 continue ${kb.tally.continue} · abort ${kb.tally.abort}`);
}

async function runBrowser(pwlib, st, kb) {
  const step = { now: '브라우저 기동' };
  try {
    kb.pw = await pwlib[kb.browser].launch();
    kb.context = await kb.pw.newContext({ baseURL: K.BASE });
    await K.blockExternal(kb.context, st.r, kb.tally);
    K.watchTokens(kb.context, kb.tok);
    // ⬛ [낡은 줄 · 2차] await kb.context.addInitScript(K.BLOCKER); // 먼저 — 녹음기가 그 위를 감싼다
    // ⬛ [낡은 줄 · 2차] await kb.context.addInitScript(DC.RECORDER);
    await kb.context.addInitScript(K.INIT_SCRIPT); // 3차(4V-2 P2-c) — 한 덩어리 · 막기 먼저 → 녹음기(순서 = 코드)
    kb.page = await kb.context.newPage();
    K.watchConsole(kb.context, kb);
    K.watchHub(kb.page, kb.hub);
    step.now = '화면 로그인(나)';
    const s = await K.screenLogin(kb.page, ID.na, env.UI_PW_NA || '');
    if (s.where !== 'main') throw new K.KitStepError(`로그인 뒤 자리=${s.where}`);
    step.now = '토큰 가림 확인';
    await K.waitMasked(kb.tok);
    if (kb.tok.masked === 0) console.log('K 가린 브라우저 토큰 0 — N4 거짓으로 남는다');
    K.flushConsole(kb); // 3차(3V2-05) — 토큰 등록 뒤 소독해 찍음
    step.now = '브리핑 덮개(본 화면 진입)';
    await K.briefOff(kb.page, kb, 3000);
    kb.home = K.pathOf(kb.page.url()); // 3차(4V-2 P1-a) — G4 되돌림 중립 주소
    step.now = '알림 배관 연결';
    if (!(await K.waitHub(kb.hub))) throw new K.KitStepError('알림 배관 연결 0');
    // ⬛ [낡은 줄 · 2차] for (const c of casesFor(kb.browser)) await runCase(kb, st, c);
    for (const c of casesFor(kb.browser)) {
      if (await runCase(kb, st, c)) throw new K.KitStepError(`경우 시간 상한(${c.label}) — 이 브라우저 남은 경우 안 돌림`);
    }
    await finishBrowser(st, kb, '경우 끝 뒤 남은 키(있으면 장치 오류)');
  } catch (e) {
    const why = e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${L.errorName(e)}`;
    console.log(`K ${kb.browser} 흐름 끊김 — 단계=${step.now} · ${why}`);
    await finishBrowser(st, kb, `단언 전 실패 — 단계=${step.now} · ${why}`);
  }
}

async function main() {
  const r = L.loadResult();
  L.bindAutosave(r);
  const flags = DC.plantFlags();
  const st = { r, ga: null, parent: null, appr: null, roomPN: null, logoutFail: 0 };
  const kbs = BROWSERS.map((b) => newKb(b, flags));
  // ⬛ [낡은 꼴 · 2차] 예외 길 — N4 를 그때까지의 셈으로 계산(앞 그림이 통과했으면 참 · 표지 없음) · 표지는 기록 뒤(3V2-01 ③④).
  // 🔴 3차(PM 결정 3V-2 ㉢) — 예외 길 = 봉인 먼저(그 뒤 옮김 0) → 표지 먼저 → 남은 키 DEVICE → N4 강제 거짓. 시간 상한 · 신호도 같은 길.
  let fatalOnce = false;
  const fatal = (where) => (e) => {
    if (fatalOnce) return;
    fatalOnce = true;
    for (const kb of kbs) kb.sealed = true;
    K.markN4Hit(`예외 길 ${where}`);
    try {
      console.log(`K 프로세스 밖 예외(${where}) ${L.errorName(e)}`);
      for (const kb of kbs) {
        if (kb.done) continue;
        kb.done = true;
        K.fillDevice(r, K.expectKeys(kb.browser).filter((k) => k.g !== 'N4'), `단언 전 실패 — 프로세스 밖 예외(${where})`, e);
        // ⬛ [낡은 줄 · 2차] K.recN4(r, kb);
        K.recN4(r, kb, where);
        K.flushConsole(kb);
      }
    } catch (e2) {
      console.log(`⚫ K 끊긴 자리 기록 실패 (${L.errorName(e2)})`);
    }
    process.exit(1);
  };
  process.on('unhandledRejection', fatal('unhandledRejection'));
  process.on('uncaughtException', fatal('uncaughtException'));
  for (const sig of ['SIGTERM', 'SIGINT']) process.on(sig, () => fatal(`신호 ${sig}`)(new K.KitStepError(`신호 ${sig}`)));
  setTimeout(() => fatal('K 전체 시간 상한')(new K.KitStepError(`K 전체 시간 상한 ${K_TOTAL_MS / 60000}분`)), K_TOTAL_MS).unref();

  // G3 비교식 자체 점검 — 시험 안에서 먼저(브라우저와 무관 · 늘 TEST)
  const self = K.g3SelfCheck();
  K.recK(r, { key: key('K35-G3-SELF', 'G3', 'self', 'chromium'), what: 'G3 비교식 자체 점검: 같은 이름 참 · 바꾼 이름 가짜 기록 거짓(비교식이 빨강을 낼 수 있다)', got: self.got, pass: self.pass, kind: 'TEST' });

  let pwlib = null;
  let pre = null;
  try {
    pwlib = require('playwright');
  } catch (e) {
    pre = `playwright 불러오기 실패 ${L.errorName(e)}`;
  }
  if (!pre && (!ROOM_A || !ROOM_B)) pre = '시드 방 번호 없음(UI_ROOM_A · UI_ROOM_B)';
  if (!pre) {
    try {
      st.ga = await K.apiLogin('GA', ID.ga, env.UI_PW_GA || '');
    } catch (e) {
      pre = e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `가 로그인 예외 ${L.errorName(e)}`;
    }
  }
  if (pre) {
    console.log(`K 시작 전 실패 — ${pre}`);
    for (const kb of kbs) await finishBrowser(st, kb, `단언 전 실패 — ${pre}`);
  } else {
    for (const kb of kbs) await runBrowser(pwlib, st, kb); // 순차 — 앞 브라우저 로그아웃 뒤 다음(한 PC 규칙)
  }
  for (const s of [st.ga, st.parent]) {
    if (!s) continue;
    const lo = await K.apiLogout(s);
    console.log(`K API 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  }
  const mine = r.rows.filter((x) => x.scope === 'K');
  const bad = mine.filter((x) => x.pass !== true).length;
  console.log(`K 본문 끝: 줄 ${mine.length} · 거짓 ${bad} · 로그아웃 실패 ${st.logoutFail}`);
  L.saveResult(r);
  process.exit(bad === 0 && st.logoutFail === 0 ? 0 : 1);
}

if (require.main === module) main();
module.exports = { casesFor };
