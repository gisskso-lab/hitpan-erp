// 측정 전용(머지 금지 가지) — #1032 로그인 경합 고침 · 「정적 칸 남음」 끝 상태 실측 (CTO 판정 §3 (나) 내부 병행 실측)
//
// 물음(결론을 미리 정하지 않는다 · 기록만 · 판정기 줄 0):
//   로그인 성공 뒤 이동(forceLoad)을 낸 다음 새 문서가 들어서지 않고 옛 문서(같은 WASM 런타임)가 살아남으면,
//   다시 만들어진 로그인 화면이 진행 막대 + 눌리지 않는 「로그인」 단추에 영영 머무는가.
//   같은 장치를 고치기 전 Login.razor(대조 가지)에도 그대로 돌린다.
//
//   길 A  「이동 취소」 — 주 프레임 문서 요청을 붙잡아 둔 채 window.stop()(CDP Runtime.evaluate 로 부름) → 20초 관찰.
//   길 A2 「중지 단추」 — 같은 붙잡음 · 브라우저 중지(CDP Page.stopLoading = 사용자 Esc/중지 단추) → 20초 관찰(보조 · 길 A 가 불성립일 때 대비).
//   길 B  「뒤로 가기」 — 로그인 → 착지 → page.goBack() → 복원(bfcache) 여부 판별 + 20초 관찰 · 문서 응답 Cache-Control 기록.
//
// 규칙: 제품 손 0 · 값 · 토큰 · 비번 출력 0(토큰은 있음 여부 1/0 만) · 만든 직원은 이 몸이 끝에 「사용 안 함」 으로 치운다 ·
//   화면 세션은 장면 끝 finally 에서 API 로그아웃 · 이 PC 에서 돌리지 않는다(CI S8 만).
'use strict';

const crypto = require('crypto');
const L = require('./lib');
const K = require('./kit35');

const env = process.env;
const TAG = 'M1032';
const ARM = env.M1032_ARM || '(미표시)';
const ID = { parent: env.UI_LOGIN_PARENT || 'ci-parent', staff: 'ci-m1032' };
const PW = { parent: env.UI_PW_PARENT || '' };
const STAFF_NAME = '측정 정적칸';
const TOKEN_KEY = 'hitpan_access_token'; // AuthStorageKeys.AccessToken (있음 여부만 본다)
const DEVICE_NOTICE_TITLE = '기기 등록 안내';
const TOTAL_MS = 14 * 60 * 1000;
const OBS_SAMPLES = 40; // 0.5초 × 40 = 20초
const OBS_MS = 20500;
const HOLD_SETTLE_MS = 3000; // 붙잡음 첫 적중 뒤 다시 만들어짐(· 대조 쪽 두 번째 이동)까지 기다림
const RELEASE_AFTER_MS = 1000; // 중지 뒤 붙잡은 처리기를 풀어 주는 때(취소됐으면 넘김이 실패한다)

