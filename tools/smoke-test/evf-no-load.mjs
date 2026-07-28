// EVF 영역3~6 — 부하 제외 (Rate Limiter 회피용, 요청 간 딜레이 포함)
import { randomUUID } from 'crypto';

const API = 'http://localhost:5257';
const TOKEN = process.env.TOKEN;

const results = [];
function log(area, name, ok, detail) {
  const mark = ok ? 'PASS' : 'FAIL';
  console.log(`[${mark}][${area}] ${name} — ${detail}`);
  results.push({ area, name, ok, detail });
}

const delay = (ms) => new Promise(r => setTimeout(r, ms));

async function apiCall(method, path, body, headers = {}) {
  const h = { 'Content-Type': 'application/json', Authorization: `Bearer ${TOKEN}`, ...headers };
  const opts = { method, headers: h };
  if (body !== undefined) opts.body = JSON.stringify(body);
  const r = await fetch(`${API}${path}`, opts);
  const text = await r.text();
  let json;
  try { json = text ? JSON.parse(text) : null; } catch { json = text; }
  return { ok: r.ok, status: r.status, body: json, raw: text };
}

// ─────────────────────────────────────────
// 영역3: 악의
// ─────────────────────────────────────────
async function testMalicious() {
  console.log('\n[영역3: 악의] SQL인젝션 / JWT위조 / tenant월경 / XSS');

  const sqli = await apiCall('GET', `/api/partners?search=' OR '1'='1`);
  log('악의', "SQL인젝션 시도", sqli.status !== 500,
    `HTTP ${sqli.status} — ${sqli.status !== 500 ? '500 없음(안전)' : '500 발생(취약)'}`);
  await delay(1000);

  const fakeJwt = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ0ZW5hbnRfaWQiOiJmYWtlIiwicm9sZSI6InN5c3RlbV9hZG1pbiJ9.fakesig';
  const jwtFake = await apiCall('GET', '/api/stock', undefined, { Authorization: `Bearer ${fakeJwt}` });
  log('악의', 'JWT 위조 토큰 차단', jwtFake.status === 401, `HTTP ${jwtFake.status}`);
  await delay(1000);

  const noAuth = await apiCall('GET', '/api/stock', undefined, { Authorization: '' });
  log('악의', '인증 없는 접근 차단', noAuth.status === 401, `HTTP ${noAuth.status}`);
  await delay(1000);

  const crossTenant = await apiCall('GET', '/api/stock?tenantId=00000000-0000-0000-0000-000000000000');
  log('악의', 'URL tenant_id 월경 시도', crossTenant.status !== 500,
    `HTTP ${crossTenant.status} — JWT tenant만 반영이면 안전`);
  await delay(1000);

  const xss = await apiCall('POST', '/api/items', {
    itemCode: 'XSS-T01', itemName: '<script>alert(1)</script>',
    itemType: 'product', unit: 'EA',
    salePrice: 100, purchasePrice: 80, stdPrice: 90, taxType: 'taxable'
  });
  const xssOk = xss.status === 400 || (xss.ok && !xss.raw.includes('<script>'));
  log('악의', 'XSS 스크립트 주입', xssOk,
    `HTTP ${xss.status} — ${xss.status === 400 ? '입력 차단' : xss.ok ? '저장됨(출력 이스케이프 별도 확인 필요)' : '오류'}`);
  await delay(1000);
}

// ─────────────────────────────────────────
// 영역4: 혼돈
// ─────────────────────────────────────────
async function testChaos() {
  console.log('\n[영역4: 혼돈] 없는 ID / 음수 수량 / 단가 0원');

  const fakeId = '00000000-0000-0000-0000-000000000000';

  const r1 = await apiCall('POST', `/api/purchase/receipts/${fakeId}/confirm`, {});
  log('혼돈', '없는 receipt 확정', r1.status === 404 || r1.status === 400,
    `HTTP ${r1.status}`);
  await delay(1000);

  const r2 = await apiCall('POST', `/api/sales/deliveries/${fakeId}/confirm`, {});
  log('혼돈', '없는 delivery 확정', r2.status === 404 || r2.status === 400,
    `HTTP ${r2.status}`);
  await delay(1000);

  const r3 = await apiCall('POST', '/api/purchase/receipts', {
    poId: null, partnerId: fakeId,
    receiptDate: new Date().toISOString().slice(0, 10),
    memo: '혼돈테스트',
    items: [{ poItemId: null, itemId: fakeId, warehouseId: fakeId, qty: -5, unitPrice: 1000, supplyAmount: -5000, vatAmount: -500 }]
  });
  log('혼돈', '음수 수량 매입 차단', r3.status === 400 || r3.status === 422,
    `HTTP ${r3.status} — ${r3.raw.slice(0, 100)}`);
  await delay(1000);

  const r4 = await apiCall('POST', '/api/purchase/receipts', {
    poId: null, partnerId: fakeId,
    receiptDate: new Date().toISOString().slice(0, 10),
    memo: '단가0테스트',
    items: [{ poItemId: null, itemId: fakeId, warehouseId: fakeId, qty: 10, unitPrice: 0, supplyAmount: 0, vatAmount: 0 }]
  });
  log('혼돈', '단가 0원 매입 차단', r4.status === 400 || r4.status === 422,
    `HTTP ${r4.status} — ${r4.raw.slice(0, 100)}`);
  await delay(1000);
}

