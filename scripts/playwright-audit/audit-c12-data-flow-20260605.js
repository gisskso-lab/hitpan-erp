// =====================================================================
// C1 + C2 데이터 흐름 실측 (사장님 결재 2026-06-05)
//
// C1: 랜딩 /api/landing/signup POST → landing_signups INSERT → tenants INSERT
// C2: ERP 부트스트랩 토큰 발급 (백오피스→ERP HMAC 흐름 정합 확인)
//
// 실측 데이터는 본 PM 테스트 전용 prefix (test-c12-), 종료 후 DELETE
// =====================================================================

const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const LANDING_API = process.env.HITPAN_LANDING_API || 'http://localhost:5082';
const BO_API = process.env.HITPAN_BO_API || 'http://localhost:5258';

const REPORT_DIR = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
const REPORT_PATH = path.join(REPORT_DIR, `audit-c12-data-flow-${new Date().toISOString().replace(/[:.]/g, '-')}.json`);

const results = {
    startedAt: new Date().toISOString(),
    c1: {},
    c2: {},
    cleanup: {}
};

function mysql(db, sql) {
    try {
        return execSync(`mysql -u root ${db} -se "${sql.replace(/"/g, '\\"')}"`, { encoding: 'utf-8' }).trim();
    } catch (e) {
        return `ERR: ${e.message.split('\n')[0]}`;
    }
}

async function safeFetch(url, init) {
    try {
        const res = await fetch(url, init);
        const text = await res.text();
        let body = text;
        try { body = JSON.parse(text); } catch {}
        return { status: res.status, ok: res.ok, body };
    } catch (e) {
        return { status: 0, ok: false, error: e.message };
    }
}

(async () => {
    const testCompanyName = `Test C1 ${Date.now()}`;
    const testBizNo = '123-45-67890';
    const testCeoName = 'PM Test';
    const testEmail = `test-c12-${Date.now()}@hitpan.kr`;

    // C1.1 — 랜딩 가입 신청
    console.log('\n=== C1. 랜딩 가입 흐름 ===');
    const signupRes = await safeFetch(`${BO_API}/api/landing/signup`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            companyName: testCompanyName,
            bizNo: testBizNo,
            ceoName: testCeoName,
            email: testEmail,
            phone: '01012345678',
            planType: 'basic',
            agreeTerms: true,
            agreePrivacy: true
        })
    });
    results.c1.signupApi = { status: signupRes.status, body: signupRes.body };
    console.log(`  POST /api/landing/signup → ${signupRes.status}`);

    // 백오피스 DB에 INSERT 됐는지 확인
    const dbSignup = mysql('hitpan_backoffice',
        `SELECT signup_token, company_name, status FROM landing_signups WHERE email='${testEmail}'`);
    results.c1.dbSignup = dbSignup;
    console.log(`  landing_signups DB: ${dbSignup.substring(0, 80)}`);

    const dbTenant = mysql('hitpan_backoffice',
        `SELECT tenant_code, status FROM tenants WHERE company_name='${testCompanyName.replace(/'/g, "''")}'`);
    results.c1.dbTenant = dbTenant;
    console.log(`  tenants DB: ${dbTenant.substring(0, 80)}`);

    // C2 — ERP 부트스트랩 토큰 발급 흐름 (백오피스 license-claim 흐름)
    console.log('\n=== C2. ERP 부트스트랩 토큰 ===');
    // 실제 토큰 발급은 라이선스 키 + biz_no 매칭 필요. 본 실측은 endpoint 응답 형식만 확인
    const licClaimRes = await safeFetch(`${BO_API}/api/landing/license/claim`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            tenantCode: 'NONEXISTENT-XX',
            licenseKey: 'HITP-TEST-TEST-TEST-TEST',
            companyName: testCompanyName
        })
    });
    results.c2.licenseClaim = { status: licClaimRes.status, body: licClaimRes.body };
    console.log(`  POST /api/backoffice/landing/license-claim → ${licClaimRes.status}`);

    // ERP webhook inbound 응답 확인 (서명 없이 401 정상)
    const erpInboundRes = await safeFetch('http://localhost:5257/api/internal/webhook/subscription', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: '{}'
    });
    results.c2.erpInbound = { status: erpInboundRes.status };
    console.log(`  POST ERP /api/internal/webhook/subscription → ${erpInboundRes.status}`);

    // 정리
    console.log('\n=== 정리 ===');
    const cleanupSignup = mysql('hitpan_backoffice', `DELETE FROM landing_signups WHERE email='${testEmail}'`);
    const cleanupTenant = mysql('hitpan_backoffice',
        `DELETE FROM tenants WHERE company_name='${testCompanyName.replace(/'/g, "''")}'`);
    results.cleanup = { signup: cleanupSignup || 'OK', tenant: cleanupTenant || 'OK' };
    console.log('  cleanup done');

    results.completedAt = new Date().toISOString();
    results.summary = {
        c1SignupOk: signupRes.status === 200 || signupRes.status === 201,
        c1DbInserted: dbSignup.includes(testEmail) || (dbSignup.length > 0 && !dbSignup.startsWith('ERR')),
        c2LicenseClaimResponse: licClaimRes.status,
        c2ErpInbound401: erpInboundRes.status === 401
    };

    if (!fs.existsSync(REPORT_DIR)) fs.mkdirSync(REPORT_DIR, { recursive: true });
    fs.writeFileSync(REPORT_PATH, JSON.stringify(results, null, 2), 'utf-8');

    console.log('\n=== 요약 ===');
    console.log(JSON.stringify(results.summary, null, 2));
    console.log(`Report: ${REPORT_PATH}`);
})();
