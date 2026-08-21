// 20260821 실측 — 작11(확정권한) · 작10 A(근태 대리입력) · G-5(권한 화면 체크박스)
//
//   대상: T-004 테스트 테넌트 (test1234.hitpan.kr)
//   🔴 터널 주소로 잰다 — 개발PC localhost 는 "아무도 안 가는 길" 이다.
//      (feedback_verify_real_usage_path: 개발PC=localhost, 고객·사장님=터널)
//   🔴 헌법 #39 — 스크립트가 스스로 "운영 아님" 을 확인하고, 아니면 멈춘다.
//
//   PM 주도 · 검증팀 SoD: 판정 근거를 전부 출력해 사람이 반증할 수 있게 한다.
//   판정 규율(인수인계서 §1-2): 초록불이 어디서 오는지 밝힌다. 문자열 존재로 판정하지 않는다.

const { chromium } = require('playwright');
const https = require('https');
const http = require('http');
const fs = require('fs');

const BASE = process.env.HITPAN_BASE || 'https://test1234.hitpan.kr';
// 🔴 계정은 코드에 넣지 않는다 — 환경변수로만 받는다.
//   실행:  HITPAN_EMAIL=... HITPAN_PASS=... node scripts/playwright-audit/measure-work11-t004-20260821.js
const EMAIL = process.env.HITPAN_EMAIL;
const PASS = process.env.HITPAN_PASS;
if (!EMAIL || !PASS) {
    console.error('🔴 HITPAN_EMAIL / HITPAN_PASS 환경변수가 필요하다. 계정을 코드에 넣지 않는다.');
    process.exit(1);
}

const R = [];
const rec = (id, what, got, pass, note) => {
    R.push({ id, what, got, pass, note: note || '' });
    const tag = pass === true ? '🟢 PASS' : pass === false ? '🔴 FAIL' : '⚪ INFO';
    console.log(`${tag}  ${id}  ${what}`);
    console.log(`         → ${got}`);
    if (note) console.log(`         · ${note}`);
};

function api(path, { method = 'GET', token, deviceId, body } = {}) {
    return new Promise((resolve, reject) => {
        const url = new URL(BASE + path);
        const lib = url.protocol === 'https:' ? https : http;
        const data = body ? JSON.stringify(body) : null;
        const headers = {};
        if (data) {
            headers['Content-Type'] = 'application/json';
            headers['Content-Length'] = Buffer.byteLength(data);
        }
        if (token) headers['Authorization'] = 'Bearer ' + token;
        if (deviceId) headers['X-HitPan-Device-Id'] = deviceId;
        const req = lib.request({
            hostname: url.hostname,
            port: url.port || (url.protocol === 'https:' ? 443 : 80),
            path: url.pathname + url.search,
            method, headers, timeout: 20000
        }, res => {
            let d = '';
            res.on('data', c => d += c);
            res.on('end', () => {
                let j = null;
                try { j = JSON.parse(d); } catch { }
                resolve({ status: res.statusCode, json: j, raw: d });
            });
        });
        req.on('timeout', () => { req.destroy(); resolve({ status: 0, json: null, raw: 'TIMEOUT' }); });
        req.on('error', e => resolve({ status: 0, json: null, raw: 'ERR ' + e.message }));
        if (data) req.write(data);
        req.end();
    });
}

const enc = v => Buffer.from(JSON.stringify(v), 'utf-8').toString('base64');

