// EVF 부하·장애 테스트 — 6대 영역
// 영역1: 부하 (동시 50사용자)
// 영역2: 장애 (DB 연결 끊김 시뮬레이션 → 타임아웃 감지)
// 영역3: 악의 (SQL인젝션, JWT위조, tenant월경)
// 영역4: 혼돈 (중복 확정, 잘못된 순서)
// 영역5: 무지 (경계값, 잘못된 입력)
// 영역6: 노후 (대용량 조회)

import autocannon from 'autocannon';

const API = 'http://localhost:5257';
const TOKEN = process.env.TOKEN;

const results = [];
function log(area, name, ok, detail) {
  const mark = ok ? 'PASS' : 'FAIL';
  console.log(`[${mark}][${area}] ${name} — ${detail}`);
  results.push({ area, name, ok, detail });
}

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
// 영역1: 부하 — 동시 50사용자, 10초
// ─────────────────────────────────────────
async function testLoad() {
  console.log('\n[영역1: 부하] 동시 50사용자 × 10초 — GET /api/stock');
  return new Promise((resolve) => {
    const instance = autocannon({
      url: `${API}/api/stock`,
      connections: 50,
      duration: 10,
      headers: { Authorization: `Bearer ${TOKEN}` },
    }, (err, result) => {
      if (err) { log('부하', '50동시 /api/stock', false, err.message); resolve(); return; }
      const p99 = result.latency.p99;
      const rps = result.requests.average;
      const errors = result.errors;
      const ok = p99 < 3000 && errors === 0;
      log('부하', '50동시 /api/stock 10초', ok,
        `RPS=${rps.toFixed(0)}, p99=${p99}ms, errors=${errors}, 2xx=${result['2xx']}`);

      console.log('\n[영역1: 부하] 동시 50사용자 × 10초 — GET /api/partners');
      const instance2 = autocannon({
        url: `${API}/api/partners`,
        connections: 50,
        duration: 10,
        headers: { Authorization: `Bearer ${TOKEN}` },
      }, (err2, result2) => {
        if (err2) { log('부하', '50동시 /api/partners', false, err2.message); resolve(); return; }
        const p99b = result2.latency.p99;
        const ok2 = p99b < 3000 && result2.errors === 0;
        log('부하', '50동시 /api/partners 10초', ok2,
          `RPS=${result2.requests.average.toFixed(0)}, p99=${p99b}ms, errors=${result2.errors}`);
        resolve();
      });
    });
  });
}

// ─────────────────────────────────────────
// 영역2: 장애 — 연결 타임아웃, 잘못된 포트
// ─────────────────────────────────────────
async function testFailure() {
  console.log('\n[영역2: 장애] 잘못된 포트 연결 시 타임아웃 감지');
  try {
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 3000);
    await fetch('http://localhost:9999/api/stock', { signal: ctrl.signal });
    clearTimeout(timer);
    log('장애', '잘못된 포트 타임아웃 감지', false, '예외 발생 안 함 — 문제');
  } catch (e) {
    const ok = e.name === 'AbortError' || e.cause?.code === 'ECONNREFUSED';
    log('장애', '잘못된 포트 타임아웃/거부 감지', ok, `${e.name}: ${e.message?.slice(0, 80)}`);
  }

  // API 서버 헬스 확인
  const h = await apiCall('GET', '/health');
  log('장애', 'API 헬스체크 응답', h.ok, `HTTP ${h.status}`);
}

