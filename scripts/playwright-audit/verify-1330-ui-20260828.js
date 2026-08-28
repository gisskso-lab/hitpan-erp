// 20260828 실측 — 1.3.30 UI 종단 검증 (검증팀 · 데이비드 박 산하)
//
//   대상: https://test1234.hitpan.kr  (🔴 터널로 잰다)
//   판정 규율:
//     🔴 HTTP 코드로 판정하지 않는다 — SPA 라 없는 주소도 200 이다. 본문(DOM)으로 가른다.
//     🔴 문자열 존재만으로 PASS 하지 않는다 — 값·행수·동작을 본다.
//     🔴 "고쳤나" 가 아니라 "갔나" — 서버가 옳아도 화면에 안 닿으면 FAIL.
//     🔴 못 잰 것은 INFO(미확인) 로 남긴다. 추측으로 PASS 주지 않는다.

const { chromium } = require('playwright');
const https = require('https');
const fs = require('fs');
const path = require('path');

const BASE = process.env.HITPAN_BASE || 'https://test1234.hitpan.kr';
const EMAIL = process.env.HITPAN_EMAIL || 'act0226';
const PASS = process.env.HITPAN_PASS || '11111111';
const WANT_VER = process.env.HITPAN_VER || '1.3.30';
const SHOTS = path.join(__dirname, 'shots', '1330-20260828');
fs.mkdirSync(SHOTS, { recursive: true });

const R = [];
const rec = (id, what, got, pass, note) => {
    R.push({ id, what, got, pass, note: note || '' });
    const tag = pass === true ? '🟢 PASS' : pass === false ? '🔴 FAIL' : '⚪ INFO';
    console.log(`${tag}  ${id}  ${what}`);
    console.log(`         → ${got}`);
    if (note) console.log(`         · ${note}`);
};

function api(p, opt) {
    opt = opt || {};
    const method = opt.method || 'GET';
    return new Promise((resolve) => {
        const url = new URL(BASE + p);
        const data = opt.body ? JSON.stringify(opt.body) : null;
        const headers = {};
        if (data) { headers['Content-Type'] = 'application/json'; headers['Content-Length'] = Buffer.byteLength(data); }
        if (opt.token) headers['Authorization'] = 'Bearer ' + opt.token;
        if (opt.deviceId) headers['X-HitPan-Device-Id'] = opt.deviceId;
        const req = https.request({
            hostname: url.hostname, port: 443, path: url.pathname + url.search,
            method, headers, timeout: 30000
        }, res => {
            let d = ''; res.on('data', c => d += c);
            res.on('end', () => { let j = null; try { j = JSON.parse(d); } catch (e) { } resolve({ status: res.statusCode, json: j, raw: d }); });
        });
        req.on('timeout', () => { req.destroy(); resolve({ status: 0, json: null, raw: 'TIMEOUT' }); });
        req.on('error', e => resolve({ status: 0, json: null, raw: 'ERR ' + e.message }));
        if (data) req.write(data);
        req.end();
    });
}

// 화면 실패 판별 문구 — SPA 200 을 본문으로 가른다
const FAIL_KW = ['페이지를 찾을 수 없', 'nothing at this address', 'Not found',
    '오류가 발생', 'An unhandled error has occurred', 'Unhandled exception'];
const LOGIN_KW = ['아이디를 입력하세요', '비밀번호를 입력하세요'];
// 고객 노출 개발용어 후보 (본문에 그대로 새면 안 되는 것)
const JARGON = ['NullReferenceException', 'MySqlException', 'Microsoft.AspNetCore',
    'Stack trace', 'tenant_id', 'source_type', 'journal_lines', 'Internal Server Error'];

const nav = [];       // 네트워크 4xx/5xx
const pageErrs = [];  // pageerror
const consoleErrs = [];

