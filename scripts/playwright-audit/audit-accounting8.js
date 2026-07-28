// 회계 8건 개별 정밀 재진단 — 각 페이지 독립 진입 + 충분 대기
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const BASE = 'https://demo.hitpan.kr';
const EMAIL = 'admin@hitpan.kr';
const PW = 'Admin1234!';
const OUT = path.join(__dirname, 'screenshots', '2026-05-26-acc8');

const PAGES = [
  '/accounting/expenses',
  '/accounting/profit',
  '/accounting/bills',
  '/accounting/bank-transactions',
  '/accounting/accounts',
  '/accounting/monthly-closing',
  '/data/backup',
  '/settings/mdb-migration',
];

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const browser = await chromium.launch({ headless: false });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  const report = [];

  // 로그인
  await page.goto(BASE, { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.locator('input[type="email"]').first().click();
  await page.locator('input[type="email"]').first().pressSequentially(EMAIL, { delay: 20 });
  await page.locator('input[type="password"]').first().click();
  await page.locator('input[type="password"]').first().pressSequentially(PW, { delay: 20 });
  await page.waitForTimeout(500);
  await page.locator('button:has-text("로그인")').click();
  await page.waitForURL(u => !u.toString().includes('/login'), { timeout: 20000 });
  console.log('[로그인 OK]');

  for (const p of PAGES) {
    const entry = { path: p, errors: [], apiFails: [] };
    const errs = [];
    const fails = [];
    const onConsole = m => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); };
    const onFail = r => { if (r.url().includes('/api/')) fails.push(r.url() + ' :: ' + r.failure()?.errorText); };
    page.on('console', onConsole);
    page.on('requestfailed', onFail);

    try {
      await page.goto(BASE + p, { waitUntil: 'domcontentloaded', timeout: 60000 });
      await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => {});
      await page.waitForTimeout(6000); // 충분히 대기
      entry.title = await page.title();
      entry.bodyLen = ((await page.textContent('body')) || '').length;
      entry.rowCount = await page.locator('table tbody tr, .mud-table-row').count();
      entry.h1 = await page.locator('h1, h2, h3, h4, h5, h6, .mud-typography-h4, .mud-typography-h5, .mud-typography-h6').allInnerTexts().then(a => a.slice(0, 3));
      entry.buttons = await page.locator('button:visible').allInnerTexts().then(a => a.slice(0, 10));
      entry.errors = [...errs];
      entry.apiFails = [...fails];
      entry.status = (errs.length === 0 && fails.length === 0 && entry.bodyLen > 16000) ? 'OK' : 'ISSUE';
      await page.screenshot({ path: path.join(OUT, p.replace(/\//g, '_') + '.png'), fullPage: true });
    } catch (e) {
      entry.error = e.message.slice(0, 300);
      entry.status = 'FAIL';
    }
    page.off('console', onConsole);
    page.off('requestfailed', onFail);
    report.push(entry);
    console.log(`[${entry.status}] ${p} | title=${entry.title || 'none'} | rows=${entry.rowCount} | len=${entry.bodyLen} | errs=${entry.errors?.length || 0} | apiFails=${entry.apiFails?.length || 0}`);
  }

  fs.writeFileSync(path.join(OUT, '_REPORT.json'), JSON.stringify(report, null, 2), 'utf8');
  console.log('\n완료 — ' + OUT);
  await browser.close();
})();
