#!/usr/bin/env node
'use strict';
// 장치 판독기(설계 §4-3 · M7) — samples.jsonl + rules.json → verdict-device.json.
// · 판독 글자는 rules.json readingText(정본 글자)만 쓴다. 기대값을 새로 만들지 않는다. 판정자 = HQA.
// · 잰 표본 0 = 빨강: 계획 칸마다 유효 표본 ≥ 1. /before 칸 표본 0 도 빨강(「대조 무효」 와 다름: 무효 = 표본 있음 · 재현 0).
// 쓰는 법: node judge.js [--rules <rules.json>] [--samples <samples.jsonl>] [--out <verdict-device.json>]
//   기본: rules = 이 폴더 rules.json · samples/out = $HQA_OUT_DIR/samples.jsonl · $HQA_OUT_DIR/verdict-device.json
// 종료 코드: 0 = 빨강 아님 · 1 = 빨강(판독 파일은 씀) · 2 = 인자 · 규칙 파일 오류(판독 파일 못 씀)
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

function defaultWindow(rules) {
  const v = rules.hqaPending.browserCondition.value.viewport;
  return { w: v.width, h: v.height };
}

function cellKey(c) {
  return `${c.item}|${c.case}|${c.screen}|d${c.delay}|${c.window.w}x${c.window.h}`;
}

function samplesFor(rules, item) {
  const v = rules.hqaPending.samplesPerCell.value;
  return Object.prototype.hasOwnProperty.call(v, item) ? v[item] : v.default;
}

function buildPlan(rules) {
  const P = rules.plan;
  const win = defaultWindow(rules);
  const cells = [];
  for (const item of P.items) {
    const spec = P.itemSpecs[item];
    if (!spec) throw new Error(`plan.itemSpecs 에 ${item} 없음`);
    const delays = P[spec.delays];
    if (!Array.isArray(delays)) throw new Error(`plan.${spec.delays} 없음`);
    const windows = (spec.windows || [{}]).map((w) => ({ w: w.w || win.w, h: w.h || win.h }));
    const n = samplesFor(rules, spec.derivedFrom || item);
    for (const kase of spec.cases) {
      for (const screen of P.screens) {
        for (const delay of delays) {
          for (const window of windows) {
            const c = { item, case: kase, screen, delay, window, samples: n, derivedFrom: spec.derivedFrom || null };
            c.key = cellKey(c);
            cells.push(c);
          }
        }
      }
    }
  }
  // 파생 칸(N-2)은 부모 칸(N-1)과 같은 화면 · 지연 · 창에 붙는다
  const derived = {};
  for (const c of cells) {
    if (!c.derivedFrom) continue;
    const parent = cells.find((p) => p.item === c.derivedFrom && p.screen === c.screen && p.delay === c.delay && p.window.w === c.window.w && p.window.h === c.window.h);
    if (!parent) throw new Error(`${c.key} 의 부모 칸 없음`);
    (derived[parent.key] = derived[parent.key] || []).push(c);
  }
  return { cells, derived };
}

// ── 고침2 N1 — 키보드 미도달 표본 관문(U1~U4 · rules.json keyboardGate) · 구분 점검 판정(K · rules.json kCheck) ──
// run-items.js(표본 쓸 때)와 judge(판독 때)가 같은 함수를 쓴다. 판정 글자는 만들지 않는다(장치 판독 글자만).
function groupKey(s) {
  const w = s.window || {};
  return `${s.item}|${s.case}|d${s.delay}|${w.w}x${w.h}`;
}

// R1 이 K-결함 R1 조건과 같은가: 활성 div 가 있고 display:contents 또는 사각형 0 · 줄 안 초점 받을 보이는 요소 0
function kStructDefect(row1) {
  if (!row1 || row1.present !== true || !row1.activator || row1.activator.present !== true) return false;
  const hidden = row1.activator.display === 'contents' || row1.activator.rects === 0;
  return hidden && row1.focusableVisible === 0;
}

// Tab 경로가 1번 줄을 지나 뒤 요소(BODY 아님)에 1회 이상 갔는가
function pathPassedRef(p) {
  return Array.isArray(p) && p.some((x) => x && x.afterRef === true && x.body !== true);
}

