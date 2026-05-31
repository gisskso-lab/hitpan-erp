// v3-B2: HitPanProtectedLocalStorage 정합 박제 (Base64(JSON 문자열))
const { chromium } = require('playwright');
const http = require('http');

const WEB = process.env.HITPAN_WEB || 'http://localhost:5235';
const API = process.env.HITPAN_API || 'http://localhost:5257';
const EMAIL = 'admin@hitpan.kr';
const PASS = 'Admin1234!';

const PAGES = [
    { name: '대시보드', path: '/dashboard', keyword: ['대시보드', 'Dashboard', '오늘', '판매', '매출'] },
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
            res.on('end', () => { try { resolve({ status: res.statusCode, json: JSON.parse(data) }); } catch { resolve({ status: res.statusCode, raw: data }); } });
        });
        req.on('error', reject);
        req.write(body); req.end();
    });
}

// HitPanProtectedLocalStorage 정합 박제 — SetAsync(key, value) = Base64(JSON.Serialize(value))
function encodeForStorage(value) {
    const json = JSON.stringify(value);
    return Buffer.from(json, 'utf-8').toString('base64');
}

(async () => {
    console.log('B2. HitPanProtectedLocalStorage 정합 박제');
    const login = await apiLogin();
    if (login.status !== 200 || !login.json?.accessToken) {
        console.log(`로그인 실패: ${JSON.stringify(login.json || login.raw)}`); process.exit(1);
    }
    console.log(`  accessToken: ${login.json.accessToken.slice(0, 30)}...`);
    console.log(`  userName: ${login.json.userName}`);

    const accessB64 = encodeForStorage(login.json.accessToken);
    const refreshB64 = encodeForStorage(login.json.refreshToken);
    const userNameB64 = encodeForStorage(login.json.userName);

    const browser = await chromium.launch({ headless: true });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });

    // AuthStorageKeys 정합: hitpan_access_token / hitpan_refresh_token / hitpan_user_name
    // SESSION_KEYS = ['access_token', 'refresh_token'] → hitpan_* 키는 localStorage
    await ctx.addInitScript(({ access, refresh, userName }) => {
        localStorage.setItem('hitpan_access_token', access);
        localStorage.setItem('hitpan_refresh_token', refresh);
        localStorage.setItem('hitpan_user_name', userName);
    }, { access: accessB64, refresh: refreshB64, userName: userNameB64 });

    const page = await ctx.newPage();

    // 박제 정합 확인
    await page.goto(`${WEB}/dashboard`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);

    const storage = await page.evaluate(() => ({
        localKeys: Object.keys(localStorage),
        sessionKeys: Object.keys(sessionStorage),
        accessRaw: localStorage.getItem('hitpan_access_token')?.slice(0, 50)
    }));
    console.log(`  localStorage keys: ${storage.localKeys.join(', ')}`);
    console.log(`  sessionStorage keys: ${storage.sessionKeys.join(', ')}`);
    console.log(`  access(b64): ${storage.accessRaw?.slice(0, 30)}...`);
    console.log(`  url: ${page.url()}`);
    console.log('');

    const results = [];
    for (const p of PAGES) {
        const errors = [];
        const onErr = msg => { if (msg.type() === 'error') errors.push(msg.text().slice(0, 200)); };
        page.on('console', onErr);

        const resp = await page.goto(`${WEB}${p.path}`, { timeout: 15000, waitUntil: 'domcontentloaded' }).catch(() => null);
        await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => {});
        await page.waitForTimeout(1200);

        const body = (await page.evaluate(() => document.body?.innerText || '').catch(() => ''))
            .replace(/\s+/g, ' ').slice(0, 500);
        const failHit = FAIL_KW.find(k => body.includes(k));
        const kwHit = p.keyword.find(k => body.includes(k));
        const status = resp?.status() ?? 0;
        let hint, ok = false;
        if (failHit) hint = `FAIL: ${failHit}`;
        else if (!kwHit) hint = `KW_MISS`;
        else { hint = `OK: ${kwHit}`; ok = status < 400 && errors.length === 0; }

        const mark = ok ? '✓' : '✗';
        console.log(`${mark} [${status}] ${p.name.padEnd(14)} ${hint}`);
        if (!ok) console.log(`    body: ${body.slice(0, 100)}`);

        results.push({ ...p, status, ok, hint, body: body.slice(0, 200), errors });
        page.off('console', onErr);
    }

    const pass = results.filter(r => r.ok).length;
    console.log('');
    console.log(`=== B2 결과: ${pass}/${results.length} PASS ===`);

    const fs = require('fs'), path = require('path');
    const dir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    const out = path.join(dir, `audit-erp-99-v3b2-${stamp}.json`);
    fs.writeFileSync(out, JSON.stringify({ pass, fail: results.length - pass, total: results.length, results }, null, 2));
    console.log(`리포트: ${out}`);

    await browser.close();
})();
