// 20260903 실측 — 3축 사슬·정합성 (검증팀 · 데이비드 박 산하)
//
//   사장님 오더:
//     ① 매입사슬과 반품 정합성
//     ② 매출사슬과 반품 정합성
//     ③ 매출·매입사슬과 **상품마스터 재고 · 원장 재고 · 회계** 정합성
//
//   🔴 판정 규율 (검증팀 규율 승계):
//     · HTTP 코드로 판정하지 않는다 — SPA 라 없는 주소도 200 이다. **본문·수치**로 가른다.
//     · 문자열 존재만으로 PASS 하지 않는다.
//     · "고쳤나" 가 아니라 **"갔나"** — 서버가 옳아도 화면에 안 닿으면 FAIL.
//     · 못 잰 것은 **UNKNOWN**. 모르는 걸 OK 로 적지 않는다.
//     · 🔴 **두 출처를 대조**한다. 한쪽만 보고 판정하지 않는다.
//
//   🔴 1차 실행에서 내가 틀린 것 (기록):
//     · 판매목록을 `/sales/list` 로 쟀다 → 실제는 **`/deliveries`**. 없는 주소를 재고 FAIL 을 외칠 뻔했다.
//     · 재고를 `/api/inventory/stock` 으로 쟀다 → 실제는 **`/api/stock/balance`**, 원장은 **POST** `/api/stock/ledger`.
//     · 회계를 `/api/accounting/*` 으로 쟀다 → 실제는 **`/api/finance/*`**.
//     ⇒ UNKNOWN 6건은 대부분 **내 시험 장비 결함**이었다. 라우트를 소스에서 확인하고 다시 짰다.
//
//   🟢 읽기 전용. POST 는 `/api/stock/ledger` (원장 **조회**) 뿐 — 데이터를 쓰지 않는다.

const { chromium } = require('playwright');
const https = require('https');
const fs = require('fs');
const path = require('path');