// ─────────────────────────────────────────
// 영역5: 무지
// ─────────────────────────────────────────
async function testIgnorance() {
  console.log('\n[영역5: 무지] 경계값 / 잘못된 입력');

  const r1 = await apiCall('POST', '/api/purchase/receipts', {});
  log('무지', '빈 body POST', r1.status === 400, `HTTP ${r1.status}`);
  await delay(1000);

  const r2 = await apiCall('POST', '/api/purchase/receipts', {
    partnerId: '00000000-0000-0000-0000-000000000000',
    receiptDate: 'not-a-date', items: []
  });
  log('무지', '잘못된 날짜형식', r2.status === 400, `HTTP ${r2.status}`);
  await delay(1000);

  const r3 = await apiCall('POST', '/api/partners', {
    partnerName: 'A'.repeat(10000), partnerType: 'supplier',
    bizNo: '0000000000', phone: '02-0000-0000', address: '서울'
  });
  log('무지', '10000자 입력 차단', r3.status === 400 || r3.status === 413,
    `HTTP ${r3.status}`);
  await delay(1000);

  const r4 = await apiCall('GET', '/api/nonexistent-endpoint-xyz');
  log('무지', '없는 엔드포인트 404', r4.status === 404, `HTTP ${r4.status}`);
  await delay(1000);

  const r5 = await apiCall('GET', '/api/items/not-a-valid-uuid');
  log('무지', '잘못된 UUID 형식', r5.status === 400 || r5.status === 404,
    `HTTP ${r5.status}`);
  await delay(1000);
}

// ─────────────────────────────────────────
// 영역6: 노후
// ─────────────────────────────────────────
async function testAge() {
  console.log('\n[영역6: 노후] 주요 엔드포인트 응답시간');

  const endpoints = [
    '/api/stock', '/api/partners', '/api/items',
    '/api/purchase/receipts', '/api/sales/deliveries', '/api/sales/tax-invoices',
  ];

  for (const ep of endpoints) {
    const start = Date.now();
    const r = await apiCall('GET', ep);
    const ms = Date.now() - start;
    const ok = r.ok && ms < 2000;
    log('노후', `${ep} 응답시간`, ok,
      `HTTP ${r.status}, ${ms}ms — ${ok ? '정상' : ms >= 2000 ? '2초 초과' : '오류'}`);
    await delay(500);
  }
}

(async () => {
  if (!TOKEN) { console.error('TOKEN 환경변수 필요'); process.exit(2); }
  console.log('=== EVF 영역3~6 (악의/혼돈/무지/노후) ===\n');

  await testMalicious();
  await testChaos();
  await testIgnorance();
  await testAge();

  const failed = results.filter(r => !r.ok);
  console.log(`\n${'='.repeat(50)}`);
  console.log(`EVF 3~6 최종: ${results.length - failed.length}/${results.length} PASS`);

  const byArea = {};
  for (const r of results) {
    if (!byArea[r.area]) byArea[r.area] = { pass: 0, total: 0 };
    byArea[r.area].total++;
    if (r.ok) byArea[r.area].pass++;
  }
  for (const [area, s] of Object.entries(byArea)) {
    console.log(`  ${area}: ${s.pass}/${s.total}`);
  }

  if (failed.length > 0) {
    console.log('\n=== FAIL 항목 (메모) ===');
    failed.forEach(f => console.log(`  [${f.area}] ${f.name}: ${f.detail}`));
  }
  process.exit(failed.length === 0 ? 0 : 1);
})();