// 1번 줄 안 tabIndex >= 0 요소에 초점이 갔는데 상태 단추 판정이 false 인 회차가 있는가(K-장치 꼴)
function pathMissedInRef(p) {
  return Array.isArray(p) && p.some((x) => x && x.inRef === true && typeof x.tabIndex === 'number' && x.tabIndex >= 0 && x.isStatusActivator === false);
}

function uChecks(s, afterReached) {
  const k = s.kbd || {};
  return {
    U1: !!(k.rowClick && k.rowClick.ok === true),
    U2: pathPassedRef(k.path),
    U3: Number(afterReached) >= 1,
    U4: kStructDefect(k.row1)
  };
}

// 미도달 표본 하나 → { valid, reading, reason, u }
//   /after 미도달 = 유효 표본에서 뺌(device_failure · 「/after 키보드 도달 0」) — keyboard_unreachable 로 바꾸지 않는다(C1-3)
//   /before 미도달 = U1~U4 모두 참일 때만 keyboard_unreachable(유효) · 아니면 device_failure
function kbdGate(rules, s, afterReached) {
  const D = rules.deviceReading;
  if (s.screen !== 'before') return { valid: false, reading: 'device_failure', reason: D.afterUnreachable, u: null };
  const u = uChecks(s, afterReached);
  const miss = Object.keys(u).filter((x) => !u[x]);
  if (miss.length === 0) return { valid: true, reading: 'keyboard_unreachable', reason: null, u };
  return { valid: false, reading: 'device_failure', reason: `/before 키보드 도달 0 · ${miss.join('·')} 결손`, u };
}

// 리허설 3 구분 점검(probe · /before · 지연 0): R1 구조 · R2 Tab 경로 · R3 행 클릭 양성 대조 → K-결함 / K-장치 / K-미결
function kCheck(r1, r2path, r3) {
  const conds = {
    r1StructDefect: kStructDefect(r1),
    r2PassedRow1: pathPassedRef(r2path),
    r2BodyOnly: Array.isArray(r2path) && r2path.length > 0 && r2path.every((x) => x && x.body === true),
    r3Connected: !!(r3 && r3.ok === true),
    r2MissedInRow1: pathMissedInRef(r2path),
    r2Reached: Array.isArray(r2path) && r2path.some((x) => x && x.inRef === true && x.isStatusActivator === true)
  };
  const defect = conds.r1StructDefect && conds.r2PassedRow1 && !conds.r2BodyOnly && conds.r3Connected && !conds.r2Reached;
  const device = conds.r2MissedInRow1;
  let verdict = 'K-미결';
  if (defect && !device) verdict = 'K-결함';
  else if (device && !defect) verdict = 'K-장치';
  return { verdict, conds };
}

// judge 쪽 유효 판단(표본의 valid 를 그대로 믿지 않고 미도달 표본은 kbdGate 로 다시 잰다)
function effective(rules, samples) {
  const afterReach = {};
  for (const s of samples) {
    if (s && s.screen === 'after' && s.kbd && s.kbd.reached === true) afterReach[groupKey(s)] = (afterReach[groupKey(s)] || 0) + 1;
  }
  const eff = new Map();
  for (const s of samples) {
    if (!s) continue;
    if (s.kbd && s.kbd.reached === false) {
      const g = kbdGate(rules, s, afterReach[groupKey(s)] || 0);
      eff.set(s, { valid: g.valid, reading: g.reading, reason: g.reason, mismatch: (s.valid === true) !== g.valid });
    } else {
      const bad = s.deviceReading === 'keyboard_unreachable';
      eff.set(s, { valid: s.valid === true && !bad, reading: null, reason: bad ? 'kbd 없는 keyboard_unreachable' : null, mismatch: bad });
    }
  }
  return eff;
}

function parseJsonl(text) {
  const samples = [];
  const bad = [];
  text.split(/\r?\n/).forEach((line, i) => {
    if (!line.trim()) return;
    try { samples.push(JSON.parse(line)); } catch (e) { bad.push({ line: i + 1, error: String(e.message).slice(0, 120) }); }
  });
  return { samples, bad };
}

