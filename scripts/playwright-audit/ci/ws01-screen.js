// W-S-01 화면 측정 본문 (작업지시서 20261127작 W-S-01 CI화면측정장치 §2 · §4 · 실측 계획서 §3 · PM 결재 §269)
//
// 🔴 측정 커밋 안에서만 쓴다 — 머지 0(D2). 대상 표지(ws01.target · main · A · B · AB)가 없거나 꼴 밖이면 아무것도 안 재고 끝 코드 1.
// 🔴 이 파일의 규칙
//   ① 기록은 recW 하나로만 — 범위 W · 키 = 판정기 EXPECT_W_TABLE 의 그 대상 칸만 · 같은 키 두 번이면 던진다.
//      칸마다 두 판단을 같이 구한다: closedOk(닫힌 쪽 기대) · openOk(닫히기 전 기대 = 음성 대조).
//      그 대상 자리가 T 면 TEST 줄(pass = closedOk) · D 면 DEVICE 줄(pass = openOk). 단언 전 끊김 = DEVICE 거짓(장치).
//   ② 결과 칸은 화면 글 · 최종 주소 · 응답 코드 · Content-Type · 요청 목록으로만 채운다(소스 글자 대조 0 · 게이트는 동작).
//   ③ 그림은 업로드 자리 밖 임시 자리(kit35 stageDir)에 찍고, 찍기 직전 · 직후 화면 글자 대조(등록값)를 지난 것만 업로드 자리로 옮긴다.
//      옮긴 그림은 이름 · sha256 을 표준출력에 찍는다(판정에 쓴 그림 → 리뷰서 기록용).
//   ④ 계정 = 런 안 시드 부모(S5 ①) + 이 본문이 실물 길(POST api/users)로 만든 일반 자식 1(만들기 실패 시 시드 자식 가 · 결과 칸에 적음).
//      끝에 계정마다 · 세션마다 로그아웃 200 → 같은 출입증 API 1회 = 401(M-9 · §255). 하나라도 빠지면 M9 거짓.
//   ⑤ 시간 상한 — 전체 6분 · 칸 묶음마다 상한. 넘치면 남은 키 = DEVICE 거짓(앞 단계 끊김)으로 채우고 끝 코드 1.
//   ⑥ 비밀번호 · 토큰은 받거나 만든 즉시 가림 · 등록(lib maskAndRegister) — 콘솔 · 결과표 · 그림 대조 모두 그 목록을 쓴다. 빈 catch 0(#15).
//   ⑦ 11/27 [3-V] 고침(개발명세서 §5 11/27) — F1 출입증 확인 뒤에만 API 칸 · 닫힘 = 404 · 200 text/html 꼴만 / F2 M6e 최종 주소 · 빈 · 오류 화면 · main 대조 /
//      F3 「새로 생긴 콘솔 오류 0」(바탕 잡음 = 같은 런 열린 화면 표지) / F4 안내 종이 기다림 / F5 띄우려 한 브라우저 세션은 늘 M9 한 줄 / F6 R5 대조 = 분석 결과 칸.
//      판단은 순수 함수(m5aJudge · m6eJudge · m2Judge · pollUntil · holdRelease · r5Judge)로 빼 자체시험 W-C14 ~ W-C19 가 잰다.
'use strict';

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const L = require('./lib');
const J = require('./judge');
const K = require('./kit35');
const DC = require('./device-check'); // 머리 조건(require.main) 때문에 불러도 장치 본문은 안 돈다 — 내보냄만 쓴다

const env = process.env;
const BASE = K.BASE;
const W_TOTAL_MS = 6 * 60 * 1000;
const CELL_MS = 60000;
const READY_MS = 25000;
const VIEW_WIDE = { width: 1280, height: 800 };
const VIEW_NARROW = { width: 390, height: 844 };

// 화면 글 기대값(대상 PR 머리 aaf691e7 · 90ee1956 의 고객 노출 글 — 실측 계획서 §3 와 대조함 · 개발명세서 §2)
const NOTICE_TITLE = '이 화면은 정식 판에서 열립니다';
const NOTICE_BODY = '지금 쓰시는 히트판에서는 이 화면을 쉬어 갑니다. 회사 자료는 그대로 있습니다.';
const NOTICE_BUTTON = '처음 화면으로';
const CLOSED_PATHS = [
  ['WS01-M2a', '/settings'],
  ['WS01-M2b', '/settings/ai-assistant'],
  ['WS01-M2c', '/sales/profitability'],
  ['WS01-M2d', '/tax-invoice-stats'],
  ['WS01-M2e', '/accounting/export'],
];
const CONTROL_PATHS = ['/accounting/purchase-sales', '/settings/devices', '/print-settings', '/settings/mdb-migration'];
const SIDE_CLOSED5 = ['사용환경 설정', 'AI 도우미 연동', '판매수익성분석', '세금계산서 통계', '세무사 자료 보내기'];
const SIDE_NEAR4 = ['접속기기 확인', '판매순위표', '세금계산서 발행이력', '월마감'];
const SIDE_RESET = '모든데이터 초기화';
const MORE_CLOSED3 = ['사용환경', '계산서통계', '세무사전송'];
const RESET_CLOSED = '모든데이터 초기화는 지금 쓰시는 히트판에서 잠시 닫아 두었습니다.';
const MDB_CLOSED = '지금 쓰시는 히트판에서는 고를 수 없습니다.';
const MDB_MERGE = '없는 것만 보태기';
const APPLY_DONE = '적용되었습니다';
const PROFIT_CLOSED = '판매수익성분석은 정식 판에서 열립니다. 지금은 숫자를 보여 드리지 않습니다.';
const DEV_WORDS = /베타|beta|exception|stack|null|undefined|404|500/i;
// 닫힌 API 10(01a-6 4 · 01a-10 5 · 01b-06 1) — 경로별 원래 방법 · 빈 본문
const CLOSED_API = [
  ['GET', '/api/settings'], ['PUT', '/api/settings'],
  ['POST', '/api/settings/validate-unit-price'], ['POST', '/api/settings/verify-force-edit-password'],
  ['GET', '/api/ai-settings'], ['PUT', '/api/ai-settings/apikey'], ['PUT', '/api/ai-settings/apikey/openai'],
  ['PUT', '/api/ai-settings/provider/openai'], ['POST', '/api/ai-settings/check/openai'],
  ['GET', '/api/reports/sales-profitability'],
];
const EXTRA = []; // 이 프로세스에서 만든 · 받은 값(그림 대조에 더함 · 등록 목록은 S0 값만 읽으므로)
const PROBE_401 = '/api/settings/company'; // 세션 놓기 뒤 같은 출입증 1회(모든 대상에서 열린 [Authorize] 길)

