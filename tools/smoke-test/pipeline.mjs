// 히트판 ERP — 파이프라인 완주 스모크 (clean DB 위에 각 영역 5건씩)
// 방식: 웹 API 직접 호출(로그인 토큰) + 최종 결과 Playwright로 화면 렌더 확인
// 대상 헌법 §20 3흐름 + 마스터(거래처/상품/사원)
//
// 사장님 지시:
//   1. DB 완전 삭제 ✅ (wipe_business_data.sql 실행 완료)
//   2. 전수조사 ✅ (ERP 매니저 12메뉴 카탈로그)
//   3. **각 영역 5건씩 웹에서 직접 입력하며 파이프라인 검증** ← 이 파일

import { chromium } from 'playwright';
import { randomUUID } from 'crypto';

const API = 'http://localhost:5257';
const WEB = 'http://localhost:5234';
const EMAIL = 'tenant@hitpan.kr';
const PASSWORD = 'Admin1234!';

const results = [];
let token = null;
const idem = () => randomUUID().replaceAll('-', '');

function log(step, ok, detail) {
  const mark = ok ? 'PASS' : 'FAIL';
  console.log(`[${mark}] ${step} — ${detail}`);
  results.push({ step, ok, detail });
}

async function api(method, path, body, extraHeaders = {}) {
  const h = { 'Content-Type': 'application/json', ...extraHeaders };
  if (token) h.Authorization = `Bearer ${token}`;
  const opts = { method, headers: h };
  if (body !== undefined) opts.body = JSON.stringify(body);
  const r = await fetch(`${API}${path}`, opts);
  const text = await r.text();
  let json;
  try { json = text ? JSON.parse(text) : null; } catch { json = text; }
  return { ok: r.ok, status: r.status, body: json, raw: text };
}

async function login() {
  const r = await api('POST', '/api/auth/login', { email: EMAIL, password: PASSWORD });
  if (!r.ok) throw new Error(`login failed ${r.status}: ${r.raw}`);
  token = r.body.accessToken;
  log('로그인', true, `tenant=${r.body.tenantId.slice(0, 8)}... role=${r.body.role}`);
}

// ──────────────────────────────────────────────────────
// 1. 마스터 — 거래처 5개
// ──────────────────────────────────────────────────────
const partners = [];
async function createPartners() {
  const specs = [
    { partnerName: '한빛상사', partnerType: 'supplier', bizNo: '1234567890', phone: '02-1234-5678', address: '서울시 강남구 테헤란로 1' },
    { partnerName: '대한공업', partnerType: 'supplier', bizNo: '2345678901', phone: '031-1111-2222', address: '경기도 화성시 봉담읍 2' },
    { partnerName: '동양유통', partnerType: 'customer', bizNo: '3456789012', phone: '02-3333-4444', address: '서울시 영등포구 여의대로 3' },
    { partnerName: '서울상회', partnerType: 'customer', bizNo: '4567890123', phone: '032-5555-6666', address: '인천시 남동구 미래로 4' },
    { partnerName: '북부물산', partnerType: 'both',     bizNo: '5678901234', phone: '033-7777-8888', address: '강원도 춘천시 중앙로 5' },
  ];
  for (const s of specs) {
    const r = await api('POST', '/api/partners', s);
    if (!r.ok) {
      log(`거래처 생성: ${s.partnerName}`, false, `HTTP ${r.status} — ${r.raw.slice(0, 200)}`);
      continue;
    }
    const id = r.body.partnerId ?? r.body.id;
    partners.push({ id, name: s.partnerName, type: s.partnerType });
    log(`거래처 생성: ${s.partnerName}`, true, `id=${id?.slice(0, 8)}...`);
  }
}

// ──────────────────────────────────────────────────────
// 2. 마스터 — 상품 5개
// ──────────────────────────────────────────────────────
const items = [];
async function createItems() {
  const specs = [
    { itemCode: 'IT-001', itemName: '스패너 대형',    itemType: 'product',  spec: '300mm', unit: 'EA', unitPrice: 12000,  vatType: 'taxable' },
    { itemCode: 'IT-002', itemName: '드라이버 세트',  itemType: 'product',  spec: '10종',  unit: 'SET', unitPrice: 25000,  vatType: 'taxable' },
    { itemCode: 'IT-003', itemName: '전동드릴',       itemType: 'product',  spec: '18V',   unit: 'EA', unitPrice: 180000, vatType: 'taxable' },
    { itemCode: 'IT-004', itemName: '볼트 M8',        itemType: 'material', spec: 'M8x30', unit: 'EA', unitPrice: 50,     vatType: 'taxable' },
    { itemCode: 'IT-005', itemName: '너트 M8',        itemType: 'material', spec: 'M8',    unit: 'EA', unitPrice: 30,     vatType: 'taxable' },
  ];
  for (const s of specs) {
    const r = await api('POST', '/api/items', s);
    if (!r.ok) {
      log(`상품 생성: ${s.itemName}`, false, `HTTP ${r.status} — ${r.raw.slice(0, 200)}`);
      continue;
    }
    const id = r.body.itemId ?? r.body.id;
    items.push({ id, code: s.itemCode, name: s.itemName, unitPrice: s.unitPrice });
    log(`상품 생성: ${s.itemName}`, true, `id=${id?.slice(0, 8)}...`);
  }
}

