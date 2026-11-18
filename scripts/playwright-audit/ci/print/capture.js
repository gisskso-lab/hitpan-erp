'use strict';
// W-G1-51 갈래 Z — 화면 차림 · 판독 입구 (개발명세서 §3 ④ ⑤ · 설계 §1-2 · §1-3 F7)
//   🔴 판독 입구 = 화면 단추가 보낸 요청의 응답만(API 직접 호출 0):
//      ② 양식정보설정 → [수정] → [미리보기] 가 보낸 POST …/form-templates/preview-pdf(FormTemplateController.cs:126)
//      ③ 전표 화면 [목록] → 거래 두 번 누름 → [인쇄] 가 보낸 POST …/documents/{type}/{id}/preview-pdf(DocumentController.cs:816)
//   🔴 로고 · 전표 차림 = 화면으로(설계 글자 · PM §6-a 4): 회사정보 화면 「로고 등록」 · 화면별 전표 저장 · 판매 반품 = [반품하기]
//      로고 지우기 = 인쇄 설정 화면 회사 로고 「삭제」(회사정보 화면엔 지우기 단추 없음 · PrintSettingsPage.razor:55~72)
//   🔴 양식 = 저장 문(setup-api.js) · 차림 실패 = 「차림 불가(사유)」 · PDF 못 받음 · 시간 넘김 = 「미측정」(판매 반품 = 오류가 곧 결함 · C8)
//   🔴 이 PC 에서 돌리지 않는다 — CI 시험 잡(ui-print)에서만(이 PC = 실물 정식설치 PC)
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

/** 전표 줄 2개 — [추가] 두 번 → 줄마다 품명(자동완성) · 수량. */
async function fillLines(page) {
  const headers = await page.locator('th').allInnerTexts();
  const qtyCol = headers.findIndex((h) => h.trim() === '수량');
  if (qtyCol < 0) throw new S.SetupError('품목표에 「수량」 머리 없음');
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

/** [목록] → 창에서 표지 거래처 줄 두 번 누름 → 창 닫힘. pick = 줄 고르기(판매 반품 ⓙ = 반품 줄). */
async function openFromList(page, t, pick, returnType) {
  await goto(page, t.route);
  if (returnType) await pickSelect(page, '반품유형', returnType);
  await button(page, '목록').first().click({ timeout: WAIT });
  const dlg = page.locator('.mud-dialog').filter({ hasText: t.listTitle }).last();
  await dlg.waitFor({ state: 'visible', timeout: WAIT });
  await sleep(800);
  const rows = pick(dlg.locator('tr').filter({ hasText: F.MARK.partner }));
  if ((await rows.count()) === 0) throw new S.SetupError(`${t.listTitle} 에 표지 거래처 줄 없음`);
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

async function main() {
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
  try {
    sess = await S.login();
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
    ({ chromium } = require('playwright'));
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
  try {
    try {
      await K.screenLogin(page, process.env.UI_LOGIN_PARENT || 'ci-parent', process.env.UI_PW_PARENT);
    } catch (e) {
      failAll(() => true, `화면 로그인 실패 ${L.errorName(e)}`);
      save();
      return 0;
    }

    // ④ 로고(화면) — 회사정보 「로고 등록」
    const logo = plan.logo;
    const logoFile = path.join(OUT, 'fixture', 'logo.png');
    fs.writeFileSync(logoFile, logo.png);
    try {
      await uploadLogo(page, logoFile);
      const st = await S.logoState(sess);
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
          doc.setup.logoDeleted = { ok: true, state: await S.logoState(sess) };
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
          if (tpl.via === 'preprint') await S.updateTemplate(sess, tplIds[t.key], F.templateBody(t, i, { ...over, paperMode: 'preprint' }));
          await S.updateTemplate(sess, tplIds[t.key], F.templateBody(t, i, over));
          if (tpl.coords) {
            const sc = await S.savedCoordCount(sess, t.key, tplIds[t.key]);
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
        await S.updateTemplate(sess, tplIds[t.key], F.templateBody(t, F.TYPES.indexOf(t), { paperMode: 'plain' }));
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
    await K.apiLogout(sess);
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

module.exports = { safeName, cfgKey, entryPick, visibleOr };