const log = (s) => console.log(`${TAG} [${ARM}] ${s}`);
const sleep = (ms) => new Promise((ok) => setTimeout(ok, ms));
const errName = (e) => L.errorName(e);
function pathOnly(u) {
  try {
    return new URL(u).pathname;
  } catch (e) {
    return `(주소 해석 실패 ${errName(e)})`;
  }
}
function jwtClaim(token, name) {
  try {
    const mid = String(token || '').split('.')[1] || '';
    const json = Buffer.from(mid.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8');
    const v = JSON.parse(json)[name];
    return typeof v === 'string' && v ? v : null;
  } catch (e) {
    log(`출입증 클레임 읽기 실패 ${errName(e)}`);
    return null;
  }
}
const SAFE_ID = /^[A-Za-z0-9-]{1,64}$/;

// ── 문서 안 표본기(초기 스크립트 · 모든 새 문서) ─────────────────────────
//   ① pageshow / pagehide 를 한 줄씩(persisted · 탐색 꼴 · 복원 못 한 까닭 이름만)
//   ② window.__hpMStart(tag, ticks) — 0.5초 간격 표본 console.log(경로 · 막대 · 아이디 칸 수 · 단추 꺼짐 · 토큰 있음 1/0)
//   ③ sessionStorage 표지(__hpM_sample)가 있으면 pageshow 때 표본기를 스스로 켠다(뒤로 가기 뒤 새 문서 · 복원 문서 둘 다)
//   ④ window.stop 을 감싸 불린 때를 한 줄(길 A 의 중지가 실제로 문서에서 돌았나)
const INIT = `(() => {
  try {
    if (window.top !== window) return;
    if (window.__hpMInit) return;
    window.__hpMInit = true;
    const vis = (e) => !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden';
    const flat = (e) => String((e && (e.innerText || e.textContent)) || '').replace(/\\s+/g, ' ').trim();
    const navType = () => { try { const x = performance.getEntriesByType('navigation')[0]; return x ? String(x.type) : 'none'; } catch (e) { return 'err'; } };
    const nrr = () => {
      try {
        const x = performance.getEntriesByType('navigation')[0];
        const r = x && x.notRestoredReasons;
        if (!r) return '-';
        const out = [];
        const walk = (n, d) => {
          if (!n || d > 3 || out.length > 12) return;
          if (Array.isArray(n.reasons)) for (const z of n.reasons) out.push(String((z && z.reason) || z).replace(/[^A-Za-z0-9_-]/g, '').slice(0, 40));
          if (n.blocked === true && out.length === 0) out.push('blocked');
          if (Array.isArray(n.children)) for (const c of n.children) walk(c, d + 1);
        };
        walk(r, 0);
        return out.length ? out.join(',') : 'none';
      } catch (e) { return 'err'; }
    };
    let t0 = Date.now();
    window.__hpMStart = (tag, ticks) => {
      if (window.__hpMOn) return 'already';
      window.__hpMOn = true;
      t0 = Date.now();
      let n = 0;
      let h = null;
      const tick = () => {
        n += 1;
        try {
          const bar = Array.from(document.querySelectorAll('.mud-progress-linear')).some(vis);
          const id = Array.from(document.querySelectorAll('label')).filter((e) => vis(e) && flat(e) === '아이디').length;
          const btn = Array.from(document.querySelectorAll('button')).find((e) => vis(e) && flat(e) === '로그인');
          let tok = 'x';
          try { tok = localStorage.getItem('${TOKEN_KEY}') ? '1' : '0'; } catch (e) { tok = 'err'; }
          console.log('[hp-m] s tag=' + tag + ' n=' + n + ' t=' + (Date.now() - t0) + ' path=' + location.pathname
            + ' bar=' + bar + ' id=' + id + ' btn=' + (btn ? btn.disabled === true : 'null') + ' tok=' + tok + ' nav=' + navType());
        } catch (e) {
          console.log('[hp-m] s tag=' + tag + ' n=' + n + ' t=' + (Date.now() - t0) + ' err=' + String((e && e.name) || 'Error').slice(0, 40));
        }
        if (n >= ticks && h !== null) clearInterval(h);
      };
      h = setInterval(tick, 500);
      return 'ok';
    };
    const stop0 = window.stop.bind(window);
    window.stop = function () {
      console.log('[hp-m] stop-called t=' + (Date.now() - t0) + ' path=' + location.pathname);
      return stop0();
    };
    window.addEventListener('pageshow', (e) => {
      console.log('[hp-m] pageshow persisted=' + (e.persisted === true) + ' path=' + location.pathname + ' nav=' + navType() + ' nrr=' + nrr());
      let flag = null;
      try { flag = sessionStorage.getItem('__hpM_sample'); } catch (err) { flag = null; }
      if (flag && /^[A-Za-z0-9-]{1,12}$/.test(flag)) window.__hpMStart(flag, ${OBS_SAMPLES + 4});
    });
    window.addEventListener('pagehide', (e) => {
      console.log('[hp-m] pagehide persisted=' + (e.persisted === true) + ' path=' + location.pathname);
    });
  } catch (e) {
    console.log('[hp-m] init-err ' + String((e && e.name) || 'Error').slice(0, 40));
  }
})();`;

// 표본 줄 해석(순수) — 꼴 밖이면 null
function sParse(line) {
  const m = /^\[hp-m\] s tag=([A-Za-z0-9-]{1,12}) n=(\d+) t=(\d+) (?:path=(\S{0,80}) bar=(true|false) id=(\d+) btn=(true|false|null) tok=(1|0|x|err) nav=([a-z_]{1,20})|err=([A-Za-z]{1,40}))$/.exec(String(line || ''));
  if (!m) return null;
  if (m[10]) return { tag: m[1], n: Number(m[2]), t: Number(m[3]), err: m[10] };
  return { tag: m[1], n: Number(m[2]), t: Number(m[3]), path: m[4], bar: m[5] === 'true', id: Number(m[6]),
    btn: m[7] === 'null' ? null : m[7] === 'true', tok: m[8], nav: m[9] };
}
// 멈춤 꼴(순수) — /login + 막대 보임 + 단추 꺼짐 + 아이디 칸 없음
const stuckShape = (x) => !!x && !x.err && x.path === '/login' && x.bar === true && x.btn === true && x.id === 0;
const sShow = (x) => (x ? (x.err ? `n=${x.n} t=${x.t} 실패 ${x.err}` : `경로=${x.path} 막대=${x.bar} 단추꺼짐=${x.btn} 아이디칸=${x.id} 토큰=${x.tok} 탐색=${x.nav}`) : '없음');

// ── 브라우저 한 벌 ───────────────────────────────────────────────────
async function openCtx(st, br, who, { route = true } = {}) {
  const context = await br.newContext({ baseURL: K.BASE, viewport: { width: 1366, height: 768 }, locale: 'ko-KR', timezoneId: 'Asia/Seoul' });
  const tally = { continue: 0, abort: 0 };
  // 길 B 는 경로 가로채기(Fetch)를 켜지 않는다 — 가로채기가 bfcache 를 막을 수 있어 판별이 흐려진다. 외부는 기동 인자 host-resolver-rules 로 끊는다.
  if (route) await K.blockExternal(context, st.r, tally);
  const tok = { access: null, deviceId: null, masked: 0 };
  K.watchTokens(context, tok);
  await context.addInitScript(INIT);
  const page = await context.newPage();
  const kb = { who, context, page, tok, closed: false, docs: [], hm: [], login: { seen: 0, status: null }, dcl: [], t0: Date.now() };
  st.kbs.push(kb);
  const at = () => Date.now() - kb.t0;
  page.on('console', (msg) => {
    const t = String(msg.text());
    if (t.startsWith('[hp-m]') && kb.hm.length < 600) kb.hm.push({ at: Date.now(), t: t.slice(0, 400) });
  });
  page.on('request', (req) => {
    let main = false;
    try {
      main = req.isNavigationRequest() && req.frame() === page.mainFrame();
    } catch (e) {
      main = false;
    }
    if (main && kb.docs.length < 20) kb.docs.push({ req, path: pathOnly(req.url()), end: '대기', at: at(), endAt: null, cc: null });
  });
  page.on('response', (res) => {
    const x = kb.docs.find((d) => d.req === res.request());
    if (x) {
      const h = res.headers();
      x.cc = String(h['cache-control'] || '(없음)').slice(0, 80);
    }
    if (/\/api\/auth\/login(\?|$)/i.test(res.url()) && res.request().method() === 'POST') {
      kb.login.status = res.status();
      kb.login.seen += 1;
    }
  });
  page.on('requestfinished', (req) => {
    const x = kb.docs.find((d) => d.req === req);
    if (!x) return;
    x.endAt = at();
    req.response().then((r) => { x.end = r ? String(r.status()) : '응답 없음'; }, (e) => { x.end = `응답 읽기 실패 ${errName(e)}`; });
  });
  page.on('requestfailed', (req) => {
    const x = kb.docs.find((d) => d.req === req);
    if (!x) return;
    x.endAt = at();
    const f = req.failure();
    x.end = `실패 ${L.sanitize(f && f.errorText ? f.errorText : '-', 40)}`;
  });
  page.on('domcontentloaded', () => { if (kb.dcl.length < 20) kb.dcl.push(at()); });
  return kb;
}
const docsShow = (kb) => kb.docs.map((d) => `${d.path}=${d.end}@${d.at}${d.endAt !== null ? `~${d.endAt}` : ''}`).join(' > ') || '없음';
async function closeKb(st, kb) {
  if (kb.closed) return;
  kb.closed = true;
  if (kb.tok.access) {
    const lo = await K.apiLogout({ who: kb.who, token: kb.tok.access, deviceId: kb.tok.deviceId });
    log(`${kb.who} 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  } else {
    log(`${kb.who} 로그아웃 건너뜀 — 연 화면 세션 토큰 없음`);
  }
  await kb.context.close().catch((e) => log(`컨텍스트 닫기 실패 ${errName(e)}`));
}
async function closeNotice(page) {
  try {
    const dlg = page.locator('.mud-dialog', { hasText: DEVICE_NOTICE_TITLE });
    if ((await dlg.count()) === 0) return false;
    await dlg.first().getByRole('button', { name: '확인', exact: true }).click({ timeout: 5000 });
    return true;
  } catch (e) {
    log(`기기 등록 안내 닫기 실패 ${errName(e)}`);
    return false;
  }
}
async function fillLogin(kb) {
  const page = kb.page;
  await page.goto(`${K.BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  await page.getByLabel('아이디', { exact: true }).fill(ID.staff, { timeout: 60000 });
  await page.getByLabel('비밀번호', { exact: true }).fill(kb.pw);
  await page.getByLabel('비밀번호', { exact: true }).press('Tab');
}

// ── 준비 · 치우기(대표 API · 화면과 같은 POST api/users · 약관 미리 동의) ──
async function prep(st) {
  const p = await K.apiLogin('PARENT', ID.parent, PW.parent);
  try {
    const uid = jwtClaim(p.token, 'user_id');
    if (!SAFE_ID.test(String(uid || ''))) throw new K.KitStepError('대표 번호 꼴 이상');
    st.pw = `Ui#${crypto.randomBytes(16).toString('hex')}`;
    L.maskAndRegister('PW_M1032_STAFF', st.pw);
    const res = await K.apiCall('POST', '/api/users', {
      sess: p,
      body: { email: ID.staff, password: st.pw, userName: STAFF_NAME, empName: STAFF_NAME, role: 'User', confirmDifferentPerson: true },
    });
    if (res.status !== 201 && res.status !== 200) {
      const j = res.json || {};
      throw new K.KitStepError(`직원 만들기 status=${res.status} code=${L.sanitize(String(j.code || j.message || ''), 80)}`);
    }
  } finally {
    const lo = await K.apiLogout(p);
    log(`준비 대표 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  }
  const f = await K.apiLogin('M1032', ID.staff, st.pw);
  try {
    st.staffUid = jwtClaim(f.token, 'user_id');
    const ag = await K.apiCall('POST', '/api/auth/terms/agree', {
      sess: f,
      body: { agreeService: true, agreePrivacy: true, agreeSubscription: true, agreeDataOwnership: true, agreeMarketing: false, termsVersion: 'v2.0.0' },
    });
    if (ag.status < 200 || ag.status >= 300) throw new K.KitStepError(`직원 약관 동의 status=${ag.status}`);
  } finally {
    const lo = await K.apiLogout(f);
    log(`준비 직원 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  }
}
async function release(st) {
  if (!SAFE_ID.test(String(st.staffUid || ''))) {
    log('직원 치우기 건너뜀 — 만든 계정 번호 없음');
    return;
  }
  const p = await K.apiLogin('PARENT', ID.parent, PW.parent);
  try {
    const res = await K.apiCall('POST', `/api/users/${encodeURIComponent(st.staffUid)}/suspend`, { sess: p });
    log(`직원 사용 안 함(자리 돌려줌) status=${res.status}`);
  } finally {
    const lo = await K.apiLogout(p);
    log(`치우기 대표 로그아웃 ${lo.got}`);
    if (!lo.ok) st.logoutFail += 1;
  }
}

// ── 붙잡음 장치(시험 몫) — 주 프레임 문서 요청만 붙잡는다. mode 가 'free' 가 되면 새 요청은 바로 넘긴다. ──
async function holdDocs(kb) {
  const page = kb.page;
  const h = { mode: 'hold', held: [], hits: 0, handler: null, releaseAll: null };
  let open = null;
  const gate = new Promise((ok) => { open = ok; });
  h.releaseAll = () => open();
  h.handler = async (route) => {
    let main = false;
    try {
      main = route.request().isNavigationRequest() && route.request().frame() === page.mainFrame();
    } catch (e) {
      main = false;
    }
    if (main && h.mode === 'hold') {
      h.hits += 1;
      const rec = { path: pathOnly(route.request().url()), req: route.request(), pass: '붙잡힘' };
      h.held.push(rec);
      await gate;
      await route.fallback().then(() => { rec.pass = '넘김 성공(요청이 살아 있었다)'; },
        (e) => { rec.pass = `넘김 실패 ${errName(e)}`; });
      return;
    }
    await route.fallback().catch((e) => log(`붙잡음 밖 넘김 실패 ${errName(e)}`));
  };
  await page.route('**/*', h.handler);
  return h;
}

// ── 길 A · A2 ─────────────────────────────────────────────────────────
async function sceneStop(st, br, how) {
  const label = how === 'window.stop' ? '길A' : '길A2';
  let kb = null;
  try {
    kb = await openCtx(st, br, how === 'window.stop' ? 'M1032A' : 'M1032A2');
    kb.pw = st.pw;
    const page = kb.page;
    const cdp = await kb.context.newCDPSession(page);
    await fillLogin(kb);
    const hold = await holdDocs(kb);
    const arm = await K.withTimeout(page.evaluate(`window.__hpMStart ? window.__hpMStart('A', 160) : 'no-init'`), K.EVAL_MS, '표본기 걸기')
      .then((v) => String(v), (e) => `실패 ${errName(e)}`);
    log(`${label} 표본기 걸기 ${arm}`);
    const clickAt = Date.now();
    await page.getByRole('button', { name: '로그인', exact: true }).click();
    // 첫 붙잡음까지(새 기기 안내가 뜨면 사람처럼 닫는다 — 붙잡음 전이라 문서 이동 대기 없음)
    let notices = 0;
    const end = Date.now() + 45000;
    while (hold.hits === 0 && Date.now() < end) {
      if (await closeNotice(page)) notices += 1;
      await sleep(300);
    }
    if (hold.hits === 0) throw new K.KitStepError(`붙잡음 적중 0(45초) · 로그인 응답 ${kb.login.seen}회 status=${kb.login.status} · 안내 닫음 ${notices}`);
    const firstHitMs = Date.now() - clickAt;
    await sleep(HOLD_SETTLE_MS);
    // 중지 직전 상태
    const pendingAtStop = hold.held.filter((x) => {
      const d = kb.docs.find((z) => z.req === x.req);
      return d && d.end === '대기';
    }).map((x) => x.req);
    const before = docsShow(kb);
    hold.mode = 'free'; // 중지 뒤 새 문서 요청은 붙잡지 않는다(안전망 · 대조 쪽 이동이 장치 탓에 막히지 않게)
    const stopAt = Date.now();
    let stopRes;
    if (how === 'window.stop') {
      stopRes = await K.withTimeout(cdp.send('Runtime.evaluate', { expression: 'window.stop(), "ok"', returnByValue: true }), 5000, 'CDP Runtime.evaluate window.stop')
        .then((r) => (r && r.result ? `값 ${String(r.result.value)}${r.exceptionDetails ? ' · 예외' : ''}` : '응답 꼴 이상'), (e) => `실패 ${e instanceof K.KitStepError ? e.message : errName(e)}`);
    } else {
      stopRes = await K.withTimeout(cdp.send('Page.stopLoading'), 5000, 'CDP Page.stopLoading')
        .then(() => '보냄', (e) => `실패 ${e instanceof K.KitStepError ? e.message : errName(e)}`);
    }
    await sleep(RELEASE_AFTER_MS);
    hold.releaseAll();
    await sleep(OBS_MS - RELEASE_AFTER_MS);
    // 붙잡았던 요청 끝
    const heldShow = hold.held.map((x) => {
      const d = kb.docs.find((z) => z.req === x.req);
      return `${x.path} 끝=${d ? d.end : '?'} · ${x.pass}${pendingAtStop.includes(x.req) ? ' · 중지 때 대기' : ' · 중지 전 끝남'}`;
    }).join(' | ') || '없음';
    const stopLine = kb.hm.find((x) => x.t.startsWith('[hp-m] stop-called') && x.at >= stopAt - 50);
    const samples = kb.hm.filter((x) => x.at >= stopAt).map((x) => sParse(x.t)).filter((x) => x && x.tag === 'A').slice(0, OBS_SAMPLES);
    const pageshowAfter = kb.hm.filter((x) => x.at >= stopAt && /^\[hp-m\] page(show|hide)/.test(x.t)).map((x) => x.t.replace('[hp-m] ', ''));
    const dclAfter = kb.dcl.filter((t) => t >= stopAt - kb.t0).length;
    // 성립 조건: 중지가 문서에서 돌았고(길 A = stop-called 줄 · 길 A2 = CDP 보냄) · 중지 때 대기였던 문서 요청이 1 이상이며
    //   그 요청이 모두 실패(취소)로 끝났고 · 중지 뒤 새 문서 적재(domcontentloaded) 0
    const pend = pendingAtStop.map((r) => kb.docs.find((z) => z.req === r)).filter(Boolean);
    const stopRan = how === 'window.stop' ? !!stopLine : stopRes === '보냄';
    const cancelled = pend.length >= 1 && pend.every((d) => /^실패/.test(d.end));
    const valid = stopRan && cancelled && dclAfter === 0;
    const last = samples[samples.length - 1] || null;
    const stuckN = samples.filter(stuckShape).length;
    const firstNot = samples.findIndex((x) => !stuckShape(x));
    const verdict = !valid ? '측정 불성립' : (samples.length >= OBS_SAMPLES - 2 && stuckShape(last) ? '멈춤(20초 뒤에도 /login + 막대 + 단추 꺼짐 + 아이디 칸 없음)' : '멈춤 아님');
    log(`${label} 붙잡음 첫 적중 +${firstHitMs}ms · 적중 ${hold.hits} · 안내 닫음 ${notices} · 로그인 응답 ${kb.login.seen}회 status=${kb.login.status}`);
    log(`${label} 중지 전 문서 ${L.sanitize(before, 300)}`);
    log(`${label} 중지 방법 ${how} · CDP ${L.sanitize(stopRes, 120)} · 문서 안 stop 불림 ${stopLine ? L.sanitize(stopLine.t.replace('[hp-m] ', ''), 80) : '못 봄'}`);
    log(`${label} 붙잡은 요청 ${L.sanitize(heldShow, 400)}`);
    log(`${label} 중지 뒤 새 문서 적재 ${dclAfter} · pageshow/hide ${L.sanitize(pageshowAfter.join(' ; ') || '없음', 200)}`);
    samples.forEach((x, i) => log(`${label} 표본 ${i + 1}/${OBS_SAMPLES} t=${x.t} ${sShow(x)}`));
    log(`${label} 문서 전체 ${L.sanitize(docsShow(kb), 400)}`);
    log(`${label} 끝 판정=${verdict} · 성립 중지돎=${stopRan} 취소됨=${cancelled} 새문서=${dclAfter} · 표본 ${samples.length} · 멈춤 꼴 ${stuckN} · 첫 멈춤 아님 ${firstNot < 0 ? '없음' : `#${firstNot + 1}`} · 끝 표본 ${sShow(last)}`);
    // 교차 확인(평가 1번 · 실패해도 기록만)
    const cross = await K.withTimeout(page.evaluate((k) => ({
      path: location.pathname,
      tok: localStorage.getItem(k) ? 1 : 0,
      bar: Array.from(document.querySelectorAll('.mud-progress-linear')).some((e) => e.getClientRects().length > 0),
    }), TOKEN_KEY), 5000, '끝 교차 확인').then((v) => `경로=${v.path} 토큰=${v.tok} 막대=${v.bar}`, (e) => `실패 ${e instanceof K.KitStepError ? e.message : errName(e)}`);
    log(`${label} 끝 교차 확인(평가) ${L.sanitize(cross, 120)}`);
    await page.unroute('**/*', hold.handler).catch((e) => log(`${label} 붙잡음 풀기 실패 ${errName(e)}`));
  } catch (e) {
    log(`${label} 끝 판정=장치 실패 · ${e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${errName(e)}`}${kb ? ` · 문서 ${L.sanitize(docsShow(kb), 300)}` : ''}`);
  } finally {
    if (kb) await closeKb(st, kb);
  }
}

// ── 길 B ──────────────────────────────────────────────────────────────
async function sceneBack(st, br, brNote) {
  const label = '길B';
  let kb = null;
  try {
    kb = await openCtx(st, br, 'M1032B', { route: false });
    kb.pw = st.pw;
    const page = kb.page;
    const cdp = await kb.context.newCDPSession(page).catch((e) => { log(`${label} CDP 세션 실패 ${errName(e)}`); return null; });
    const bfNot = [];
    if (cdp) {
      await cdp.send('Page.enable').catch((e) => log(`${label} CDP Page.enable 실패 ${errName(e)}`));
      cdp.on('Page.backForwardCacheNotUsed', (ev) => {
        const xs = (ev && Array.isArray(ev.notRestoredExplanations) ? ev.notRestoredExplanations : []).map((x) => `${x.type}:${x.reason}`);
        bfNot.push(xs.join(',').replace(/[^A-Za-z0-9:,_-]/g, '').slice(0, 300) || '(빈 목록)');
      });
    }
    await fillLogin(kb);
    const clickAt = Date.now();
    await page.getByRole('button', { name: '로그인', exact: true }).click();
    let notices = 0;
    let landed = null;
    const end = Date.now() + 60000;
    while (Date.now() < end) {
      if (await closeNotice(page)) notices += 1;
      const p = pathOnly(page.url());
      if (p !== '/login' && kb.docs.some((d) => d.path === p && d.end === '200')) { landed = p; break; }
      await sleep(500);
    }
    if (!landed) throw new K.KitStepError(`착지 못 함(60초) · 지금 ${pathOnly(page.url())} · 로그인 응답 ${kb.login.seen}회 status=${kb.login.status}`);
    const landMs = Date.now() - clickAt;
    await sleep(4000);
    const docsBefore = kb.docs.length;
    const flag = await K.withTimeout(page.evaluate(() => { sessionStorage.setItem('__hpM_sample', 'B'); return 'ok'; }), 5000, '표지 두기')
      .then((v) => v, (e) => `실패 ${errName(e)}`);
    const backAt = Date.now();
    const back = await page.goBack({ waitUntil: 'commit', timeout: 20000 })
      .then((r) => (r ? `응답 ${r.status()}` : '응답 null(복원 또는 같은 문서)'), (e) => `실패 ${errName(e)}`);
    await sleep(OBS_MS);
    const newDocs = kb.docs.slice(docsBefore);
    const shows = kb.hm.filter((x) => x.at >= backAt - 50 && /^\[hp-m\] page(show|hide)/.test(x.t)).map((x) => x.t.replace('[hp-m] ', ''));
    const showAfter = kb.hm.find((x) => x.at >= backAt - 50 && x.t.startsWith('[hp-m] pageshow'));
    const persisted = showAfter ? /persisted=true/.test(showAfter.t) : null;
    const loginDocAfter = newDocs.some((d) => d.path === '/login');
    const restoreJudge = persisted === null ? '판별 못 함(pageshow 못 봄)'
      : persisted && !loginDocAfter ? '복원됨(bfcache · persisted=true · /login 문서 요청 0)'
        : !persisted && loginDocAfter ? '복원 안 됨(새 문서 · /login 문서 요청 있음)'
          : `판별 불일치(persisted=${persisted} · /login 문서 요청 ${loginDocAfter})`;
    const samples = kb.hm.filter((x) => x.at >= backAt).map((x) => sParse(x.t)).filter((x) => x && x.tag === 'B').slice(0, OBS_SAMPLES);
    const last = samples[samples.length - 1] || null;
    const stuckN = samples.filter(stuckShape).length;
    const verdict = persisted !== true ? `멈춤 판정 대상 아님(${restoreJudge}) · 끝 ${sShow(last)}`
      : (samples.length >= OBS_SAMPLES - 2 && stuckShape(last) ? '멈춤(복원 문서가 20초 뒤에도 /login + 막대 + 단추 꺼짐 + 아이디 칸 없음)' : `멈춤 아님 · 끝 ${sShow(last)}`);
    log(`${label} 브라우저 ${brNote} · 착지 ${landed} +${landMs}ms · 안내 닫음 ${notices} · 로그인 응답 ${kb.login.seen}회 status=${kb.login.status}`);
    log(`${label} 착지까지 문서 ${L.sanitize(kb.docs.slice(0, docsBefore).map((d) => `${d.path}=${d.end} cc=${d.cc}`).join(' > ') || '없음', 400)}`);
    log(`${label} 표지 ${flag} · goBack ${L.sanitize(back, 80)} · 뒤 문서 ${L.sanitize(newDocs.map((d) => `${d.path}=${d.end} cc=${d.cc}`).join(' > ') || '없음', 300)}`);
    log(`${label} pageshow/hide ${L.sanitize(shows.join(' ; ') || '없음', 400)}`);
    log(`${label} CDP bfcache 안 씀 까닭 ${L.sanitize(bfNot.join(' | ') || '이벤트 없음', 400)}`);
    samples.forEach((x, i) => log(`${label} 표본 ${i + 1}/${OBS_SAMPLES} t=${x.t} ${sShow(x)}`));
    log(`${label} 끝 판정=${restoreJudge} · ${verdict} · 표본 ${samples.length} · 멈춤 꼴 ${stuckN}`);
  } catch (e) {
    log(`${label} 끝 판정=장치 실패 · ${e instanceof K.KitStepError ? L.sanitize(e.message, 200) : `예외 ${errName(e)}`}${kb ? ` · 문서 ${L.sanitize(docsShow(kb), 300)}` : ''}`);
  } finally {
    if (kb) await closeKb(st, kb);
  }
}

async function main() {
  const st = { r: {}, kbs: [], logoutFail: 0, pw: null, staffUid: null, cleaned: false };
  const brs = [];
  const cleanup = async () => {
    if (st.cleaned) return;
    st.cleaned = true;
    for (const kb of st.kbs) await closeKb(st, kb);
    await release(st).catch((e) => log(`직원 치우기 실패 ${e instanceof K.KitStepError ? L.sanitize(e.message, 120) : errName(e)}`));
    for (const b of brs) await b.close().catch((e) => log(`브라우저 닫기 실패 ${errName(e)}`));
  };
  const fatal = (where) => (e) => {
    log(`프로세스 밖 예외(${where}) ${errName(e)}`);
    setTimeout(() => process.exit(1), 15000).unref();
    cleanup().then(() => process.exit(1), () => process.exit(1));
  };
  process.on('unhandledRejection', fatal('unhandledRejection'));
  process.on('uncaughtException', fatal('uncaughtException'));
  setTimeout(() => fatal('전체 시간 상한')(new K.KitStepError('전체 시간 상한')), TOTAL_MS).unref();

  const pw = require('playwright');
  log(`시작 · 가지 꼴 ${ARM} · playwright ${require('playwright/package.json').version}`);
  try {
    await prep(st);
    log('준비 통과(직원 만들기 · 약관 동의)');
  } catch (e) {
    log(`준비 실패 — ${e instanceof K.KitStepError ? L.sanitize(e.message, 160) : errName(e)}`);
    await cleanup();
    process.exit(1);
  }
  // 길 B — 문서 복원 캐시가 켜질 수 있는 브라우저(Playwright 기본 인자의 bfcache 끔 줄을 뺌 · 가능하면 새 headless = channel chromium)
  //   외부 요청은 경로 가로채기 대신 이름 풀이 규칙으로 끊는다(127.0.0.1 만 남김).
  const optB = { ignoreDefaultArgs: ['--disable-back-forward-cache'], args: ['--host-resolver-rules=MAP * ~NOTFOUND , EXCLUDE 127.0.0.1'] };
  let brB = null;
  let brBNote = '';
  try {
    brB = await pw.chromium.launch({ channel: 'chromium', ...optB });
    brBNote = 'channel=chromium · 기본 인자 --disable-back-forward-cache 뺌 · 외부 = 이름 풀이 끊음';
  } catch (e) {
    log(`길B channel=chromium 기동 실패 ${errName(e)} — 기본 실행 파일로`);
    brB = await pw.chromium.launch(optB);
    brBNote = '기본 실행 파일 · 기본 인자 --disable-back-forward-cache 뺌 · 외부 = 이름 풀이 끊음';
  }
  brs.push(brB);
  await sceneBack(st, brB, brBNote);
  // 길 A · A2 — 기존 장면(terms-slow-doc)과 같은 기본 기동
  const brA = await pw.chromium.launch();
  brs.push(brA);
  await sceneStop(st, brA, 'window.stop');
  await sceneStop(st, brA, 'Page.stopLoading');
  await cleanup();
  log(`본문 끝 · 로그아웃 실패 ${st.logoutFail}`);
  process.exit(st.logoutFail === 0 ? 0 : 1);
}

if (require.main === module) main();
module.exports = { sParse, stuckShape, INIT };