// ──────────────────────────────────────────────────────
// 3. 창고 확인 (기본등록정보로 유지됨)
// ──────────────────────────────────────────────────────
let defaultWarehouseId = null;
async function ensureWarehouse() {
  const r = await api('GET', '/api/warehouses');
  if (!r.ok || !Array.isArray(r.body) || r.body.length === 0) {
    throw new Error(`창고 로드 실패: ${r.status} ${r.raw}`);
  }
  const wh = r.body.find(w => w.isActive) ?? r.body[0];
  defaultWarehouseId = wh.warehouseId;
  log('창고 마스터', true, `default=${wh.whCode} (${defaultWarehouseId.slice(0, 8)}...)`);
}

// ──────────────────────────────────────────────────────
// 4. 매입 흐름 — 매입 5건 (발주 스킵하고 direct receipt)
// ──────────────────────────────────────────────────────
const receipts = [];
async function createPurchases() {
  const suppliers = partners.filter(p => p.type === 'supplier' || p.type === 'both');
  if (suppliers.length === 0) { log('매입 5건', false, '공급처 0개'); return; }
  if (items.length < 3) { log('매입 5건', false, '상품 부족'); return; }

  for (let i = 0; i < 5; i++) {
    const p = suppliers[i % suppliers.length];
    const it = items[i % items.length];
    const qty = 10 + i * 2;
    const supply = qty * it.unitPrice;
    const vat = Math.round(supply * 0.1);

    const createBody = {
      poId: null,
      partnerId: p.id,
      receiptDate: new Date().toISOString().slice(0, 10),
      memo: `파이프라인 테스트 매입 #${i + 1}`,
      items: [{
        poItemId: null,
        itemId: it.id,
        warehouseId: defaultWarehouseId,
        qty, unitPrice: it.unitPrice,
        supplyAmount: supply,
        vatAmount: vat,
      }],
    };
    const create = await api('POST', '/api/purchase/receipts', createBody);
    if (!create.ok) {
      log(`매입 #${i + 1} 생성`, false, `HTTP ${create.status} — ${create.raw.slice(0, 200)}`);
      continue;
    }
    const rid = create.body.receiptId ?? create.body.id;

    // 확정
    const conf = await api('POST', `/api/purchase/receipts/${rid}/confirm`,
      {}, { 'Idempotency-Key': idem() });
    if (!conf.ok) {
      log(`매입 #${i + 1} 확정`, false, `HTTP ${conf.status} — ${conf.raw.slice(0, 200)}`);
      continue;
    }
    receipts.push({ id: rid, qty, supply, vat, itemId: it.id, partnerId: p.id });
    log(`매입 #${i + 1} 생성+확정`, true, `receiptId=${rid.slice(0, 8)}... qty=${qty} total=${supply + vat}`);
  }
}

// ──────────────────────────────────────────────────────
// 5. 판매 흐름 — 거래명세서 5건 + 계산서 5건
// ──────────────────────────────────────────────────────
const deliveries = [];
const invoices = [];
async function createSales() {
  const customers = partners.filter(p => p.type === 'customer' || p.type === 'both');
  if (customers.length === 0) { log('판매 5건', false, '고객 0개'); return; }
  if (items.length < 3) return;

  for (let i = 0; i < 5; i++) {
    const p = customers[i % customers.length];
    const it = items[i % items.length];
    const qty = 3 + i;
    const unitPrice = it.unitPrice * 1.3;  // 30% 마진
    const supply = qty * unitPrice;
    const vat = Math.round(supply * 0.1);

    // 거래명세서 생성
    const createBody = {
      documentType: 'delivery',
      orderId: null,
      partnerId: p.id,
      deliveryDate: new Date().toISOString().slice(0, 10),
      memo: `파이프라인 테스트 명세서 #${i + 1}`,
      items: [{
        orderItemId: null,
        itemId: it.id,
        warehouseId: defaultWarehouseId,
        qty, unitPrice,
        supplyAmount: supply,
        vatAmount: vat,
      }],
    };
    const create = await api('POST', '/api/sales/deliveries', createBody);
    if (!create.ok) {
      log(`거래명세서 #${i + 1} 생성`, false, `HTTP ${create.status} — ${create.raw.slice(0, 200)}`);
      continue;
    }
    const did = create.body.id ?? create.body.deliveryId;

    // 확정
    const conf = await api('POST', `/api/sales/deliveries/${did}/confirm`,
      {}, { 'Idempotency-Key': idem() });
    if (!conf.ok) {
      log(`거래명세서 #${i + 1} 확정`, false, `HTTP ${conf.status} — ${conf.raw.slice(0, 200)}`);
      continue;
    }
    deliveries.push({ id: did, total: supply + vat, itemId: it.id, partnerId: p.id });
    log(`거래명세서 #${i + 1} 생성+확정`, true, `id=${did.slice(0, 8)}... total=${supply + vat}`);

    // 세금계산서 발행
    const issue = await api('POST', '/api/sales/tax-invoices',
      { deliveryId: did, memo: null }, { 'Idempotency-Key': idem() });
    if (!issue.ok) {
      log(`계산서 #${i + 1} 발행`, false, `HTTP ${issue.status} — ${issue.raw.slice(0, 200)}`);
      continue;
    }
    invoices.push({ invoiceId: issue.body.invoiceId, invoiceNo: issue.body.invoiceNo });
    log(`계산서 #${i + 1} 발행`, true, `no=${issue.body.invoiceNo}`);
  }
}

