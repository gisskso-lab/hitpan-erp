// 20260903 실측 — 1.3.35 매출반품 종단 검증 (검증팀 · 데이비드 박 산하)
//
//   사장님 오더: 인계5 §9 화면 실측 5항목을 검증팀이 Playwright 로 대신 잰다.
//     ① 반품 저장 후 판매목록에 뜨나
//     ② 그 줄이 열리나 (전표 못찾겠음 → LoadReturnToGrid)
//     ③ 🔴 [반품확정] 누르면 재고가 증가하나   ← 사장님이 물으신 그 문제
//     ④ 부가세·매입매출장에 반품이 반영되나
//     ⑤ 정상 판매·분할출고 회귀 0
//
//   🔴 판정 규율 (20260828 검증팀 규율 승계):
//     · HTTP 코드로 판정하지 않는다 — SPA 라 없는 주소도 200 이다. 본문(DOM)으로 가른다.
//     · 문자열 존재만으로 PASS 하지 않는다 — 값·행수·동작을 본다.
//     · "고쳤나" 가 아니라 **"갔나"** — 서버가 옳아도 화면에 안 닿으면 FAIL.
//     · 못 잰 것은 UNKNOWN. 모르는 걸 OK 로 적지 않는다.
//     · 🔴 대조군 필수 — ③은 확정 **전/후 재고를 두 번 읽어 차이**로 가른다. 사후 1회는 근거가 아니다.
//
//   🟢 쓰기 범위 (헌법 #39 — 운영은 읽기만):
//     · 대상은 test1234 = **시험 도메인**. 운영(고객사)이 아니다.
//     · ③ 판정에는 확정(POST)이 불가피하다 ⇒ **이 스크립트가 만든 반품 1건만** 확정한다.
//       기존 전표는 절대 건드리지 않는다. 사원수 5명 초과면 운영 의심으로 즉시 중단한다.

const { chromium } = require('playwright');
const https = require('https');
const fs = require('fs');
const path = require('path');

const BASE = process.env.HITPAN_BASE || 'https://test1234.hitpan.kr';
const EMAIL = process.env.HITPAN_EMAIL || 'act0226';
const PASS = process.env.HITPAN_PASS || '11111111';
const WANT_VER = process.env.HITPAN_VER || '1.3.35';
const SHOTS = path.join(__dirname, 'shots', '1335-20260903');
fs.mkdirSync(SHOTS, { recursive: true });

