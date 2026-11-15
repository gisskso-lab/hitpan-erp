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
  const plan = buildPlan(rules);
  const byCell = {};
  for (const c of plan.cells) byCell[c.key] = [];
  const unplanned = [];
  for (const s of samples) {
    if (s && Object.prototype.hasOwnProperty.call(byCell, s.cell)) byCell[s.cell].push(s);
    else unplanned.push(s && s.cell ? s.cell : '(cell 없음)');
  }
  const validOf = (key) => byCell[key].filter((s) => s.valid === true);
  const redReasons = [];

  const cells = plan.cells.map((c) => {
    const all = byCell[c.key];
    const valid = all.filter((s) => s.valid === true).length;
    const out = { key: c.key, item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, planned: c.samples, got: all.length, valid, invalid: all.length - valid };
    if (valid === 0) {
      out.status = T.cellFail;
      out.reason = c.screen === 'before' ? T.beforeZeroReason : T.cellZeroReason;
      redReasons.push(`${out.reason}: ${c.key}`);
    } else {
      out.status = '표본 있음';
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
    const reproduced = bValid.filter((s) => s.circuit && s.circuit.dead).length;
    const afterDead = aValid.filter((s) => s.circuit && s.circuit.dead).length;
    const afterOther = aValid.filter((s) => R(s).otherRowsChanged === true).length;
    const afterFocusOff = aValid.filter((s) => R(s).focusSameRowBtn === false).length;
    const labels = [];
    if (fail.length === 0) {
      if (reproduced === 0) labels.push(t.contrastInvalid);
      if (afterDead > 0 || afterOther > 0) labels.push(t.red);
      if (afterFocusOff > 0) labels.push(t.yellow);
    }
    items['N-1'] = {
      source: t.source, ruleText: t.ruleText, expectedAfter: t.expectedAfter, expectedBefore: t.expectedBefore,
      deviceFail: fail.length > 0, failCells: fail,
      labels,
      reading: fail.length > 0 ? T.cellFail : (labels.length ? labels.join(' · ') : T.record),
      counts: {
        beforeContrastValid: bValid.length, beforeCircuitDead: reproduced,
        afterValid: aValid.length, afterCircuitDead: afterDead, afterOtherRowsChanged: afterOther, afterFocusOffRowBtn: afterFocusOff,
        afterListNotClosed: aValid.filter((s) => R(s).listClosed === false).length,
        afterNotOpened: aValid.filter((s) => R(s).opened === false).length,
        beforeNotOpened: bValid.filter((s) => R(s).opened === false).length
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
      contrast: fail.length > 0 ? T.cellFail : (beforeReproduced === 0 ? t.contrastInvalid : '재현됨'),
      reading: fail.length > 0 ? T.cellFail : T.record,
      judgedBy: 'HQA'
    };
  };
  if (has('I-6')) items['I-6'] = contrastItem('I-6', (s) => R(s).maxOpenCount >= 2, [T['I-6'].expected]);
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
      judgedBy: 'HQA'
    };
  }

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
    totals: { planned: plan.cells.length, samples: samples.length, valid: samples.filter((s) => s && s.valid === true).length, unplanned: unplanned.length, badLines: bad.length },
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

module.exports = { buildPlan, cellKey, judge, parseJsonl, defaultWindow };

if (require.main === module) process.exitCode = main(process.argv.slice(2));
