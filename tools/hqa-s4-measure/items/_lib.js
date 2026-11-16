'use strict';
// 측정 공용 — 읽기 · 신뢰 입력 도우미 · 회로 신호 수집 (설계 §4-3 · §4-4).
// · 입력은 page.keyboard · page.mouse · locator 동작(CDP 신뢰 입력)만 쓴다.
// · page.evaluate 는 이 파일에만 있고 전부 읽기 전용이다(정적 검사 = selftest-judge.js 가 확인).
// · 시험 앱에 스크립트 주입 0. 읽기는 측정 시점에만.

function errText(e) {
  const s = String(e && e.message ? e.message : e);
  return s.split('\n')[0].slice(0, 300);
}

function settleMs(rules, delay, extra) {
  const s = rules.device.settle;
  return s.baseMs + s.perDelayFactor * delay + (extra || 0);
}

// ── 회로 신호 3종 수집 ──────────────────────────────────────────────
function attachSignals(page, rules) {
  const t0 = Date.now();
  const sig = {
    t0, wsOpened: 0, wsFrames: 0, wsCloses: [], consoleMatches: [], consoleErrors: 0,
    pageErrors: [], navs: 0, navsAtConnect: null, connectedAt: null, reconnectSeen: [], ending: false
  };
  const re = new RegExp(rules.device.circuit.consolePattern, 'i');
  const part = rules.device.circuit.wsPathPart;
  page.on('websocket', (ws) => {
    if (!ws.url().includes(part)) return;
    sig.wsOpened += 1;
    ws.on('framereceived', () => {
      sig.wsFrames += 1;
      if (sig.connectedAt === null) { sig.connectedAt = Date.now(); sig.navsAtConnect = sig.navs; }
    });
    ws.on('close', () => {
      if (!sig.ending) sig.wsCloses.push({ at: Date.now(), navsSoFar: sig.navs });
    });
  });
  page.on('console', (m) => {
    if (m.type() !== 'error') return;
    sig.consoleErrors += 1;
    const t = m.text();
    if (re.test(t)) sig.consoleMatches.push({ at: Date.now(), text: t.slice(0, 160) });
  });
  page.on('pageerror', (e) => { sig.pageErrors.push({ at: Date.now(), text: errText(e).slice(0, 160) }); });
  page.on('framenavigated', (f) => { if (f === page.mainFrame()) sig.navs += 1; });
  return sig;
}

// 입력 시작(fromAt) 뒤의 신호만 「죽음」 으로 센다. 입력 전 신호는 따로 센다.
function circuitSince(ctx, fromAt) {
  const sig = ctx.sig;
  const nav0 = sig.navsAtConnect;
  const wsAfter = sig.wsCloses.filter((c) => c.at >= fromAt && c.navsSoFar === nav0);
  const wsNav = sig.wsCloses.filter((c) => c.at >= fromAt && c.navsSoFar !== nav0);
  const conAfter = sig.consoleMatches.filter((c) => c.at >= fromAt);
  const recAfter = sig.reconnectSeen.filter((r) => r.at >= fromAt);
  const rel = (x) => x - sig.t0;
  return {
    reconnectVisible: recAfter.length > 0,
    wsClosedNotNav: wsAfter.length > 0,
    consoleCircuitError: conAfter.length > 0,
    dead: recAfter.length > 0 || wsAfter.length > 0 || conAfter.length > 0,
    detail: {
      reconnectSeenAtMs: recAfter.map((r) => rel(r.at)).slice(0, 5),
      wsCloseAtMs: wsAfter.map((c) => rel(c.at)),
      wsCloseDuringNavAtMs: wsNav.map((c) => rel(c.at)),
      consoleCircuit: conAfter.slice(0, 3).map((c) => ({ atMs: rel(c.at), text: c.text })),
      pageErrors: sig.pageErrors.filter((p) => p.at >= fromAt).slice(0, 3).map((p) => ({ atMs: rel(p.at), text: p.text })),
      consoleErrorsTotal: sig.consoleErrors,
      fromMs: rel(fromAt)
    }
  };
}