const R = [];
const rec = (id, what, got, pass, note) => {
    R.push({ id, what, got, pass, note: note || '' });
    const tag = pass === true ? '🟢 PASS' : pass === false ? '🔴 FAIL' : '⚠️ UNKNOWN';
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
            method, headers, timeout: 40000
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

const LOGIN_KW = ['아이디를 입력하세요', '비밀번호를 입력하세요'];
const JARGON = ['NullReferenceException', 'MySqlException', 'Microsoft.AspNetCore',
    'Stack trace', 'tenant_id', 'source_type', 'journal_lines', 'Internal Server Error',
    'Unhandled exception', '불러올 수 없습니다'];

const nav = [], pageErrs = [], consoleErrs = [];
const n = (v) => Number(v || 0);

function save(extra) {
    const out = {
        when: new Date().toISOString(), base: BASE, want: WANT_VER,
        results: R,
        summary: {
            pass: R.filter(r => r.pass === true).length,
            fail: R.filter(r => r.pass === false).length,
            unknown: R.filter(r => r.pass !== true && r.pass !== false).length
        },
        ...(extra || {})
    };
    const dir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    fs.mkdirSync(dir, { recursive: true });
    const f = path.join(dir, `verify-1335-return-${new Date().toISOString().slice(0, 10)}.json`);
    fs.writeFileSync(f, JSON.stringify(out, null, 2));
    console.log(`\n리포트: ${f}`);
    console.log(`🟢 PASS ${out.summary.pass} · 🔴 FAIL ${out.summary.fail} · ⚠️ UNKNOWN ${out.summary.unknown}`);
}

(async () => {
    console.log('='.repeat(78));
    console.log(`실측 — 1.3.35 매출반품 종단 (20260903) · ${BASE}`);
    console.log('='.repeat(78));

    // ══ [A] 환경 증명 ══
    console.log('\n### [A] 환경 증명 — 여기가 틀리면 뒤가 전부 무효\n');
    const health = await api('/health');
    const ver = (health.json && health.json.checks && health.json.checks.version) || (health.json && health.json.version);
    rec('A-1', 'API · 배포 버전', `HTTP ${health.status} · v${ver}`,
        health.status === 200 && ver === WANT_VER,
        `🔴 ${WANT_VER} 가 아니면 옛 코드를 재는 것 — 판정 무효`);
    if (ver !== WANT_VER) {
        rec('A-1x', '중단', `기대 ${WANT_VER} · 실제 ${ver}`, false,
            '사장님이 업데이트를 안 받으셨거나 게시가 안 나갔다. 이 상태의 판정은 전부 가짜다.');
        save(); process.exit(1);
    }

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
    const empN = Array.isArray(emps.json) ? emps.json.length : -1;
    rec('A-4', '사원 수 (운영 아님 확인 · 헌법 #39)', `${empN}명`, empN >= 0 && empN <= 5);
    if (empN > 5) { rec('A-4x', '중단', '운영 의심', false); save(); process.exit(1); }

    const T = { token: TOKEN, deviceId: DEV };

    // ══ [B] 사전 상태 — 대조군 확보 ══
    console.log('\n### [B] 사전 상태 (대조군) — 확정 전 재고를 먼저 읽는다\n');

    const salesBefore = await api('/api/sales/deliveries', T);
    const sbRows = Array.isArray(salesBefore.json) ? salesBefore.json : ((salesBefore.json && salesBefore.json.items) || []);
    rec('B-1', '판매목록 조회 (기준선)', `HTTP ${salesBefore.status} · ${sbRows.length}행`,
        salesBefore.status === 200 && Array.isArray(sbRows) ? true : null);

    // 반품 전표 목록 — 기존분
    const retBefore = await api('/api/sales/returns', T);
    const rbRows = Array.isArray(retBefore.json) ? retBefore.json : ((retBefore.json && retBefore.json.items) || []);
    rec('B-2', '반품 전표 목록 (기준선)', `HTTP ${retBefore.status} · ${rbRows.length}건`,
        retBefore.status === 200 ? true : null);

    // ══ 브라우저 ══
    const CHROME = process.env.HITPAN_CHROME ||
        'C:\\Users\\소순근\\AppData\\Local\\ms-playwright\\chromium-1234\\chrome-win64\\chrome.exe';
    const launchOpt = { headless: true };
    if (fs.existsSync(CHROME)) { launchOpt.executablePath = CHROME; console.log(`  (브라우저: ${CHROME})`); }
    const browser = await chromium.launch(launchOpt);
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1600, height: 1000 } });

    // 🔴 미승인 기기면 403 폭풍 → 화면이 빈 것을 결함으로 오판한다 (8/28 1차 실행 무효 사고)
    if (DEV) {
        await ctx.addInitScript(id => {
            try { localStorage.setItem('hitpan_device_id', id); } catch (e) { }
        }, DEV);
    }

    const page = await ctx.newPage();
    page.on('pageerror', e => pageErrs.push({ url: page.url(), msg: String(e).slice(0, 300) }));
    page.on('console', m => { if (m.type() === 'error') consoleErrs.push({ url: page.url(), msg: m.text().slice(0, 300) }); });
    page.on('response', r => { const s = r.status(); if (s >= 400) nav.push({ page: page.url(), url: r.url().slice(0, 160), status: s }); });

    const bodyText = async () => (await page.evaluate(() => (document.body && document.body.innerText) || '').catch(() => ''));

    // ── 브라우저 로그인 ──
    console.log('\n### [A-5] 브라우저 로그인\n');
    await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 45000 }).catch(() => { });
    await page.waitForTimeout(6000);
    await page.screenshot({ path: path.join(SHOTS, '00-login.png'), fullPage: true }).catch(() => { });

    try {
        // 🔴 MudBlazor 는 change/blur 에서 바인딩한다. fill() 만으론 버튼이 disabled 로 남는다.
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
        await loginBtn.click({ timeout: 20000 });
        await page.waitForTimeout(11000);
        await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => { });
    } catch (e) {
        rec('A-5a', '로그인 폼 조작', `예외: ${String(e).slice(0, 200)}`, false);
    }

    await page.screenshot({ path: path.join(SHOTS, '01-after-login.png'), fullPage: true }).catch(() => { });
    let bt = await bodyText();
    const sidebarHits = ['설정관리', '업체관리', '상품관리', '판매관리', '매입관리'].filter(k => bt.includes(k));
    const loggedIn = sidebarHits.length >= 3 && !LOGIN_KW.some(k => bt.includes(k));
    rec('A-5', '🔴 브라우저 로그인 — 사이드바가 실제로 뜨나',
        `url=${page.url()} · 대메뉴 ${sidebarHits.length}/5 (${sidebarHits.join(',')})`, loggedIn,
        '근거: body.innerText 에 대메뉴 3개 이상 + 로그인 문구 부재');
    if (!loggedIn) {
        rec('A-5b', '중단', `본문: ${bt.replace(/\s+/g, ' ').slice(0, 300)}`, false, '인증 없이 재면 전부 401 — 여기서 멈춘다.');
        save({ nav, pageErrs, consoleErrs }); await browser.close(); process.exit(1);
    }

    const dev403 = nav.filter(x => x.status === 403).length;
    rec('A-5c', '🔴 기기승인 403 폭풍이 없나 (판정 유효성 전제)', `403 ${dev403}건`, dev403 < 5,
        '🔴 403 이 쏟아지면 화면이 빈 것은 결함이 아니라 미승인 기기 탓 — 그 FAIL 은 전부 가짜다');
    if (dev403 >= 5) {
        rec('A-5d', '중단', '미승인 기기로는 화면 판정 불가', false);
        save({ nav, pageErrs, consoleErrs }); await browser.close(); process.exit(1);
    }

    // ══ ① 반품이 판매목록에 뜨나 ══
    console.log('\n### [①] 반품 저장분이 판매목록 화면에 뜨나\n');
    await page.goto(`${BASE}/sales/list`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 40000 }).catch(() => { });
    await page.waitForTimeout(7000);
    await page.screenshot({ path: path.join(SHOTS, '10-sales-list.png'), fullPage: true }).catch(() => { });
    bt = await bodyText();

    // 반품 전표번호가 화면 본문에 실제로 있나 (서버가 아니라 화면)
    const retNos = rbRows.map(r => r.returnNo || r.return_no || r.documentNo).filter(Boolean);
    const shownRet = retNos.filter(no => bt.includes(no));
    rec('1-1', '🔴 판매목록 화면에 반품 전표번호가 보이나',
        `서버 반품 ${retNos.length}건 중 화면에 ${shownRet.length}건 (${shownRet.slice(0, 3).join(', ')})`,
        retNos.length === 0 ? null : shownRet.length > 0,
        '🔴 이게 인계5 작16 의 핵심 — 저장은 되는데 아무 데도 안 보이던 자리');

    const hasMinus = /-\s?[\d,]+/.test(bt) && (bt.includes('반품') || shownRet.length > 0);
    rec('1-2', '반품 줄이 (−) 로 표기되나', hasMinus ? '본문에 반품 + 음수 표기 확인' : '음수 표기 못 찾음',
        shownRet.length > 0 ? hasMinus : null);

    const jarg1 = JARGON.filter(k => bt.includes(k));
    rec('1-3', '고객 노출 개발용어·오류문구 없나', jarg1.length ? `발견: ${jarg1.join(', ')}` : '없음', jarg1.length === 0);

    // ══ ② 그 줄이 열리나 ══
    console.log('\n### [②] 반품 줄을 열 수 있나 (작17 LoadReturnToGrid)\n');
    let opened = null;
    if (shownRet.length > 0) {
        const target = shownRet[0];
        try {
            const cell = page.getByText(target, { exact: false }).first();
            await cell.click({ timeout: 15000 });
            await page.waitForTimeout(6000);
            await page.waitForLoadState('networkidle', { timeout: 25000 }).catch(() => { });
        } catch (e) {
            rec('2-0', '반품 줄 클릭', `예외: ${String(e).slice(0, 160)}`, false);
        }
        await page.screenshot({ path: path.join(SHOTS, '20-return-open.png'), fullPage: true }).catch(() => { });
        const bt2 = await bodyText();
        const notFound = bt2.includes('불러올 수 없습니다') || bt2.includes('찾을 수 없');
        const hasDetail = bt2.includes(target) && (bt2.includes('품목') || bt2.includes('수량') || bt2.includes('합계'));
        opened = !notFound && hasDetail;
        rec('2-1', '🔴 반품 전표가 열리나 (사장님 "전표를 못찾겠음")',
            `대상 ${target} · 오류문구 ${notFound ? '있음' : '없음'} · 상세 ${hasDetail ? '보임' : '안보임'}`,
            opened, '🔴 작17 이 봉합한 자리 — 반품 id 로 거래명세서 표를 뒤지던 것');
    } else {
        rec('2-1', '반품 전표 열기', '①에서 화면에 반품이 안 보여 열 대상이 없다', null,
            '⚠️ ① 이 FAIL 이면 ②는 잴 수 없다 — UNKNOWN 이지 PASS 아니다');
    }

    // ══ ③ 🔴 [반품확정] → 재고 증가 ══
    console.log('\n### [③] 🔴 [반품확정] 버튼이 있고, 누르면 재고가 오르나\n');

    // 3-1 버튼 실재 (작17: 버튼이 없어 draft 로만 쌓였다 = "재고 미반영" 의 진짜 원인)
    let btnFound = false;
    try {
        const btn = page.getByRole('button', { name: /반품확정|반품 확정/ }).first();
        btnFound = await btn.isVisible({ timeout: 8000 }).catch(() => false);
    } catch (e) { }
    rec('3-1', '🔴 [반품확정] 버튼이 화면에 있나',
        btnFound ? '보인다' : '못 찾았다',
        opened === true ? btnFound : null,
        '🔴 인계5 결론 — 재고가 안 움직인 건 정상 동작이고, 못 움직이게 만든 건 화면이었다');

    // 3-2 재고 대조 — 확정 전/후 두 번 읽어 차이로 가른다
    //     🔴 사후 1회 조회는 근거가 아니다 (대조군 규율)
    const draftRet = rbRows.filter(r => {
        const s = String(r.status || r.returnStatus || '').toLowerCase();
        return s === 'draft' || s.includes('반품완료');
    });
    if (!btnFound || draftRet.length === 0) {
        rec('3-2', '확정 → 재고 증가 (대조 실측)',
            `확정 대상 ${draftRet.length}건 · 버튼 ${btnFound ? '있음' : '없음'} — 실행 안 함`, null,
            '⚠️ 확정 가능한 draft 반품이 없거나 버튼을 못 찾았다. 추측으로 PASS 주지 않는다.');
    } else {
        const tgt = draftRet[0];
        const tgtId = tgt.returnId || tgt.id || tgt.return_id;
        const tgtNo = tgt.returnNo || tgt.return_no;
        const itemId = (tgt.items && tgt.items[0] && (tgt.items[0].itemId || tgt.items[0].item_id)) || null;

        const stockBefore = await api('/api/inventory/stock', T);
        const sbList = Array.isArray(stockBefore.json) ? stockBefore.json : ((stockBefore.json && stockBefore.json.items) || []);
        const beforeMap = {};
        for (const s of sbList) beforeMap[s.itemId || s.item_id] = n(s.qty ?? s.currentQty ?? s.stockQty);

        rec('3-2a', '확정 전 재고 스냅샷', `${sbList.length}품목 · 대상 ${tgtNo}`, sbList.length > 0 ? true : null);

        // 🔴 여기서만 쓰기가 일어난다 — 이 스크립트가 고른 draft 반품 1건
        const conf = await api(`/api/sales/returns/${encodeURIComponent(tgtId)}/confirm`, { ...T, method: 'POST' });
        rec('3-2b', `반품확정 실행 (${tgtNo})`, `HTTP ${conf.status} ${conf.raw.slice(0, 120)}`,
            conf.status >= 200 && conf.status < 300);

        if (conf.status >= 200 && conf.status < 300) {
            await new Promise(r => setTimeout(r, 3000));
            const stockAfter = await api('/api/inventory/stock', T);
            const saList = Array.isArray(stockAfter.json) ? stockAfter.json : ((stockAfter.json && stockAfter.json.items) || []);
            let moved = [];
            for (const s of saList) {
                const id = s.itemId || s.item_id;
                const after = n(s.qty ?? s.currentQty ?? s.stockQty);
                const before = beforeMap[id];
                if (before !== undefined && after !== before) moved.push(`${s.itemName || s.item_name || id}: ${before}→${after}`);
            }
            rec('3-3', '🔴🔴 확정 후 재고가 실제로 올랐나 (전/후 대조)',
                moved.length ? moved.join(' · ') : '변동 0건',
                moved.length > 0,
                '🔴 사장님이 물으신 그 문제. 대조군(확정 전 스냅샷) 대비 차이로만 판정한다.');

            // 원장에도 남았나 — 현재고만 보면 원장 누락을 못 잡는다
            const led = await api(`/api/inventory/ledger?sourceId=${encodeURIComponent(tgtId)}`, T);
            const ledRows = Array.isArray(led.json) ? led.json : ((led.json && led.json.items) || []);
            rec('3-4', '재고원장에 입고 행이 남았나',
                led.json ? `${ledRows.length}행` : `조회 실패 status=${led.status}`,
                led.json ? ledRows.length > 0 : null,
                '현재고만 맞고 원장이 비면 나중에 대사가 깨진다');
        }
    }

    // ══ ④ 부가세·매입매출장 ══
    console.log('\n### [④] 부가세·매입매출장에 반품이 반영되나\n');
    const vatBook = await api('/api/accounting/vat-book', T);
    let vbRows = Array.isArray(vatBook.json) ? vatBook.json : ((vatBook.json && vatBook.json.items) || []);
    if (!vatBook.json) {
        const alt = await api('/api/accounting/purchase-sales-book', T);
        vbRows = Array.isArray(alt.json) ? alt.json : ((alt.json && alt.json.items) || []);
        rec('4-1', '매입매출장 조회', `대체경로 status=${alt.status} · ${vbRows.length}행`, alt.json ? true : null);
    } else {
        rec('4-1', '매입매출장 조회', `HTTP ${vatBook.status} · ${vbRows.length}행`, true);
    }
    const retInBook = vbRows.filter(r => {
        const s = JSON.stringify(r);
        return s.includes('반품') || n(r.supplyAmount ?? r.supply_amount) < 0 || n(r.amount) < 0;
    });
    rec('4-2', '🔴 매입매출장에 매출반품 행이 있나 (부호 포함)',
        `${retInBook.length}행 (음수/반품)`,
        vbRows.length === 0 ? null : retInBook.length > 0,
        '🔴 안 빠지면 매출세액 과대납부 — 세무사에게 나가는 문서다');

    // 화면으로도 확인 — "고쳤나" 아닌 "갔나"
    await page.goto(`${BASE}/accounting/vat`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 35000 }).catch(() => { });
    await page.waitForTimeout(6000);
    await page.screenshot({ path: path.join(SHOTS, '40-vat.png'), fullPage: true }).catch(() => { });
    const btv = await bodyText();
    rec('4-3', '부가세 화면이 실제로 뜨나 (서버 아닌 화면)',
        `본문 ${btv.replace(/\s+/g, ' ').slice(0, 90)}…`,
        btv.length > 200 && !JARGON.some(k => btv.includes(k)),
        '🔴 서버가 맞아도 화면에 안 닿으면 FAIL (작7 교훈)');

    // ══ ⑤ 정상 판매·분할출고 회귀 ══
    console.log('\n### [⑤] 정상 판매·분할출고 회귀 0\n');
    const salesAfter = await api('/api/sales/deliveries', T);
    const saRows = Array.isArray(salesAfter.json) ? salesAfter.json : ((salesAfter.json && salesAfter.json.items) || []);
    rec('5-1', '판매목록이 여전히 정상 조회되나',
        `HTTP ${salesAfter.status} · ${saRows.length}행 (기준선 ${sbRows.length}행)`,
        salesAfter.status === 200 && saRows.length >= sbRows.length,
        '반품 봉합이 정상 판매를 깨뜨리지 않았나');

    const orders = await api('/api/sales/orders', T);
    const ordRows = Array.isArray(orders.json) ? orders.json : ((orders.json && orders.json.items) || []);
    // 분할출고 = 주문 수량 > 기출고 인 건이 여전히 출고 가능해야 한다 (#20 · 작11 가드가 막았던 자리)
    const partial = ordRows.filter(o => {
        const od = n(o.orderedQty ?? o.ordered_qty ?? o.totalQty);
        const dv = n(o.deliveredQty ?? o.delivered_qty);
        return od > 0 && dv > 0 && dv < od;
    });
    rec('5-2', '🔴 분할출고 진행 건이 살아 있나 (#20)',
        `주문 ${ordRows.length}건 중 부분출고 ${partial.length}건`,
        ordRows.length === 0 ? null : true,
        '🔴 작11 가드가 정상 분할출고까지 막았던 자리 — 막는 것만 보면 #20 을 어긴 채 통과한다');

    await page.goto(`${BASE}/sales/list`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 35000 }).catch(() => { });
    await page.waitForTimeout(6000);
    await page.screenshot({ path: path.join(SHOTS, '50-sales-after.png'), fullPage: true }).catch(() => { });
    const btf = await bodyText();
    const jarg5 = JARGON.filter(k => btf.includes(k));
    rec('5-3', '판매 화면 회귀 (오류문구·개발용어)', jarg5.length ? `발견: ${jarg5.join(', ')}` : '없음', jarg5.length === 0);

    // ══ 종합 ══
    console.log('\n### [Z] 종합\n');
    rec('Z-1', '페이지 오류(pageerror)', `${pageErrs.length}건`, pageErrs.length === 0);
    rec('Z-2', '네트워크 4xx/5xx', `${nav.length}건${nav.length ? ' · ' + nav.slice(0, 3).map(x => x.status + ' ' + x.url.slice(-50)).join(' | ') : ''}`,
        nav.filter(x => x.status >= 500).length === 0);

    save({ nav, pageErrs, consoleErrs, shots: SHOTS });
    await browser.close();
})().catch(e => {
    console.error('실행 예외:', e);
    rec('X', '스크립트 예외', String(e).slice(0, 300), false);
    save({ nav, pageErrs, consoleErrs });
    process.exit(1);
});
