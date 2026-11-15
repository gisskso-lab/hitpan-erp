'use strict';
// 11/13 §6 새 항목 N-1 · N-2 (설계 §4-2).
// N-1: 키보드로 목록 열기 → ↑ · ↓ 빠른 연타(간격 · 횟수 = rules.json device.n1) → 회로 신호 3종 · 목록 닫힘 · 초점 · 다른 줄 변화.
// N-2: N-1 직후 같은 문맥에서 Enter 두 번 → #pick-count · #picks (관찰 칸 · 기록만).
// 판독 글자(대조 무효 / 🔴 / 🟡)는 judge.js 가 정본 글자로만 매긴다.
const L = require('./_lib.js');

async function n1(ctx) {
  const d = ctx.rules.device;
  const row = String(d.n1.row);
  const st0 = await L.readState(ctx);
  const reach = await L.tabTo(ctx, row);
  if (!reach.reached) {
    // 고침2 N1-b: 못 갔으면 열기 0 · 연타 0 · N-2 입력 0 — 회로 신호 3종은 정착 시간 동안 기록(그림 0)
    const u = await L.unreached(ctx, row, reach, d.n1.settleExtraMs);
    const D = ctx.rules.deviceReading;
    return [{
      item: 'N-1',
      readings: {
        row, reachedByTab: false, tabsToReach: null, keyPressed: 0, opened: false, burstSent: 0,
        noInput: D.noInput, burstNote: D.n1NoBurst,
        listClosed: null, focusSameRowBtn: null, otherRowsChanged: null,
        final: L.slim(u.state)
      },
      circuit: L.circuitSince(ctx, ctx.inputAt),
      kbd: u.kbd
    }, {
      item: 'N-2',
      readings: { startedAfterN1: false, parentOpened: false, keyPressed: 0, pickDelta: null, note: D.n2ParentNotOpened, final: L.slim(u.state) },
      circuit: L.circuitSince(ctx, ctx.inputAt),
      kbd: u.kbd
    }];
  }
  await L.press(ctx, 'Enter');
  const st1 = await L.waitOpen(ctx, true);
  const burstStart = Date.now();
  for (let i = 0; i < d.n1.count; i++) {
    await L.press(ctx, d.n1.keys[i % d.n1.keys.length]);
    if (d.n1.intervalMs > 0) await L.wait(ctx, d.n1.intervalMs);
  }
  const burstMs = Date.now() - burstStart;
  const s = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay, d.n1.settleExtraMs));
  const st2 = s.state;
  const shotN1 = await L.shot(ctx, 'n1-end');
  const changedOther = L.rowsDiff(st0, st2, row);
  const picksOther = L.newPicks(st0, st2).filter((p) => p.row !== row);
  const circuitN1 = L.circuitSince(ctx, ctx.inputAt);
  const out = [{
    item: 'N-1',
    readings: {
      row, tabsToReach: reach.tabs,
      opened: st1.openCount > 0, openCountAfterOpen: st1.openCount,
      burst: { keys: d.n1.keys, count: d.n1.count, intervalMs: d.n1.intervalMs, elapsedMs: burstMs },
      listClosed: st2.openCount === 0,
      openCountAfter: st2.openCount,
      maxOpenSeen: s.maxOpen,
      focusAfter: st2.focus,
      focusSameRowBtn: st2.focus.row === row && st2.focus.isStatusActivator && !st2.focus.inOpenList,
      changedOtherRows: changedOther,
      newPicksOtherRows: picksOther,
      otherRowsChanged: changedOther.length > 0 || picksOther.length > 0,
      picksBefore: st0.picks, picksAfter: st2.picks,
      final: L.slim(st2)
    },
    circuit: circuitN1,
    shot: shotN1,
    kbd: { reached: true, tabs: reach.tabs }
  }];

  // N-2 — 같은 문맥 이어서(관찰 칸)
  const t2 = Date.now();
  await L.press(ctx, 'Enter');
  await L.wait(ctx, d.n2.gapMs);
  await L.press(ctx, 'Enter');
  const s3 = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));
  const st3 = s3.state;
  out.push({
    item: 'N-2',
    readings: {
      startedAfterN1: true,
      focusBefore: st2.focus,
      pickDelta: st3.pickCount - st2.pickCount,
      newPicks: L.newPicks(st2, st3),
      picksBefore: st2.picks, picksAfter: st3.picks,
      openCountAfter: st3.openCount,
      maxOpenSeen: s3.maxOpen,
      focusAfter: st3.focus,
      final: L.slim(st3)
    },
    circuit: L.circuitSince(ctx, t2),
    kbd: { reached: true, tabs: reach.tabs }
  });
  return out;
}

module.exports = { 'N-1': n1 };
