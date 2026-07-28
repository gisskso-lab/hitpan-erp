// CRUD 5건 정밀 재진단 — 실제 페이지 코드에서 추출한 정확한 셀렉터
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const BASE = 'https://demo.hitpan.kr';
const EMAIL = 'admin@hitpan.kr';
const PW = 'Admin1234!';
const OUT = path.join(__dirname, 'screenshots', '2026-05-26-crud-v2');

const SCENARIOS = [
  {
    name: '01_업체검색',
    path: '/partners',
    actions: async (page) => {
      // Label="업체명·코드 검색"
      const input = page.locator('input').filter({ has: page.locator('xpath=ancestor::div[contains(@class,"mud-input-control")]//label[contains(text(),"검색")]') }).first();
      const alt = page.locator('label:has-text("업체명") + div input, label:has-text("검색") + div input').first();
      const findInput = (await input.count()) ? input : alt;
      if (await findInput.count() === 0) {
        // 무차별 input
        const inputs = await page.locator('input:visible').count();
        return `검색 input 못찾음 (visible inputs: ${inputs})`;
      }
      await findInput.click();
      await findInput.pressSequentially('테스트', { delay: 30 });
      await page.waitForTimeout(2000);
      const rowsAfter = await page.locator('table tbody tr, .mud-table-row').count();
      return `검색 입력 OK, 결과 rows=${rowsAfter}`;
    }
  },
  {
    name: '02_상품등록버튼',
    path: '/items',
    actions: async (page) => {
      const btn = page.locator('button:has-text("상품 등록"), button:has-text("등록")').first();
      if (await btn.count() === 0) return '등록 버튼 없음';
      const disabled = await btn.isDisabled().catch(() => null);
      await btn.click();
      await page.waitForTimeout(2000);
      const dialog = await page.locator('.mud-dialog:visible, [role="dialog"]:visible').count();
      return `버튼 disabled=${disabled} 클릭후 다이얼로그=${dialog}`;
    }
  },
  {
    name: '03_재고현황행수',
    path: '/stock',
    actions: async (page) => {
      await page.waitForTimeout(2000); // 추가 대기
      const rows = await page.locator('table tbody tr, .mud-table-row').count();
      const cards = await page.locator('.mud-card').count();
      return `rows=${rows} cards=${cards}`;
    }
  },
  {
    name: '04_수금메뉴버튼',
    path: '/collections',
    actions: async (page) => {
      const allButtons = await page.locator('button:visible').allInnerTexts();
      return `버튼들: [${allButtons.slice(0, 15).join(' | ')}]`;
    }
  },
  {
    name: '05_세금계산서버튼',
    path: '/tax-invoice',
    actions: async (page) => {
      const allButtons = await page.locator('button:visible').allInnerTexts();
      const disabled = [];
      const cnt = await page.locator('button:visible').count();
      for (let i = 0; i < Math.min(cnt, 15); i++) {
        const b = page.locator('button:visible').nth(i);
        const t = (await b.innerText()).slice(0, 30);
        const d = await b.isDisabled().catch(() => null);
        disabled.push(`${t}=${d ? 'DIS' : 'EN'}`);
      }
      return `버튼: ${disabled.join(' / ')}`;
    }
  },
];

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const browser = await chromium.launch({ headless: false });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();

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

  const report = [];
  for (const s of SCENARIOS) {
    const entry = { name: s.name, path: s.path };
    try {
      await page.goto(BASE + s.path, { waitUntil: 'domcontentloaded' });
      await page.waitForLoadState('networkidle').catch(() => {});
      await page.waitForTimeout(4000);
      entry.result = await s.actions(page);
      await page.screenshot({ path: path.join(OUT, s.name + '.png'), fullPage: true });
    } catch (e) {
      entry.error = e.message.slice(0, 300);
    }
    report.push(entry);
    console.log(`[${s.name}] ${s.path}`);
    console.log(`  → ${entry.result || entry.error}\n`);
  }

  fs.writeFileSync(path.join(OUT, '_REPORT.json'), JSON.stringify(report, null, 2), 'utf8');
  await browser.close();
})();
