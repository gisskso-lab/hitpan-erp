'use strict';
// 11/7 관문 ① 항목 I-1 ~ I-7 (설계 §4-2). I-9(지연 0/150/300) = 이 항목들을 run-items.js 가 세 지연 단에서 돌린 것.
// I-8(T3) = 정의 미확인 → 장치에 넣지 않음(rules.json plan.notMeasured 「미측정 · 정의 없음」).
// 판독 글자는 여기서 매기지 않는다 — 판독값만 남기고 판독은 judge.js(정본 글자만).
const L = require('./_lib.js');

const chipOf = (ctx, row) => `[data-row="${row}"] ${ctx.rules.device.sel.statusChip}`;

// 고침2 N1-b: Tab 으로 못 간 표본 — 그 뒤 키 입력 0 · 「입력 안 함(도달 0)」 · 유효 여부는 run-items.js(judge.js kbdGate · U1~U4)
async function notReached(ctx, row, reach, extraReadings) {
  const u = await L.unreached(ctx, row, reach);
  return [{
    readings: Object.assign({ reachedByTab: false, tabsToReach: null, keyPressed: 0, opened: false, noInput: ctx.rules.deviceReading.noInput }, extraReadings || {}, { final: L.slim(u.state) }),
    circuit: L.circuitSince(ctx, ctx.inputAt),
    kbd: u.kbd
  }];
}
const reachedKbd = (reach) => ({ reached: true, tabs: reach.tabs });

// I-1 키보드 탭 도달 — #before 초점 → Tab 반복 · 초점 요소 설명
async function i1(ctx) {
  const d = ctx.rules.device;
  await L.locatorClick(ctx, '#before');
  const path = [];
  for (let i = 1; i <= d.i1.tabs; i++) {
    await L.press(ctx, 'Tab');
    await L.wait(ctx, d.keyGapMs);
    const st = await L.readState(ctx);
    path.push({ i, focus: st.focus, openCount: st.openCount });
  }
  const hits = path.filter((p) => p.focus.isStatusActivator);
  // 고침2 N1: 12회 경로는 그대로. 상태 단추 0 이면 구조 읽기 + 행 클릭 양성 대조(키 입력 0) — 「키보드 도달 0(측정됨)」 은 U1~U4 충족 때만
  let kbd = { reached: hits.length > 0, tabs: hits.length ? hits[0].i : null };
  if (!hits.length) {
    const reach = { cause: 'keyboard-unreachable', tabs: d.i1.tabs, path: path.map((p) => L.pathStep(p.i, p.focus, d.kbd.refRow)) };
    const row1 = await L.readRowStruct(ctx, d.kbd.refRow);
    const rowClick = await L.rowClickControl(ctx, d.kbd.refRow);
    kbd = { reached: false, cause: reach.cause, tabs: reach.tabs, path: reach.path, row1, rowClick, keyPressedAfter: 0, note: ctx.rules.deviceReading.noInput };
  }
  const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  return [{
    readings: {
      path,
      firstStatusBtnAtTab: hits.length ? hits[0].i : null,
      statusRowsInTabOrder: hits.map((p) => p.focus.row),
      maxOpenSeen: Math.max(s.maxOpen, ...path.map((p) => p.openCount)),
      final: L.slim(s.state)
    },
    circuit: L.circuitSince(ctx, ctx.inputAt),
    kbd
  }];
}

// I-2 Enter/Space — 짧게 · 길게(같은 키 down 반복 = repeat 표지)
async function i2(ctx) {
  const d = ctx.rules.device;
  const row = d.i2.row;
  const key = ctx.cell.case.startsWith('enter') ? 'Enter' : 'Space';
  const long = ctx.cell.case.endsWith('long');
  const reach = await L.tabTo(ctx, row);
  if (!reach.reached) return notReached(ctx, row, reach, { key, long });
  const st0 = await L.readState(ctx);
  if (long) {
    const lp = d.i2.longPress;
    await L.down(ctx, key);
    await L.wait(ctx, lp.firstDelayMs);
    for (let r = 0; r < lp.repeats; r++) {
      await L.down(ctx, key);
      await L.wait(ctx, lp.intervalMs);
    }
    await L.up(ctx, key);
  } else {
    await L.press(ctx, key);
  }
  const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  return [{
    readings: {
      key, long, tabsToReach: reach.tabs,
      openCountAfter: s.state.openCount,
      maxOpenSeen: s.maxOpen,
      pickDelta: s.state.pickCount - st0.pickCount,
      newPicks: L.newPicks(st0, s.state),
      focusAfter: s.state.focus,
      changedRows: L.rowsDiff(st0, s.state),
      final: L.slim(s.state)
    },
    circuit: L.circuitSince(ctx, ctx.inputAt),
    kbd: reachedKbd(reach)
  }];
}

