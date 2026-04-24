// 히트판 ERP 스모크 테스트 — 사장님 재테스트 3건 실 호출 검증
// 1. 로그인
// 2. 매입처리 (500 안 나는지)
// 3. 거래명세서 확정 (405 안 나는지)
// 4. 계산서 발행 (오류 없이 진행되는지)

import { chromium } from 'playwright';

const WEB = 'http://localhost:5234';
const API = 'http://localhost:5257';
const EMAIL = 'tenant@hitpan.kr';
const PASSWORD = 'Admin1234!';

const results = [];
function log(step, ok, detail) {
  const mark = ok ? 'PASS' : 'FAIL';
  console.log(`[${mark}] ${step} — ${detail}`);
  results.push({ step, ok, detail });
}

// 에러 500/405 포착용 네트워크 이벤트 수집기
const netErrors = [];

async function loginViaApi() {
  const r = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: EMAIL, password: PASSWORD })
  });
  if (!r.ok) throw new Error(`login failed: ${r.status}`);
  return await r.json();
}

async function apiGet(token, path) {
  const r = await fetch(`${API}${path}`, { headers: { Authorization: `Bearer ${token}` } });
  if (!r.ok) throw new Error(`GET ${path} → ${r.status}`);
  return await r.json();
}

async function scenario1_매입처리(token, sample) {
  const body = {
    poId: null,
    partnerId: sample.partnerId,
    receiptDate: new Date().toISOString().slice(0, 10),
    memo: 'smoke-test 매입',
    items: [{
      poItemId: null,
      itemId: sample.itemId,
      warehouseId: sample.warehouseId,  // ← 진짜 warehouse_id
      qty: 1,
      unitPrice: 1000,
      supplyAmount: 1000,
      vatAmount: 100
    }]
  };
  const r = await fetch(`${API}/api/purchase/receipts`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`
    },
    body: JSON.stringify(body)
  });
  const text = await r.text();
  if (!r.ok) {
    log('매입처리', false, `HTTP ${r.status} — ${text.slice(0, 200)}`);
    return null;
  }
  const json = JSON.parse(text);
  log('매입처리', true, `receiptId=${json.receiptId ?? json.id ?? 'ok'}`);
  return json;
}

async function scenario2_거래명세서(token, sample) {
  // 1) 거래명세서 생성
  const createBody = {
    documentType: 'delivery',
    orderId: null,
    partnerId: sample.partnerId,
    deliveryDate: new Date().toISOString().slice(0, 10),
    memo: 'smoke-test 명세',
    items: [{
      orderItemId: null,
      itemId: sample.itemId,
      warehouseId: sample.warehouseId,
      qty: 1,
      unitPrice: 2000,
      supplyAmount: 2000,
      vatAmount: 200
    }]
  };
  const create = await fetch(`${API}/api/sales/deliveries`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify(createBody)
  });
  if (!create.ok) {
    const t = await create.text();
    log('거래명세서 생성', false, `HTTP ${create.status} — ${t.slice(0, 200)}`);
    return null;
  }
  const created = await create.json();
  const id = created.id ?? created.deliveryId;
  log('거래명세서 생성', true, `id=${id}`);

  // 2) 단건 confirm
  const conf = await fetch(`${API}/api/sales/deliveries/${id}/confirm`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: '{}'
  });
  if (!conf.ok) {
    const t = await conf.text();
    log('거래명세서 확정', false, `HTTP ${conf.status} — ${t.slice(0, 200)}`);
    return null;
  }
  log('거래명세서 확정', true, 'confirmed');

  // 3) bulk-confirm 라우트(프론트 호출) 존재 확인 — 새 ID는 없으니 빈 배열로 405 여부만
  const bulk = await fetch(`${API}/api/sales/deliveries/bulk-confirm`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify({ deliveryIds: [] })
  });
  if (bulk.status === 405) {
    log('bulk-confirm 라우트', false, 'HTTP 405 (여전히 엔드포인트 없음)');
  } else {
    log('bulk-confirm 라우트', true, `HTTP ${bulk.status} (405 아님)`);
  }

  return id;
}

async function scenario3_계산서(token, deliveryId) {
  if (!deliveryId) {
    log('계산서 발행', false, '선행 거래명세서 없음');
    return;
  }
  const idem = crypto.randomUUID().replaceAll('-', '');
  const r = await fetch(`${API}/api/sales/tax-invoices`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      'Idempotency-Key': idem
    },
    body: JSON.stringify({ deliveryId, memo: 'smoke' })
  });
  const text = await r.text();
  if (!r.ok) {
    log('계산서 발행', false, `HTTP ${r.status} — ${text.slice(0, 300)}`);
    return;
  }
  const json = JSON.parse(text);
  log('계산서 발행', true, `invoiceNo=${json.invoiceNo}`);
}

async function browserSmoke() {
  const browser = await chromium.launch({ headless: true });
  const ctx = await browser.newContext();
  const page = await ctx.newPage();

  page.on('response', async (resp) => {
    const url = resp.url();
    const status = resp.status();
    if ((status === 500 || status === 405) && url.includes(':5257/api/')) {
      netErrors.push({ url, status });
    }
  });

  try {
    await page.goto(`${WEB}/`, { waitUntil: 'networkidle', timeout: 30000 });
    log('Web 화면 로딩', true, `title=${await page.title()}`);
  } catch (e) {
    log('Web 화면 로딩', false, e.message);
  } finally {
    await browser.close();
  }
}

(async () => {
  try {
    await browserSmoke();

    const login = await loginViaApi();
    log('API 로그인', true, `tenant=${login.tenantId.slice(0, 8)}... role=${login.role}`);

    const partners = await apiGet(login.accessToken, '/api/partners');
    const supplier = partners.find(p => p.partnerType === 'supplier' || p.partnerType === 'both') ?? partners[0];
    const items = await apiGet(login.accessToken, '/api/items');
    const warehouses = await apiGet(login.accessToken, '/api/warehouses');

    // 재고 있는 품목+창고 조합을 우선 선택(판매 확정은 재고 필요).
    let stockRows = [];
    try {
      stockRows = await apiGet(login.accessToken, '/api/stock/balance');
    } catch { /* 엔드포인트 네이밍 차이에 대비, 없으면 fallback */ }
    let itemId, warehouseId, itemName, whCode;
    if (Array.isArray(stockRows) && stockRows.length > 0) {
      const first = stockRows.find(x => Number(x.currentQty ?? x.qty ?? 0) > 5) ?? stockRows[0];
      itemId = first.itemId;
      warehouseId = first.warehouseId;
      itemName = items.find(i => i.itemId === itemId)?.itemName ?? itemId;
      whCode = warehouses.find(w => w.warehouseId === warehouseId)?.whCode ?? warehouseId;
    } else {
      // fallback — 그냥 첫 품목/첫 창고
      itemId = items[0].itemId;
      const wh = warehouses.find(w => w.isActive) ?? warehouses[0];
      warehouseId = wh.warehouseId;
      itemName = items[0].itemName;
      whCode = wh.whCode;
    }
    const sample = { partnerId: supplier.partnerId, itemId, warehouseId };
    log('샘플 데이터', true, `partner=${supplier.partnerName} item=${itemName} wh=${whCode}`);

    await scenario1_매입처리(login.accessToken, sample);
    const delId = await scenario2_거래명세서(login.accessToken, sample);
    await scenario3_계산서(login.accessToken, delId);

    console.log('\n=== 500/405 네트워크 이벤트 ===');
    console.log(netErrors.length === 0 ? '없음 ✅' : JSON.stringify(netErrors, null, 2));

    const failed = results.filter(r => !r.ok);
    console.log(`\n=== 최종: ${results.length - failed.length}/${results.length} PASS ===`);
    process.exit(failed.length === 0 ? 0 : 1);
  } catch (e) {
    console.error('FATAL:', e.message);
    process.exit(2);
  }
})();