// ── 읽기(읽기 전용) ─────────────────────────────────────────────────
async function readState(ctx) {
  const s = ctx.rules.device.sel;
  const refRow = String(ctx.rules.device.kbd.refRow);
  const st = await ctx.page.evaluate((a) => {
    const q = (x) => document.querySelector(x);
    const txt = (el) => (el ? String(el.textContent || '').trim() : null);
    const vis = (el) => {
      if (!el) return false;
      const cs = getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden') return false;
      const r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    };
    // 고침2 N1: afterRef = 기준 줄(1번) 상태 칸보다 문서 순서상 뒤(그 안은 아님 · 요소 목록 순번 비교) · body = 초점이 문서 몸에 머묾 · tabIndex = el.tabIndex
    const ref = q('[data-row="' + a.refRow + '"]');
    const afterRefOf = (el) => {
      if (!ref || el === ref || ref.contains(el)) return false;
      const all = Array.from(document.querySelectorAll('*'));
      return all.indexOf(el) > all.indexOf(ref);
    };
    const describe = (el) => {
      if (!el || el === document.body || el === document.documentElement) {
        return { tag: el ? el.tagName.toLowerCase() : null, id: null, role: null, row: null, inOpenList: false, isStatusActivator: false, text: null, afterRef: false, body: true, tabIndex: null };
      }
      const rowEl = el.closest('[data-row]');
      const role = el.getAttribute('role');
      const tag = el.tagName.toLowerCase();
      const t = txt(el);
      return {
        tag,
        id: el.id || null,
        role,
        row: rowEl ? rowEl.getAttribute('data-row') : null,
        inOpenList: !!el.closest(a.openList),
        isStatusActivator: !!rowEl && (tag === 'button' || role === 'button'),
        text: t ? t.slice(0, 60) : null,
        afterRef: afterRefOf(el),
        body: false,
        tabIndex: typeof el.tabIndex === 'number' ? el.tabIndex : null
      };
    };
    const open = Array.from(document.querySelectorAll(a.openList)).filter(vis);
    const rows = {};
    for (const r of document.querySelectorAll('[data-row]')) {
      rows[r.getAttribute('data-row')] = { kind: r.getAttribute('data-status-kind'), chip: txt(r.querySelector(a.chip)) };
    }
    const det = q('#detail');
    const rc = q(a.reconnect);
    const root = q('#fixture-root');
    const css = q('link[data-fixture-css]');
    return {
      variant: root ? root.getAttribute('data-variant') : null,
      cssVariant: css ? css.getAttribute('data-fixture-css') : null,
      openCount: open.length,
      focus: describe(document.activeElement),
      pickCount: Number(txt(q('#pick-count'))),
      picks: txt(q('#picks')),
      rowclickCount: Number(txt(q('#rowclick-count'))),
      rowclicks: txt(q('#rowclicks')),
      detailOpen: det ? det.getAttribute('data-open') : null,
      detailRow: det ? det.getAttribute('data-row') : null,
      rows,
      reconnect: rc ? { present: true, visible: vis(rc), cls: String(rc.className || '').slice(0, 120) } : { present: false, visible: false, cls: '' },
      viewport: { w: window.innerWidth, h: window.innerHeight }
    };
  }, { openList: s.openList, chip: s.statusChip, reconnect: s.reconnect, refRow });
  if (st.reconnect.visible) ctx.sig.reconnectSeen.push({ at: Date.now() });
  return st;
}

// 고침2 N1 R1 — 줄 안 후보 요소 구조(읽기만 · 입력 0). 값 = 태그 · role · tabindex 속성 · tabIndex · display · 사각형 수 · mud-/bo- 클래스 이름만.
async function readRowStruct(ctx, rowId) {
  return ctx.page.evaluate((a) => {
    const cell = document.querySelector('[data-row="' + a.row + '"]');
    if (!cell) return { row: a.row, present: false, candidates: [], focusableVisible: null, activator: { present: false, display: null, rects: null } };
    const info = (el) => ({
      tag: el.tagName.toLowerCase(),
      role: el.getAttribute('role'),
      tabindexAttr: el.getAttribute('tabindex'),
      tabIndex: typeof el.tabIndex === 'number' ? el.tabIndex : null,
      display: getComputedStyle(el).display,
      rects: el.getClientRects().length,
      cls: Array.from(el.classList).filter((c) => c.indexOf('mud-') === 0 || c.indexOf('bo-') === 0).slice(0, 8)
    });
    const cand = Array.from(cell.querySelectorAll('button, [role=button], [tabindex], .mud-menu-activator')).slice(0, 20).map(info);
    const focusableVisible = Array.from(cell.querySelectorAll('*')).filter((el) => el.tabIndex >= 0 && el.getClientRects().length > 0).length;
    const act = cell.querySelector('.mud-menu-activator');
    const actInfo = act ? info(act) : null;
    return {
      row: a.row, present: true, candidates: cand, focusableVisible,
      activator: act ? { present: true, display: actInfo.display, rects: actInfo.rects } : { present: false, display: null, rects: null }
    };
  }, { row: String(rowId) });
}

