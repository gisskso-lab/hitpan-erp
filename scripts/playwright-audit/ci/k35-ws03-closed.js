// K 본문 — W-S-03 서버 문 닫기 · 첫 로그인 · 휴대폰 화면 회귀 (W-S-03 고침 1 · [4] P2-1 · 개발명세서 docs/개발/erp/20261128_W-S-03_서버문닫기_온보딩모바일_개발명세서.md 「고침 1」)
//
// 🔴 이 파일의 규칙(k35-popup.js · 쿨타임 몸과 같은 꼴)
//   ① 기록은 kit35.recK 하나로만 — 키 = judge.js EXPECT_S3_ROWS(기대 키 7 · 전부 시험 줄). 밖 = 던짐.
//   ② 판정은 주소(문서 이동 기록) · 로그인 응답 칸(참거짓만) · 화면 글자 · 서버 응답 코드 · 시험 DB 행 수로만.
//      단언 전 실패(새 직원 만들기 · 로그인 · 첫 로그인 상태 재현 · 시간초과) = 그 키에 DEVICE 거짓.
//   ③ 그림을 찍지 않는다(N4 줄 없음) — 증거 = 결과표 결과 칸.
//   ④ 닫힌 두 서버 문(POST api/devices/mobile-token · mobile-register)의 상태 숫자 · 본문 꼴은 **기록만** 한다(단정 0 · §321 · W-S-01 405 선례).
//      판정 = 처리기 도달 0 — 시험 DB 의 등록 토큰 표 · 기기 표 행 변화 0 · 심어 둔 토큰이 안 쓰임 · 처리기 안내 글 0.
//   ⑤ 첫 로그인 상태는 시험 DB(CI 컨테이너 · 실물 아님)에서 새 직원의 마지막 로그인 칸을 비워 다시 만든다 — 약관은 미리 동의해 둔다
//      (약관 미동의 첫 로그인 → /terms 는 기존 장면 H-D2 약관 화면 길이 잰다 · 이 몸은 중복으로 만들지 않는다).
//   ⑥ 연 세션은 장면 끝 finally 에서 반드시 로그아웃(§255 · 계정 잠김 사고 선례) · 브라우저를 닫는다.
//   ⑦ 이 PC 에서 돌리지 않는다(이 PC DB = 실물) — CI 의 S8 에서만. 이름 = 다른 K 몸 뒤에 돈다(알파벳 순).
//   ⑧ 고침 3 — 알려진 결함 대장(judge.js KNOWN_DEFECT_ROWS · 첫 화면 2 장면) 꼴일 때만 결과 칸 머리에 표지 · 끝 코드는 판정기 면제 함수를 쓴다(endCode).
//   ⑨ 측정 M(11/28 · [1-V] 보충 1 · 검증팀 몫) — 진단은 로그 줄(K 닫힌문 dx* · 관찰 *)로만. 판정 식 · 기대 키 · 결과표 got · 알려진 결함 목록 손 0.
//      관찰 장면 O1 · T1 · D1 은 recK 를 부르지 않는다(기대 키 7 그대로 · 자체시험 K-C22k).
'use strict';

const crypto = require('crypto');
const { spawnSync } = require('child_process');
const L = require('./lib');
const K = require('./kit35');
const DC = require('./device-check'); // 머리 조건(require.main) 때문에 불러도 장치 본문은 안 돈다 — 내보냄만 쓴다
// 🔴 고침 3(작업지시서 20261128작_W-S-03_고침3_알려진결함대장 §4 ②) — 알려진 결함 목록 · 표지 · 면제 함수는 판정기 것 그대로(머리 조건 때문에 판정 본문은 안 돈다)
const J = require('./judge');

const env = process.env;
const BROWSER = 'chromium';
const ID = { parent: env.UI_LOGIN_PARENT || 'ci-parent', fresh: 'ci-s3new' };
const PW = { parent: env.UI_PW_PARENT || '' };
const FRESH_NAME = '시험 새직원';
// kit35 의 UA 와 같은 글자(kit35 가 내보내지 않아 옮겨 적음 · 시드 · K 몸 API 로그인과 같은 기기 꼴)
const API_UA = 'Mozilla/5.0 (X11; Linux x86_64) HitPanCiSeed/1';
const PHONE_UA = 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 HitPanCiPhone/1';
// 제품 글자(따로 적어 잠금) — 바뀌면 이 몸이 빨강이 되어 알린다
const NOTICE_TITLE = '이 화면은 정식 판에서 열립니다'; // BetaClosedRoutes.NoticeTitle
const WELCOME_OLD = '온보딩(Phase 2'; // Welcome.razor 옛 글 — 보이면 옛 환영 화면이 살아남
const PHONE_NOTICE = '이제 등록 없이 휴대폰에서 로그인하시면 됩니다.'; // MobileDeviceRegisterPage.razor 안내
const PHONE_OLD_ASK = '모바일 기기 등록을 하시겠습니까?'; // 옛 QR 등록 화면 물음 — 보이면 옛 화면이 살아남
const UPDATE_NOTICE = '「최신 버전 확인/업데이트」 화면으로 바로 이동합니다'; // LocalSwapUiText.LoginButtonNotice 일부
const UPDATE_BTN = /^최신버젼업데이트/; // Login.razor 수동 업데이트 단추(문구가 상태에 따라 꼬리만 바뀜)
const HOME_PATH = '/'; // LocalSwapUiText.PostLoginPath(false)
const UPDATE_PATH = '/data/update'; // LocalSwapUiText.UpdatePath = PostLoginPath(true)
const WELCOME_PATH = '/welcome'; // BetaClosedRoutes.Welcome
// 처리기가 돌았다는 표지(DeviceController 의 두 액션이 내는 응답 글자 · 칸 이름)
const HANDLER_WORDS = ['등록 정보가 올바르지 않습니다', '만료되었거나 이미 사용된 코드', 'qrImage', 'expiresInMinutes'];
// ⬛ [낡은 줄 · 고침 3] const S3_TOTAL_MS = 7 * 60 * 1000; // 이 몸 전체 상한(워크플로 S8 단계 상한 안)
// 🔴 측정 M(11/28 · [1-V] 보충 1 · 설계 로그인경합_첫화면403띠 §6) — 관찰 장면 셋(O1 · T1 · D1 · 각 150초 상한)이 판정 장면 뒤에 붙는다 → 14분.
const S3_TOTAL_MS = 14 * 60 * 1000; // 이 몸 전체 상한(워크플로 S8 단계 상한 안)
const TEXT_MS = 30000;
// 🔴 측정 M — 진단 줄 상한(판정 칸 · 기대 키 · 알려진 결함 목록 손 0 · 로그 줄만 · 결과표 got 손 0)
const MX_CON_MAX = 30; // 창 한 벌 · 구간 하나당 콘솔 error 줄 · 화면 예외 줄 각각
const MX_LINE = 1500; // 줄당 글자(소독 뒤 자름)
const MX_DOC_MAX = 30; // 문서 이동 기록
const MX_FX_MAX = 200; // 4xx · 5xx 한 건 한 줄(로그 넘침 막기만 · 넘친 수는 끝 줄에)
const OBS_SCENE_MS = 150000; // 관찰 장면 하나 상한
const OBS_ID = { terms: 'ci-s3terms' }; // 장면 T1 — 약관 미동의 새 직원(기존 준비와 같은 만들기 길)
const OBS_TERMS_NAME = '시험 약관미동의';
// 장면 D1 — 지문(device-fingerprint.js _envSeed = 화면 크기 · 언어 · 시간대 · UA 숫자 뺀 글자)이 앞 장면들과 다르게 → 새 기기 꼴
const D1_UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 HitPanCiNewDevice/1';

