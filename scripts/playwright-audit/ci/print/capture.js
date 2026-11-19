'use strict';
// W-G1-51 갈래 Z — 화면 차림 · 판독 입구 (개발명세서 §3 ④ ⑤ · 설계 §1-2 · §1-3 F7)
//   🔴 판독 입구 = 화면 단추가 보낸 요청의 응답만(API 직접 호출 0):
//      ② 양식정보설정 → [수정] → [미리보기] 가 보낸 POST …/form-templates/preview-pdf(FormTemplateController.cs:126)
//      ③ 전표 화면 [목록] → 거래 두 번 누름 → [인쇄] 가 보낸 POST …/documents/{type}/{id}/preview-pdf(DocumentController.cs:816)
//   🔴 로고 · 전표 차림 = 화면으로(설계 글자 · PM §6-a 4): 회사정보 화면 「로고 등록」 · 화면별 전표 저장 · 판매 반품 = [반품하기]
//      로고 지우기 = 인쇄 설정 화면 회사 로고 「삭제」(회사정보 화면엔 지우기 단추 없음 · PrintSettingsPage.razor:55~72)
//   🔴 양식 = 저장 문(setup-api.js) · 차림 실패 = 「차림 불가(사유)」 · PDF 못 받음 · 시간 넘김 = 「미측정」(판매 반품 = 오류가 곧 결함 · C8)
//   🔴 이 PC 에서 돌리지 않는다 — CI 시험 잡(ui-print)에서만(이 PC = 실물 정식설치 PC)
//   🔴 4차(11/19 · 개발명세서 §4-a ⓐ · ⓒ) — 차림 순서: API 로그인 → 약관 동의(표지 참일 때만) → 기초 자료 · 양식(API 세션)
//      → API 로그아웃 → 화면 로그인(응답 · 자리 · 토큰 판정 · 200 아니면 차림 불가) → 로고 · 전표(화면) · 양식 바꾸기 · 확인(화면 세션 토큰) → 끝 로그아웃(화면 세션)
//   🔴 5차(11/19 · 개발명세서 §1-5) — ⓔ6 동의 뒤 상태 문 다시 확인(S.confirmTerms · 약관 동의 바로 뒤) · ⓔ3 main 손 주입(realDeps · 동작 순서 무변)
//   쓰는 법: node capture.js   (환경: PRINT_OUT · UI_BASE · UI_LOGIN_PARENT · UI_PW_PARENT · PRINT_RUN · PRINT_JOB · PRINT_BOARD · PRINT_PATCH_FAIL · PRINT_PROBE_OK)
const fs = require('fs');
const path = require('path');
const K = require('../kit35');
const L = require('../lib');
const F = require('./fixture');
const PNG = require('./png');
const S = require('./setup-api');

const OUT = process.env.PRINT_OUT || path.join(process.env.RUNNER_TEMP || '.', 'print-out');
const WAIT = 60000;
const sleep = (ms) => new Promise((ok) => setTimeout(ok, ms));
const log = (s) => console.log(L.sanitize(s, 400));

function safeName(id, n) {
  return `${String(n).padStart(3, '0')}-${id.replace(/[^A-Za-z0-9.-]/g, '_')}`;
}

// ── 화면 도우미(MudBlazor) ──
function inputByLabel(page, label) {
  return page.locator('.mud-input-control').filter({ has: page.locator('label', { hasText: new RegExp(`^\\s*${label}`) }) }).locator('input').first();
}
async function pickAutocomplete(page, input, text) {
  await input.click({ timeout: WAIT });
  await input.fill(text);
  const opt = page.locator('.mud-popover-open .mud-list-item').filter({ hasText: text }).first();
  await opt.waitFor({ state: 'visible', timeout: 20000 });
  await opt.click();
  await sleep(300);
}
async function pickSelect(page, label, optionText) {
  await page.locator('.mud-input-control').filter({ has: page.locator('label', { hasText: label }) }).first().click({ timeout: WAIT });
  const opt = page.locator('.mud-popover-open .mud-list-item').filter({ hasText: optionText }).first();
  await opt.waitFor({ state: 'visible', timeout: 15000 });
  await opt.click();
  await sleep(300);
}
const button = (scope, name) => scope.getByRole('button', { name, exact: true });
async function goto(page, route) {
  await page.goto(`${K.BASE}${route}`, { waitUntil: 'domcontentloaded', timeout: WAIT });
  await page.locator('.mud-main-content, main').first().waitFor({ timeout: WAIT });
  await sleep(800);
}
async function errorToasts(page) {
  return page.locator('.mud-snackbar.mud-alert-filled-error, .mud-snackbar.mud-alert-filled-warning, .mud-snackbar-error, .mud-snackbar-warning').count();
}

/**
 * 🔴 7차 R-3(CTO 재판정 §5-1 · Z4 〔가〕② 13칸) — 품목표 수량 머리 글자. 정확 일치만(부분 일치 = 「반품수량」 말고 다른 칸까지 잡을 수 있어 안 씀).
 *   근거(제품 .razor MudTh 원문 · C5 시험이 런마다 대조): 「수량」 = QuotationGrid · DeliveryGrid · SalesOrderGrid · PurchaseOrderGrid · PurchaseReceiptGrid
 *   · 「반품수량」 = PurchaseReturnGrid.razor:39(매입 반품 — makeDoc → fillLines 길) · SalesReturnGrid.razor:40(판매 반품 = [반품하기] 길 · fillLines 안 지남)
 */
const QTY_HEADERS = ['수량', '반품수량'];
function qtyColumn(headers) {
  return headers.findIndex((h) => QTY_HEADERS.includes(String(h).trim()));
}

/** 전표 줄 2개 — [추가] 두 번 → 줄마다 품명(자동완성) · 수량. */
async function fillLines(page) {
  const headers = await page.locator('th').allInnerTexts();
  // ⬛ [낡은 줄 · 1~6차] const qtyCol = headers.findIndex((h) => h.trim() === '수량');
  // ⬛ [낡은 줄 · 1~6차] if (qtyCol < 0) throw new S.SetupError('품목표에 「수량」 머리 없음');
  const qtyCol = qtyColumn(headers);
  if (qtyCol < 0) throw new S.SetupError(`품목표에 수량 머리(${QTY_HEADERS.join(' · ')}) 없음 — 머리 ${headers.length}: ${L.sanitize(headers.map((h) => String(h).trim()).join('|'), 160)}`);
  for (let k = 0; k < F.MARK.items.length; k++) {
    await button(page, '추가').first().click({ timeout: WAIT });
    await sleep(400);
  }
  const rows = page.locator('tr').filter({ has: page.locator('input[placeholder="품명 검색"]') });
  const n = await rows.count();
  if (n < F.MARK.items.length) throw new S.SetupError(`품목 줄 ${n}(2 이상이어야)`);
  for (let k = 0; k < F.MARK.items.length; k++) {
    const it = F.MARK.items[k];
    const row = rows.nth(n - F.MARK.items.length + k);
    await pickAutocomplete(page, row.locator('input[placeholder="품명 검색"]'), it.name);
    const q = row.locator(`td:nth-child(${qtyCol + 1}) input`).first();
    await q.click();
    await q.fill(String(it.qty));
    await q.press('Tab');
    await sleep(300);
  }
}
// ⬛ [낡은 덩이 · 1~9차] saveDoc — 오류 알림 수만 셈(성공 · 확인 창 · 번호 안 봄 → 거래명세서 「수주서 자동 생성」 창이 열린 채 ok · [1-V] 판매 39 E4 · E5)
// async function saveDoc(page, what) {
//   const before = await errorToasts(page);
//   await button(page, '저장').first().click({ timeout: WAIT });
//   await sleep(2500);
//   const after = await errorToasts(page);
//   if (after > before) throw new S.SetupError(`${what} 저장 뒤 경고 · 오류 알림 ${after - before}`);
// }

