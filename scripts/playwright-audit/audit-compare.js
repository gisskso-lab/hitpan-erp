// C 가도 — demo와 로컬(localhost:5234) 동시 비교 조사
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const TARGETS = [
  { name: 'DEMO', base: 'https://demo.hitpan.kr', api: 'https://api-demo.hitpan.kr' },
  { name: 'LOCAL', base: 'http://localhost:5234', api: 'http://localhost:5257' },
];
const EMAIL = 'admin@hitpan.kr';
const PW = 'Admin1234!';
const MENUS = [
  '/partners', '/items', '/stock', '/employees',
  '/sales/summary', '/purchase-status', '/tax-invoice',
  '/collections', '/accounting/cashbook', '/accounting/accounts',
];

async function audit(target) {
  const browser = await chromium.launch({ headless: true });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  const result = { target: target.name, base: target.base, loginOk: false, pages: [] };

  try {
    await page.goto(target.base, { waitUntil: 'domcontentloaded', timeout: 30000 });
    await page.waitForLoadState('networkidle').catch(() => {});
    await page.locator('input[type="email"]').first().click();
    await page.locator('input[type="email"]').first().pressSequentially(EMAIL, { delay: 20 });
    await page.locator('input[type="password"]').first().click();
    await page.locator('input[type="password"]').first().pressSequentially(PW, { delay: 20 });
    await page.waitForTimeout(500);
    await page.locator('button:has-text("로그인")').click();
    await page.waitForURL(u => !u.toString().includes('/login'), { timeout: 20000 });
    result.loginOk = true;
  } catch (e) {
    result.loginError = e.message.slice(0, 200);
  }

  for (const m of MENUS) {
    const entry = { path: m };
    try {
      await page.goto(target.base + m, { waitUntil: 'domcontentloaded', timeout: 30000 });
      await page.waitForLoadState('networkidle').catch(() => {});
      await page.waitForTimeout(3000);
      entry.title = await page.title();
      entry.rowCount = await page.locator('table tbody tr, .mud-table-row').count();
      entry.bodyLen = ((await page.textContent('body')) || '').length;
    } catch (e) {
      entry.error = e.message.slice(0, 100);
    }
    result.pages.push(entry);
  }

  await browser.close();
  return result;
}

(async () => {
  console.log('=== DEMO vs LOCAL 비교 ===\n');
  const out = [];
  for (const t of TARGETS) {
    console.log('[' + t.name + '] 가도...');
    try {
      const r = await audit(t);
      out.push(r);
    } catch (e) {
      console.log('[' + t.name + '] 실패: ' + e.message);
      out.push({ target: t.name, error: e.message });
    }
  }

  // 비교 출력
  const demo = out.find(x => x.target === 'DEMO');
  const local = out.find(x => x.target === 'LOCAL');
  console.log('\n=== 비교 결과 ===');
  console.log(`로그인: DEMO=${demo?.loginOk} LOCAL=${local?.loginOk}`);
  if (local?.loginError) console.log('  LOCAL 로그인 오류: ' + local.loginError);
  console.log('\n페이지별 (rows / bodyLen):');
  MENUS.forEach((m, i) => {
    const d = demo?.pages?.[i];
    const l = local?.pages?.[i];
    const diff = (d && l && (d.rowCount !== l.rowCount || Math.abs(d.bodyLen - l.bodyLen) > 1000)) ? ' ⚠️' : '';
    console.log(`  ${m}`);
    console.log(`    DEMO:  rows=${d?.rowCount} len=${d?.bodyLen} title="${d?.title}"`);
    console.log(`    LOCAL: rows=${l?.rowCount} len=${l?.bodyLen} title="${l?.title}"${diff}`);
  });

  fs.writeFileSync(path.join(__dirname, 'screenshots', '2026-05-26', '_COMPARE.json'),
    JSON.stringify(out, null, 2), 'utf8');
})();
