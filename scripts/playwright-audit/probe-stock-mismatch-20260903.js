// 20260903 실측 (3차) — 🔴 재고 불일치 3건 규명
//
//   인계5 가 UNKNOWN 으로 남긴 자리. 정합성 화면이 「stock vs ledger 정합성 = 3건 불일치」 를 띄운다.
//   서버 검사식과 같은 계산을 재현해 **어느 품목이 얼마나** 어긋났는지 이름과 숫자로 특정한다.
//
//   서버 검사식(FinanceService.CheckIntegrityAsync):
//     item_stock.current_qty  vs  SUM(stock_ledger.qty_in) − SUM(stock_ledger.qty_out)
//     ABS(차) > 0.01 이면 불일치
//
//   🔴 내가 틀린 것 3개 (전부 기록):
//     1) POST /api/stock/ledger 를 빈 {} 로 불러 400 → 필수 파라미터(FromDate·ToDate·LedgerType) 누락이었다.
//     2) 파라미터를 채웠더니 이번엔 **7종·기간 무관 500**.
//     3) 🔴 브라우저로 관찰하니 화면은 그 주소를 **아예 안 쓴다** — 실제 경로는
//        **GET /api/reports/stock-ledger?view=item&from=…&to=…** 였다.
//     ⇒ 나는 **아무도 안 가는 길**을 재고 "원장이 비었다" 로 오판할 뻔했다.
//        (`/api/stock/ledger` 의 500 자체는 별건으로 남긴다 — 화면이 안 쓸 뿐 살아는 있어야 한다.)
//
//   🟢 읽기 전용.

const https = require('https');
const fs = require('fs');
const path = require('path');

const BASE = process.env.HITPAN_BASE || 'https://test1234.hitpan.kr';
const EMAIL = process.env.HITPAN_EMAIL || 'act0226';
const PASS = process.env.HITPAN_PASS || '11111111';

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
            method: opt.method || 'GET', headers, timeout: 60000
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
const arr = (j) => Array.isArray(j) ? j : ((j && (j.items || j.data || j.rows)) || []);

