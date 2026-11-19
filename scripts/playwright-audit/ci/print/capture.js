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
async function saveDoc(page, what) {
  const before = await errorToasts(page);
  await button(page, '저장').first().click({ timeout: WAIT });
  await sleep(2500);
  const after = await errorToasts(page);
  if (after > before) throw new S.SetupError(`${what} 저장 뒤 경고 · 오류 알림 ${after - before}`);
}

/** 화면별 전표 1건(거래처 · 품목 2줄) — 화면으로 저장. */
async function makeDoc(page, t) {
  await goto(page, t.route);
  if (t.returnType === '매입반품') await pickSelect(page, '반품유형', '매입반품');
  await pickAutocomplete(page, inputByLabel(page, t.partnerLabel), F.MARK.partner);
  await fillLines(page);
  await saveDoc(page, t.label);
}
/**
 * 🔴 3차(11/19 · [4] F5) — 판독 입구 줄 고르기. 판매 반품을 거래명세서 [반품하기]로 만들므로 `/deliveries` 목록엔
 *   표지 거래처 줄이 둘(원 전표 · 반품). 2차는 거래명세서 입구가 `rows => rows` 의 첫 줄이라 목록 정렬에 따라 반품 줄을 집을 수 있었다.
 *   → 거래명세서 일반 입구 = 「반품」 글자 줄 **뺌**(정렬에 기대지 않음) · ⓙ = 「반품」 글자 줄만 · 그 밖 = 그대로.
 */
function entryPick(typeKey, entry) {
  if (entry === 'doc-j') return (rows) => rows.filter({ hasText: '반품' });
  if (typeKey === 'delivery') return (rows) => rows.filter({ hasNotText: '반품' });
  return (rows) => rows;
}

/** 판매 반품 — 거래명세서 저장(확정) → [반품하기] → 반품 사유 → 저장(DeliveryPage.razor:109 · :1234 · :289). */
async function makeSalesReturn(page) {
  // ⬛ [낡은 줄 · 2차] await openFromList(page, F.TYPES.find((x) => x.key === 'delivery'), (row) => row);
  await openFromList(page, F.TYPES.find((x) => x.key === 'delivery'), entryPick('delivery', 'doc'));
  const ret = button(page, '반품하기').first();
  if (await ret.isDisabled()) throw new S.SetupError('[반품하기] 꺼짐(거래명세서 확정 아님 · :109)');
  await ret.click();
  await page.waitForURL(/returnOf=/, { timeout: WAIT });
  await page.locator('.mud-main-content, main').first().waitFor({ timeout: WAIT });
  await sleep(1500);
  await pickSelect(page, '반품 사유', '고객 단순 반품');
  await saveDoc(page, '판매 반품');
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
    const v = await page.locator('input').evaluateAll((els) => els.filter((x) => !x.closest('.mud-dialog')).map((x) => x.value || '').filter((x) => /\d{4}-\d{2}-\d{2}/.test(x)).slice(0, 3));
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
async function captureDoc(page, t, entry) {
  // ⬛ [낡은 줄 · 2차] doc-j = (rows) => rows.filter({ hasText: '반품' }) · 그 밖 = (rows) => rows([4] F5)
  if (entry === 'doc-j') await openFromList(page, F.TYPES.find((x) => x.key === 'delivery'), entryPick(t.key, entry));
  else await openFromList(page, t, entryPick(t.key, entry), t.returnType);
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
  // 🔴 4차 ⓐ(개발명세서 §4-a) — 로그인 직후 약관 동의 차림(로그인 응답 표지가 참일 때만 · 화면과 같은 문) · 결과 = doc.setup.terms
  const terms = {};
  doc.setup.terms = terms;
  try {
    sess = await S.login();
    await S.ensureTerms(sess, terms);
    await S.confirmTerms(sess, terms); // 🔴 5차 ⓔ6([3-V] 4차 D4) — 동의 뒤 GET /api/auth/terms/status 다시 확인 · 아직 필요 = 차림 불가
    await S.createPartner(sess, F.MARK.partner);
    for (const it of F.MARK.items) await S.createItem(sess, it.name, it.price);
    doc.setup.master = { ok: true };
  } catch (e) {
    doc.setup.master = { ok: false, why: e.message };
    failAll(() => true, `기초 자료 차림 실패 — ${e.message}`);
    save();
    return 0;
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
        if (t.key === 'sales_return') await makeSalesReturn(page);
        else await makeDoc(page, t);
        doc.setup[`doc.${t.key}`] = { ok: true };
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
            const r = c.path === 'settings' ? await captureSettings(page, t) : await captureDoc(page, t, c.path);
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
module.exports = { safeName, cfgKey, entryPick, visibleOr, main, realDeps, QTY_HEADERS, qtyColumn, waitListRows, listDiag, LIST_WAIT }; // 🔴 7차 — R-3 · R-4 시험(T-Z35 · T-Z36)