(async () => {
    console.log('='.repeat(78));
    console.log(`실측 — 1.3.30 UI 종단 (20260828) · ${BASE}`);
    console.log('='.repeat(78));

    // ── [A] 환경 증명 ──
    console.log('\n### [A] 환경 증명\n');
    const health = await api('/health');
    const ver = (health.json && health.json.checks && health.json.checks.version) || (health.json && health.json.version);
    rec('A-1', 'API · 배포 버전', `HTTP ${health.status} · v${ver}`,
        health.status === 200 && ver === WANT_VER,
        `🔴 ${WANT_VER} 가 아니면 옛 코드를 재는 것 — 판정 무효`);
    if (ver !== WANT_VER) { console.log('\n🔴 버전 불일치 — 중단'); save(); process.exit(1); }

    const login = await api('/api/auth/login', { method: 'POST', body: { email: EMAIL, password: PASS } });
    if (login.status !== 200 || !(login.json && login.json.accessToken)) {
        rec('A-2', 'API 로그인', `HTTP ${login.status} · ${login.raw.slice(0, 120)}`, false);
        save(); process.exit(1);
    }
    const TOKEN = login.json.accessToken;
    rec('A-2', 'API 로그인', 'HTTP 200 · accessToken 수신', true);

    const devs = await api('/api/devices', { token: TOKEN });
    const devList = Array.isArray(devs.json) ? devs.json : [];
    const main = devList.find(d => d.isMainPc);
    const DEV = (main && main.deviceId) || (devList[0] && devList[0].deviceId);
    rec('A-3', '기기줄(deviceId)', DEV ? DEV.slice(0, 16) + '…' : '없음', !!DEV);

    const emps = await api('/api/employees', { token: TOKEN, deviceId: DEV });
    const n = Array.isArray(emps.json) ? emps.json.length : -1;
    rec('A-4', '사원 수 (운영 아님 확인 · 헌법 #39)', `${n}명`, n >= 0 && n <= 5);
    if (n > 5) { console.log('\n🔴 운영 의심 — 즉시 중단(#39)'); save(); process.exit(1); }

    // ── 브라우저 ──
    // 🔴 설치된 리비전(1234)과 패키지 기대치(1223)가 달라 기본 경로가 비어 있다.
    //    없는 브라우저로 재면 판정 자체가 안 되므로 실재하는 실행파일을 직접 지정한다.
    const CHROME = process.env.HITPAN_CHROME ||
        'C:\\Users\\소순근\\AppData\\Local\\ms-playwright\\chromium-1234\\chrome-win64\\chrome.exe';
    const launchOpt = { headless: true };
    if (fs.existsSync(CHROME)) { launchOpt.executablePath = CHROME; console.log(`  (브라우저: ${CHROME})`); }
    const browser = await chromium.launch(launchOpt);
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1600, height: 1000 } });

    // 🔴 1차 실행이 통째로 무효였던 이유 — 여기에 적어 둔다.
    //   새로 띄운 headless 브라우저는 `hitpan_device_id` 가 비어 있어 **미승인 기기**다.
    //   DeviceAuthMiddleware 가 모든 API 를 403 forbidden_device_auth 로 막았고(500건),
    //   그 결과 정합성 화면이 "0건" 으로 보였다. 그건 화면 결함이 아니라 **내 시험 장비 결함**이다.
    //   ⇒ 이미 승인된 메인PC 기기번호를 브라우저 저장소에 심어 고객과 같은 조건으로 맞춘다.
    //     (브라우저 localStorage 만 건드린다 — DB 쓰기 아님)
    if (DEV) {
        await ctx.addInitScript(id => {
            try { localStorage.setItem('hitpan_device_id', id); } catch (e) { }
        }, DEV);
    }

    const page = await ctx.newPage();

    page.on('pageerror', e => pageErrs.push({ url: page.url(), msg: String(e).slice(0, 300) }));
    page.on('console', m => { if (m.type() === 'error') consoleErrs.push({ url: page.url(), msg: m.text().slice(0, 300) }); });
    page.on('response', r => {
        const s = r.status();
        if (s >= 400) nav.push({ page: page.url(), url: r.url().slice(0, 160), status: s });
    });

    const bodyText = async () => (await page.evaluate(() => (document.body && document.body.innerText) || '').catch(() => ''));

    // ── [A-5] 브라우저 로그인 (화면으로 확인) ──
    console.log('\n### [A-5] 브라우저 로그인 — 화면으로 확인\n');
    await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 45000 }).catch(() => { });
    await page.waitForTimeout(6000);
    await page.screenshot({ path: path.join(SHOTS, '00-login.png'), fullPage: true }).catch(() => { });

    let loggedIn = false;
    try {
        // 🔴 MudBlazor 는 change/blur 에서 바인딩한다. fill() 만으로는 「로그인」 버튼이
        //    disabled 인 채로 남는다 (실측 확인: disabled=true). 키보드로 치고 Tab 으로 확정한다.
        const idBox = page.locator('input[type=text]').first();
        const pwBox = page.locator('input[type=password]').first();
        await idBox.click({ timeout: 15000 });
        await idBox.type(EMAIL, { delay: 60 });
        await page.keyboard.press('Tab');
        await page.waitForTimeout(400);
        await pwBox.click({ timeout: 15000 });
        await pwBox.type(PASS, { delay: 60 });
        await page.keyboard.press('Tab');
        await page.waitForTimeout(1200);

        const loginBtn = page.getByRole('button', { name: '로그인', exact: true }).first();
        const dis = await loginBtn.isDisabled().catch(() => null);
        if (dis) {
            rec('A-5a', '🔴 로그인 버튼이 입력 후에도 disabled', 'disabled=true — 바인딩이 안 걸렸다', false);
        }
        await loginBtn.click({ timeout: 20000 });
        await page.waitForTimeout(11000);
        await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => { });
    } catch (e) {
        rec('A-5a', '로그인 폼 조작', `예외: ${String(e).slice(0, 200)}`, false);
    }

    await page.screenshot({ path: path.join(SHOTS, '01-after-login.png'), fullPage: true }).catch(() => { });
    const bt = await bodyText();
    // 🔴 화면으로 가른다: 사이드바 대메뉴가 실제로 떴나
    const sidebarProbe = ['설정관리', '업체관리', '상품관리', '판매관리', '매입관리'];
    const sidebarHits = sidebarProbe.filter(k => bt.includes(k));
    loggedIn = sidebarHits.length >= 3 && !LOGIN_KW.some(k => bt.includes(k));
    rec('A-5', '🔴 브라우저 로그인 — 사이드바가 실제로 뜨나',
        `url=${page.url()} · 사이드바 대메뉴 ${sidebarHits.length}/5 (${sidebarHits.join(',')})`,
        loggedIn,
        '초록불 근거: document.body.innerText 안에 사이드바 대메뉴 문자열 3개 이상 + 로그인 폼 문구 부재');
    if (!loggedIn) {
        rec('A-5b', '중단', `본문 앞부분: ${bt.replace(/\s+/g, ' ').slice(0, 400)}`, false,
            '인증 없이 재면 전부 401 — 판정 근거가 안 된다. 여기서 멈춘다.');
        save({ nav, pageErrs, consoleErrs });
        await browser.close();
        process.exit(1);
    }

    // ── [A-5c] 🔴 기기승인 게이트 확인 — 이게 걸리면 뒤 판정이 전부 무효다 ──
    const dev403 = nav.filter(x => x.status === 403).length;
    rec('A-5c', '🔴 기기승인 403 폭풍이 없나 (판정 유효성 전제)',
        `로그인까지 403 ${dev403}건`,
        dev403 < 5,
        '🔴 403 이 쏟아지면 화면이 빈 것은 결함이 아니라 미승인 기기 때문이다 — 그 상태의 FAIL 은 전부 가짜다');
    if (dev403 >= 5) {
        rec('A-5d', '중단', '미승인 기기 상태로는 화면 판정을 할 수 없다', false);
        save({ nav, pageErrs, consoleErrs });
        await browser.close();
        process.exit(1);
    }

    // ── 사이드바 링크 긁기 ──
    console.log('\n### [A-6] 사이드바 링크 수집\n');
    for (let round = 0; round < 3; round++) {
        const groups = page.locator('.mud-nav-group > .mud-nav-link');
        const gc = await groups.count().catch(() => 0);
        for (let i = 0; i < gc; i++) {
            const g = groups.nth(i);
            const expanded = await g.getAttribute('aria-expanded').catch(() => null);
            if (expanded === 'false') { await g.click({ timeout: 3000 }).catch(() => { }); await page.waitForTimeout(200); }
        }
        await page.waitForTimeout(500);
    }
    await page.screenshot({ path: path.join(SHOTS, '02-sidebar.png'), fullPage: true }).catch(() => { });

    const links = await page.evaluate(() => {
        const out = [];
        document.querySelectorAll('nav a[href], .mud-drawer a[href], aside a[href]').forEach(a => {
            const h = a.getAttribute('href');
            if (!h || !h.startsWith('/')) return;
            out.push({ href: h, label: (a.innerText || '').trim().replace(/\s+/g, ' ') });
        });
        return out;
    });
    const uniq = [];
    const seen = new Set();
    for (const l of links) { if (!seen.has(l.href)) { seen.add(l.href); uniq.push(l); } }
    rec('A-6', '사이드바에서 긁은 링크 수', `${uniq.length}개`, uniq.length > 20,
        '하드코딩 URL 목록이 아니라 실제 DOM 의 href 를 순회한다');

    // ── 페이지 방문 헬퍼 ──
    async function visit(url, label, keywords, shot) {
        const before = { n: nav.length, p: pageErrs.length, c: consoleErrs.length };
        const resp = await page.goto(BASE + url, { waitUntil: 'domcontentloaded', timeout: 45000 }).catch(() => null);
        await page.waitForLoadState('networkidle', { timeout: 25000 }).catch(() => { });
        await page.waitForTimeout(3200);
        const body = (await bodyText()).replace(/\s+/g, ' ');
        const my = {
            net: nav.slice(before.n).filter(x => !/\.(js|css|png|woff2?|ico|dll|wasm)(\?|$)/.test(x.url)),
            pe: pageErrs.slice(before.p),
            ce: consoleErrs.slice(before.c)
        };
        if (shot) await page.screenshot({ path: path.join(SHOTS, shot), fullPage: false }).catch(() => { });
        const failKw = FAIL_KW.find(k => body.includes(k));
        const kwHit = (keywords || []).filter(k => body.includes(k));
        const jargon = JARGON.filter(j => body.includes(j));
        const has5xx = my.net.some(x => x.status >= 500);
        const blank = body.trim().length < 40;
        return {
            url, label, status: resp ? resp.status() : 0, body: body.slice(0, 400), bodyLen: body.length,
            failKw, kwHit, jargon, net: my.net, pageErrs: my.pe, consoleErrs: my.ce, has5xx, blank
        };
    }

    // ── [B-1] 정합성 검사 화면 ──
    console.log('\n### [B-1] 정합성 검사 화면 /accounting/integrity\n');
    const ig = await visit('/accounting/integrity', '정합성 검사', ['정합성 검사', '검사 실행'], '10-integrity-before.png');
    rec('B-1a', '화면이 열리나 (본문으로 판정)',
        `본문 ${ig.bodyLen}자 · 키워드 [${ig.kwHit.join(',') || '없음'}] · 실패문구 ${ig.failKw || '없음'}`,
        !ig.failKw && !ig.blank && ig.kwHit.indexOf('정합성 검사') >= 0,
        '초록불 근거: 본문에 「정합성 검사」 제목 + 「검사 실행」 버튼 문자열');

    let igItems = [];
    let igApiItems = [];
    if (!ig.failKw && !ig.blank) {
        const btn = page.getByRole('button', { name: /검사 실행|검사 중/ }).first();
        if (await btn.count() > 0) {
            await btn.click({ timeout: 15000 }).catch(() => { });
            await page.waitForTimeout(14000);
            await page.waitForLoadState('networkidle', { timeout: 25000 }).catch(() => { });
        } else {
            rec('B-1a2', '검사 실행 버튼', '⚪ 버튼을 못 찾음', null);
        }
        await page.screenshot({ path: path.join(SHOTS, '11-integrity-after.png'), fullPage: true }).catch(() => { });
        const b2 = (await bodyText()).replace(/\s+/g, ' ');
        igItems = await page.evaluate(() => {
            const rows = [];
            document.querySelectorAll('table tbody tr').forEach(tr => {
                const t = (tr.innerText || '').trim().replace(/\s+/g, ' ');
                if (t) rows.push(t);
            });
            return rows;
        });
        rec('B-1b', '🔴 검사 실행 → 항목이 몇 건 뜨나',
            `표 행 ${igItems.length}건 · 본문 ${b2.length}자`,
            igItems.length > 0,
            '초록불 근거: table tbody tr 의 실제 행 수 (문자열 존재가 아니라 행 수)');
        console.log('    항목: ' + igItems.slice(0, 20).join(' || '));

        const igApi = await api('/api/finance/integrity-check', { token: TOKEN, deviceId: DEV });
        igApiItems = (igApi.json && (igApi.json.items || igApi.json.Items)) || [];
        rec('B-1c', '⚪ 대조 — 서버 API 가 주는 검사 항목',
            `HTTP ${igApi.status} · ${igApiItems.length}개 · [${igApiItems.map(x => x.checkName || x.CheckName).join(' / ')}]`,
            null, '화면 행 수와 대조한다');

        const salesReturnInScreen = /매출반품|매출 반품/.test(b2);
        const unposted = igApiItems.find(x => (x.checkName || x.CheckName) === '확정전표 기표 누락');
        rec('B-1d', '🔴 「매출반품」 이라는 낱말이 화면에 보이나',
            salesReturnInScreen ? '보인다' : '🔴 안 보인다',
            salesReturnInScreen,
            '⚠️ 작12 봉합은 「확정전표 기표 누락」 검사 SQL 안에 sales_return 을 넣은 것이라 ' +
            '전용 항목명이 없다. 안 보이는 게 설계상 정상일 수 있다 — 실질 판정은 B-1e');
        const unpostedOnScreen = igItems.some(r => r.indexOf('확정전표 기표 누락') >= 0);
        rec('B-1e', '🔴 매출반품을 세는 검사(「확정전표 기표 누락」)가 화면까지 갔나',
            unposted
                ? `API 있음(status=${unposted.status || unposted.Status}) · 화면표시=${unpostedOnScreen}`
                : '🔴 API 에 그 검사가 없다',
            !!unposted && unpostedOnScreen,
            '초록불 근거: 서버 응답 checkName + 화면 표 행 문자열 둘 다 확인');

        const srList = await api('/api/sales-returns', { token: TOKEN, deviceId: DEV });
        const srN = Array.isArray(srList.json) ? srList.json.length : ((srList.json && srList.json.items && srList.json.items.length) || -1);
        rec('B-1f', '⚪ 매출반품 데이터가 실재하나 (검사 유효성)',
            `HTTP ${srList.status} · ${srN}건`,
            null,
            '🔴 0건이면 「확정전표 기표 누락 OK」 는 검사가 통과한 게 아니라 셀 것이 없었던 것이다');
    }

    // ── [B-2] 마이너스 계산서 UI ──
    console.log('\n### [B-2] 마이너스 계산서 발행 UI /tax-invoice\n');
    const ti = await visit('/tax-invoice', '세금계산서', ['세금계산서'], '20-tax-invoice.png');
    rec('B-2a', '계산서 화면이 열리나',
        `본문 ${ti.bodyLen}자 · 키워드 [${ti.kwHit.join(',') || '없음'}] · 실패문구 ${ti.failKw || '없음'}`,
        !ti.failKw && !ti.blank && ti.kwHit.length > 0,
        '초록불 근거: 본문에 「세금계산서」 + 실패문구 부재');

    const tiBody = (await bodyText()).replace(/\s+/g, ' ');
    const minusKw = ['마이너스', '수정계산서', '음수', '취소계산서', '역발행', '(-)'];
    const minusHits = minusKw.filter(k => tiBody.indexOf(k) >= 0);
    const tiButtons = await page.evaluate(() =>
        Array.from(document.querySelectorAll('button')).map(b => (b.innerText || '').trim().replace(/\s+/g, ' ')).filter(Boolean));
    rec('B-2b', '🔴 마이너스 계산서 발행 UI 가 화면에 있나',
        minusHits.length ? `문구 발견: ${minusHits.join(',')}` : '🔴 없다 (관련 문구 0건)',
        minusHits.length > 0,
        `초록불 근거: 화면 innerText 에서 [${minusKw.join('/')}] 검색 · 버튼 목록=[${tiButtons.join(' | ')}]`);
    rec('B-2c', '⚪ PM 보고("화면 미착수") 반증 결과',
        minusHits.length ? '🔴 PM 보고와 다르다 — 화면에 무언가 있다' : '🟢 PM 보고대로 화면에 없다 = 보고가 정확',
        null, '이 항목은 "없다"가 정확한 보고인지를 가리는 것이지 기능 PASS 가 아니다');

    // ── [C] 6단계 워크플로우 스모크 (사이드바 링크 순회) ──
    console.log('\n### [C] 6단계 워크플로우 스모크\n');
    const TAB_URLS = [
        { href: '/quotations', label: '견적서(탭)' },
        { href: '/sales-orders', label: '수주서(탭)' },
        { href: '/deliveries', label: '거래명세서(탭)' },
        { href: '/purchase-orders', label: '발주서(탭)' },
        { href: '/purchases', label: '매입명세서(탭)' },
        { href: '/returns', label: '반품처리(탭)' }
    ];
    const SKIP = /^\/(logout|login)$/;
    const already = ['/accounting/integrity', '/tax-invoice'];
    const targets = TAB_URLS.concat(uniq.filter(l => !SKIP.test(l.href) && already.indexOf(l.href) < 0));

    const cResults = [];
    let idx = 0;
    for (const t of targets) {
        idx++;
        const r = await visit(t.href, t.label || t.href, [], null);
        const bad = !!r.failKw || r.blank || r.has5xx || r.pageErrs.length > 0 || r.jargon.length > 0;
        const mark = bad ? '🔴' : '🟢';
        const n4 = r.net.filter(x => x.status >= 400 && x.status < 500);
        const n5 = r.net.filter(x => x.status >= 500);
        console.log(`${mark} ${String(idx).padStart(2)} ${t.href.padEnd(32)} ${String(r.bodyLen).padStart(5)}자` +
            (r.failKw ? ` FAILKW=${r.failKw}` : '') +
            (r.blank ? ' BLANK' : '') +
            (n5.length ? ` 5xx=${n5.map(x => x.status + ' ' + x.url.split('/').slice(3).join('/')).join(';')}` : '') +
            (n4.length ? ` 4xx=${n4.map(x => x.status + ' ' + x.url.split('/').slice(3).join('/')).join(';')}` : '') +
            (r.pageErrs.length ? ` PAGEERR=${r.pageErrs.map(x => x.msg.slice(0, 80)).join(';')}` : '') +
            (r.jargon.length ? ` JARGON=${r.jargon.join(',')}` : ''));
        if (bad) await page.screenshot({ path: path.join(SHOTS, `c-${String(idx).padStart(2, '0')}-${t.href.replace(/\//g, '_')}.png`) }).catch(() => { });
        cResults.push(Object.assign({}, r, { bad }));
    }
    const cBad = cResults.filter(x => x.bad);
    rec('C-1', '워크플로우 화면 스모크', `${cResults.length}개 중 이상 ${cBad.length}개 — [${cBad.map(x => x.url).join(', ')}]`,
        cBad.length === 0,
        '초록불 근거: 각 화면의 body.innerText 길이·실패문구·5xx 응답·pageerror·개발용어 노출 5축');

    // ── [D] 회귀 ──
    console.log('\n### [D] 회귀 — 최근 봉합분\n');

    const rs = await visit('/return-status', '반품현황', ['반품'], '30-return-status.png');
    rec('D-1a', '매입 반품현황 화면',
        `본문 ${rs.bodyLen}자 · 5xx ${rs.net.filter(x => x.status >= 500).length}건`,
        !rs.failKw && !rs.blank && !rs.has5xx,
        '초록불 근거: 본문 + 네트워크 5xx 0건');

    const dfA = await api('/api/purchase-returns?startDate=2026-01-01&endDate=2026-12-31', { token: TOKEN, deviceId: DEV });
    const dfB = await api('/api/purchase-returns', { token: TOKEN, deviceId: DEV });
    rec('D-1b', '🔴 매입반품 목록 — 날짜 필터 걸었을 때 (8/26 작2 500 자리)',
        `날짜있음 HTTP ${dfA.status} · 날짜없음(대조군) HTTP ${dfB.status}`,
        dfA.status === 200 && dfB.status === 200,
        '초록불 근거: 대조군 대비 날짜 있을 때 500 안 나는지. 그 버그는 날짜 있을 때만 터졌다');

    try {
        const btnSearch = page.getByRole('button', { name: /조회|검색/ }).first();
        if (await btnSearch.count() > 0) {
            const before = nav.length;
            await btnSearch.click({ timeout: 10000 });
            await page.waitForTimeout(7000);
            const after = nav.slice(before).filter(x => x.status >= 500);
            const b = (await bodyText()).replace(/\s+/g, ' ');
            await page.screenshot({ path: path.join(SHOTS, '30b-return-status-search.png'), fullPage: false }).catch(() => { });
            rec('D-1c', '🔴 반품현황 화면에서 「조회」 실제 클릭',
                `5xx ${after.length}건 · 본문 ${b.length}자`,
                after.length === 0,
                '초록불 근거: 클릭 후 발생한 네트워크 응답 중 5xx 개수');
        } else {
            rec('D-1c', '반품현황 조회 버튼', '⚪ 조회 버튼을 못 찾음 — 미확인', null, '추측으로 PASS 주지 않는다');
        }
    } catch (e) {
        rec('D-1c', '반품현황 조회 클릭', `⚪ 예외 ${String(e).slice(0, 120)} — 미확인`, null);
    }

    const led = await visit('/accounting/ledger', '회계장부', ['장부', '계정', '차변', '대변', '시산표'], '31-ledger.png');
    rec('D-2', '회계장부·시산표 화면',
        `본문 ${led.bodyLen}자 · 키워드 [${led.kwHit.join(',') || '없음'}] · 5xx ${led.net.filter(x => x.status >= 500).length}건`,
        !led.failKw && !led.blank && !led.has5xx && led.kwHit.length > 0,
        '초록불 근거: 회계 낱말(장부/계정/차변/대변/시산표) 본문 존재 + 5xx 0건');

    const so = await api('/api/sales-orders', { token: TOKEN, deviceId: DEV });
    const soN = Array.isArray(so.json) ? so.json.length : ((so.json && so.json.items && so.json.items.length) || -1);
    rec('D-3a', '⚪ 수주 목록 조회', `HTTP ${so.status} · ${soN}건`, null);
    rec('D-3b', '🔴 매출 사슬 중복생성 차단 회귀',
        '⚪ 미확인 — 쓰기(거래명세서 2회 생성)를 해야 잴 수 있는데 본 검증은 읽기 전용이다',
        null,
        '🔴 추측으로 PASS 주지 않는다. 별도 쓰기 허용 검증에서 재야 한다');

    const sr = await visit('/sales-returns', '반품확인서(매출)', ['반품'], '32-sales-returns.png');
    rec('D-4', '매출반품 화면 (사이드바 진입점은 제거됨 · URL 직행)',
        `본문 ${sr.bodyLen}자 · 실패문구 ${sr.failKw || '없음'} · 5xx ${sr.net.filter(x => x.status >= 500).length}건`,
        !sr.failKw && !sr.blank && !sr.has5xx,
        '8/25작15 로 메뉴는 뺐고 화면은 남겼다 — 살아 있는지만 본다');

    // ── 콘솔·네트워크 총계 ──
    console.log('\n### 콘솔 / 네트워크 총계\n');
    const net5xx = nav.filter(x => x.status >= 500);
    const net4xx = nav.filter(x => x.status >= 400 && x.status < 500);
    rec('X-1', '네트워크 5xx 총계', `${net5xx.length}건`, net5xx.length === 0,
        net5xx.slice(0, 12).map(x => `${x.status} ${x.url}`).join(' | '));
    rec('X-2', '네트워크 4xx 총계', `${net4xx.length}건`, null,
        net4xx.slice(0, 20).map(x => `${x.status} ${x.url}`).join(' | '));
    rec('X-3', 'pageerror 총계', `${pageErrs.length}건`, pageErrs.length === 0,
        pageErrs.slice(0, 10).map(x => x.msg).join(' | '));
    rec('X-4', 'console error 총계', `${consoleErrs.length}건`, null,
        consoleErrs.slice(0, 15).map(x => x.msg).join(' | '));

    save({ cResults, igItems, igApiItems, nav, pageErrs, consoleErrs, sidebar: uniq });
    await browser.close();

    const pass = R.filter(x => x.pass === true).length;
    const fail = R.filter(x => x.pass === false).length;
    const info = R.filter(x => x.pass === null).length;
    console.log('\n' + '='.repeat(78));
    console.log(`결과 — 🟢 ${pass} PASS · 🔴 ${fail} FAIL · ⚪ ${info} INFO(미확인)`);
    console.log('='.repeat(78));
    console.log('⚠️ INFO 는 통과가 아니다. 못 잰 것이다.');
})();

function save(extra) {
    const out = path.join(SHOTS, 'result.json');
    try {
        fs.writeFileSync(out, JSON.stringify(Object.assign({ base: BASE, ver: WANT_VER, at: new Date().toISOString(), results: R }, extra || {}), null, 2));
        console.log(`\n📄 ${out}`);
    } catch (e) { console.log(`\n⚠️ 저장 실패: ${e.message}`); }
}
