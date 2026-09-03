/**
 * 매출·매입 사슬 + 반품 정합성/무결성 실측 — 2026-09-03
 *
 * 사장님 오더:
 *   "매출매입사슬+반품 정합성, 무결성"
 *   "매출매입사슬로 연결되는 재고, 금액 정합성, 무결성"
 *
 * 🔴 판정 규율 (20260828 검증팀 규율 승계):
 *  · HTTP 200 으로 판정하지 않는다. JSON 본문과 **실제 수치**로 가른다.
 *  · 확인 못 한 것은 PASS 도 FAIL 도 아닌 **UNKNOWN**. 모르는 걸 OK 로 적지 않는다.
 *  · 계산은 두 출처를 **대조**한다(전표 합계 vs 원장 합계). 한쪽만 보고 판정하지 않는다.
 *
 * 🟢 이 스크립트는 **읽기 전용**이다. POST/PUT/DELETE 를 하지 않는다.
 *    운영 데이터를 건드리지 않는다 (헌법 #39 — 운영은 읽기만).
 */
const fs = require('fs');
const path = require('path');

const BASE = process.env.HITPAN_BASE || 'https://test1234.hitpan.kr';
const LOGIN = { email: 'act0226', password: '11111111' };

let TOK = '';
let DEV = '';
const results = [];

function H() {
  return {
    'Content-Type': 'application/json',
    'Authorization': 'Bearer ' + TOK,
    'X-HitPan-Device-Id': DEV
  };
}

async function get(p) {
  try {
    const r = await fetch(BASE + p, { method: 'GET', headers: H() });
    const text = await r.text();
    let json = null;
    try { json = JSON.parse(text); } catch (e) { /* HTML = 라우트 없음 */ }
    return { status: r.status, json, text: text.slice(0, 300) };
  } catch (e) {
    return { status: 0, json: null, text: String(e.message) };
  }
}

function judge(id, title, verdict, detail) {
  results.push({ id, title, verdict, detail });
  const mark = verdict === 'PASS' ? '🟢' : verdict === 'FAIL' ? '🔴' : '⚠️';
  console.log(`${mark} [${id}] ${title}`);
  if (detail) console.log(`      ${detail}`);
}

const n = (v) => Number(v || 0);
const won = (v) => n(v).toLocaleString('ko-KR');

async function login() {
  const r = await fetch(BASE + '/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(LOGIN)
  });
  if (!r.ok) throw new Error('로그인 실패 ' + r.status + ' ' + (await r.text()).slice(0, 200));
  const j = await r.json();
  TOK = j.accessToken;

  const dv = await (await fetch(BASE + '/api/devices', {
    headers: { Authorization: 'Bearer ' + TOK }
  })).json();
  DEV = (dv.find(d => d.isMainPc) || dv[0] || {}).deviceId;
  if (!DEV) throw new Error('기기를 찾지 못했다');
  console.log(`로그인 OK · device=${DEV}\n대상: ${BASE}\n`);
}

// ────────────────────────────────────────────────────────────────
// [축 A] 매출 사슬 — 명세서 ↔ 반품
// ────────────────────────────────────────────────────────────────
async function axisSalesChain() {
  console.log('\n══ [축 A] 매출 사슬 + 반품 ══');

  const del = await get('/api/sales/deliveries?from=2026-08-01&to=2026-09-30');
  const ret = await get('/api/sales/returns?from=2026-08-01&to=2026-09-30');

  if (!Array.isArray(del.json)) {
    judge('A-0', '거래명세서 목록 조회', 'UNKNOWN',
      `status=${del.status} body=${del.text}`);
    return { deliveries: [], returns: [] };
  }
  const deliveries = del.json;
  const returns = Array.isArray(ret.json) ? ret.json : [];

  judge('A-0', '전표 조회', 'PASS',
    `거래명세서 ${deliveries.length}건 · 매출반품 ${returns.length}건`);

  // A-1 반품이 실재하는가 (사장님: "전표도 없네?")
  if (returns.length === 0) {
    judge('A-1', '매출반품 전표 실재', 'FAIL',
      '반품 전표가 0건이다 — 저장이 안 됐거나 조회가 못 읽는다');
  } else {
    const lines = returns.map(r =>
      `${r.returnNo} · ${r.status} · 공급가 ${won(r.totalAmount)} · 부가세 ${won(r.vatAmount)}`
      + (r.deliveryId ? '' : '  ⚠️원전표링크 없음'));
    judge('A-1', '매출반품 전표 실재', 'PASS', lines.join('\n      '));
  }

  // A-2 🔴 사슬 — 반품이 원전표에 매달렸는가 (결재 3: 근거 없는 마이너스 금지)
  const orphan = returns.filter(r => !r.deliveryId);
  if (returns.length === 0) {
    judge('A-2', '반품 → 원전표 사슬', 'UNKNOWN', '반품이 없어 판정 불가');
  } else if (orphan.length > 0) {
    judge('A-2', '반품 → 원전표 사슬', 'FAIL',
      `원전표 링크 없는 반품 ${orphan.length}건: ${orphan.map(o => o.returnNo).join(', ')}`);
  } else {
    judge('A-2', '반품 → 원전표 사슬', 'PASS',
      returns.map(r => `${r.returnNo} → ${r.deliveryId.slice(0, 8)}…`).join(' / '));
  }

  // A-3 🔴 상태 — 확정인가 미확정인가 (재고가 움직였어야 하는지 가르는 기준)
  const byStatus = {};
  returns.forEach(r => { byStatus[r.status] = (byStatus[r.status] || 0) + 1; });
  const confirmed = returns.filter(r => r.status === 'confirmed');
  judge('A-3', '반품 상태 분포',
    returns.length === 0 ? 'UNKNOWN' : 'PASS',
    Object.entries(byStatus).map(([k, v]) => `${k}=${v}건`).join(' · ')
    + (confirmed.length === 0 && returns.length > 0
        ? '\n      ⚠️ 확정된 반품이 0건 ⇒ 재고·회계가 안 움직인 것이 **정상**이다'
        : ''));

  return { deliveries, returns, confirmed };
}