const key = (id, g, p) => ({ id, g, path: p, browser: BROWSER });
const KEY = {
  home: key('K35-S3-1', 'S3', 'first-home'),
  update: key('K35-S3-1', 'S3', 'first-update'),
  welcome: key('K35-S3-2', 'S3', 'direct-welcome'),
  phone: key('K35-S3-2', 'S3', 'phone-register'),
  gate: key('K35-S3-3', 'S3', 'gate-mainpc'),
  token: key('K35-S3-4', 'S3', 'mobile-token'),
  register: key('K35-S3-4', 'S3', 'mobile-register'),
};
const WHAT = {
  home: '새 직원 첫 로그인(로그인 응답 첫 로그인=참 · 약관 동의됨) → 첫 화면 / · 환영 화면(/welcome) 이동 0 · 본 화면 자리 잡음 · 흰 띠 0',
  update: '새 직원 첫 로그인 + [최신버젼업데이트] 누름 → 수동 업데이트 화면(/data/update)으로 이동 · 환영 화면 이동 0 · 흰 띠 0',
  welcome: '로그인한 채 주소창에 /welcome 직접 → 「이 화면은 정식 판에서 열립니다」 안내 · 옛 환영 글 0 · 주소 그대로 · 흰 띠 0',
  // ⬛ [낡은 줄 · 고침 1] phone: '휴대폰 화면 크기 · 로그인 없이 /m/device-register(옛 QR 주소) → 「등록 없이 로그인」 안내 · 옛 등록 물음 0 · 옛 등록 요청 0 · 흰 띠 0',
  //   🔴 고침 2(런 46118135957 · 개발명세서 「고침 2」 ③) — 설계 §4 「안내 + [로그인]」 은 화면 손 0 이다. 그 화면은 기본 틀 MainLayout
  //   (MainLayout.razor:2 [Authorize] · :106 ~ :108 NotAuthorized → RedirectToLogin)을 써서 로그인 없으면 /login 으로 간다(main 과 같음).
  //   ⇒ 로그인 없음 = /login 착지 · 로그인한 휴대폰 = 안내 — 둘 다 잰다(키 그대로 · 약하게 하지 않음).
  phone: '휴대폰 화면 크기 · /m/device-register(옛 QR 주소) — 로그인 없음 → /login 착지(MainLayout 문 · 화면 손 0) · 로그인한 휴대폰 → 「등록 없이 로그인」 안내 · 두 번 다 옛 등록 물음 0 · 옛 등록 요청 0 · 흰 띠 0',
  gate: '기기 관문 · 메인PC 확인 길 그대로 — 새 직원 브라우저 세션으로 GET api/devices/gate-status · is-main-pc 둘 다 200',
  token: '닫힌 문 POST api/devices/mobile-token(대표 세션) → 처리기 도달 0(등록 토큰 표 · 기기 표 행 변화 0 · 처리기 글 0) · 상태 숫자 · 본문 꼴은 기록만',
  register: '닫힌 문 POST api/devices/mobile-register(대표 세션 · 시험 DB 에 심은 쓸 수 있는 토큰) → 처리기 도달 0(심은 토큰 안 쓰임 · 기기 표 행 변화 0 · 처리기 글 0) · 로그인 없는 호출 숫자도 기록만',
};

// ── 시험 DB(CI 컨테이너 · 127.0.0.1) — 장치 S2 에서 깐 mysql 클라이언트 · 비번은 환경으로만 넘김(인자 · 출력 0) ──
//   표 · 칸은 출하 DDL(installer/hitpan_db_clean.sql — users :4380 · device_register_tokens :3884 · tenant_devices :4050)에서 확인(#13).
const SAFE_ID = /^[A-Za-z0-9-]{1,64}$/;
const SAFE_HEX = /^[0-9a-f]{64}$/;
function idOk(v, what) {
  const s = String(v ?? '');
  if (!SAFE_ID.test(s)) throw new K.KitStepError(`${what} 꼴 이상(질의에 넣지 않음)`);
  return s;
}
function dbq(sqlText) {
  const dbEnv = { ...process.env, MYSQL_PWD: env.UI_DB_PASSWORD || env.DB_PASSWORD || '' };
  const args = ['-h', env.DB_HOST || '127.0.0.1', '-P', env.DB_PORT || '13306', '-u', env.DB_USER || 'root',
    '-N', '-B', '--default-character-set=utf8mb4', env.DB_NAME || 'hitpan_ui', '-e', sqlText];
  const run = spawnSync('mysql', args, { env: dbEnv, encoding: 'utf8', timeout: 30000 });
  if (run.error) throw new K.KitStepError(`시험 DB 질의 실행 실패 ${run.error.code || L.errorName(run.error)}`);
  if (run.status !== 0) throw new K.KitStepError(`시험 DB 질의 실패 exit=${run.status} · ${L.sanitize(String(run.stderr || '').trim(), 120)}`);
  return String(run.stdout || '').split(/\r?\n/).filter((x) => x.length > 0).map((x) => x.split('\t'));
}
function dbCount(sqlText) {
  const rows = dbq(sqlText);
  const n = rows.length > 0 ? Number(rows[rows.length - 1][0]) : NaN;
  if (!Number.isFinite(n)) throw new K.KitStepError('시험 DB 셈 결과 꼴 이상');
  return n;
}
function counts(tid) {
  const t = idOk(tid, '회사 번호');
  return {
    tokens: dbCount(`SELECT COUNT(*) FROM device_register_tokens WHERE tenant_id = '${t}';`),
    devices: dbCount(`SELECT COUNT(*) FROM tenant_devices WHERE tenant_id = '${t}';`),
  };
}
// 첫 로그인 상태 다시 만들기 — 서버 판정(AuthService: 마지막 로그인 칸이 비면 첫 로그인)과 같은 칸만 비운다.
function resetFirstLogin(st) {
  const uid = idOk(st.freshUid, '새 직원 번호');
  const t = idOk(st.tid, '회사 번호');
  const n = dbCount(`UPDATE users SET last_login_at = NULL WHERE user_id = '${uid}' AND tenant_id = '${t}'; SELECT ROW_COUNT();`);
  if (n !== 1) throw new K.KitStepError(`첫 로그인 상태 재현 — 바뀐 행 ${n}(1 이어야 함)`);
}

