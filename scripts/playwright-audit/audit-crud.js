// B 가도 — 핵심 화면 CRUD 동작 검증 (버튼·폼·검색 클릭)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const BASE = 'https://demo.hitpan.kr';
const EMAIL = 'admin@hitpan.kr';
const PW = 'Admin1234!';
const OUT = path.join(__dirname, 'screenshots', '2026-05-26');

// 각 페이지에서 시도할 액션
const SCENARIOS = [
  {
    name: '15_업체마스터_검색',
    path: '/partners',
    actions: async (page) => {
      const search = page.locator('input[placeholder*="검색"], input[aria-label*="검색"]').first();
      if (await search.count() > 0) {
        await search.click();
        await search.pressSequentially('테스트', { delay: 30 });
        await page.waitForTimeout(1500);
        return 'search-typed';
      }
      return 'no-search-input';
    }
  },
  {
    name: '18_상품마스터_신규버튼',
    path: '/items',
    actions: async (page) => {
      const btn = page.locator('button:has-text("신규"), button:has-text("추가"), button:has-text("등록")').first();
      if (await btn.count() > 0) {
        await btn.click();
        await page.waitForTimeout(1500);
        const dialog = await page.locator('.mud-dialog, [role="dialog"]').count();
        return dialog > 0 ? 'dialog-opened' : 'btn-clicked-no-dialog';
      }
      return 'no-new-btn';
    }
  },
  {
    name: '31_재고현황_필터',
    path: '/stock',
    actions: async (page) => {
      const rows = await page.locator('table tbody tr, .mud-table-row').count();
      return 'rows=' + rows;
    }
  },
  {
    name: '36_수금_신규',
    path: '/collections',
    actions: async (page) => {
      const btn = page.locator('button:has-text("신규"), button:has-text("등록"), button:has-text("추가")').first();
      if (await btn.count() > 0) {
        await btn.click();
        await page.waitForTimeout(1500);
        const dialog = await page.locator('.mud-dialog, [role="dialog"]').count();
        return dialog > 0 ? 'dialog-opened' : 'btn-clicked-no-dialog';
      }
      return 'no-new-btn';
    }
  },
  {
    name: '29_세금계산서발행_탭열기',
    path: '/tax-invoice',
    actions: async (page) => {
      const btn = page.locator('button:has-text("신규"), button:has-text("발행")').first();
      if (await btn.count() > 0) {
        await btn.click();
        await page.waitForTimeout(1500);
        return 'btn-clicked';
      }
      return 'no-btn';
    }
  },
];

(async () => {
  const browser = await chromium.launch({ headless: false });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  const errors = [];
  const apiCalls = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text().slice(0, 200)); });
  page.on('requestfailed', r => {
    if (r.url().includes('/api/')) apiCalls.push({ url: r.url(), failure: r.failure()?.errorText });
  });

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
  console.log('[로그인 OK] → ' + page.url());

  const report = [];
  for (const s of SCENARIOS) {
    console.log('[CRUD] ' + s.name);
    const entry = { name: s.name, path: s.path };
    try {
      await page.goto(BASE + s.path, { waitUntil: 'domcontentloaded' });
      await page.waitForLoadState('networkidle').catch(() => {});
      await page.waitForTimeout(3500);
      const beforeErr = errors.length;
      entry.result = await s.actions(page);
      entry.newErrors = errors.slice(beforeErr);
      await page.screenshot({ path: path.join(OUT, 'CRUD_' + s.name + '.png'), fullPage: true });
    } catch (e) {
      entry.error = e.message.slice(0, 200);
    }
    report.push(entry);
    console.log('  → ' + (entry.result || entry.error));
  }

  fs.writeFileSync(path.join(OUT, '_CRUD_REPORT.json'), JSON.stringify({ report, errors, apiCalls }, null, 2), 'utf8');
  console.log('\n=== CRUD 조사 완료 ===');
  console.log('리포트: ' + path.join(OUT, '_CRUD_REPORT.json'));
  await browser.close();
})();
