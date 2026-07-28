// =====================================================================
// 히트판 ERP 99% 가도 전수조사 (사장님 작업지시 2026-05-31)
// 범위: 1-5번 박제 후 동작 검증
// 1. 양식정보설정 페이지 로드·시드 6건·PaperMode 분기 UI
// 2. 상품마스터 ItemSpecManager UI (1:N CRUD)
// 3. 6대 그리드 규격 콤보박스 동작 (PC)
// 4. 반품 페이지 PurchaseReturnGrid + 반품 사유 컴포
// 5. 약관 4건 강제 동의 강제 흐름
//
// 사장님 조건: 동작 검증은 제한적 (3시스템 연결 전, 계정 발급 전)
// → 본 스크립트는 UI 로드·콘솔 에러·404 영역만 박제
// =====================================================================

const { chromium } = require('playwright');

const BASE = process.env.HITPAN_BASE || 'http://localhost:5234';
const TENANT_EMAIL = process.env.HITPAN_EMAIL || 'tenant@hitpan.kr';
const TENANT_PASS = process.env.HITPAN_PASS || 'Admin1234!';

const PAGES = [
    { name: '양식정보설정', path: '/settings/form-templates' },
    { name: '상품마스터-신규', path: '/items/new' },
    { name: '견적서', path: '/quotations' },
    { name: '수주서', path: '/sales-orders' },
    { name: '거래명세서', path: '/delivery' },
    { name: '발주서', path: '/purchase-orders' },
    { name: '매입명세서', path: '/purchase-receipts' },
    { name: '반품처리', path: '/returns' },
    { name: '약관페이지', path: '/terms' },
    { name: '대시보드', path: '/dashboard' },
    { name: '상품관리', path: '/items' },
    { name: '업체관리', path: '/partners' },
    { name: '재고현황', path: '/stock' },
    { name: '세금계산서', path: '/sales/tax-invoices' },
    { name: '회계', path: '/accounting' },
    { name: '경비처리', path: '/finance/expenses' },
];

async function login(page) {
    console.log(`[LOGIN] ${TENANT_EMAIL}`);
    await page.goto(`${BASE}/login`);
    await page.waitForLoadState('networkidle');
    try {
        await page.fill('input[type="email"]', TENANT_EMAIL);
        await page.fill('input[type="password"]', TENANT_PASS);
        await page.click('button:has-text("로그인")');
        await page.waitForLoadState('networkidle', { timeout: 10000 });
        console.log('  ✓ 로그인 시도 완료');
    } catch (e) {
        console.log(`  ✗ 로그인 실패: ${e.message}`);
    }
}

async function auditPage(page, { name, path }) {
    const result = { name, path, ok: false, errors: [], warnings: [], status: 0 };
    const consoleErrors = [];

    page.on('console', msg => {
        if (msg.type() === 'error') consoleErrors.push(msg.text().slice(0, 200));
    });
    page.on('pageerror', err => consoleErrors.push(`PAGE: ${err.message.slice(0, 200)}`));

    try {
        const resp = await page.goto(`${BASE}${path}`, { timeout: 15000, waitUntil: 'domcontentloaded' });
        result.status = resp?.status() ?? 0;
        await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => {});

        const title = await page.title();
        const bodyText = (await page.evaluate(() => document.body?.innerText || '').catch(() => ''))
            .slice(0, 200);

        result.title = title;
        result.bodyPreview = bodyText;
        result.errors = consoleErrors.slice(0, 5);
        result.ok = result.status >= 200 && result.status < 400 && consoleErrors.length === 0;
    } catch (e) {
        result.errors.push(`NAV: ${e.message.slice(0, 200)}`);
    }
    return result;
}

(async () => {
    const browser = await chromium.launch({ headless: true });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();

    await login(page);

    const results = [];
    for (const p of PAGES) {
        const r = await auditPage(page, p);
        const mark = r.ok ? '✓' : '✗';
        console.log(`${mark} [${r.status}] ${r.name} (${r.path}) — errors:${r.errors.length}`);
        if (r.errors.length > 0) {
            r.errors.forEach(e => console.log(`    └ ${e}`));
        }
        results.push(r);
    }

    const pass = results.filter(r => r.ok).length;
    const fail = results.length - pass;
    console.log('');
    console.log('===================================================');
    console.log(`결과: ${pass}/${results.length} PASS (FAIL ${fail})`);
    console.log('===================================================');

    const fs = require('fs');
    const path = require('path');
    const reportDir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    if (!fs.existsSync(reportDir)) fs.mkdirSync(reportDir, { recursive: true });
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    const reportPath = path.join(reportDir, `audit-erp-99-${stamp}.json`);
    fs.writeFileSync(reportPath, JSON.stringify({ pass, fail, total: results.length, results }, null, 2));
    console.log(`리포트: ${reportPath}`);

    await browser.close();
    process.exit(fail === 0 ? 0 : 1);
})();
