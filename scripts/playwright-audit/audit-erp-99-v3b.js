// v3-B: API 직접 호출 → 토큰 박제 → localStorage 박제 → 페이지 검증
const { chromium } = require('playwright');
const http = require('http');

const WEB = process.env.HITPAN_WEB || 'http://localhost:5234';
const API = process.env.HITPAN_API || 'http://localhost:5257';
const EMAIL = process.env.HITPAN_EMAIL || 'admin@hitpan.kr';
const PASS = process.env.HITPAN_PASS || 'Admin1234!';

const PAGES = [
    { name: '대시보드', path: '/dashboard', keyword: ['대시보드', 'Dashboard', '오늘'] },
    { name: '양식정보설정', path: '/settings/form-templates', keyword: ['양식', '템플릿', '순백지'] },
    { name: '상품관리', path: '/items', keyword: ['상품', '품목'] },
    { name: '상품마스터-신규', path: '/items/new', keyword: ['규격', '단위', '판매단가'] },
    { name: '견적서', path: '/quotations', keyword: ['견적', '거래처'] },
    { name: '수주서', path: '/sales-orders', keyword: ['수주', '거래처'] },
    { name: '거래명세서', path: '/deliveries', keyword: ['거래명세', '발행'] },
    { name: '발주서', path: '/purchase-orders', keyword: ['발주', '거래처'] },
    { name: '매입명세서', path: '/purchases', keyword: ['매입', '거래처'] },
    { name: '반품처리', path: '/returns', keyword: ['반품', '반품일자', '반품 사유'] },
    { name: '업체관리', path: '/partners', keyword: ['업체', '거래처', '사업자'] },
    { name: '재고현황', path: '/stock', keyword: ['재고', '창고'] },
    { name: '회계', path: '/accounting', keyword: ['회계', '계정', '분개'] },
    { name: '경비처리', path: '/accounting/expenses', keyword: ['경비', '비용'] },
];

const FAIL_KW = ['이메일 비밀번호 로그인', '페이지를 찾을 수 없'];

function apiLogin() {
    return new Promise((resolve, reject) => {
        const body = JSON.stringify({ email: EMAIL, password: PASS });
        const url = new URL(API + '/api/auth/login');
        const req = http.request({
            hostname: url.hostname, port: url.port, path: url.pathname,
            method: 'POST', headers: { 'Content-Type': 'application/json', 'Content-Length': body.length }
        }, res => {
            let data = '';
            res.on('data', c => data += c);
            res.on('end', () => {
                try {
                    const json = JSON.parse(data);
                    resolve({ status: res.statusCode, json });
                } catch (e) {
                    resolve({ status: res.statusCode, raw: data });
                }
            });
        });
        req.on('error', reject);
        req.write(body); req.end();
    });
}

async function auditPage(page, { name, path, keyword }) {
    const result = { name, path, ok: false, errors: [], status: 0, hint: '' };
    const consoleErrors = [];
    const onErr = msg => { if (msg.type() === 'error') consoleErrors.push(msg.text().slice(0, 200)); };
    page.on('console', onErr);
    page.on('pageerror', e => consoleErrors.push(`PAGE: ${e.message.slice(0, 200)}`));

    try {
        const resp = await page.goto(`${WEB}${path}`, { timeout: 15000, waitUntil: 'domcontentloaded' });
        result.status = resp?.status() ?? 0;
        await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => {});
        await page.waitForTimeout(1000);

        const body = (await page.evaluate(() => document.body?.innerText || '').catch(() => ''))
            .replace(/\s+/g, ' ').slice(0, 600);
        result.bodyPreview = body;

        const failHit = FAIL_KW.find(k => body.includes(k));
        const kwHit = keyword.find(k => body.includes(k));
        if (failHit) result.hint = `FAIL: ${failHit}`;
        else if (!kwHit) result.hint = `KW_MISS: ${keyword.join('|')}`;
        else {
            result.hint = `OK: ${kwHit}`;
            result.ok = result.status >= 200 && result.status < 400 && consoleErrors.length === 0;
        }
        result.errors = consoleErrors.slice(0, 3);
    } catch (e) {
        result.errors.push(`NAV: ${e.message.slice(0, 200)}`);
    } finally {
        page.removeAllListeners('console');
        page.removeAllListeners('pageerror');
    }
    return result;
}

(async () => {
    console.log('B. API 직접 로그인 → localStorage 박제');
    const login = await apiLogin();
    console.log(`  API status: ${login.status}`);
    if (login.status !== 200 || !login.json?.accessToken) {
        console.log(`  로그인 실패: ${JSON.stringify(login.json || login.raw)}`);
        process.exit(1);
    }
    console.log(`  accessToken: ${login.json.accessToken.slice(0, 30)}...`);
    console.log(`  tenantId: ${login.json.tenantId}`);
    console.log(`  userName: ${login.json.userName}`);

    const browser = await chromium.launch({ headless: true });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });

    // localStorage init 박제 — 페이지 로드 전에 토큰 미리 박제
    await ctx.addInitScript(({ access, refresh, userName }) => {
        localStorage.setItem('hitpan_access_token', access);
        if (refresh) localStorage.setItem('hitpan_refresh_token', refresh);
        if (userName) localStorage.setItem('hitpan_user_name', userName);
    }, { access: login.json.accessToken, refresh: login.json.refreshToken, userName: login.json.userName });

    const page = await ctx.newPage();

    // 토큰 박제 정합 확인
    await page.goto(`${WEB}/dashboard`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2000);
    const tokenInStorage = await page.evaluate(() => localStorage.getItem('hitpan_access_token'));
    console.log(`  localStorage 박제: ${tokenInStorage ? '✓' : '✗'}`);
    console.log('');

    const results = [];
    for (const p of PAGES) {
        const r = await auditPage(page, p);
        const mark = r.ok ? '✓' : '✗';
        console.log(`${mark} [${r.status}] ${r.name.padEnd(14)} ${r.hint}`);
        if (!r.ok && r.bodyPreview) console.log(`    └ ${r.bodyPreview.slice(0, 100)}`);
        if (r.errors.length > 0) r.errors.forEach(e => console.log(`    err: ${e.slice(0, 80)}`));
        results.push(r);
    }

    const pass = results.filter(r => r.ok).length;
    const fail = results.length - pass;
    console.log('');
    console.log('=================================================');
    console.log(`결과: ${pass}/${results.length} PASS (FAIL ${fail})`);
    console.log('=================================================');

    const fs = require('fs');
    const path = require('path');
    const dir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    const out = path.join(dir, `audit-erp-99-v3b-${stamp}.json`);
    fs.writeFileSync(out, JSON.stringify({ login: { status: login.status }, pass, fail, total: results.length, results }, null, 2));
    console.log(`리포트: ${out}`);

    await browser.close();
})();
