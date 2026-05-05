// E2E 파이프라인 테스트 — admin@hitpan.kr 계정 사용
import { chromium } from 'playwright';
import { randomUUID } from 'crypto';

const API = 'http://localhost:5257';
const WEB = 'http://localhost:5234';
const EMAIL = 'admin@hitpan.kr';
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
  log('로그인', true, `role=${r.body.role}`);
}

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
    if (!r.ok) { log(`거래처 생성: ${s.partnerName}`, false, `HTTP ${r.status} — ${r.raw.slice(0, 200)}`); continue; }
    const id = r.body.partnerId ?? r.body.id;
    partners.push({ id, name: s.partnerName, type: s.partnerType });
    log(`거래처 생성: ${s.partnerName}`, true, `id=${id?.slice(0, 8)}...`);
  }
}

const items = [];
async function createItems() {
  const specs = [
    { itemCode: 'IT-P01', itemName: '스패너 대형',   itemType: 'product',  spec: '300mm', unit: 'EA',  salePrice: 12000,  purchasePrice: 9000,  stdPrice: 10000, taxType: 'taxable' },
    { itemCode: 'IT-P02', itemName: '드라이버 세트', itemType: 'product',  spec: '10종',  unit: 'SET', salePrice: 25000,  purchasePrice: 18000, stdPrice: 20000, taxType: 'taxable' },
    { itemCode: 'IT-P03', itemName: '전동드릴',      itemType: 'product',  spec: '18V',   unit: 'EA',  salePrice: 180000, purchasePrice: 130000,stdPrice: 150000,taxType: 'taxable' },
    { itemCode: 'IT-M01', itemName: '볼트 M8',       itemType: 'material', spec: 'M8x30', unit: 'EA',  salePrice: 50,     purchasePrice: 35,    stdPrice: 40,    taxType: 'taxable' },
    { itemCode: 'IT-M02', itemName: '너트 M8',       itemType: 'material', spec: 'M8',    unit: 'EA',  salePrice: 30,     purchasePrice: 20,    stdPrice: 25,    taxType: 'taxable' },
  ];
  for (const s of specs) {
    const r = await api('POST', '/api/items', s);
    if (!r.ok) { log(`상품 생성: ${s.itemName}`, false, `HTTP ${r.status} — ${r.raw.slice(0, 200)}`); continue; }
    const id = r.body.itemId ?? r.body.id;
    items.push({ id, code: s.itemCode, name: s.itemName, salePrice: s.salePrice, purchasePrice: s.purchasePrice });
    log(`상품 생성: ${s.itemName}`, true, `id=${id?.slice(0, 8)}...`);
  }
}

let defaultWarehouseId = null;
async function ensureWarehouse() {
  const r = await api('GET', '/api/warehouses');
  if (!r.ok || !Array.isArray(r.body) || r.body.length === 0) throw new Error(`창고 실패: ${r.status} ${r.raw}`);
  const wh = r.body.find(w => w.isActive) ?? r.body[0];
  defaultWarehouseId = wh.warehouseId;
  log('창고 마스터', true, `${wh.whCode} (${defaultWarehouseId.slice(0, 8)}...)`);
}

const receipts = [];
async function createPurchases() {
  const suppliers = partners.filter(p => p.type === 'supplier' || p.type === 'both');
  if (!suppliers.length || items.length < 3) { log('매입 5건', false, '데이터 부족'); return; }
  for (let i = 0; i < 5; i++) {
    const p = suppliers[i % suppliers.length];
    const it = items[i % items.length];
    const qty = 10 + i * 2;
    const supply = qty * it.purchasePrice;
    const vat = Math.round(supply * 0.1);
    const createBody = {
      poId: null, partnerId: p.id,
      receiptDate: new Date().toISOString().slice(0, 10),
      memo: `E2E 테스트 매입 #${i + 1}`,
      items: [{ poItemId: null, itemId: it.id, warehouseId: defaultWarehouseId, qty, unitPrice: it.purchasePrice, supplyAmount: supply, vatAmount: vat }]
    };
    const create = await api('POST', '/api/purchase/receipts', createBody);
    if (!create.ok) { log(`매입 #${i + 1} 생성`, false, `${create.status} ${create.raw.slice(0, 200)}`); continue; }
    const rid = create.body.receiptId ?? create.body.id;
    const conf = await api('POST', `/api/purchase/receipts/${rid}/confirm`, {}, { 'Idempotency-Key': idem() });
    if (!conf.ok) { log(`매입 #${i + 1} 확정`, false, `${conf.status} ${conf.raw.slice(0, 200)}`); continue; }
    receipts.push({ id: rid, qty, itemId: it.id });
    log(`매입 #${i + 1} 생성+확정`, true, `rid=${rid.slice(0, 8)}... qty=${qty}`);
  }
}