// ─────────────────────────────────────────
// 영역3: 악의 — SQL인젝션, JWT위조, tenant월경
// ─────────────────────────────────────────
async function testMalicious() {
  console.log('\n[영역3: 악의] SQL인젝션 / JWT위조 / tenant월경');

  // SQL 인젝션 — 검색 파라미터
  const sqli = await apiCall('GET', `/api/partners?search=' OR '1'='1`);
  const sqliOk = sqli.status !== 500;
  log('악의', "SQL인젝션 시도 (' OR '1'='1)", sqliOk,
    `HTTP ${sqli.status} — ${sqliOk ? '500 없음(안전)' : '500 발생(취약)'}`);

  // JWT 위조 — 가짜 토큰
  const fakeJwt = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ0ZW5hbnRfaWQiOiJmYWtlLXRlbmFudCIsInJvbGUiOiJzeXN0ZW1fYWRtaW4ifQ.fakesignature';
  const jwtFake = await apiCall('GET', '/api/stock', undefined, { Authorization: `Bearer ${fakeJwt}` });
  log('악의', 'JWT 위조 토큰 차단', jwtFake.status === 401,
    `HTTP ${jwtFake.status} — ${jwtFake.status === 401 ? '차단됨(안전)' : '통과됨(취약)'}`);

  // JWT 없음 — 비인증 접근
  const noAuth = await apiCall('GET', '/api/stock', undefined, { Authorization: '' });
  log('악의', '인증 없는 접근 차단', noAuth.status === 401,
    `HTTP ${noAuth.status} — ${noAuth.status === 401 ? '차단됨(안전)' : '통과됨(취약)'}`);

  // tenant 월경 시도 — URL에 타 tenant_id 직접 삽입
  const crossTenant = await apiCall('GET', '/api/stock?tenantId=00000000-0000-0000-0000-000000000000');
  log('악의', 'URL tenant_id 월경 시도', crossTenant.status !== 500,
    `HTTP ${crossTenant.status} — ${crossTenant.status === 200 ? 'JWT tenant만 반영되면 안전' : crossTenant.status}`);

  // XSS 시도 — 상품명에 스크립트 주입
  const xss = await apiCall('POST', '/api/items', {
    itemCode: 'XSS-001', itemName: '<script>alert(1)</script>',
    itemType: 'product', unit: 'EA',
    salePrice: 100, purchasePrice: 80, stdPrice: 90, taxType: 'taxable'
  });
  const xssOk = xss.status === 400 || (xss.ok && !xss.raw.includes('<script>'));
  log('악의', 'XSS 스크립트 주입 차단', xssOk,
    `HTTP ${xss.status} — ${xss.status === 400 ? '입력검증 차단' : xss.ok ? '저장됨(출력시 이스케이프 필요)' : '오류'}`);
}

// ─────────────────────────────────────────
// 영역4: 혼돈 — 중복 확정, 없는 ID
// ─────────────────────────────────────────
async function testChaos() {
  console.log('\n[영역4: 혼돈] 중복 확정 / 없는 ID');

  // 없는 receiptId 확정 시도
  const fakeId = '00000000-0000-0000-0000-000000000000';
  const r = await apiCall('POST', `/api/purchase/receipts/${fakeId}/confirm`, {});
  log('혼돈', '없는 ID 확정 시도', r.status === 404 || r.status === 400,
    `HTTP ${r.status} — ${r.status === 404 ? '404 정상' : r.status === 400 ? '400 정상' : '예상 외 응답'}`);

  // 없는 delivery 확정
  const r2 = await apiCall('POST', `/api/sales/deliveries/${fakeId}/confirm`, {});
  log('혼돈', '없는 delivery 확정 시도', r2.status === 404 || r2.status === 400,
    `HTTP ${r2.status}`);

  // 음수 수량 매입 시도
  const r3 = await apiCall('POST', '/api/purchase/receipts', {
    poId: null, partnerId: fakeId,
    receiptDate: new Date().toISOString().slice(0, 10),
    memo: '혼돈테스트',
    items: [{ poItemId: null, itemId: fakeId, warehouseId: fakeId, qty: -5, unitPrice: 1000, supplyAmount: -5000, vatAmount: -500 }]
  });
  log('혼돈', '음수 수량 매입 차단', r3.status === 400 || r3.status === 422,
    `HTTP ${r3.status} — ${r3.raw.slice(0, 100)}`);

  // 단가 0원 매입 시도
  const r4 = await apiCall('POST', '/api/purchase/receipts', {
    poId: null, partnerId: fakeId,
    receiptDate: new Date().toISOString().slice(0, 10),
    memo: '단가0테스트',
    items: [{ poItemId: null, itemId: fakeId, warehouseId: fakeId, qty: 10, unitPrice: 0, supplyAmount: 0, vatAmount: 0 }]
  });
  log('혼돈', '단가 0원 매입 차단', r4.status === 400 || r4.status === 422,
    `HTTP ${r4.status} — ${r4.raw.slice(0, 100)}`);
}

