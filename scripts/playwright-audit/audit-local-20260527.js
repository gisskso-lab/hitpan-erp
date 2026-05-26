// PM 5/27 정밀 재조사 — localhost 화면 기반 (8f80489 진범 #4 봉합 효과 포함)
// 6단계 워크플로우 순서 + 매입·판매·재고 정밀 (헌법 #20 정합)
// 실행: node scripts/playwright-audit/audit-local-20260527.js

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const WEB_URL = 'http://localhost:5234';
const API_URL = 'http://localhost:5257';
const EMAIL = 'admin@hitpan.kr';
const PASSWORD = 'Admin1234!';
const STAMP = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
const OUT_DIR = path.join(__dirname, 'screenshots', `local-${STAMP}`);

// 6단계 워크플로우 순서 (헌법 #20 정합) — Sidebar.razor 추출
const MENUS = [
  // 1단계: 설정
  { name: '01_회사정보',          path: '/company',                         step: 1 },
  { name: '02_직원계정',          path: '/users',                           step: 1 },
  { name: '03_권한설정',          path: '/users/permissions',               step: 1 },
  { name: '04_결재설정',          path: '/settings/approval',               step: 1 },
  { name: '05_결재라인',          path: '/settings/approval-lines',         step: 1 },
  { name: '06_등록기기',          path: '/settings/devices',                step: 1 },

  // 2단계: 마스터
  { name: '10_업체마스터',        path: '/partners',                        step: 2 },
  { name: '11_업체특별단가',      path: '/partners/special-prices',         step: 2 },
  { name: '12_업체별원장',        path: '/partners/ledger',                 step: 2 },
  { name: '13_상품마스터',        path: '/items',                           step: 2 },
  { name: '14_BOM',                path: '/bom',                            step: 2 },
  { name: '15_상품특별단가',      path: '/items/special-prices',            step: 2 },
  { name: '16_상품별원장',        path: '/items/ledger',                    step: 2 },

  // 3단계: 매입 (정밀 영역)
  { name: '20_발주',              path: '/purchase-orders',                 step: 3, focus: true },
  { name: '21_매입',              path: '/purchases',                       step: 3, focus: true },
  { name: '22_반품',              path: '/purchase-returns',                step: 3, focus: true },
  { name: '23_발주현황',          path: '/purchase-order-status',           step: 3 },
  { name: '24_매입현황',          path: '/purchase-status',                 step: 3 },
  { name: '25_반품현황',          path: '/return-status',                   step: 3 },

  // 4단계: 판매 (정밀 영역)
  { name: '30_견적',              path: '/quotations',                      step: 4, focus: true },
  { name: '31_수주',              path: '/sales-orders',                    step: 4, focus: true },
  { name: '32_거래명세서',        path: '/sales',                           step: 4, focus: true },
  { name: '33_세금계산서',        path: '/tax-invoice',                     step: 4, focus: true },
  { name: '34_견적현황',          path: '/quotation-status',                step: 4 },
  { name: '35_수주현황',          path: '/sales-order-status',              step: 4 },
  { name: '36_판매현황',          path: '/sales/summary',                   step: 4 },
  { name: '37_세금계산서통계',    path: '/tax-invoice-stats',               step: 4 },

  // 5단계: 재고 (정밀 영역)
  { name: '40_재고현황',          path: '/stock',                           step: 5, focus: true },
  { name: '41_수불부',            path: '/stock/ledger',                    step: 5, focus: true },
  { name: '42_재고실사',          path: '/stock/adjust',                    step: 5, focus: true },
  { name: '43_재고이송',          path: '/stock/transfer',                  step: 5 },
  { name: '44_창고관리',          path: '/stock/warehouse-manage',          step: 5 },

  // 6단계: 재무
  { name: '50_수금',              path: '/collections',                     step: 6 },
  { name: '51_지급',              path: '/payments',                        step: 6 },
  { name: '52_현금출납장',        path: '/accounting/cashbook',             step: 6 },
  { name: '53_매입매출장',        path: '/accounting/purchase-sales',       step: 6 },
  { name: '54_부가세',            path: '/accounting/vat',                  step: 6 },
  { name: '55_경비처리',          path: '/accounting/expenses',             step: 6, focus: true }, // 진범 #4 봉합 검증
  { name: '56_손익현황',          path: '/accounting/profit',               step: 6 },
  { name: '57_어음관리',          path: '/accounting/bills',                step: 6 }, // 진범 #5 재현 확인
  { name: '58_은행거래',          path: '/accounting/bank-transactions',    step: 6 },
  { name: '59_계정과목',          path: '/accounting/accounts',             step: 6 }, // 진범 #6 재현 확인
  { name: '60_월마감',            path: '/accounting/monthly-closing',      step: 6 },
];

