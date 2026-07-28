// 진범 #1·#2·#3·#4 실제 demo 동작 정밀 검증
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const BASE = 'https://demo.hitpan.kr';
const EMAIL = 'admin@hitpan.kr';
const PW = 'Admin1234!';
const OUT = path.join(__dirname, 'screenshots', '2026-05-26-verify');

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const browser = await chromium.launch({ headless: false });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  const findings = [];

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
  console.log('[로그인 OK]\n');

  // === 진범 #1: 상품 등록 클릭 → /items/new 페이지 이동 ===
  console.log('[진범 #1] /items 진입 → 상품 등록 버튼 클릭');
  await page.goto(BASE + '/items', { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(4000);
  await page.locator('button:has-text("상품 등록")').first().click();
  await page.waitForTimeout(3000);
  const url1 = page.url();
  await page.screenshot({ path: path.join(OUT, '01_상품등록_이동후.png'), fullPage: true });
  findings.push({ progress: '#1', after_click_url: url1, expected: '/items/new', result: url1.endsWith('/items/new') ? 'OK' : 'FAIL' });
  console.log('  → URL=' + url1 + ' (' + (url1.endsWith('/items/new') ? 'OK' : 'FAIL') + ')\n');

  // === 진범 #2: 수금 페이지 모든 visible 영역 박제 ===
  console.log('[진범 #2] /collections 모든 UI 박제');
  await page.goto(BASE + '/collections', { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(4000);
  await page.screenshot({ path: path.join(OUT, '02_수금_초기.png'), fullPage: true });
  const collectButtons = await page.locator('button:visible').allInnerTexts();
  const collectTexts = (await page.textContent('body')).slice(0, 2000);
  findings.push({ progress: '#2', visible_buttons: collectButtons.slice(0, 30), preview: collectTexts.slice(0, 500) });
  console.log('  → 버튼 ' + collectButtons.length + '개');
  console.log('  → 본문 길이 ' + collectTexts.length + '자\n');

  // === 진범 #3: 세금계산서 발행 모든 영역 박제 ===
  console.log('[진범 #3] /tax-invoice 모든 UI 박제');
  await page.goto(BASE + '/tax-invoice', { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(4000);
  await page.screenshot({ path: path.join(OUT, '03_세금계산서_초기.png'), fullPage: true });
  const taxButtons = await page.locator('button:visible').allInnerTexts();
  const taxPreview = (await page.textContent('body')).slice(0, 2000);
  findings.push({ progress: '#3', visible_buttons: taxButtons.slice(0, 30), preview: taxPreview.slice(0, 500) });
  console.log('  → 버튼 ' + taxButtons.length + '개\n');

  // === 진범 #4: 경비처리 페이지 측정 + 응답 시간 ===
  console.log('[진범 #4] /accounting/expenses 폭탄 검증');
  const t0 = Date.now();
  await page.goto(BASE + '/accounting/expenses', { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('networkidle', { timeout: 60000 }).catch(() => {});
  await page.waitForTimeout(5000);
  const t1 = Date.now();
  const rowsExp = await page.locator('table tbody tr, .mud-table-row').count();
  const lenExp = ((await page.textContent('body')) || '').length;
  await page.screenshot({ path: path.join(OUT, '04_경비처리.png'), fullPage: false });
  findings.push({ progress: '#4', load_ms: t1 - t0, rows: rowsExp, bodyLen: lenExp });
  console.log('  → 로드 ' + (t1 - t0) + 'ms, rows=' + rowsExp + ', len=' + lenExp + '\n');

  fs.writeFileSync(path.join(OUT, '_VERIFY.json'), JSON.stringify(findings, null, 2), 'utf8');
  console.log('완료 — ' + OUT);
  await browser.close();
})();