// ── 대상 · 키 ─────────────────────────────────────────────────────
const TGT = J.wTargetScan(__dirname);
const TARGET = TGT.target;
const IDX = TARGET ? J.W_TARGETS.indexOf(TARGET) : -1;
const ROW = new Map(J.EXPECT_W_TABLE.map(([id, g, cols]) => [id, { g, slot: IDX >= 0 ? cols[IDX] : null }]));
const written = new Set();
function has(id) {
  const x = ROW.get(id);
  return !!x && x.slot !== null;
}
class WContractError extends Error {
  constructor(message) {
    super(message);
    this.name = 'WContractError';
  }
}
// ① 기록 — 닫힌 쪽 · 대조 판단을 함께 받고 자리(T/D)에 맞는 줄 하나를 쓴다.
function recW(r, id, { what, got, closedOk, openOk, err = null }) {
  const x = ROW.get(id);
  if (!x || x.slot === null) throw new WContractError(`recW: ${id} 는 대상 ${TARGET} 의 키 아님`);
  if (written.has(id)) throw new WContractError(`recW: ${id} 두 번`);
  written.add(id);
  const isT = x.slot === 'T';
  return L.rec(r, {
    id, g: x.g, path: TARGET, browser: 'chromium', scope: 'W',
    kind: isT ? 'TEST' : 'DEVICE', pass: isT ? closedOk === true : openOk === true,
    what: `${isT ? '[닫힘 기대]' : '[대조 · 닫히기 전 기대]'} ${what}`, got, err,
  });
}
// 단언 전 끊김 — 자리와 무관하게 DEVICE 거짓
function recWDevice(r, id, why, err = null) {
  const x = ROW.get(id);
  if (!x || x.slot === null || written.has(id)) return null;
  written.add(id);
  return L.rec(r, { id, g: x.g, path: TARGET, browser: 'chromium', scope: 'W', kind: 'DEVICE', pass: false, what: '단언 전 끊김(장치)', got: why, err });
}
function fillRest(r, why) {
  for (const [id] of J.EXPECT_W_TABLE) if (has(id) && !written.has(id)) recWDevice(r, id, why);
}
// 칸 묶음 — 상한 · 예외 = 그 묶음의 안 쓴 키를 DEVICE 거짓으로
async function cell(r, ids, what, fn) {
  const mine = ids.filter(has);
  if (mine.length === 0) return;
  try {
    await K.withTimeout(fn(), CELL_MS, `W ${what}`);
  } catch (e) {
    console.log(`W ${what} 끊김 — ${L.errorName(e)}`);
    for (const id of mine) recWDevice(r, id, `${what} 끊김`, e);
  }
}

// ── 11/27 [3-V] 고침 — 칸 판단을 순수 함수로 뺌(자체시험 W-C14 ~ W-C19 가 직접 잰다 · 「빼면 빨강」 변이 자리) ──
// F1 M5a · R5 — 같은 출입증으로 열린 길(PROBE_401)이 200 JSON 이어야 「출입증 있음」. 401 · 403 은 닫힘으로 세지 않는다.
function isOpenProbe(x) {
  return !!x && x.status === 200 && x.json === true;
}
// 닫힘 = 기대한 응답 꼴일 때만(404 · 또는 200 text/html) — 401 · 403 · 405 · 5xx · 빈 형식은 닫힘 아님
function isClosedShape(x) {
  return !!x && x.json !== true && (x.status === 404 || (x.status === 200 && /text\/html/i.test(x.ctype || '')));
}
function apiGate(sess, pre) {
  if (!sess || !sess.token) return { ok: false, why: '브라우저 출입증 못 받음(장치)' };
  if (!isOpenProbe(pre)) return { ok: false, why: `출입증 확인 실패 — 열린 길 응답 ${pre ? pre.status : 0} · json ${pre ? pre.json === true : false}(장치)` };
  return { ok: true, why: '' };
}
function m5aJudge(pre, rows, post, n = CLOSED_API.length) {
  const gate = isOpenProbe(pre) && isOpenProbe(post);
  const closedN = rows.filter(isClosedShape).length;
  const js = rows.filter((x) => x.json === true).length;
  return {
    gate,
    why: gate ? '' : `출입증 확인 실패 — 앞 ${pre ? pre.status : 0} · 뒤 ${post ? post.status : 0}(장치)`,
    closedN,
    js,
    closedOk: gate && rows.length === n && closedN === n,
    openOk: gate && rows.length === n && js === n,
  };
}
// F2 M6e — 역할 문 → /login → 인증됨 → / (RedirectToLogin · Login.razor forceLoad). 최종 주소 / · 사이드바 · 빈 화면 · 오류 화면 아님.
function m6eJudge(v) {
  const landed = v.up === true && v.at === '/' && v.nav > 0 && v.textLen >= 10 && v.errUi !== true && v.notFound !== true;
  const ok = landed && v.pw === 0 && v.notice !== true;
  return { landed, closedOk: ok, openOk: ok };
}
// F3 M2a ~ e — 콘솔 오류 표지(글 대신 짧은 해시) · 바탕 잡음 = 같은 런의 열린 화면(로그인 · / · 대조 주소 4)에서 본 표지 → 「새로 생긴 오류 0」
function errSig(kind, text) {
  const norm = String(text || '')
    .replace(/https?:\/\/\S+/g, 'U')
    .replace(/[0-9a-f]{8,}/gi, 'H')
    .replace(/\d+/g, '0')
    .slice(0, 300);
  return crypto.createHash('sha256').update(`${kind}|${norm}`).digest('hex').slice(0, 12);
}
function newErrCount(sigs, base) {
  return sigs.filter((s) => !base.has(s)).length;
}
function m2Judge(v) {
  if (v.baseReady !== true) return { device: '바탕 잡음 기준 못 잼(대조 주소 칸 끊김)', newErr: -1, closedOk: false, openOk: false };
  const newErr = newErrCount(v.sigs, v.base);
  return {
    device: null,
    newErr,
    closedOk: v.up === true && v.okText === true && v.devWord !== true && v.home === '/' && newErr === 0,
    openOk: v.up === true && v.pn === 0 && v.at === v.p && newErr === 0,
  };
}
// F4 — 늦게 그려지는 칸: 값이 조건을 채울 때까지 짧게 다시 본다(상한 안 · 마지막 값 돌려줌)
async function pollUntil(fn, okFn, ms, step = 300) {
  const end = Date.now() + ms;
  let v = await fn();
  while (!okFn(v) && Date.now() < end) {
    await new Promise((ok) => setTimeout(ok, step));
    v = await fn();
  }
  return v;
}
// F5 M9 — 브라우저를 띄우려 한 세션은 반드시 목록에 한 줄(출입증 있으면 놓기 · 없으면 거짓 줄)
async function holdRelease(hold, who, list, rel) {
  const b = hold ? hold.b : null;
  if (b && b.tok && b.tok.access) list.push(await rel({ who, token: b.tok.access, deviceId: b.tok.deviceId }));
  else list.push({ ok: false, got: `${who} ${b ? '출입증 못 받음(브라우저는 떴음)' : '브라우저 못 띄움'}` });
}
function m9(list) {
  return { ok: list.length > 0 && list.every((x) => x.ok), got: list.map((x) => x.got).join(' · ') || '세션 0' };
}
// F6 R5 — main 대조 = 수익 질문 응답에 분석 결과 칸(analysis.kind = sales-profitability)이 있음
const PROFIT_KIND = 'sales-profitability';
function r5Judge(hi, pf) {
  const hiClosed = hi.a.includes(PROFIT_CLOSED);
  const pfClosed = pf.a.includes(PROFIT_CLOSED);
  if (!pfClosed && hi.a === pf.a && pf.kind !== PROFIT_KIND) return { device: `분기 못 봄 — 두 질문 답이 같음(응답 ${hi.st} · ${pf.st} · 답 길이 ${hi.a.length})`, hiClosed, pfClosed };
  return {
    device: null,
    hiClosed,
    pfClosed,
    closedOk: hi.st !== 500 && !hiClosed && pfClosed,
    openOk: hi.st !== 500 && !pfClosed && pf.kind === PROFIT_KIND && hi.kind !== PROFIT_KIND,
  };
}

