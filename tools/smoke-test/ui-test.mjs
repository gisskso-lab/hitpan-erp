// UI 스모크 — 로그인 → 매입처리 페이지 → 네트워크에 500/405 없음 확인
import { chromium } from 'playwright';

const WEB = 'http://localhost:5234';
const errs = [];

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext();
const page = await ctx.newPage();
page.on('response', (r) => {
  const s = r.status();
  if ((s === 500 || s === 405) && r.url().includes(':5257/api/')) {
    errs.push(`${s} ${r.url()}`);
  }
});
page.on('console', (m) => {
  if (m.type() === 'error') {
    const t = m.text();
    if (t.includes('500') || t.includes('405')) errs.push(`console.error: ${t}`);
  }
});

try {
  await page.goto(`${WEB}/login`, { waitUntil: 'networkidle', timeout: 30000 });
  await page.getByLabel(/이메일|email/i).fill('tenant@hitpan.kr');
  await page.getByLabel(/비밀번호|password/i).fill('Admin1234!');
  await page.getByRole('button', { name: /로그인/ }).click();
  await page.waitForURL(u => !u.pathname.includes('login'), { timeout: 15000 });
  console.log('[PASS] 로그인 성공 — 현재 URL:', page.url());

  await page.goto(`${WEB}/purchases`, { waitUntil: 'networkidle', timeout: 30000 });
  await page.waitForTimeout(3000);
  console.log('[PASS] 매입처리 페이지 로드:', await page.title());

  await page.goto(`${WEB}/deliveries`, { waitUntil: 'networkidle', timeout: 30000 });
  await page.waitForTimeout(3000);
  console.log('[PASS] 거래명세서 페이지 로드:', await page.title());

  await page.goto(`${WEB}/tax-invoice`, { waitUntil: 'networkidle', timeout: 30000 });
  await page.waitForTimeout(3000);
  console.log('[PASS] 세금계산서 페이지 로드:', await page.title());
} catch (e) {
  console.error('FATAL', e.message);
  errs.push(`exception: ${e.message}`);
}

console.log(`\n=== 500/405 네트워크 이벤트: ${errs.length} ===`);
errs.forEach(e => console.log('  ', e));
await browser.close();
process.exit(errs.length === 0 ? 0 : 1);