// H4 §7-2 — I-8(T3) 줄 읽기(읽기만 · 입력 0). 줄 = [data-row] 의 data-status-kind 가 rules.device.i8.kinds 값인 줄(줄 번호는 화면에서 읽음).
//   줄마다: 상태 칸 안 단추 수 · 보이는 단추 수(사각형 > 0 · 넓이 > 0) · 가장 큰 단추 넓이 · 숨김 클래스 요소 수 · 탭 정지(tabIndex ≥ 0 · 보임) 수 · 칩 글자
//   클래스 이름 · id 는 남기지 않는다(수 · 참거짓만)
async function readBlankRows(ctx) {
  const d = ctx.rules.device;
  return ctx.page.evaluate((a) => {
    const txt = (el) => (el ? (el.textContent || '').trim() : null);
    const area = (el) => { const r = el.getBoundingClientRect(); return el.getClientRects().length > 0 ? Math.round(r.width * r.height) : 0; };
    const out = [];
    for (const r of document.querySelectorAll('[data-row]')) {
      const kind = r.getAttribute('data-status-kind');
      if (!a.kinds.includes(kind)) continue;
      const btns = Array.from(r.querySelectorAll('button, [role=button]'));
      const areas = btns.map(area);
      out.push({
        row: r.getAttribute('data-row'), kind,
        buttons: btns.length,
        visibleButtons: areas.filter((x) => x > 0).length,
        maxButtonArea: areas.length ? Math.max.apply(null, areas) : 0,
        hidden: r.querySelectorAll(a.hiddenSel).length,
        tabStops: Array.from(r.querySelectorAll('*')).filter((el) => el.tabIndex >= 0 && area(el) > 0).length,
        chip: txt(r.querySelector(a.chip))
      });
    }
    return out;
  }, { kinds: d.i8.kinds, hiddenSel: d.i8.hiddenSel, chip: d.sel.statusChip });
}

async function readRects(ctx) {
  const s = ctx.rules.device.sel;
  return ctx.page.evaluate((a) => {
    const vis = (el) => {
      const cs = getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden') return false;
      const r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    };
    const box = (r) => ({ x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) });
    const lists = Array.from(document.querySelectorAll(a.openList)).filter(vis).map((el) => box(el.getBoundingClientRect()));
    const rows = {};
    for (const r of document.querySelectorAll('[data-row]')) {
      const tr = r.closest('tr');
      const td0 = tr ? tr.querySelector('td') : null;
      rows[r.getAttribute('data-row')] = {
        row: tr ? box(tr.getBoundingClientRect()) : null,
        firstCell: td0 ? box(td0.getBoundingClientRect()) : null,
        statusCell: box(r.getBoundingClientRect())
      };
    }
    return { lists, rows, viewport: { w: window.innerWidth, h: window.innerHeight } };
  }, { openList: s.openList });
}

function slim(st) {
  return {
    openCount: st.openCount, focus: st.focus, pickCount: st.pickCount, picks: st.picks,
    rowclickCount: st.rowclickCount, rowclicks: st.rowclicks, detailOpen: st.detailOpen, detailRow: st.detailRow,
    reconnect: st.reconnect
  };
}

// 상태 칸 글자가 바뀐 줄(exceptRow 는 뺌)
function rowsDiff(a, b, exceptRow) {
  const out = [];
  for (const id of Object.keys(b.rows)) {
    if (exceptRow !== undefined && id === String(exceptRow)) continue;
    const x = a.rows[id];
    const y = b.rows[id];
    if (!x || x.chip !== y.chip || x.kind !== y.kind) out.push({ row: id, before: x ? x.chip : null, after: y.chip });
  }
  return out;
}

// #picks 에 새로 붙은 「줄번호:값」 들
function newPicks(a, b) {
  const sp = (s) => (s ? s.split(',').filter((x) => x.length > 0) : []);
  const pa = sp(a.picks);
  const pb = sp(b.picks);
  return pb.slice(pa.length).map((p) => {
    const i = p.indexOf(':');
    return { row: i >= 0 ? p.slice(0, i) : p, value: i >= 0 ? p.slice(i + 1) : null };
  });
}