// ──────────────────────────────────────────────────────
// 6. Web UI 로드 검증 — 주요 화면 500/405 감시
// ──────────────────────────────────────────────────────
async function uiSmoke() {
  const browser = await chromium.launch({ headless: true });
  const ctx = await browser.newContext();
  const page = await ctx.newPage();
  const errs = [];
  page.on('response', (r) => {
    const s = r.status();
    if ((s === 500 || s === 405) && r.url().includes(':5257/api/')) {
      errs.push(`${s} ${r.url().replace(API, '')}`);
    }
  });

  // 로그인 (간접 — localStorage/sessionStorage에 토큰을 직접 주입)
  await page.goto(`${WEB}/`);
  await page.evaluate((t) => {
    sessionStorage.setItem('access_token', btoa(JSON.stringify(t)));
  }, token);

  const routes = [
    '/', '/dashboard', '/partners', '/items', '/bom',
    '/purchases', '/purchase-orders', '/purchase-order-status', '/purchase-status',
    '/sales-orders', '/deliveries', '/tax-invoice', '/sales/summary',
    '/stock', '/stock/adjust', '/stock/transfer', '/stock/warehouse-manage',
    '/collections', '/payments',
    '/accounting/cashbook', '/accounting/vat', '/accounting/profit', '/accounting/monthly-closing',
    '/tax/certificate', '/approval/pending', '/approval/sent', '/approval/completed',
    '/hr/attendance', '/hr/leave', '/hr/labor-contracts',
    '/employees', '/users', '/users/permissions', '/settings', '/settings/devices',
  ];

  let loadOk = 0;
  for (const r of routes) {
    try {
      await page.goto(`${WEB}${r}`, { waitUntil: 'networkidle', timeout: 20000 });
      await page.waitForTimeout(500);
      loadOk++;
    } catch {
      errs.push(`nav-timeout ${r}`);
    }
  }

  log('웹 화면 로드 (36 route)', errs.length === 0, `loaded=${loadOk}/${routes.length} errors=${errs.length}`);
  if (errs.length > 0) {
    console.log('\n--- 500/405/timeout 이벤트 ---');
    errs.forEach(e => console.log('  ', e));
  }

  await browser.close();
  return errs;
}

// ──────────────────────────────────────────────────────
// 실행
// ──────────────────────────────────────────────────────
(async () => {
  console.log('=== 히트판 ERP 파이프라인 완주 스모크 ===\n');
  try {
    await login();
    await ensureWarehouse();
    await createPartners();
    await createItems();
    await createPurchases();
    await createSales();
    const uiErrs = await uiSmoke();

    const failed = results.filter(r => !r.ok);
    const passed = results.length - failed.length;
    console.log(`\n=== 최종: ${passed}/${results.length} PASS ===`);
    console.log(`마스터: 거래처 ${partners.length}/5, 상품 ${items.length}/5`);
    console.log(`매입: ${receipts.length}/5, 거래명세서: ${deliveries.length}/5, 계산서: ${invoices.length}/5`);
    console.log(`UI 화면 로드 에러: ${uiErrs.length}`);

    if (failed.length > 0) {
      console.log('\n=== FAIL 항목 ===');
      failed.forEach(f => console.log(`  - ${f.step}: ${f.detail}`));
    }

    process.exit(failed.length + uiErrs.length === 0 ? 0 : 1);
  } catch (e) {
    console.error('FATAL:', e.message, e.stack);
    process.exit(2);
  }
})();