// ── 화면 도우미 ────────────────────────────────────────────────────
const sleep = (ms) => new Promise((ok) => setTimeout(ok, ms));
const NOTICE_WAIT_MS = 6000; // F4 — 닫힌 주소 칸만 안내 종이를 기다림(대조 런 = 5 주소 × 6초 · 전체 상한 안)
const hex = (n) => crypto.randomBytes(n).toString('hex');
function pathNow(page) {
  return K.pathOf(page.url()).replace(/\?.*$/, '');
}
// 앱이 눈을 떴나 — 사이드바 줄 또는 안내 제목 또는 주어진 글이 보일 때까지
async function ready(page, extra = null, ms = READY_MS) {
  const end = Date.now() + ms;
  while (Date.now() < end) {
    const n = await page.evaluate(([t, x]) => {
      const txt = document.body ? document.body.innerText : '';
      return { nav: document.querySelectorAll('.mud-nav-link').length, notice: txt.includes(t), extra: x ? txt.includes(x) : false };
    }, [NOTICE_TITLE, extra]).catch((e) => ({ nav: 0, notice: false, extra: false, err: L.errorName(e) }));
    if (extra ? n.extra : (n.nav > 0 || n.notice)) {
      await sleep(1200); // 늦게 그려지는 칸 몫
      return true;
    }
    await sleep(300);
  }
  return false;
}
async function gotoReady(page, p, extra = null) {
  await page.goto(`${BASE}${p}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  return ready(page, extra);
}
async function bodyText(page) {
  return page.evaluate(() => (document.body ? document.body.innerText : '')).catch((e) => `(글 못 읽음 ${L.errorName(e)})`);
}
async function navTexts(page) {
  // 접힌 묶음을 모두 편다(최대 40번) → 메뉴 줄 글(숨은 줄 포함 textContent)
  for (let i = 0; i < 40; i += 1) {
    const b = page.locator('.mud-nav-group button[aria-expanded="false"]').first();
    if ((await b.count()) === 0) break;
    await b.click({ timeout: 5000 }).catch((e) => console.log(`W 메뉴 펴기 실패 ${L.errorName(e)}`));
    await sleep(150);
  }
  return page.evaluate(() => [...document.querySelectorAll('.mud-nav-link')].map((x) => (x.textContent || '').replace(/\s+/g, ' ').trim()));
}
function countIn(list, names) {
  return names.filter((n) => list.some((t) => t === n)).length;
}
async function urlAfter(page, from, ms = 10000) {
  await page.waitForURL((u) => K.pathOf(String(u)).replace(/\?.*$/, '') !== from, { timeout: ms })
    .catch((e) => console.log(`W 주소 바뀜 기다림 끝(${L.errorName(e)})`));
  await sleep(800);
  return pathNow(page);
}
// ③ 그림 — 임시 자리 → 직전 · 직후 대조 → 업로드 자리
async function shot(page, label) {
  const stage = K.stageDir('ws01');
  if (!K.stageOutside(stage)) return { ok: false, got: '임시 자리가 업로드 자리 안(계약)' };
  const up = path.join(L.outDir(), 'main', 'shots', 'ws01');
  fs.mkdirSync(stage, { recursive: true });
  fs.mkdirSync(up, { recursive: true });
  const name = `${String(label).replace(/[^a-z0-9-]/gi, '_')}.png`;
  const tmp = path.join(stage, name);
  const leak = async () => K.domLeakHits(await page.evaluate(() => {
    const vals = [...document.querySelectorAll('input,textarea')].filter((x) => x.type !== 'password').map((x) => x.value || '');
    return `${document.body ? document.body.innerText : ''}\n${vals.join('\n')}`;
  }), [...K.registeredValues(), ...EXTRA]);
  try {
    if ((await leak()) > 0) return { ok: false, got: '찍기 전 대조 걸림 · 안 찍음' };
    await K.withTimeout(page.screenshot({ path: tmp, fullPage: false }), K.SHOT_MS, '그림');
    if ((await leak()) > 0) {
      fs.rmSync(tmp, { force: true });
      return { ok: false, got: '찍은 뒤 대조 걸림 · 지움' };
    }
    const dst = path.join(up, name);
    fs.renameSync(tmp, dst);
    const sha = crypto.createHash('sha256').update(fs.readFileSync(dst)).digest('hex');
    console.log(`W 그림 ${name} sha256=${sha}`);
    return { ok: true, got: `${name} sha256=${sha.slice(0, 16)}` };
  } catch (e) {
    try {
      fs.rmSync(tmp, { force: true });
    } catch (e2) {
      console.log(`W 임시 그림 지우기 실패 ${L.errorName(e2)}`);
    }
    return { ok: false, got: `그림 예외 ${L.errorName(e)}` };
  }
}
async function fetchForm(method, p, sess, body) {
  const headers = { 'Content-Type': 'application/json', 'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64) HitPanCiSeed/1' };
  if (sess && sess.token) headers.Authorization = `Bearer ${sess.token}`;
  if (sess && sess.deviceId) headers['X-HitPan-Device-Id'] = sess.deviceId;
  const res = await fetch(`${BASE}${p}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const ctype = res.headers.get('content-type') || '';
  const text = await res.text();
  return { status: res.status, ctype, json: /json/i.test(ctype), text };
}
// ④ 세션 놓기 — 로그아웃 200 → 같은 출입증 1회 = 401
async function release(sess) {
  if (!sess || !sess.token) return { ok: false, got: `${sess ? sess.who : '-'} 세션 없음` };
  const lo = await K.apiLogout(sess);
  let st = 0;
  try {
    st = (await fetchForm('GET', PROBE_401, sess)).status;
  } catch (e) {
    console.log(`W 세션 확인 요청 실패 ${L.errorName(e)}`);
  }
  return { ok: lo.ok && st === 401, got: `${lo.got} → 뒤 요청 ${st}` };
}
// ⬛ 11/27 [3-V] F5 앞: async function newBrowserSession(pwlib, st, who, loginId, pw, view) {
//   F5 — hold.b 를 브라우저 띄우기 **전에** 채운다 → 안에서 던져도 부르는 쪽이 출입증 놓기 · 브라우저 닫기를 한다.
//   F3 — esig = 콘솔 오류 표지(해시)만 · base · baseReady = 바탕 잡음(대조 주소 칸 끝에 채움).
async function newBrowserSession(pwlib, st, who, loginId, pw, view, hold = {}) {
  const b = { who, tok: { access: null, masked: 0, deviceId: null }, req: [], cerr: 0, esig: [], base: new Set(), baseReady: false, tally: { continue: 0, abort: 0 } };
  hold.b = b;
  b.browser = await pwlib.chromium.launch();
  b.context = await b.browser.newContext({ baseURL: BASE, viewport: view });
  await K.blockExternal(b.context, st.r, b.tally);
  K.watchTokens(b.context, b.tok);
  b.page = await b.context.newPage();
  b.page.on('request', (q) => {
    if (b.req.length >= 3000) return;
    const p = K.pathOf(q.url()).replace(/\?.*$/, '');
    if (p.startsWith('/api/')) b.req.push(`${q.method()} ${p}`);
  });
  // 콘솔 오류 = 화면 예외 + 오류 줄(자원 못 받음 줄은 외부 차단 · 상태 코드 응답이라 뺌 · 글은 찍지 않고 수만)
  // ⬛ 11/27 [3-V] F3 앞: 수만 셈(b.cerr += 1) — 이제 수 + 표지(해시 12자 · 글은 남기지 않음)
  b.page.on('console', (m) => {
    if (m.type() === 'error' && !/Failed to load resource/i.test(m.text())) {
      b.cerr += 1;
      if (b.esig.length < 500) b.esig.push(errSig('c', m.text()));
    }
  });
  b.page.on('pageerror', (e) => {
    b.cerr += 1;
    if (b.esig.length < 500) b.esig.push(errSig('p', `${e && e.name} ${e && e.message}`));
  });
  const s = await K.screenLogin(b.page, loginId, pw);
  if (s.where !== 'main') throw new K.KitStepError(`${who} 로그인 뒤 자리=${s.where}`);
  await K.waitMasked(b.tok);
  if (b.tok.access) EXTRA.push(b.tok.access);
  try {
    await DC.closeBriefing(b.page, { lastErr: null }, 3000);
  } catch (e) {
    console.log(`W 브리핑 덮개 닫기 예외 ${L.errorName(e)}`);
  }
  return b;
}
async function closeBrowser(b) {
  if (!b || !b.browser) return;
  try {
    await b.browser.close();
  } catch (e) {
    console.log(`W 브라우저 닫기 예외 ${L.errorName(e)}`);
  }
}

// ── 칸 ─────────────────────────────────────────────────────────────
async function cellsWide(r, b) {
  const page = b.page;
  await cell(r, ['WS01-M1a', 'WS01-M6b'], '사이드바', async () => {
    await gotoReady(page, '/');
    const list = await navTexts(page);
    const c5 = countIn(list, SIDE_CLOSED5);
    const n4 = countIn(list, SIDE_NEAR4);
    const rs = countIn(list, [SIDE_RESET]);
    const sh = await shot(page, 'm1a-sidebar');
    if (has('WS01-M1a')) recW(r, 'WS01-M1a', { what: '사이드바 닫힌 5줄 0 · 옆 4줄 있음 / 대조 = 5줄 있음', got: `닫힌 5 중 ${c5} · 옆 4 중 ${n4} · 메뉴 줄 ${list.length} · ${sh.got}`, closedOk: c5 === 0 && n4 === 4, openOk: c5 === 5 });
    if (has('WS01-M6b')) recW(r, 'WS01-M6b', { what: '사이드바 「모든데이터 초기화」 줄 0 / 대조 = 있음', got: `초기화 줄 ${rs}`, closedOk: rs === 0, openOk: rs === 1 });
  });
  await cell(r, ['WS01-M2ctl'], '대조 주소 4', async () => {
    const got = [];
    let ok = true;
    for (const p of CONTROL_PATHS) {
      const up = await gotoReady(page, p);
      const t = await bodyText(page);
      const at = pathNow(page);
      const one = up && at === p && !t.includes(NOTICE_TITLE);
      ok = ok && one;
      got.push(`${p}=${one ? '원래 화면' : `안내 ${t.includes(NOTICE_TITLE)} · 자리 ${at}`}`);
    }
    // F3 — 바탕 잡음 = 여기까지(로그인 · / · 대조 주소 4) 본 콘솔 오류 표지. 닫힌 주소 칸은 이 밖의 표지만 「새 오류」 로 센다.
    b.base = new Set(b.esig);
    b.baseReady = true;
    got.push(`바탕 잡음 ${b.base.size}종(오류 ${b.cerr})`);
    recW(r, 'WS01-M2ctl', { what: '대조 주소 4 원래 화면(안내 0)', got: got.join(' · '), closedOk: ok, openOk: ok });
  });
  for (const [id, p] of CLOSED_PATHS) {
    await cell(r, [id], `직접 주소 ${p}`, async () => {
      const c0 = b.cerr;
      const s0 = b.esig.length;
      const up = await gotoReady(page, p);
      const paper = page.locator('.mud-paper', { hasText: NOTICE_TITLE });
      // ⬛ 11/27 [3-V] F4 앞: const pn = await paper.count(); (한 번만 셈) — 이제 안내 종이를 짧게 기다림
      const pn = await pollUntil(() => paper.count(), (n) => n > 0, NOTICE_WAIT_MS);
      const at = pathNow(page);
      const ptxt = pn > 0 ? await paper.first().innerText() : '';
      const okText = ptxt.includes(NOTICE_TITLE) && ptxt.includes(NOTICE_BODY) && ptxt.includes(NOTICE_BUTTON);
      const devWord = DEV_WORDS.test(ptxt);
      const sh = await shot(page, `m2-${id.slice(-1)}`);
      let home = '-';
      if (pn > 0) {
        await page.getByRole('button', { name: NOTICE_BUTTON, exact: true }).click({ timeout: 5000 });
        home = await urlAfter(page, at);
      }
      const cerr = b.cerr - c0;
      // ⬛ 11/27 [3-V] F3 앞: closedOk = … && cerr === 0 · openOk = up && pn === 0 && at === p (main 은 오류 수를 안 봄)
      //   이제 「새로 생긴 오류 0」 — 바탕 잡음(같은 런 열린 화면 표지) 뺌 · main 대조(D)도 같은 조건을 먼저 지나야 함
      const sigs = b.esig.slice(s0);
      const j = m2Judge({ up, okText, devWord, home, pn, at, p, sigs, base: b.base, baseReady: b.baseReady });
      const newSig = [...new Set(sigs.filter((s) => !b.base.has(s)))].slice(0, 5).join(',') || '-';
      if (j.device) {
        recWDevice(r, id, `${j.device} · 콘솔 오류 ${cerr}`);
        return;
      }
      recW(r, id, {
        what: `${p} 완전 새로 읽기 → 안내 글 3줄 일치 · 단추 → / · 새 콘솔 오류 0(바탕 잡음 뺌) · 베타 · 개발 낱말 0 / 대조 = 원래 화면 · 새 콘솔 오류 0`,
        got: `자리 ${at} · 안내 ${pn} · 글 일치 ${okText} · 낱말 ${devWord} · 단추 뒤 ${home} · 콘솔 오류 ${cerr}(새 ${j.newErr} · 표지 ${newSig}) · ${sh.got}`,
        closedOk: j.closedOk,
        openOk: j.openOk,
      });
    });
  }
  await cell(r, ['WS01-M2f', 'WS01-M6d'], '/data/reset', async () => {
    await gotoReady(page, '/data/reset');
    const end = Date.now() + READY_MS;
    let pw = 0;
    let t = '';
    while (Date.now() < end) {
      pw = await page.locator('input[type="password"]').count();
      t = await bodyText(page);
      if (pw > 0 || t.includes(RESET_CLOSED)) break;
      await sleep(300);
    }
    await sleep(800);
    pw = await page.locator('input[type="password"]').count();
    t = await bodyText(page);
    const sh = await shot(page, 'm2f-data-reset');
    const closed = t.includes(RESET_CLOSED);
    if (has('WS01-M2f')) recW(r, 'WS01-M2f', { what: '/data/reset 초기화 닫힘 안내 글 일치 · 비번 입력칸 0 / 대조 = 입력칸 있음', got: `안내 ${closed} · 비번 칸 ${pw} · 자리 ${pathNow(page)}`, closedOk: closed && pw === 0, openOk: pw > 0 && !closed });
    if (has('WS01-M6d')) recW(r, 'WS01-M6d', { what: 'F4 그림 찍힘(판단 1줄은 사람 · 리뷰서)', got: sh.got, closedOk: sh.ok, openOk: sh.ok });
  });
  await cell(r, ['WS01-R6', 'WS01-M6c'], '자료이관', async () => {
    await gotoReady(page, '/settings/mdb-migration', MDB_MERGE);
    const v = await page.evaluate(([merge]) => {
      const radios = [...document.querySelectorAll('.mud-radio')];
      const dis = (el) => !!(el && ((el.querySelector('input') && el.querySelector('input').disabled) || el.classList.contains('mud-disabled')));
      const m = radios.find((x) => (x.textContent || '').includes(merge));
      const o = radios.find((x) => x !== m && x.parentElement === (m && m.parentElement));
      return { n: radios.length, mergeFound: !!m, mergeDis: dis(m), overFound: !!o, overDis: dis(o) };
    }, [MDB_MERGE]);
    const t = await bodyText(page);
    const sh = await shot(page, 'r6-mdb');
    if (has('WS01-R6')) recW(r, 'WS01-R6', { what: '자료이관 「없는 것만 보태기」 고를 수 있음', got: `보태기 칸 ${v.mergeFound} · 잠김 ${v.mergeDis} · 단추 ${v.n} · ${sh.got}`, closedOk: v.mergeFound && !v.mergeDis, openOk: v.mergeFound && !v.mergeDis });
    if (has('WS01-M6c')) recW(r, 'WS01-M6c', { what: '덮어쓰기 비활성 + 안내 글 일치 / 대조 = 활성', got: `덮어쓰기 칸 ${v.overFound} · 잠김 ${v.overDis} · 안내 ${t.includes(MDB_CLOSED)}`, closedOk: v.overFound && v.overDis && t.includes(MDB_CLOSED), openOk: v.overFound && !v.overDis });
  });
  await cell(r, ['WS01-R1'], '매입매출장', async () => {
    const rx = /\/api\/finance\/purchase-sales-ledger/i;
    let resp = null;
    const armed = page.waitForResponse((x) => rx.test(x.url()), { timeout: 40000 }).then((x) => { resp = x; return x; })
      .catch((e) => { console.log(`W 매입매출장 응답 기다림 끝 ${L.errorName(e)}`); return null; });
    await gotoReady(page, '/accounting/purchase-sales');
    await sleep(2000);
    if (!resp) {
      const q = page.getByRole('button', { name: '조회', exact: true });
      if ((await q.count()) > 0) await q.first().click({ timeout: 5000 });
    }
    await armed;
    await sleep(1000);
    const st = resp ? resp.status() : 0;
    const ct = resp ? (resp.headers()['content-type'] || '') : '';
    const errSnack = await page.locator('.mud-snackbar[class*="error"]').count();
    const ok = st === 200 && /json/i.test(ct) && errSnack === 0;
    recW(r, 'WS01-R1', { what: '매입매출장 조회 200 JSON · 오류 알림 0', got: `응답 ${st} · 형식 ${/json/i.test(ct) ? 'json' : (ct.split(';')[0] || '-')} · 오류 알림 ${errSnack}`, closedOk: ok, openOk: ok });
  });
  await cell(r, ['WS01-R3'], '인쇄 설정', async () => {
    const rx = /\/api\/settings\/print-common/i;
    await gotoReady(page, '/print-settings');
    const f = page.locator('.mud-main-content input[type="text"]:not([readonly]):not([disabled])').first();
    if ((await f.count()) === 0) throw new K.KitStepError('바꿀 칸 없음');
    const before = await f.inputValue();
    const want = `${before.slice(0, 20)}w${hex(1)}`;
    await f.fill(want);
    await f.press('Tab');
    const put = page.waitForResponse((x) => rx.test(x.url()) && x.request().method() === 'PUT', { timeout: 20000 });
    await page.getByRole('button', { name: /저장/ }).first().click({ timeout: 5000 });
    const pr = await put;
    await gotoReady(page, '/print-settings');
    await sleep(1500);
    const after = await page.locator('.mud-main-content input[type="text"]:not([readonly]):not([disabled])').first().inputValue();
    const ok = pr.status() === 200 && after === want;
    recW(r, 'WS01-R3', { what: '인쇄 설정 1칸 저장 → 새로 읽어도 값 유지 · print-common 200', got: `저장 응답 ${pr.status()} · 값 유지 ${after === want}`, closedOk: ok, openOk: ok });
  });
  await cell(r, ['WS01-R2', 'WS01-R4', 'WS01-M3c'], '사용자정보', async () => {
    const i0 = b.req.length;
    const rx = /\/api\/settings\/company(\?|$)/i;
    await gotoReady(page, '/settings/user-info', '업태');
    const cnt = page.getByLabel('상시근로자수');
    const biz = page.getByLabel('업태', { exact: true });
    const cntWant = String(10 + crypto.randomInt(80));
    const bizWant = `ci${hex(2)}`;
    await cnt.fill(cntWant);
    const bizRo = await biz.evaluate((x) => x.readOnly === true);
    if (!bizRo) await biz.fill(bizWant);
    await biz.press('Tab');
    let put = null;
    const armed = page.waitForResponse((x) => rx.test(x.url()) && x.request().method() === 'PUT', { timeout: 20000 })
      .then((x) => { put = x; return x; }).catch((e) => { console.log(`W 회사 정보 저장 응답 기다림 끝 ${L.errorName(e)}`); return null; });
    let apply = page.getByRole('button', { name: '적용', exact: true });
    if ((await apply.count()) === 0) apply = page.getByRole('button', { name: /적용/ });
    await apply.first().click({ timeout: 5000 });
    await armed;
    let snack = false;
    const end = Date.now() + 15000;
    while (Date.now() < end && !snack) {
      snack = (await bodyText(page)).includes(APPLY_DONE);
      if (!snack) await sleep(300);
    }
    await gotoReady(page, '/settings/user-info', '업태');
    await sleep(1500);
    const cntAfter = await page.getByLabel('상시근로자수').inputValue();
    const bizAfter = await page.getByLabel('업태', { exact: true }).inputValue();
    const root = b.req.slice(i0).filter((x) => x === 'GET /api/settings' || x === 'PUT /api/settings').length;
    const putSt = put ? put.status() : 0;
    const putJson = put ? /json/i.test(put.headers()['content-type'] || '') : false;
    if (has('WS01-R2')) {
      const ok = putSt === 200 && putJson && cntAfter === cntWant;
      recW(r, 'WS01-R2', { what: '사업장 정보(상시근로자수) 저장 → 새로 읽어도 값 유지 · settings/company 200 JSON', got: `저장 응답 ${putSt} · json ${putJson} · 값 유지 ${cntAfter === cntWant}`, closedOk: ok, openOk: ok });
    }
    if (has('WS01-R4')) {
      if (bizRo) recWDevice(r, 'WS01-R4', '업태 칸 잠김(바꿀 수 없음)');
      else recW(r, 'WS01-R4', { what: '[적용] 「적용되었습니다」 · 업태 유지 · api/settings 루트 GET/PUT 0 / 대조 = 루트 요청 있음', got: `알림 ${snack} · 업태 유지 ${bizAfter === bizWant} · 루트 요청 ${root}`, closedOk: snack && bizAfter === bizWant && root === 0, openOk: root > 0 });
    }
    if (has('WS01-M3c')) {
      const from = pathNow(page);
      await page.getByRole('button', { name: '닫기', exact: true }).last().click({ timeout: 5000 });
      const at = await urlAfter(page, from);
      recW(r, 'WS01-M3c', { what: '사용자정보 [닫기] → / / 대조 = /settings', got: `최종 주소 ${at}`, closedOk: at === '/', openOk: at === '/settings' });
    }
  });
  await cell(r, ['WS01-M3b'], '구독 돌아가기', async () => {
    const btn = '사용자정보설정으로 돌아가기';
    await gotoReady(page, '/settings/subscription-billing', btn);
    const from = pathNow(page);
    await page.getByRole('button', { name: btn, exact: true }).click({ timeout: 5000 });
    const at = await urlAfter(page, from);
    recW(r, 'WS01-M3b', { what: '구독 「사용자정보설정으로 돌아가기」 → /settings/user-info / 대조 = /settings', got: `최종 주소 ${at}`, closedOk: at === '/settings/user-info', openOk: at === '/settings' });
  });
}
async function cellsNarrow(r, b) {
  const page = b.page;
  await page.setViewportSize(VIEW_NARROW);
  try {
    await cell(r, ['WS01-M1b'], '더보기(390×844)', async () => {
      await gotoReady(page, '/more', '');
      await sleep(1500);
      const items = await page.evaluate(() => [...document.querySelectorAll('.hp-menu-item')].map((x) => (x.textContent || '').replace(/\s+/g, ' ').trim()));
      const c3 = MORE_CLOSED3.filter((n) => items.some((t) => t === n || t.startsWith(`${n} `))).length;
      const sh = await shot(page, 'm1b-more');
      recW(r, 'WS01-M1b', { what: '더보기 닫힌 3줄 0 · 다른 항목 있음 / 대조 = 3줄 있음', got: `닫힌 3 중 ${c3} · 항목 ${items.length} · ${sh.got}`, closedOk: c3 === 0 && items.length > 0, openOk: c3 === 3 });
    });
    await cell(r, ['WS01-M3a'], '아바타(390×844)', async () => {
      await gotoReady(page, '/');
      const from = pathNow(page);
      await page.locator('button.hp-mha-btn:has(.hp-mha-avatar)').first().click({ timeout: 5000 });
      const at = await urlAfter(page, from);
      recW(r, 'WS01-M3a', { what: '아바타 → /settings/user-info / 대조 = /settings', got: `폭 390 · 최종 주소 ${at}`, closedOk: at === '/settings/user-info', openOk: at === '/settings' });
    });
  } finally {
    await page.setViewportSize(VIEW_WIDE);
  }
}
async function probeOpen(sess) {
  try {
    return await fetchForm('GET', PROBE_401, sess);
  } catch (e) {
    console.log(`W 출입증 확인 요청 실패 ${L.errorName(e)}`);
    return { status: 0, ctype: '', json: false, text: '' };
  }
}
// ⬛ 11/27 [3-V] F1 · F6 앞: 출입증 확인 없이 바로 R5 · M5a(출입증 못 받으면 10 경로 401 = 「JSON 아님」 = 닫힘 초록) · R5 대조 = 「닫힘 글 아님」 뿐
//   이제 ① 같은 출입증으로 열린 길 200 JSON 을 먼저 봄(아니면 R5 · M5a = DEVICE 거짓) ② M5a 닫힘 = 404 · 200 text/html 꼴만 · 끝에 열린 길 다시 200 JSON
//   ③ R5 대조 = 수익 질문 응답에 분석 결과 칸(kind) 있음 · 인사엔 없음
async function cellsApi(r, sess) {
  const pre = sess && sess.token ? await probeOpen(sess) : null;
  const gate = apiGate(sess, pre);
  if (!gate.ok) {
    console.log(`W API 칸 — ${gate.why}`);
    for (const id of ['WS01-R5', 'WS01-M5a']) recWDevice(r, id, gate.why);
    return;
  }
  await cell(r, ['WS01-R5'], '도우미 분기', async () => {
    const ask = async (q) => {
      const x = await fetchForm('POST', '/api/chatbot/ask', sess, { message: q });
      let a = '';
      let kind = '';
      try {
        const j = x.json ? JSON.parse(x.text) : null;
        a = String((j && (j.answer || j.Answer || j.message || j.Message)) || '');
        const an = j && (j.analysis || j.Analysis);
        kind = String((an && (an.kind || an.Kind)) || '');
      } catch (e) {
        console.log(`W 도우미 응답 해석 실패 ${L.errorName(e)}`);
      }
      return { st: x.status, a, kind };
    };
    const hi = await ask('안녕하세요');
    const pf = await ask('이번 달 수익 분석해줘');
    const j = r5Judge(hi, pf);
    // 두 답이 글자 같고 닫힘 안내도 분석 칸도 아니면 = 분기를 못 봄(도우미가 꺼져 같은 답만) → 장치
    if (j.device) {
      recWDevice(r, 'WS01-R5', j.device);
      return;
    }
    recW(r, 'WS01-R5', {
      what: '「안녕하세요」 = 닫힘 안내 아님 · 500 아님 / 수익 질문 = 닫힘 안내 글 · 대조 = 수익 질문 응답에 분석 결과 칸(kind) 있음',
      got: `인사 응답 ${hi.st} · 안내 ${j.hiClosed} · 분석 칸 ${hi.kind === PROFIT_KIND} / 수익 응답 ${pf.st} · 안내 ${j.pfClosed} · 분석 칸 ${pf.kind === PROFIT_KIND}`,
      closedOk: j.closedOk,
      openOk: j.openOk,
    });
  });
  await cell(r, ['WS01-M5a'], '닫힌 API 10', async () => {
    const got = [];
    const rows = [];
    for (const [m, p] of CLOSED_API) {
      const x = await fetchForm(m, p, sess, m === 'GET' ? undefined : {});
      rows.push(x);
      got.push(`${m} ${p} ${x.status} ${x.json ? 'json' : (x.ctype.split(';')[0] || '빈 형식')}`);
    }
    const post = await probeOpen(sess);
    const j = m5aJudge(pre, rows, post);
    if (!j.gate) {
      recWDevice(r, 'WS01-M5a', `${j.why} · ${got.join(' · ')}`);
      return;
    }
    recW(r, 'WS01-M5a', { what: '닫힌 10 경로 = 404 또는 200 text/html 꼴(401 · 403 은 닫힘 아님 · 앞뒤 열린 길 200 JSON) / 대조 = 10 모두 JSON', got: `닫힘 꼴 ${j.closedN}/10 · JSON ${j.js}/10 · 열린 길 앞 ${pre.status} 뒤 ${post.status} · ${got.join(' · ')}`, closedOk: j.closedOk, openOk: j.openOk });
  });
}

// ── 흐름 ───────────────────────────────────────────────────────────
async function main() {
  if (!TARGET) {
    console.log(`⚫ W 본문: 대상 표지 ${TGT.present ? `꼴 밖(${TGT.why || '-'})` : '없음'} — 아무것도 안 잰다(판정기가 짝 · 꼴을 빨강으로 잡는다)`);
    process.exit(1);
  }
  const r = L.loadResult();
  L.bindAutosave(r);
  const st = { r, sessP: [], sessC: [], childNote: '' };
  console.log(`W 본문 시작 — 대상 ${TARGET} · 키 ${J.EXPECT_W[TARGET].length}`);
  setTimeout(() => {
    console.log(`⚫ W 전체 시간 상한 ${W_TOTAL_MS / 60000}분`);
    fillRest(r, '전체 시간 상한');
    L.saveResult(r);
    process.exit(1);
  }, W_TOTAL_MS).unref();

  let pwlib = null;
  try {
    pwlib = require('playwright');
  } catch (e) {
    console.log(`W playwright 불러오기 실패 ${L.errorName(e)}`);
  }
  const pId = env.UI_LOGIN_PARENT || '';
  const pPw = env.UI_PW_PARENT || '';
  let child = null;
  // 1) API — 부모로 일반 자식 1 만들기(실물 길) → 자식 약관 → 두 API 세션 놓기(브라우저 로그인 전 · 한 PC 규칙)
  try {
    const p = await K.apiLogin('W_PARENT_API', pId, pPw);
    const cId = `ci-wc${hex(3)}`;
    const cPw = `Ui#${hex(16)}`;
    L.maskAndRegister('UI_PW_WC', cPw);
    EXTRA.push(cPw);
    const mk = await K.apiCall('POST', '/api/users', { sess: p, body: { email: cId, password: cPw, userName: '시험 다', empName: '시험 다', role: 'User', confirmDifferentPerson: true } });
    if (mk.status === 200 || mk.status === 201) {
      child = { id: cId, pw: cPw };
      st.childNote = '본문이 만든 일반 자식';
    } else {
      child = { id: env.UI_LOGIN_GA || '', pw: env.UI_PW_GA || '' };
      st.childNote = `만들기 응답 ${mk.status} → 시드 일반 자식 가 사용`;
    }
    console.log(`W 일반 자식: ${st.childNote}`);
    const c = await K.apiLogin('W_CHILD_API', child.id, child.pw);
    const ag = await K.apiCall('POST', '/api/auth/terms/agree', { sess: c, body: { agreeService: true, agreePrivacy: true, agreeSubscription: true, agreeDataOwnership: true, agreeMarketing: false, termsVersion: 'v2.0.0' } });
    console.log(`W 일반 자식 약관 응답 ${ag.status}`);
    st.sessC.push(await release(c));
    st.sessP.push(await release(p));
  } catch (e) {
    console.log(`W API 준비 끊김 — ${L.errorName(e)}`);
    st.sessP.push({ ok: false, got: `API 준비 끊김 ${L.errorName(e)}` });
  }
  // 2) 브라우저 — 부모(넓은 화면 → 좁은 화면) → API 칸(같은 출입증) → 세션 놓기
  // ⬛ 11/27 [3-V] F5 앞: bp = await newBrowserSession(…) 뒤에만 bp 가 채워짐 → 안에서 던지면 출입증 · 브라우저를 놓지 못함 ·
  //   자식 쪽엔 「못 받음」 거짓 줄이 없어 API 세션만으로 M9c 초록. 이제 hold(띄우기 전 채움) + holdRelease(늘 한 줄).
  const hp = {};
  if (pwlib) {
    try {
      const bp = await newBrowserSession(pwlib, st, 'W_PARENT_B', pId, pPw, VIEW_WIDE, hp);
      await cellsWide(r, bp);
      await cellsNarrow(r, bp);
      await cellsApi(r, { who: 'W_PARENT_B', token: bp.tok.access, deviceId: bp.tok.deviceId });
    } catch (e) {
      console.log(`W 부모 브라우저 끊김 — ${L.errorName(e)}`);
    }
    await holdRelease(hp, 'W_PARENT_B', st.sessP, release);
    await closeBrowser(hp.b);
    // 3) 일반 자식 브라우저 — /data/reset 역할 문(B · AB = T · main = D 대조 · A = 키 없음)
    if (has('WS01-M6e') && child) {
      const hc = {};
      // ⬛ 11/27 [3-V] F2 앞: closedOk = pw === 0 && !닫힘안내 · openOk = false 고정(최종 주소 · 준비 결과 안 봄)
      await cell(r, ['WS01-M6e'], '일반 자식 /data/reset', async () => {
        const bc = await newBrowserSession(pwlib, st, 'W_CHILD_B', child.id, child.pw, VIEW_WIDE, hc);
        const up = await gotoReady(bc.page, '/data/reset');
        const look = async () => ({ at: pathNow(bc.page), nav: await bc.page.locator('.mud-nav-link').count() });
        await pollUntil(look, (x) => x.at === '/' && x.nav > 0, 15000);
        await sleep(1500);
        const v = await bc.page.evaluate(([notice]) => {
          const t = document.body ? document.body.innerText : '';
          const eu = document.getElementById('blazor-error-ui');
          return {
            nav: document.querySelectorAll('.mud-nav-link').length,
            textLen: t.trim().length,
            errUi: !!eu && getComputedStyle(eu).display !== 'none',
            notFound: t.includes('페이지를 찾을 수 없습니다'),
            notice: t.includes(notice),
          };
        }, [RESET_CLOSED]);
        v.up = up;
        v.at = pathNow(bc.page);
        v.pw = await bc.page.locator('input[type="password"]').count();
        const j = m6eJudge(v);
        const sh = await shot(bc.page, 'm6e-child-reset');
        recW(r, 'WS01-M6e', {
          what: '일반 자식 /data/reset = 역할 문에 막혀 / 로 감(사이드바 · 빈 · 오류 화면 아님) · 대표 판정 글 · 입력칸 안 나옴 / 대조(main) = 같음(눈 뜸)',
          got: `준비 ${up} · 최종 주소 ${v.at} · 메뉴 줄 ${v.nav} · 글 길이 ${v.textLen} · 오류 띠 ${v.errUi} · 없는 쪽 ${v.notFound} · 비번 칸 ${v.pw} · 닫힘 안내 ${v.notice} · ${st.childNote} · ${sh.got}`,
          closedOk: j.closedOk,
          openOk: j.openOk,
        });
      });
      await holdRelease(hc, 'W_CHILD_B', st.sessC, release);
      await closeBrowser(hc.b);
    }
  }
  // 4) M-9 세션 놓기
  // ⬛ 11/27 [3-V] F5 앞: const m9 = (list) => … (지역 함수) — 이제 위 순수 함수 m9(자체시험 W-C18)
  const mp = m9(st.sessP);
  const mc = m9(st.sessC);
  if (has('WS01-M9p')) recW(r, 'WS01-M9p', { what: '부모 세션마다 로그아웃 200 → 같은 출입증 401', got: mp.got, closedOk: mp.ok, openOk: mp.ok });
  if (has('WS01-M9c')) recW(r, 'WS01-M9c', { what: '일반 자식 세션마다 로그아웃 200 → 같은 출입증 401', got: mc.got, closedOk: mc.ok, openOk: mc.ok });
  fillRest(r, '앞 단계 끊김(못 잼)');
  try {
    fs.rmSync(K.stageDir('ws01'), { recursive: true, force: true });
  } catch (e) {
    console.log(`W 임시 그림 자리 정리 실패 ${L.errorName(e)}`);
  }
  L.saveResult(r);
  const mine = r.rows.filter((x) => x.scope === 'W');
  const bad = mine.filter((x) => x.pass !== true).length;
  console.log(`W 본문 끝: 대상 ${TARGET} · 줄 ${mine.length} · 거짓 ${bad}`);
  process.exit(bad === 0 ? 0 : 1);
}

if (require.main === module) {
  main().catch((e) => {
    console.log(`⚫ W 본문 예외 — ${L.errorName(e)}`);
    process.exit(1);
  });
}
module.exports = {
  recW, has, CLOSED_API, CLOSED_PATHS, CONTROL_PATHS,
  // 11/27 [3-V] 고침 — 자체시험 W-C14 ~ W-C19 가 직접 잰다
  newBrowserSession, isOpenProbe, isClosedShape, apiGate, m5aJudge, m6eJudge, errSig, newErrCount, m2Judge, pollUntil, holdRelease, m9, r5Judge, PROFIT_CLOSED, PROFIT_KIND,
};