(async () => {
    console.log('='.repeat(78));
    console.log('🔴 재고 불일치 3건 규명 — item_stock vs stock_ledger');
    console.log('='.repeat(78));

    const login = await api('/api/auth/login', { method: 'POST', body: { email: EMAIL, password: PASS } });
    const TOKEN = login.json.accessToken;
    const devs = await api('/api/devices', { token: TOKEN });
    const dl = arr(devs.json);
    const DEV = ((dl.find(d => d.isMainPc) || dl[0]) || {}).deviceId;
    const T = { token: TOKEN, deviceId: DEV };

    // 1) 서버 판정 (대조 기준)
    const integ = await api('/api/finance/integrity-check', T);
    const igItems = arr(integ.json && (integ.json.items || integ.json));
    const stockChk = igItems.find(x => String(x.checkName || x.CheckName || '').includes('stock vs ledger'));
    const verdict = stockChk ? `${stockChk.status || stockChk.Status} / ${stockChk.detail || stockChk.Detail}` : '못 읽음';
    console.log(`\n[기준] 서버 판정 = ${verdict}`);

    // 2) 현재고
    const bal = await api('/api/stock/balance', T);
    const balRows = arr(bal.json);
    console.log(`[재료] 현재고 ${balRows.length}품목 (status ${bal.status})`);

    // 3) 🔴 재고원장 — 화면이 실제로 쓰는 경로 (브라우저 관찰로 확인)
    //    기간은 전 구간을 덮어야 누적이 맞는다.
    const FROM = '2000-01-01', TO = '2099-12-31';
    const led = await api(`/api/reports/stock-ledger?view=item&from=${FROM}&to=${TO}`, T);
    const ledRows = arr(led.json);
    console.log(`[재료] 재고원장 ${ledRows.length}행 (status ${led.status}) · GET /api/reports/stock-ledger`);
    if (!ledRows.length) {
        console.log('응답 표본:', JSON.stringify(led.json).slice(0, 400));
        console.log('\n⚠️ 원장이 비어 대조 불가 — UNKNOWN 유지. 추측하지 않는다.');
        return;
    }
    console.log('원장 표본:', JSON.stringify(ledRows[0]).slice(0, 300));
    console.log('현재고 표본:', JSON.stringify(balRows[0]).slice(0, 300));

    // 4) 서버 검사식과 같은 계산
    const ledSum = {}, ledName = {};
    for (const L of ledRows) {
        const id = L.itemId || L.item_id;
        if (!id) continue;
        const inQ = n(L.inQty ?? L.qtyIn ?? L.qty_in ?? L.inboundQty);
        const outQ = n(L.outQty ?? L.qtyOut ?? L.qty_out ?? L.outboundQty);
        ledSum[id] = (ledSum[id] || 0) + (inQ - outQ);
        ledName[id] = L.itemName || L.item_name || id;
    }

    const diffs = [];
    let compared = 0, onlyInStock = [], onlyInLedger = [];
    for (const b of balRows) {
        const id = b.itemId || b.item_id;
        const cur = n(b.currentQty ?? b.current_qty ?? b.qty ?? b.stockQty ?? b.balanceQty);
        if (!(id in ledSum)) { onlyInStock.push(b.itemName || b.item_name || id); continue; }
        compared++;
        const lq = ledSum[id];
        if (Math.abs(cur - lq) > 0.01) {
            diffs.push({
                item: b.itemName || b.item_name || ledName[id] || id, itemId: id,
                현재고: cur, 원장누적: Math.round(lq * 1000) / 1000,
                차: Math.round((cur - lq) * 1000) / 1000
            });
        }
    }
    const stockIds = new Set(balRows.map(b => b.itemId || b.item_id));
    for (const id of Object.keys(ledSum)) if (!stockIds.has(id)) onlyInLedger.push(ledName[id]);

    console.log(`\n${'─'.repeat(78)}`);
    console.log(`대조 ${compared}품목 · 🔴 불일치 ${diffs.length}건  (서버 판정: ${verdict})`);
    console.log('─'.repeat(78));
    for (const d of diffs) {
        console.log(`🔴 ${d.item}`);
        console.log(`     현재고 ${d.현재고} · 원장누적 ${d.원장누적} · 차 ${d.차 > 0 ? '+' : ''}${d.차}`);
        console.log(`     itemId=${d.itemId}`);
    }
    if (onlyInStock.length) console.log(`\n⚠️ 원장에 이동이 전혀 없는 품목 ${onlyInStock.length}개: ${onlyInStock.slice(0, 8).join(', ')}`);
    if (onlyInLedger.length) console.log(`⚠️ 현재고 표에 없는데 원장엔 있는 품목 ${onlyInLedger.length}개: ${onlyInLedger.slice(0, 8).join(', ')}`);

    // 5) 불일치 품목의 이동 내역 — 왜 어긋났는지 단서
    for (const d of diffs.slice(0, 3)) {
        const mv = ledRows.filter(L => (L.itemId || L.item_id) === d.itemId);
        console.log(`\n── ${d.item} · 원장 ${mv.length}행 ──`);
        for (const m of mv.slice(0, 15)) {
            const inQ = n(m.inQty ?? m.qtyIn ?? m.qty_in), outQ = n(m.outQty ?? m.qtyOut ?? m.qty_out);
            console.log(`   ${String(m.ledgerDate || m.ledger_date || m.date || '').slice(0, 10)} ` +
                `${String(m.transactionType || m.transaction_type || m.sourceType || m.type || '').padEnd(16)} ` +
                `입 ${String(inQ).padStart(7)} 출 ${String(outQ).padStart(7)}  ${m.documentNo || m.document_no || ''}`);
        }
    }

    const out = {
        when: new Date().toISOString(),
        endpointUsed: '/api/reports/stock-ledger?view=item (화면이 실제 쓰는 경로 · 브라우저 관찰로 확인)',
        serverVerdict: verdict, compared, mismatchCount: diffs.length, diffs,
        onlyInStock, onlyInLedger
    };
    const dir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    fs.mkdirSync(dir, { recursive: true });
    const f = path.join(dir, `probe-stock-mismatch-${new Date().toISOString().slice(0, 10)}.json`);
    fs.writeFileSync(f, JSON.stringify(out, null, 2));
    console.log(`\n리포트: ${f}`);
})().catch(e => { console.error('예외:', e); process.exit(1); });
