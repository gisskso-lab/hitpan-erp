// C안: 헤드풀 모드 — 사장님이 직접 로그인 + 30초 대기 + 자동 페이지 순회
// 실행: node audit-erp-99-headfull.js
// 브라우저 창이 뜨면 사장님이 직접 admin@hitpan.kr / Admin1234! 로그인하시면 됨
const { chromium } = require('playwright');

const WEB = 'http://localhost:5234';
const PAGES = [
    { name: '대시보드', path: '/dashboard', keyword: ['오늘', '판매', '매출', '대시보드'] },
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

(async () => {
    const browser = await chromium.launch({ headless: false, slowMo: 100 });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();

    console.log('=================================================');
    console.log('C안 헤드풀 — 사장님 직접 로그인 가도');
    console.log('=================================================');
    console.log('1. 브라우저 창이 뜸');
    console.log('2. 로그인 페이지에서 admin@hitpan.kr / Admin1234! 박제');
    console.log('3. 로그인 성공 후 자동으로 14 페이지 순회');
    console.log('4. 결과 JSON 박제');
    console.log('=================================================');

    await page.goto(`${WEB}/login`);
    console.log('로그인 대기 중... (최대 120초)');

    // 로그인 완료 박제 = url이 /login에서 다른 곳으로
    try {
        await page.waitForFunction(() => !window.location.pathname.startsWith('/login'),
            { timeout: 120000 });
        console.log('✓ 로그인 박제됨');
    } catch (e) {
        console.log('✗ 로그인 시간 초과 (120초)'); await browser.close(); process.exit(1);
    }

    await page.waitForTimeout(2000);
    const tokenInfo = await page.evaluate(() => ({
        local: Object.keys(localStorage),
        session: Object.keys(sessionStorage),
        url: window.location.href
    }));
    console.log(`  url: ${tokenInfo.url}`);
    console.log(`  localStorage: ${tokenInfo.local.join(', ')}`);
    console.log(`  sessionStorage: ${tokenInfo.session.join(', ')}`);
    console.log('');

    const results = [];
    for (const p of PAGES) {
        const errors = [];
        const onErr = msg => { if (msg.type() === 'error') errors.push(msg.text().slice(0, 200)); };
        page.on('console', onErr);

        const resp = await page.goto(`${WEB}${p.path}`, { timeout: 15000, waitUntil: 'domcontentloaded' }).catch(() => null);
        await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => {});
        await page.waitForTimeout(1500);

        const body = (await page.evaluate(() => document.body?.innerText || '').catch(() => ''))
            .replace(/\s+/g, ' ').slice(0, 500);
        const failHit = FAIL_KW.find(k => body.includes(k));
        const kwHit = p.keyword.find(k => body.includes(k));
        const status = resp?.status() ?? 0;
        let hint, ok = false;
        if (failHit) hint = `FAIL: ${failHit}`;
        else if (!kwHit) hint = `KW_MISS (errors: ${errors.length})`;
        else { hint = `OK: ${kwHit}`; ok = status < 400 && errors.length === 0; }

        const mark = ok ? '✓' : '✗';
        console.log(`${mark} [${status}] ${p.name.padEnd(14)} ${hint}`);
        if (!ok && body) console.log(`    body: ${body.slice(0, 80)}`);
        if (errors.length > 0) errors.slice(0, 2).forEach(e => console.log(`    err: ${e.slice(0, 80)}`));

        results.push({ ...p, status, ok, hint, body: body.slice(0, 300), errors: errors.slice(0, 3) });
        page.off('console', onErr);
    }

    const pass = results.filter(r => r.ok).length;
    console.log('');
    console.log('=================================================');
    console.log(`결과: ${pass}/${results.length} PASS (FAIL ${results.length - pass})`);
    console.log('=================================================');

    const fs = require('fs'), path = require('path');
    const dir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    const out = path.join(dir, `audit-erp-99-headfull-${stamp}.json`);
    fs.writeFileSync(out, JSON.stringify({ pass, fail: results.length - pass, total: results.length, results }, null, 2));
    console.log(`리포트: ${out}`);

    console.log('');
    console.log('30초 후 브라우저 닫힘. 더 확인하실 영역 있으면 Ctrl+C로 박제 유지.');
    await page.waitForTimeout(30000);
    await browser.close();
})();
