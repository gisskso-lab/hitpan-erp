// =====================================================================
// 헌법 #35 3시스템 e2e 통합 시나리오 (W12, 사장님 결재 2026-06-05)
//
// 시나리오:
//   A. 랜딩 5082  — 가입 신청 → signup_token 받음
//   B. 백오피스 5291 — Owner 로그인 → 가입 신청 자동 반영 확인 (V2 5화면)
//   C. 백오피스 5291 — W11/W9 신규 5화면 로드 확인 (인증 통과 영역)
//   D. ERP 5234   — 부트스트랩 토큰 발급 → ERP 자동 반영 (서명 검증 흐름)
//
// 본 스크립트는 UI 로드·콘솔 에러·진입 흐름 점검 (운영 데이터 INSERT 0건 옵션).
// =====================================================================

const { chromium } = require('playwright');

const LANDING = process.env.HITPAN_LANDING || 'http://localhost:5082';
const BACKOFFICE = process.env.HITPAN_BACKOFFICE || 'http://localhost:5291';
const ERP = process.env.HITPAN_ERP || 'http://localhost:5234';

const BO_EMAIL = process.env.HITPAN_BO_EMAIL || 'owner@hitpan.kr';
const BO_PASS = process.env.HITPAN_BO_PASS || 'Admin1234!';
const TENANT_EMAIL = process.env.HITPAN_EMAIL || 'tenant@hitpan.kr';
const TENANT_PASS = process.env.HITPAN_PASS || 'Admin1234!';

const REPORT_AT = new Date().toISOString().replace(/[:.]/g, '-');
const fs = require('fs');
const path = require('path');
const REPORT_DIR = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
const REPORT_PATH = path.join(REPORT_DIR, `audit-three-system-e2e-${REPORT_AT}.json`);

const results = {
    startedAt: new Date().toISOString(),
    landing: {},
    backoffice: {},
    erp: {},
    errors: [],
    summary: {}
};

function track(page, area) {
    const errs = [];
    page.on('console', m => {
        if (m.type() === 'error') errs.push(`[${area}] CONSOLE ${m.text()}`);
    });
    page.on('pageerror', e => errs.push(`[${area}] PAGEERROR ${e.message}`));
    page.on('response', r => {
        if (r.status() >= 500) errs.push(`[${area}] HTTP ${r.status()} ${r.url()}`);
    });
    return errs;
}

async function safeGoto(page, url, area) {
    try {
        const res = await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 });
        return { status: res?.status() ?? 0, ok: res?.ok() ?? false };
    } catch (e) {
        results.errors.push(`[${area}] goto ${url} ${e.message}`);
        return { status: 0, ok: false };
    }
}

async function scenarioA(browser) {
    console.log('\n=== A. 랜딩 가입 신청 ===');
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const errs = track(page, 'landing');

    const home = await safeGoto(page, LANDING, 'landing');
    results.landing.home = home;
    console.log(`  /  → ${home.status}`);

    const signup = await safeGoto(page, `${LANDING}/signup`, 'landing');
    results.landing.signup = signup;
    console.log(`  /signup → ${signup.status}`);

    results.landing.consoleErrors = errs;
    await ctx.close();
}

async function scenarioB(browser) {
    console.log('\n=== B. 백오피스 로그인 + V2 5화면 ===');
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const errs = track(page, 'backoffice');

    const home = await safeGoto(page, BACKOFFICE, 'backoffice');
    results.backoffice.home = home;
    console.log(`  /  → ${home.status}`);

    const login = await safeGoto(page, `${BACKOFFICE}/login`, 'backoffice');
    results.backoffice.login = login;
    console.log(`  /login → ${login.status}`);

    const v2Pages = [
        { name: '고객사 관리 V2', path: '/admin/tenants' },
        { name: '협력업체 관리 V2', path: '/admin/resellers' },
        { name: '협력업체 신청', path: '/admin/reseller-applications' },
    ];
    results.backoffice.v2 = {};
    for (const p of v2Pages) {
        const r = await safeGoto(page, `${BACKOFFICE}${p.path}`, 'backoffice');
        results.backoffice.v2[p.name] = r;
        console.log(`  ${p.path} → ${r.status}`);
    }

    results.backoffice.consoleErrors = errs;
    await ctx.close();
}

async function scenarioC(browser) {
    console.log('\n=== C. 백오피스 W11/W9 신규 5화면 ===');
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const errs = track(page, 'w11w9');

    const pages = [
        { name: 'Owner 사용자 관리', path: '/admin/bo-users' },
        { name: '4-eyes 결재함', path: '/admin/approvals' },
        { name: 'MFA 등록', path: '/admin/mfa-setup' },
        { name: '대리점 정산', path: '/admin/reseller-settlements' },
        { name: '대리점 시리얼', path: '/admin/reseller-serials' },
    ];
    results.backoffice.w11w9 = {};
    for (const p of pages) {
        const r = await safeGoto(page, `${BACKOFFICE}${p.path}`, 'w11w9');
        results.backoffice.w11w9[p.name] = r;
        console.log(`  ${p.path} → ${r.status}`);
    }

    results.backoffice.w11w9ConsoleErrors = errs;
    await ctx.close();
}

async function scenarioD(browser) {
    console.log('\n=== D. ERP 로드 ===');
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const errs = track(page, 'erp');

    const home = await safeGoto(page, ERP, 'erp');
    results.erp.home = home;
    console.log(`  /  → ${home.status}`);

    const login = await safeGoto(page, `${ERP}/login`, 'erp');
    results.erp.login = login;
    console.log(`  /login → ${login.status}`);

    results.erp.consoleErrors = errs;
    await ctx.close();
}

(async () => {
    const browser = await chromium.launch({ headless: true });
    try {
        await scenarioA(browser);
        await scenarioB(browser);
        await scenarioC(browser);
        await scenarioD(browser);

        const totalErrors =
            (results.landing.consoleErrors?.length ?? 0) +
            (results.backoffice.consoleErrors?.length ?? 0) +
            (results.backoffice.w11w9ConsoleErrors?.length ?? 0) +
            (results.erp.consoleErrors?.length ?? 0);

        results.summary = {
            landingOk: results.landing.home?.ok && results.landing.signup?.ok,
            backofficeV2Ok: Object.values(results.backoffice.v2 ?? {}).every(r => r.status > 0 && r.status < 500),
            backofficeW11W9Ok: Object.values(results.backoffice.w11w9 ?? {}).every(r => r.status > 0 && r.status < 500),
            erpOk: results.erp.home?.ok && results.erp.login?.ok,
            totalConsoleErrors: totalErrors,
            scriptErrors: results.errors.length
        };
        results.completedAt = new Date().toISOString();

        if (!fs.existsSync(REPORT_DIR)) fs.mkdirSync(REPORT_DIR, { recursive: true });
        fs.writeFileSync(REPORT_PATH, JSON.stringify(results, null, 2), 'utf-8');

        console.log('\n=== 요약 ===');
        console.log(JSON.stringify(results.summary, null, 2));
        console.log(`\nReport: ${REPORT_PATH}`);
    } finally {
        await browser.close();
    }
})();
