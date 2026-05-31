// =====================================================================
// 히트판 ERP 99% 가도 전수조사 v2 — 정직 봉합 (사장님 작업지시 2026-05-31)
// 이전 v1 자수: 로그인 실패 + bodyPreview "로그인 페이지"를 PASS로 박제 → 거짓
// v2 봉합:
//   1. MudTextField input fill + 이벤트 박제로 disabled 해제
//   2. 로그인 후 토큰 박제 확인
//   3. 페이지 진입 후 bodyPreview에 "로그인" "페이지를 찾을 수 없습니다" 들어가면 FAIL
//   4. 페이지 고유 키워드 박제 시 PASS
// =====================================================================

const { chromium } = require('playwright');

const BASE = process.env.HITPAN_BASE || 'http://localhost:5234';
const TENANT_EMAIL = process.env.HITPAN_EMAIL || 'tenant@hitpan.kr';
const TENANT_PASS = process.env.HITPAN_PASS || 'Admin1234!';

const PAGES = [
    { name: '대시보드', path: '/dashboard', keyword: ['대시보드', 'Dashboard'] },
    { name: '양식정보설정', path: '/settings/form-templates', keyword: ['양식', '템플릿', '순백지'] },
    { name: '상품관리', path: '/items', keyword: ['상품', '품목', '단가'] },
    { name: '상품마스터-신규', path: '/items/new', keyword: ['규격', '단위', '판매단가'] },
    { name: '견적서', path: '/quotations', keyword: ['견적', '유효기한', '거래처'] },
    { name: '수주서', path: '/sales-orders', keyword: ['수주', '거래처'] },
    { name: '거래명세서', path: '/delivery', keyword: ['거래명세', '발행'] },
    { name: '발주서', path: '/purchase-orders', keyword: ['발주', '거래처'] },
    { name: '매입명세서', path: '/purchase-receipts', keyword: ['매입', '거래처'] },
    { name: '반품처리', path: '/returns', keyword: ['반품', '반품일자'] },
    { name: '업체관리', path: '/partners', keyword: ['업체', '거래처', '사업자'] },
    { name: '재고현황', path: '/stock', keyword: ['재고', '창고'] },
    { name: '회계', path: '/accounting', keyword: ['회계', '계정'] },
    { name: '경비처리', path: '/finance/expenses', keyword: ['경비'] },
];

const FAIL_KEYWORDS = ['이메일', '비밀번호', '페이지를 찾을 수 없', '오류가 발생'];

async function login(page) {
    console.log(`[LOGIN] ${TENANT_EMAIL}`);
    await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });

    // MudTextField input 박제 + Blazor 변경 이벤트 박제
    const emailInput = page.locator('input').first();
    await emailInput.click();
    await emailInput.fill(TENANT_EMAIL);
    await emailInput.evaluate(el => el.dispatchEvent(new Event('change', { bubbles: true })));

    const passwordInput = page.locator('input[type="password"]');
    await passwordInput.click();
    await passwordInput.fill(TENANT_PASS);
    await passwordInput.evaluate(el => el.dispatchEvent(new Event('change', { bubbles: true })));

    // 버튼 enabled 박제 대기 (최대 5초)
    await page.waitForFunction(() => {
        const btns = Array.from(document.querySelectorAll('button'));
        return btns.some(b => b.textContent?.includes('로그인') && !b.disabled);
    }, { timeout: 5000 }).catch(() => console.log('  ⚠ 버튼 enabled 박제 안 됨'));

    await page.click('button:has-text("로그인"):not([disabled])', { timeout: 5000 }).catch(e =>
        console.log(`  ✗ 클릭 실패: ${e.message.slice(0, 100)}`)
    );

    // 로그인 응답 대기
    await page.waitForLoadState('networkidle', { timeout: 10000 }).catch(() => {});
    await page.waitForTimeout(1500);

    // 토큰 박제 확인
    const token = await page.evaluate(() => localStorage.getItem('hitpan_access_token'));
    const url = page.url();
    console.log(`  → url=${url}`);
    console.log(`  → token=${token ? token.slice(0, 30) + '...' : 'null'}`);
    return token != null;
}

async function auditPage(page, { name, path, keyword }) {
    const result = { name, path, ok: false, errors: [], status: 0, hint: '' };
    const consoleErrors = [];

    const onConsole = msg => {
        if (msg.type() === 'error') consoleErrors.push(msg.text().slice(0, 200));
    };
    const onError = err => consoleErrors.push(`PAGE: ${err.message.slice(0, 200)}`);
    page.on('console', onConsole);
    page.on('pageerror', onError);

    try {
        const resp = await page.goto(`${BASE}${path}`, { timeout: 15000, waitUntil: 'domcontentloaded' });
        result.status = resp?.status() ?? 0;
        await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => {});
        await page.waitForTimeout(800);

        const bodyText = (await page.evaluate(() => document.body?.innerText || '').catch(() => ''))
            .replace(/\s+/g, ' ')
            .slice(0, 400);
        result.bodyPreview = bodyText;

        const failHit = FAIL_KEYWORDS.find(k => bodyText.includes(k));
        const keywordHit = keyword.find(k => bodyText.includes(k));

        if (failHit) {
            result.hint = `FAIL_KEYWORD: ${failHit}`;
        } else if (!keywordHit) {
            result.hint = `KEYWORD_MISSING: ${keyword.join('|')}`;
        } else {
            result.hint = `KEYWORD_HIT: ${keywordHit}`;
            result.ok = result.status >= 200 && result.status < 400 && consoleErrors.length === 0;
        }
        result.errors = consoleErrors.slice(0, 5);
    } catch (e) {
        result.errors.push(`NAV: ${e.message.slice(0, 200)}`);
    } finally {
        page.off('console', onConsole);
        page.off('pageerror', onError);
    }
    return result;
}

(async () => {
    const browser = await chromium.launch({ headless: true });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();

    const loginOk = await login(page);
    console.log(`로그인 결과: ${loginOk ? '✓' : '✗'}`);
    console.log('');

    const results = [];
    for (const p of PAGES) {
        const r = await auditPage(page, p);
        const mark = r.ok ? '✓' : '✗';
        console.log(`${mark} [${r.status}] ${r.name.padEnd(12)} ${r.hint}`);
        if (r.errors.length > 0) r.errors.forEach(e => console.log(`    └ ${e.slice(0, 100)}`));
        if (!r.ok && r.bodyPreview) console.log(`    └ body: ${r.bodyPreview.slice(0, 80)}`);
        results.push(r);
    }

    const pass = results.filter(r => r.ok).length;
    const fail = results.length - pass;
    console.log('');
    console.log('=================================================');
    console.log(`로그인: ${loginOk ? '✓' : '✗'}  /  페이지: ${pass}/${results.length} PASS (FAIL ${fail})`);
    console.log('=================================================');

    const fs = require('fs');
    const path = require('path');
    const reportDir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    if (!fs.existsSync(reportDir)) fs.mkdirSync(reportDir, { recursive: true });
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    const reportPath = path.join(reportDir, `audit-erp-99-v2-${stamp}.json`);
    fs.writeFileSync(reportPath, JSON.stringify({ loginOk, pass, fail, total: results.length, results }, null, 2));
    console.log(`리포트: ${reportPath}`);

    await browser.close();
})();