const BASE = process.env.HITPAN_BASE || 'https://test1234.hitpan.kr';
const EMAIL = process.env.HITPAN_EMAIL || 'act0226';
const PASS = process.env.HITPAN_PASS || '11111111';
const WANT_VER = process.env.HITPAN_VER || '1.3.35';
const SHOTS = path.join(__dirname, 'shots', '3axis-20260903');
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
        const data = opt.body !== undefined ? JSON.stringify(opt.body) : null;
        const headers = {};
        if (data) { headers['Content-Type'] = 'application/json'; headers['Content-Length'] = Buffer.byteLength(data); }
        if (opt.token) headers['Authorization'] = 'Bearer ' + opt.token;
        if (opt.deviceId) headers['X-HitPan-Device-Id'] = opt.deviceId;
        const req = https.request({
            hostname: url.hostname, port: 443, path: url.pathname + url.search,
            method, headers, timeout: 45000
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

const n = (v) => Number(v || 0);
const won = (v) => n(v).toLocaleString('ko-KR');
const arr = (j) => Array.isArray(j) ? j : ((j && (j.items || j.data || j.rows)) || []);
const JARGON = ['NullReferenceException', 'MySqlException', 'Microsoft.AspNetCore', 'Stack trace',
    'tenant_id', 'source_type', 'journal_lines', 'Internal Server Error', 'Unhandled exception'];
const nav = [], pageErrs = [];

function save(extra) {
    const out = {
        when: new Date().toISOString(), base: BASE, want: WANT_VER, results: R,
        summary: {
            pass: R.filter(r => r.pass === true).length,
            fail: R.filter(r => r.pass === false).length,
            unknown: R.filter(r => r.pass !== true && r.pass !== false).length
        }, ...(extra || {})
    };
    const dir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    fs.mkdirSync(dir, { recursive: true });
    const f = path.join(dir, `audit-3axis-${new Date().toISOString().slice(0, 10)}.json`);
    fs.writeFileSync(f, JSON.stringify(out, null, 2));
    console.log(`\n리포트: ${f}`);
    console.log(`🟢 PASS ${out.summary.pass} · 🔴 FAIL ${out.summary.fail} · ⚠️ UNKNOWN ${out.summary.unknown}`);
}

(async () => {
    console.log('='.repeat(78));
    console.log(`실측 — 3축 사슬·정합성 (20260903) · ${BASE}`);
    console.log('  ① 매입사슬+반품  ② 매출사슬+반품  ③ 재고(마스터·원장)·회계 3자대조');
    console.log('='.repeat(78));

    // ══ [A] 환경 증명 ══
    console.log('\n### [A] 환경 증명\n');
    const health = await api('/health');
    const ver = (health.json && health.json.checks && health.json.checks.version) || (health.json && health.json.version);
    rec('A-1', 'API · 배포 버전', `HTTP ${health.status} · v${ver}`, health.status === 200 && ver === WANT_VER,
        `🔴 ${WANT_VER} 아니면 옛 코드 — 판정 무효`);
    if (ver !== WANT_VER) { save(); process.exit(1); }

    const login = await api('/api/auth/login', { method: 'POST', body: { email: EMAIL, password: PASS } });
    if (login.status !== 200 || !(login.json && login.json.accessToken)) {
        rec('A-2', 'API 로그인', `HTTP ${login.status}`, false); save(); process.exit(1);
    }
    const TOKEN = login.json.accessToken;
    const devs = await api('/api/devices', { token: TOKEN });
    const dl = arr(devs.json);
    const DEV = ((dl.find(d => d.isMainPc) || dl[0]) || {}).deviceId;
    const T = { token: TOKEN, deviceId: DEV };
    rec('A-2', 'API 로그인 · 기기줄', `토큰 수신 · dev ${DEV ? DEV.slice(0, 12) + '…' : '없음'}`, !!DEV);

    const emps = await api('/api/employees', T);
    const empN = arr(emps.json).length;
    rec('A-3', '사원 수 (운영 아님 · #39)', `${empN}명`, empN <= 5);
    if (empN > 5) { rec('A-3x', '중단', '운영 의심', false); save(); process.exit(1); }

    // ══════════════════════════════════════════════════════════
    // [축 ①] 매입사슬 + 반품 정합성
    // ══════════════════════════════════════════════════════════
    console.log('\n══ [축 ①] 매입사슬 + 반품 정합성 ══\n');

    const pOrders = await api('/api/purchase/orders', T);
    const pReceipts = await api('/api/purchase/receipts', T);
    const pReturns = await api('/api/purchase/returns', T);
    const po = arr(pOrders.json), pr = arr(pReceipts.json), prt = arr(pReturns.json);
    rec('1-1', '매입사슬 3단 조회 (발주→매입→반품)',
        `발주 ${po.length} · 매입 ${pr.length} · 반품 ${prt.length}`,
        pOrders.status === 200 && pReceipts.status === 200 && pReturns.status === 200);

    // 1-2 반품 → 원전표 사슬 (근거가 붙어 있나)
    let pNoChain = [];
    for (const r of prt) {
        const rid = r.returnId || r.id || r.return_id;
        const d = await api(`/api/purchase/returns/${encodeURIComponent(rid)}`, T);
        const j = d.json || {};
        const items = arr(j.items || j.lines || []);
        const linked = (j.receiptId || j.receipt_id || j.sourceId || j.source_id) ||
            items.some(x => x.receiptItemId || x.receipt_item_id || x.sourceItemId);
        if (!linked) pNoChain.push(r.returnNo || r.return_no || rid);
    }
    rec('1-2', '🔴 매입반품 → 원전표 사슬이 붙어 있나',
        prt.length === 0 ? '반품 0건' : (pNoChain.length ? `사슬 없는 반품: ${pNoChain.join(', ')}` : `${prt.length}건 전부 연결됨`),
        prt.length === 0 ? null : pNoChain.length === 0,
        '🔴 사슬이 없으면 「왜 반품됐나」를 장부에서 못 따라간다');

    // 1-3 초과반품 (매입수량 < 반품수량)
    let pOver = [];
    for (const r of prt) {
        const rid = r.returnId || r.id || r.return_id;
        const d = await api(`/api/purchase/returns/${encodeURIComponent(rid)}`, T);
        for (const it of arr((d.json || {}).items || [])) {
            const rq = n(it.qty ?? it.quantity ?? it.returnQty);
            const bq = n(it.receiptQty ?? it.receipt_qty ?? it.sourceQty);
            if (bq > 0 && rq > bq) pOver.push(`${r.returnNo || rid} ${it.itemName || it.item_name}: 매입 ${bq} < 반품 ${rq}`);
        }
    }
    rec('1-3', '매입 초과반품이 없나 (반품 ≤ 매입)',
        pOver.length ? pOver.join(' · ') : '초과 0건',
        prt.length === 0 ? null : pOver.length === 0);

    // ══════════════════════════════════════════════════════════
    // [축 ②] 매출사슬 + 반품 정합성
    // ══════════════════════════════════════════════════════════
    console.log('\n══ [축 ②] 매출사슬 + 반품 정합성 ══\n');

    const sOrders = await api('/api/sales/orders', T);
    const sDeliv = await api('/api/sales/deliveries', T);
    const sReturns = await api('/api/sales/returns', T);
    const so = arr(sOrders.json), sd = arr(sDeliv.json), srt = arr(sReturns.json);
    rec('2-1', '매출사슬 3단 조회 (수주→명세서→반품)',
        `수주 ${so.length} · 명세서 ${sd.length} · 반품 ${srt.length}`,
        sOrders.status === 200 && sDeliv.status === 200 && sReturns.status === 200);

    let sNoChain = [], sOver = [], sDetail = [];
    for (const r of srt) {
        const rid = r.returnId || r.id || r.return_id;
        const rno = r.returnNo || r.return_no || rid;
        const d = await api(`/api/sales/returns/${encodeURIComponent(rid)}`, T);
        const j = d.json || {};
        sDetail.push({ rno, rid, status: r.status || j.status, ok: d.status === 200, j });
        const items = arr(j.items || j.lines || []);
        const linked = (j.deliveryId || j.delivery_id || j.sourceId || j.source_id) ||
            items.some(x => x.deliveryItemId || x.delivery_item_id);
        if (!linked) sNoChain.push(rno);
        for (const it of items) {
            const rq = n(it.qty ?? it.quantity ?? it.returnQty);
            const bq = n(it.deliveryQty ?? it.delivery_qty ?? it.sourceQty);
            if (bq > 0 && rq > bq) sOver.push(`${rno} ${it.itemName || it.item_name}: 판매 ${bq} < 반품 ${rq}`);
        }
    }
    rec('2-2', '🔴 매출반품 → 원전표 사슬이 붙어 있나',
        srt.length === 0 ? '반품 0건' : (sNoChain.length ? `사슬 없는 반품: ${sNoChain.join(', ')}` : `${srt.length}건 전부 연결됨`),
        srt.length === 0 ? null : sNoChain.length === 0,
        '🔴 작17 이 DB-116 으로 소급 복구한 자리');

    rec('2-3', '매출 초과반품이 없나 (반품 ≤ 판매)',
        sOver.length ? sOver.join(' · ') : '초과 0건',
        srt.length === 0 ? null : sOver.length === 0,
        '⚠️ 인계5 기록 — 과거분 반-20260828-002 (판매1 < 반품99) 가 남아 있을 수 있다');

    rec('2-4', '🔴 반품 전표를 열 수 있나 (작17 LoadReturnToGrid · 서버측)',
        sDetail.length ? `${sDetail.filter(x => x.ok).length}/${sDetail.length}건 상세 조회 성공` : '반품 0건',
        sDetail.length === 0 ? null : sDetail.every(x => x.ok),
        '사장님 "전표를 못찾겠음" 의 서버측 확인 — 화면은 [축 ④] 에서 별도로 잰다');

    // ══════════════════════════════════════════════════════════
    // [축 ③] 🔴 재고 3자 대조 — 상품마스터 · 재고원장 · 회계
    // ══════════════════════════════════════════════════════════
    console.log('\n══ [축 ③] 🔴 상품마스터 재고 ↔ 원장 재고 ↔ 회계 ══\n');

    // ③-1 상품마스터(현재고)
    const bal = await api('/api/stock/balance', T);
    const balRows = arr(bal.json);
    rec('3-1', '상품마스터 현재고 조회 (/api/stock/balance)',
        `HTTP ${bal.status} · ${balRows.length}품목`, bal.status === 200 && balRows.length > 0);

    // ③-2 재고원장 (POST 조회)
    const led = await api('/api/stock/ledger', { ...T, method: 'POST', body: {} });
    const ledRows = arr(led.json);
    rec('3-2', '재고원장 조회 (POST /api/stock/ledger)',
        `HTTP ${led.status} · ${ledRows.length}행`, led.status === 200 ? ledRows.length >= 0 : null);

    // ③-3 🔴 마스터 현재고 vs 원장 누적 — 두 출처 대조
    if (balRows.length && ledRows.length) {
        const ledSum = {};
        for (const L of ledRows) {
            const id = L.itemId || L.item_id;
            if (!id) continue;
            const inQ = n(L.qtyIn ?? L.qty_in ?? L.inQty);
            const outQ = n(L.qtyOut ?? L.qty_out ?? L.outQty);
            const dq = (inQ || outQ) ? (inQ - outQ) : n(L.qty ?? L.quantity);
            ledSum[id] = (ledSum[id] || 0) + dq;
        }
        const diffs = [];
        let compared = 0;
        for (const b of balRows) {
            const id = b.itemId || b.item_id;
            if (!(id in ledSum)) continue;
            compared++;
            const cur = n(b.qty ?? b.currentQty ?? b.stockQty ?? b.balanceQty);
            if (Math.abs(cur - ledSum[id]) > 0.0001) {
                diffs.push(`${b.itemName || b.item_name || id}: 마스터 ${cur} vs 원장 ${ledSum[id]} (차 ${cur - ledSum[id]})`);
            }
        }
        rec('3-3', '🔴🔴 상품마스터 현재고 = 재고원장 누적인가 (2자 대조)',
            compared === 0 ? '대조 가능한 품목 0 — 원장에 itemId 가 없다' :
                (diffs.length ? `${compared}품목 중 불일치 ${diffs.length}건\n         ${diffs.slice(0, 6).join('\n         ')}` : `${compared}품목 전부 일치`),
            compared === 0 ? null : diffs.length === 0,
            '🔴 인계5 미규명 — 재고 불일치 3건이 UNKNOWN 으로 남아 있던 자리');
    } else {
        rec('3-3', '🔴 마스터 vs 원장 대조', '한쪽이 비어 대조 불가', null,
            '⚠️ 대조군이 없으면 판정 안 한다');
    }

    // ③-4 회계 정합성 검사 (서버)
    const integ = await api('/api/finance/integrity-check', T);
    const igRows = arr(integ.json);
    if (integ.json) {
        const bad = igRows.filter(x =>
            (x.status && String(x.status).toUpperCase() !== 'OK') || n(x.diff) !== 0 || n(x.count) > 0);
        rec('3-4', '🔴 회계 정합성 검사 (/api/finance/integrity-check)',
            igRows.length === 0 ? '항목 0' :
                (bad.length ? `이상 ${bad.length}건: ` + bad.map(b => `${b.name || b.checkName || b.title}=${b.count ?? b.diff}`).join(', ')
                    : `${igRows.length}개 항목 전부 이상 없음`),
            igRows.length === 0 ? null : bad.length === 0,
            '🔴 8/27 작8 이 신설한 화면의 서버측');
    } else {
        rec('3-4', '회계 정합성 검사', `status=${integ.status} · JSON 아님`, null);
    }

    // ③-5 시산표 — 차변 = 대변 (회계의 근본 등식)
    const tb = await api('/api/finance/trial-balance', T);
    const tbRows = arr(tb.json);
    if (tbRows.length) {
        let dr = 0, cr = 0;
        for (const r of tbRows) {
            dr += n(r.debit ?? r.debitAmount ?? r.debit_amount);
            cr += n(r.credit ?? r.creditAmount ?? r.credit_amount);
        }
        rec('3-5', '🔴 시산표 차변 = 대변 (회계 근본등식)',
            `차변 ${won(dr)} · 대변 ${won(cr)} · 차 ${won(dr - cr)}`,
            Math.abs(dr - cr) < 1,
            '🔴 이게 깨지면 분개 어딘가가 한쪽만 기표됐다');
    } else {
        rec('3-5', '시산표', `status=${tb.status} · ${tbRows.length}행`, null);
    }

    // ③-6 매입매출장 + 부가세 — 반품이 (−) 로 반영됐나
    const psl = await api('/api/finance/purchase-sales-ledger', T);
    const pslRows = arr(psl.json);
    const retRows = pslRows.filter(r => {
        const s = JSON.stringify(r);
        return s.includes('반품') || n(r.supplyAmount ?? r.supply_amount ?? r.amount) < 0;
    });
    rec('3-6', '🔴 매입매출장에 반품이 (−) 로 반영됐나',
        pslRows.length === 0 ? `status=${psl.status} · 0행` : `${pslRows.length}행 중 반품/음수 ${retRows.length}행`,
        pslRows.length === 0 ? null : retRows.length > 0,
        '🔴 안 빠지면 매출세액 과대납부 — 세무사에게 나가는 문서다');

    const vat = await api('/api/finance/vat', T);
    rec('3-7', '부가세 집계 조회', `status=${vat.status} · ${vat.json ? '수신' : '본문 없음'}`,
        vat.status === 200 && !!vat.json);

    // ══════════════════════════════════════════════════════════
    // [축 ④] 🔴 화면 — "고쳤나" 가 아니라 "갔나"
    // ══════════════════════════════════════════════════════════
    console.log('\n══ [축 ④] 🔴 화면 실측 — 서버가 옳아도 화면에 안 닿으면 FAIL ══\n');

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

    await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 45000 }).catch(() => { });
    await page.waitForTimeout(6000);
    try {
        const idBox = page.locator('input[type=text]').first();
        const pwBox = page.locator('input[type=password]').first();
        await idBox.click({ timeout: 15000 }); await idBox.type(EMAIL, { delay: 60 });
        await page.keyboard.press('Tab'); await page.waitForTimeout(400);
        await pwBox.click({ timeout: 15000 }); await pwBox.type(PASS, { delay: 60 });
        await page.keyboard.press('Tab'); await page.waitForTimeout(1200);
        await page.getByRole('button', { name: '로그인', exact: true }).first().click({ timeout: 20000 });
        await page.waitForTimeout(11000);
        await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => { });
    } catch (e) { rec('4-0', '브라우저 로그인 조작', String(e).slice(0, 150), false); }

    let bt = await bodyText();
    const sbHits = ['설정관리', '업체관리', '상품관리', '판매관리', '매입관리'].filter(k => bt.includes(k));
    const loggedIn = sbHits.length >= 3;
    rec('4-0', '브라우저 로그인', `대메뉴 ${sbHits.length}/5`, loggedIn);
    if (!loggedIn) { save({ nav, pageErrs }); await browser.close(); process.exit(1); }

    const dev403 = nav.filter(x => x.status === 403).length;
    rec('4-0b', '🔴 기기승인 403 폭풍 없나 (판정 유효성 전제)', `403 ${dev403}건`, dev403 < 5,
        '🔴 403 이면 화면이 빈 건 결함이 아니라 미승인 기기 탓 — 그 FAIL 은 가짜다');
    if (dev403 >= 5) { save({ nav, pageErrs }); await browser.close(); process.exit(1); }

    // 4-1 판매목록(/deliveries) 에 반품이 보이나  🔴 1차에서 URL 을 틀렸던 자리
    await page.goto(`${BASE}/deliveries`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 40000 }).catch(() => { });
    await page.waitForTimeout(8000);
    await page.screenshot({ path: path.join(SHOTS, '10-deliveries.png'), fullPage: true }).catch(() => { });
    bt = await bodyText();
    const retNos = srt.map(r => r.returnNo || r.return_no).filter(Boolean);
    const shown = retNos.filter(no => bt.includes(no));
    rec('4-1', '🔴 판매목록 화면(/deliveries)에 반품 전표가 보이나',
        `서버 반품 ${retNos.length}건 중 화면 ${shown.length}건${shown.length ? ' (' + shown.slice(0, 3).join(', ') + ')' : ''}`,
        retNos.length === 0 ? null : shown.length > 0,
        '🔴 작16 의 핵심 — 저장은 되는데 아무 데도 안 보이던 자리');

    // 4-2 재고현황 화면
    await page.goto(`${BASE}/stock`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 40000 }).catch(() => { });
    await page.waitForTimeout(7000);
    await page.screenshot({ path: path.join(SHOTS, '20-stock.png'), fullPage: true }).catch(() => { });
    const btStock = await bodyText();
    const stockNames = balRows.slice(0, 5).map(b => b.itemName || b.item_name).filter(Boolean);
    const stockShown = stockNames.filter(nm => btStock.includes(nm));
    rec('4-2', '재고현황 화면에 품목이 실제로 뜨나',
        `서버 ${balRows.length}품목 · 표본 ${stockNames.length}개 중 화면 ${stockShown.length}개`,
        stockNames.length === 0 ? null : stockShown.length > 0);

    // 4-3 회계 정합성 화면 (8/27 작8 신설)
    await page.goto(`${BASE}/accounting/integrity`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 40000 }).catch(() => { });
    await page.waitForTimeout(7000);
    await page.screenshot({ path: path.join(SHOTS, '30-integrity.png'), fullPage: true }).catch(() => { });
    const btI = await bodyText();
    const jargI = JARGON.filter(k => btI.includes(k));
    rec('4-3', '회계 정합성 화면이 뜨나 (8/27 작8)',
        `본문 ${btI.replace(/\s+/g, ' ').length}자 · 개발용어 ${jargI.length ? jargI.join(',') : '없음'}`,
        btI.length > 200 && jargI.length === 0);

    // 4-4 매입매출장 화면
    await page.goto(`${BASE}/accounting/purchase-sales`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 40000 }).catch(() => { });
    await page.waitForTimeout(7000);
    await page.screenshot({ path: path.join(SHOTS, '40-purchase-sales.png'), fullPage: true }).catch(() => { });
    const btP = await bodyText();
    rec('4-4', '매입매출장 화면이 뜨나',
        `본문 ${btP.replace(/\s+/g, ' ').length}자`,
        btP.length > 200 && !JARGON.some(k => btP.includes(k)));

    // 4-5 매입반품 화면
    await page.goto(`${BASE}/returns`, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => { });
    await page.waitForLoadState('networkidle', { timeout: 40000 }).catch(() => { });
    await page.waitForTimeout(7000);
    await page.screenshot({ path: path.join(SHOTS, '50-purchase-returns.png'), fullPage: true }).catch(() => { });
    const btR = await bodyText();
    const pRetNos = prt.map(r => r.returnNo || r.return_no).filter(Boolean);
    const pShown = pRetNos.filter(no => btR.includes(no));
    rec('4-5', '매입반품 화면에 반품이 보이나',
        `서버 ${pRetNos.length}건 중 화면 ${pShown.length}건`,
        pRetNos.length === 0 ? null : pShown.length > 0,
        '🔴 매출만 고치고 매입이 같은 사고를 안고 있는지 — 대조축');

    // ══ 종합 ══
    console.log('\n══ [Z] 종합 ══\n');
    rec('Z-1', '페이지 오류(pageerror)', `${pageErrs.length}건${pageErrs.length ? ' · ' + pageErrs[0].slice(0, 90) : ''}`, pageErrs.length === 0);
    rec('Z-2', '네트워크 5xx', `${nav.filter(x => x.status >= 500).length}건 (4xx 포함 ${nav.length}건)`,
        nav.filter(x => x.status >= 500).length === 0);

    save({ nav, pageErrs, shots: SHOTS });
    await browser.close();
})().catch(e => {
    console.error('실행 예외:', e);
    rec('X', '스크립트 예외', String(e).slice(0, 300), false);
    save({ nav, pageErrs });
    process.exit(1);
});