// I-3 덮임 — 창 높이 330/200 · 아래쪽 줄 열기 · 목록 상자와 이웃 줄 겹침 넓이 + PNG
async function i3(ctx) {
  const row = String(ctx.rules.device.i3.row);
  const st0 = await L.readState(ctx);
  await L.locatorClick(ctx, chipOf(ctx, row));
  const opened = await L.waitOpen(ctx, true);
  const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  const rects = await L.readRects(ctx);
  const overlaps = [];
  let total = 0;
  for (const list of rects.lists) {
    for (const id of Object.keys(rects.rows)) {
      if (id === row) continue;
      const area = L.overlapArea(list, rects.rows[id].row);
      if (area > 0) { overlaps.push({ row: id, area }); total += area; }
    }
  }
  const vh = rects.viewport.h;
  const shotPath = await L.shot(ctx, 'cover');
  return [{
    readings: {
      row, windowH: ctx.cell.window.h,
      openedSeen: opened.openCount,
      openCountAfterSettle: s.state.openCount,
      lists: rects.lists,
      targetRow: rects.rows[row] || null,
      overlaps, overlapTotalArea: total, overlapAny: total > 0,
      listClippedByViewport: rects.lists.some((r) => r.y < 0 || r.y + r.h > vh),
      viewport: rects.viewport,
      rowclickDelta: s.state.rowclickCount - st0.rowclickCount,
      final: L.slim(s.state)
    },
    circuit: L.circuitSince(ctx, ctx.inputAt),
    shot: shotPath
  }];
}

// I-4 Esc — 마우스로 연 뒤 · 키보드로 연 뒤 · 닫힌 뒤 초점
async function i4(ctx) {
  const row = String(ctx.rules.device.i4.row);
  let reach = null;
  if (ctx.cell.case === 'mouse-open') {
    await L.locatorClick(ctx, chipOf(ctx, row));
  } else {
    reach = await L.tabTo(ctx, row);
    if (!reach.reached) return notReached(ctx, row, reach, { openPath: ctx.cell.case, escNote: ctx.rules.deviceReading.kbdOpenNoEscV5 }); // 고침5 H1-10: 옛 글자 kbdOpenNoEsc(「Esc 측정 0」) 대체 — 미측정과 혼동 막기(판독 글자만)
    await L.press(ctx, 'Enter');
  }
  const st1 = await L.waitOpen(ctx, true);
  await L.press(ctx, 'Escape');
  const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  return [{
    readings: {
      openPath: ctx.cell.case, tabsToReach: reach ? reach.tabs : null,
      openCountBeforeEsc: st1.openCount,
      openCountAfterEsc: s.state.openCount,
      focusAfterEsc: s.state.focus,
      focusOnSameRowBtn: s.state.focus.row === row && s.state.focus.isStatusActivator && !s.state.focus.inOpenList,
      final: L.slim(s.state)
    },
    circuit: L.circuitSince(ctx, ctx.inputAt),
    kbd: reach ? reachedKbd(reach) : null
  }];
}

// I-5 열린 채 Tab / Shift+Tab
async function i5(ctx) {
  const row = String(ctx.rules.device.i5.row);
  const reach = await L.tabTo(ctx, row);
  if (!reach.reached) return notReached(ctx, row, reach, { kbdCase: ctx.cell.case });
  await L.press(ctx, 'Enter');
  const st1 = await L.waitOpen(ctx, true);
  await L.press(ctx, ctx.cell.case === 'tab' ? 'Tab' : 'Shift+Tab');
  const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  return [{
    readings: {
      tabsToReach: reach.tabs,
      openCountBeforeKey: st1.openCount,
      focusBeforeKey: st1.focus,
      openCountAfterKey: s.state.openCount,
      maxOpenSeen: s.maxOpen,
      focusAfterKey: s.state.focus,
      final: L.slim(s.state)
    },
    circuit: L.circuitSince(ctx, ctx.inputAt),
    kbd: reachedKbd(reach)
  }];
}

