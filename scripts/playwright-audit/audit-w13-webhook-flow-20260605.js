// =====================================================================
// W13 Webhook 통합 실측 (사장님 결재 2026-06-05)
//
// 흐름:
//   1) 직접 SQL로 webhook_outbox 모의 INSERT (test target_url=httpbin)
//   2) WebhookDispatcher 사이클 대기 (max 90초)
//   3) status 변화 추적 (pending → dispatched / failed)
//   4) ERP webhook inbound 엔드포인트 응답 확인
//
// 실측 데이터 INSERT 0건 — 본 점검 후 DELETE
// =====================================================================

const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const REPORT_DIR = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
const REPORT_PATH = path.join(REPORT_DIR, `audit-w13-webhook-${new Date().toISOString().replace(/[:.]/g, '-')}.json`);

function mysql(sql) {
    try {
        const out = execSync(`mysql -u root hitpan_backoffice -e "${sql.replace(/"/g, '\\"')}"`, { encoding: 'utf-8' });
        return out;
    } catch (e) {
        return `ERR: ${e.message}`;
    }
}

const results = {
    startedAt: new Date().toISOString(),
    steps: []
};

function step(name, detail) {
    console.log(`[${new Date().toISOString().substring(11, 19)}] ${name}: ${detail}`);
    results.steps.push({ at: new Date().toISOString(), name, detail });
}

async function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

(async () => {
    // 1) 초기 상태
    step('outbox 초기 상태', mysql('SELECT status, COUNT(*) cnt FROM webhook_outbox GROUP BY status').trim());

    // 2) 모의 INSERT (httpbin 송신용, fake tenant_id)
    const nonce = 'test-' + Date.now();
    const tenantId = 'test-w13-' + Date.now().toString(36);
    const payload = JSON.stringify({ test: true, eventType: 'subscription_changed', tenantId });
    const insertSql = `INSERT INTO webhook_outbox (tenant_id, event_type, target_url, payload_json, signature, nonce, status) VALUES ('${tenantId}', 'subscription_changed', 'https://httpbin.org/post', '${payload.replace(/'/g, "''")}', 'test-sig-w13', '${nonce}', 'pending')`;
    step('모의 INSERT', mysql(insertSql).trim() || 'OK');

    const inserted = mysql(`SELECT outbox_id, status, retry_count FROM webhook_outbox WHERE nonce='${nonce}'`).trim();
    step('INSERT 검증', inserted);

    // 3) Dispatcher 사이클 대기 (최대 90초, 5초마다 점검)
    step('Dispatcher 사이클 대기 시작', '5초 주기 polling, 최대 90초');
    let final = null;
    for (let i = 0; i < 18; i++) {
        await sleep(5000);
        const status = mysql(`SELECT status, retry_count, IFNULL(last_error, '') AS last_err FROM webhook_outbox WHERE nonce='${nonce}'`).trim();
        const lines = status.split('\n');
        if (lines.length >= 2) {
            const row = lines[1];
            if (row.startsWith('dispatched') || row.startsWith('failed')) {
                final = row;
                step(`Dispatcher 처리 완료 (${(i + 1) * 5}초)`, row);
                break;
            }
            if (i % 3 === 0) step(`+${(i + 1) * 5}초`, row);
        }
    }
    if (!final) {
        const last = mysql(`SELECT status, retry_count, IFNULL(last_error, '') FROM webhook_outbox WHERE nonce='${nonce}'`).trim();
        step('Timeout — dispatcher 미반응', last);
    }

    // 4) 정리 (테스트 데이터 DELETE)
    step('테스트 데이터 정리', mysql(`DELETE FROM webhook_outbox WHERE nonce='${nonce}'`).trim() || 'OK');

    // 5) ERP webhook inbound 엔드포인트 응답
    try {
        const r = await fetch('http://localhost:5257/api/internal/webhook/subscription', { method: 'POST' });
        step('ERP inbound 401/400 (서명 없음 정상)', `HTTP ${r.status}`);
    } catch (e) {
        step('ERP inbound 호출 실패', e.message);
    }

    results.completedAt = new Date().toISOString();
    results.summary = {
        finalStatus: final ? final.split(/\s+/)[0] : 'timeout',
        dispatchSucceeded: final?.startsWith('dispatched') ?? false
    };

    if (!fs.existsSync(REPORT_DIR)) fs.mkdirSync(REPORT_DIR, { recursive: true });
    fs.writeFileSync(REPORT_PATH, JSON.stringify(results, null, 2), 'utf-8');

    console.log('\n=== 요약 ===');
    console.log(JSON.stringify(results.summary, null, 2));
    console.log(`Report: ${REPORT_PATH}`);
})();