(async () => {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const report = [];
  const browser = await chromium.launch({ headless: false });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();

  const consoleErrors = [];
  const networkFails = [];
  page.on('console', msg => {
    if (msg.type() === 'error') consoleErrors.push({ url: page.url(), text: msg.text() });
  });
  page.on('response', resp => {
    if (resp.status() >= 400) networkFails.push({ url: resp.url(), status: resp.status() });
  });

  // 로그인
  console.log('[1] 로그인 → ' + WEB_URL);
  await page.goto(WEB_URL, { waitUntil: 'networkidle', timeout: 60000 });
  await page.screenshot({ path: path.join(OUT_DIR, '00_landing.png'), fullPage: true });

  try {
    const emailInput = page.locator('input[type="email"]').first();
    await emailInput.click();
    await emailInput.pressSequentially(EMAIL, { delay: 20 });
    const pwInput = page.locator('input[type="password"]').first();
    await pwInput.click();
    await pwInput.pressSequentially(PASSWORD, { delay: 20 });
    await page.waitForTimeout(800);
    await page.locator('button:has-text("로그인")').click();
    await page.waitForURL(url => !url.toString().includes('/login'), { timeout: 20000 }).catch(() => {});
    await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => {});
    await page.screenshot({ path: path.join(OUT_DIR, '00b_after_login.png'), fullPage: true });
    const afterUrl = page.url();
    console.log('[1] 로그인 후 URL → ' + afterUrl);
    if (afterUrl.includes('/login')) {
      console.log('[1] ❌ 로그인 실패 — URL이 /login 그대로. 가도 중단.');
      report.push({ step: 'LOGIN', status: 'FAIL', error: 'still on /login after submit', url: afterUrl });
      fs.writeFileSync(path.join(OUT_DIR, '_REPORT.json'), JSON.stringify(report, null, 2), 'utf8');
      await browser.close();
      return;
    }
    console.log('[1] ✅ 로그인 정합 → 가도 진행');
  } catch (e) {
    console.log('[1] 로그인 예외: ' + e.message);
    report.push({ step: 'LOGIN', status: 'FAIL', error: e.message });
    fs.writeFileSync(path.join(OUT_DIR, '_REPORT.json'), JSON.stringify(report, null, 2), 'utf8');
    await browser.close();
    return;
  }

  // 메뉴 순차
  for (const menu of MENUS) {
    const t0 = Date.now();
    console.log(`[${menu.step}] ${menu.name} → ${menu.path}${menu.focus ? ' ★' : ''}`);
    const entry = { menu: menu.name, path: menu.path, step: menu.step, focus: !!menu.focus, errors: [], network: [] };
    try {
      const beforeErr = consoleErrors.length;
      const beforeNet = networkFails.length;

      await page.goto(WEB_URL + menu.path, { waitUntil: 'domcontentloaded', timeout: 30000 });
      await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => {});
      await page.waitForTimeout(3500);
      await page.screenshot({ path: path.join(OUT_DIR, `${menu.name}.png`), fullPage: true });

      const bodyText = (await page.textContent('body')) || '';
      entry.bodyLen = bodyText.length;
      entry.title = await page.title();
      entry.rowCount = await page.locator('table tbody tr, .mud-table-row').count();
      entry.buttonCount = await page.locator('button:visible').count();
      entry.errors = consoleErrors.slice(beforeErr);
      entry.network = networkFails.slice(beforeNet);
      entry.loadMs = Date.now() - t0;
      entry.status = (entry.errors.length === 0 && entry.network.length === 0) ? 'OK' : 'ISSUE';
    } catch (e) {
      entry.status = 'FAIL';
      entry.error = e.message;
      entry.loadMs = Date.now() - t0;
    }
    report.push(entry);
    console.log(`    → ${entry.status} (bodyLen=${entry.bodyLen} rows=${entry.rowCount} btns=${entry.buttonCount} ${entry.loadMs}ms errs=${entry.errors?.length || 0} net=${entry.network?.length || 0})`);
  }

  // 리포트
  const reportPath = path.join(OUT_DIR, '_REPORT.json');
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2), 'utf8');

  // 진범 후보 요약 (ISSUE/FAIL only)
  const issues = report.filter(r => r.status === 'ISSUE' || r.status === 'FAIL');
  const summaryPath = path.join(OUT_DIR, '_ISSUES.json');
  fs.writeFileSync(summaryPath, JSON.stringify(issues, null, 2), 'utf8');

  // 헌법 #19 정합 — 27,640행 폭탄 재발 감시
  const bigRows = report.filter(r => r.rowCount > 200);
  if (bigRows.length > 0) {
    fs.writeFileSync(path.join(OUT_DIR, '_BIG_ROWS.json'), JSON.stringify(bigRows, null, 2), 'utf8');
  }

  console.log('\n=== 정밀 재조사 완료 ===');
  console.log(`총 ${report.length}개 화면, ISSUE/FAIL ${issues.length}개, 200행 초과 ${bigRows.length}개`);
  console.log('스크린샷: ' + OUT_DIR);
  console.log('리포트: ' + reportPath);

  await browser.close();
})();
