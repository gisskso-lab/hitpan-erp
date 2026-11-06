// G1-3 갈래 H — S5 시드 ② 자식계정 2(가·나) · 대화방 2(A·B) — 작업지시서 §3-3
//
// 🔴 순서 메모: 자식·대화방은 **떠 있는 API** 로만 만들 수 있어 S6(API 기동) 뒤에 돈다(개발명세서 §1-5).
// 🔴 [사실 정정] 지시서 §2-2 의 `POST api/users/children`(UserController.cs:451)은 **저장하지 않는 뼈대**다
//    (Accepted 만 돌려준다 · :456~467). 그래서 화면이 실제로 쓰는 `POST api/users`(UserController.cs:58 ·
//    화면 AccountSeatApi.cs:51 · UserService.cs:48)로 만든다 — 제품 0줄.
// 🔴 약관: 부모·가(API 로만 보내는 쪽)는 화면 `Terms.razor:117` 과 같은 요청 `POST api/auth/terms/agree` 로 동의한다.
//    나(브라우저 쪽)는 화면에서 사람이 하는 그대로 동의한다(device-check.js · K 시험).
// 접속·갱신 토큰은 받는 즉시 가리고 S10 검색 대상으로 넘긴다(§3-2 S0 ⓑ).
'use strict';

const L = require('./lib');

const BASE = process.env.UI_BASE || 'http://127.0.0.1:18080';
const UA = 'Mozilla/5.0 (X11; Linux x86_64) HitPanCiSeed/1';

class SeedError extends Error {}

async function call(method, path, { token, deviceId, body } = {}) {
  const headers = { 'Content-Type': 'application/json', 'User-Agent': UA };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (deviceId) headers['X-HitPan-Device-Id'] = deviceId;
  const res = await fetch(`${BASE}${path}`, { method, headers, body: body ? JSON.stringify(body) : undefined });
  const text = await res.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch (e) {
    console.log(`응답 JSON 아님 ${method} ${path} (원문 생략 · ${e.name})`);
  }
  return { status: res.status, json };
}

// 로그인 — 응답을 받는 즉시 토큰부터 가린다(첫 출력 · 본문 출력 0).
async function login(who, loginId, password) {
  const res = await call('POST', '/api/auth/login', {
    body: { email: loginId, password, deviceType: 'pc', deviceName: `ci-${who}`, deviceFingerprint: `ci-${who}-fp` },
  });
  const j = res.json || {};
  const access = j.accessToken || j.AccessToken;
  const refresh = j.refreshToken || j.RefreshToken;
  if (access) L.maskAndRegister(`TOKEN_ACCESS_${who}`, access);
  if (refresh) L.maskAndRegister(`TOKEN_REFRESH_${who}`, refresh);
  if (res.status !== 200 || !access) {
    throw new SeedError(`${who} 로그인 status=${res.status} message=${L.sanitize(j.message || j.code || '')}`);
  }
  return {
    token: access,
    deviceId: j.deviceId || j.DeviceId || null,
    awaiting: !!(j.deviceAwaitingApproval || j.DeviceAwaitingApproval),
    terms: !!(j.requiresTermsConsent || j.RequiresTermsConsent),
  };
}

async function agreeTerms(who, s) {
  const res = await call('POST', '/api/auth/terms/agree', {
    token: s.token, deviceId: s.deviceId,
    body: { agreeService: true, agreePrivacy: true, agreeSubscription: true, agreeDataOwnership: true, agreeMarketing: false, termsVersion: 'v2.0.0' },
  });
  if (res.status < 200 || res.status >= 300) throw new SeedError(`${who} 약관 동의 status=${res.status}`);
}

// 🔴 3차 Q-3(리뷰서 §재확인 R-①) — 시드가 연 부모·가 API 로그인을 끝에 닫는다.
//   화면과 같은 길 `POST api/auth/logout`(Web AuthService.cs:243 · AuthController.cs:353 [Authorize]) — 서버가 그 세션을
//   지운다(AuthController.cs:409~426) → K 본문이 부모·가로 화면 로그인할 때 한 PC 규칙 409(AuthService.cs:418~464)에 안 걸린다.
//   device-check.js 의 logoutViaApi 와 같은 요청(런2~4 H-D4 status=200).
async function logout(who, s) {
  try {
    const res = await call('POST', '/api/auth/logout', { token: s.token, deviceId: s.deviceId, body: {} });
    return { who, ok: res.status === 200, got: `${who} status=${res.status}` };
  } catch (e) {
    return { who, ok: false, got: `${who} 요청 실패 ${e.name}` };
  }
}

