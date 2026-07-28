// v3c: 콘솔 에러 영역 박제 (어디서 에러 나는지)
const { chromium } = require('playwright');
const http = require('http');

const WEB = process.env.HITPAN_WEB || 'http://localhost:5234';
const API = process.env.HITPAN_API || 'http://localhost:5257';
const EMAIL = 'admin@hitpan.kr';
const PASS = 'Admin1234!';

const PAGES = [
    { name: '대시보드', path: '/dashboard' },
    { name: '양식정보설정', path: '/settings/form-templates' },
    { name: '견적서', path: '/quotations' },
    { name: '반품처리', path: '/returns' },
    { name: '상품마스터-신규', path: '/items/new' },
];

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
            res.on('end', () => { try { resolve({ status: res.statusCode, json: JSON.parse(data) }); } catch { resolve({ raw: data }); } });
        });
        req.on('error', reject);
        req.write(body); req.end();
    });
}

const encode = v => Buffer.from(JSON.stringify(v), 'utf-8').toString('base64');

(async () => {
    const login = await apiLogin();
    if (!login.json?.accessToken) { console.log('로그인 실패'); process.exit(1); }
    console.log(`로그인 ✓ userName=${login.json.userName}`);
    console.log('');

    const browser = await chromium.launch({ headless: true, args: ['--disable-application-cache', '--disk-cache-size=0'] });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true, bypassCSP: true });
    await ctx.route('**/*', route => route.continue());
    await ctx.clearCookies();
    await ctx.addInitScript(({ a, r, u }) => {
        localStorage.setItem('hitpan_access_token', a);
        localStorage.setItem('hitpan_refresh_token', r);
        localStorage.setItem('hitpan_user_name', u);
    }, { a: encode(login.json.accessToken), r: encode(login.json.refreshToken), u: encode(login.json.userName) });
    const page = await ctx.newPage();

    // 첫 로드
    await page.goto(`${WEB}/dashboard`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2000);

    for (const p of PAGES) {
        console.log(`=== ${p.name} (${p.path}) ===`);
        const errors = [];
        const reqs = [];
        const onErr = msg => { if (msg.type() === 'error') errors.push(msg.text()); };
        const onReq = resp => {
            if (resp.status() >= 400) reqs.push(`${resp.status()} ${resp.url().slice(0, 80)}`);
        };
        page.on('console', onErr);
        page.on('response', onReq);

        await page.goto(`${WEB}${p.path}`, { waitUntil: 'domcontentloaded' }).catch(() => {});
        await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => {});
        await page.waitForTimeout(2000);

        const url = page.url();
        const body = (await page.evaluate(() => document.body?.innerText || '').catch(() => '')).slice(0, 250);
        console.log(`  url: ${url}`);
        console.log(`  body: ${body.replace(/\s+/g, ' ').slice(0, 150)}`);
        console.log(`  console errors: ${errors.length}`);
        errors.slice(0, 3).forEach(e => console.log(`    err: ${e.slice(0, 150)}`));
        console.log(`  failed requests (4xx/5xx): ${reqs.length}`);
        reqs.slice(0, 5).forEach(r => console.log(`    ${r}`));
        console.log('');

        page.off('console', onErr);
        page.off('response', onReq);
    }

    await browser.close();
})();
