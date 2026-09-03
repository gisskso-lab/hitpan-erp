// 20260903 실측 (4차) — 🔴 창고 사슬: 나간 창고로 되돌아오는가
//
//   사장님 오더:
//     "품목의 전체재고수량을 기준으로 (상품마스타, 원장, 재고관리)
//      마스터에서 창고1 = 몇 개 / 창고2 = 몇 개 이렇게 관리하면 되잖아.
//      입출고, 반품시 창고 자동세팅되도록 하고"
//
//   🟢 선행검증([1-V]) 결과 — **이미 그렇게 되어 있다**:
//     · item_stock    : UNIQUE (tenant_id, item_id, warehouse_id)  → 창고별 행
//     · stock_ledger  : warehouse_id NOT NULL                      → 창고별 기록
//     · DeliveryPage.razor:246 : `Warehouse = it.WarehouseId` // 반품은 나간 창고로 되돌아온다
//     ⇒ 설계는 사장님 말씀대로 서 있다. 이 스크립트는 **실제 데이터가 그런지** 를 잰다.
//
//   재는 것:
//     ① 원장의 창고별 누적 == 마스터의 창고별 재고   (창고를 넣고 대조하면 맞는가)
//     ② 반품 원장의 창고 == 원판매 원장의 창고        (나간 창고로 돌아왔는가)
//     ③ 창고 미지정(NULL/빈값) 원장 행이 있는가       (자동세팅이 새는 곳)
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

const R = [];
const rec = (id, what, got, pass, note) => {
    R.push({ id, what, got, pass, note: note || '' });
    const tag = pass === true ? '🟢 PASS' : pass === false ? '🔴 FAIL' : '⚠️ UNKNOWN';
    console.log(`${tag}  ${id}  ${what}`);
    console.log(`         → ${got}`);
    if (note) console.log(`         · ${note}`);
};