function judge(rules, samples, extra) {
  const T = rules.readingText;
  const D = rules.deviceReading;
  const plan = buildPlan(rules);
  const eff = effective(rules, samples);
  const isValid = (s) => !!(eff.get(s) && eff.get(s).valid);
  const isKu = (s) => !!(eff.get(s) && eff.get(s).valid && eff.get(s).reading === 'keyboard_unreachable');
  const isAfterUr = (s) => !!(s && s.screen === 'after' && s.kbd && s.kbd.reached === false);
  const byCell = {};
  for (const c of plan.cells) byCell[c.key] = [];
  const unplanned = [];
  for (const s of samples) {
    if (s && Object.prototype.hasOwnProperty.call(byCell, s.cell)) byCell[s.cell].push(s);
    else unplanned.push(s && s.cell ? s.cell : '(cell 없음)');
  }
  const validOf = (key) => byCell[key].filter(isValid);
  const redReasons = [];

  const cells = plan.cells.map((c) => {
    const all = byCell[c.key];
    const valid = all.filter(isValid).length;
    const out = { key: c.key, item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, planned: c.samples, got: all.length, valid, invalid: all.length - valid };
    // 고침2 N1-d: 칸 판독 — device_failure(유효 표본 0 · 빨강 · 기존 규칙 그대로) ↔ keyboard_unreachable(/before 유효 표본 · 판정 = HQA)
    out.kbdUnreachable = all.filter(isKu).length;
    out.afterUnreachExcl = all.filter(isAfterUr).length;
    if (valid === 0) {
      out.status = T.cellFail;
      out.reason = c.screen === 'before' ? T.beforeZeroReason : T.cellZeroReason;
      if (out.afterUnreachExcl > 0) out.reason += ' · ' + D.afterUnreachable;
      out.deviceReading = 'device_failure';
      redReasons.push(`${out.reason}: ${c.key}`);
    } else {
      out.status = '표본 있음';
      out.deviceReading = out.kbdUnreachable > 0 ? 'keyboard_unreachable' : null;
      if (out.kbdUnreachable > 0) out.deviceReadingText = `${D.keyboard_unreachable} · ${D.sameShapeD1}`;
    }
    return out;
  });
  const cellFailed = (c) => validOf(c.key).length === 0;
  const cellsOf = (item, pred) => plan.cells.filter((c) => c.item === item && (!pred || pred(c)));
  const flat = (cs) => cs.reduce((a, c) => a.concat(validOf(c.key)), []);
  const R = (s) => s.readings || {};

  const items = {};
  const has = (item) => rules.plan.items.includes(item);

  // N-1 — 11/13 §6 판정 글자 그대로
  if (has('N-1')) {
    const t = T['N-1'];
    const cd = rules.plan.n1ContrastDelays;
    const bCells = cellsOf('N-1', (c) => c.screen === 'before' && cd.includes(c.delay));
    const aCells = cellsOf('N-1', (c) => c.screen === 'after');
    const fail = bCells.concat(aCells).filter(cellFailed).map((c) => c.key);
    const bValid = flat(bCells);
    const aValid = flat(aCells);
    // 고침3 N-A: 「키보드 도달 0(측정됨)」 표본은 화살표 입력 0 → 재현 시도가 아니다 → 재현 셈에서 뺀다(I-6 의 !isKu 와 같은 방식)
    //   그 표본의 회로 죽음은 beforeKuDead 로 따로 센다(기록만 · 판정 글자 무관)
    const reproduced = bValid.filter((s) => !isKu(s) && s.circuit && s.circuit.dead).length;
    const bKuDead = bValid.filter((s) => isKu(s) && s.circuit && s.circuit.dead).length;
    const afterDead = aValid.filter((s) => s.circuit && s.circuit.dead).length;
    const afterOther = aValid.filter((s) => R(s).otherRowsChanged === true).length;
    const afterFocusOff = aValid.filter((s) => R(s).focusSameRowBtn === false).length;
    const labels = [];
    const bKu = bValid.filter(isKu).length;
    if (fail.length === 0) {
      if (reproduced === 0) labels.push(t.contrastInvalid);
      if (afterDead > 0 || afterOther > 0) labels.push(t.red);
      if (afterFocusOff > 0) labels.push(t.yellow);
    }
    items['N-1'] = {
      source: t.source, ruleText: t.ruleText, expectedAfter: t.expectedAfter, expectedBefore: t.expectedBefore,
      deviceFail: fail.length > 0, failCells: fail,
      labels,
      // 고침2 N1-d: 「대조 무효」 글자는 그대로 · 사유 칸만 붙인다(/before 키보드로 못 연 표본이 있을 때)
      contrastReason: fail.length === 0 && reproduced === 0 && bKu > 0 ? D.n1ContrastReason : null,
      reading: fail.length > 0 ? T.cellFail : (labels.length ? labels.join(' · ') : T.record),
      counts: {
        beforeContrastValid: bValid.length, beforeCircuitDead: reproduced,
        afterValid: aValid.length, afterCircuitDead: afterDead, afterOtherRowsChanged: afterOther, afterFocusOffRowBtn: afterFocusOff,
        afterListNotClosed: aValid.filter((s) => R(s).listClosed === false).length,
        afterNotOpened: aValid.filter((s) => R(s).opened === false).length,
        beforeNotOpened: bValid.filter((s) => R(s).opened === false).length,
        beforeKbdUnreachable: bKu, beforeKuDead: bKuDead,
        afterUnreachExcl: aCells.reduce((a, c) => a + byCell[c.key].filter(isAfterUr).length, 0)
      },
      byDelay: rules.plan.nDelays.map((d) => {
        const b = flat(cellsOf('N-1', (c) => c.screen === 'before' && c.delay === d));
        const a = flat(cellsOf('N-1', (c) => c.screen === 'after' && c.delay === d));
        return { delay: d, beforeValid: b.length, beforeDead: b.filter((s) => s.circuit && s.circuit.dead).length, afterValid: a.length, afterDead: a.filter((s) => s.circuit && s.circuit.dead).length };
      }),
      judgedBy: 'HQA'
    };
  }

  // N-2 — 관찰 칸 · 기록만
  if (has('N-2')) {
    const t = T['N-2'];
    const cs = cellsOf('N-2');
    items['N-2'] = {
      source: t.source, afterText: t.afterText, beforeText: t.beforeText, ruleText: t.ruleText, expected: t.expected,
      deviceFail: cs.some(cellFailed), failCells: cs.filter(cellFailed).map((c) => c.key),
      reading: cs.some(cellFailed) ? T.cellFail : T.record,
      pickDeltaByScreen: ['before', 'after'].map((sc) => ({ screen: sc, values: flat(cs.filter((c) => c.screen === sc)).map((s) => R(s).pickDelta) })),
      judgedBy: 'HQA'
    };
  }

  // I-6 · I-7 — 정본에 기대값 글자가 있는 것만 기계 판독(맞음/어긋남 사실) · 판정 = HQA
  const contrastItem = (item, isFail, expectedTexts) => {
    const t = T[item];
    const cs = cellsOf(item);
    const fail = cs.filter(cellFailed).map((c) => c.key);
    const a = flat(cs.filter((c) => c.screen === 'after'));
    const b = flat(cs.filter((c) => c.screen === 'before'));
    const afterMismatch = a.filter(isFail).length;
    const beforeReproduced = b.filter(isFail).length;
    return {
      source: t.source, expected: expectedTexts, contrastRule: t.contrastRule,
      deviceFail: fail.length > 0, failCells: fail,
      afterValid: a.length, afterMismatchExpected: afterMismatch,
      beforeValid: b.length, beforeReproduced: beforeReproduced,
      beforeKbdUnreachable: b.filter(isKu).length,
      afterUnreachExcl: cs.filter((c) => c.screen === 'after').reduce((n, c) => n + byCell[c.key].filter(isAfterUr).length, 0),
      contrast: fail.length > 0 ? T.cellFail : (beforeReproduced === 0 ? t.contrastInvalid : '재현됨'),
      reading: fail.length > 0 ? T.cellFail : T.record,
      judgedBy: 'HQA'
    };
  };
  if (has('I-6')) items['I-6'] = contrastItem('I-6', (s) => !isKu(s) && R(s).maxOpenCount >= 2, [T['I-6'].expected]);
  if (has('I-7')) {
    items['I-7'] = contrastItem('I-7', (s) => R(s).rowclickDelta > 0 || (Array.isArray(R(s).changedOtherRows) && R(s).changedOtherRows.length > 0), [T['I-7'].expected, T['I-7'].expectedF10]);
  }

  // 나머지 11/7 항목 — 실패 정의 없음 → 기록 · 판정 = HQA
  for (const item of ['I-1', 'I-2', 'I-3', 'I-4', 'I-5']) {
    if (!has(item)) continue;
    const cs = cellsOf(item);
    items[item] = {
      deviceFail: cs.some(cellFailed), failCells: cs.filter(cellFailed).map((c) => c.key),
      reading: cs.some(cellFailed) ? T.cellFail : T.record,
      validSamples: flat(cs).length,
      beforeKbdUnreachable: flat(cs.filter((c) => c.screen === 'before')).filter(isKu).length,
      afterUnreachExcl: cs.filter((c) => c.screen === 'after').reduce((n, c) => n + byCell[c.key].filter(isAfterUr).length, 0),
      judgedBy: 'HQA'
    };
  }

  const mism = samples.filter((s) => eff.get(s) && eff.get(s).mismatch).length;
  if (mism) redReasons.push(`${D.validMismatch} ${mism}건`);
  if (unplanned.length) redReasons.push(`계획 밖 표본 ${unplanned.length}건`);
  const bad = (extra && extra.badLines) || [];
  if (bad.length) redReasons.push(`읽지 못한 줄 ${bad.length}건`);
  if (extra && extra.samplesMissing) redReasons.push('samples.jsonl 없음');
  if (samples.length === 0) redReasons.push('표본 0');

  const pending = Object.keys(rules.hqaPending).filter((k) => !k.startsWith('_') && String(rules.hqaPending[k].status).includes('HQA 회신 대기'));
  return {
    marker: T.marker,
    rulesVersion: rules.rulesVersion,
    rulesSha256: (extra && extra.rulesSha256) || null,
    totals: {
      planned: plan.cells.length, samples: samples.length, valid: samples.filter(isValid).length, unplanned: unplanned.length, badLines: bad.length,
      kbdUnreachable: samples.filter(isKu).length, afterUnreachExcl: samples.filter(isAfterUr).length, validMismatch: mism
    },
    deviceReadingText: { device_failure: D.device_failure, keyboard_unreachable: D.keyboard_unreachable },
    red: redReasons.length > 0,
    redReasons,
    hqaPendingOpen: pending,
    notMeasured: rules.plan.notMeasured,
    items,
    cells
  };
}