// 출입증 가운데 토막에서 클레임 하나(값은 찍지 않는다 · 질의에만 쓴다 · 쿨타임 몸과 같은 꼴)
function jwtClaim(token, name) {
  try {
    const mid = String(token || '').split('.')[1] || '';
    const json = Buffer.from(mid.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8');
    const v = JSON.parse(json)[name];
    return typeof v === 'string' && v ? v : null;
  } catch (e) {
    console.log(`K 닫힌문 출입증 클레임 읽기 실패 (${L.errorName(e)})`);
    return null;
  }
}

// 본문 꼴까지 남기는 요청(kit35.apiCall 은 JSON 아닌 본문을 버린다) — 같은 머리(기기 꼴 UA · 출입증 · 기기 번호)
async function rawCall(method, p, sess, body) {
  const headers = { 'Content-Type': 'application/json', 'User-Agent': API_UA };
  if (sess && sess.token) headers.Authorization = `Bearer ${sess.token}`;
  if (sess && sess.deviceId) headers['X-HitPan-Device-Id'] = sess.deviceId;
  const res = await fetch(`${K.BASE}${p}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await res.text();
  return {
    status: res.status,
    contentType: res.headers.get('content-type') || null,
    allow: res.headers.get('allow') || null,
    bodyLength: text.length,
    bodyHead: text ? L.sanitize(text.replace(/\s+/g, ' ').trim(), 60) : '',
    handlerWords: HANDLER_WORDS.filter((w) => text.includes(w)).length,
  };
}

// ── 줄 쓰기 ──
// ⬛ [낡은 몸 · 고침 2] function rec(st, name, res) {
// ⬛   K.recK(st.r, { key: KEY[name], what: WHAT[name], got: res.got, pass: res.pass, kind: 'TEST' });
// ⬛ }
// 🔴 고침 3 — res.known 이 참이고(장면이 알려진 꼴을 따로 계산 · 판정 식은 그대로) 그 키가 판정기 대장에 있으면
//   결과 칸 머리에 J.KNOWN_WHY + 대장 이름. 결과 칸은 L.rec 가 글자로 저장한다(객체는 JSON 글자) → 머리 글자를 붙여 글자로 넘긴다.
//   아니면 머리 없음 → 판정기가 종전대로 빨강.
function knownName(name) {
  const k = KEY[name];
  const w = J.KNOWN_DEFECT_ROWS.find((x) => x[0] === k.id && x[1] === k.g && x[2] === k.path && x[3] === k.browser);
  return w ? w[4] : null;
}
function rec(st, name, res) {
  const kn = res.pass === false && res.known === true ? knownName(name) : null;
  const got = kn ? `${J.KNOWN_WHY}${kn} · ${typeof res.got === 'string' ? res.got : JSON.stringify(res.got)}` : res.got;
  K.recK(st.r, { key: KEY[name], what: WHAT[name], got, pass: res.pass, kind: 'TEST' });
}
// 🔴 고침 3 — 알려진 꼴(장면 판정 식이 거짓일 때만 따로 계산 · W-S-03 자기 조건이 참일 때만 참)
//   첫 화면: 본 화면 · 주소 / · 이동 기록에 / · /welcome 이동 0 은 모두 참이고 흰 띠만 참(선행검증서 §2).
function homeKnown(s, navs, band) {
  return s.where === 'main' && s.path === HOME_PATH && navs.includes(HOME_PATH) && !navs.some(isWelcome) && band === true;
}
//   업데이트: /welcome 이동 0 · 문서 이동 기록에 /data/update 시도 있음(갈 곳 계산은 맞았다) · 실패 사유는
//   「/data/update 끊김(실패) 뒤 / 」 또는 흰 띠뿐(선행검증서 §1 · §2). 흰 띠 읽기 실패(null) = 꼴 아님.
function updateKnown(navs, docNavs, band) {
  if (navs.some(isWelcome)) return false;
  const ui = docNavs.findIndex((d) => d.startsWith(`${UPDATE_PATH}=`));
  if (ui < 0) return false;
  if (band !== true && band !== false) return false;
  const navOk = navs.includes(UPDATE_PATH);
  const cutThenHome = !navOk && docNavs[ui].startsWith(`${UPDATE_PATH}=실패`)
    && docNavs.slice(ui + 1).some((d) => d.startsWith(`${HOME_PATH}=`)) && navs.includes(HOME_PATH);
  return (navOk || cutThenHome) && !(navOk && band === false);
}
function dev(st, name, step, e) {
  const why = e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${L.errorName(e)}`;
  console.log(`K 닫힌문 ${name} 단언 전 실패 — 단계=${step.now} · ${why}`);
  if (!K.isWritten(st.r, KEY[name])) {
    K.recK(st.r, { key: KEY[name], what: WHAT[name], got: `단언 전 실패 — 단계=${step.now} · ${why}`, pass: false, kind: 'DEVICE', err: e instanceof K.KitStepError ? null : e });
  }
}

// ── API 세션(연 것은 st.apiSess 에 적어 끝에 반드시 놓는다) ──
async function apiOpen(st, who, loginId, password) {
  const s = await K.apiLogin(who, loginId, password);
  st.apiSess.push(s);
  return s;
}
async function apiClose(st, s) {
  const i = st.apiSess.indexOf(s);
  if (i < 0) return;
  st.apiSess.splice(i, 1);
  const lo = await K.apiLogout(s);
  console.log(`K 닫힌문 API 로그아웃 ${lo.got}`);
  if (!lo.ok) st.logoutFail += 1;
}

// ── 준비: 새 직원 만들기(대표 · 화면과 같은 POST api/users) · 약관 미리 동의 ──
async function prep(st) {
  const step = { now: '대표 API 로그인' };
  const p = await apiOpen(st, 'PARENT', ID.parent, PW.parent);
  try {
    st.tid = idOk(jwtClaim(p.token, 'tenant_id'), '회사 번호');
    st.parentUid = idOk(jwtClaim(p.token, 'user_id'), '대표 번호');
    step.now = '새 직원 만들기';
    st.freshPw = `Ui#${crypto.randomBytes(16).toString('hex')}`;
    L.maskAndRegister('PW_S3_FRESH', st.freshPw);
    const res = await K.apiCall('POST', '/api/users', {
      sess: p,
      body: { email: ID.fresh, password: st.freshPw, userName: FRESH_NAME, empName: FRESH_NAME, role: 'User', confirmDifferentPerson: true },
    });
    if (res.status !== 201 && res.status !== 200) {
      const j = res.json || {};
      throw new K.KitStepError(`새 직원 만들기 status=${res.status} code=${L.sanitize(String(j.code || j.message || ''), 80)}`);
    }
  } finally {
    await apiClose(st, p);
  }
  step.now = '새 직원 API 로그인 · 약관 동의';
  const f = await apiOpen(st, 'S3FRESH', ID.fresh, st.freshPw);
  try {
    st.freshUid = idOk(jwtClaim(f.token, 'user_id'), '새 직원 번호');
    const ag = await K.apiCall('POST', '/api/auth/terms/agree', {
      sess: f,
      body: { agreeService: true, agreePrivacy: true, agreeSubscription: true, agreeDataOwnership: true, agreeMarketing: false, termsVersion: 'v2.0.0' },
    });
    if (ag.status < 200 || ag.status >= 300) throw new K.KitStepError(`새 직원 약관 동의 status=${ag.status}`);
  } finally {
    await apiClose(st, f);
  }
}

// ── 브라우저 한 벌(컨텍스트) — 외부 끊음 · 토큰 가림 · 막기/녹음기 초기 스크립트(앞 몸과 같은 꼴) ──
async function openCtx(st, who, extra = {}) {
  const tally = { continue: 0, abort: 0 };
  const context = await st.br.newContext({ baseURL: K.BASE, ...extra });
  await K.blockExternal(context, st.r, tally);
  const tok = { access: null, deviceId: null, masked: 0 };
  K.watchTokens(context, tok);
  await context.addInitScript(K.INIT_SCRIPT);
  const kb = {
    browser: BROWSER, who, context, page: null, tok, tally, closed: false,
    br: { seen: 0, closed: 0, errs: 0, lastErr: null }, consoleLines: 0, consoleBuf: [],
    navs: [], login: { seen: 0, redirect: null, terms: null, deviceNew: null, status: null, err: null }, oldReq: 0,
    // 🔴 고침 2 — 실패 때 got 에 싣는 진단(값 · 토큰 · 그림 0 · 주소는 쿼리 뗀 경로만 · 글자는 싣는 그 시점 소독)
    dx: { fails: [], failCount: 0, docs: [], unhandled: [] },
  };
  K.watchConsole(context, kb);
  st.kbs.push(kb);
  kb.page = await context.newPage();
  const page = kb.page;
  page.on('framenavigated', (fr) => {
    if (fr !== page.mainFrame()) return;
    const p = K.pathOf(fr.url()).split('?')[0];
    if (kb.navs.length < 40) kb.navs.push(p);
  });
  // 로그인 응답 — 참거짓 칸 둘만 읽는다(토큰은 watchTokens 가 가림 · 여기서 찍지 않음)
  page.on('response', async (res) => {
    if (!/\/api\/auth\/login(\?|$)/i.test(res.url()) || res.request().method() !== 'POST') return;
    try {
      const j = await res.json();
      kb.login.status = res.status();
      kb.login.redirect = !!(j && (j.redirectToWelcome ?? j.RedirectToWelcome));
      kb.login.terms = !!(j && (j.requiresTermsConsent ?? j.RequiresTermsConsent));
      // 🔴 고침 2 ② — 새 기기 알림 창(Login.razor :427 ~ :445)이 뜨는 로그인인가(참거짓만)
      kb.login.deviceNew = !!(j && (j.deviceNewlyRegistered ?? j.DeviceNewlyRegistered));
      kb.login.seen += 1;
    } catch (e) {
      kb.login.err = L.errorName(e);
    }
  });
  page.on('request', (req) => {
    if (/\/api\/devices\/mobile-(register|token)(\?|$)/i.test(req.url())) kb.oldReq += 1;
    // 🔴 고침 2 ② — 주 프레임 문서 이동 요청(경로만 · 끝 = 완료 상태 / 실패 사유). 서비스 워커 요청은 frame() 이 던진다 → 센 것 아님.
    let mainDoc = false;
    try {
      mainDoc = req.isNavigationRequest() && req.frame() === page.mainFrame();
    } catch (e) {
      mainDoc = false;
    }
    if (mainDoc && kb.dx.docs.length < 12) kb.dx.docs.push({ req, path: pathOnly(req.url()), end: '대기' });
  });
  page.on('requestfinished', (req) => {
    const x = kb.dx.docs.find((d) => d.req === req);
    if (!x) return;
    req.response().then((r) => { x.end = r ? String(r.status()) : '응답 없음'; }, (e) => { x.end = `응답 읽기 실패 ${L.errorName(e)}`; });
  });
  page.on('requestfailed', (req) => {
    const x = kb.dx.docs.find((d) => d.req === req);
    if (!x) return;
    const f = req.failure();
    x.end = `실패 ${L.sanitize(f && f.errorText ? f.errorText : '-', 40)}`;
  });
  // 🔴 고침 2 ① — 4xx · 5xx 응답(방법 · 쿼리 뗀 경로 · 숫자). 외부 차단(ERR_BLOCKED_BY_CLIENT)은 응답이 아니라 여기 안 옴.
  page.on('response', (res) => {
    const s = res.status();
    if (s < 400) return;
    kb.dx.failCount += 1;
    if (kb.dx.fails.length < 15) kb.dx.fails.push(`${s} ${res.request().method()} ${pathOnly(res.url())}`);
  });
  // 🔴 고침 2 ① — 흰 띠를 띄우는 Blazor 처리 안 된 예외 글(kit35.watchConsole 은 20줄 상한이라 런 46118135957 에서 잘렸다).
  //   원문은 메모리에만 · got 에 실을 때 K.consoleLine(등록 목록 소독 + 제어문자)으로 다시 거른다.
  page.on('console', (msg) => {
    if (msg.type() !== 'error' || kb.dx.unhandled.length >= 6) return;
    const t = String(msg.text());
    if (/unhandled|crit:|exception/i.test(t)) kb.dx.unhandled.push(t);
  });
  page.on('pageerror', (e) => {
    if (kb.dx.unhandled.length < 6) kb.dx.unhandled.push(`화면 예외 ${L.errorName(e)}`);
  });
  mxWatch(kb);
  return kb;
}

// ── 🔴 측정 M 진단(로그 줄만 · 판정 · got 손 0) ──
//   받을 때 = 원문을 메모리에만 쌓음 · 찍을 때 = mxText(쿼리 뗌 · 출입증 꼴 가림 · K.consoleLine 소독 그대로)로 그 시점 등록 목록 소독.
//   예외 = 4xx · 5xx 는 받는 즉시 한 줄(경로 = 쿼리 뗀 pathname · 소독) — 프로세스가 죽어도 남게.
function mxNew() {
  return { t0: Date.now(), con: [], conCount: 0, firstConAt: null, perr: [], perrCount: 0, docs: [], fxCount: 0, fxPrinted: 0 };
}
function mxReset(kb) {
  kb.mx = mxNew();
}
const mxMs = (kb) => Date.now() - kb.mx.t0;
function mxText(t, cap = MX_LINE) {
  const s = String(t ?? '')
    // 쿼리 · 조각은 떼되 stack 의 「:줄:칸」 꼬리는 남긴다(흰 띠 부품 파일:줄이 이 측정의 산출 · 자체 대조에서 꼬리가 먹히던 꼴 고침)
    .replace(/(https?:\/\/[^\s?#"'<>()]*)[?#][^\s"'<>()]*?((?::\d+){1,2})?(?=[\s"'<>()]|$)/gi, (_, a, lc) => `${a}?(쿼리 뗌)${lc || ''}`)
    .replace(/\beyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]+(\.[A-Za-z0-9_-]*)?/g, '(출입증 꼴 가림)');
  return K.consoleLine(s, cap);
}
function mxWatch(kb) {
  kb.mx = mxNew();
  const page = kb.page;
  page.on('console', (msg) => {
    if (msg.type() !== 'error') return;
    const m = kb.mx;
    m.conCount += 1;
    if (m.firstConAt === null) {
      m.firstConAt = mxMs(kb);
      console.log(`K 닫힌문 dxfirst ${kb.who} 첫 콘솔 error +${m.firstConAt}ms`);
    }
    if (m.con.length < MX_CON_MAX) m.con.push({ at: mxMs(kb), text: String(msg.text()) });
  });
  page.on('pageerror', (e) => {
    const m = kb.mx;
    m.perrCount += 1;
    if (m.perr.length < MX_CON_MAX) {
      m.perr.push({ at: mxMs(kb), name: e && e.name ? String(e.name) : 'Error', message: String((e && e.message) || ''), stack: String((e && e.stack) || '') });
    }
  });
  page.on('request', (req) => {
    let mainDoc = false;
    try {
      mainDoc = req.isNavigationRequest() && req.frame() === page.mainFrame();
    } catch (e) {
      mainDoc = false;
    }
    if (mainDoc && kb.mx.docs.length < MX_DOC_MAX) kb.mx.docs.push({ req, path: pathOnly(req.url()), at: mxMs(kb), endAt: null, end: '대기' });
  });
  page.on('requestfinished', (req) => {
    const x = kb.mx.docs.find((d) => d.req === req);
    if (!x) return;
    x.endAt = mxMs(kb);
    req.response().then((r) => { x.end = r ? String(r.status()) : '응답 없음'; }, (e) => { x.end = `응답 읽기 실패 ${L.errorName(e)}`; });
  });
  page.on('requestfailed', (req) => {
    const x = kb.mx.docs.find((d) => d.req === req);
    if (!x) return;
    x.endAt = mxMs(kb);
    const f = req.failure();
    x.end = `실패 ${L.sanitize(f && f.errorText ? f.errorText : '-', 40)}`;
  });
  page.on('response', (res) => {
    const s = res.status();
    if (s < 400) return;
    const m = kb.mx;
    m.fxCount += 1;
    if (m.fxPrinted >= MX_FX_MAX) return;
    m.fxPrinted += 1;
    console.log(`K 닫힌문 dx4xx ${s} ${res.request().method()} ${L.sanitize(pathOnly(res.url()), 200)} · ${kb.who} · +${mxMs(kb)}ms`);
  });
}
// 구간 끝에 쌓은 것을 찍고 비운다(같은 줄 두 번 안 찍음)
function mxFlush(kb, label) {
  const m = kb.mx;
  if (!m) return;
  const tag = `${kb.who}${label ? ` ${label}` : ''}`;
  console.log(`K 닫힌문 dxsum ${tag} · 4xx·5xx ${m.fxCount}건(찍음 ${m.fxPrinted}) · 콘솔 error ${m.conCount}건(찍음 ${m.con.length}) · 화면 예외 ${m.perrCount}건(찍음 ${m.perr.length}) · 첫 콘솔 error ${m.firstConAt === null ? '없음' : `+${m.firstConAt}ms`}`);
  for (const d of m.docs) console.log(`K 닫힌문 dxdoc ${tag} ${L.sanitize(d.path, 200)} 시작 +${d.at}ms 끝 ${d.endAt === null ? '-' : `+${d.endAt}ms`} ${d.end}`);
  m.con.forEach((x, i) => console.log(`K 닫힌문 dxcon ${tag} #${i + 1} +${x.at}ms ${mxText(x.text)}`));
  m.perr.forEach((x, i) => console.log(`K 닫힌문 dxperr ${tag} #${i + 1} +${x.at}ms 이름=${L.errorName({ name: x.name })} ${mxText(`글=${x.message} stack=${x.stack}`)}`));
  m.con = []; m.perr = []; m.docs = [];
}
// 화면 한 번 보기 — 주소 · 흰 띠(보임 · 안 글) · 보이는 스낵바 글 전부(읽기만)
async function mxSnap(kb, label) {
  const v = await K.withTimeout(kb.page.evaluate(() => {
    const flat = (e) => String((e && (e.innerText || e.textContent)) || '').replace(/\s+/g, ' ').trim();
    const band = document.getElementById('blazor-error-ui');
    const shown = !!band && getComputedStyle(band).display !== 'none';
    const snacks = Array.from(document.querySelectorAll('.mud-snackbar')).map(flat).filter((x) => x.length > 0).slice(0, 10);
    return { path: location.pathname, shown, bandText: band ? flat(band) : null, snacks };
  }), 10000, `화면 보기 ${label}`).catch((e) => ({ err: e instanceof K.KitStepError ? L.sanitize(e.message, 80) : L.errorName(e) }));
  if (v.err) {
    console.log(`K 닫힌문 dxsnap ${kb.who} ${label} 읽기 실패 ${v.err}`);
    return;
  }
  const snacks = v.snacks.length === 0 ? '없음' : v.snacks.map((x, i) => `[${i + 1}] ${mxText(x, 300)}`).join(' ');
  console.log(`K 닫힌문 dxsnap ${kb.who} ${label} 주소=${L.sanitize(v.path, 120)} · 흰 띠 ${v.shown ? '보임' : '안 보임'} · 띠 글=${v.bandText === null ? '(띠 없음)' : mxText(v.bandText, 300)} · 스낵바 ${v.snacks.length}개 ${snacks}`);
}
// 착지 뒤 3초 · 10초 두 번(배경 · 절대 던지지 않음 — 부른 쪽이 다음 장면 전 await)
function landSnaps(kb, landAt = Date.now()) {
  return (async () => {
    try {
      await K.sleep(Math.max(0, landAt + 3000 - Date.now()));
      await mxSnap(kb, '착지+3초');
      await K.sleep(Math.max(0, landAt + 10000 - Date.now()));
      await mxSnap(kb, '착지+10초');
    } catch (e) {
      console.log(`K 닫힌문 dxsnap ${kb.who} 섰음 ${L.errorName(e)}`);
    }
  })();
}
// 쿼리 · 조각을 뗀 경로(토큰 · 열쇠 꼴 값이 쿼리로 실려도 got 에 안 들어감)
function pathOnly(u) {
  try {
    return new URL(u).pathname;
  } catch (e) {
    return `(주소 해석 실패 ${L.errorName(e)})`;
  }
}
// 실패 때만 got 에 싣는 진단 묶음(그림 · 비번 · 토큰 0)
function diag(kb, extra = {}) {
  return {
    deviceNew: kb.login.deviceNew,
    docNavs: kb.dx.docs.map((d) => `${d.path}=${d.end}`),
    fails4xx5xx: kb.dx.failCount,
    fails: [...kb.dx.fails],
    unhandled: kb.dx.unhandled.map((t) => K.consoleLine(t, 200)),
    ...extra,
  };
}
async function closeKb(st, kb) {
  if (kb.closed) return;
  kb.closed = true;
  K.flushConsole(kb);
  mxFlush(kb, '끝'); // 🔴 측정 M — 남은 진단 찍기(로그아웃 · 닫기 전)
  if (kb.tok.access) {
    const lo = await K.apiLogout({ who: kb.who, token: kb.tok.access, deviceId: kb.tok.deviceId });
    console.log(`K 닫힌문 ${kb.who} 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  }
  await kb.context.close().catch((e) => console.log(`K 닫힌문 컨텍스트 닫기 실패 ${L.errorName(e)}`));
}
// 흰 띠(화면 아래 오류 띠 #blazor-error-ui) 보임 여부
async function errBand(page) {
  return page.evaluate(() => {
    const e = document.getElementById('blazor-error-ui');
    return !!e && getComputedStyle(e).display !== 'none';
  }).catch((e) => {
    console.log(`K 닫힌문 흰 띠 읽기 실패 ${L.errorName(e)}`);
    return null;
  });
}
async function seen(page, text, ms = TEXT_MS) {
  return page.getByText(text, { exact: false }).first().waitFor({ state: 'visible', timeout: ms }).then(() => true, () => false);
}
const isWelcome = (p) => String(p || '').toLowerCase().replace(/\/+$/, '') === WELCOME_PATH;

// 화면 로그인 — kit35.screenLogin 과 같은 동작 + [최신버젼업데이트] 를 고를 수 있게
async function loginScreen(kb, loginId, password, { update = false } = {}) {
  const page = kb.page;
  await page.goto(`${K.BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  await page.getByLabel('아이디', { exact: true }).fill(loginId, { timeout: 60000 });
  await page.getByLabel('비밀번호', { exact: true }).fill(password);
  await page.getByLabel('비밀번호', { exact: true }).press('Tab');
  if (update) {
    await page.getByRole('button', { name: UPDATE_BTN }).first().click({ timeout: 60000 });
    if (!(await seen(page, UPDATE_NOTICE, 10000))) throw new K.KitStepError('[최신버젼업데이트] 누름 안내가 안 뜸(단추가 안 눌림)');
  }
  kb.navs.length = 0; // 로그인 누름 뒤 이동만 센다
  // 🔴 고침 2 — 진단도 로그인 누름 뒤만 센다
  kb.dx.docs.length = 0; kb.dx.fails.length = 0; kb.dx.failCount = 0; kb.dx.unhandled.length = 0;
  mxFlush(kb, '로그인 누름 전'); // 🔴 측정 M — 누름 전 것은 따로 찍고 시각 0 = 누름 직전
  mxReset(kb);
  console.log(`K 닫힌문 dxclick ${kb.who} 로그인 누름 +0ms${update ? ' · [최신버젼업데이트] 고름' : ''}`);
  await page.getByRole('button', { name: '로그인', exact: true }).click();
  const note = { closed: 0, errs: 0 };
  const s = await DC.settleScreen(page, 90000, note);
  // 🔴 측정 M — 착지 시각(자리 잡기 끝) · 3초 · 10초 보기의 0점
  kb.mx.landAt = Date.now();
  console.log(`K 닫힌문 dxland ${kb.who} 착지 +${mxMs(kb)}ms ${s.where}@${L.sanitize(s.path, 120)} · 새 기기 창 닫음 ${note.closed}`);
  const end = Date.now() + 10000;
  while (kb.login.seen === 0 && Date.now() < end) await K.sleep(250);
  await K.waitMasked(kb.tok);
  // ⬛ [낡은 줄 · 고침 1] return s;  — 🔴 고침 2 ② 새 기기 알림 창을 장치가 닫은 횟수(note)를 같이 돌려준다(진단)
  return { ...s, noticeClosed: note.closed, noticeCloseErrs: note.errs };
}

// ── 장면 1 · 2 · 3: 첫 로그인 → 첫 화면 · 직접 /welcome · 기기 관문 ──
async function sceneHome(st) {
  const step = { now: '첫 로그인 상태 재현' };
  let kb = null;
  let snaps = null; // 🔴 측정 M — 착지 3초 · 10초 보기(배경 · 판정 줄 시각 손 0 · 장면 2 전에 기다림)
  try {
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    resetFirstLogin(st);
    step.now = '브라우저 열기 · 화면 로그인';
    kb = await openCtx(st, 'S3HOME');
    const s = await loginScreen(kb, ID.fresh, st.freshPw);
    snaps = landSnaps(kb, kb.mx.landAt);
    if (kb.login.seen === 0 || kb.login.redirect !== true || kb.login.terms !== false) {
      throw new K.KitStepError(`첫 로그인 상태 아님(로그인 응답 ${kb.login.seen}회 · 첫 로그인=${kb.login.redirect} · 약관 필요=${kb.login.terms} · status=${kb.login.status})`);
    }
    await DC.closeBriefing(kb.page, kb.br, 3000);
    const band = await errBand(kb.page);
    const navs = [...kb.navs];
    const pass = s.where === 'main' && s.path === HOME_PATH && navs.includes(HOME_PATH) && !navs.some(isWelcome) && band === false;
    const got = { where: s.where, path: s.path, navs, loginRedirectToWelcome: kb.login.redirect, loginTerms: kb.login.terms, errBand: band };
    // ⬛ [낡은 줄 · 고침 1] rec(st, 'home', { pass, got });  — 🔴 고침 2 실패 때만 진단을 싣는다(판정 칸 그대로)
    // ⬛ [낡은 줄 · 고침 2] rec(st, 'home', { pass, got: pass ? got : { ...got, dx: diag(kb, { noticeClosed: s.noticeClosed, noticeCloseErrs: s.noticeCloseErrs }) } });
    // 🔴 고침 3 — 판정 식 그대로 · 거짓일 때만 알려진 꼴 계산(머리 글자는 rec 가 붙임)
    const known = !pass && homeKnown(s, navs, band);
    rec(st, 'home', { pass, known, got: pass ? got : { ...got, dx: diag(kb, { noticeClosed: s.noticeClosed, noticeCloseErrs: s.noticeCloseErrs }) } });
  } catch (e) {
    dev(st, 'home', step, e);
  }
  // 🔴 측정 M — 첫 화면 구간 진단을 끊어 찍고 장면 2 부터 새로 센다
  if (snaps) await snaps;
  if (kb) { mxFlush(kb, 'first-home'); mxReset(kb); }
  // 장면 2 — 같은 세션 그대로 주소창에 /welcome
  const step2 = { now: '직접 /welcome' };
  try {
    if (!kb || !kb.tok.access) throw new K.KitStepError(`로그인 세션 없음 — 장면 1 이 앞에서 섰다(${step.now})`);
    kb.navs.length = 0;
    await kb.page.goto(`${K.BASE}${WELCOME_PATH}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    const notice = await seen(kb.page, NOTICE_TITLE);
    await DC.closeBriefing(kb.page, kb.br, 3000);
    const old = await kb.page.getByText(WELCOME_OLD, { exact: false }).count();
    const at = K.pathOf(kb.page.url()).split('?')[0];
    const band = await errBand(kb.page);
    rec(st, 'welcome', { pass: notice && old === 0 && isWelcome(at) && band === false, got: { notice, oldWelcomeText: old, path: at, navs: [...kb.navs], errBand: band } });
  } catch (e) {
    dev(st, 'welcome', step2, e);
  }
  // 장면 3 — 기기 관문 · 메인PC 확인 길(같은 세션 출입증)
  const step3 = { now: '기기 관문 · 메인PC 확인 요청' };
  try {
    if (!kb || !kb.tok.access) throw new K.KitStepError('로그인 세션 없음 — 장면 1 이 앞에서 섰다');
    const sess = { who: kb.who, token: kb.tok.access, deviceId: kb.tok.deviceId };
    const g = await K.apiCall('GET', '/api/devices/gate-status', { sess });
    const m = await K.apiCall('GET', '/api/devices/is-main-pc', { sess });
    const isMain = m.json && typeof m.json === 'object' ? (m.json.isMainPc ?? m.json.IsMainPc ?? null) : null;
    const pass = g.status === 200 && !!g.json && m.status === 200 && typeof isMain === 'boolean';
    rec(st, 'gate', { pass, got: { gateStatus: g.status, gateBody: g.json ? 'JSON' : '없음', isMainPcStatus: m.status, isMainPc: isMain } });
  } catch (e) {
    dev(st, 'gate', step3, e);
  } finally {
    if (kb) await closeKb(st, kb);
  }
}

// ── 장면 4: 첫 로그인 + [최신버젼업데이트] → 수동 업데이트 화면 ──
async function sceneUpdate(st) {
  const step = { now: '첫 로그인 상태 재현' };
  let kb = null;
  let snaps = null; // 🔴 측정 M
  try {
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    resetFirstLogin(st);
    step.now = '브라우저 열기 · [최신버젼업데이트] · 화면 로그인';
    kb = await openCtx(st, 'S3UPD');
    const s = await loginScreen(kb, ID.fresh, st.freshPw, { update: true });
    snaps = landSnaps(kb, kb.mx.landAt); // 🔴 측정 M
    if (kb.login.seen === 0 || kb.login.redirect !== true || kb.login.terms !== false) {
      throw new K.KitStepError(`첫 로그인 상태 아님(로그인 응답 ${kb.login.seen}회 · 첫 로그인=${kb.login.redirect} · 약관 필요=${kb.login.terms} · status=${kb.login.status})`);
    }
    const band = await errBand(kb.page);
    const navs = [...kb.navs];
    const pass = navs.includes(UPDATE_PATH) && !navs.some(isWelcome) && band === false;
    const got = { navs, settled: `${s.where}@${s.path}`, loginRedirectToWelcome: kb.login.redirect, errBand: band };
    // ⬛ [낡은 줄 · 고침 1] rec(st, 'update', { pass, got });  — 🔴 고침 2 실패 때만 진단을 싣는다(판정 칸 그대로)
    // ⬛ [낡은 줄 · 고침 2] rec(st, 'update', { pass, got: pass ? got : { ...got, dx: diag(kb, { noticeClosed: s.noticeClosed, noticeCloseErrs: s.noticeCloseErrs }) } });
    // 🔴 고침 3 — 판정 식 그대로 · 거짓일 때만 알려진 꼴 계산(문서 이동 기록 = diag 와 같은 글자 꼴)
    const known = !pass && updateKnown(navs, kb.dx.docs.map((d) => `${d.path}=${d.end}`), band);
    rec(st, 'update', { pass, known, got: pass ? got : { ...got, dx: diag(kb, { noticeClosed: s.noticeClosed, noticeCloseErrs: s.noticeCloseErrs }) } });
  } catch (e) {
    dev(st, 'update', step, e);
  } finally {
    // ⬛ [낡은 줄 · 고침 3] if (kb) await closeKb(st, kb);
    if (snaps) await snaps; // 🔴 측정 M — 절대 던지지 않는 약속
    if (kb) { mxFlush(kb, 'first-update'); await closeKb(st, kb); }
  }
}

// ── 장면 5: 휴대폰 /m/device-register(옛 QR 주소 · 로그인 없음) ──
async function scenePhone(st) {
  const step = { now: '휴대폰 화면 열기' };
  let kb = null;
  try {
    kb = await openCtx(st, 'S3PHONE', { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, userAgent: PHONE_UA });
    const t = crypto.randomBytes(32).toString('hex'); // 어디에도 없는 값(서버 표에 없음 · 비밀 아님)
    // ⬛ [낡은 몸 · 고침 1] 로그인 없이 열고 안내 글을 기다렸다(notice 참이어야 통과) — 런 46118135957 got path=/login.
    //   사유: 그 화면은 [AllowAnonymous] 지만 기본 틀 MainLayout(:2 [Authorize] · :106 ~ :108 RedirectToLogin)이 로그인 창으로 보낸다(main 같음 · 설계 화면 손 0).
    // ⬛ await kb.page.goto(`${K.BASE}/m/device-register?t=${t}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    // ⬛ const notice = await seen(kb.page, PHONE_NOTICE);
    // ⬛ await K.sleep(2000);
    // ⬛ const ask = await kb.page.getByText(PHONE_OLD_ASK, { exact: false }).count();
    // ⬛ const band = await errBand(kb.page);
    // ⬛ const at = K.pathOf(kb.page.url()).split('?')[0];
    // ⬛ rec(st, 'phone', { pass: notice && ask === 0 && kb.oldReq === 0 && band === false, got: { notice, oldAsk: ask, oldRequests: kb.oldReq, path: at, errBand: band } });
    // 🔴 고침 2 ③ — ㉮ 로그인 없음 → /login 착지(로그인 창 칸이 보임) ㉯ 같은 휴대폰으로 로그인 → 같은 옛 주소 → 안내. 둘 다 옛 물음 0 · 옛 요청 0 · 흰 띠 0.
    await kb.page.goto(`${K.BASE}/m/device-register?t=${t}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    const loginField = await kb.page.getByLabel('아이디', { exact: true }).first()
      .waitFor({ state: 'visible', timeout: TEXT_MS }).then(() => true, () => false);
    await K.sleep(2000); // 옛 화면이었다면 등록 요청이 나갈 몫
    const anon = {
      path: K.pathOf(kb.page.url()).split('?')[0],
      loginField,
      notice: (await kb.page.getByText(PHONE_NOTICE, { exact: false }).count()) > 0,
      oldAsk: await kb.page.getByText(PHONE_OLD_ASK, { exact: false }).count(),
      oldRequests: kb.oldReq,
      errBand: await errBand(kb.page),
    };
    const anonPass = anon.path === '/login' && anon.loginField && anon.oldAsk === 0 && anon.oldRequests === 0 && anon.errBand === false;
    // ㉯ 로그인한 휴대폰(새 직원 · 이미 첫 로그인 지남) — 갈 곳은 판정하지 않는다(새 기기 알림 경합은 장면 4 진단 몫)
    step.now = '휴대폰 로그인';
    if (!st.prepOk) throw new K.KitStepError(`준비 실패 — ${st.prepWhy}`);
    await kb.page.getByLabel('아이디', { exact: true }).fill(ID.fresh, { timeout: 60000 });
    await kb.page.getByLabel('비밀번호', { exact: true }).fill(st.freshPw);
    await kb.page.getByLabel('비밀번호', { exact: true }).press('Tab');
    await kb.page.getByRole('button', { name: '로그인', exact: true }).click();
    const lend = Date.now() + 30000;
    while (kb.login.seen === 0 && Date.now() < lend) await K.sleep(250);
    await K.waitMasked(kb.tok);
    if (kb.login.seen === 0 || !kb.tok.access) throw new K.KitStepError(`휴대폰 로그인 안 됨(로그인 응답 ${kb.login.seen}회 · status=${kb.login.status})`);
    await K.sleep(3000); // 로그인 뒤 이동이 끝날 몫(갈 곳 무판정)
    step.now = '로그인한 휴대폰 옛 주소';
    const before = kb.oldReq;
    await kb.page.goto(`${K.BASE}/m/device-register?t=${t}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
    const notice = await seen(kb.page, PHONE_NOTICE);
    await K.sleep(2000);
    const signed = {
      path: K.pathOf(kb.page.url()).split('?')[0],
      notice,
      oldAsk: await kb.page.getByText(PHONE_OLD_ASK, { exact: false }).count(),
      oldRequests: kb.oldReq - before,
      errBand: await errBand(kb.page),
    };
    const signedPass = signed.notice && signed.oldAsk === 0 && signed.oldRequests === 0 && signed.errBand === false;
    const pass = anonPass && signedPass;
    rec(st, 'phone', { pass, got: pass ? { anon, signed } : { anon, signed, dx: diag(kb) } });
  } catch (e) {
    dev(st, 'phone', step, e);
  } finally {
    if (kb) await closeKb(st, kb); // 로그인 없음 — 컨텍스트만 닫힌다
  }
}

// ── 장면 6 · 7: 닫힌 두 서버 문 — 처리기 도달 0(시험 DB 행 변화 0) ──
async function probes(st) {
  const step = { now: '대표 API 로그인' };
  let p = null;
  try {
    p = await apiOpen(st, 'PARENT', ID.parent, PW.parent);
    const tid = idOk(jwtClaim(p.token, 'tenant_id'), '회사 번호');
    step.now = 'mobile-token 앞 셈';
    const before = counts(tid);
    step.now = 'POST mobile-token';
    const r = await rawCall('POST', '/api/devices/mobile-token', p, {});
    const after = counts(tid);
    console.log(`K 닫힌문 mobile-token status=${r.status} · 꼴 ${r.contentType || '-'} · 본문 ${r.bodyLength}자 · 토큰 행 ${before.tokens}→${after.tokens} · 기기 행 ${before.devices}→${after.devices}`);
    const pass = after.tokens === before.tokens && after.devices === before.devices && r.handlerWords === 0;
    rec(st, 'token', { pass, got: { ...r, tokenRows: `${before.tokens}→${after.tokens}`, deviceRows: `${before.devices}→${after.devices}` } });
  } catch (e) {
    dev(st, 'token', step, e);
  }
  const step2 = { now: '쓸 수 있는 토큰 심기' };
  let hash = null;
  try {
    if (!p) throw new K.KitStepError('대표 세션 없음 — 앞에서 섰다');
    const tid = idOk(jwtClaim(p.token, 'tenant_id'), '회사 번호');
    const by = idOk(jwtClaim(p.token, 'user_id'), '대표 번호');
    const plain = crypto.randomBytes(32).toString('hex');
    L.maskAndRegister('S3_QR_PLAIN', plain); // 열쇠 꼴 값 — 가리고 누출 검사 대상
    hash = crypto.createHash('sha256').update(plain, 'utf8').digest('hex'); // TenantDeviceService.Sha256Hex 와 같은 꼴(소문자 16진)
    if (!SAFE_HEX.test(hash)) throw new K.KitStepError('토큰 해시 꼴 이상');
    // 서비스 발급(IssueMobileRegisterTokenAsync)과 같은 칸 · 같은 10분 만료
    dbq(`INSERT INTO device_register_tokens (token_id, tenant_id, token_hash, issued_by, issued_at, expires_at) VALUES (UUID(), '${tid}', '${hash}', '${by}', NOW(6), DATE_ADD(NOW(6), INTERVAL 10 MINUTE));`);
    const before = counts(tid);
    const body = { token: plain, deviceName: 'ci-s3-phone', fingerprint: `ci-s3-fp-${crypto.randomBytes(6).toString('hex')}` };
    step2.now = 'POST mobile-register(대표 세션)';
    const r = await rawCall('POST', '/api/devices/mobile-register', p, body);
    step2.now = 'POST mobile-register(로그인 없음 · 기록만)';
    const anon = await rawCall('POST', '/api/devices/mobile-register', null, body);
    const after = counts(tid);
    const row = dbq(`SELECT (used_at IS NULL) + 0, (used_device_id IS NULL) + 0 FROM device_register_tokens WHERE token_hash = '${hash}';`);
    const unused = row.length === 1 && row[0][0] === '1' && row[0][1] === '1';
    console.log(`K 닫힌문 mobile-register status=${r.status} · 꼴 ${r.contentType || '-'} · 본문 ${r.bodyLength}자 · 로그인 없음 status=${anon.status} · 심은 토큰 안 쓰임=${unused} · 기기 행 ${before.devices}→${after.devices}`);
    const pass = unused && after.devices === before.devices && r.handlerWords === 0 && anon.handlerWords === 0;
    rec(st, 'register', {
      pass,
      got: { ...r, anonStatus: anon.status, anonBodyLength: anon.bodyLength, anonHandlerWords: anon.handlerWords, plantedUnused: unused, deviceRows: `${before.devices}→${after.devices}`, tokenRows: `${before.tokens}→${after.tokens}` },
    });
  } catch (e) {
    dev(st, 'register', step2, e);
  } finally {
    if (hash) {
      try {
        dbq(`DELETE FROM device_register_tokens WHERE token_hash = '${hash}';`);
      } catch (e) {
        console.log(`K 닫힌문 심은 토큰 지우기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 120) : L.errorName(e)}`);
      }
    }
    if (p) await apiClose(st, p);
  }
}

// ── 🔴 측정 M 관찰 장면 O1 · T1 · D1 — 로그 줄만(recK 0 · 기대 키 0 · 판정 0 · 끝 코드는 로그아웃 실패만 종전대로 셈) ──
//   판정 장면 · 닫힌 문 시험이 다 적힌 뒤에 돈다(시간 상한에 걸려도 판정 줄은 이미 있음 · fillDevice 가 채울 키 없음).
//   하나당 OBS_SCENE_MS 상한 · 남은 시간이 모자라면 「못 잼」 한 줄. 장면 몸은 스스로 잡아 절대 던지지 않는다(처리 안 된 거절 0).
async function obsScene(st, label, fn) {
  const left = st.t0 + S3_TOTAL_MS - Date.now();
  if (left < OBS_SCENE_MS + 60000) {
    console.log(`K 닫힌문 관찰 ${label} 못 잼 — 남은 시간 ${Math.round(left / 1000)}초(장면 상한 ${OBS_SCENE_MS / 1000}초 + 정리 60초 모자람)`);
    return;
  }
  console.log(`K 닫힌문 관찰 ${label} 시작`);
  const run = fn().then(
    () => console.log(`K 닫힌문 관찰 ${label} 끝`),
    (e) => console.log(`K 닫힌문 관찰 ${label} 못 만듦/섰음 — ${e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${L.errorName(e)}`}`),
  );
  await K.withTimeout(run, OBS_SCENE_MS, `관찰 ${label}`).catch((e) => console.log(`K 닫힌문 관찰 ${label} 시간 상한 — ${L.sanitize(e.message, 120)}(창은 끝 정리가 로그아웃 · 닫음)`));
}
// 화면 로그인 한 번 관찰(이동 목록 · 로그인 응답 참거짓 · 착지 · 3초 · 10초 보기 · 진단 찍기)
async function obsLogin(kb, label, loginId, password) {
  const s = await loginScreen(kb, loginId, password);
  const snaps = landSnaps(kb, kb.mx.landAt);
  console.log(`K 닫힌문 관찰 ${label} 로그인 응답 ${kb.login.seen}회 · status=${kb.login.status} · 첫 로그인=${kb.login.redirect} · 약관 필요=${kb.login.terms} · 새 기기=${kb.login.deviceNew} · 새 기기 창 닫음 ${s.noticeClosed}(실패 ${s.noticeCloseErrs})`);
  console.log(`K 닫힌문 관찰 ${label} 착지 ${s.where}@${L.sanitize(s.path, 120)} · 이동 ${L.sanitize(kb.navs.join(' > ') || '없음', 600)}`);
  await snaps;
  const band = await errBand(kb.page);
  console.log(`K 닫힌문 관찰 ${label} 마지막 주소 ${L.sanitize(K.pathOf(kb.page.url()).split('?')[0], 120)} · 흰 띠 ${band}`);
  mxFlush(kb, label);
}
// O1 — 대표 계정 화면 로그인 첫 화면(대조군 · 첫 로그인 아님 · 약관 동의됨 가정은 결과 줄로 확인)
async function obsOwner(st) {
  if (!PW.parent) throw new K.KitStepError('못 만듦 — 대표 비번 환경 칸 비어 있음');
  const kb = await openCtx(st, 'S3O1');
  try {
    await obsLogin(kb, 'O1', ID.parent, PW.parent);
  } finally {
    await closeKb(st, kb);
  }
}
// T1 — 약관 미동의 새 직원(준비 prep 과 같은 만들기 길 · 약관 동의만 건너뜀) → 화면 로그인 → 문서 이동 목록 · 약관 화면
async function obsTerms(st) {
  if (!st.prepOk) throw new K.KitStepError(`못 만듦 — 준비 실패(${st.prepWhy})`);
  const pw = `Ui#${crypto.randomBytes(16).toString('hex')}`;
  L.maskAndRegister('PW_S3_TERMS', pw);
  const p = await apiOpen(st, 'PARENT', ID.parent, PW.parent);
  try {
    const res = await K.apiCall('POST', '/api/users', {
      sess: p,
      body: { email: OBS_ID.terms, password: pw, userName: OBS_TERMS_NAME, empName: OBS_TERMS_NAME, role: 'User', confirmDifferentPerson: true },
    });
    if (res.status !== 201 && res.status !== 200) {
      const j = res.json || {};
      throw new K.KitStepError(`못 만듦 — 약관 미동의 직원 만들기 status=${res.status} code=${L.sanitize(String(j.code || j.message || ''), 80)}`);
    }
  } finally {
    await apiClose(st, p);
  }
  const kb = await openCtx(st, 'S3T1');
  try {
    await obsLogin(kb, 'T1', OBS_ID.terms, pw);
    const termsShown = await K.withTimeout(kb.page.evaluate(() => location.pathname), 10000, 'T1 주소').catch(() => null);
    console.log(`K 닫힌문 관찰 T1 약관 화면 주소=${L.sanitize(String(termsShown), 120)}(가설 확정 꼴 = 문서 이동 /terms=실패 → /=200)`);
  } finally {
    await closeKb(st, kb);
  }
}
// D1 — 새 기기 꼴(지문 재료가 다른 창 · 첫 로그인 지난 새 직원) → 새 기기 알림 창 보임(장치가 닫음 수) → 이동 목록
async function obsNewDevice(st) {
  if (!st.prepOk) throw new K.KitStepError(`못 만듦 — 준비 실패(${st.prepWhy})`);
  const kb = await openCtx(st, 'S3D1', { viewport: { width: 1366, height: 700 }, screen: { width: 1366, height: 768 }, userAgent: D1_UA, locale: 'ko-KR', timezoneId: 'Asia/Seoul' });
  try {
    await obsLogin(kb, 'D1', ID.fresh, st.freshPw);
    if (kb.login.deviceNew !== true) console.log(`K 닫힌문 관찰 D1 못 만듦 — 새 기기 꼴 아님(로그인 응답 새 기기=${kb.login.deviceNew}) · 까닭은 AuthController 새 기기 칸 조건 [4] 코드 반증 몫`);
  } finally {
    await closeKb(st, kb);
  }
}

// 끝 정리 — 연 브라우저 세션 · API 세션 모두 로그아웃 · 브라우저 닫기. 실패는 수만 센다(던지지 않음).
async function cleanup(st) {
  if (st.cleaned) return;
  st.cleaned = true;
  for (const kb of st.kbs) await closeKb(st, kb);
  for (const s of [...st.apiSess]) await apiClose(st, s);
  if (st.br) await st.br.close().catch((e) => console.log(`K 닫힌문 브라우저 닫기 실패 ${L.errorName(e)}`));
}

const st0 = {
  r: null, br: null, kbs: [], apiSess: [], logoutFail: 0, cleaned: false, t0: null,
  tid: null, parentUid: null, freshUid: null, freshPw: null, prepOk: false, prepWhy: null,
};

async function main() {
  const st = st0;
  st.t0 = Date.now(); // 🔴 측정 M — 관찰 장면 남은 시간 셈의 0점(전체 상한 타이머와 같은 시각)
  st.r = L.loadResult();
  L.bindAutosave(st.r);
  const allKeys = Object.values(KEY);
  let fatalOnce = false;
  const fatal = (where) => (e) => {
    if (fatalOnce) return;
    fatalOnce = true;
    try {
      console.log(`K 닫힌문 프로세스 밖 예외(${where}) ${L.errorName(e)}`);
      K.fillDevice(st.r, allKeys, `단언 전 실패 — 프로세스 밖 예외(${where})`, e);
      L.saveResult(st.r);
    } catch (e2) {
      console.log(`⚫ K 닫힌문 끊긴 자리 기록 실패 (${L.errorName(e2)})`);
    }
    // 세션 놓기 — 10초 상한 뒤 무조건 종료
    setTimeout(() => process.exit(1), 10000).unref();
    cleanup(st).then(() => process.exit(1), () => process.exit(1));
  };
  process.on('unhandledRejection', fatal('unhandledRejection'));
  process.on('uncaughtException', fatal('uncaughtException'));
  for (const sig of ['SIGTERM', 'SIGINT']) process.on(sig, () => fatal(`신호 ${sig}`)(new K.KitStepError(`신호 ${sig}`)));
  setTimeout(() => fatal('닫힌문 전체 시간 상한')(new K.KitStepError(`닫힌문 전체 시간 상한 ${S3_TOTAL_MS / 60000}분`)), S3_TOTAL_MS).unref();

  let pre = null;
  try {
    const pw = require('playwright');
    st.br = await pw[BROWSER].launch();
  } catch (e) {
    pre = `브라우저 기동 실패 ${L.errorName(e)}`;
  }
  try {
    await prep(st);
    st.prepOk = true;
  } catch (e) {
    st.prepWhy = e instanceof K.KitStepError ? L.sanitize(e.message, 160) : `예외 ${L.errorName(e)}`;
    console.log(`K 닫힌문 준비 실패 — ${st.prepWhy}`);
  }
  if (pre) {
    console.log(`K 닫힌문 시작 전 실패 — ${pre}`);
    K.fillDevice(st.r, [KEY.home, KEY.update, KEY.welcome, KEY.phone, KEY.gate], `단언 전 실패 — ${pre}`);
  } else {
    await sceneHome(st);
    await sceneUpdate(st);
    await scenePhone(st);
  }
  await probes(st); // 브라우저 없이도 돈다
  K.fillDevice(st.r, allKeys, '경우 끝 뒤 남은 키(있으면 장치 오류)');
  // 🔴 측정 M — 관찰 장면(판정 키 다 적힌 뒤 · 로그 줄만)
  if (pre) {
    console.log(`K 닫힌문 관찰 O1 · T1 · D1 못 잼 — ${pre}`);
  } else {
    await obsScene(st, 'O1', () => obsOwner(st));
    await obsScene(st, 'T1', () => obsTerms(st));
    await obsScene(st, 'D1', () => obsNewDevice(st));
  }
  await cleanup(st);
  // ⬛ [낡은 줄 · 고침 2] const mine = st.r.rows.filter((x) => x.scope === 'K' && String(x.id || '').startsWith('K35-S3'));
  // ⬛ [낡은 줄 · 고침 2] const bad = mine.filter((x) => x.pass !== true).length;
  // ⬛ [낡은 줄 · 고침 2] console.log(`K 닫힌문 본문 끝: 줄 ${mine.length} · 거짓 ${bad} · 로그아웃 실패 ${st.logoutFail}`);
  // ⬛ [낡은 줄 · 고침 2] L.saveResult(st.r);
  // ⬛ [낡은 줄 · 고침 2] process.exit(bad === 0 && st.logoutFail === 0 ? 0 : 1);
  // 🔴 고침 3 — 끝 코드 = endCode(판정기 면제 함수 그대로) · 끝 줄에 알려진 결함 수
  const mine = myRows(st.r.rows);
  const bad = mine.filter((x) => x.pass !== true).length;
  const known = mine.filter((x) => J.knownDefectFail(x)).length;
  console.log(`K 닫힌문 본문 끝: 줄 ${mine.length} · 거짓 ${bad} · 로그아웃 실패 ${st.logoutFail} · 알려진 결함 ${known}(거짓 수 안 · 끝 코드에서 뺌)`);
  L.saveResult(st.r);
  process.exit(endCode(st.r.rows, st.logoutFail));
}

// 🔴 고침 3 — 이 몸의 줄 · 끝 코드(순수 함수 · 자체시험 K-C27h). 거짓 줄 중 판정기 knownDefectFail 참인 줄은 뺀다 · 로그아웃 실패는 그대로 1.
function myRows(rows) {
  return (Array.isArray(rows) ? rows : []).filter((x) => x && x.scope === 'K' && String(x.id || '').startsWith('K35-S3'));
}
function endCode(rows, logoutFail) {
  const bad = myRows(rows).filter((x) => x.pass !== true && !J.knownDefectFail(x)).length;
  return bad === 0 && logoutFail === 0 ? 0 : 1;
}

if (require.main === module) main();
// ⬛ [낡은 줄 · 고침 2] module.exports = { KEY, HANDLER_WORDS, isWelcome };
module.exports = { KEY, HANDLER_WORDS, isWelcome, endCode, homeKnown, updateKnown }; // 고침 3 — endCode · 알려진 꼴 두 식
