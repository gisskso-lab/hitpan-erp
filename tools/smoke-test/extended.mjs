// 확장 스모크 — 카탈로그의 [파괴][현장] 고위험 경로 전수
// 매입 반품 / BOM 조립 / 수금·지급 / 재고 조정·이송 / 사원·출퇴근·연차 / 월마감
// 사장님 지시에 따라 "모든 기능 다 잘 돌아가야 함" 기준 검증.
//
// 주의: 이 스크립트는 pipeline.mjs가 먼저 실행된 이후(마스터·매입·판매 5건씩 존재하는 상태)를 가정.

import { randomUUID } from 'crypto';

const API = 'http://localhost:5257';
const EMAIL = 'tenant@hitpan.kr';
const PASSWORD = 'Admin1234!';

let token = null;
const results = [];
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
  if (!r.ok) throw new Error(`login failed ${r.status}`);
  token = r.body.accessToken;
  log('로그인', true, `role=${r.body.role}`);
}

// ──────────────────────────────────────────────────────
// A. 사원 5명 (Employee)
// ──────────────────────────────────────────────────────
const employees = [];
async function createEmployees() {
  const specs = [
    { empName: '김영업', position: '영업과장', empType: 'regular', joinDate: '2024-01-15', phone: '010-1111-0001', email: 'sales1@hitpan.kr', role: 'sales_user' },
    { empName: '이매입', position: '매입담당', empType: 'regular', joinDate: '2024-02-01', phone: '010-1111-0002', email: 'purchase1@hitpan.kr', role: 'purchase_manager' },
    { empName: '박경리', position: '경리실장', empType: 'regular', joinDate: '2024-01-01', phone: '010-1111-0003', email: 'account1@hitpan.kr', role: 'account_manager' },
    { empName: '최창고', position: '창고반장', empType: 'regular', joinDate: '2024-03-01', phone: '010-1111-0004', email: 'warehouse1@hitpan.kr', role: 'sales_user' },
    { empName: '정인사', position: '인사팀장', empType: 'regular', joinDate: '2024-01-10', phone: '010-1111-0005', email: 'hr1@hitpan.kr', role: 'hr_manager' },
  ];
  for (const s of specs) {
    const r = await api('POST', '/api/employees', s);
    if (!r.ok) {
      log(`사원 생성: ${s.empName}`, false, `HTTP ${r.status} — ${r.raw.slice(0, 200)}`);
      continue;
    }
    const id = r.body.employeeId ?? r.body.id;
    employees.push({ id, name: s.empName });
    log(`사원 생성: ${s.empName}`, true, `id=${id?.slice(0, 8)}...`);
  }
}

// ──────────────────────────────────────────────────────
// B. BOM 1개 (완제품 + 자재 2종 조립)
// ──────────────────────────────────────────────────────
async function createAndAssembleBom() {
  const itemsR = await api('GET', '/api/items');
  if (!itemsR.ok) { log('BOM 준비', false, `items 조회 실패 ${itemsR.status}`); return; }
  const it = itemsR.body;
  const product = it.find(x => x.itemName?.includes('전동드릴') || x.itemType === 'product');
  const materials = it.filter(x => x.itemType === 'material').slice(0, 2);
  if (!product || materials.length < 2) {
    log('BOM 준비', false, `product/materials 부족 (product=${!!product}, materials=${materials.length})`);
    return;
  }

  const bomCreate = await api('POST', '/api/bom', {
    productItemId: product.itemId,
    bomName: `${product.itemName} 기본 BOM`,
    isDefault: true,
    items: materials.map((m, i) => ({
      seqNo: i + 1,
      materialItemId: m.itemId,
      qty: 2,
      unit: 'EA',
      lossRate: 0,
    })),
  });
  if (!bomCreate.ok) {
    log('BOM 생성', false, `HTTP ${bomCreate.status} — ${bomCreate.raw.slice(0, 200)}`);
    return;
  }
  const bomId = bomCreate.body.id ?? bomCreate.body.bomId;
  log('BOM 생성', true, `bomId=${bomId?.slice(0, 8)}...`);

  const check = await api('POST', `/api/bom/${bomId}/check-assemble`, 1);
  log('BOM 조립 체크', check.ok, check.ok ? `canProduce=${check.body.canProduce}` : `HTTP ${check.status}`);

  const assemble = await api('POST', '/api/bom/assemble',
    { bomId, produceQty: 1, memo: '스모크 테스트 조립' },
    { 'Idempotency-Key': idem() });
  log('BOM 조립 실행', assemble.ok, assemble.ok ? `ok` : `HTTP ${assemble.status} — ${assemble.raw.slice(0, 200)}`);
}