// ─────────────────────────────────────────
// 영역5: 무지 — 경계값, 잘못된 입력
// ─────────────────────────────────────────
async function testIgnorance() {
  console.log('\n[영역5: 무지] 경계값 / 잘못된 입력');

  // 빈 body POST
  const r1 = await apiCall('POST', '/api/purchase/receipts', {});
  log('무지', '빈 body POST 차단', r1.status === 400,
    `HTTP ${r1.status}`);

  // 잘못된 날짜 형식
  const r2 = await apiCall('POST', '/api/purchase/receipts', {
    partnerId: '00000000-0000-0000-0000-000000000000',
    receiptDate: 'not-a-date',
    items: []
  });
  log('무지', '잘못된 날짜형식 차단', r2.status === 400,
    `HTTP ${r2.status}`);

  // 매우 긴 문자열
  const longStr = 'A'.repeat(10000);
  const r3 = await apiCall('POST', '/api/partners', {
    partnerName: longStr, partnerType: 'supplier',
    bizNo: '0000000000', phone: '02-0000-0000', address: '서울'
  });
  log('무지', '10000자 입력 차단', r3.status === 400 || r3.status === 413,
    `HTTP ${r3.status}`);

  // 존재하지 않는 엔드포인트
  const r4 = await apiCall('GET', '/api/nonexistent-endpoint-xyz');
  log('무지', '없는 엔드포인트 404', r4.status === 404,
    `HTTP ${r4.status}`);

  // 잘못된 UUID 형식
  const r5 = await apiCall('GET', '/api/items/not-a-valid-uuid');
  log('무지', '잘못된 UUID 형식 처리', r5.status === 400 || r5.status === 404,
    `HTTP ${r5.status}`);
}

// ─────────────────────────────────────────
// 영역6: 노후 — 대용량 페이지 조회
// ─────────────────────────────────────────
async function testAge() {
  console.log('\n[영역6: 노후] 대용량 페이지 조회 응답시간');

  const endpoints = [
    '/api/stock',
    '/api/partners',
    '/api/items',
    '/api/purchase/receipts',
    '/api/sales/deliveries',
    '/api/sales/tax-invoices',
  ];

  for (const ep of endpoints) {
    const start = Date.now();
    const r = await apiCall('GET', ep);
    const ms = Date.now() - start;
    const ok = r.ok && ms < 2000;
    log('노후', `${ep} 응답시간`, ok,
      `HTTP ${r.status}, ${ms}ms — ${ok ? '정상' : ms >= 2000 ? '2초 초과(느림)' : '오류'}`);
  }
}

// ─────────────────────────────────────────
// 실행
// ─────────────────────────────────────────
(async () => {
  if (!TOKEN) { console.error('TOKEN 환경변수 필요'); process.exit(2); }
  console.log('=== EVF 부하·장애 테스트 (6대 영역) ===\n');

  await testLoad();
  await testFailure();
  await testMalicious();
  await testChaos();
  await testIgnorance();
  await testAge();

  const failed = results.filter(r => !r.ok);
  const passed = results.length - failed.length;
  console.log(`\n${'='.repeat(50)}`);
  console.log(`EVF 최종: ${passed}/${results.length} PASS`);

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
    console.log('\n=== FAIL 항목 ===');
    failed.forEach(f => console.log(`  [${f.area}] ${f.name}: ${f.detail}`));
  }
  process.exit(failed.length === 0 ? 0 : 1);
})();
