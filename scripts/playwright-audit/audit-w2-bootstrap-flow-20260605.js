// =====================================================================
// 3순위 — W2 부트스트랩 토큰 실차 통합 실측 (사장님 결재 2026-06-05)
//
// 흐름:
//   1) 랜딩 가입 (signup_token 받음, tenants.status='pending')
//   2) PM이 SQL로 status='active' + license_key_hash 설정 (실제 결제 시뮬레이션)
//   3) ERP /license/claim 호출 → 부트스트랩 토큰 받음
//   4) 토큰 클레임 + bootstrap_key 정합 확인
//   5) 정리 (DELETE)
//
// 본 실측은 백오피스→ERP 단방향 흐름 (헌법 #35 W2) 무결성 검증
// =====================================================================

const { execSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const BO_API = process.env.HITPAN_BO_API || 'http://localhost:5258';
const LICENSE_PEPPER = process.env.LICENSE_PEPPER || 'dev-pepper-2026';
const BIZ_PEPPER = process.env.BIZ_PEPPER || 'dev-pepper-2026';

const REPORT_DIR = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
const REPORT_PATH = path.join(REPORT_DIR, `audit-w2-bootstrap-${new Date().toISOString().replace(/[:.]/g, '-')}.json`);

function mysql(db, sql) {
    try {
        return execSync(`mysql -u root ${db} -se "${sql.replace(/"/g, '\\"')}"`, { encoding: 'utf-8' }).trim();
    } catch (e) {
        return `ERR: ${e.message.split('\n')[0]}`;
    }
}

function hmacSha256(message, key) {
    return crypto.createHmac('sha256', key).update(message).digest('hex');
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

const results = {
    startedAt: new Date().toISOString(),
    steps: []
};

function step(name, detail) {
    console.log(`[${new Date().toISOString().substring(11, 19)}] ${name}: ${detail}`);
    results.steps.push({ at: new Date().toISOString(), name, detail });
}

(async () => {
    const ts = Date.now();
    const testCompanyName = `W2 Test ${ts}`;
    const testBizNo = '1234567890';
    const testBizNoFormatted = '123-45-67890';
    const testCeoName = 'W2 PM';
    const testEmail = `test-w2-${ts}@hitpan.kr`;
    const testLicenseKey = `HITP-TEST-${ts}-W2-W2`;

    // 1) 가입
    step('1. 랜딩 가입 신청', '');
    const signupRes = await safeFetch(`${BO_API}/api/landing/signup`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            companyName: testCompanyName,
            bizNo: testBizNoFormatted,
            ceoName: testCeoName,
            email: testEmail,
            phone: '01012345678',
            planType: 'basic',
            agreeTerms: true,
            agreePrivacy: true
        })
    });
    step('1.1 signup 응답', `HTTP ${signupRes.status}`);
    if (signupRes.status !== 200) {
        step('FAIL', 'signup 실패로 진행 불가');
        results.summary = { signupOk: false };
        fs.writeFileSync(REPORT_PATH, JSON.stringify(results, null, 2), 'utf-8');
        return;
    }

    const tenantCode = mysql('hitpan_backoffice',
        `SELECT tenant_code FROM tenants WHERE company_name='${testCompanyName.replace(/'/g, "''")}'`).split('\n').pop().trim();
    step('1.2 tenant_code', tenantCode);

    // 2) PM이 SQL로 결제 완료 시뮬레이션 (license_key_hash + status='active')
    step('2. 결제 완료 시뮬레이션 (PM SQL)', '');
    const licenseHash = hmacSha256(testLicenseKey, LICENSE_PEPPER);
    step('2.1 license HMAC', licenseHash.substring(0, 16) + '...');

    const updateRes = mysql('hitpan_backoffice',
        `UPDATE tenants SET license_key_hash='${licenseHash}', status='active' WHERE tenant_code='${tenantCode}'`);
    step('2.2 UPDATE tenants', updateRes || 'OK');

    // 3) license/claim 호출
    step('3. license/claim 호출 (ERP 시뮬레이션)', '');
    const claimRes = await safeFetch(`${BO_API}/api/landing/license/claim`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            licenseKey: testLicenseKey,
            bizNo: testBizNo
        })
    });
    step('3.1 claim 응답', `HTTP ${claimRes.status}`);
    step('3.2 claim body keys', Object.keys(claimRes.body || {}).join(', '));

    const claimBody = claimRes.body || {};
    const bootstrapToken = claimBody.bootstrapToken || claimBody.BootstrapToken;
    step('3.3 bootstrapToken 발급?', bootstrapToken ? `${bootstrapToken.substring(0, 30)}...` : 'NULL');
    step('3.4 valid', String(claimBody.valid ?? claimBody.Valid));
    step('3.5 message', String(claimBody.message ?? claimBody.Message ?? ''));

    // 4) 토큰 형식 검증 (HMAC 토큰 = base64(payload).base64(signature))
    if (bootstrapToken) {
        const parts = bootstrapToken.split('.');
        step('4.1 토큰 파트', `${parts.length}개`);
        if (parts.length === 2) {
            try {
                const payload = JSON.parse(Buffer.from(parts[0], 'base64').toString('utf-8'));
                step('4.2 payload 클레임 키', Object.keys(payload).join(', '));
            } catch (e) {
                step('4.2 payload 파싱 실패', e.message);
            }
        }
    }

    // 5) 정리
    step('5. 정리', '');
    mysql('hitpan_backoffice', `DELETE FROM landing_signups WHERE email='${testEmail}'`);
    mysql('hitpan_backoffice', `DELETE FROM tenants WHERE tenant_code='${tenantCode}'`);
    step('5.1 DELETE 완료', '');

    results.completedAt = new Date().toISOString();
    results.summary = {
        signupOk: signupRes.status === 200,
        tenantCodeAssigned: !!tenantCode,
        licenseClaimStatus: claimRes.status,
        valid: claimBody.valid ?? claimBody.Valid ?? false,
        bootstrapTokenIssued: !!bootstrapToken
    };

    if (!fs.existsSync(REPORT_DIR)) fs.mkdirSync(REPORT_DIR, { recursive: true });
    fs.writeFileSync(REPORT_PATH, JSON.stringify(results, null, 2), 'utf-8');

    console.log('\n=== 요약 ===');
    console.log(JSON.stringify(results.summary, null, 2));
    console.log(`Report: ${REPORT_PATH}`);
})();
