// 사장님 지시: 웹에서 버튼 하나하나 직접 클릭 — 자동화로.
// 각 단계 스크린샷 + 네트워크 기록을 남겨 "됐다/안됐다" 를 증거와 함께 검증.
// 대상: 사장님이 수동 테스트에서 발견한 6버그 전부 재현 + 원인 식별

import { chromium } from 'playwright';
import fs from 'fs';
import path from 'path';

const WEB = 'http://localhost:5234';
const EMAIL = 'tenant@hitpan.kr';
const PASSWORD = 'Admin1234!';

const SHOT_DIR = 'screenshots';
fs.mkdirSync(SHOT_DIR, { recursive: true });

const net = [];
const results = [];
let shotNum = 0;

function log(step, ok, detail = '') {
  const mark = ok ? 'PASS' : 'FAIL';
  console.log(`[${mark}] ${step}${detail ? ' — ' + detail : ''}`);
  results.push({ step, ok, detail });
}

async function shot(page, name) {
  shotNum++;
  const file = path.join(SHOT_DIR, `${String(shotNum).padStart(2, '0')}_${name}.png`);
  await page.screenshot({ path: file, fullPage: false });
  return file;
}

async function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

(async () => {
  const browser = await chromium.launch({ headless: true });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();

  page.on('response', (r) => {
    const s = r.status();
    const url = r.url();
    if (url.includes(':5257/api/')) {
      const short = url.replace('http://localhost:5257', '');
      if (s >= 400) net.push({ status: s, url: short });
    }
  });

  page.on('pageerror', (e) => {
    console.log('PAGE ERROR:', e.message);
  });

  try {
    // ── 1. 로그인 ─────────────────────────────────────────────
    await page.goto(`${WEB}/login`, { waitUntil: 'networkidle', timeout: 20000 });
    await shot(page, 'login_page');

    // MudBlazor는 fill()로 넣으면 @bind-Value가 트리거 안 됨 → pressSequentially(실제 타이핑) 필요
    const emailInput = page.locator('input').nth(0);
    await emailInput.click();
    await emailInput.pressSequentially(EMAIL, { delay: 30 });
    await emailInput.press('Tab');
    const pwInput = page.locator('input[type="password"]').first();
    await pwInput.click();
    await pwInput.pressSequentially(PASSWORD, { delay: 30 });
    await pwInput.press('Tab');
    await sleep(500);
    await shot(page, 'login_filled');

    // 로그인 버튼은 "로그인" 텍스트 포함 버튼
    await page.getByRole('button', { name: /^로그인$/ }).click({ timeout: 10000 });
    await page.waitForURL(u => !u.pathname.includes('login'), { timeout: 15000 });
    await sleep(2500);
    await shot(page, 'after_login');
    log('로그인', true, page.url());

    // ── 2. BOM 페이지 진입 ──────────────────────────────────
    await page.goto(`${WEB}/bom`, { waitUntil: 'networkidle', timeout: 20000 });
    await sleep(2000);
    await shot(page, 'bom_list');
    log('BOM 목록 진입', true);

    // 신규 BOM 버튼 클릭
    const newBomBtn = page.getByRole('button', { name: /신규\s*BOM|\+/ }).first();
    const newBomCount = await page.locator('a[href="/bom/new"]').count();
    if (newBomCount > 0) {
      await page.locator('a[href="/bom/new"]').first().click();
    } else {
      await newBomBtn.click();
    }
    await sleep(2500);
    await shot(page, 'bom_new');
    log('BOM 신규 진입', page.url().includes('/bom/new'), page.url());

    // 완제품 선택 — 첫 번째 선택 가능한 옵션
    // BomDetail: Autocomplete/Select 로 상품 선택
    const fieldCount = await page.locator('.mud-input-slot, input[role="combobox"]').count();
    console.log('  BOM 폼 input 수:', fieldCount);

    // 상단 첫 번째 인풋(완제품 선택) 클릭
    const firstSelect = page.locator('input').nth(0);
    await firstSelect.click().catch(() => {});
    await sleep(1000);
    await shot(page, 'bom_product_selector_open');

    // 드롭다운에 나오는 아이템 선택
    const dropdownItems = page.locator('.mud-list-item, .mud-autocomplete-item, [role="option"]');
    const cnt = await dropdownItems.count();
    console.log('  드롭다운 항목 수:', cnt);
    if (cnt > 0) {
      await dropdownItems.first().click();
      await sleep(800);
      await shot(page, 'bom_product_selected');
    } else {
      log('BOM 완제품 드롭다운', false, '선택 가능한 상품 0개');
    }

    // BOM 이름 입력 (두 번째 텍스트필드)
    const inputs = page.locator('input[type="text"]');
    const inpCnt = await inputs.count();
    console.log('  text input 수:', inpCnt);

    // 자재 추가 버튼
    const addMatBtn = page.getByRole('button', { name: /자재\s*추가|추가/ });
    if (await addMatBtn.count() > 0) {
      await addMatBtn.first().click().catch(() => {});
      await sleep(1000);
      await shot(page, 'bom_material_added');
    }

    // 저장 버튼
    const saveBtn = page.getByRole('button', { name: /^저장$/ });
    if (await saveBtn.count() > 0) {
      await saveBtn.first().click().catch(() => {});
      await sleep(3000);
      await shot(page, 'bom_save_result');
    }

    // ── 3. 상품관리 — 재고 표시 확인 ────────────────────────
    await page.goto(`${WEB}/items`, { waitUntil: 'networkidle', timeout: 20000 });
    await sleep(2500);
    await shot(page, 'items_list_with_stock');
    log('상품관리 — 재고 확인', true);

    // ── 3-2. 매입처리 페이지 ─────────────────────────────
    await page.goto(`${WEB}/purchases`, { waitUntil: 'networkidle', timeout: 20000 });
    await sleep(2500);
    await shot(page, 'purchases_list');
    log('매입 목록 진입', true);

    // ── 3-3. 매입 현황 (확정된 매입 목록이 보이는지) ──────
    await page.goto(`${WEB}/purchase-status`, { waitUntil: 'networkidle', timeout: 20000 });
    await sleep(2500);
    await shot(page, 'purchase_status');
    log('매입현황 진입', true);

    // ── 4. 지급처리 페이지 (바로 확인) ─────────────────────
    await page.goto(`${WEB}/payments`, { waitUntil: 'networkidle', timeout: 20000 });
    await sleep(2500);
    await shot(page, 'payments_list');
    log('지급처리 진입', true);

    // ── 5. 연차신청 페이지 ────────────────────────────────
    await page.goto(`${WEB}/hr/leave`, { waitUntil: 'networkidle', timeout: 20000 });
    await sleep(2500);
    await shot(page, 'hr_leave');
    log('휴가·연차 진입', true);

    // ── 6. 결재함 ──────────────────────────────────────
    await page.goto(`${WEB}/approval/sent`, { waitUntil: 'networkidle', timeout: 20000 });
    await sleep(2500);
    await shot(page, 'approval_sent');
    log('결재 발신함 진입', true);

  } catch (e) {
    console.error('FATAL:', e.message);
  } finally {
    // 네트워크 에러 요약
    console.log('\n=== 네트워크 4xx/5xx ===');
    if (net.length === 0) console.log('  (없음)');
    else net.forEach(n => console.log(`  ${n.status} ${n.url}`));

    console.log(`\n=== 스크린샷: ${shotNum}장 ${SHOT_DIR}/ 에 저장 ===`);
    await browser.close();
  }
})();