async function main() {
  const r = L.loadResult();
  const stage = { now: '준비' };
  const opened = []; // 3차 Q-3 — [이름, 세션] 연 순서대로
  let code = 0;
  // ⬛ [낡은 흐름 · 1~2차] catch 안에서 바로 process.exit(1) — 3차: 로그아웃(finally)을 거친 뒤 끝낸다.
  try {
    const ids = {
      parent: process.env.UI_LOGIN_PARENT || 'ci-parent',
      ga: process.env.UI_LOGIN_GA || 'ci-ga',
      na: process.env.UI_LOGIN_NA || 'ci-na',
    };
    stage.now = '부모 로그인';
    const p = await login('PARENT', ids.parent, process.env.UI_PW_PARENT);
    opened.push(['부모', p]);
    if (p.awaiting) throw new SeedError('부모 기기 승인 대기 — §8 ③ (첫 런 실측 대상)');
    if (p.terms) { stage.now = '부모 약관'; await agreeTerms('PARENT', p); }

    for (const [who, loginId, pw, name] of [
      ['GA', ids.ga, process.env.UI_PW_GA, '시험 가'],
      ['NA', ids.na, process.env.UI_PW_NA, '시험 나'],
    ]) {
      stage.now = `자식 ${who} 만들기`;
      const res = await call('POST', '/api/users', {
        token: p.token, deviceId: p.deviceId,
        body: { email: loginId, password: pw, userName: name, empName: name, role: 'User', confirmDifferentPerson: true },
      });
      if (res.status !== 201 && res.status !== 200) {
        throw new SeedError(`자식 ${who} status=${res.status} code=${L.sanitize((res.json || {}).code || (res.json || {}).message || '')}`);
      }
    }

    stage.now = '가 로그인';
    const ga = await login('GA', ids.ga, process.env.UI_PW_GA);
    opened.push(['가', ga]);
    if (ga.awaiting) throw new SeedError('가 기기 승인 대기 — §8 ③ (첫 런 실측 대상 · 부모 승인 길 필요)');
    if (ga.terms) { stage.now = '가 약관'; await agreeTerms('GA', ga); }

    stage.now = '대화 상대 찾기';
    const emps = await call('GET', '/api/chat/employees', { token: ga.token, deviceId: ga.deviceId });
    if (emps.status !== 200 || !Array.isArray(emps.json)) throw new SeedError(`chat/employees status=${emps.status}`);
    const na = emps.json.find((e) => (e.empName || e.EmpName) === '시험 나');
    if (!na) throw new SeedError(`chat/employees 에 「시험 나」 없음 (후보 ${emps.json.length}명)`);
    const naEmpId = na.employeeId || na.EmployeeId;

    stage.now = '방 A(1:1)';
    const a = await call('POST', '/api/chat/rooms', { token: ga.token, deviceId: ga.deviceId, body: { roomType: 'direct', employeeIds: [naEmpId] } });
    if (a.status !== 200 || !a.json) throw new SeedError(`방 A status=${a.status}`);
    stage.now = '방 B(단체)';
    const b = await call('POST', '/api/chat/rooms', { token: ga.token, deviceId: ga.deviceId, body: { roomType: 'group', roomName: '시험 B', employeeIds: [naEmpId] } });
    if (b.status !== 200 || !b.json) throw new SeedError(`방 B status=${b.status}`);
    const roomA = a.json.roomId || a.json.RoomId;
    const roomB = b.json.roomId || b.json.RoomId;
    if (!roomA || !roomB || roomA === roomB) throw new SeedError(`방 번호 이상 (A·B 같음 또는 빈 값)`);

    L.exportPlain('UI_ROOM_A', roomA);
    L.exportPlain('UI_ROOM_B', roomB);
    L.exportPlain('UI_LOGIN_GA', ids.ga);
    L.exportPlain('UI_LOGIN_NA', ids.na);
    L.rec(r, { id: 'H-S5b', what: '시드② 자식 2(가·나) + 방 2(A·B)', got: 'ok', pass: true, kind: 'DEVICE' });
    // ⬛ [낡은 줄 · 1~2차] L.saveResult(r); — 3차: finally 에서 로그아웃 줄까지 적은 뒤 한 번 저장.
  } catch (e) {
    const got = e instanceof SeedError ? e.message : `예외 ${e.name} (원문 생략)`;
    L.rec(r, { id: 'H-S5b', what: `시드② 단계=${stage.now}`, got, pass: false, kind: 'DEVICE' });
    // ⬛ [낡은 줄 · 1~2차] L.saveResult(r); process.exit(1);
    code = 1;
  } finally {
    // 3차 Q-3 — 연 세션을 모두 닫는다(성공·실패 무관). 결과 줄 하나 · 하나라도 200 이 아니면 [장치] 빨강.
    const outs = [];
    for (const [who, s] of opened) outs.push(await logout(who, s));
    //   시드가 끝까지 갔으면 연 세션은 반드시 2(부모·가) — 0·1 이면 빈 통과라 빨강. 앞에서 섰으면 연 만큼만 닫는다.
    const ok = outs.every((o) => o.ok) && (code === 1 || outs.length === 2);
    L.rec(r, { id: 'H-S5c', what: '시드② 가 연 부모·가 API 로그인 로그아웃(K 화면 로그인이 한 PC 규칙 409 에 안 걸리게)', got: outs.length ? outs.map((o) => o.got).join(' · ') : '연 세션 0', pass: ok, kind: 'DEVICE' });
    if (!ok) code = 1;
    L.saveResult(r);
  }
  process.exit(code);
}

// 3차 — 로컬 확인(가짜 서버 단독 실행)에서 require 로 불러 쓸 수 있게. CI 는 `node seed-children.js` 직접 실행.
// ⬛ [낡은 줄 · 1~2차] main();
if (require.main === module) main();
module.exports = { logout };