(async () => {
    console.log('='.repeat(78));
    console.log('🔴 창고 사슬 실측 — 나간 창고로 되돌아오는가');
    console.log('='.repeat(78));

    const login = await api('/api/auth/login', { method: 'POST', body: { email: EMAIL, password: PASS } });
    const TOKEN = login.json.accessToken;
    const devs = await api('/api/devices', { token: TOKEN });
    const dl = arr(devs.json);
    const DEV = ((dl.find(d => d.isMainPc) || dl[0]) || {}).deviceId;
    const T = { token: TOKEN, deviceId: DEV };

    // 창고 목록
    const whs = arr((await api('/api/warehouses', T)).json);
    console.log(`\n창고 ${whs.length}개: ${whs.map(w => `${w.warehouseName || w.name}(${w.whCode || w.wh_code || ''})`).join(', ')}\n`);

    // 마스터 현재고 — 창고별
    const bal = arr((await api('/api/stock/balance', T)).json);
    console.log(`마스터 현재고 ${bal.length}행 (창고별)`);

    // ① 창고별 대조
    //   🔴 1차에서 내가 틀렸다: /api/reports/stock-ledger?view=warehouse 를 썼는데
    //      수불부의 view 분기는 "partner" 와 기본(품목) **둘뿐**이라 창고축이 없다.
    //      → 품목축이 돌아왔고, 마스터(창고명)와 축이 달라 겹치는 키가 0인데도
    //        "18곳 전부 일치" 라는 **가짜 PASS** 가 났다. (대조군 없이 판정한 셈)
    //   ⇒ 창고축은 /api/reports/stock-status?view=warehouse 다 (ReportService: "warehouse" => STOCK_BY_WAREHOUSE).
    const stWh = await api('/api/reports/stock-status?view=warehouse', T);
    const stWhRows = arr(stWh.json);
    console.log(`재고현황(창고축) ${stWhRows.length}행 · status ${stWh.status}`);
    if (stWhRows.length) console.log(`  표본: ${JSON.stringify(stWhRows[0]).slice(0, 220)}`);

    if (stWhRows.length) {
        // 마스터(item_stock) 창고별 합
        const masterByWh = {};
        for (const b of bal) {
            const w = b.warehouseName || b.warehouse_name || b.warehouseId;
            masterByWh[w] = (masterByWh[w] || 0) + n(b.currentQty ?? b.current_qty);
        }
        // 창고현황 리포트의 창고별 합
        const rptByWh = {};
        for (const s of stWhRows) {
            const w = s.warehouseName || s.warehouse_name || s.label || s.Label;
            const q = n(s.currentQty ?? s.current_qty ?? s.qty ?? s.balance ?? s.totalQty);
            rptByWh[w] = (rptByWh[w] || 0) + q;
        }
        const names = [...new Set([...Object.keys(masterByWh), ...Object.keys(rptByWh)])];
        const bad = [], noPair = [];
        console.log('\n창고'.padEnd(20) + '마스터합'.padStart(12) + '현황합'.padStart(12) + '차'.padStart(10));
        console.log('-'.repeat(54));
        for (const w of names) {
            const m = masterByWh[w], l = rptByWh[w];
            const d = (m === undefined || l === undefined) ? null : Math.round((m - l) * 1000) / 1000;
            console.log(String(w).padEnd(20) + String(m ?? '-').padStart(12) + String(l ?? '-').padStart(12) +
                String(d === null ? '-' : d).padStart(10) + (d !== null && Math.abs(d) > 0.01 ? '  🔴' : ''));
            if (d === null) noPair.push(w); else if (Math.abs(d) > 0.01) bad.push(`${w}: 마스터 ${m} vs 현황 ${l}`);
        }
        const paired = names.length - noPair.length;
        rec('W-1', '🔴 창고별 마스터 재고 == 창고별 재고현황',
            paired === 0 ? '짝지어진 창고 0 — 축이 안 맞아 대조 불가' :
                (bad.length ? bad.join(' · ') : `창고 ${paired}곳 대조 전부 일치` + (noPair.length ? ` (한쪽에만 있는 ${noPair.length}곳 제외)` : '')),
            paired === 0 ? null : bad.length === 0,
            '🔴 짝지어진 키가 0이면 PASS 가 아니라 UNKNOWN 이다 — 1차에서 이걸 PASS 로 적었다');
    } else {
        rec('W-1', '창고별 대조', `창고축 조회 실패 status=${stWh.status}`, null);
    }

    // ② 반품이 나간 창고로 돌아왔나 — 전표 단위 대조
    const srt = arr((await api('/api/sales/returns', T)).json);
    const chainRows = [];
    for (const r of srt) {
        const rid = r.returnId || r.id || r.return_id;
        const rno = r.returnNo || r.return_no;
        const d = await api(`/api/sales/returns/${encodeURIComponent(rid)}`, T);
        const j = d.json || {};
        const items = arr(j.items || j.lines || []);
        const did = j.deliveryId || j.delivery_id;
        let dItems = [];
        if (did) {
            const dd = await api(`/api/sales/deliveries/${encodeURIComponent(did)}`, T);
            dItems = arr((dd.json || {}).items || []);
        }
        for (const it of items) {
            const iid = it.itemId || it.item_id;
            const rWh = it.warehouseId || it.warehouse_id || null;
            const src = dItems.find(x => (x.itemId || x.item_id) === iid);
            const dWh = src ? (src.warehouseId || src.warehouse_id || null) : null;
            chainRows.push({
                반품: rno, 품목: it.itemName || it.item_name || iid,
                판매창고: dWh, 반품창고: rWh,
                일치: (dWh && rWh) ? (String(dWh) === String(rWh)) : null
            });
        }
    }
    console.log('\n반품'.padEnd(22) + '품목'.padEnd(18) + '판매창고'.padEnd(14) + '반품창고'.padEnd(14) + '일치');
    console.log('-'.repeat(76));
    for (const c of chainRows) {
        const mark = c.일치 === true ? '🟢' : c.일치 === false ? '🔴' : '⚠️';
        console.log(String(c.반품).padEnd(22) + String(c.품목).slice(0, 16).padEnd(18) +
            String(c.판매창고 || '-').slice(0, 12).padEnd(14) + String(c.반품창고 || '-').slice(0, 12).padEnd(14) + mark);
    }
    const mismatch = chainRows.filter(c => c.일치 === false);
    const unknown = chainRows.filter(c => c.일치 === null);
    rec('W-2', '🔴 반품이 나간 창고로 되돌아왔나 (원전표 대조)',
        chainRows.length === 0 ? '반품 품목 0' :
            `${chainRows.length}줄 중 일치 ${chainRows.filter(c => c.일치 === true).length} · 불일치 ${mismatch.length} · 판정불가 ${unknown.length}`,
        chainRows.length === 0 ? null : (unknown.length === chainRows.length ? null : mismatch.length === 0),
        '🔴 DeliveryPage.razor:246 「반품은 나간 창고로 되돌아온다」 가 실제로 도는지');

    // ③ 창고 미지정 행
    const noWh = chainRows.filter(c => !c.반품창고);
    rec('W-3', '반품 줄에 창고가 비어 있는 건이 있나',
        noWh.length ? `${noWh.length}줄 (${noWh.map(x => x.반품).join(', ')})` : '없음',
        chainRows.length === 0 ? null : noWh.length === 0,
        '창고가 비면 서버가 기본창고(MAIN)로 폴백한다 — 나간 창고가 아닐 수 있다');

    const out = { when: new Date().toISOString(), results: R, chainRows, warehouses: whs.length };
    const dir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    fs.mkdirSync(dir, { recursive: true });
    const f = path.join(dir, `probe-warehouse-chain-${new Date().toISOString().slice(0, 10)}.json`);
    fs.writeFileSync(f, JSON.stringify(out, null, 2));
    console.log(`\n리포트: ${f}`);
    console.log(`🟢 ${R.filter(r => r.pass === true).length} · 🔴 ${R.filter(r => r.pass === false).length} · ⚠️ ${R.filter(r => r.pass !== true && r.pass !== false).length}`);
})().catch(e => { console.error('예외:', e); process.exit(1); });