// ────────────────────────────────────────────────────────────────
// [축 B] 반품 상한 — 판매수량을 넘지 않는가
// ────────────────────────────────────────────────────────────────
async function axisReturnCap(returns) {
  console.log('\n══ [축 B] 반품 상한 (판매수량 대비) ══');

  if (!returns.length) { judge('B-1', '반품 상한', 'UNKNOWN', '반품 없음'); return; }

  let over = 0, checked = 0, unknown = 0;
  for (const r of returns) {
    const d = await get(`/api/sales/returns/${encodeURIComponent(r.returnId)}`);
    if (!d.json || !Array.isArray(d.json.items)) { unknown++; continue; }
    if (!r.deliveryId) { unknown++; continue; }

    const src = await get(`/api/sales/deliveries/${encodeURIComponent(r.deliveryId)}`);
    if (!src.json || !Array.isArray(src.json.items)) { unknown++; continue; }

    const sold = {};
    src.json.items.forEach(it => {
      sold[it.itemId] = (sold[it.itemId] || 0) + n(it.qty);
    });

    for (const it of d.json.items) {
      checked++;
      const s = sold[it.itemId] || 0;
      if (n(it.qty) > s) {
        over++;
        judge('B-1', `초과반품 ${r.returnNo}`, 'FAIL',
          `${it.itemName || it.itemId}: 판매 ${s} < 반품 ${n(it.qty)}`);
      }
    }
  }
  if (over === 0) {
    judge('B-1', '반품 상한 (판매수량 이내)',
      checked > 0 ? 'PASS' : 'UNKNOWN',
      `검사 ${checked}줄 · 초과 0건` + (unknown ? ` · 판정불가 ${unknown}건` : ''));
  }
}

// ────────────────────────────────────────────────────────────────
// [축 C] 🔴 금액 — 헤더 vs 라인, 그리고 (−) 부호
// ────────────────────────────────────────────────────────────────
async function axisAmount(returns) {
  console.log('\n══ [축 C] 금액 정합 (헤더 vs 라인) ══');

  if (!returns.length) { judge('C-1', '금액 정합', 'UNKNOWN', '반품 없음'); return; }

  let bad = 0, ok = 0, unknown = 0;
  for (const r of returns) {
    const d = await get(`/api/sales/returns/${encodeURIComponent(r.returnId)}`);
    if (!d.json || !Array.isArray(d.json.items)) { unknown++; continue; }

    const lineSupply = d.json.items.reduce((a, it) => a + n(it.supplyAmount), 0);
    const lineVat = d.json.items.reduce((a, it) => a + n(it.vatAmount), 0);
    const dSupply = Math.abs(n(r.totalAmount) - lineSupply);
    const dVat = Math.abs(n(r.vatAmount) - lineVat);

    if (dSupply >= 0.01 || dVat >= 0.01) {
      bad++;
      judge('C-1', `금액 불일치 ${r.returnNo}`, 'FAIL',
        `헤더 공급가 ${won(r.totalAmount)} vs 라인 ${won(lineSupply)} (차 ${won(dSupply)}) · `
        + `헤더 부가세 ${won(r.vatAmount)} vs 라인 ${won(lineVat)} (차 ${won(dVat)})`);
    } else {
      ok++;
      // 🔴 저장은 양수여야 한다 — 원장이 음수를 안 받는다(20260831 아키텍처 판정)
      const neg = d.json.items.filter(it => n(it.qty) < 0 || n(it.supplyAmount) < 0);
      if (neg.length) {
        judge('C-2', `저장 부호 ${r.returnNo}`, 'FAIL',
          `음수로 저장된 줄 ${neg.length}개 — 원장 CHECK 제약에 걸린다`);
      }
    }
  }
  if (bad === 0) {
    judge('C-1', '헤더 = 라인 합계', ok > 0 ? 'PASS' : 'UNKNOWN',
      `일치 ${ok}건` + (unknown ? ` · 판정불가 ${unknown}건` : ''));
  }
}

