// 20260903 실측 (8차) — 사장님 전결: "안잰건 3건 모두 봉합해"
//
//   재는 것 (이제까지 UNKNOWN 이던 3축):
//     A. 계산서 → 세금계산서 사슬
//     B. BOM 전개 → 생산 → 재고
//     C. 재고이송 out/in 짝 정합
//
//   🔴 판정 규율 (7차에서 사장님께 지적받은 그대로):
//     · 데이터가 0 건이면 그것은 PASS 가 아니라 **UNKNOWN** 이다.
//       "문제 없음" 과 "확인할 대상이 없음" 은 다른 말이다.
//     · 필드명은 실제 응답을 찍어서 확인한다(6차 debitTotal 오진 재발 방지).
//     · 대조군 없는 통과는 UNKNOWN 으로 내린다(짝지은 건수를 반드시 출력).
//
//   🟢 읽기 전용 — 쓰기 0건. 운영 수술 금지(헌법 #39).

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

// 🔴 응답 구조를 모를 때 추측하지 않는다 — 실제 키를 찍는다(6차 오진 재발 방지).
const keysOf = (o) => (o && typeof o === 'object') ? Object.keys(o).join(', ') : '(객체 아님)';

(async () => {
    console.log('='.repeat(78));
    console.log('실측 8차 — 안 잰 3축을 잰다 (계산서 사슬 · BOM 생산 · 재고이송)');
    console.log('='.repeat(78));

    const login = await api('/api/auth/login', { method: 'POST', body: { email: EMAIL, password: PASS } });
    if (!login.json || !login.json.accessToken) {
        console.log('🔴 로그인 실패 — 측정 불가. status=' + login.status + ' ' + login.raw.slice(0, 200));
        process.exit(1);
    }
    const TOKEN = login.json.accessToken;
    const devs = await api('/api/devices', { token: TOKEN });
    const DEV = ((arr(devs.json).find(d => d.isMainPc) || arr(devs.json)[0]) || {}).deviceId;
    const T = { token: TOKEN, deviceId: DEV };

    const health = await api('/health');
    const ver = (health.json && health.json.checks && health.json.checks.version) || (health.json && health.json.version);
    console.log(`\n배포 버전: v${ver}\n`);

    // ══════════════════════════════════════════════════════════════════
    // A. 계산서 → 세금계산서 사슬
    // ══════════════════════════════════════════════════════════════════
    console.log('\n── A. 계산서 → 세금계산서 사슬 ' + '─'.repeat(40));

    // 🔴 경로는 컨트롤러에서 확인했다 — 추측하면 "0건" 오진이 난다.
    //    실제: [Route("api/sales/tax-invoices")] · List 는 from/to 를 받는다.
    const txList = await api('/api/sales/tax-invoices?from=2020-01-01&to=2030-12-31', T);
    const txs = arr(txList.json);
    console.log(`  응답 status=${txList.status} · 건수=${txs.length}`);
    if (txs.length > 0) console.log(`  필드: ${keysOf(txs[0])}`);

    if (txList.status !== 200) {
        rec('A-1', '세금계산서 목록 조회', `status=${txList.status}`, null,
            '🔴 API 가 안 열린다 — 이 축은 못 쟀다. 경로가 다르거나 권한 문제.');
    } else if (txs.length === 0) {
        rec('A-1', '세금계산서 건수', '0건', null,
            '🔴 데이터가 없어 사슬을 확인할 수 없다. **UNKNOWN 이지 PASS 아니다.** '
            + '측정하려면 시험 계산서 발행이 필요하고 그건 test1234 쓰기라 결재 대상이다.');
    } else {
        // 세금계산서가 원전표(거래명세서)를 물고 있는가
        const linked = txs.filter(t => t.deliveryId || t.sourceId || t.deliveryNo);
        rec('A-1', '세금계산서 ↔ 원전표 연결', `${linked.length}/${txs.length} 건이 원전표를 물고 있다`,
            linked.length === txs.length,
            linked.length === 0 ? '🔴 사슬이 끊겼다 — 어느 명세서에서 나온 계산서인지 알 수 없다.' : '');

        // 🔴 필드명은 amountTotal 이다 — totalAmount 로 읽어 "전부 0" 오진을 냈다(1차 판).
        //   6차에서 시산표를 debitTotal 대신 debit 으로 읽어 "0" 이라 보고한 것과 **같은 실수**다.
        //   ⇒ 값이 0 이면 계산이 맞아 0 인지 **내가 딴 칸을 읽은 건지** 먼저 가른다.
        const amtOk = txs.filter(t => n(t.amountTotal) > 0);
        rec('A-2', '세금계산서 금액', `금액>0 인 것 ${amtOk.length}/${txs.length}`,
            amtOk.length === txs.length, '금액 0 이면 발행 실패거나 미완성 전표다.');

        // 사슬 값이 실제로 맞물리는지 — 번호가 있고 발행 상태인가
        const issued = txs.filter(t => String(t.status || '').toLowerCase() === 'issued');
        rec('A-3', '세금계산서 발행 상태', `issued ${issued.length}/${txs.length}`,
            issued.length === txs.length,
            `대조군: 원전표 번호 ${txs.map(t => t.deliveryNo).filter(Boolean).length}건 확인됨`);
    }

    // ══════════════════════════════════════════════════════════════════
    // B. BOM 전개 → 생산 → 재고
    // ══════════════════════════════════════════════════════════════════
    console.log('\n── B. BOM 전개 → 생산 → 재고 ' + '─'.repeat(42));

    const bomList = await api('/api/bom', T);
    const boms = arr(bomList.json);
    console.log(`  응답 status=${bomList.status} · 건수=${boms.length}`);
    if (boms.length > 0) console.log(`  필드: ${keysOf(boms[0])}`);

    if (bomList.status !== 200) {
        rec('B-1', 'BOM 목록 조회', `status=${bomList.status}`, null, '🔴 API 가 안 열린다 — 못 쟀다.');
    } else if (boms.length === 0) {
        rec('B-1', 'BOM 건수', '0건', null, '🔴 BOM 이 없어 전개를 확인할 수 없다 — UNKNOWN.');
    } else {
        rec('B-1', 'BOM 등록', `${boms.length}건`, true, '');

        // 자재를 실제로 갖고 있는가 — 헤더만 있고 자재가 0이면 전개가 불가능하다
        let withItems = 0, checked = 0;
        for (const b of boms.slice(0, 5)) {
            const id = b.bomId || b.id || b.bomHeaderId;
            if (!id) continue;
            const d = await api(`/api/bom/${id}`, T);
            checked++;
            const items = arr(d.json && (d.json.items || d.json.components)) ;
            if (items.length > 0) withItems++;
        }
        rec('B-2', 'BOM 자재 구성', `조회 ${checked}건 중 자재 있는 것 ${withItems}건`,
            checked > 0 ? withItems === checked : null,
            withItems < checked ? '🔴 자재가 0인 BOM 은 전개해도 아무것도 안 빠진다.' : '');
    }

    // ══════════════════════════════════════════════════════════════════
    // C. 재고이송 out/in 짝 정합
    // ══════════════════════════════════════════════════════════════════
    console.log('\n── C. 재고이송 out/in 짝 ' + '─'.repeat(46));

    // 🔴 경로를 컨트롤러에서 확인했다: [HttpGet("transfer/history")] · [Route("api/stock")]
    //
    //   1차 판에서 /api/reports/stock-ledger 로 재려다 실패했다 — 그 응답은 **집계 뷰**라
    //   (label,qtyIn,qtyOut,balance,…) sourceType 이 아예 없어 이송을 골라낼 수가 없었다.
    //   그대로였으면 "이송 0건 = UNKNOWN" 이라는 **오진**을 낼 뻔했다. 실제로는 2건이 정상 존재한다.
    //   ⇒ 없다고 말하기 전에 **경로부터 의심한다.**
    const tr = await api('/api/stock/transfer/history?from=2020-01-01&to=2030-12-31', T);
    const transfers = arr(tr.json);
    console.log(`  응답 status=${tr.status} · 이송 건수=${transfers.length}`);
    if (transfers.length > 0) console.log(`  필드: ${keysOf(transfers[0])}`);

    if (tr.status !== 200) {
        rec('C-1', '재고이송 이력 조회', `status=${tr.status}`, null, '🔴 API 가 안 열린다 — 못 쟀다.');
    } else if (transfers.length === 0) {
        rec('C-1', '재고이송 건수', '0건', null,
            '🔴 이송 기록이 없어 짝을 확인할 수 없다 — UNKNOWN 이지 PASS 아니다.');
    } else {
        // 🔴 이송은 「어느 창고에서 → 어느 창고로」 가 둘 다 있어야 짝이다.
        //    한쪽만 있으면 재고가 공중에서 생기거나 사라진다.
        const paired = transfers.filter(t =>
            t.fromWarehouse && t.toWarehouse && t.fromWarehouse !== t.toWarehouse && n(t.qty) > 0);

        rec('C-1', '재고이송 출발↔도착 짝', `${paired.length}/${transfers.length} 건이 짝을 갖췄다`,
            paired.length === transfers.length,
            paired.length === transfers.length
                ? `대조군 있음 — 검사한 이송 ${transfers.length}건 전부 출발·도착이 다르고 수량>0. `
                  + transfers.map(t => `${t.itemName}: ${t.fromWarehouse}→${t.toWarehouse} ${t.qty}`).join(' · ')
                : '🔴 출발·도착이 같거나 수량이 0인 이송이 있다 — 재고가 어긋난다.');
    }

    // ══════════════════════════════════════════════════════════════════
    // 결과
    // ══════════════════════════════════════════════════════════════════
    console.log('\n' + '='.repeat(78));
    const pass = R.filter(r => r.pass === true).length;
    const fail = R.filter(r => r.pass === false).length;
    const unk = R.filter(r => r.pass === null).length;
    console.log(`판정: 🟢 PASS ${pass} · 🔴 FAIL ${fail} · ⚠️ UNKNOWN ${unk}`);
    if (unk > 0) {
        console.log('\n🔴 UNKNOWN 이 있다 — 이것을 "문제 없음" 으로 보고하지 않는다.');
        R.filter(r => r.pass === null).forEach(r => console.log(`   · ${r.id} ${r.what} — ${r.note}`));
    }
    console.log('='.repeat(78));

    const outPath = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports',
        'audit-unmeasured-3axis-2026-09-03.json');
    try {
        fs.writeFileSync(outPath, JSON.stringify({ version: ver, at: new Date().toISOString(), results: R }, null, 2));
        console.log('\n보고서: ' + outPath);
    } catch (e) { console.log('보고서 저장 실패: ' + e.message); }
})();
