// PM 1차 전수조사 — demo.hitpan.kr 화면 기반 (DB 쿼리 0회)
// 실행: node scripts/playwright-audit/audit-demo.js

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const BASE_URL = 'https://demo.hitpan.kr';
const EMAIL = 'admin@hitpan.kr';
const PASSWORD = 'Admin1234!';
const OUT_DIR = path.join(__dirname, 'screenshots', new Date().toISOString().slice(0, 10));

// 사이드바 메뉴 순차 (1단계 → 6단계, 헌법 6단계 워크플로우 정합)
// 실제 Sidebar.razor에서 추출한 라우트 (PM 42번째 박제 후 정정)
const MENUS = [
  // 계정관리
  { name: '01_회사정보', path: '/company' },
  { name: '02_직원계정', path: '/users' },
  { name: '03_권한설정', path: '/users/permissions' },
  { name: '04_결재설정', path: '/settings/approval' },
  { name: '05_결재라인', path: '/settings/approval-lines' },
  { name: '06_직급관리', path: '/settings/positions' },
  { name: '07_등록기기', path: '/settings/devices' },
  { name: '08_사용환경', path: '/settings' },
  // 그룹웨어
  { name: '09_결재대기', path: '/approval/pending' },
  { name: '10_결재완료', path: '/approval/completed' },
  { name: '11_사원관리', path: '/employees' },
  { name: '12_HR직원', path: '/hr/employees' },
  { name: '13_근태', path: '/hr/attendance' },
  { name: '14_휴가연차', path: '/hr/leave' },
  // 업체관리
  { name: '15_업체마스터', path: '/partners' },
  { name: '16_업체특별단가', path: '/partners/special-prices' },
  { name: '17_업체별원장', path: '/partners/ledger' },
  // 상품관리
  { name: '18_상품마스터', path: '/items' },
  { name: '19_BOM', path: '/bom' },
  { name: '20_상품특별단가', path: '/items/special-prices' },
  { name: '21_상품별원장', path: '/items/ledger' },
  // 판매·매입 현황
  { name: '22_견적현황', path: '/quotation-status' },
  { name: '23_수주현황', path: '/sales-order-status' },
  { name: '24_판매현황', path: '/sales/summary' },
  { name: '25_판매순위', path: '/sales/ranking' },
  { name: '26_발주현황', path: '/purchase-order-status' },
  { name: '27_매입현황', path: '/purchase-status' },
  { name: '28_반품현황', path: '/return-status' },
  // 계산서
  { name: '29_세금계산서발행', path: '/tax-invoice' },
  { name: '30_세금계산서통계', path: '/tax-invoice-stats' },
  // 재고
  { name: '31_재고현황', path: '/stock' },
  { name: '32_수불부', path: '/stock/ledger' },
  { name: '33_재고실사', path: '/stock/adjust' },
  { name: '34_재고이송', path: '/stock/transfer' },
  { name: '35_창고관리', path: '/stock/warehouse-manage' },
  // 회계
  { name: '36_수금', path: '/collections' },
  { name: '37_지급', path: '/payments' },
  { name: '38_현금출납장', path: '/accounting/cashbook' },
  { name: '39_매입매출장', path: '/accounting/purchase-sales' },
  { name: '40_부가세', path: '/accounting/vat' },
  { name: '41_경비처리', path: '/accounting/expenses' },
  { name: '42_손익현황', path: '/accounting/profit' },
  { name: '43_어음관리', path: '/accounting/bills' },
  { name: '44_은행거래', path: '/accounting/bank-transactions' },
  { name: '45_계정과목', path: '/accounting/accounts' },
  { name: '46_월마감', path: '/accounting/monthly-closing' },
  // 자료관리
  { name: '47_자료백업', path: '/data/backup' },
  { name: '48_자료이관', path: '/settings/mdb-migration' },
];