// ──────────────────────────────────────────────────────
// C. 수금 5건
// ──────────────────────────────────────────────────────
async function createCollections() {
  const partnersR = await api('GET', '/api/partners');
  const customers = partnersR.body.filter(p => p.partnerType === 'customer' || p.partnerType === 'both').slice(0, 5);
  if (customers.length === 0) { log('수금 5건', false, '고객 0개'); return; }
  for (let i = 0; i < 5; i++) {
    const p = customers[i % customers.length];
    const r = await api('POST', '/api/collections', {
      partnerId: p.partnerId,
      collectionDate: new Date().toISOString().slice(0, 10),
      amount: 100000 + i * 50000,
      collectionMethod: ['cash', 'bank', 'card', 'cash', 'bank'][i],
      memo: `스모크 수금 #${i + 1}`,
    });
    log(`수금 #${i + 1}`, r.ok, r.ok ? `id=${(r.body.collectionId ?? r.body.id)?.slice(0, 8)}...` : `HTTP ${r.status} — ${r.raw.slice(0, 150)}`);
  }
}

// ──────────────────────────────────────────────────────
// D. 지급 5건
// ──────────────────────────────────────────────────────
async function createPayments() {
  const partnersR = await api('GET', '/api/partners');
  const suppliers = partnersR.body.filter(p => p.partnerType === 'supplier' || p.partnerType === 'both').slice(0, 5);
  if (suppliers.length === 0) { log('지급 5건', false, '공급처 0개'); return; }
  for (let i = 0; i < 5; i++) {
    const p = suppliers[i % suppliers.length];
    const r = await api('POST', '/api/payments', {
      partnerId: p.partnerId,
      paymentDate: new Date().toISOString().slice(0, 10),
      amount: 80000 + i * 30000,
      paymentMethod: ['cash', 'bank', 'card', 'cash', 'bank'][i],
      paymentType: 'purchase',
      memo: `스모크 지급 #${i + 1}`,
    });
    log(`지급 #${i + 1}`, r.ok, r.ok ? `id=${(r.body.paymentId ?? r.body.id)?.slice(0, 8)}...` : `HTTP ${r.status} — ${r.raw.slice(0, 150)}`);
  }
}

// ──────────────────────────────────────────────────────
// E. 출퇴근 (check-in/out)
// ──────────────────────────────────────────────────────
async function hrCheckInOut() {
  const ci = await api('POST', '/api/hr/check-in', { memo: '스모크 출근' });
  // 하루 1회 제약 — 같은 날 이미 체크인된 상태면 비즈니스 로직 정상 동작. 400 + "이미" 메시지면 PASS.
  const alreadyChecked = !ci.ok && ci.status === 400 && (ci.raw || '').includes('\\uC774\\uBBF8');
  const rawIncludesAlready = !ci.ok && ci.status === 400 && (ci.raw || '').includes('이미');
  const ok = ci.ok || alreadyChecked || rawIncludesAlready;
  log('출근 체크인', ok,
    ci.ok ? 'ok (신규 출근)' : (ok ? '하루 1회 제약 정상 동작 (기존 출근 존재)' : `HTTP ${ci.status} — ${ci.raw.slice(0, 150)}`));
  const co = await api('POST', '/api/hr/check-out', { memo: '스모크 퇴근' });
  log('퇴근 체크아웃', co.ok, co.ok ? 'ok' : `HTTP ${co.status} — ${co.raw.slice(0, 150)}`);
}

// ──────────────────────────────────────────────────────
// F. 연차 신청 + 승인
// ──────────────────────────────────────────────────────
async function leaveRequest() {
  if (employees.length === 0) { log('연차 신청', false, '사원 0명'); return; }
  const emp = employees[0];
  const r = await api('POST', '/api/leave-requests', {
    employeeId: emp.id,
    leaveType: 'annual',
    leaveDays: 1,
    startDate: '2026-05-10',
    endDate: '2026-05-10',
    reason: '스모크 테스트 연차',
  });
  if (!r.ok) { log('연차 신청', false, `HTTP ${r.status} — ${r.raw.slice(0, 200)}`); return; }
  const lrid = r.body.requestId ?? r.body.id;
  log('연차 신청', true, `id=${lrid?.slice(0, 8)}...`);

  const approve = await api('POST', `/api/leave-requests/${lrid}/approve`, {});
  log('연차 승인', approve.ok, approve.ok ? 'ok' : `HTTP ${approve.status} — ${approve.raw.slice(0, 200)}`);
}

// ──────────────────────────────────────────────────────
// G. 재고 실사 조정
// ──────────────────────────────────────────────────────
async function stockAdjust() {
  const balR = await api('GET', '/api/stock/balance');
  if (!balR.ok || !Array.isArray(balR.body) || balR.body.length === 0) {
    log('재고 조정', false, '잔고 데이터 없음');
    return;
  }
  const first = balR.body.find(x => Number(x.currentQty ?? x.qty ?? 0) > 0) ?? balR.body[0];
  const r = await api('POST', '/api/stock/adjust',
    {
      itemId: first.itemId,
      warehouseId: first.warehouseId,
      adjustQty: 1,
      reason: '스모크 조정 +1',
    },
    { 'Idempotency-Key': idem() });
  log('재고 조정 (+1)', r.ok, r.ok ? 'ok' : `HTTP ${r.status} — ${r.raw.slice(0, 200)}`);
}