// I-6 A 줄 열고 Tab 으로 B 줄 → Esc/엔터 · 열린 목록 수 최대값
async function i6(ctx) {
  const d = ctx.rules.device;
  const rowA = String(d.i6.row);
  const reach = await L.tabTo(ctx, rowA);
  if (!reach.reached) return notReached(ctx, rowA, reach, { rowA, maxOpenCount: null });
  await L.press(ctx, 'Enter');
  const st1 = await L.waitOpen(ctx, true);
  let maxOpen = st1.openCount;
  let rowB = null;
  let tabsToB = null;
  const path = [];
  for (let i = 1; i <= d.i6.maxTabs; i++) {
    await L.press(ctx, 'Tab');
    await L.wait(ctx, d.keyGapMs);
    const st = await L.readState(ctx);
    if (st.openCount > maxOpen) maxOpen = st.openCount;
    path.push({ i, row: st.focus.row, inOpenList: st.focus.inOpenList, openCount: st.openCount });
    if (st.focus.row && st.focus.row !== rowA && st.focus.isStatusActivator) { rowB = st.focus.row; tabsToB = i; break; }
  }
  await L.press(ctx, ctx.cell.case === 'esc' ? 'Escape' : 'Enter');
  const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  if (s.maxOpen > maxOpen) maxOpen = s.maxOpen;
  return [{
    readings: {
      rowA, tabsToReachA: reach.tabs, openCountA: st1.openCount,
      reachedB: rowB !== null, rowB, tabsToB, path,
      finalKey: ctx.cell.case === 'esc' ? 'Escape' : 'Enter',
      maxOpenCount: maxOpen,
      openCountAfter: s.state.openCount,
      focusAfter: s.state.focus,
      final: L.slim(s.state)
    },
    circuit: L.circuitSince(ctx, ctx.inputAt),
    kbd: reachedKbd(reach)
  }];
}

// I-7 열린 채 다른 줄 위 바깥 누름 → 행 클릭 0 · 바뀐 줄(F10 = 연 줄뿐 · 다른 줄 0)
async function i7(ctx) {
  const d = ctx.rules.device;
  const rowA = String(d.i7.row);
  const rowB = String(d.i7.otherRow);
  const st0 = await L.readState(ctx);
  await L.locatorClick(ctx, chipOf(ctx, rowA));
  const st1 = await L.waitOpen(ctx, true);
  const rects = await L.readRects(ctx);
  const cellB = rects.rows[rowB] ? rects.rows[rowB].firstCell : null;
  if (!cellB) throw new Error(`${rowB}번 줄 첫 칸 좌표 없음`);
  const x = Math.round(cellB.x + cellB.w / 2);
  const y = Math.round(cellB.y + cellB.h / 2);
  const inside = rects.lists.some((r) => x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h);
  await L.mouseClick(ctx, x, y);
  const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  const changed = L.rowsDiff(st0, s.state);
  return [{
    readings: {
      rowA, rowB, openCountBeforePress: st1.openCount,
      point: { x, y }, pointInsideOpenList: inside,
      rowclickDelta: s.state.rowclickCount - st0.rowclickCount,
      rowclicksAfter: s.state.rowclicks,
      detailOpenBefore: st0.detailOpen, detailOpenAfter: s.state.detailOpen, detailRowAfter: s.state.detailRow,
      changedRows: changed,
      changedOtherRows: changed.filter((c) => c.row !== rowA),
      newPicks: L.newPicks(st0, s.state),
      openCountAfter: s.state.openCount,
      final: L.slim(s.state)
    },
    circuit: L.circuitSince(ctx, ctx.inputAt)
  }];
}

module.exports = { 'I-1': i1, 'I-2': i2, 'I-3': i3, 'I-4': i4, 'I-5': i5, 'I-6': i6, 'I-7': i7 };