const deliveries = [];
const invoices = [];
async function createSales() {
  const customers = partners.filter(p => p.type === 'customer' || p.type === 'both');
  if (!customers.length || items.length < 3) { log('판매 5건', false, '데이터 부족'); return; }
  for (let i = 0; i < 5; i++) {
    const p = customers[i % customers.length];
    const it = items[i % items.length];
    const qty = 3 + i;
    const unitPrice = it.salePrice;
    const supply = qty * unitPrice;
    const vat = Math.round(supply * 0.1);
    const createBody = {
      documentType: 'delivery', orderId: null, partnerId: p.id,
      deliveryDate: new Date().toISOString().slice(0, 10),
      memo: `E2E 테스트 명세서 #${i + 1}`,
      items: [{ orderItemId: null, itemId: it.id, warehouseId: defaultWarehouseId, qty, unitPrice, supplyAmount: supply, vatAmount: vat }]
    };
    const create = await api('POST', '/api/sales/deliveries', createBody);
    if (!create.ok) { log(`거래명세서 #${i + 1} 생성`, false, `${create.status} ${create.raw.slice(0, 200)}`); continue; }
    const did = create.body.id ?? create.body.deliveryId;
    const conf = await api('POST', `/api/sales/deliveries/${did}/confirm`, {}, { 'Idempotency-Key': idem() });
    if (!conf.ok) { log(`거래명세서 #${i + 1} 확정`, false, `${conf.status} ${conf.raw.slice(0, 200)}`); continue; }
    deliveries.push({ id: did });
    log(`거래명세서 #${i + 1} 생성+확정`, true, `did=${did.slice(0, 8)}...`);
    const issue = await api('POST', '/api/sales/tax-invoices', { deliveryId: did, memo: null }, { 'Idempotency-Key': idem() });
    if (!issue.ok) { log(`계산서 #${i + 1} 발행`, false, `${issue.status} ${issue.raw.slice(0, 200)}`); continue; }
    invoices.push({ invoiceId: issue.body.invoiceId });
    log(`계산서 #${i + 1} 발행`, true, `no=${issue.body.invoiceNo}`);
  }
}

async function uiSmoke() {
  const browser = await chromium.launch({ headless: true });
  const ctx = await browser.newContext();
  const page = await ctx.newPage();
  const netErrs = [];
  page.on('response', (r) => {
    const s = r.status();
    if ((s === 500 || s === 405) && r.url().includes(':5257/api/')) netErrs.push(`${s} ${r.url()}`);
  });

  await page.goto(`${WEB}/login`, { waitUntil: 'networkidle', timeout: 30000 });
  try {
    await page.getByLabel(/이메일|email/i).fill(EMAIL);
    await page.getByLabel(/비밀번호|password/i).fill(PASSWORD);
    await page.getByRole('button', { name: /로그인/ }).click();
    await page.waitForURL(u => !u.pathname.includes('login'), { timeout: 15000 });
    log('UI 로그인', true, page.url());
  } catch (e) {
    log('UI 로그인', false, e.message);
    await browser.close();
    return netErrs;
  }

  const routes = [
    '/dashboard', '/partners', '/items',
    '/purchases', '/deliveries', '/tax-invoice', '/stock'
  ];
  let loaded = 0;
  for (const r of routes) {
    try {
      await page.goto(`${WEB}${r}`, { waitUntil: 'networkidle', timeout: 20000 });
      await page.waitForTimeout(1000);
      loaded++;
    } catch { netErrs.push(`nav-timeout ${r}`); }
  }
  log(`UI 7개 화면 로드`, netErrs.length === 0, `loaded=${loaded}/${routes.length}`);
  if (netErrs.length > 0) {
    console.log('\n--- 500/405/timeout ---');
    netErrs.forEach(e => console.log('  ', e));
  }
  await browser.close();
  return netErrs;
}

(async () => {
  console.log('=== 히트판 ERP E2E 파이프라인 테스트 ===\n');
  try {
    await login();
    await ensureWarehouse();
    await createPartners();
    await createItems();
    await createPurchases();
    await createSales();
    const uiErrs = await uiSmoke();

    const failed = results.filter(r => !r.ok);
    console.log(`\n=== 최종: ${results.length - failed.length}/${results.length} PASS ===`);
    console.log(`거래처: ${partners.length}/5, 상품: ${items.length}/5`);
    console.log(`매입: ${receipts.length}/5, 거래명세서: ${deliveries.length}/5, 계산서: ${invoices.length}/5`);
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
