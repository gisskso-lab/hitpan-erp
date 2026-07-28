// v3-A: MudBlazor input 이벤트 보강 (input·change·blur·keypress)
const { chromium } = require('playwright');

const BASE = process.env.HITPAN_BASE || 'http://localhost:5234';
const EMAIL = process.env.HITPAN_EMAIL || 'tenant@hitpan.kr';
const PASS = process.env.HITPAN_PASS || 'Admin1234!';

async function dispatchAll(elementHandle) {
    await elementHandle.evaluate(el => {
        ['input', 'change', 'blur', 'keyup'].forEach(type => {
            el.dispatchEvent(new Event(type, { bubbles: true }));
        });
    });
}

(async () => {
    const browser = await chromium.launch({ headless: true });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();

    console.log('A. MudBlazor input 이벤트 전수 박제 시도');
    await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });

    const emailInput = page.locator('input').first();
    await emailInput.click();
    await emailInput.type(EMAIL, { delay: 50 });
    await dispatchAll(emailInput);
    console.log(`  email 박제: ${await emailInput.inputValue()}`);

    const pwInput = page.locator('input[type="password"]');
    await pwInput.click();
    await pwInput.type(PASS, { delay: 50 });
    await dispatchAll(pwInput);
    console.log(`  password 박제: ${(await pwInput.inputValue()).length}자`);

    // 버튼 enabled 박제 대기 5초
    const enabled = await page.waitForFunction(() => {
        const btns = Array.from(document.querySelectorAll('button'));
        return btns.find(b => b.textContent?.includes('로그인') && !b.disabled);
    }, { timeout: 5000 }).catch(() => null);

    console.log(`  버튼 enabled: ${enabled ? '✓' : '✗'}`);

    if (enabled) {
        await page.click('button:has-text("로그인"):not([disabled])');
        await page.waitForLoadState('networkidle', { timeout: 10000 }).catch(() => {});
        await page.waitForTimeout(1500);
        const token = await page.evaluate(() => localStorage.getItem('hitpan_access_token'));
        const url = page.url();
        console.log(`  url=${url}`);
        console.log(`  token=${token ? token.slice(0, 30) + '...' : 'null'}`);
        console.log(`결과: A안 = ${token ? '✓ 봉합 성공' : '✗ 토큰 박제 안 됨'}`);
    } else {
        console.log('결과: A안 = ✗ 버튼 enabled 박제 실패');
    }

    await browser.close();
})();