function overlapArea(r1, r2) {
  if (!r1 || !r2) return 0;
  const w = Math.min(r1.x + r1.w, r2.x + r2.w) - Math.max(r1.x, r2.x);
  const h = Math.min(r1.y + r1.h, r2.y + r2.h) - Math.max(r1.y, r2.y);
  return w > 0 && h > 0 ? w * h : 0;
}

// ── 신뢰 입력(기록과 함께) ────────────────────────────────────────────
function note(ctx, rec) { ctx.inputs.push(Object.assign({ atMs: Date.now() - ctx.sig.t0 }, rec)); }

async function press(ctx, key) { note(ctx, { k: 'press', key }); await ctx.page.keyboard.press(key); }
async function down(ctx, key) { note(ctx, { k: 'down', key }); await ctx.page.keyboard.down(key); }
async function up(ctx, key) { note(ctx, { k: 'up', key }); await ctx.page.keyboard.up(key); }
async function mouseClick(ctx, x, y) { note(ctx, { k: 'mouse.click', x, y }); await ctx.page.mouse.click(x, y); }
async function locatorClick(ctx, selector) { note(ctx, { k: 'locator.click', selector }); await ctx.page.locator(selector).first().click(); }
async function wait(ctx, ms) { await ctx.page.waitForTimeout(ms); }

// 기다리며 상태를 거듭 읽는다(목록 최대 열림 수 · 재연결 표시 관찰)
async function settle(ctx, ms) {
  const end = Date.now() + ms;
  let maxOpen = 0;
  let last = null;
  do {
    await wait(ctx, ctx.rules.device.pollMs);
    last = await readState(ctx);
    if (last.openCount > maxOpen) maxOpen = last.openCount;
  } while (Date.now() < end);
  return { state: last, maxOpen };
}

async function waitOpen(ctx, wantOpen) {
  const end = Date.now() + ctx.rules.device.openWaitMs + 2 * ctx.cell.delay;
  let st = await readState(ctx);
  while ((wantOpen ? st.openCount === 0 : st.openCount > 0) && Date.now() < end) {
    await wait(ctx, ctx.rules.device.pollMs);
    st = await readState(ctx);
  }
  return st;
}

// ── 고침6 c2(결-2 · 결-3 · 결-4 · PM 결재 11/16) — I-4 HQA 절차(#850 댓글 6709803008 L46): 새 함수만 ──
//   옛 waitOpen · settle · settleMs 의 시그니처 · 본문은 무변(자체시험이 소스 글자 · 반사로 부름).
//   시계 · 읽기 · 기다림은 모듈 칸(module.exports.nowMs · readState · wait)으로 부른다 —
//   자체시험이 이 칸에 가짜 시계 · 읽기 순서를 끼워 i4 를 실제로 돌린다(브라우저 · 망 0 · 「게이트는 글자가 아니라 동작」).
function nowMs() { return Date.now(); }
function lib() { return module.exports; }

// 열린 메뉴 관측(원문 「열린 메뉴 관측」) — 간격 pollMs 로 읽다가 처음 열린 목록이 보인 때를 돌려준다.
//   상한 = openWaitMs + 2×지연(옛 waitOpen 과 같은 상한) · 못 보면 observed=false(부르는 쪽이 Esc 를 누르지 않는다 · 결-4)
//   observedAtMs = 그 읽기가 끝난 시각(sig.t0 기준 ms)
async function waitOpenObserved(ctx, pollMs) {
  const M = lib();
  const end = M.nowMs() + ctx.rules.device.openWaitMs + 2 * ctx.cell.delay;
  let st = await M.readState(ctx);
  while (st.openCount === 0 && M.nowMs() < end) {
    await M.wait(ctx, pollMs);
    st = await M.readState(ctx);
  }
  const observed = st.openCount > 0;
  return { state: st, observed, observedAtMs: observed ? M.nowMs() - ctx.sig.t0 : null };
}

// Tab 경로 한 칸(원자료 · 값 0 — 태그 · role · 줄 번호 · 참거짓 · tabIndex 만)
function pathStep(i, f, rowId) {
  return {
    i, row: f.row, tag: f.tag, role: f.role, tabIndex: f.tabIndex,
    inRef: f.row === String(rowId), isStatusActivator: f.isStatusActivator, afterRef: f.afterRef, body: f.body
  };
}

