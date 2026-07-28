// 그리드 규격 콤보박스 UI 실측 (작지① 사장님 2026-05-31)
// 견적서 페이지 → 라인 추가 → 품명 선택 → 규격 콤보 옵션 자동 로드 확인
const { chromium } = require('playwright');
const http = require('http');

const WEB = process.env.HITPAN_WEB || 'http://localhost:5234';
const API = process.env.HITPAN_API || 'http://localhost:5257';
const EMAIL = 'admin@hitpan.kr';
const PASS = 'Admin1234!';

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

function encodeForStorage(value) {
    return Buffer.from(JSON.stringify(value), 'utf-8').toString('base64');
}

(async () => {
    console.log('=== 그리드 규격 콤보박스 UI 실측 (작지①) ===');
    const login = await apiLogin();
    if (login.status !== 200) { console.log('로그인 실패'); process.exit(1); }
    console.log(`로그인 ✓ ${login.json.userName}`);

    // 사전: API /api/items 첫 상품 + 그 상품의 규격 옵션 가져오기
    const tok = login.json.accessToken;
    const itemsRes = await new Promise((resolve, reject) => {
        const r = http.request({ hostname:'localhost', port:5257, path:'/api/items?page=1&pageSize=5', method:'GET', headers:{ Authorization:`Bearer ${tok}` }}, res => {
            let d=''; res.on('data',c=>d+=c); res.on('end',()=>{ try{resolve(JSON.parse(d));}catch{resolve(null);} });
        });
        r.on('error', reject); r.end();
    });
    const sample = itemsRes?.items?.[0] || itemsRes?.[0];
    if (!sample) { console.log('items 응답 없음'); console.log(JSON.stringify(itemsRes).slice(0,300)); }
    else {
        console.log(`샘플 상품: itemId=${sample.itemId ?? sample.ItemId} name=${sample.itemName ?? sample.ItemName}`);
        const itemId = sample.itemId ?? sample.ItemId;
        const specsRes = await new Promise((resolve, reject) => {
            const r = http.request({ hostname:'localhost', port:5257, path:`/api/items/${itemId}/specs?activeOnly=true`, method:'GET', headers:{ Authorization:`Bearer ${tok}` }}, res => {
                let d=''; res.on('data',c=>d+=c); res.on('end',()=>{ try{resolve(JSON.parse(d));}catch{resolve(null);} });
            });
            r.on('error', reject); r.end();
        });
        console.log(`샘플 상품 규격 옵션 (${specsRes?.length ?? 0}건): ${JSON.stringify(specsRes).slice(0,200)}`);
    }
    console.log('');

    // 견적서 페이지 UI 진입
    const browser = await chromium.launch({ headless: true });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    await ctx.addInitScript(({ access, refresh, userName }) => {
        localStorage.setItem('hitpan_access_token', access);
        localStorage.setItem('hitpan_refresh_token', refresh);
        localStorage.setItem('hitpan_user_name', userName);
    }, {
        access: encodeForStorage(login.json.accessToken),
        refresh: encodeForStorage(login.json.refreshToken),
        userName: encodeForStorage(login.json.userName)
    });
    const page = await ctx.newPage();

    await page.goto(`${WEB}/dashboard`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(4000);

    console.log('--- 견적서 (/quotations) 진입 ---');
    await page.goto(`${WEB}/quotations`, { waitUntil: 'domcontentloaded' });
    await page.waitForLoadState('networkidle', { timeout: 12000 }).catch(() => {});
    await page.waitForTimeout(4000);

    // ItemSpecAutocomplete가 DOM에 존재하는지 확인 (MudAutocomplete + placeholder)
    const html = await page.content();
    const hasPlaceholder = html.includes('품명 먼저 선택') || html.includes('규격 선택 또는 직접 입력');
    const hasGrid = html.includes('규격');
    console.log(`  견적서 그리드 헤더 '규격' 존재: ${hasGrid}`);
    console.log(`  ItemSpecAutocomplete placeholder 박제: ${hasPlaceholder}`);

    // 페이지 텍스트에 견적/품목 등 핵심 키워드
    const body = (await page.evaluate(() => document.body?.innerText || '')).replace(/\s+/g, ' ').slice(0, 300);
    console.log(`  body: ${body.slice(0, 200)}`);
    console.log('');

    // 다른 그리드 4종 진입 검증
    for (const p of ['/sales-orders','/deliveries','/purchase-orders','/purchases','/returns']) {
        await page.goto(`${WEB}${p}`, { waitUntil: 'domcontentloaded' }).catch(()=>{});
        await page.waitForLoadState('networkidle', { timeout: 10000 }).catch(()=>{});
        await page.waitForTimeout(3000);
        const h = await page.content();
        const hasPh = h.includes('품명 먼저 선택') || h.includes('규격 선택 또는 직접 입력');
        const hasSpecHeader = h.includes('규격');
        console.log(`${p.padEnd(20)} 규격헤더=${hasSpecHeader} placeholder=${hasPh}`);
    }

    await browser.close();
})();
