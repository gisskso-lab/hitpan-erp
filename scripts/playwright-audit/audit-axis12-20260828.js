/**
 * 전수조사 [축1 상품] + [축2 BOM] — 2026-08-28 검증팀
 *
 * 판정 규율:
 *  · HTTP 200 으로 판정하지 않는다. JSON 본문과 실제 수치로 가른다.
 *  · 재고는 반드시 작업 전/후 두 번 찍어 차이(delta)로 판정한다. 사후값 단독 판정 금지.
 *  · 확인 못 한 것은 PASS 도 FAIL 도 아닌 UNKNOWN.
 *
 * 쓰기 결재: 사장님 승인. 생성물 전부 ZZTEST-0828- 접두어.
 */
const fs = require('fs');
const path = require('path');

const BASE = 'https://test1234.hitpan.kr';
const LOGIN = { email: 'act0226', password: '11111111' };
const TAG = 'ZZTEST-0828-';

let TOK = '';
let DEV = '';
const created = [];   // 생성물 대장
const results = [];   // 판정
const timings = [];   // 응답시간

function H(extra) {
  return Object.assign({
    'Content-Type': 'application/json',
    'Authorization': 'Bearer ' + TOK,
    'X-HitPan-Device-Id': DEV
  }, extra || {});
}

async function call(method, p, body, extraHeaders) {
  const t0 = Date.now();
  const opt = { method, headers: H(extraHeaders) };
  if (body !== undefined) opt.body = JSON.stringify(body);
  const r = await fetch(BASE + p, opt);
  const ms = Date.now() - t0;
  const text = await r.text();
  let json = null;
  try { json = JSON.parse(text); } catch (e) { /* HTML fallback = 라우트 없음 */ }
  const ct = r.headers.get('content-type') || '';
  const isJson = ct.includes('json') && json !== null;
  timings.push({ method, path: p, status: r.status, ms, isJson });
  return { status: r.status, ms, json, isJson, text: text.slice(0, 400) };
}

const GET = (p) => call('GET', p);
const POST = (p, b, h) => call('POST', p, b === undefined ? {} : b, h);
const PUT = (p, b) => call('PUT', p, b);

function record(axis, id, name, verdict, evidence) {
  results.push({ axis, id, name, verdict, evidence });
  console.log('\n[' + verdict + '] ' + axis + ' ' + id + ' — ' + name);
  console.log('   근거: ' + evidence);
}

/** 특정 품목의 전 창고 합계 재고 (item_stock 실측) */
async function stockOf(itemId) {
  const r = await GET('/api/stock/balance');
  if (!r.isJson || !Array.isArray(r.json)) return null;
  const rows = r.json.filter(x => x.itemId === itemId);
  return {
    total: rows.reduce((a, b) => a + Number(b.currentQty || 0), 0),
    byWh: rows.map(x => ({ wh: x.warehouseName, qty: Number(x.currentQty) }))
  };
}

