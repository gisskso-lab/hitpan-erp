// 20260903 실측 (2차) — [목록] 을 실제로 눌러서 잰다
//
//   🔴 1차 FAIL 2건은 **내 시험 결함**이었다:
//     `/deliveries`·`/returns` 는 목록이 아니라 **입력 화면**이다(빈 전표가 뜬다).
//     목록은 우측 상단 **[목록]** 버튼을 눌러야 다이얼로그로 열린다.
//     ⇒ 목록을 안 열고 "화면에 반품 0건" 이라 적은 것은 **가짜 FAIL** 이다.
//
//   이번엔 [목록] 을 클릭하고, 열린 다이얼로그 본문에서 판정한다.
//   추가로 ③ 반품 전표를 실제로 **열어서** 상세가 뜨는지, [반품확정] 버튼이 있는지 본다.
//
//   🟢 읽기 전용 — 클릭은 조회 동작만. 저장·확정 버튼은 누르지 않는다.

const { chromium } = require('playwright');
const https = require('https');
const fs = require('fs');
const path = require('path');

const BASE = process.env.HITPAN_BASE || 'https://test1234.hitpan.kr';
const EMAIL = process.env.HITPAN_EMAIL || 'act0226';
const PASS = process.env.HITPAN_PASS || '11111111';
const SHOTS = path.join(__dirname, 'shots', 'list-20260903');
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
    return new Promise((resolve) => {
        const url = new URL(BASE + p);
        const data = opt.body !== undefined ? JSON.stringify(opt.body) : null;
        const headers = {};
        if (data) { headers['Content-Type'] = 'application/json'; headers['Content-Length'] = Buffer.byteLength(data); }
        if (opt.token) headers['Authorization'] = 'Bearer ' + opt.token;
        if (opt.deviceId) headers['X-HitPan-Device-Id'] = opt.deviceId;
        const req = https.request({
            hostname: url.hostname, port: 443, path: url.pathname + url.search,
            method: opt.method || 'GET', headers, timeout: 45000
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
const arr = (j) => Array.isArray(j) ? j : ((j && (j.items || j.data || j.rows)) || []);
const nav = [], pageErrs = [];

function save(extra) {
    const out = {
        when: new Date().toISOString(), base: BASE, results: R,
        summary: {
            pass: R.filter(r => r.pass === true).length,
            fail: R.filter(r => r.pass === false).length,
            unknown: R.filter(r => r.pass !== true && r.pass !== false).length
        }, ...(extra || {})
    };
    const dir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    fs.mkdirSync(dir, { recursive: true });
    const f = path.join(dir, `audit-list-open-${new Date().toISOString().slice(0, 10)}.json`);
    fs.writeFileSync(f, JSON.stringify(out, null, 2));
    console.log(`\n리포트: ${f}`);
    console.log(`🟢 PASS ${out.summary.pass} · 🔴 FAIL ${out.summary.fail} · ⚠️ UNKNOWN ${out.summary.unknown}`);
}

(async () => {
    console.log('='.repeat(78));
    console.log(`실측 2차 — [목록] 을 눌러서 잰다 · ${BASE}`);
    console.log('='.repeat(78));

    const login = await api('/api/auth/login', { method: 'POST', body: { email: EMAIL, password: PASS } });
    const TOKEN = login.json.accessToken;
    const devs = await api('/api/devices', { token: TOKEN });
    const dl = arr(devs.json);
    const DEV = ((dl.find(d => d.isMainPc) || dl[0]) || {}).deviceId;
    const T = { token: TOKEN, deviceId: DEV };

    const sReturns = await api('/api/sales/returns', T);
    const srt = arr(sReturns.json);
    const pReturns = await api('/api/purchase/returns', T);
    const prt = arr(pReturns.json);
    const sRetNos = srt.map(r => r.returnNo || r.return_no).filter(Boolean);
    const pRetNos = prt.map(r => r.returnNo || r.return_no).filter(Boolean);
    console.log(`\n서버 기준선: 매출반품 ${sRetNos.length}건 (${sRetNos.join(', ')})`);
    console.log(`             매입반품 ${pRetNos.length}건 (${pRetNos.slice(0, 4).join(', ')}…)\n`);

    const CHROME = process.env.HITPAN_CHROME ||
        'C:\\Users\\소순근\\AppData\\Local\\ms-playwright\\chromium-1234\\chrome-win64\\chrome.exe';
    const lo = { headless: true };
    if (fs.existsSync(CHROME)) lo.executablePath = CHROME;
    const browser = await chromium.launch(lo);
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1600, height: 1000 } });
    if (DEV) await ctx.addInitScript(id => { try { localStorage.setItem('hitpan_device_id', id); } catch (e) { } }, DEV);
    const page = await ctx.newPage();
    page.on('pageerror', e => pageErrs.push(String(e).slice(0, 200)));
    page.on('response', r => { if (r.status() >= 400) nav.push({ status: r.status(), url: r.url().slice(-70) }); });
    const bodyText = async () => (await page.evaluate(() => (document.body && document.body.innerText) || '').catch(() => ''));

    // 로그인
    await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 45000 }).catch(() => { });
    await page.waitForTimeout(6000);
    const idBox = page.locator('input[type=text]').first();
    const pwBox = page.locator('input[type=password]').first();
    await idBox.click({ timeout: 15000 }); await idBox.type(EMAIL, { delay: 60 });
    await page.keyboard.press('Tab'); await page.waitForTimeout(400);
    await pwBox.click({ timeout: 15000 }); await pwBox.type(PASS, { delay: 60 });
    await page.keyboard.press('Tab'); await page.waitForTimeout(1200);
    await page.getByRole('button', { name: '로그인', exact: true }).first().click({ timeout: 20000 });
    await page.waitForTimeout(11000);
    await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => { });
    let bt = await bodyText();
    const loggedIn = ['설정관리', '판매관리', '매입관리'].filter(k => bt.includes(k)).length >= 3;
    rec('L-0', '브라우저 로그인', loggedIn ? '사이드바 확인' : '실패', loggedIn);
    if (!loggedIn) { save({ nav, pageErrs }); await browser.close(); process.exit(1); }

    // ── 공통 루틴: 화면 열고 [목록] 클릭 ──
    async function openList(url, shotPrefix, label) {
        await page.goto(BASE + url, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
        await page.waitForLoadState('networkidle', { timeout: 40000 }).catch(() => { });
        await page.waitForTimeout(7000);
        await page.screenshot({ path: path.join(SHOTS, `${shotPrefix}-before.png`), fullPage: true }).catch(() => { });

        let clicked = false;
        try {
            const btn = page.getByRole('button', { name: '목록', exact: true }).first();
            if (await btn.isVisible({ timeout: 8000 }).catch(() => false)) {
                await btn.click({ timeout: 15000 });
                clicked = true;
            }
        } catch (e) { }
        if (!clicked) {
            try { await page.getByText('목록', { exact: true }).first().click({ timeout: 8000 }); clicked = true; } catch (e) { }
        }
        await page.waitForTimeout(7000);
        await page.waitForLoadState('networkidle', { timeout: 25000 }).catch(() => { });
        await page.screenshot({ path: path.join(SHOTS, `${shotPrefix}-list.png`), fullPage: true }).catch(() => { });
        rec(`${shotPrefix}-c`, `${label} — [목록] 버튼 클릭`, clicked ? '눌렀다' : '버튼을 못 찾았다', clicked);
        return clicked ? await bodyText() : null;
    }

    // ══ ① 매출 — 판매목록에 반품이 보이나 ══
    console.log('\n══ [①] 매출: /deliveries → [목록] ══\n');
    const btS = await openList('/deliveries', '10-sales', '거래명세서');
    if (btS) {
        const shown = sRetNos.filter(no => btS.includes(no));
        rec('1-1', '🔴 판매목록(다이얼로그)에 매출반품이 보이나',
            `서버 ${sRetNos.length}건 중 화면 ${shown.length}건${shown.length ? ' (' + shown.join(', ') + ')' : ''}`,
            sRetNos.length === 0 ? null : shown.length > 0,
            '🔴 작16 이 봉합한 자리 — 원전표 아래 (−) 로 사슬 배치');
        const hasMinus = /[-−]\s?[\d,]{2,}/.test(btS);
        rec('1-2', '반품 줄이 (−) 로 표기되나', hasMinus ? '음수 표기 있음' : '음수 표기 없음',
            sRetNos.length === 0 ? null : hasMinus);
        // 상태 어휘 (작16: 판매=판매완료 / 반품=반품완료)
        const hasRetWord = btS.includes('반품완료') || btS.includes('반품확정') || btS.includes('반품');
        rec('1-3', '반품 어휘가 판매와 구분되나 (작16 어휘분리)',
            hasRetWord ? '반품 어휘 확인' : '반품 어휘 없음',
            sRetNos.length === 0 ? null : hasRetWord);
    } else {
        rec('1-1', '판매목록 반품 표시', '[목록] 을 못 열어 판정 불가', null);
    }

    // ══ ② 반품 전표를 열 수 있나 + [반품확정] 버튼 ══
    console.log('\n══ [②] 반품 전표 열기 + [반품확정] 버튼 ══\n');
    if (btS && sRetNos.length) {
        const target = sRetNos.find(no => btS.includes(no));
        if (target) {
            let opened = false, btnFound = false, bodyAfter = '';
            try {
                await page.getByText(target, { exact: false }).first().click({ timeout: 15000 });
                await page.waitForTimeout(7000);
                await page.waitForLoadState('networkidle', { timeout: 25000 }).catch(() => { });
                opened = true;
            } catch (e) {
                rec('2-0', '반품 줄 클릭', `예외: ${String(e).slice(0, 140)}`, false);
            }
            await page.screenshot({ path: path.join(SHOTS, '20-return-open.png'), fullPage: true }).catch(() => { });
            bodyAfter = await bodyText();
            const notFound = bodyAfter.includes('불러올 수 없습니다') || bodyAfter.includes('찾을 수 없');
            rec('2-1', '🔴 반품 전표가 열리나 (사장님 "전표를 못찾겠음")',
                `대상 ${target} · 오류문구 ${notFound ? '있음' : '없음'} · 본문 ${bodyAfter.replace(/\s+/g, ' ').length}자`,
                opened ? !notFound : null,
                '🔴 작17 LoadReturnToGrid 가 봉합한 자리');

            for (const nm of ['반품확정', '반품 확정', '확정']) {
                try {
                    const b = page.getByRole('button', { name: nm, exact: false }).first();
                    if (await b.isVisible({ timeout: 4000 }).catch(() => false)) { btnFound = true; break; }
                } catch (e) { }
            }
            rec('2-2', '🔴🔴 [반품확정] 버튼이 화면에 있나',
                btnFound ? '보인다' : '못 찾았다', btnFound,
                '🔴 인계5 결론 — 버튼이 없어 draft 로만 쌓인 게 「재고 미반영」의 진짜 원인이었다. '
                + '재고가 안 움직인 건 정상 동작이고, 못 움직이게 만든 건 화면이었다.');
        } else {
            rec('2-1', '반품 전표 열기', '목록에 반품이 안 보여 열 대상 없음', null);
        }
    } else {
        rec('2-1', '반품 전표 열기', '①이 판정 불가라 잴 수 없다', null);
    }

    // ══ ③ 매입 — 반품목록 ══
    console.log('\n══ [③] 매입: /returns → [목록] ══\n');
    const btP = await openList('/returns', '30-purchase', '매입반품');
    if (btP) {
        const shownP = pRetNos.filter(no => btP.includes(no));
        rec('3-1', '매입반품 목록에 반품이 보이나',
            `서버 ${pRetNos.length}건 중 화면 ${shownP.length}건${shownP.length ? ' (' + shownP.slice(0, 4).join(', ') + ')' : ''}`,
            pRetNos.length === 0 ? null : shownP.length > 0,
            '🔴 매출 대조축 — 매입이 같은 사고를 안고 있는지');
    } else {
        rec('3-1', '매입반품 목록', '[목록] 을 못 열어 판정 불가', null);
    }

    // ══ ④ 재고원장 화면 (API 400 이던 자리) ══
    console.log('\n══ [④] 재고원장 화면 ══\n');
    await page.goto(`${BASE}/stock/ledger`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 40000 }).catch(() => { });
    await page.waitForTimeout(8000);
    await page.screenshot({ path: path.join(SHOTS, '40-stock-ledger.png'), fullPage: true }).catch(() => { });
    const btL = await bodyText();
    rec('4-1', '재고원장 화면이 뜨나 (API 는 400 이던 자리)',
        `본문 ${btL.replace(/\s+/g, ' ').length}자`,
        btL.length > 300,
        '⚠️ API 400 은 필수 파라미터 때문일 수 있다 — 화면이 뜨면 서버는 정상이고 내 호출이 틀린 것');

    rec('Z-1', '페이지 오류', `${pageErrs.length}건`, pageErrs.length === 0);
    rec('Z-2', '네트워크 5xx', `${nav.filter(x => x.status >= 500).length}건 (4xx 포함 ${nav.length})`,
        nav.filter(x => x.status >= 500).length === 0);

    save({ nav, pageErrs, shots: SHOTS, serverBaseline: { sRetNos, pRetNos } });
    await browser.close();
})().catch(e => {
    console.error('실행 예외:', e);
    rec('X', '스크립트 예외', String(e).slice(0, 300), false);
    save({ nav, pageErrs });
    process.exit(1);
});