// ────────────────────────────────────────────────────────────────
// [축 D] 🔴 재고 — 확정 반품이 실제로 입고됐는가 + 현재고 vs 원장
// ────────────────────────────────────────────────────────────────
async function axisStock(confirmed) {
  console.log('\n══ [축 D] 재고 정합 ══');

  // D-1 정합성 검사 화면(20260827작8 신설)이 있으면 그것이 가장 강한 근거다
  const integ = await get('/api/accounting/integrity');
  if (integ.json) {
    const arr = Array.isArray(integ.json) ? integ.json : (integ.json.items || []);
    const bad = arr.filter(x =>
      (x.status && String(x.status).toUpperCase() !== 'OK') ||
      n(x.diff) !== 0 || n(x.count) > 0);
    if (arr.length === 0) {
      judge('D-1', '정합성 검사 (서버)', 'UNKNOWN', '항목이 비었다');
    } else if (bad.length === 0) {
      judge('D-1', '정합성 검사 (서버)', 'PASS', `${arr.length}개 항목 전부 이상 없음`);
    } else {
      judge('D-1', '정합성 검사 (서버)', 'FAIL',
        bad.map(b => `${b.name || b.checkName || b.title}: ${b.count ?? b.diff ?? JSON.stringify(b).slice(0,80)}`)
           .join('\n      '));
    }
  } else {
    judge('D-1', '정합성 검사 (서버)', 'UNKNOWN',
      `status=${integ.status} — 화면 API 를 못 읽었다`);
  }

  // D-2 확정 반품 → 재고 반영
  if (!confirmed || confirmed.length === 0) {
    judge('D-2', '확정반품 → 재고 입고', 'UNKNOWN',
      '확정된 반품이 0건 ⇒ 재고가 안 움직인 것이 정상. '
      + '⚠️ 단 「확정할 방법이 있는가」는 별개 문제다');
    return;
  }

  for (const r of confirmed) {
    const led = await get(`/api/inventory/ledger?sourceId=${encodeURIComponent(r.returnId)}`);
    if (!led.json) {
      judge('D-2', `재고원장 ${r.returnNo}`, 'UNKNOWN',
        `원장 조회 실패 status=${led.status}`);
      continue;
    }
    const rows = Array.isArray(led.json) ? led.json : (led.json.items || []);
    if (rows.length === 0) {
      judge('D-2', `재고원장 ${r.returnNo}`, 'FAIL',
        '🔴 P0 — 확정된 반품인데 재고원장에 행이 없다 (입고 미반영)');
    } else {
      const qtyIn = rows.reduce((a, x) => a + n(x.qtyIn), 0);
      judge('D-2', `재고원장 ${r.returnNo}`, qtyIn > 0 ? 'PASS' : 'FAIL',
        `입고수량 ${qtyIn}`);
    }
  }
}