(async () => {
    console.log('='.repeat(78));
    console.log('실측 — 작11 확정권한 · 작10 A 근태대리입력 · G-5 권한화면');
    console.log(`대상: ${BASE}  (터널 = 고객이 실제로 가는 길)`);
    console.log('='.repeat(78));

    // ── [0] 환경 증명 (헌법 #39) ──────────────────────────────────────
    console.log('\n### [0] 환경 증명 — 운영이 아님을 먼저 증명한다\n');

    const health = await api('/health');
    // ⚠️ 버전은 json.version 이 아니라 checks.version 에 있다(초판 스크립트가 여기서 헛다리를 짚어 FAIL 을 냈다).
    const ver = health.json?.checks?.version;
    rec('E-1', 'API 도달 · 버전', `HTTP ${health.status} · v${ver} · db=${health.json?.checks?.database}`,
        health.status === 200 && ver === '1.3.0');

    const login = await api('/api/auth/login', { method: 'POST', body: { email: EMAIL, password: PASS } });
    if (login.status !== 200) {
        rec('E-2', '로그인', `HTTP ${login.status} · ${login.raw.slice(0, 120)}`, false);
        console.log('\n🔴 로그인 실패 — 중단한다.');
        process.exit(1);
    }
    const TOKEN = login.json.accessToken;
    const payload = JSON.parse(Buffer.from(
        TOKEN.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf-8'));
    rec('E-2', '로그인 · account_type', `${payload.account_type} · tenant=${payload.tenant_id.slice(0, 8)}…`, true,
        'tenant_admin 이면 Layer 0 바이패스 대상 — 이 사실이 뒤 판정의 전제다');

    const devs = await api('/api/devices', { token: TOKEN });
    const mainPc = (devs.json || []).find(d => d.isMainPc);
    rec('E-3', '메인PC 기기줄 (정상 통행로 축②)',
        mainPc ? `${mainPc.deviceName} · status=${mainPc.status}` : '없음', !!mainPc,
        'appsettings 를 고치지 않고 승인된 기기줄로 통과한다 (헌법 #21·#29)');
    const DEV = mainPc?.deviceId;

    const emps = await api('/api/employees', { token: TOKEN, deviceId: DEV });
    const empCount = (emps.json || []).length;
    const isSafe = empCount >= 1 && empCount <= 2;
    rec('E-4', '사원 수 (demo 는 12명이었다)', `${empCount}명`, isSafe,
        isSafe ? 'T-004 백지 테넌트 — 운영 아님 확인' : '⚠️ 운영 의심');
    if (!isSafe) { console.log('\n🔴 운영 의심 — 헌법 #39 에 따라 중단한다.'); process.exit(1); }

    // ── [1] 작11 전제 — 확정 4곳이 권한을 보나 ─────────────────────────
    console.log('\n### [1] 작11 전제 — 확정 API 가 권한을 보나\n');

    const perms = await api('/api/permissions', { token: TOKEN, deviceId: DEV });
    const me = (perms.json || [])[0];
    const codes = (me?.permissions || []).map(p => p.menuCode);
    const hasSales = codes.includes('SALES');
    rec('W-1', '메뉴코드에 SALES 가 있나 (설8 초안 반증)',
        hasSales ? '🔴 SALES 있음 — 설8 이 맞았다'
            : `SALES 없음 · DELIVERY=${codes.includes('DELIVERY')} · PURCHASE=${codes.includes('PURCHASE')} (총 ${codes.length}개)`,
        !hasSales && codes.includes('DELIVERY') && codes.includes('PURCHASE'),
        '작11 정정(DELIVERY·PURCHASE)이 실물과 맞는지 — 소스가 아니라 돌아가는 응답으로 판정');

    const dlv = (me?.permissions || []).find(p => p.menuCode === 'DELIVERY');
    rec('W-2', '대표의 DELIVERY 권한 상태',
        `view=${dlv?.canView} · create=${dlv?.canCreate} · update=${dlv?.canUpdate}`,
        dlv?.canUpdate === false,
        '전부 false 인 상태로 확정을 시도한다 — 막히면 권한이 있는 것, 안 막히면 없는 것');

    const CONFIRMS = [
        ['W-3', '/api/sales/deliveries/PLAYWRIGHT-NOEXIST/confirm', '거래명세서 확정'],
        ['W-4', '/api/purchase/receipts/PLAYWRIGHT-NOEXIST/confirm', '매입 확정'],
        ['W-5', '/api/purchase/returns/PLAYWRIGHT-NOEXIST/confirm', '매입반품 확정'],
        ['W-6', '/api/sales/returns/PLAYWRIGHT-NOEXIST/confirm', '판매반품 확정(유일하게 통제됨)'],
    ];
    for (const [id, ep, label] of CONFIRMS) {
        const r = await api(ep, { method: 'POST', token: TOKEN, deviceId: DEV, body: {} });
        rec(id, `${label}`, `HTTP ${r.status}`, r.status !== 403,
            r.status === 403
                ? '403 = 권한이 막았다'
                : '403 아님 = 인가를 통과해 업무로직까지 갔다 (막는 것이 없다)');
    }

    // ── [2] 작10 A — 테넌트 격리: 대조가 판정한다 ──────────────────────
    console.log('\n### [2] 작10 A — 근태 대리입력 테넌트 격리 ([3-V]가 뚫었던 자리)\n');

    const mineId = (emps.json || [])[0]?.employeeId;
    const foreign = await api('/api/hr/attendance/proxy/check-in', {
        method: 'POST', token: TOKEN, deviceId: DEV,
        body: { employeeId: '00000000-0000-0000-0000-000000000999' }
    });
    const mine = await api('/api/hr/attendance/proxy/check-in', {
        method: 'POST', token: TOKEN, deviceId: DEV, body: { employeeId: mineId }
    });
    const fMsg = (foreign.json?.error || foreign.raw || '').slice(0, 80);
    const mMsg = (mine.json?.error || mine.raw || '').slice(0, 80);

    rec('A-1', '남의 회사 employee_id 지정', `HTTP ${foreign.status} · "${fMsg}"`, foreign.status !== 200,
        '200 이면 테넌트 격리가 뚫린 것 = P0');
    rec('A-2', '자기 회사 employee_id 지정', `HTTP ${mine.status} · "${mMsg}"`, null,
        '이 줄 자체는 합격/불합격이 아니다 — 다음 줄의 대조 재료다');
    rec('A-3', '🔴 두 응답이 갈리나 (대조 판정)',
        fMsg === mMsg ? `같은 메시지 — 구분 불가 ("${fMsg}")` : `다르다 · 남="${fMsg}" / 내="${mMsg}"`,
        fMsg !== mMsg,
        '같으면 "그냥 다 실패하는 것" 과 구분 못 한다. 달라야 가드가 소속을 실제로 본 것이다');
    rec('A-4', '자동 근퇴 회귀 없음',
        /이미 출근/.test(mMsg) ? '"오늘 이미 출근" = 로그인 자동출근이 돌았다는 증거' : `확인 불가 ("${mMsg}")`,
        /이미 출근/.test(mMsg),
        'AuthController.cs:236-258 자동 출근이 살아 있나');

    // ── [3] G-5 — 권한 화면 체크박스 (브라우저만 볼 수 있다) ────────────
    console.log('\n### [3] G-5 — 권한 화면 체크박스 실재 (API 로는 판정 불가)\n');
    console.log('     이유: EnforcedMenus 는 프론트 필터다. API 는 전체 목록을 돌려주므로');
    console.log('           "API 에 있다" 가 "화면에 뜬다" 를 뜻하지 않는다.\n');

    // 설치된 브라우저 빌드가 playwright 기대 리비전과 어긋날 때를 대비해
    //   HITPAN_CHROME 로 실행 파일을 직접 지정할 수 있게 한다(실측이 도구 문제로 멈추지 않도록).
    const chromePath = process.env.HITPAN_CHROME || undefined;
    const browser = await chromium.launch({ headless: true, executablePath: chromePath });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1600, height: 1000 } });
    await ctx.addInitScript(({ a, r, u, d }) => {
        localStorage.setItem('hitpan_access_token', a);
        localStorage.setItem('hitpan_refresh_token', r);
        localStorage.setItem('hitpan_user_name', u);
        localStorage.setItem('hitpan_device_id', d);   // 원문 저장 (토큰과 달리 base64 아님)
    }, { a: enc(TOKEN), r: enc(login.json.refreshToken), u: enc(login.json.userName), d: DEV });

    const page = await ctx.newPage();
    const errs = [];
    page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 140)); });
    page.on('pageerror', e => errs.push('PAGEERR ' + String(e).slice(0, 140)));

    await page.goto(`${BASE}/users/permissions`, { waitUntil: 'networkidle', timeout: 60000 }).catch(() => { });
    await page.waitForTimeout(8000);
    const permText = await page.evaluate(() => document.body.innerText).catch(() => '');

    rec('G-5a', '권한 화면이 실제로 떴나',
        permText.includes('권한') ? '권한 화면 렌더됨' : `렌더 실패 — 본문: "${permText.slice(0, 80)}"`,
        permText.includes('권한'),
        '안 떴으면 아래 판정은 전부 의미 없다 (초록불 출처 확인)');

    // 대조군: 반드시 있어야 하는 것 / 지금은 없어야 하는 것
    const LABELS = [
        ['G-5b', '근태 대리입력', true, 'HR_PROXY — 작10 A 가 EnforcedMenus 에 넣었다. 없으면 작10 A 가 화면에서 죽은 것'],
        ['G-5c', '거래명세서', false, 'DELIVERY — 작11 착수 전이라 지금은 "안 보이는 게 정상". 보이면 이미 등록된 것'],
        ['G-5d', '매입명세서', false, 'PURCHASE — 위와 동일'],
    ];
    for (const [id, label, expected, note] of LABELS) {
        const shown = permText.includes(label);
        rec(id, `권한 화면에 "${label}" 이 뜨나`, shown ? '보임' : '안 보임', shown === expected, note);
    }

    fs.mkdirSync('scripts/playwright-audit/shots', { recursive: true });
    await page.screenshot({ path: 'scripts/playwright-audit/shots/20260821-permissions.png', fullPage: true }).catch(() => { });

    await page.goto(`${BASE}/hr/attendance`, { waitUntil: 'networkidle', timeout: 60000 }).catch(() => { });
    await page.waitForTimeout(7000);
    const hrText = await page.evaluate(() => document.body.innerText).catch(() => '');
    rec('G-6', '근태 화면이 떴나',
        (hrText.includes('근태') || hrText.includes('출근')) ? '렌더됨' : `실패 — "${hrText.slice(0, 80)}"`,
        hrText.includes('근태') || hrText.includes('출근'));
    await page.screenshot({ path: 'scripts/playwright-audit/shots/20260821-attendance.png', fullPage: true }).catch(() => { });

    rec('G-7', '브라우저 콘솔 오류', errs.length ? `${errs.length}건 · 첫 건: ${errs[0]}` : '0건', errs.length === 0);

    await browser.close();

    // ── 요약 ──────────────────────────────────────────────────────────
    console.log('\n' + '='.repeat(78));
    const p = R.filter(x => x.pass === true).length;
    const f = R.filter(x => x.pass === false).length;
    const i = R.filter(x => x.pass === null).length;
    console.log(`판정: 🟢 PASS ${p} · 🔴 FAIL ${f} · ⚪ INFO ${i}   (총 ${R.length})`);
    console.log('='.repeat(78));
    if (f) {
        console.log('\n🔴 FAIL 목록:');
        R.filter(x => x.pass === false).forEach(x => console.log(`   ${x.id}  ${x.what}\n        → ${x.got}`));
    }
    fs.writeFileSync('scripts/playwright-audit/result-work11-20260821.json',
        JSON.stringify({ base: BASE, at: new Date().toISOString(), results: R }, null, 2), 'utf-8');
    console.log('\n결과: scripts/playwright-audit/result-work11-20260821.json');
    console.log('화면: scripts/playwright-audit/shots/');
})();