function argOf(argv, name) {
  const i = argv.indexOf(name);
  return i >= 0 && i + 1 < argv.length ? argv[i + 1] : null;
}

function main(argv) {
  const outDir = process.env.HQA_OUT_DIR || null;
  const rulesPath = argOf(argv, '--rules') || path.join(__dirname, 'rules.json');
  const samplesPath = argOf(argv, '--samples') || (outDir ? path.join(outDir, 'samples.jsonl') : null);
  const outPath = argOf(argv, '--out') || (outDir ? path.join(outDir, 'verdict-device.json') : null);
  if (!samplesPath || !outPath) {
    process.stderr.write('judge: --samples/--out 또는 HQA_OUT_DIR 필요\n');
    return 2;
  }
  let rulesBuf;
  let rules;
  try {
    rulesBuf = fs.readFileSync(rulesPath);
    rules = JSON.parse(rulesBuf.toString('utf8'));
  } catch (e) {
    process.stderr.write(`judge: 규칙 파일 읽기 실패 — ${String(e.message).slice(0, 200)}\n`);
    return 2;
  }
  let text = '';
  let samplesMissing = false;
  if (fs.existsSync(samplesPath)) text = fs.readFileSync(samplesPath, 'utf8');
  else samplesMissing = true;
  const parsed = parseJsonl(text);
  const verdict = judge(rules, parsed.samples, {
    badLines: parsed.bad, samplesMissing,
    rulesSha256: crypto.createHash('sha256').update(rulesBuf).digest('hex')
  });
  fs.writeFileSync(outPath, JSON.stringify(verdict, null, 2) + '\n', 'utf8');
  process.stdout.write(JSON.stringify({ red: verdict.red, redReasons: verdict.redReasons.slice(0, 10), totals: verdict.totals }) + '\n');
  return verdict.red ? 1 : 0;
}

module.exports = { buildPlan, cellKey, judge, parseJsonl, defaultWindow, groupKey, kStructDefect, uChecks, kbdGate, kCheck, effective };

if (require.main === module) process.exitCode = main(process.argv.slice(2));