async function main() {
  // ── 로그인 + 기기 헤더 ───────────────────────────────
  const lr = await fetch(BASE + '/api/auth/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(LOGIN)
  });
  TOK = (await lr.json()).accessToken;
  if (!TOK) throw new Error('login failed');
  const dv = await (await fetch(BASE + '/api/devices', { headers: { Authorization: 'Bearer ' + TOK } })).json();
  DEV = (dv.find(d => d.isMainPc) || {}).deviceId;
  if (!DEV) throw new Error('main pc device not found');
  console.log('로그인 OK / device=' + DEV);

  // ── 사전 조사: 창고·거래처 ──────────────────────────
  const whs = (await GET('/api/warehouses')).json;
  const MAIN = whs.find(w => w.whCode === 'MAIN') || whs[0];
  const partners = (await GET('/api/partners')).json;
  const SUP = partners.find(p => p.partnerType === 'supplier');
  console.log('창고=' + MAIN.whName + ' / 공급처=' + SUP.partnerName);

  // ════════════════════════════════════════════════════
  // [축1-1] 상품등록 → 발주 → 매입확정 → 재고 상승
  //         + 헌법 #6: draft 에선 원장이 안 움직여야 한다
  // ════════════════════════════════════════════════════
  const QTY = 10, PRICE = 1000;
  let A1_itemId = null;
  {
    const ci = await POST('/api/items', {
      itemName: TAG + '상품A', itemCode: TAG + 'A', itemType: 'material', unit: 'EA',
      purchasePrice: PRICE, salePrice: 2000, standardPrice: PRICE,
      taxType: 'taxable', safetyStock: 0, autoOrderEnabled: false, autoOrderQty: 0,
      autoReceiveOnOrder: false, memo: TAG + '전수조사 축1'
    });
    A1_itemId = ci.isJson ? (typeof ci.json === 'string' ? ci.json : (ci.json.itemId || ci.json.id)) : null;
    created.push({ kind: '상품(items)', name: TAG + '상품A', id: A1_itemId, status: ci.status });
    console.log('상품A 생성: ' + ci.status + ' ' + A1_itemId + ' ' + ci.text.slice(0, 120));

    const before = await stockOf(A1_itemId);

    // 발주 (draft)
    const po = await POST('/api/purchase/orders', {
      partnerId: SUP.partnerId, poDate: new Date().toISOString(),
      memo: TAG + '축1 발주',
      items: [{ itemId: A1_itemId, orderedQty: QTY, unitPrice: PRICE, supplyAmount: QTY * PRICE, vatAmount: QTY * PRICE * 0.1, warehouseId: MAIN.warehouseId }]
    });
    const poId = po.isJson ? (po.json.id || po.json.poId) : null;
    created.push({ kind: '발주서(purchase_orders)', name: TAG + '축1 발주', id: poId, status: po.status });
    console.log('발주 생성: ' + po.status + ' ' + poId + ' ' + po.text.slice(0, 120));

    const afterPo = await stockOf(A1_itemId);

    // 매입(입고) 생성 — 아직 draft
    const rc = await POST('/api/purchase/receipts', {
      poId: poId, partnerId: SUP.partnerId, receiptDate: new Date().toISOString(),
      memo: TAG + '축1 매입',
      items: [{ itemId: A1_itemId, warehouseId: MAIN.warehouseId, qty: QTY, unitPrice: PRICE, supplyAmount: QTY * PRICE, vatAmount: QTY * PRICE * 0.1 }]
    });
    const rcId = rc.isJson ? (rc.json.id || rc.json.receiptId) : null;
    created.push({ kind: '매입(purchase_receipts)', name: TAG + '축1 매입', id: rcId, status: rc.status });
    console.log('매입 생성: ' + rc.status + ' ' + rcId + ' ' + rc.text.slice(0, 120));

    const afterDraftReceipt = await stockOf(A1_itemId);

    // 헌법 #6 판정 — 확정 전 재고 무변동이어야 한다
    record('축1', '1-A', '헌법 #6 — 확정 전(draft) 재고 무변동',
      (before && afterDraftReceipt && before.total === afterDraftReceipt.total) ? 'PASS' : 'FAIL',
      '상품등록직후=' + (before && before.total) + ' / 발주(draft)후=' + (afterPo && afterPo.total) +
      ' / 매입서생성(draft)후=' + (afterDraftReceipt && afterDraftReceipt.total) + ' (기대: 세 값 모두 동일)');

    // 확정
    const cf = await POST('/api/purchase/receipts/' + rcId + '/confirm', {}, { 'Idempotency-Key': TAG + 'cf-' + rcId });
    const afterConfirm = await stockOf(A1_itemId);
    const delta = (afterConfirm ? afterConfirm.total : 0) - (afterDraftReceipt ? afterDraftReceipt.total : 0);

    record('축1', '1-B', '매입확정 시 재고 상승 (전/후 대조)',
      (cf.status === 200 && delta === QTY) ? 'PASS' : 'FAIL',
      '확정HTTP=' + cf.status + ' / 확정직전=' + (afterDraftReceipt && afterDraftReceipt.total) +
      ' → 확정후=' + (afterConfirm && afterConfirm.total) + ' / delta=' + delta + ' (기대 +' + QTY + ')' +
      ' / 창고별=' + JSON.stringify(afterConfirm && afterConfirm.byWh) + ' / 확정응답=' + cf.text.slice(0, 120));

    // 원장(stock_ledger)에 실제로 갔나 — 만드는 쪽 말고 읽는 쪽
    const led = await POST('/api/stock/ledger', {
      fromDate: new Date(Date.now() - 86400000).toISOString(),
      toDate: new Date(Date.now() + 86400000).toISOString(), itemId: A1_itemId
    });
    const ledRows = (led.isJson && Array.isArray(led.json)) ? led.json.filter(x => x.itemId === A1_itemId) : [];
    record('축1', '1-C', '원장(stock_ledger)에 입고 행이 실제로 기록됐나',
      (led.isJson && ledRows.length > 0) ? 'PASS' : (led.isJson ? 'FAIL' : 'UNKNOWN'),
      'ledger HTTP=' + led.status + ' isJson=' + led.isJson + ' 전체행=' +
      ((led.isJson && Array.isArray(led.json)) ? led.json.length : 'n/a') +
      ' 해당품목행=' + ledRows.length + ' 샘플=' + JSON.stringify(ledRows.slice(0, 2)));
  }

  // ════════════════════════════════════════════════════
  // [축1-2] 자동발주 중복 — 조회 반복이 알림을 되살리나
  // ════════════════════════════════════════════════════
  let A2_itemId = null;
  {
    const ci = await POST('/api/items', {
      itemName: TAG + '자동발주B', itemCode: TAG + 'B', itemType: 'material', unit: 'EA',
      purchasePrice: 500, salePrice: 1000, standardPrice: 500, taxType: 'taxable',
      safetyStock: 20,                 // 재고 0 이므로 즉시 미달
      autoOrderEnabled: true, autoOrderPartnerId: SUP.partnerId, autoOrderQty: 20,
      autoReceiveOnOrder: false,       // 발주까지만
      memo: TAG + '전수조사 축1-2 자동발주'
    });
    A2_itemId = ci.isJson ? (typeof ci.json === 'string' ? ci.json : (ci.json.itemId || ci.json.id)) : null;
    created.push({ kind: '상품(items)', name: TAG + '자동발주B', id: A2_itemId, status: ci.status });

    // 발주 총건수 기준선
    const poBefore = (await GET('/api/purchase/orders')).json;
    const baseCount = Array.isArray(poBefore) ? poBefore.length : -1;

    // 알림 조회 1회 → 알림 생성 유도
    const al1 = (await GET('/api/bom/alerts')).json;
    const mine1 = al1.filter(a => a.itemId === A2_itemId);

    // 🔴 과거 사고 재현 시도: 조회를 3회 반복 → pending 이 중복 생성되나
    await GET('/api/bom/alerts'); await GET('/api/bom/alerts');
    const al2 = (await GET('/api/bom/alerts')).json;
    const mine2 = al2.filter(a => a.itemId === A2_itemId);

    record('축1', '2-A', '알림 조회 반복 시 같은 품목 알림이 증식하나 (조회 4회)',
      (mine1.length === 1 && mine2.length === 1) ? 'PASS' : 'FAIL',
      '조회1회후 해당품목 알림수=' + mine1.length + ' → 조회4회후=' + mine2.length + ' (기대: 둘 다 1)');

    // 자동발주 실행
    const alertId = mine2.length ? mine2[0].alertId : (mine1.length ? mine1[0].alertId : null);
    if (alertId) {
      const od = await POST('/api/bom/alerts/' + alertId + '/order?autoReceive=false');
      created.push({ kind: '자동발주(alert order)', name: TAG + '자동발주B 발주', id: alertId, status: od.status });
      console.log('자동발주B 결과: ' + od.status + ' ' + od.text.slice(0, 200));
    }

    const poAfter1 = (await GET('/api/purchase/orders')).json;
    const c1 = Array.isArray(poAfter1) ? poAfter1.length : -1;

    // 🔴 발주 직후 조회 3회 반복 — 'ordered' 를 pending 으로 되살리는가
    await GET('/api/bom/alerts'); await GET('/api/bom/alerts'); await GET('/api/bom/alerts');
    const al3 = (await GET('/api/bom/alerts')).json;
    const mine3 = al3.filter(a => a.itemId === A2_itemId);

    const poAfter2 = (await GET('/api/purchase/orders')).json;
    const c2 = Array.isArray(poAfter2) ? poAfter2.length : -1;

    record('축1', '2-B', '자동발주 후 조회 반복 — 알림 재삽입(유령 pending) 발생하나',
      (mine3.length === 1 && mine3[0].status === 'ordered') ? 'PASS' : 'FAIL',
      '발주후 조회4회 / 해당품목 알림수=' + mine3.length + ' 상태=' + JSON.stringify(mine3.map(x => x.status)) +
      ' (기대: 1건·ordered. pending 이 다시 생기면 FAIL)');

    record('축1', '2-C', '자동발주 중복 — 발주 건수가 조회 반복으로 늘어나나',
      (c2 === c1 && c1 === baseCount + 1) ? 'PASS' : 'FAIL',
      '발주총건수: 시작=' + baseCount + ' → 자동발주직후=' + c1 + ' → 조회3회반복후=' + c2 +
      ' (기대: 시작+1 뒤 불변)');

    // 같은 알림 재발주 시도 = 멱등 가드
    if (alertId) {
      const dup = await POST('/api/bom/alerts/' + alertId + '/order?autoReceive=false');
      const poAfter3 = (await GET('/api/purchase/orders')).json;
      const c3 = Array.isArray(poAfter3) ? poAfter3.length : -1;
      record('축1', '2-D', '같은 알림 재발주 차단(멱등)',
        (dup.status >= 400 && c3 === c2) ? 'PASS' : 'FAIL',
        '재발주 HTTP=' + dup.status + ' 응답=' + (dup.text || '').slice(0, 150) +
        ' / 발주건수 ' + c2 + '→' + c3 + ' (기대: 4xx + 건수 불변)');
    }
  }

  // ════════════════════════════════════════════════════
  // [축2-1] BOM 생산 — 자재 차감 · 제품 상승 · 배수 정확도
  // ════════════════════════════════════════════════════
  let M1 = null, M2 = null, bomId = null, prodItemId = null;
  {
    async function mkMaterial(suffix, price) {
      const ci = await POST('/api/items', {
        itemName: TAG + '자재' + suffix, itemCode: TAG + 'M' + suffix, itemType: 'material',
        unit: 'EA', purchasePrice: price, salePrice: price * 2, standardPrice: price,
        taxType: 'taxable', safetyStock: 0, autoOrderEnabled: false, autoOrderQty: 0,
        autoReceiveOnOrder: false, memo: TAG + '축2 자재'
      });
      const id = ci.isJson ? (typeof ci.json === 'string' ? ci.json : (ci.json.itemId || ci.json.id)) : null;
      created.push({ kind: '상품(items)', name: TAG + '자재' + suffix, id, status: ci.status });
      return id;
    }
    async function stockIn(itemId, qty, price, label) {
      const rc = await POST('/api/purchase/receipts', {
        partnerId: SUP.partnerId, receiptDate: new Date().toISOString(), memo: TAG + label,
        items: [{ itemId, warehouseId: MAIN.warehouseId, qty, unitPrice: price, supplyAmount: qty * price, vatAmount: qty * price * 0.1 }]
      });
      const id = rc.isJson ? (rc.json.id || rc.json.receiptId) : null;
      created.push({ kind: '매입(purchase_receipts)', name: TAG + label, id, status: rc.status });
      const cf = await POST('/api/purchase/receipts/' + id + '/confirm', {}, { 'Idempotency-Key': TAG + 'cf-' + id });
      console.log(label + ': 생성' + rc.status + ' 확정' + cf.status);
      return { id, confirm: cf.status };
    }

    M1 = await mkMaterial('X', 100);
    M2 = await mkMaterial('Y', 200);
    await stockIn(M1, 100, 100, '축2 자재X 입고');
    await stockIn(M2, 100, 200, '축2 자재Y 입고');

    // BOM 등록: 완제품 1개당 자재X 2개 + 자재Y 3개
    const cb = await POST('/api/bom', {
      productItemId: '', productItemName: TAG + '완제품P', bomName: TAG + 'BOM',
      isDefault: true, memo: TAG + '축2 BOM',
      items: [
        { seqNo: 1, materialItemId: M1, qty: 2, unit: 'EA', lossRate: 0, memo: TAG },
        { seqNo: 2, materialItemId: M2, qty: 3, unit: 'EA', lossRate: 0, memo: TAG }
      ]
    });
    bomId = cb.isJson ? (typeof cb.json === 'string' ? cb.json : (cb.json.bomId || cb.json.id)) : null;
    created.push({ kind: 'BOM(boms)', name: TAG + 'BOM', id: bomId, status: cb.status });
    console.log('BOM 생성: ' + cb.status + ' ' + bomId + ' ' + cb.text.slice(0, 200));

    const bd = await GET('/api/bom/' + bomId);
    prodItemId = bd.isJson ? bd.json.productItemId : null;
    created.push({ kind: '상품(BOM완제품 자동생성)', name: TAG + '완제품P', id: prodItemId, status: bd.status });

    // ── 생산 전/후 대조 ──
    const PRODUCE = 10;
    const beforeM1 = await stockOf(M1), beforeM2 = await stockOf(M2), beforeP = await stockOf(prodItemId);

    const chk = await POST('/api/bom/' + bomId + '/check-assemble', PRODUCE);
    console.log('check-assemble: ' + chk.status + ' ' + chk.text.slice(0, 250));

    const asm = await POST('/api/bom/assemble',
      { bomId: bomId, produceQty: PRODUCE, memo: TAG + '축2 생산' },
      { 'Idempotency-Key': TAG + 'asm-' + bomId });
    created.push({ kind: 'BOM 생산(assemble)', name: TAG + '생산 10개', id: bomId, status: asm.status });
    console.log('assemble: ' + asm.status + ' ' + asm.text.slice(0, 200));

    const afterM1 = await stockOf(M1), afterM2 = await stockOf(M2), afterP = await stockOf(prodItemId);

    const dM1 = afterM1.total - beforeM1.total;
    const dM2 = afterM2.total - beforeM2.total;
    const dP = afterP.total - (beforeP ? beforeP.total : 0);

    record('축2', '1-A', 'BOM 생산 시 자재 재고 차감',
      (dM1 === -2 * PRODUCE && dM2 === -3 * PRODUCE) ? 'PASS' : 'FAIL',
      '자재X: ' + beforeM1.total + '→' + afterM1.total + ' (delta=' + dM1 + ', 기대 -' + (2 * PRODUCE) + ')' +
      ' / 자재Y: ' + beforeM2.total + '→' + afterM2.total + ' (delta=' + dM2 + ', 기대 -' + (3 * PRODUCE) + ')' +
      ' / assemble HTTP=' + asm.status);

    record('축2', '1-B', 'BOM 생산 시 생산제품 재고 상승',
      (dP === PRODUCE) ? 'PASS' : 'FAIL',
      '완제품: ' + (beforeP ? beforeP.total : 0) + '→' + afterP.total + ' (delta=' + dP + ', 기대 +' + PRODUCE + ')');

    record('축2', '1-C', '수량 배수 정확도 (1개당 X2·Y3 → 10개 생산 시 X20·Y30)',
      (dM1 === -20 && dM2 === -30 && dP === 10) ? 'PASS' : 'FAIL',
      '실측 배수: X소요=' + (-dM1) + ' ÷ 생산' + PRODUCE + ' = ' + (-dM1 / PRODUCE) + '배 (기대 2) · ' +
      'Y소요=' + (-dM2) + ' ÷ 생산' + PRODUCE + ' = ' + (-dM2 / PRODUCE) + '배 (기대 3)');

    // 멱등 — 같은 키 재전송 시 재고 2배 방지
    const asm2 = await POST('/api/bom/assemble',
      { bomId: bomId, produceQty: PRODUCE, memo: TAG + '축2 생산' },
      { 'Idempotency-Key': TAG + 'asm-' + bomId });
    const afterDup = await stockOf(prodItemId);
    record('축2', '1-D', '생산 멱등 — 같은 Idempotency-Key 재전송 시 재고 2배 안 되나',
      (afterDup.total === afterP.total) ? 'PASS' : 'FAIL',
      '재전송 HTTP=' + asm2.status + ' / 완제품 재고 ' + afterP.total + '→' + afterDup.total + ' (기대: 불변)');
  }

  // ════════════════════════════════════════════════════
  // [축2-3] 반제품 자동사슬 차단 — 미해결 P0 현재 상태
  // ════════════════════════════════════════════════════
  {
    const ci = await POST('/api/items', {
      itemName: TAG + '반제품S', itemCode: TAG + 'S', itemType: 'semi_finished', unit: 'EA',
      purchasePrice: 700, salePrice: 1400, standardPrice: 700, taxType: 'taxable',
      safetyStock: 30, autoOrderEnabled: true, autoOrderPartnerId: SUP.partnerId,
      autoOrderQty: 30, autoReceiveOnOrder: true,      // 🔴 사슬 ON — 막히는지 본다
      memo: TAG + '축2-3 반제품 사슬검증'
    });
    const semiId = ci.isJson ? (typeof ci.json === 'string' ? ci.json : (ci.json.itemId || ci.json.id)) : null;
    created.push({ kind: '상품(items)', name: TAG + '반제품S', id: semiId, status: ci.status });

    const before = await stockOf(semiId);
    const alerts = (await GET('/api/bom/alerts')).json;
    const mine = alerts.filter(a => a.itemId === semiId);

    let od = null;
    if (mine.length) {
      od = await POST('/api/bom/alerts/' + mine[0].alertId + '/order?autoReceive=true');
      created.push({ kind: '자동발주(반제품 alert order)', name: TAG + '반제품S 발주', id: mine[0].alertId, status: od.status });
      console.log('반제품 사슬 결과: ' + od.status + ' ' + od.text.slice(0, 300));
    }

    const after = await stockOf(semiId);
    const dq = (after ? after.total : 0) - (before ? before.total : 0);
    const body = od && od.isJson ? od.json : null;

    const blocked = body && body.orderCreated === true && body.receiptConfirmed === false;
    record('축2', '3-A', '반제품 자동사슬 차단 (매입확정·회계 오염 방지)',
      (blocked && dq === 0) ? 'PASS' : (od ? 'FAIL' : 'UNKNOWN'),
      'order HTTP=' + (od && od.status) + ' / 응답본문=' + JSON.stringify(body) +
      ' / 반제품 재고 ' + (before && before.total) + '→' + (after && after.total) + ' (delta=' + dq + ')' +
      ' — 기대: orderCreated=true·receiptConfirmed=false·재고 delta 0');

    record('축2', '3-B', '차단 사유가 사람 말로 전달되나 (개발용어 금지, 헌법 #23)',
      (body && body.chainSkippedReason && !/semi_finished|item_type|null|Exception/i.test(body.chainSkippedReason)) ? 'PASS' : (body ? 'FAIL' : 'UNKNOWN'),
      'chainSkippedReason="' + (body && body.chainSkippedReason) + '"');

    // 대조군 — material 은 사슬이 실제로 돌아야 한다 (차단이 과잉인지 가른다)
    const ci2 = await POST('/api/items', {
      itemName: TAG + '사슬대조M', itemCode: TAG + 'CM', itemType: 'material', unit: 'EA',
      purchasePrice: 600, salePrice: 1200, standardPrice: 600, taxType: 'taxable',
      safetyStock: 40, autoOrderEnabled: true, autoOrderPartnerId: SUP.partnerId,
      autoOrderQty: 40, autoReceiveOnOrder: true, memo: TAG + '축2-3 대조군'
    });
    const ctlId = ci2.isJson ? (typeof ci2.json === 'string' ? ci2.json : (ci2.json.itemId || ci2.json.id)) : null;
    created.push({ kind: '상품(items)', name: TAG + '사슬대조M', id: ctlId, status: ci2.status });

    const cBefore = await stockOf(ctlId);
    const al = (await GET('/api/bom/alerts')).json.filter(a => a.itemId === ctlId);
    let od2 = null;
    if (al.length) {
      od2 = await POST('/api/bom/alerts/' + al[0].alertId + '/order?autoReceive=true');
      created.push({ kind: '자동발주(대조군 alert order)', name: TAG + '사슬대조M 발주', id: al[0].alertId, status: od2.status });
      console.log('대조군 사슬 결과: ' + od2.status + ' ' + od2.text.slice(0, 300));
    }
    const cAfter = await stockOf(ctlId);
    const cD = (cAfter ? cAfter.total : 0) - (cBefore ? cBefore.total : 0);
    const b2 = od2 && od2.isJson ? od2.json : null;

    record('축2', '3-C', '대조군 — material 은 사슬이 실제로 도나 (차단 과잉 여부)',
      (b2 && b2.receiptConfirmed === true && cD === 40) ? 'PASS' : (od2 ? 'FAIL' : 'UNKNOWN'),
      '응답본문=' + JSON.stringify(b2) + ' / 재고 ' + (cBefore && cBefore.total) + '→' + (cAfter && cAfter.total) +
      ' (delta=' + cD + ', 기대 +40)');
  }

  // ════════════════════════════════════════════════════
  // [축2-2] 자재 자동발주 중복
  // ════════════════════════════════════════════════════
  {
    const cur = await stockOf(M1);
    const det = await GET('/api/items/' + M1);
    const d = det.json;
    const up = await PUT('/api/items/' + M1, Object.assign({}, d, {
      safetyStock: cur.total + 50, autoOrderEnabled: true,
      autoOrderPartnerId: SUP.partnerId, autoOrderQty: 25, autoReceiveOnOrder: false,
      isActive: true, memo: TAG + '축2-2 자동발주 유발'
    }));
    created.push({ kind: '상품수정(내가 만든 자재X)', name: TAG + '자재X 안전재고 상향', id: M1, status: up.status });
    console.log('자재X 안전재고 상향: ' + up.status + ' ' + up.text.slice(0, 150));

    const poB = (await GET('/api/purchase/orders')).json;
    const cB = Array.isArray(poB) ? poB.length : -1;

    const a1 = (await GET('/api/bom/alerts')).json.filter(a => a.itemId === M1);
    await GET('/api/bom/alerts'); await GET('/api/bom/alerts');
    const a2 = (await GET('/api/bom/alerts')).json.filter(a => a.itemId === M1);

    record('축2', '2-A', 'BOM 자재 알림 — 조회 반복으로 증식하나',
      (a1.length === 1 && a2.length === 1) ? 'PASS' : 'FAIL',
      '자재X 알림수: 조회1회=' + a1.length + ' → 조회4회=' + a2.length + ' (기대 둘 다 1) / 상태=' +
      JSON.stringify(a2.map(x => x.status)));

    let ord = null;
    if (a2.length) {
      ord = await POST('/api/bom/alerts/' + a2[0].alertId + '/order?autoReceive=false');
      created.push({ kind: '자동발주(자재X alert order)', name: TAG + '자재X 발주', id: a2[0].alertId, status: ord.status });
    }

    const poM = (await GET('/api/purchase/orders')).json;
    const cM = Array.isArray(poM) ? poM.length : -1;
    await GET('/api/bom/alerts'); await GET('/api/bom/alerts'); await GET('/api/bom/alerts');
    const poA = (await GET('/api/purchase/orders')).json;
    const cA = Array.isArray(poA) ? poA.length : -1;
    const a3 = (await GET('/api/bom/alerts')).json.filter(a => a.itemId === M1);

    record('축2', '2-B', 'BOM 자재 자동발주 중복 — 반복 조회로 발주가 늘어나나',
      (cA === cM && cM === cB + 1 && a3.length === 1) ? 'PASS' : 'FAIL',
      '발주건수 시작=' + cB + ' → 발주후=' + cM + ' → 조회3회반복후=' + cA +
      ' (기대: +1 뒤 불변) / 알림 잔존수=' + a3.length + ' 상태=' + JSON.stringify(a3.map(x => x.status)));
  }

  // ── 속도 측정 요약 ──────────────────────────────────
  const byPath = {};
  for (const t of timings) {
    const k = t.method + ' ' + t.path.split('?')[0].replace(/[0-9a-f-]{30,}/g, '{id}');
    (byPath[k] = byPath[k] || []).push(t.ms);
  }
  const speed = Object.entries(byPath).map(([k, v]) => ({
    endpoint: k, n: v.length,
    avg: Math.round(v.reduce((a, b) => a + b, 0) / v.length),
    min: Math.min.apply(null, v), max: Math.max.apply(null, v)
  })).sort((a, b) => b.avg - a.avg);

  console.log('\n\n═══ 응답시간 (ms) ═══');
  for (const s of speed) console.log(String(s.avg).padStart(6) + 'ms avg (n=' + s.n + ', ' + s.min + '~' + s.max + ')  ' + s.endpoint);

  console.log('\n═══ 판정 요약 ═══');
  for (const r of results) console.log(r.verdict.padEnd(8) + ' ' + r.axis + ' ' + r.id + ' ' + r.name);

  console.log('\n═══ 생성물 대장 ═══');
  for (const c of created) console.log(c.kind + ' | ' + c.name + ' | ' + c.id + ' | HTTP ' + c.status);

  const out = {
    ranAt: new Date().toISOString(), base: BASE, tag: TAG,
    results: results, created: created, speed: speed, timings: timings
  };
  const fn = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports',
    'audit-axis12-' + new Date().toISOString().replace(/[:.]/g, '-') + '.json');
  fs.mkdirSync(path.dirname(fn), { recursive: true });
  fs.writeFileSync(fn, JSON.stringify(out, null, 2));
  console.log('\n결과 저장: ' + fn);
}

main().catch(function (e) { console.error('FATAL', e); process.exit(1); });