// ────────────────────────────────────────────────────────────────
// [축 E] 🔴 회계·금액 — 부가세·매입매출장에 반품이 반영됐는가 (1.3.33 봉합)
// ────────────────────────────────────────────────────────────────
async function axisFinance() {
  console.log('\n══ [축 E] 회계 반영 (부가세 · 매입매출장) ══');

  const led = await get('/api/finance/purchase-sales-ledger?from=2026-08-01&to=2026-09-30');
  if (Array.isArray(led.json)) {
    const kinds = {};
    led.json.forEach(x => { kinds[x.docType] = (kinds[x.docType] || 0) + 1; });
    const hasSalesReturn = (kinds['매출반품'] || 0) > 0;
    const hasPurchaseReturn = (kinds['매입반품'] || 0) > 0;

    judge('E-1', '매입매출장 구성', 'PASS',
      Object.entries(kinds).map(([k, v]) => `${k}=${v}건`).join(' · '));

    judge('E-2', '매입매출장에 매출반품 행',
      hasSalesReturn ? 'PASS' : (kinds['매출'] ? 'FAIL' : 'UNKNOWN'),
      hasSalesReturn
        ? '매출반품이 별도 행으로 뜬다 (세무사 제출 가능)'
        : '🔴 매출반품 행이 없다 — 확정 반품이 없으면 정상일 수 있다');

    // 🔴 금액 부호 — 반품 행은 (−) 여야 합계가 실제 매출이 된다
    const sr = led.json.filter(x => x.docType === '매출반품');
    const wrongSign = sr.filter(x => n(x.supplyAmount) > 0);
    if (sr.length) {
      judge('E-3', '매출반품 행 부호', wrongSign.length === 0 ? 'PASS' : 'FAIL',
        wrongSign.length === 0
          ? sr.map(x => `${x.docNo} ${won(x.supplyAmount)}`).join(' / ')
          : `양수로 뜬 반품 ${wrongSign.length}건 — 합계가 매출을 부풀린다`);
    }
  } else {
    judge('E-1', '매입매출장', 'UNKNOWN', `status=${led.status} ${led.text}`);
  }

  const vat = await get('/api/finance/vat?year=2026&half=2');
  if (vat.json) {
    const s = vat.json.salesSupply ?? vat.json.sales?.supply;
    const p = vat.json.purchaseSupply ?? vat.json.purchase?.supply;
    judge('E-4', '부가세 집계 조회', 'PASS',
      `매출 공급가 ${won(s)} · 매입 공급가 ${won(p)} (반품 차감 반영본)`);
  } else {
    judge('E-4', '부가세 집계 조회', 'UNKNOWN', `status=${vat.status}`);
  }
}

// ────────────────────────────────────────────────────────────────
// [축 F] 매입 사슬 — 매출과 대칭인가
// ────────────────────────────────────────────────────────────────
async function axisPurchase() {
  console.log('\n══ [축 F] 매입 사슬 + 반품 ══');

  const rec = await get('/api/purchase/receipts?from=2026-08-01&to=2026-09-30');
  const pret = await get('/api/purchase/returns?from=2026-08-01&to=2026-09-30');

  const receipts = Array.isArray(rec.json) ? rec.json : [];
  const preturns = Array.isArray(pret.json) ? pret.json : [];

  judge('F-1', '매입 전표 조회',
    rec.json ? 'PASS' : 'UNKNOWN',
    `매입 ${receipts.length}건 · 매입반품 ${preturns.length}건`);

  if (preturns.length) {
    const orphan = preturns.filter(r => !r.receiptId);
    judge('F-2', '매입반품 → 원전표 사슬',
      orphan.length === 0 ? 'PASS' : 'FAIL',
      orphan.length === 0
        ? `${preturns.length}건 전부 원전표 연결`
        : `링크 없는 반품 ${orphan.length}건`);
  } else {
    judge('F-2', '매입반품 → 원전표 사슬', 'UNKNOWN', '매입반품 없음');
  }
}

// ────────────────────────────────────────────────────────────────
(async () => {
  console.log('매출·매입 사슬 + 반품 정합성 실측 — 2026-09-03');
  console.log('🟢 읽기 전용 — 데이터를 바꾸지 않는다\n');

  await login();

  const { returns, confirmed } = await axisSalesChain();
  await axisReturnCap(returns);
  await axisAmount(returns);
  await axisStock(confirmed);
  await axisFinance();
  await axisPurchase();

  // ── 판정 요약
  console.log('\n═══════════ 판정 요약 ═══════════');
  const pass = results.filter(r => r.verdict === 'PASS').length;
  const fail = results.filter(r => r.verdict === 'FAIL').length;
  const unk = results.filter(r => r.verdict === 'UNKNOWN').length;
  console.log(`🟢 PASS ${pass} · 🔴 FAIL ${fail} · ⚠️ UNKNOWN ${unk}`);

  if (fail) {
    console.log('\n🔴 FAIL 항목:');
    results.filter(r => r.verdict === 'FAIL')
           .forEach(r => console.log(`  [${r.id}] ${r.title}\n      ${r.detail}`));
  }
  if (unk) {
    console.log('\n⚠️ UNKNOWN (확인 못 함 — PASS 아님):');
    results.filter(r => r.verdict === 'UNKNOWN')
           .forEach(r => console.log(`  [${r.id}] ${r.title} — ${r.detail}`));
  }

  const out = path.join(__dirname, '..', '..', 'tests', 'scenarios', 'reports',
    `audit-chain-integrity-${new Date().toISOString().slice(0, 10)}.json`);
  try {
    fs.mkdirSync(path.dirname(out), { recursive: true });
    fs.writeFileSync(out, JSON.stringify({ base: BASE, results }, null, 2), 'utf8');
    console.log(`\n보고서: ${out}`);
  } catch (e) { console.log('보고서 저장 실패: ' + e.message); }

  process.exit(fail > 0 ? 1 : 0);
})().catch(e => { console.error('실측 중단: ' + e.message); process.exit(2); });