/**
 * 🔴 10차 D4 · D8 ⓐ(작업지시서 §1-2 · [1-V] 판매 39 §3 조건 1 · 2 · 명세서 §1-10-0 (4) · (5)) — 저장 성공 = 확인 창 안 남음 · 오류 알림 0 · 저장 응답 2xx
 *   · 성공 알림(제품 원문) · 번호(알림 글 → 응답 번호 칸 → 응답 id 차례) 모두일 때만. 오류 알림 0 ≠ 성공.
 *   url = 화면 단추가 보내는 저장 POST(제품 Web 원문) · toast = 성공 알림 원문 · confirm = 저장 앞 확인 창(창 범위 안 단추) · after = 저장 뒤 화면 이동.
 */
const SAVE_WAIT = 20000;
const TOAST_WAIT = 6000;
const SAVE_OK = {
  // QuotationPage.razor.cs:299 · 저장 = QuotationService.cs:148 POST api/quotations · 응답 { id }(QuotationController.cs:95)
  estimate: { url: '/api/quotations', toast: /견적서 저장이 완료되었습니다/ },
  // DeliveryPage.razor:647 · 창 :624~628 · 저장 = DeliveryService.cs:82 POST api/sales/deliveries · 응답 documentNumber · id · autoCreatedOrderNo(DeliveryModels.cs:268)
  delivery: {
    url: '/api/sales/deliveries', toast: /저장 완료: *([^\s|]+)/,
    confirm: { title: '수주서 자동 생성', text: '수주서가 없는 거래입니다. 수주서를 자동 생성합니다.', yes: '저장', no: '취소' },
  },
  // SalesOrderPage.razor.cs:290 · 저장 = DeliveryService.cs:137 POST api/sales/orders · 응답 documentNumber · id
  sales_order: { url: '/api/sales/orders', toast: /수주서 저장이 완료되었습니다/ },
  // PurchaseOrderPage.razor.cs:317 · 저장 = POST api/purchase/orders · 응답 id(PurchaseOrderPage.razor.cs:798)
  purchase_order: { url: '/api/purchase/orders', toast: /발주서가 서버에 저장되었습니다/ },
  // PurchaseReceiptPage.razor.cs:386 · 저장 = POST api/purchase/receipts · 응답 id(PurchaseReceiptPage.razor.cs:913)
  receipt: { url: '/api/purchase/receipts', toast: /매입명세서가 서버에 저장되었습니다/ },
  // ReturnPage.razor.cs:255 「{docLabel}을 저장했습니다.」 · 저장 = POST api/purchase/returns · 응답 returnId · returnNo(ReturnPage.razor.cs:284~287)
  purchase_return: { url: '/api/purchase/returns', toast: /을 저장했습니다/ },
  // DeliveryPage.razor:346 「반품 저장 완료: {번호}」 뒤 :347 forceLoad 이동 → 알림이 지워질 수 있어 알림 대신 이동(/deliveries) · 저장 = DeliveryService.cs:858 POST api/sales/returns · 응답 returnId · returnNo
  // ⬛ [낡은 줄 · 10차 1단] sales_return: { url: '/api/sales/returns', toast: null, after: /\/deliveries(\?|#|$)/ },
  // 🔴 11차 P2-2 — 저장 전 주소 `/deliveries?returnOf=…`(DeliveryPage.razor:1234)가 옛 꼴에 이미 맞아 이동 판정 · waitForURL 공회전 → 「returnOf 없는 /deliveries」(:347 NavigateTo("/deliveries") 글자)만
  sales_return: { url: '/api/sales/returns', toast: null, after: /\/deliveries(?![^#]*[?&]returnOf=)(?:[?#]|$)/ },
};
const AUTO_SO = /수주서 (\S+) 가 자동 생성되었습니다/; // DeliveryPage.razor:652
const NO_KEYS = ['documentNumber', 'documentNo', 'returnNo', 'receiptNo', 'orderNo', 'quotationNo', 'deliveryNo'];
const ID_KEYS = ['id', 'returnId', 'receiptId', 'orderId', 'quotationId', 'deliveryId'];
const clean1 = (s, cap) => L.sanitize(String(s ?? '').replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/\s+/g, ' ').trim(), cap);
/** 응답 본문 → { no(번호 칸) · id · auto(자동 생성 수주 번호) } — 칸 이름 대소문자 무시 · data 한 겹까지. */
function numberFrom(body) {
  const base = body && typeof body === 'object' ? body : {};
  const o = base.data && typeof base.data === 'object' ? { ...base.data, ...base } : base;
  const low = new Map(Object.keys(o).map((k) => [k.toLowerCase(), o[k]]));
  const first = (keys) => {
    for (const k of keys) {
      const v = low.get(k.toLowerCase());
      if (typeof v === 'string' && v.trim()) return v.trim();
      if (typeof v === 'number' && Number.isFinite(v)) return String(v);
    }
    return '';
  };
  return { no: first(NO_KEYS), id: first(ID_KEYS), auto: first(['autoCreatedOrderNo']) };
}
/** 저장 판정(순수) — 차례: 확인 창 남음 → 오류 알림 → 응답 없음 · 실패 → 성공 알림 → 이동 → 번호. pick = 목록 줄 고르기용 원 번호(D8 ⓓ · 기록은 소독본). */
function saveVerdict(what, ok, s) {
  if (s.dialogLeft != null) return { ok: false, why: `${what} 확인 창 남음(창 머리 「${clean1(s.dialogLeft, 60)}」)` };
  if (s.errDelta > 0) return { ok: false, why: `${what} 저장 뒤 경고 · 오류 알림 ${s.errDelta}` };
  if (!s.resp) return { ok: false, why: `${what} 저장 요청 응답 없음` };
  if (!(s.resp.status >= 200 && s.resp.status < 300)) return { ok: false, why: `${what} 저장 응답 ${s.resp.status}` };
  if (ok.toast && !ok.toast.test(String(s.toastText || ''))) return { ok: false, why: `${what} 저장 성공 알림 없음` };
  if (ok.after && !ok.after.test(String(s.url || ''))) return { ok: false, why: `${what} 저장 뒤 화면 이동 없음` };
  const m = ok.toast ? ok.toast.exec(String(s.toastText || '')) : null;
  const n = numberFrom(s.resp.body);
  const docNo = (m && m[1]) || n.no;
  const no = docNo || n.id;
  if (!no) return { ok: false, why: `${what} 전표 번호 없음` };
  const a = AUTO_SO.exec(String(s.toastText || ''));
  return { ok: true, no: clean1(no, 40), docNo: clean1(docNo, 40), id: clean1(n.id, 40), auto: String(n.auto || (a && a[1]) || '').slice(0, 60), pick: String(docNo || '').slice(0, 60) };
}
/** 저장 손(실물 화면) — 자체시험(T-D4a · T-D4b · T-D8a)은 같은 꼴의 가짜 손을 넣는다. */
function saveIo(page) {
  return {
    errors: () => errorToasts(page),
    clickSave: () => button(page, '저장').first().click({ timeout: WAIT }),
    waitResp: (urlPart) => page.waitForResponse((r) => {
      if (r.request().method() !== 'POST') return false;
      try {
        return new URL(r.url()).pathname.replace(/\/+$/, '').endsWith(urlPart);
      } catch (e) {
        log(`저장 응답 주소 못 읽음(${L.errorName(e)})`);
        return false;
      }
    }, { timeout: SAVE_WAIT }).then(async (r) => {
      let body = null;
      try {
        body = await r.json();
      } catch (e) {
        body = { jsonErr: L.errorName(e) };
      }
      return { status: r.status(), body };
    }, (e) => {
      log(`저장 응답 기다림 끝(${L.errorName(e)})`);
      return null;
    }),
    confirmDialog: async (c) => {
      const dlg = page.locator('.mud-dialog').filter({ hasText: c.title }).last();
      try {
        await dlg.waitFor({ state: 'visible', timeout: 10000 });
      } catch (e) {
        log(`확인 창 「${c.title}」 안 보임(${L.errorName(e)})`);
        return { seen: false };
      }
      const text = clean1(await dlg.innerText(), 120);
      await button(dlg, c.yes).first().click({ timeout: WAIT }); // 창 범위 안 단추(화면 [저장] 과 이름이 같음)
      return { seen: true, text };
    },
    dialogLeft: async () => {
      const d = page.locator('.mud-dialog').last();
      if (!(await visibleOr(d, '저장 뒤 창'))) return null;
      return String(await d.innerText()).split('\n')[0] || '창';
    },
    toasts: async (re, ms) => {
      const t0 = Date.now();
      let last = '';
      while (Date.now() - t0 < ms) {
        try {
          last = (await page.locator('.mud-snackbar').allInnerTexts()).map((x) => String(x)).join(' | ');
        } catch (e) {
          log(`알림 읽기 실패(${L.errorName(e)})`);
        }
        if (re.test(last)) return last;
        await sleep(250);
      }
      return last;
    },
    waitUrl: async (re) => {
      try {
        await page.waitForURL(re, { timeout: SAVE_WAIT });
      } catch (e) {
        log(`저장 뒤 이동 기다림 끝(${L.errorName(e)})`);
      }
    },
    url: () => page.url(),
  };
}
async function saveDoc(page, what, key, io = saveIo(page)) {
  const ok = SAVE_OK[key];
  if (!ok) throw new S.SetupError(`${what} 저장 판정 꼴 없음(${key})`);
  const before = await io.errors();
  const resp = io.waitResp(ok.url);
  await io.clickSave();
  const dialog = ok.confirm ? await io.confirmDialog(ok.confirm) : null;
  const r = await resp;
  const toastText = ok.toast ? await io.toasts(ok.toast, TOAST_WAIT) : '';
  if (ok.after) await io.waitUrl(ok.after);
  const v = saveVerdict(what, ok, { dialogLeft: await io.dialogLeft(), errDelta: (await io.errors()) - before, resp: r, toastText, url: io.url() });
  if (!v.ok) throw new S.SetupError(dialog && dialog.seen === false ? `${v.why} · 확인 창 「${ok.confirm.title}」 안 보임` : v.why);
  if (dialog) v.dialog = dialog; // [1-V] PM 미확인 「창이 실제로 떴나」 → 창 글(소독본) 기록 · 화면 사진 0(W4)
  v.status = r.status;
  return v;
}

/** 화면별 전표 1건(거래처 · 품목 2줄) — 화면으로 저장. */
async function makeDoc(page, t) {
  await goto(page, t.route);
  if (t.returnType === '매입반품') await pickSelect(page, '반품유형', '매입반품');
  await pickAutocomplete(page, inputByLabel(page, t.partnerLabel), F.MARK.partner);
  await fillLines(page);
  // ⬛ [낡은 줄 · 1~9차] await saveDoc(page, t.label);
  return saveDoc(page, t.label, t.key); // 🔴 10차 D4 — 번호를 돌려줌(main 이 기록 · D8 ⓓ 고르기)
}
/**
 * 🔴 3차(11/19 · [4] F5) — 판독 입구 줄 고르기. 판매 반품을 거래명세서 [반품하기]로 만들므로 `/deliveries` 목록엔
 *   표지 거래처 줄이 둘(원 전표 · 반품). 2차는 거래명세서 입구가 `rows => rows` 의 첫 줄이라 목록 정렬에 따라 반품 줄을 집을 수 있었다.
 *   → 거래명세서 일반 입구 = 「반품」 글자 줄 **뺌**(정렬에 기대지 않음) · ⓙ = 「반품」 글자 줄만 · 그 밖 = 그대로.
 */
// ⬛ [낡은 줄 · 3~9차] function entryPick(typeKey, entry) {
function entryPick(typeKey, entry, nos) {
  if (nos) return pickByNo(typeKey, entry, nos); // 🔴 10차 D8 ⓓ — 번호가 있으면 번호로 · 없으면(옛 부름 · 자체시험 T-Z36 꼴) 3차 규칙 그대로
  if (entry === 'doc-j') return (rows) => rows.filter({ hasText: '반품' });
  if (typeKey === 'delivery') return (rows) => rows.filter({ hasNotText: '반품' });
  return (rows) => rows;
}

/**
 * 🔴 10차 D8 ⓓ([1-V] 판매 39 §3 조건 5 · 명세서 §1-10-0 (5)) — 입구 줄 = 장치가 만든 전표 번호 글자가 든 줄.
 *   수주서: 거래명세서 저장이 표지 거래처 수주를 하나 더 만듦(DeliveryPage.razor:650~653) → 제 번호 줄 · 제 번호를 못 받았으면 자동 생성 번호 줄을 뺌 · 둘 다 없으면 차림 불가(첫 줄로 안 물러섬)
 *   거래명세서: 제 번호 + 「반품」 글자 뺌(반품 줄은 「{원전표} 의 반품」 으로 원전표 번호를 품음 · SalesListDialog.razor 반품 줄) · ⓙ = 「반품」 줄(반품 번호가 있으면 그 번호까지)
 *   견적: 응답 = { id } 만(QuotationController.cs:95 · id 는 목록에 안 보임) → 번호 칸이 있을 때만 거름 · 없으면 표지 줄(장치가 만드는 견적 = 1건 · 자동 생성 견적 길 없음)
 */
function pickByNo(typeKey, entry, nos) {
  const docNo = (k) => (nos[k] && nos[k].pick) || '';
  if (entry === 'doc-j') {
    const rn = docNo('sales_return');
    return (rows) => (rn ? rows.filter({ hasText: '반품' }).filter({ hasText: rn }) : rows.filter({ hasText: '반품' }));
  }
  if (typeKey === 'delivery') {
    const n = docNo('delivery');
    if (!n) throw new S.SetupError('거래명세서 입구 — 번호 없음(첫 줄로 물러서지 않음)');
    return (rows) => rows.filter({ hasNotText: '반품' }).filter({ hasText: n });
  }
  if (typeKey === 'sales_order') {
    const n = docNo('sales_order');
    const auto = (nos.delivery && nos.delivery.auto) || '';
    if (n) return (rows) => rows.filter({ hasText: n });
    if (auto) return (rows) => rows.filter({ hasNotText: auto });
    throw new S.SetupError('수주서 입구 — 번호 없음 · 자동 생성 수주 번호도 없음(첫 줄로 물러서지 않음)');
  }
  if (typeKey === 'estimate') {
    const n = docNo('estimate');
    return n ? (rows) => rows.filter({ hasText: n }) : (rows) => rows;
  }
  return (rows) => rows;
}

// ⬛ [낡은 덩이 · 3~9차] makeSalesReturn(page) — 확정 단계 없음 · 표지 첫 줄 · saveDoc 오류 알림만([1-V] 판매 39 §3 조건 3 · 4)
// /** 판매 반품 — 거래명세서 저장(확정) → [반품하기] → 반품 사유 → 저장(DeliveryPage.razor:109 · :1234 · :289). */
// async function makeSalesReturn(page) {
//   // ⬛ [낡은 줄 · 2차] await openFromList(page, F.TYPES.find((x) => x.key === 'delivery'), (row) => row);
//   await openFromList(page, F.TYPES.find((x) => x.key === 'delivery'), entryPick('delivery', 'doc'));
//   const ret = button(page, '반품하기').first();
//   if (await ret.isDisabled()) throw new S.SetupError('[반품하기] 꺼짐(거래명세서 확정 아님 · :109)');
//   await ret.click();
//   await page.waitForURL(/returnOf=/, { timeout: WAIT });
//   await page.locator('.mud-main-content, main').first().waitFor({ timeout: WAIT });
//   await sleep(1500);
//   await pickSelect(page, '반품 사유', '고객 단순 반품');
//   await saveDoc(page, '판매 반품');
// }

/**
 * 🔴 10차 D8 ⓑ([1-V] 판매 39 §3 조건 3) — 판매 목록 창(SalesListDialog.razor)에서 번호 줄 체크 → 「판매확정」(:120~124) → 확인 창(:361~366) 「판매확정」
 *   → 알림 「{n}건 확정 완료」(:389) · 「{n}건 확정 실패 — {사유}」(:395 · 재고 부족이면 자동발주 창이 뒤따름 → 닫고 사유로 남김).
 */
const CONFIRM_OK = { button: '판매확정', ask: /판매확정 처리하시겠습니까/, yes: '판매확정', done: /(\d+)건 확정 완료/, fail: /(\d+)건 확정 실패 — ([^|]*)/ };
function confirmVerdict(text) {
  const s = String(text || '');
  const bad = CONFIRM_OK.fail.exec(s);
  if (bad) return { ok: false, why: clean1(`${bad[1]}건 확정 실패 — ${bad[2]}`, 120) };
  const good = CONFIRM_OK.done.exec(s);
  if (good && Number(good[1]) >= 1) return { ok: true, n: Number(good[1]) };
  return { ok: false, why: '확정 알림 없음' };
}
async function confirmDelivery(page, t, n) {
  await goto(page, t.route);
  await button(page, '목록').first().click({ timeout: WAIT });
  const dlg = page.locator('.mud-dialog').filter({ hasText: t.listTitle }).last();
  await dlg.waitFor({ state: 'visible', timeout: WAIT });
  const rows = await waitListRows(page, dlg, t, (r) => r.filter({ hasNotText: '반품' }).filter({ hasText: n }));
  await rows.first().locator('input[type="checkbox"]').first().check({ timeout: WAIT, force: true });
  await button(dlg, CONFIRM_OK.button).first().click({ timeout: WAIT });
  const ask = page.locator('.mud-dialog').filter({ hasText: CONFIRM_OK.ask }).last();
  await ask.waitFor({ state: 'visible', timeout: WAIT });
  await button(ask, CONFIRM_OK.yes).first().click({ timeout: WAIT });
  const v = confirmVerdict(await saveIo(page).toasts(/건 확정 (완료|실패)/, TOAST_WAIT));
  await closeDialogs(page);
  if (!v.ok) throw new S.SetupError(`거래명세서 확정 실패 — ${v.why}`);
  return v;
}

/**
 * 🔴 10차 D8 ⓑ · ⓒ · ⓓ — 판매 반품: 재고 선행 결과 확인 → 거래명세서 확정(번호 줄) → 번호 줄 두 번 누름 → [반품하기](확정일 때만 켜짐 · DeliveryPage.razor:109)
 *   → 반품 사유 → 저장(D4 · 응답 번호 + /deliveries 이동). steps = 화면 손(자체시험 T-D8b 는 가짜 손 · 확정을 걷으면 [반품하기] 꺼짐).
 */
async function makeSalesReturn(page, nos = {}, stock = null, steps = salesReturnSteps(page)) {
  if (!stock || stock.ok !== true) throw new S.SetupError(`재고 선행 실패 — ${(stock && stock.why) || '기록 없음'}`);
  const t = F.TYPES.find((x) => x.key === 'delivery');
  const n = nos.delivery && nos.delivery.pick;
  if (!n) throw new S.SetupError('판매 반품 — 거래명세서 번호 없음(D4 기록 없음 · 첫 줄로 물러서지 않음)');
  await steps.confirm(t, n);
  await steps.open(t, entryPick('delivery', 'doc', nos));
  if (!(await steps.returnEnabled())) throw new S.SetupError('[반품하기] 꺼짐(거래명세서 확정 아님 · :109)');
  await steps.clickReturn();
  await steps.pickReason();
  return steps.save();
}
function salesReturnSteps(page) {
  return {
    confirm: (t, n) => confirmDelivery(page, t, n),
    open: (t, pick) => openFromList(page, t, pick),
    returnEnabled: async () => !(await button(page, '반품하기').first().isDisabled()),
    clickReturn: async () => {
      await button(page, '반품하기').first().click();
      await page.waitForURL(/returnOf=/, { timeout: WAIT });
      await page.locator('.mud-main-content, main').first().waitFor({ timeout: WAIT });
      await sleep(1500);
    },
    pickReason: () => pickSelect(page, '반품 사유', '고객 단순 반품'),
    save: () => saveDoc(page, '판매 반품', 'sales_return'),
  };
}

/**
 * 🔴 7차 R-4(CTO 재판정 §5-1 「두 가지만」 · W4) — ① 목록 줄이 나타날 때까지 기다린다(전엔 0.8초 쉬고 한 번 셈)
 *   ② 못 찾으면 글자 진단만 남긴다: 줄 수 · 기간 값 · 브라우저 시간대 · 창 글 소독본(L.sanitize 200자 · 제어 글자 빈칸). 화면 사진 0(W4 · 누출 검사가 그림을 못 읽음).
 *   기간 · 시간대는 근거가 없어 값을 바꾸지 않는다(기록만). 기다림 상한 = LIST_WAIT(10초) — 못 찾는 칸이 39 이어도 캡처 단계 70분 상한에 6.5분 이하(C7 · 명세서 §1-7).
 */
const LIST_WAIT = 10000;
const CTL_ALL = /[\u0000-\u001f\u007f]/g;
async function listDiag(page, dlg) {
  // 🔴 8차 Z8-4(CTO 8차 판정 §4-1 · §6) — 읽기 3칸 더함 · 실패 가지(waitListRows 가 표지 줄을 못 찾은 뒤)에서만 불림 · 화면 사진 0
  //   (i) 창 밖 날짜 = 뒤 화면 새 전표의 날짜 기본값(최대 3 · 40자) (ii) 창 머리 줄 · 첫 3줄 글 따로(줄마다 80자) (iii) 결과 없음 문구 참/거짓 · 쪽 글(40자)
  //   칸마다 take() + 시간 상한 DIAG_MS(못 읽으면 「못 읽음(이름)」 · 칸 9 × 2초 = 칸 하나에 최대 18초)
  // 🔴 8차 Z8-3([3-V] P2-1) — 진단 글 소독 = clean(): 제어 글자 → 빈칸 **먼저** · 공백 접기 · L.sanitize(3꼴 지움 뒤 자름)
  //   7차 창 글은 소독 **뒤에** 제어 글자를 빈칸으로 바꿔, 제어 글자로 쪼갠 비밀값(빈칸 든 값)이 소독 뒤 다시 이어졌다 · 7차 기간 값은 제어 글자를 안 걸렀다
  const DIAG_MS = 2000;
  const RESULT_NONE = /(조회된[^\n]{0,30}없습니다|결과가 없습니다)/; // SalesListDialog.razor:89 「조회된 거래명세서가 없습니다.」 · 그 밖 「결과가 없습니다」 꼴
  const PAGER = '.mud-table-pagination-caption, .mud-table-page-number-information, .mud-table-pagination-information';
  const clean = (s, cap) => L.sanitize(String(s ?? '').replace(CTL_ALL, ' ').replace(/\s+/g, ' ').trim(), cap);
  const part = [];
  // ⬛ [낡은 줄 · 7차] const take = async (what, fn) => {
  const take = async (what, fn, ms = DIAG_MS) => {
    let t = null;
    try {
      // ⬛ [낡은 줄 · 7차] part.push(`${what} ${await fn()}`);
      const cap = new Promise((_, no) => {
        t = setTimeout(() => no(Object.assign(new Error(`${what} 시간 상한 ${ms}ms`), { name: 'DiagTimeout' })), ms);
      });
      part.push(`${what} ${await Promise.race([fn(), cap])}`); // race 가 늦은 거절도 받음(처리 안 된 거절 0)
    } catch (e) {
      part.push(`${what} 못 읽음(${L.errorName(e)})`); // 진단 실패도 사유 글에 남김(#15)
    } finally {
      clearTimeout(t);
    }
  };
  await take('줄 수', async () => `${await dlg.locator('tr').count()}(표지 줄 ${await dlg.locator('tr').filter({ hasText: F.MARK.partner }).count()})`);
  await take('기간 값', async () => {
    const v = await dlg.locator('input').evaluateAll((els) => els.map((x) => x.value || '').filter((x) => /\d{4}-\d{2}-\d{2}/.test(x)));
    // ⬛ [낡은 줄 · 7차] return v.length ? v.map((x) => L.sanitize(x, 40)).join(' ~ ') : '없음';
    return v.length ? v.map((x) => clean(x, 40)).join(' ~ ') : '없음';
  });
  await take('브라우저 시간대', async () => page.evaluate(() => `${Intl.DateTimeFormat().resolvedOptions().timeZone} · 분 차 ${-new Date().getTimezoneOffset()}`));
  // ⬛ [낡은 줄 · 7차] await take('창 글', async () => `「${L.sanitize(String(await dlg.innerText()).replace(/\s+/g, ' '), 200).replace(CTL_ALL, ' ')}」`);
  await take('창 글', async () => `「${clean(await dlg.innerText(), 200)}」`);
  // ── 8차 Z8-4 (i) 창 밖 날짜(뒤 화면 · `.mud-dialog` 밖 입력의 yyyy-mm-dd 꼴 값 · 최대 3)
  await take('창 밖 날짜', async () => {
    // ⬛ [낡은 줄 · 8~9차] const v = await page.locator('input').evaluateAll((els) => els.filter((x) => !x.closest('.mud-dialog')).map((x) => x.value || '').filter((x) => /\d{4}-\d{2}-\d{2}/.test(x)).slice(0, 3));
    // 🔴 10차 D5(F9-1) — 거래명세서 「판매일자」 MudDatePicker 는 DateFormat 없음(DeliveryPage.razor:69 · 문화권 짧은 날짜 꼴 = 구성 요소 판 · 전역화 자료가 정함)
    //   목록 창 기간 칸만 DateFormat="yyyy-MM-dd"(SalesListDialog.razor:20 · :27) → 창 밖 후보 꼴 2(yyyy-MM-dd · yyyy. M. d.)를 함께 잡음 · 실물 값 = CI 에서만(C6 「F9-1 실물 값」)
    //   (거름 글자는 브라우저 안에서 도는 함수라 바깥 이름을 못 씀 → 글자 그대로)
    const v = await page.locator('input').evaluateAll((els) => els.filter((x) => !x.closest('.mud-dialog')).map((x) => x.value || '').filter((x) => /\d{4}-\d{2}-\d{2}|\d{4}\. ?\d{1,2}\. ?\d{1,2}\.?/.test(x)).slice(0, 3));
    return v.length ? v.map((x) => clean(x, 40)).join(' / ') : '없음';
  });
  // ── 8차 Z8-4 (ii) 창 머리 줄 · 첫 3줄(머리 = th 가 든 줄 · 나머지 = 몸 줄 · 빈 목록 문구 줄도 몸 줄로 셈)
  const rowsOf = () => dlg.locator('tr').evaluateAll((els) => els.map((r) => ({ head: !!r.querySelector('th'), text: r.innerText || r.textContent || '' })));
  await take('창 머리 줄', async () => {
    const h = (await rowsOf()).find((r) => r.head);
    return h ? `「${clean(h.text, 80)}」` : '없음';
  });
  await take('첫 3줄', async () => {
    const b = (await rowsOf()).filter((r) => !r.head).slice(0, 3);
    return b.length ? b.map((r, k) => `${k + 1}「${clean(r.text, 80)}」`).join(' ') : '없음';
  });
  // ── 8차 Z8-4 (iii) 결과 없음 문구(원문 대신 참/거짓) · 쪽 글(있으면 40자)
  await take('결과 없음 문구', async () => (RESULT_NONE.test(String(await dlg.innerText())) ? '참' : '거짓'));
  await take('쪽 글', async () => {
    const p = (await dlg.locator(PAGER).allInnerTexts()).map((x) => String(x).trim()).filter(Boolean);
    return p.length ? `「${clean(p[0], 40)}」` : '없음';
  });
  return part.join(' · ');
}
async function waitListRows(page, dlg, t, pick, wait = LIST_WAIT) {
  const marked = dlg.locator('tr').filter({ hasText: F.MARK.partner });
  try {
    await marked.first().waitFor({ state: 'visible', timeout: wait });
  } catch (e) {
    log(`${t.listTitle} 표지 줄 기다림 끝(${L.errorName(e)}) — 진단 남김`);
  }
  const rows = pick(marked);
  if ((await rows.count()) > 0) return rows;
  throw new S.SetupError(`${t.listTitle} 에 표지 거래처 줄 없음 — ${await listDiag(page, dlg)}`);
}

/** [목록] → 창에서 표지 거래처 줄 두 번 누름 → 창 닫힘. pick = 줄 고르기(판매 반품 ⓙ = 반품 줄). */
async function openFromList(page, t, pick, returnType) {
  await goto(page, t.route);
  if (returnType) await pickSelect(page, '반품유형', returnType);
  await button(page, '목록').first().click({ timeout: WAIT });
  const dlg = page.locator('.mud-dialog').filter({ hasText: t.listTitle }).last();
  await dlg.waitFor({ state: 'visible', timeout: WAIT });
  // ⬛ [낡은 줄 · 1~6차] await sleep(800);
  // ⬛ [낡은 줄 · 1~6차] const rows = pick(dlg.locator('tr').filter({ hasText: F.MARK.partner }));
  // ⬛ [낡은 줄 · 1~6차] if ((await rows.count()) === 0) throw new S.SetupError(`${t.listTitle} 에 표지 거래처 줄 없음`);
  const rows = await waitListRows(page, dlg, t, pick);
  await rows.first().locator('td').filter({ hasText: F.MARK.partner }).first().dblclick();
  await dlg.waitFor({ state: 'hidden', timeout: WAIT }).catch((e) => log(`목록 창 안 닫힘(${L.errorName(e)}) — 계속`));
  await sleep(1200);
}

/** 화면 단추가 보낸 미리보기 요청의 응답 잡기. */
async function catchPreview(page, click, urlPart) {
  const wait = page.waitForResponse((r) => r.request().method() === 'POST' && r.url().includes(urlPart) && r.url().includes('preview-pdf'), { timeout: WAIT });
  await click();
  const resp = await wait;
  const status = resp.status();
  const body = await resp.body();
  return { status, body };
}
async function warnCount(page) {
  return page.locator('.mud-dialog .mud-alert-text-warning, .mud-dialog .mud-alert-filled-warning, .mud-dialog .mud-alert-outlined-warning').count();
}
/** 🔴 3차([3-V] A6 · #15) — 보임 확인이 던지면(창이 죽음 등) 사유 1줄 남기고 「안 보임」 으로 본다(2차 `.catch(() => false)` 3곳). */
async function visibleOr(loc, what, opts) {
  try {
    return await loc.isVisible(opts);
  } catch (e) {
    log(`${what} 보임 확인 실패(${L.errorName(e)}) — 안 보임으로 봄`);
    return false;
  }
}
async function closeDialogs(page) {
  for (let k = 0; k < 3; k++) {
    const dlg = page.locator('.mud-dialog').last();
    // ⬛ [낡은 줄 · 2차] if (!(await dlg.isVisible().catch(() => false))) return;
    if (!(await visibleOr(dlg, '창'))) return;
    const close = dlg.getByRole('button', { name: /^(닫기|취소)$/ }).first();
    // ⬛ [낡은 줄 · 2차] if (await close.isVisible().catch(() => false)) …
    if (await visibleOr(close, '닫기 단추')) await close.click().catch((e) => log(`창 닫기 실패 ${L.errorName(e)}`));
    else await page.keyboard.press('Escape');
    await sleep(500);
  }
}

// ── 판독 입구 ──
async function captureSettings(page, t) {
  await goto(page, '/settings/form-templates');
  const row = page.locator('tr').filter({ hasText: `${F.MARK.testPrefix} ${t.label}` }).first();
  await row.waitFor({ state: 'visible', timeout: WAIT });
  await row.getByRole('button', { name: '수정', exact: true }).click();
  const dlg = page.locator('.mud-dialog').last();
  await dlg.waitFor({ state: 'visible', timeout: WAIT });
  await sleep(800);
  const r = await catchPreview(page, () => dlg.getByRole('button', { name: '미리보기', exact: true }).click(), '/form-templates/');
  return { ...r, ui: {} };
}
// ⬛ [낡은 줄 · 3~9차] async function captureDoc(page, t, entry) {
async function captureDoc(page, t, entry, nos) {
  // ⬛ [낡은 줄 · 2차] doc-j = (rows) => rows.filter({ hasText: '반품' }) · 그 밖 = (rows) => rows([4] F5)
  // ⬛ [낡은 줄 · 3~9차] if (entry === 'doc-j') await openFromList(page, F.TYPES.find((x) => x.key === 'delivery'), entryPick(t.key, entry));
  // ⬛ [낡은 줄 · 3~9차] else await openFromList(page, t, entryPick(t.key, entry), t.returnType);
  // 🔴 10차 D8 ⓓ — nos(D4 번호)로 입구 줄 고르기
  if (entry === 'doc-j') await openFromList(page, F.TYPES.find((x) => x.key === 'delivery'), entryPick(t.key, entry, nos));
  else await openFromList(page, t, entryPick(t.key, entry, nos), t.returnType);
  const r = await catchPreview(page, () => button(page, '인쇄').first().click({ timeout: WAIT }), '/api/documents/');
  await sleep(1200);
  const ui = { warnCount: await warnCount(page) };
  return { ...r, ui };
}
async function editorFacts(page, t) {
  await goto(page, '/settings/form-templates');
  const row = page.locator('tr').filter({ hasText: `${F.MARK.testPrefix} ${t.label}` }).first();
  await row.getByRole('button', { name: '수정', exact: true }).click();
  const dlg = page.locator('.mud-dialog').last();
  await dlg.waitFor({ state: 'visible', timeout: WAIT });
  await sleep(800);
  const coordTable = await dlg.getByRole('button', { name: '칸 추가', exact: true }).count();
  const accepts = await dlg.locator('input[type=file]').evaluateAll((els) => els.map((e) => e.getAttribute('accept') || ''));
  return { coordTableVisible: coordTable > 0, pdfAccept: accepts.some((a) => /\.pdf|application\/pdf/i.test(a)) };
}

// ── 로고(화면) ──
async function uploadLogo(page, file) {
  await goto(page, '/company');
  const input = page.locator('label', { hasText: '로고 등록' }).locator('input[type=file]').first();
  const wait = page.waitForResponse((r) => r.request().method() === 'POST' && r.url().includes('/api/settings/company-image'), { timeout: WAIT });
  await input.setInputFiles(file);
  const resp = await wait;
  if (resp.status() !== 200) throw new S.SetupError(`로고 올리기 응답 ${resp.status()}`);
}
async function deleteLogo(page) {
  await goto(page, '/print-settings');
  const card = page.locator('.mud-card, .mud-paper').filter({ hasText: '회사 로고' }).filter({ has: page.getByRole('button', { name: '삭제', exact: true }) }).last();
  const wait = page.waitForResponse((r) => r.request().method() === 'DELETE' && r.url().includes('/api/settings/company-image'), { timeout: WAIT });
  await card.getByRole('button', { name: '삭제', exact: true }).first().click();
  const confirm = page.locator('.mud-dialog').last().getByRole('button', { name: /^(삭제|확인|예)$/ }).first();
  // ⬛ [낡은 줄 · 2차] if (await confirm.isVisible({ timeout: 3000 }).catch(() => false)) await confirm.click();
  if (await visibleOr(confirm, '로고 지우기 확인 단추', { timeout: 3000 })) await confirm.click();
  const resp = await wait;
  if (resp.status() !== 204 && resp.status() !== 200) throw new S.SetupError(`로고 지우기 응답 ${resp.status()}`);
}

// ── 본문 ──
function cfgKey(tpl) {
  return JSON.stringify(tpl || {});
}

/**
 * 🔴 5차 ⓔ3([3-V] 4차 D1 · [4] 4차 F1 — 자체시험이 이 차림 흐름을 안 돌려 배선 변이가 초록이던 길) — 손 주입.
 *   기본 = 실제 손(아래 그대로 · `node capture.js` 동작 무변). 자체시험 T-Z28 은 가짜 K 응답(가짜 fetch) · 가짜 화면 손 · 메모리 fs 를 넣어
 *   main 본문을 **그대로** 돌린다. main 안 이름(K · S · F · PNG · fs · OUT · log · 화면 손 8)은 주입된 손을 가리키고 본문 글자 · 차례는 4차와 같다.
 */
function realDeps() {
  return {
    K, S, F, PNG, fs, OUT, log,
    loadPlaywright: () => require('playwright'),
    ui: { uploadLogo, deleteLogo, makeDoc, makeSalesReturn, captureSettings, captureDoc, editorFacts, closeDialogs },
  };
}

// ⬛ [낡은 줄 · 1~4차] async function main() {
async function main(deps = realDeps()) {
  // 🔴 5차 ⓔ3 — 주입된 손(이름 같음 · 아래 본문 무변)
  const { K, S, F, PNG, fs, OUT, log } = deps;
  const { uploadLogo, deleteLogo, makeDoc, makeSalesReturn, captureSettings, captureDoc, editorFacts, closeDialogs } = deps.ui;
  fs.mkdirSync(path.join(OUT, 'pdfs'), { recursive: true });
  fs.mkdirSync(path.join(OUT, 'fixture'), { recursive: true });
  const plan = F.plannedCells();
  const cells = plan.cells.map((c) => ({ ...c, status: 'pending' }));
  const doc = {
    schema: 'print-cells/1', run: process.env.PRINT_RUN || '?', job: process.env.PRINT_JOB || '?', board: process.env.PRINT_BOARD || 'main',
    probeSelftestOk: process.env.PRINT_PROBE_OK === '1', positive: F.POSITIVE_CONTROL, groups: plan.groups, setup: {}, cells,
    note: '대표 DB 조건 재현 = 미측정(고정)',
  };
  const save = () => fs.writeFileSync(path.join(OUT, 'cells.json'), JSON.stringify(doc, null, 2));
  const failAll = (pred, reason) => cells.filter((c) => c.status === 'pending' && pred(c)).forEach((c) => { c.status = 'setup-fail'; c.reason = reason; });
  save();

  if (process.env.PRINT_PATCH_FAIL === '1') {
    failAll(() => true, '패치 적용 실패(git apply)');
    save();
    log('차림 불가 — 패치 적용 실패');
    return 0;
  }
  // 문 없음(main) · 기록 칸 먼저 표시
  for (const c of cells) if (c.noDoorAtMain && doc.board === 'main') c.status = 'no-door';

  // ① 저장 문 차림 — 로그인 · 거래처 · 품목
  let sess;
  const itemIds = []; // 🔴 10차 D8 ⓒ — 재고 선행에 품목 id
  const nos = {}; // 🔴 10차 D4 — 종마다 저장 번호(D8 ⓓ 입구 고르기)
  // 🔴 4차 ⓐ(개발명세서 §4-a) — 로그인 직후 약관 동의 차림(로그인 응답 표지가 참일 때만 · 화면과 같은 문) · 결과 = doc.setup.terms
  const terms = {};
  doc.setup.terms = terms;
  try {
    sess = await S.login();
    await S.ensureTerms(sess, terms);
    await S.confirmTerms(sess, terms); // 🔴 5차 ⓔ6([3-V] 4차 D4) — 동의 뒤 GET /api/auth/terms/status 다시 확인 · 아직 필요 = 차림 불가
    await S.createPartner(sess, F.MARK.partner);
    // ⬛ [낡은 줄 · 1~9차] for (const it of F.MARK.items) await S.createItem(sess, it.name, it.price);
    for (const it of F.MARK.items) itemIds.push({ name: it.name, id: await S.createItem(sess, it.name, it.price) }); // 🔴 10차 D8 ⓒ
    doc.setup.master = { ok: true };
  } catch (e) {
    doc.setup.master = { ok: false, why: e.message };
    failAll(() => true, `기초 자료 차림 실패 — ${e.message}`);
    save();
    return 0;
  }
  // 🔴 10차 D8 ⓒ(PM 판단 = API 재고 조정) — 판매 반품용 거래명세서 확정 앞 재고 선행 · 실패 = 판매 반품 칸만 차림 불가(⑤ 에서 이름 붙은 사유) · 다른 칸 계속
  //   창고 id = 기존 차림 값에서만(PM 조건) → 지금 기존 차림(setup-api · fixture)에 창고 값 없음 → 빈 값 = 「창고 id 없음」(명세서 §1-10 남은 것 · PM 결재 대기)
  // ⬛ [낡은 줄 · 10차 1단] const STOCK_WAREHOUSE = ''; — PM 결재 9번 전 빈 값
  try {
    const STOCK_WAREHOUSE = await S.pickWarehouse(sess); // 🔴 10차 2단(PM 결재 9번) — GET /api/warehouses 읽기 1회 · 0개 = 「재고 선행 — 창고 없음」(아래 catch · 판매 반품 칸만)
    doc.setup.stock = await S.ensureStock(sess, itemIds, Math.max(...F.MARK.items.map((x) => Number(x.qty) || 0)) * 10, STOCK_WAREHOUSE);
  } catch (e) {
    doc.setup.stock = { ok: false, why: e.message || L.errorName(e) };
  }

  // ② 종마다 양식 그림 · 경쟁 양식(이름순 앞섬 · 비기본) · 시험 양식(기본) — 슬롯 ㉠
  const tplIds = {};
  const bgPaths = {};
  for (const [i, t] of F.TYPES.entries()) {
    try {
      const bg = PNG.formBgPng(i, t.mark);
      fs.writeFileSync(path.join(OUT, 'fixture', `bg-${t.key}.png`), bg.png);
      bgPaths[t.key] = await S.uploadBackground(sess, bg.png, `bg-${t.key}.png`);
      await S.createTemplate(sess, F.competitorBody(t));
      tplIds[t.key] = await S.createTemplate(sess, F.templateBody(t, i, { paperMode: 'plain' }));
      doc.setup[`tpl.${t.key}`] = { ok: true, bgSha: bg.sha };
    } catch (e) {
      doc.setup[`tpl.${t.key}`] = { ok: false, why: e.message };
      failAll((c) => c.type === t.key, `양식 차림 실패 — ${e.message}`);
    }
  }
  save();

  // ③ 브라우저 · 화면 로그인
  let chromium;
  try {
    // ⬛ [낡은 줄 · 1~4차] ({ chromium } = require('playwright'));
    ({ chromium } = deps.loadPlaywright()); // 🔴 5차 ⓔ3 — 기본 = require('playwright')(realDeps)
  } catch (e) {
    failAll(() => true, `playwright 없음 ${L.errorName(e)}`);
    save();
    return 0;
  }
  const browser = await chromium.launch();
  const context = await browser.newContext({ locale: 'ko-KR', viewport: { width: 1600, height: 1000 } });
  const blocked = {};
  await K.blockExternal(context, blocked, {});
  const page = await context.newPage();
  // 🔴 4차 ⓒ — 끝에 닫을 세션(화면 로그인 전 = API 세션 · 넘김 뒤 = 화면 세션 또는 0) · 화면 뒤 API 차림이 쓰는 세션 = api
  let closeAtEnd = sess;
  let api = null;
  try {
    // ⬛ [낡은 덩이 · 2~3차] API 세션을 연 채 화면 로그인 · 결과(자리 · 응답 상태)를 안 봄 → 한 PC 규칙 409 덫(§4-a 잠복 원인)
    // try {
    //   await K.screenLogin(page, process.env.UI_LOGIN_PARENT || 'ci-parent', process.env.UI_PW_PARENT);
    // } catch (e) {
    //   failAll(() => true, `화면 로그인 실패 ${L.errorName(e)}`);
    //   save();
    //   return 0;
    // }
    // 🔴 4차 ⓒ — ① API 로그아웃 → ② 토큰 가로채기 · 로그인 응답 지켜보기 → ③ 화면 로그인 → ④ 응답 · 토큰 가림 기다림 → ⑤ 판정(setup-api.js handoffToScreen)
    const h = await S.handoffToScreen({
      sess, context, page, loginId: process.env.UI_LOGIN_PARENT || 'ci-parent', password: process.env.UI_PW_PARENT,
    });
    doc.setup.apiLogout = h.apiLogout;
    doc.setup.screenLogin = h.verdict;
    closeAtEnd = h.tok && h.tok.access ? S.screenSession(h.tok) : null;
    log(`API 로그아웃(화면 로그인 전) ${h.apiLogout ? h.apiLogout.got : '-'} · ${h.verdict.why}`);
    if (!h.verdict.ok) {
      failAll(() => true, h.verdict.why);
      save();
      return 0;
    }
    api = h.api;

    // ④ 로고(화면) — 회사정보 「로고 등록」
    const logo = plan.logo;
    const logoFile = path.join(OUT, 'fixture', 'logo.png');
    fs.writeFileSync(logoFile, logo.png);
    try {
      await uploadLogo(page, logoFile);
      // ⬛ [낡은 줄 · 2~3차] const st = await S.logoState(sess);
      const st = await S.logoState(api);
      doc.setup.logo = { ok: true, sha: logo.sha, state: st };
    } catch (e) {
      doc.setup.logo = { ok: false, why: e.message || L.errorName(e) };
      failAll((c) => /^WM-/.test(c.k), `로고 차림 실패 — ${e.message || L.errorName(e)}`);
    }

    // ⑤ 전표(화면) — 7종(판매 반품 = 거래명세서 [반품하기])
    for (const t of F.TYPES) {
      if (cells.every((c) => c.type !== t.key || c.status !== 'pending')) continue;
      try {
        // ⬛ [낡은 줄 · 1~9차] if (t.key === 'sales_return') await makeSalesReturn(page);
        // ⬛ [낡은 줄 · 1~9차] else await makeDoc(page, t);
        // ⬛ [낡은 줄 · 1~9차] doc.setup[`doc.${t.key}`] = { ok: true };
        // 🔴 10차 D4 · D8 — 저장 번호 기록 · 판매 반품 = 번호 · 재고 선행 결과를 넘김(가짜 손이 값을 안 돌리면 옛 기록 꼴 그대로)
        const v = t.key === 'sales_return' ? await makeSalesReturn(page, nos, doc.setup.stock) : await makeDoc(page, t);
        if (v && v.ok) nos[t.key] = v;
        doc.setup[`doc.${t.key}`] = v && v.ok ? { ok: true, no: v.no, docNo: v.docNo, id: v.id, auto: clean1(v.auto, 40), status: v.status, dialog: v.dialog } : { ok: true };
      } catch (e) {
        doc.setup[`doc.${t.key}`] = { ok: false, why: e.message || L.errorName(e) };
        failAll((c) => c.type === t.key && c.path !== 'settings' && c.path !== 'editor', `전표 차림 실패 — ${e.message || L.errorName(e)}`);
        await closeDialogs(page);
      }
    }
    save();

    // ⑥ 판독 — 차림(양식 한 행)을 저장 문으로 바꿔 가며 · 칸마다 화면 단추 응답
    let n = 0;
    const order = ['가', '나'];
    for (const phase of [null, ...order]) {
      if (phase === '나') {
        try {
          await deleteLogo(page);
          // ⬛ [낡은 줄 · 2~3차] doc.setup.logoDeleted = { ok: true, state: await S.logoState(sess) };
          doc.setup.logoDeleted = { ok: true, state: await S.logoState(api) }; // 🔴 4차 ⓒ 화면 세션
        } catch (e) {
          doc.setup.logoDeleted = { ok: false, why: e.message || L.errorName(e) };
          failAll((c) => c.logoPhase === '나', `로고 지우기 차림 실패 — ${e.message || L.errorName(e)}`);
        }
      }
      const phaseCells = cells.filter((c) => c.status === 'pending' && (c.logoPhase || null) === phase && c.tpl);
      const byCfg = new Map();
      for (const c of phaseCells) {
        const key = `${c.type}|${cfgKey(c.tpl)}`;
        if (!byCfg.has(key)) byCfg.set(key, []);
        byCfg.get(key).push(c);
      }
      for (const [key, group] of byCfg) {
        const t = F.TYPES.find((x) => x.key === group[0].type);
        const i = F.TYPES.indexOf(t);
        const tpl = group[0].tpl;
        try {
          const over = { ...tpl, bgPath: tpl.bg ? bgPaths[t.key] : '' };
          // ⬛ [낡은 줄 · 2~3차] if (tpl.via === 'preprint') await S.updateTemplate(sess, tplIds[t.key], F.templateBody(t, i, { ...over, paperMode: 'preprint' }));
          // ⬛ [낡은 줄 · 2~3차] await S.updateTemplate(sess, tplIds[t.key], F.templateBody(t, i, over));
          // ⬛ [낡은 줄 · 2~3차] const sc = await S.savedCoordCount(sess, t.key, tplIds[t.key]);
          // 🔴 4차 ⓒ — 아래 셋 = 화면 세션(api)
          if (tpl.via === 'preprint') await S.updateTemplate(api, tplIds[t.key], F.templateBody(t, i, { ...over, paperMode: 'preprint' }));
          await S.updateTemplate(api, tplIds[t.key], F.templateBody(t, i, over));
          if (tpl.coords) {
            const sc = await S.savedCoordCount(api, t.key, tplIds[t.key]);
            if (sc.n !== F.FIELDS.length) throw new S.SetupError(`저장된 칸 수 ${sc.n} ≠ N ${F.FIELDS.length}`);
          }
        } catch (e) {
          group.forEach((c) => { c.status = 'setup-fail'; c.reason = `양식 바꾸기 실패 — ${e.message || L.errorName(e)}`; });
          continue;
        }
        for (const c of group) {
          n += 1;
          try {
            // ⬛ [낡은 줄 · 1~9차] const r = c.path === 'settings' ? await captureSettings(page, t) : await captureDoc(page, t, c.path);
            const r = c.path === 'settings' ? await captureSettings(page, t) : await captureDoc(page, t, c.path, nos); // 🔴 10차 D8 ⓓ
            c.ui = r.ui;
            c.http = { status: r.status };
            if (r.status === 200 && r.body.subarray(0, 5).toString('latin1') === '%PDF-') {
              c.pdf = path.join('pdfs', `${safeName(c.id, n)}.pdf`);
              fs.writeFileSync(path.join(OUT, c.pdf), r.body);
              c.status = 'captured';
            } else {
              c.status = 'capture-fail';
              c.http.firstLine = L.sanitize(r.body.toString('utf8').split('\n')[0], 160);
              c.reason = `응답 ${r.status}`;
            }
          } catch (e) {
            c.status = c.status === 'pending' ? 'capture-fail' : c.status;
            c.reason = c.reason || `${e instanceof S.SetupError ? '차림' : '판독 입구'} ${L.errorName(e)} ${L.sanitize(e.message || '', 120)}`;
            if (e instanceof S.SetupError) c.status = 'setup-fail';
          }
          await closeDialogs(page);
          if (n % 10 === 0) save();
        }
        log(`판독 ${key.split('|')[0]} 차림 ${group.length}칸 끝`);
      }
    }
    // ⑦ S 칸 · 기록(편집창 빈 A4 — 좌표표 · PDF 받기 보이나)
    for (const c of cells.filter((x) => x.status === 'pending' && x.path === 'editor')) {
      try {
        const t = F.TYPES.find((x) => x.key === c.type);
        // ⬛ [낡은 줄 · 2~3차] await S.updateTemplate(sess, tplIds[t.key], F.templateBody(t, F.TYPES.indexOf(t), { paperMode: 'plain' }));
        await S.updateTemplate(api, tplIds[t.key], F.templateBody(t, F.TYPES.indexOf(t), { paperMode: 'plain' })); // 🔴 4차 ⓒ 화면 세션
        c.ui = await editorFacts(page, t);
        c.status = 'recorded';
      } catch (e) {
        c.status = 'capture-fail';
        c.reason = `편집창 기록 실패 ${L.errorName(e)}`;
      }
      await closeDialogs(page);
    }
    for (const c of cells) if (c.status === 'pending') { c.status = 'unmeasured'; c.reason = '차림 순서에서 빠짐'; }
    doc.setup.externalBlocked = blocked.external_blocked || 0;
  } finally {
    save();
    await browser.close().catch((e) => log(`브라우저 닫기 실패 ${L.errorName(e)}`));
    // ⬛ [낡은 줄 · 2~3차] await K.apiLogout(sess);
    // 🔴 4차 ⓒ — 열린 세션 하나만 닫음(넘김 전 = API 세션 · 넘김 뒤 = 화면 세션 · 넘김에서 이미 닫은 API 세션은 다시 안 닫음)
    doc.setup.logoutEnd = await K.apiLogout(closeAtEnd);
    save();
  }
  log(`차림 · 판독 입구 끝 — 칸 ${cells.length} · PDF ${cells.filter((c) => c.status === 'captured').length} · 차림 불가 ${cells.filter((c) => c.status === 'setup-fail').length} · 못 받음 ${cells.filter((c) => c.status === 'capture-fail').length}`);
  return 0;
}

if (require.main === module) {
  main().then((code) => { process.exitCode = code; }, (e) => {
    console.log(`⚫ FAIL [장치] 차림 · 판독 입구 예외 ${L.errorName(e)}`);
    process.exitCode = 1;
  });
}

// ⬛ [낡은 줄 · 3~4차] module.exports = { safeName, cfgKey, entryPick, visibleOr };
// ⬛ [낡은 줄 · 5~6차] module.exports = { safeName, cfgKey, entryPick, visibleOr, main, realDeps }; // 🔴 5차 ⓔ3 — 배선 시험(T-Z28)이 main 을 가짜 손으로 돌림
// ⬛ [낡은 줄 · 7~9차] module.exports = { safeName, cfgKey, entryPick, visibleOr, main, realDeps, QTY_HEADERS, qtyColumn, waitListRows, listDiag, LIST_WAIT }; // 🔴 7차 — R-3 · R-4 시험(T-Z35 · T-Z36)
module.exports = {
  safeName, cfgKey, entryPick, visibleOr, main, realDeps, QTY_HEADERS, qtyColumn, waitListRows, listDiag, LIST_WAIT,
  // 🔴 10차 — D4 · D8 시험(T-D4a · T-D4b · T-D8a~d · T-D5)
  SAVE_OK, CONFIRM_OK, numberFrom, saveVerdict, saveDoc, pickByNo, confirmVerdict, makeSalesReturn,
};
