// 2026-05-31 22:30 최종 종합 전수조사
// 사장님 지시: 그리드 콤보 + BOM/권한/사원/약관 + 워크플로우 3흐름 + 회계/경비 + 모바일
const { chromium } = require('playwright');
const http = require('http');

const WEB = process.env.HITPAN_WEB || 'http://localhost:5234';
const API = process.env.HITPAN_API || 'http://localhost:5257';
const EMAIL = 'admin@hitpan.kr';
const PASS = 'Admin1234!';

// === 시나리오 6대 영역 ===
// A. 약관 동의 페이지 + 기본 페이지 + 신규 시나리오 (BOM/권한/사원)
const PAGES = [
    // 작지② 양식정보설정
    { name: '양식정보설정', path: '/settings/form-templates', keyword: ['양식', '템플릿', '순백지'] },
    // 신규 — BOM/권한/사원/약관
    { name: 'BOM관리', path: '/bom', keyword: ['BOM', '자재', '생산'] },
    { name: '권한설정', path: '/users/permissions', keyword: ['권한', '역할', 'Role', '사용자'] },
    { name: '사원관리', path: '/employees', keyword: ['사원', '직원', '이메일', '입사'] },
    { name: '약관동의', path: '/terms', keyword: ['약관', '동의', '이용'] },
    // 회계·경비 CRUD 진입
    { name: '회계', path: '/accounting', keyword: ['회계', '계정', '분개', '경비'] },
    { name: '경비처리', path: '/accounting/expenses', keyword: ['경비', '비용', '지출'] },
    // 워크플로우 3흐름 — 매입
    { name: '발주서', path: '/purchase-orders', keyword: ['발주', '거래처'] },
    { name: '매입명세서', path: '/purchases', keyword: ['매입', '입고'] },
    { name: '반품처리', path: '/returns', keyword: ['반품'] },
    // 판매
    { name: '견적서', path: '/quotations', keyword: ['견적', '거래처'] },
    { name: '수주서', path: '/sales-orders', keyword: ['수주'] },
    { name: '거래명세서', path: '/deliveries', keyword: ['거래명세', '공급가액', '부가세', '메모'] },
    // 재고
    { name: '재고현황', path: '/stock', keyword: ['재고', '창고'] },
];

const FAIL_KW = ['페이지를 찾을 수 없', '500 Internal'];

function apiCall(method, path, body, token) {
    return new Promise((resolve, reject) => {
        const data = body ? JSON.stringify(body) : null;
        const url = new URL(API + path);
        const headers = { 'Content-Type': 'application/json' };
        if (data) headers['Content-Length'] = Buffer.byteLength(data);
        if (token) headers['Authorization'] = `Bearer ${token}`;
        const req = http.request({ hostname: url.hostname, port: url.port, path: url.pathname + url.search, method, headers }, res => {
            let d = '';
            res.on('data', c => d += c);
            res.on('end', () => { try { resolve({ status: res.statusCode, json: JSON.parse(d) }); } catch { resolve({ status: res.statusCode, raw: d }); } });
        });
        req.on('error', reject);
        if (data) req.write(data);
        req.end();
    });
}

function encodeForStorage(value) {
    return Buffer.from(JSON.stringify(value), 'utf-8').toString('base64');
}

