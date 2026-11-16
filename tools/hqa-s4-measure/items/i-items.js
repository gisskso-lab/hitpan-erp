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

// J1-B(반려 [3-V] 2-b) — I-4 규칙 값 단언: 기다림 · 관측 간격 · polling 상한 = 양의 유한수 · 메뉴 0 실패 경우 = 배열.
//   빠지거나 깨진 칸 이름 목록을 돌려준다(빈 목록 = 정상). 기본값으로 메우지 않는다(값이 빠진 채 진행하면 HQA 절차가 아닌 것을 잰다).
function i4RuleBadKeys(d4) {
  if (!d4 || typeof d4 !== 'object') return ['device.i4'];
  const pos = (v) => typeof v === 'number' && Number.isFinite(v) && v > 0;
  const bad = ['escWaitMs', 'openPollMs', 'pollAfterEscMaxMs'].filter((k) => !pos(d4[k]));
  if (!Array.isArray(d4.menuZeroFailCases)) bad.push('menuZeroFailCases');
  return bad;
}

// I-4 Esc — 마우스로 연 뒤 · 키보드로 연 뒤 · 닫힌 뒤 초점
async function i4(ctx) {
  const ruleBad = i4RuleBadKeys(ctx.rules.device.i4); // J1-B(2-b): 첫 일 = 규칙 값 단언 · 깨지면 입력 0 으로 표본 = 장치 실패
  if (ruleBad.length) throw new Error(`${ctx.rules.deviceReading.i4RuleBad} · ${ruleBad.join('·')} · 입력 0`);
  const row = String(ctx.rules.device.i4.row);
  let reach = null;
  if (ctx.cell.case === 'mouse-open') {
    await L.locatorClick(ctx, chipOf(ctx, row));
  } else {
    reach = await L.tabTo(ctx, row);
    if (!reach.reached) return notReached(ctx, row, reach, { openPath: ctx.cell.case, escNote: ctx.rules.deviceReading.kbdOpenNoEscV5 }); // 고침5 H1-10: 옛 글자 kbdOpenNoEsc(「Esc 측정 0」) 대체 — 미측정과 혼동 막기(판독 글자만)
    await L.press(ctx, 'Enter');
  }
  // ⬛ 옛 줄(고침5까지 · 열림을 못 봐도 바로 Esc · 기다림 0):
  // const st1 = await L.waitOpen(ctx, true);
  // await L.press(ctx, 'Escape');
  // 🔴 고침6 c2(결-2 · 결-3 · 결-4 · 결-7) — HQA 절차: 열린 메뉴 관측 → escWaitMs 유지 → Esc. 두 경우 · 모든 화면 같은 값(화면별 칸 0)
  const d4 = ctx.rules.device.i4;
  const ob = await L.waitOpenObserved(ctx, d4.openPollMs);
  if (!ob.observed) throw new Error(`${ctx.rules.deviceReading.i4OpenNotObserved} · Esc 누름 0 · 관측 간격 ${d4.openPollMs}ms · 상한 ${ctx.rules.device.openWaitMs + 2 * ctx.cell.delay}ms`);
  const st1 = ob.state;
  const restMs = d4.escWaitMs - (L.nowMs() - ctx.sig.t0 - ob.observedAtMs);
  // ⬛ 옛 줄(J1 c2 ~ c6 · 기다리는 동안 읽기 0 → 「700ms 유지」 미확인 · 반려 [3-V] 2-a):
  // if (restMs > 0) await L.wait(ctx, restMs);
  // ⬛ 옛 줄(J1-B c7 · 기다림 = openPollMs 간격 관측 loop · 700ms 동안 읽기 약 14회 = HQA 원 절차(6709803008 「열린 메뉴 관측 → 700ms 유지 → Esc」)에 없는 읽기
  //   → 메인 스레드에 끼어들어 e1t3 의 렌더 뒤 틈 경쟁(재려는 결함 자체)을 흔들 수 있음 · 관찰자 영향 · PM 결정 11/17 J1-C):
  // 🔴 J1-B(2-a) — 기다림 = openPollMs 간격 관측 · Esc 직전 읽기 최소 1 · 그 사이 열린 목록 0 을 한 번이라도 보면 Esc 누름 0 · 장치 실패
  // const holdEnd = L.nowMs() + Math.max(0, restMs);
  // let stEsc = null;
  // let holdReads = 0;
  // for (;;) {
  //   const left = holdEnd - L.nowMs();
  //   if (left > 0) await L.wait(ctx, Math.min(d4.openPollMs, left));
  //   stEsc = await L.readState(ctx);
  //   holdReads += 1;
  //   if (stEsc.openCount === 0) throw new Error(`${ctx.rules.deviceReading.i4OpenNotHeld} · Esc 누름 0 · 관측 뒤 ${L.nowMs() - ctx.sig.t0 - ob.observedAtMs}ms · 기다림 ${d4.escWaitMs}ms · 간격 ${d4.openPollMs}ms`);
  //   if (L.nowMs() >= holdEnd) break;
  // }
  // 🔴 J1-C(관찰자 영향 줄이기) — 기다림 = 읽기 0 · L.wait 로 끝 시각(첫 열림 관측 시각 + escWaitMs)까지 · Esc 직전 딱 한 번 readState
  //   → 열린 목록 0 이면 Esc 누름 0 · 장치 실패(사유 i4OpenNotHeld). holdReads = 1(Esc 직전 한 번).
  //   남는 위험(공개 · 부록 §J1-C Y1): 기다림 중 순간 닫혔다 다시 열림은 관측 안 함 · Esc 직전 읽기와 Esc 사이 틈
  const holdEnd = L.nowMs() + Math.max(0, restMs);
  const left = holdEnd - L.nowMs();
  if (left > 0) await L.wait(ctx, left);
  const stEsc = await L.readState(ctx);
  const holdReads = 1;
  if (stEsc.openCount === 0) throw new Error(`${ctx.rules.deviceReading.i4OpenNotHeld} · Esc 누름 0 · 관측 뒤 ${L.nowMs() - ctx.sig.t0 - ob.observedAtMs}ms · 기다림 ${d4.escWaitMs}ms · 기다림 중 읽기 0 · Esc 직전 읽기 1`);
  const escPressedAtMs = L.nowMs() - ctx.sig.t0;
  await L.press(ctx, 'Escape');
  // ⬛ 옛 줄(고침6 c2 까지 · 고정 settle 뒤 마지막 상태 하나):
  // const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  // focusOnSameRowBtn: s.state.focus.row === row && s.state.focus.isStatusActivator && !s.state.focus.inOpenList,
  // 🔴 고침6 c3(결-5 · 결-12) — Esc → 메뉴 0 관측 → 최대 pollAfterEscMaxMs polling · 두 식(지금 식 · HQA 원문 식) 각각 기록
  const pa = await L.pollAfterEsc(ctx, d4.openPollMs, d4.pollAfterEscMaxMs);
  // ⬛ 옛 줄(J1 c3 · 목록 칸이 빠지면 빈 목록으로 메움 → kbd-open 메뉴 0 못 봄이 통과로 샘 · 반려 [3-V] 2-b):
  // if (!pa.menuZeroObserved && (d4.menuZeroFailCases || []).includes(ctx.cell.case)) {
  if (!pa.menuZeroObserved && d4.menuZeroFailCases.includes(ctx.cell.case)) {
    throw new Error(`${ctx.rules.deviceReading.i4MenuZeroNotObserved} · 열린 목록 ${pa.state.openCount} · 상한 ${ctx.rules.device.openWaitMs + 2 * ctx.cell.delay}ms · Esc ${escPressedAtMs}ms`);
  }
  const s = { state: pa.state }; // 같은 줄 번호(row)는 pollAfterEsc 가 rules device.i4.row 로 읽는다(같은 값)
  return [{
    readings: {
      openPath: ctx.cell.case, tabsToReach: reach ? reach.tabs : null,
      // 옛 이름 그대로 · 뜻 = 「열린 메뉴 관측 시점」 의 열린 목록 수(Esc 직전 아님 · 이름만 Before) — rules device.i4._openCountAtEsc_표지_J1B
      openCountBeforeEsc: st1.openCount,
      // J1-B(2-a): Esc 직전 마지막 읽기의 열린 목록 수(기다림 관측이 통과했으니 ≥ 1) · 기다리는 동안 읽은 수
      // J1-C: holdReads = 1(기다림 중 읽기 0 · Esc 직전 한 번) · openCountAtEsc = 그 한 번 읽기 값(≥ 1)
      openCountAtEsc: stEsc.openCount, holdReads,
      // openCountAfterEsc · focusAfterEsc · maxOpenAfterEsc = pollAfterEsc 가 멈춘 시각 값(두 식 모두 참이면 3초 전에 멈춤 · 3초 끝 값 아님) — J1-B [4] ④
      openCountAfterEsc: s.state.openCount,
      focusAfterEsc: s.state.focus,
      focusOnSameRowBtn: pa.menuZeroObserved ? pa.restoredAtMs !== null : null,
      // 고침6 c2: 기다림 기록(결-3) — 시각 = sig.t0 기준 ms
      escWaitMs: d4.escWaitMs, openPollMs: d4.openPollMs,
      openObserved: true, openObservedAtMs: ob.observedAtMs, escPressedAtMs, actualWaitMs: escPressedAtMs - ob.observedAtMs,
      // 고침6 c3: Esc 뒤 판독 기록(결-5) · 두 식(결-12) — 메뉴 0 못 본 mouse-open = 열림 남음 판독값 · 초점 polling 0(null)
      menuZeroObserved: pa.menuZeroObserved, menuZeroAtMs: pa.menuZeroAtMs, pollAfterEscMaxMs: d4.pollAfterEscMaxMs,
      restoredAtMs: pa.restoredAtMs, hqaRestoredAtMs: pa.hqaRestoredAtMs, polledMs: pa.polledMs, pollReads: pa.reads,
      hqaFocusRow1Restored: pa.menuZeroObserved ? pa.hqaRestoredAtMs !== null : null, hqaFocusRowEnd: pa.hqaFocusRowEnd,
      maxOpenAfterEsc: pa.maxOpen,
      menuNotClosedNote: pa.menuZeroObserved ? null : ctx.rules.deviceReading.i4MenuNotClosedRead,
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
  if (ctx.cell.case === 'below-list') return i7Below(ctx); // 고침5 결-10 甲: F10 전용 경우(아래 함수) · row-cell 길은 그대로
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

// 고침5 결-10 甲(F10 · 정본 #15 · I-7 겸함): 열린 목록 「밑」 누름 — 누른 점이 목록 상자 밖 · 아래인지 누르기 전에 단언(아니면 표본 오류 · 누름 0)
//   점 = 첫 열린 목록 가로 가운데 · 목록 아래 끝 + device.i7.belowGapPx · 기대 글자 = readingText.I-7.expectedF10(무변)
async function i7Below(ctx) {
  const d = ctx.rules.device;
  const rowA = String(d.i7.row);
  const st0 = await L.readState(ctx);
  await L.locatorClick(ctx, chipOf(ctx, rowA));
  const st1 = await L.waitOpen(ctx, true);
  const rects = await L.readRects(ctx);
  const lb = rects.lists[0];
  if (!lb) throw new Error('열린 목록 좌표 없음');
  const x = Math.round(lb.x + lb.w / 2);
  const y = lb.y + lb.h + d.i7.belowGapPx;
  const inside = rects.lists.some((r) => x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h);
  const below = y > lb.y + lb.h && x >= lb.x && x <= lb.x + lb.w;
  if (!below || inside || y >= rects.viewport.h) throw new Error(`목록 밑 점 단언 실패 below=${below} inside=${inside} y=${y} vh=${rects.viewport.h}`);
  await L.mouseClick(ctx, x, y);
  const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  const changed = L.rowsDiff(st0, s.state);
  return [{
    readings: {
      rowA, pressAt: 'below-list', openCountBeforePress: st1.openCount,
      point: { x, y }, listBox: lb, pointBelowOpenList: below, pointInsideOpenList: inside,
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

// H4 §7-2 — I-8(T3) 공백 상태 칸: 입력 0 구조 읽기만(키 · 누름 · 초점 이동 0). 허용 호출 = L.settle · L.readBlankRows · L.circuitSince
//   (selftest-judge.js 정적 검사가 이 함수 원문의 L.* 호출을 허용 목록과 대조 · 심은 위반 = 빨강)
async function i8(ctx) {
  const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  const rows = await L.readBlankRows(ctx);
  return [{
    readings: { blankRows: rows, rowsRead: rows.length, final: L.slim(s.state) },
    circuit: L.circuitSince(ctx, ctx.inputAt)
  }];
}

module.exports = { 'I-1': i1, 'I-2': i2, 'I-3': i3, 'I-4': i4, 'I-5': i5, 'I-6': i6, 'I-7': i7, 'I-8': i8 };
