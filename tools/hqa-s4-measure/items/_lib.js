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
    const describe = (el) => {
      if (!el || el === document.body || el === document.documentElement) {
        return { tag: el ? el.tagName.toLowerCase() : null, id: null, role: null, row: null, inOpenList: false, isStatusActivator: false, text: null };
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
        text: t ? t.slice(0, 60) : null
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
  }, { openList: s.openList, chip: s.statusChip, reconnect: s.reconnect });
  if (st.reconnect.visible) ctx.sig.reconnectSeen.push({ at: Date.now() });
  return st;
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

// #before 를 마우스로 누른 뒤 Tab 만으로 그 줄 상태 단추까지 간다.
async function tabTo(ctx, rowId) {
  await locatorClick(ctx, '#before');
  const path = [];
  for (let i = 1; i <= ctx.rules.device.maxTabsToRow; i++) {
    await press(ctx, 'Tab');
    await wait(ctx, ctx.rules.device.keyGapMs);
    const st = await readState(ctx);
    path.push({ i, row: st.focus.row, tag: st.focus.tag, role: st.focus.role });
    if (st.focus.row === String(rowId) && st.focus.isStatusActivator) return { tabs: i, path };
  }
  throw new Error(`Tab ${ctx.rules.device.maxTabsToRow}회 안에 ${rowId}번 줄 상태 단추에 못 감`);
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
  overlapArea, press, down, up, mouseClick, locatorClick, wait, settle, waitOpen, tabTo, shot, shotName
};