(async () => {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const report = [];
  const browser = await chromium.launch({ headless: false });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();

  // 콘솔 에러 + 네트워크 실패 수집
  const consoleErrors = [];
  const networkFails = [];
  page.on('console', msg => {
    if (msg.type() === 'error') consoleErrors.push({ url: page.url(), text: msg.text() });
  });
  page.on('response', resp => {
    if (resp.status() >= 400) networkFails.push({ url: resp.url(), status: resp.status() });
  });
  // 로그인 API 호출 추적
  const loginCalls = [];
  page.on('request', req => {
    if (req.url().includes('/api/auth') || req.url().includes('/login') || req.url().includes('/api/account')) {
      loginCalls.push({ method: req.method(), url: req.url(), postData: req.postData() });
    }
  });
  page.on('response', async resp => {
    if (resp.url().includes('/api/auth') || resp.url().includes('/api/account')) {
      let body = '';
      try { body = (await resp.text()).slice(0, 500); } catch {}
      loginCalls.push({ direction: 'response', status: resp.status(), url: resp.url(), body });
    }
  });

  // 1) 로그인
  console.log('[1] 로그인 시도...');
  await page.goto(BASE_URL, { waitUntil: 'networkidle', timeout: 60000 });
  await page.screenshot({ path: path.join(OUT_DIR, '00_랜딩.png'), fullPage: true });

  try {
    // MudBlazor: focus + type (키보드 이벤트로 oninput 트리거 → 폼 검증 통과)
    const emailInput = page.locator('input[type="email"]').first();
    await emailInput.click();
    await emailInput.pressSequentially(EMAIL, { delay: 20 });
    const pwInput = page.locator('input[type="password"]').first();
    await pwInput.click();
    await pwInput.pressSequentially(PASSWORD, { delay: 20 });
    await page.waitForTimeout(1000);
    await page.screenshot({ path: path.join(OUT_DIR, '00b_로그인폼.png'), fullPage: true });
    // 활성화된 로그인 버튼 클릭
    await page.locator('button:has-text("로그인")').click();
    // URL 변경 대기 (login 페이지 벗어남)
    try {
      await page.waitForURL(url => !url.toString().includes('/login'), { timeout: 20000 });
    } catch {
      console.log('[1] URL 변경 안됨 — 로그인 응답 대기');
    }
    await page.waitForLoadState('networkidle', { timeout: 30000 });
    await page.screenshot({ path: path.join(OUT_DIR, '00c_로그인후.png'), fullPage: true });
    console.log('[1] 로그인 완료 → ' + page.url());
    console.log('[1] 로그인 API 호출:');
    loginCalls.forEach(c => console.log('   ', JSON.stringify(c).slice(0, 300)));
    fs.writeFileSync(path.join(OUT_DIR, '_LOGIN_CALLS.json'), JSON.stringify(loginCalls, null, 2), 'utf8');
  } catch (e) {
    console.log('[1] 로그인 실패: ' + e.message);
    report.push({ step: '로그인', status: 'FAIL', error: e.message });
  }

  // 2) 메뉴 순차 방문
  for (const menu of MENUS) {
    console.log(`[*] ${menu.name} → ${menu.path}`);
    const entry = { menu: menu.name, path: menu.path, errors: [], network: [] };
    try {
      const beforeErrCount = consoleErrors.length;
      const beforeNetCount = networkFails.length;

      await page.goto(BASE_URL + menu.path, { waitUntil: 'domcontentloaded', timeout: 30000 });
      await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => {});
      await page.waitForTimeout(4000); // Blazor + API fetch 완료 대기 (abort 방지)
      await page.screenshot({ path: path.join(OUT_DIR, `${menu.name}.png`), fullPage: true });

      // 본문 텍스트 길이 측정 (빈 화면 감지)
      const bodyText = (await page.textContent('body')) || '';
      entry.bodyTextLength = bodyText.length;
      entry.title = await page.title();

      // 표/행 개수
      const rowCount = await page.locator('table tbody tr, .mud-table-row').count();
      entry.rowCount = rowCount;

      // 새로 쌓인 에러
      entry.errors = consoleErrors.slice(beforeErrCount);
      entry.network = networkFails.slice(beforeNetCount);
      entry.status = (entry.errors.length === 0 && entry.network.length === 0) ? 'OK' : 'ISSUE';
    } catch (e) {
      entry.status = 'FAIL';
      entry.error = e.message;
    }
    report.push(entry);
  }

  // 3) 리포트 저장
  const reportPath = path.join(OUT_DIR, '_REPORT.json');
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2), 'utf8');
  console.log('\n=== 전수조사 완료 ===');
  console.log('스크린샷: ' + OUT_DIR);
  console.log('리포트: ' + reportPath);

  await browser.close();
})();