(async () => {
    console.log('=== 2026-05-31 최종 종합 전수조사 ===');
    console.log('');

    // 1. 로그인
    const login = await apiCall('POST', '/api/auth/login', { email: EMAIL, password: PASS });
    if (login.status !== 200 || !login.json?.accessToken) {
        console.log(`로그인 실패: ${JSON.stringify(login.json || login.raw)}`); process.exit(1);
    }
    const token = login.json.accessToken;
    console.log(`✓ 로그인: ${login.json.userName}`);

    // 2. 워크플로우 3흐름 API 풀스택 검증 (헌법 #20)
    console.log('\n=== 워크플로우 3흐름 API 풀스택 검증 (헌법 #20) ===');

    // 흐름 1: 매입 — 발주 → 매입 → 재고 반영
    const partners = await apiCall('GET', '/api/partners?pageSize=5', null, token);
    const items = await apiCall('GET', '/api/items?pageSize=5', null, token);
    const itemArr = Array.isArray(items.json) ? items.json : (items.json?.items ?? []);
    const partnerArr = Array.isArray(partners.json) ? partners.json : (partners.json?.items ?? []);
    console.log(`  업체 ${partnerArr.length}건 / 상품 ${itemArr.length}건`);

    // 흐름 1-1: 발주 리스트 진입
    const purchaseOrders = await apiCall('GET', '/api/purchase-orders?pageSize=3', null, token);
    console.log(`  매입흐름: 발주 [${purchaseOrders.status}] ${purchaseOrders.json?.items?.length ?? 0}건`);

    // 흐름 1-2: 매입 리스트
    const purchases = await apiCall('GET', '/api/purchases?pageSize=3', null, token);
    console.log(`  매입흐름: 매입 [${purchases.status}] ${purchases.json?.items?.length ?? 0}건`);

    // 흐름 1-3: 재고 현황
    const stock = await apiCall('GET', '/api/stock?pageSize=3', null, token);
    console.log(`  매입흐름: 재고 [${stock.status}] ${stock.json?.items?.length ?? 0}건`);

    // 흐름 2: BOM
    const bom = await apiCall('GET', '/api/bom?pageSize=3', null, token);
    console.log(`  BOM흐름: BOM 마스터 [${bom.status}] ${bom.json?.items?.length ?? bom.json?.length ?? 0}건`);

    // 흐름 3: 판매 — 견적 → 수주 → 거래명세서
    const quotations = await apiCall('GET', '/api/quotations?pageSize=3', null, token);
    console.log(`  판매흐름: 견적 [${quotations.status}] ${quotations.json?.items?.length ?? 0}건`);
    const salesOrders = await apiCall('GET', '/api/sales-orders?pageSize=3', null, token);
    console.log(`  판매흐름: 수주 [${salesOrders.status}] ${salesOrders.json?.items?.length ?? 0}건`);
    const deliveries = await apiCall('GET', '/api/deliveries?pageSize=3', null, token);
    console.log(`  판매흐름: 거래명세서 [${deliveries.status}] ${deliveries.json?.items?.length ?? 0}건`);
    const taxInvoices = await apiCall('GET', '/api/tax-invoices?pageSize=3', null, token);
    console.log(`  판매흐름: 세금계산서 [${taxInvoices.status}] ${taxInvoices.json?.items?.length ?? 0}건`);

    // 4. 그리드 품명→규격 동적 로드 (item_specs)
    console.log('\n=== 그리드 규격 콤보 동적 로드 API 검증 ===');
    if (itemArr.length > 0) {
        const itemId = itemArr[0].itemId || itemArr[0].id;
        if (itemId) {
            const specs = await apiCall('GET', `/api/items/${itemId}/specs?activeOnly=true`, null, token);
            console.log(`  품목 ${itemId.slice(0, 8)}... → specs [${specs.status}] ${Array.isArray(specs.json) ? specs.json.length : 0}건`);
            if (Array.isArray(specs.json) && specs.json.length > 0) {
                console.log(`    예시: ${specs.json[0].specValue} (default=${specs.json[0].isDefault})`);
            }
        }
    }

    // 5. 회계·경비 API
    console.log('\n=== 회계·경비처리 API 검증 ===');
    const accounting = await apiCall('GET', '/api/accounting/journal?pageSize=3', null, token);
    console.log(`  회계 분개 [${accounting.status}]`);
    const expenses = await apiCall('GET', '/api/accounting/expenses?pageSize=3', null, token);
    console.log(`  경비처리 [${expenses.status}]`);

    // 6. UI 전수조사
    console.log('\n=== UI 전수조사 (14개 화면) ===');
    const accessB64 = encodeForStorage(token);
    const refreshB64 = encodeForStorage(login.json.refreshToken);
    const userNameB64 = encodeForStorage(login.json.userName);

    const browser = await chromium.launch({ headless: true });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    await ctx.addInitScript(({ access, refresh, userName }) => {
        localStorage.setItem('hitpan_access_token', access);
        localStorage.setItem('hitpan_refresh_token', refresh);
        localStorage.setItem('hitpan_user_name', userName);
    }, { access: accessB64, refresh: refreshB64, userName: userNameB64 });

    const page = await ctx.newPage();
    await page.goto(`${WEB}/dashboard`, { waitUntil: 'networkidle' }).catch(() => {});
    await page.waitForTimeout(4500);

    const results = [];
    for (const p of PAGES) {
        const errors = [];
        const onErr = msg => { if (msg.type() === 'error') errors.push(msg.text().slice(0, 150)); };
        page.on('console', onErr);

        const resp = await page.goto(`${WEB}${p.path}`, { timeout: 20000, waitUntil: 'domcontentloaded' }).catch(() => null);
        await page.waitForLoadState('networkidle', { timeout: 12000 }).catch(() => {});
        await page.waitForTimeout(3500);

        const body = (await page.evaluate(() => document.body?.innerText || '').catch(() => '')).replace(/\s+/g, ' ').slice(0, 500);
        const failHit = FAIL_KW.find(k => body.includes(k));
        const kwHit = p.keyword.find(k => body.includes(k));
        const status = resp?.status() ?? 0;
        let hint, ok = false;
        if (failHit) hint = `FAIL: ${failHit}`;
        else if (!kwHit) hint = `KW_MISS`;
        else { hint = `OK: ${kwHit}`; ok = status < 400; }

        console.log(`${ok ? '✓' : '✗'} [${status}] ${p.name.padEnd(12)} ${hint}${errors.length > 0 ? ` (errors=${errors.length})` : ''}`);
        results.push({ ...p, status, ok, hint, body: body.slice(0, 200), errorsCount: errors.length });
        page.off('console', onErr);
    }

    const pass = results.filter(r => r.ok).length;
    console.log('');
    console.log(`=== UI 결과: ${pass}/${results.length} PASS ===`);

    // 7. 그리드 콤보 동적 로드 UI 클릭 실측 (견적서)
    console.log('\n=== 그리드 콤보 UI 클릭 실측 (견적서) ===');
    let comboTest = { tried: false, ok: false, hint: '' };
    try {
        await page.goto(`${WEB}/quotations`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(4000);
        // 「신규」 또는 「+」 버튼 클릭 시도
        const newBtn = page.locator('button:has-text("신규"), button:has-text("새로"), button:has-text("+ 견적")').first();
        if (await newBtn.count() > 0) {
            comboTest.tried = true;
            await newBtn.click({ timeout: 3000 }).catch(() => {});
            await page.waitForTimeout(2500);
            // 라인 추가 버튼
            const addLineBtn = page.locator('button:has-text("라인 추가"), button:has-text("품목 추가"), button:has-text("+ 추가")').first();
            if (await addLineBtn.count() > 0) {
                await addLineBtn.click().catch(() => {});
                await page.waitForTimeout(1500);
                const specCombo = page.locator('input[placeholder*="규격"], .mud-autocomplete input').first();
                const found = await specCombo.count();
                comboTest.ok = found > 0;
                comboTest.hint = found > 0 ? `규격 콤보 ${found}건 발견` : '규격 콤보 미발견';
            } else {
                comboTest.hint = '라인 추가 버튼 미발견';
            }
        } else {
            comboTest.hint = '신규 버튼 미발견 (페이지 구조 변경 가능)';
        }
        console.log(`${comboTest.ok ? '✓' : '⚠'} 그리드 콤보: ${comboTest.hint}`);
    } catch (e) {
        comboTest.hint = `예외: ${e.message.slice(0, 80)}`;
        console.log(`⚠ 그리드 콤보: ${comboTest.hint}`);
    }

    // 8. 모바일 반응형 — iPhone 13 viewport
    console.log('\n=== 모바일 반응형 (iPhone 13: 390x844) ===');
    const mobileCtx = await browser.newContext({
        viewport: { width: 390, height: 844 },
        userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1',
        ignoreHTTPSErrors: true
    });
    await mobileCtx.addInitScript(({ access, refresh, userName }) => {
        localStorage.setItem('hitpan_access_token', access);
        localStorage.setItem('hitpan_refresh_token', refresh);
        localStorage.setItem('hitpan_user_name', userName);
    }, { access: accessB64, refresh: refreshB64, userName: userNameB64 });

    const mPage = await mobileCtx.newPage();
    const mobilePages = ['/dashboard', '/items', '/quotations', '/deliveries', '/stock'];
    const mobileResults = [];
    for (const path of mobilePages) {
        const resp = await mPage.goto(`${WEB}${path}`, { timeout: 20000, waitUntil: 'domcontentloaded' }).catch(() => null);
        await mPage.waitForTimeout(3000);
        const dims = await mPage.evaluate(() => ({
            scrollW: document.documentElement.scrollWidth,
            clientW: document.documentElement.clientWidth,
            hasHorizontal: document.documentElement.scrollWidth > document.documentElement.clientWidth + 2
        }));
        const ok = resp?.status() < 400 && !dims.hasHorizontal;
        console.log(`${ok ? '✓' : '⚠'} [${resp?.status() ?? 0}] ${path.padEnd(14)} scroll=${dims.scrollW}/${dims.clientW}${dims.hasHorizontal ? ' (가로 스크롤 발생!)' : ''}`);
        mobileResults.push({ path, status: resp?.status(), dims, ok });
    }
    const mobilePass = mobileResults.filter(r => r.ok).length;
    console.log(`=== 모바일 결과: ${mobilePass}/${mobileResults.length} PASS ===`);

    // 8. 리포트 박제
    const fs = require('fs'), path = require('path');
    const dir = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports');
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    const out = path.join(dir, `audit-final-${stamp}.json`);
    fs.writeFileSync(out, JSON.stringify({
        ui: { pass, fail: results.length - pass, total: results.length, results },
        mobile: { pass: mobilePass, total: mobileResults.length, results: mobileResults },
        comboTest
    }, null, 2));
    console.log(`\n리포트: ${out}`);

    await browser.close();
})();