// #before 를 마우스로 누른 뒤 Tab 만으로 그 줄 상태 단추까지 간다.
// 고침2 N1-b: 못 가도 throw 하지 않는다 — { reached:false, cause:'keyboard-unreachable', tabs, path } 를 돌려준다.
//   부르는 쪽은 reached=false 면 그 뒤 키 입력을 하지 않는다(unreached() 로 기록만).
async function tabTo(ctx, rowId) {
  await locatorClick(ctx, '#before');
  const path = [];
  const max = ctx.rules.device.maxTabsToRow;
  for (let i = 1; i <= max; i++) {
    await press(ctx, 'Tab');
    await wait(ctx, ctx.rules.device.keyGapMs);
    const st = await readState(ctx);
    path.push(pathStep(i, st.focus, rowId));
    if (st.focus.row === String(rowId) && st.focus.isStatusActivator) return { reached: true, cause: null, tabs: i, path };
  }
  return { reached: false, cause: 'keyboard-unreachable', tabs: max, path };
}

// 고침2 N1 U1 — 회로 연결 양성 대조: 그 줄 첫 칸을 마우스로 1회 눌러 #rowclick-count 가 늘었는가(README §5).
//   키 입력 0. 미도달 표본에서만(도달 표본의 절차는 바꾸지 않는다).
async function rowClickControl(ctx, rowId) {
  const st0 = await readState(ctx);
  const rects = await readRects(ctx);
  const cell = rects.rows[String(rowId)] ? rects.rows[String(rowId)].firstCell : null;
  if (!cell) return { clicked: false, before: st0.rowclickCount, after: st0.rowclickCount, ok: false };
  await mouseClick(ctx, Math.round(cell.x + cell.w / 2), Math.round(cell.y + cell.h / 2));
  const end = Date.now() + ctx.rules.device.openWaitMs;
  let st = await readState(ctx);
  while (st.rowclickCount <= st0.rowclickCount && Date.now() < end) {
    await wait(ctx, ctx.rules.device.pollMs);
    st = await readState(ctx);
  }
  return { clicked: true, before: st0.rowclickCount, after: st.rowclickCount, ok: st.rowclickCount > st0.rowclickCount };
}

// 고침2 N1-b: 미도달 뒤 — 구조 읽기(입력 0) → 행 클릭 양성 대조(마우스 1회) → 정착 시간 동안 회로 신호 관찰. 키 입력 0.
//   돌려줌: kbd(원자료 · U 판정은 judge.js kbdGate) · 정착 뒤 상태
async function unreached(ctx, rowId, reach, settleExtra) {
  const row1 = await readRowStruct(ctx, rowId);
  const rowClick = await rowClickControl(ctx, rowId);
  const s = await settle(ctx, settleMs(ctx.rules, ctx.cell.delay, settleExtra));
  return {
    kbd: {
      reached: false, cause: reach.cause || 'keyboard-unreachable', tabs: reach.tabs, path: reach.path,
      row1, rowClick, keyPressedAfter: 0, note: ctx.rules.deviceReading.noInput
    },
    state: s.state, maxOpen: s.maxOpen
  };
}

function shotName(cell, sampleNo, suffix) {
  // 점(.)으로 잇는다 — 가림(M8)의 22자 토큰 꼴(영숫자 · - · _)에 이름이 안 걸리게
  const raw = `${cell.item}.${cell.screen}.d${cell.delay}.w${cell.window.w}h${cell.window.h}.${cell.case}.s${sampleNo}${suffix ? '.' + suffix : ''}`;
  return raw.replace(/[^A-Za-z0-9.-]/g, '-') + '.png';
}

async function shot(ctx, suffix) {
  const name = shotName(ctx.cell, ctx.sampleNo, suffix);
  await ctx.page.screenshot({ path: require('path').join(ctx.outDir, 'shots', name) });
  return 'shots/' + name;
}

module.exports = {
  errText, settleMs, attachSignals, circuitSince, readState, readRects, slim, rowsDiff, newPicks,
  overlapArea, press, down, up, mouseClick, locatorClick, wait, settle, waitOpen, tabTo, shot, shotName,
  readRowStruct, rowClickControl, unreached, pathStep,
  // H4
  readBlankRows,
  // 고침6
  nowMs, waitOpenObserved
};