// ──────────────────────────────────────────────────────
// H. 매입 반품 1건
// ──────────────────────────────────────────────────────
async function purchaseReturn() {
  // 반품은 POST /api/purchase/receipts/{id}/convert-to-return (detail GET 없음)
  // RateLimitMiddleware 회피를 위해 잠깐 대기
  await new Promise(r => setTimeout(r, 2000));
  const recsR = await api('GET', '/api/purchase/receipts');
  if (!recsR.ok || !Array.isArray(recsR.body) || recsR.body.length === 0) {
    log('매입 반품', false, '매입 이력 없음');
    return;
  }
  const rec = recsR.body.find(x => (x.status ?? '').toLowerCase() === 'confirmed') ?? recsR.body[0];
  const rid = rec.receiptId ?? rec.id;
  let conv = await api('POST', `/api/purchase/receipts/${rid}/convert-to-return`);
  // 429 rate limit 시 1회 재시도 (2초 대기)
  if (conv.status === 429) {
    await new Promise(r => setTimeout(r, 3000));
    conv = await api('POST', `/api/purchase/receipts/${rid}/convert-to-return`);
  }
  if (!conv.ok) {
    log('매입 반품 생성(convert-to-return)', false, `HTTP ${conv.status} — ${conv.raw.slice(0, 200)}`);
    return;
  }
  const retId = conv.body.returnId;
  log('매입 반품 생성(convert-to-return)', true, `id=${retId?.slice(0, 8)}... no=${conv.body.returnNo}`);

  const confBody = { cancelStockEffect: true };
  const conf = await api('POST', `/api/purchase/returns/${retId}/confirm`, confBody,
    { 'Idempotency-Key': idem() });
  log('매입 반품 확정', conf.ok, conf.ok ? 'ok' : `HTTP ${conf.status} — ${conf.raw.slice(0, 200)}`);
}

// ──────────────────────────────────────────────────────
// I. 월마감 상태 조회 (마감 실행까지 하면 이후 테스트 차단되므로 조회까지만)
// ──────────────────────────────────────────────────────
async function monthlyClosing() {
  const ym = new Date().toISOString().slice(0, 7);
  const r = await api('GET', `/api/monthly-closing?yearMonth=${ym}`);
  log('월마감 상태 조회', r.ok, r.ok ? `status=${r.body?.status ?? 'open'}` : `HTTP ${r.status} — ${r.raw.slice(0, 150)}`);
}

// ──────────────────────────────────────────────────────
// J. 결재 대기함 조회
// ──────────────────────────────────────────────────────
async function approvals() {
  const p = await api('GET', '/api/approval/pending');
  log('결재 대기함', p.ok, p.ok ? `count=${Array.isArray(p.body) ? p.body.length : '?'}` : `HTTP ${p.status}`);
  const s = await api('GET', '/api/approval/sent');
  log('결재 발신함', s.ok, s.ok ? `count=${Array.isArray(s.body) ? s.body.length : '?'}` : `HTTP ${s.status}`);
  const c = await api('GET', '/api/approval/completed');
  log('결재 완료함', c.ok, c.ok ? `count=${Array.isArray(c.body) ? c.body.length : '?'}` : `HTTP ${c.status}`);
}

// ──────────────────────────────────────────────────────
// K. 재무 리포트 4종
// ──────────────────────────────────────────────────────
async function financeReports() {
  const dash = await api('GET', '/api/finance/dashboard');
  log('대시보드 KPI', dash.ok, dash.ok ? `ok` : `HTTP ${dash.status}`);
  const cb = await api('GET', '/api/finance/cashbook');
  log('현금출납', cb.ok, cb.ok ? `ok` : `HTTP ${cb.status}`);
  const vat = await api('GET', '/api/finance/vat?yearMonth=' + new Date().toISOString().slice(0, 7));
  log('부가세 신고자료', vat.ok, vat.ok ? `ok` : `HTTP ${vat.status}`);
  const profit = await api('GET', '/api/finance/profit');
  log('손익 현황', profit.ok, profit.ok ? `ok` : `HTTP ${profit.status}`);
}

// ──────────────────────────────────────────────────────
// 실행
// ──────────────────────────────────────────────────────
(async () => {
  console.log('=== 확장 스모크 (카탈로그 파괴·현장 경로) ===\n');
  try {
    await login();
    await createEmployees();
    await createAndAssembleBom();
    await createCollections();
    await createPayments();
    await hrCheckInOut();
    await leaveRequest();
    await stockAdjust();
    await purchaseReturn();
    await monthlyClosing();
    await approvals();
    await financeReports();

    const failed = results.filter(r => !r.ok);
    const passed = results.length - failed.length;
    console.log(`\n=== 확장 스모크 최종: ${passed}/${results.length} PASS ===`);
    if (failed.length > 0) {
      console.log('\n=== FAIL 항목 ===');
      failed.forEach(f => console.log(`  - ${f.step}: ${f.detail}`));
    }
    process.exit(failed.length === 0 ? 0 : 1);
  } catch (e) {
    console.error('FATAL:', e.message, e.stack);
    process.exit(2);
  }
})();
