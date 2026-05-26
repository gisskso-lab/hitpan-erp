// 단일 페이지 정밀 진단 — 모든 fetch 캡처
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const TARGET = process.argv[2] || '/accounting/accounts';
const BASE = 'https://demo.hitpan.kr';
const EMAIL = 'admin@hitpan.kr';
const PW = 'Admin1234!';

(async () => {
  const browser = await chromium.launch({ headless: false });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  const fetches = [];
  const errors = [];
  page.on('requestfinished', async r => {
    if (!r.url().includes('/api/')) return;
    const resp = await r.response();
    const status = resp ? resp.status() : 'no-resp';
    const ct = resp ? (resp.headers()['content-type'] || '') : '';
    fetches.push({ method: r.method(), url: r.url(), status, ct: ct.slice(0, 30) });
  });
  page.on('requestfailed', r => {
    if (!r.url().includes('/api/')) return;
    fetches.push({ method: r.method(), url: r.url(), status: 'FAILED', failure: r.failure()?.errorText });
  });
  page.on('console', m => {
    if (m.type() === 'error') errors.push(m.text().slice(0, 300));
  });

  // 로그인
  await page.goto(BASE, { waitUntil: 'networkidle' });
  await page.locator('input[type="email"]').first().click();
  await page.locator('input[type="email"]').first().pressSequentially(EMAIL, { delay: 20 });
  await page.locator('input[type="password"]').first().click();
  await page.locator('input[type="password"]').first().pressSequentially(PW, { delay: 20 });
  await page.waitForTimeout(500);
  await page.locator('button:has-text("로그인")').click();
  await page.waitForURL(u => !u.toString().includes('/login'), { timeout: 20000 });
  await page.waitForLoadState('networkidle');

  fetches.length = 0; // 초기화
  errors.length = 0;
  console.log(`\n=== ${TARGET} 진단 ===`);
  await page.goto(BASE + TARGET, { waitUntil: 'networkidle' });
  await page.waitForTimeout(3000);

  console.log('\n[fetch 목록]');
  fetches.forEach(f => {
    console.log(`  ${f.status} ${f.method} ${f.url} ${f.ct || ''} ${f.failure || ''}`);
  });
  console.log('\n[console errors]');
  errors.forEach(e => console.log('  ' + e));

  await page.screenshot({ path: path.join(__dirname, 'screenshots', '2026-05-26', 'PROBE_' + TARGET.replace(/\//g, '_') + '.png'), fullPage: true });
  await browser.close();
})();
