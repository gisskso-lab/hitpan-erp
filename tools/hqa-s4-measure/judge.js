#!/usr/bin/env node
'use strict';
// 장치 판독기(설계 §4-3 · M7) — samples.jsonl + rules.json → verdict-device.json.
// · 판독 글자는 rules.json readingText(정본 글자)만 쓴다. 기대값을 새로 만들지 않는다. 판정자 = HQA.
// · 고침4 G4(D-3): 칸 실패 = 유효 표본 수 < 정해진 표본 수(rules hqaPending.samplesPerCell) → 「장치 실패」 + 빨강 · 사유 「정해진 표본 수 모자람 유효/정해진」.
//   /before 칸 표본 0 도 빨강(「대조 무효」 와 다름: 무효 = 표본 있음 · 재현 0). (옛 꼴 = 칸 유효 0 일 때만 실패 — 고침4 로 대체됨)
// · 고침4 G1: 화면 = rules.plan.screenTable 의 이름(before · after · a2) · 가르기는 이름 같음 비교만(주소 앞머리 판단 0)
// · 고침4 G3: rules.n1Contrast 화면을 N-1 대조로 읽는다(D-1 ⓒ) · 그것이 before 가 아니면 /before N-1 칸 = 기록만 · 판정에 안 씀
// · 고침4 G6 순서 · G7 판정 칸 가림 충돌 → 빨강
// · 고침5 H1(작업지시서 §7): 분모 칸(H1-1) · 머리 해시 · 계획 · 브라우저 조건(H1-2) · 전 항목 칸 넷(H1-3 · H1-4) · 회차 글자(H1-5)
//   · 노출 표지(H1-6) · /after 제외 판독 도움(H1-7) · I-4 닫힌 뒤 초점(H1-8) · 열린 칸 셈 = 관문 꼴(H1-9) — 옛 칸은 모두 그대로 둔다(#1)
// 쓰는 법: node judge.js [--rules <rules.json>] [--samples <samples.jsonl>] [--out <verdict-device.json>] [--withheld <withheld.json>]
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

// ── 고침4 G1 — 화면 표(이름 ↔ 주소 ↔ 기대 data-variant ↔ 기대 CSS 판) · 화면 가르기 = 이름 같음 비교만 ──
function screenSpec(rules, screen) {
  const t = rules.plan && rules.plan.screenTable;
  const s = t && typeof screen === 'string' && !screen.startsWith('_') && Object.prototype.hasOwnProperty.call(t, screen) ? t[screen] : null;
  if (!s || typeof s.path !== 'string' || typeof s.variant !== 'string' || typeof s.css !== 'string') throw new Error(`plan.screenTable 에 화면 ${screen} 없음`);
  return s;
}
// 그 항목이 재는 화면 목록(itemSpecs.screens · 없으면 plan.screens)
//   H4 §7-1: 경우(kase)를 주면 itemSpecs.caseScreens[경우] 화면을 뒤에 더함(I-4 kbd-open + e1t3) · 경우 없이 부르면 옛 꼴 그대로
function screensFor(rules, item, kase) {
  const spec = rules.plan.itemSpecs[item];
  const base = (spec && Array.isArray(spec.screens)) ? spec.screens : rules.plan.screens;
  const cs = spec && spec.caseScreens && typeof kase === 'string' && Array.isArray(spec.caseScreens[kase]) ? spec.caseScreens[kase] : [];
  return base.concat(cs.filter((s) => !base.includes(s)));
}
function n1ContrastOf(rules) {
  const c = rules.n1Contrast;
  if (typeof c !== 'string' || !c) throw new Error('n1Contrast 없음');
  return c;
}
// H4 §7-1 — 대조 화면 표(rules.contrastScreen) · 찾는 순서 = 「항목/경우」 → 「항목」 → before
//   표가 없는 옛 규칙 파일은 N-1 = n1Contrast · 나머지 = before(옛 동작 그대로)
function contrastTable(rules) {
  const t = rules.contrastScreen;
  return t && typeof t === 'object' && !Array.isArray(t) ? t : {};
}
function contrastScreenOf(rules, item, kase) {
  const t = contrastTable(rules);
  const own = (k) => (Object.prototype.hasOwnProperty.call(t, k) && typeof t[k] === 'string' && t[k] ? t[k] : null);
  if (item === 'N-1' && !own('N-1')) return n1ContrastOf(rules);
  return own(`${item}/${kase}`) || own(item) || 'before';
}
// 대조 화면 표의 화면 이름들(before · after 제외) — kbdGate 가 a2 와 같은 U1~U4 문을 거는 화면
function contrastScreensSet(rules) {
  const t = contrastTable(rules);
  const out = new Set([n1ContrastOf(rules)]);
  for (const k of Object.keys(t)) if (!k.startsWith('_') && typeof t[k] === 'string') out.add(t[k]);
  out.delete('before');
  out.delete('after');
  return out;
}
// 경우마다 대조 화면이 다른 항목인가(표에 「항목/경우」 열쇠가 있음 · I-4)
function caseSplitItem(rules, item) {
  return Object.keys(contrastTable(rules)).some((k) => k.startsWith(item + '/'));
}
// 같은 지연 단 안 재는 순서(rules.plan.screenOrder) — 목록 밖 화면은 -1
function screenRankOf(rules, screen) { return (rules.plan.screenOrder || []).indexOf(screen); }
// 표본 주소(주소 글자는 표에서만 · 장치 코드에 주소 글자 0)
function screenUrl(rules, screen) {
  const d = rules.device;
  return `http://${d.host}:${d.proxyPort}${screenSpec(rules, screen).path}${d.query}`;
}
// 측정 첫 상태(st0)로 화면 확정 — data-variant 만으로 화면을 정하고(다르면 throw) · CSS 판은 다르면 오류 칸(C-3)
function screenCheck(rules, screen, st0) {
  const sp = screenSpec(rules, screen);
  if (!st0 || st0.variant !== sp.variant) throw new Error(`화면 판 불일치 data-variant=${st0 ? st0.variant : null}`);
  const errors = [];
  if (st0.cssVariant !== sp.css) errors.push(`상태 칸 CSS 판 불일치 data-fixture-css=${st0.cssVariant}`);
  return { errors, cssMeasured: st0.cssVariant, variantMeasured: st0.variant };
}
function checkScreens(rules) {
  const P = rules.plan;
  const contrast = n1ContrastOf(rules);
  screenSpec(rules, contrast);
  // H4 §7-1: 대조 화면 표 단언 — N-1 칸 = n1Contrast(어긋나면 거부 · 옛 키 유지 #1) · 표의 화면은 screenTable 에 있어야 함
  const t = contrastTable(rules);
  if (Object.prototype.hasOwnProperty.call(t, 'N-1') && t['N-1'] !== contrast) throw new Error(`contrastScreen N-1=${t['N-1']} 이 n1Contrast=${contrast} 와 다름`);
  for (const k of Object.keys(t)) if (!k.startsWith('_')) screenSpec(rules, t[k]);
  const used = new Set();
  for (const item of P.items) {
    const spec = P.itemSpecs[item] || {};
    // H4: 옛 꼴 = 항목 화면 목록 하나 → 경우별 화면 목록(caseScreens) · 경우 없는 칸은 옛 목록
    const kases = Array.isArray(spec.cases) && spec.cases.length ? spec.cases : [undefined];
    for (const kase of kases) {
      for (const sc of screensFor(rules, item, kase)) {
        screenSpec(rules, sc);
        used.add(sc);
        // 화면 목록 밖 화면은 대조 화면 표가 그 항목(경우)에 가리킨 화면만(옛 꼴 = N-1 의 n1Contrast 하나만 · 다른 항목 칸 = 거부)
        if (!P.screens.includes(sc) && contrastScreenOf(rules, item, kase) !== sc) throw new Error(`${item}${kase ? '/' + kase : ''} 의 화면 ${sc} 는 계획 밖(그 항목 · 경우의 대조 화면만 허용)`);
        if (screenRankOf(rules, sc) < 0) throw new Error(`plan.screenOrder 에 ${sc} 없음`);
      }
    }
  }
  if (P.items.includes('N-1') && !screensFor(rules, 'N-1').includes(contrast)) throw new Error(`n1Contrast=${contrast} 가 N-1 화면에 없음`);
  // H4: 표가 가리킨 대조 화면이 그 항목(경우) 칸에 실제로 계획돼 있어야 함(계획 안 된 대조 = 조용한 무효 막기)
  for (const k of Object.keys(t)) {
    if (k.startsWith('_')) continue;
    const [item, kase] = k.split('/');
    if (!P.items.includes(item)) continue;
    const spec = P.itemSpecs[item] || {};
    const kases = kase ? [kase] : (spec.cases || []);
    for (const kk of kases) {
      if (!(spec.cases || []).includes(kk) || !screensFor(rules, item, kk).includes(t[k])) throw new Error(`contrastScreen ${k}=${t[k]} 칸이 계획에 없음`);
    }
  }
}
// 고침4 보탬(PM F-6 ②): 칸의 지연 목록 = itemSpecs.screenDelays[화면](있으면) · 없으면 itemSpecs.delays
//   화면 목록 밖 화면(a2)은 판정 대조 단(n1ContrastDelays)만 — 그 밖 단(0 · 150)이 하나라도 있으면 거부
function delaysFor(rules, item, screen) {
  const P = rules.plan;
  const spec = P.itemSpecs[item];
  const sd = spec.screenDelays;
  const name = sd && Object.prototype.hasOwnProperty.call(sd, screen) ? sd[screen] : spec.delays;
  const delays = P[name];
  if (!Array.isArray(delays)) throw new Error(`plan.${name} 없음`);
  // H4 결-H3: 대조 단 제한은 N-1 의 대조 화면(a2)에만(e1t3 · v2 = 항목 지연 전부 · 결과로 단을 고르지 않음)
  if (!P.screens.includes(screen) && item === 'N-1') {
    const cd = P.n1ContrastDelays;
    if (!Array.isArray(cd)) throw new Error('plan.n1ContrastDelays 없음');
    const out = delays.filter((d) => !cd.includes(d));
    if (out.length) throw new Error(`${item} 의 화면 ${screen} 지연 ${out.join(' · ')} 는 계획 밖(대조 단 ${cd.join(' · ')} 만)`);
  }
  return delays;
}
// 고침4 보탬(PM F-6 ③): 「기록만」 칸(n1Contrast ≠ before 일 때 /before N-1) — 원자료 · verdict 표지(그 칸의 장치 실패 = 장치 건강 신호 · 판정 무관)
//   H4 §7-1: 일반화 — 대조 화면이 before 가 아닌 항목(경우)의 /before 칸 = 기록만(I-4 kbd-open · I-8 · N-1) · N-1 글자는 옛 글자 그대로
function recordOnlyTag(rules, cell) {
  if (!cell || cell.screen !== 'before') return null;
  if (cell.item === 'N-1') {
    if (n1ContrastOf(rules) === 'before') return null;
    const t = rules.readingText['N-1'];
    return { role: t.recordOnly, failTag: t.recordOnlyHealth };
  }
  if (contrastScreenOf(rules, cell.item, cell.case) === 'before') return null;
  const h = rules.readingText.h4;
  if (!h || typeof h.recordOnly !== 'string') throw new Error('readingText.h4 없음');
  return { role: h.recordOnly, failTag: h.recordOnlyHealth };
}
// 고침4 보탬(PM F-6 ④): probe a2 R1 이 읽은 화면 판 속성(data-variant · data-fixture-css) ↔ plan.screenTable.a2 — 다르면 장치 빨강(되돌림 판단과 별개)
function a2VariantCheck(rules, variant, css) {
  const sp = screenSpec(rules, 'a2');
  const red = variant !== sp.variant || css !== sp.css;
  return {
    variant, css, expected: { variant: sp.variant, css: sp.css },
    device: red ? 'red' : 'ok',
    deviceReason: red ? `${rules.deviceReading.a2ProbeVariantDiff} data-variant=${variant} · data-fixture-css=${css}` : null
  };
}

function buildPlan(rules) {
  const P = rules.plan;
  const win = defaultWindow(rules);
  const cells = [];
  checkScreens(rules);
  for (const item of P.items) {
    const spec = P.itemSpecs[item];
    if (!spec) throw new Error(`plan.itemSpecs 에 ${item} 없음`);
    const windows = (spec.windows || [{}]).map((w) => ({ w: w.w || win.w, h: w.h || win.h }));
    const n = samplesFor(rules, spec.derivedFrom || item);
    for (const kase of spec.cases) {
      // 고침4 G1: 옛 꼴 = P.screens 고정 → 항목별 화면 목록(screensFor) · H4: 경우별 화면(caseScreens) 더함
      for (const screen of screensFor(rules, item, kase)) {
        // 고침4 보탬(PM F-6 ②): 옛 꼴 = 항목 지연 목록 하나(P[spec.delays]) → 화면별 지연 목록(delaysFor · a2 = 대조 단만)
        for (const delay of delaysFor(rules, item, screen)) {
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
//   고침4 G2: a2 미도달 = /before 와 같은 U1~U4 문(옛 꼴 = before 아니면 모두 「/after …」 글자 → a2 를 따로 뺐다)
//             a2 사유 글자 = 「A2 화면 키보드 도달 0 …」(주소 글자 0 · CTO C-4)
function kbdGate(rules, s, afterReached) {
  const D = rules.deviceReading;
  if (s.screen === 'after') return { valid: false, reading: 'device_failure', reason: D.afterUnreachable, u: null };
  // H4: 대조 화면 표의 비교 화면(e1t3 등)도 a2 와 같은 U1~U4 문(옛 꼴 = before · a2 만)
  if (s.screen !== 'before' && s.screen !== 'a2' && !contrastScreensSet(rules).has(s.screen)) return { valid: false, reading: 'device_failure', reason: '계획 밖 화면', u: null };
  const u = uChecks(s, afterReached);
  const miss = Object.keys(u).filter((x) => !u[x]);
  if (miss.length === 0) return { valid: true, reading: 'keyboard_unreachable', reason: null, u };
  const head = s.screen === 'a2' ? D.a2Unreachable : (s.screen === 'before' ? '/before 키보드 도달 0' : `${D.contrastUnreachable} ${s.screen}`);
  return { valid: false, reading: 'device_failure', reason: `${head} · ${miss.join('·')} 결손`, u };
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

// ── 고침4 G7 — 판정 칸 가림 충돌: 표본의 판정 칸(rules.judgeSchema samples.jsonl)에 가림 꼴(mask.js findSecrets)이 걸리는가 ──
//   돌려줌: [{ path, kind, length }] (값 0)
function maskConflictCtx(rules) {
  const M = require('./mask.js');
  return { M, pats: M.schemaFor(rules, 'samples.jsonl'), allow: M.loadAllow(), hctx: M.hexCtx(process.env, 'samples.jsonl') };
}
function maskConflicts(rules, s, mc) {
  const c = mc || maskConflictCtx(rules);
  const out = [];
  if (!s || typeof s !== 'object' || !c.pats.length) return out;
  for (const [p, v] of c.M.schemaValues(s, c.pats)) {
    const str = typeof v === 'string' ? v : (v && typeof v === 'object' ? JSON.stringify(v) : null);
    if (str === null) continue;
    for (const h of c.M.findSecrets(str, c.allow, c.hctx)) out.push({ path: c.M.pathStr(p), kind: h.kind, length: h.tok.length });
  }
  return out;
}

// ── 고침5 H1 — 판독 칸 넷 · 회차 글자 · 노출 표지 · 제외 판독 도움 함수 ──
// 판독 글자 묶음(rules.readingText.fix5) — 없으면 규칙 파일 오류(throw → 종료 2)
function fix5Text(rules) {
  const t = rules.readingText && rules.readingText.fix5;
  if (!t || typeof t !== 'object') throw new Error('readingText.fix5 없음');
  return t;
}
// 고침6 결-11 X2: 대조 무효 사유 글자 = 그 칸 대조 화면 이름을 넣은 글자(readingText.fix5 의 화면 꼴 새 키 둘 · 아래 표)
//   /before 칸이면 옛 글자(contrastNoRepro · contrastNoStimulus)와 같은 글자 · 새 키가 없는 옛 규칙 파일 = 옛 글자 그대로 · 주소 글자 0
//   새 키 이름은 21자 이하(P3-3 · 가림 대비) — 작업지시서 글자 contrastNoStimulus+Screen(24자) 대신 contrastNoStimScreen
const CONTRAST_REASON_KEYS = { contrastNoRepro: 'contrastNoReproScreen', contrastNoStimulus: 'contrastNoStimScreen' };
function contrastScreenLabel(rules, screen) {
  const t = fix5Text(rules).contrastScreenLabel;
  return t && typeof t === 'object' && typeof t[screen] === 'string' ? t[screen] : `비교 화면 ${screen}`;
}
function contrastReasonText(rules, key, screens) {
  const F5 = fix5Text(rules);
  const tpl = F5[CONTRAST_REASON_KEYS[key]];
  if (typeof tpl !== 'string') return F5[key];
  const ss = Array.from(new Set(screens || []));
  const label = (ss.length ? ss : ['before']).map((s) => contrastScreenLabel(rules, s)).join(' · ');
  return tpl.split('{screen}').join(label);
}
// 결-6 甲: 판정 칸(judgeSchema samples.jsonl) 값에 가린 자리표가 있는가 — 돌려줌 [{ path }] (값 0)
const MASKED_RE = /<가림:[0-9]+>/;
function maskedReads(rules, s, mc) {
  const c = mc || maskConflictCtx(rules);
  const out = [];
  if (!s || typeof s !== 'object' || !c.pats.length) return out;
  for (const [p, v] of c.M.schemaValues(s, c.pats)) {
    const str = typeof v === 'string' ? v : (v && typeof v === 'object' ? JSON.stringify(v) : null);
    if (str !== null && MASKED_RE.test(str)) out.push({ path: c.M.pathStr(p) });
  }
  return out;
}
// H1-6: 칸이 rules.exposure 항목에 걸리는가(항목에 없는 칸 = 아무 값) — 걸린 출처 목록
function exposureOf(rules, cell) {
  const xs = Array.isArray(rules.exposure) ? rules.exposure : [];
  const hit = (e, k, v) => !Object.prototype.hasOwnProperty.call(e, k) || e[k] === v;
  return xs.filter((e) => e && typeof e === 'object' && hit(e, 'item', cell.item) && hit(e, 'screen', cell.screen) && hit(e, 'delay', cell.delay) && hit(e, 'case', cell.case))
    .map((e) => e.source);
}
// H1-7: Tab 경로 꼴(줄 · 줄 안 · 뒤 · BODY · 상태 단추 순서) — 두 경로가 같은 꼴인지 비교용
function pathShape(p) {
  return JSON.stringify(pathOnly(p).map((x) => [x.row, x.inRef, x.afterRef, x.body, x.isStatusActivator]));
}
// H1-4: 항목별 /before 실패 꼴(rules.readingText.beforeFailShape 에 defined=true 인 것만 · 꼴 함수는 여기 고정)
const BEFORE_FAIL = {
  'I-1': (s, ku) => ku,
  'I-3': (s, ku, R) => !ku && R.overlapAny === true,
  'I-4': (s, ku, R) => !ku && Number(R.openCountAfterEsc) >= 1,
  'I-6': (s, ku, R) => !ku && R.maxOpenCount >= 2,
  'I-7': (s, ku, R) => R.rowclickDelta > 0 || (Array.isArray(R.changedOtherRows) && R.changedOtherRows.length > 0),
  'N-1': (s, ku) => !ku && !!(s.circuit && s.circuit.dead),
  // H4 결-H2: I-4 키보드로 연 뒤 경우 전용(대조 화면 e1t3) — Esc 뒤 끝 초점이 같은 줄 상태 단추 아님
  'I-4/kbd-open': (s, ku, R) => !ku && R.focusOnSameRowBtn === false,
  // H4 §7-2: I-8 — 읽은 줄 하나라도 숨김 꼴(표본 단위 · 꼴 단위 판독은 i8Rows)
  'I-8': (s, ku, R) => i8Rows(R).some(I8_ROW_FAIL)
};
// H4 §7-2 — I-8 줄 하나(readings.blankRows[]) : 실패 꼴 = 숨김 클래스 ≥ 1 또는 보이는 단추 0 또는 탭 정지 0(11/7 리뷰서 L151)
//   기대(After) 구조 = 단추 ≥ 1 · 크기 > 0 · 숨김 0 · 탭 정지 1(L306 + L151 의 부정) — 맞음/어긋남 사실만 · 판정 = HQA
function i8Rows(R) { return R && Array.isArray(R.blankRows) ? R.blankRows.filter((r) => r && typeof r === 'object') : []; }
const I8_ROW_FAIL = (r) => Number(r.hidden) >= 1 || Number(r.visibleButtons) === 0 || Number(r.tabStops) === 0;
const I8_ROW_OK = (r) => Number(r.buttons) >= 1 && Number(r.visibleButtons) >= 1 && Number(r.hidden) === 0 && Number(r.tabStops) === 1;

// 고침4 G5: Tab 경로 한 칸을 스키마 칸만으로(태그 · role · 줄 번호 · 참거짓 · tabIndex · 순번)
const PATH_KEYS = ['i', 'row', 'tag', 'role', 'tabIndex', 'inRef', 'isStatusActivator', 'afterRef', 'body'];
function pathOnly(p) {
  if (!Array.isArray(p)) return [];
  return p.map((x) => { const o = {}; for (const k of PATH_KEYS) o[k] = x && Object.prototype.hasOwnProperty.call(x, k) ? x[k] : null; return o; });
}

// ── 고침4 G6 — 재는 순서 · 문맥 순번 점검(rules.plan.screenOrder) ──
//   전체 순번(order) 순으로 늘어놓았을 때 지연 단은 오름 · 같은 단 안 화면 순서는 screenOrder 대로(after → a2 → before)
//   문맥 순번(contextId): 부모 표본(parentCell 없음)끼리 겹침 0 · 파생 표본은 부모와 같은 문맥
function orderCheck(rules, samples) {
  const D = rules.deviceReading;
  const xs = samples.filter((s) => s && typeof s === 'object');
  const problems = [];
  const missing = xs.filter((s) => !Number.isInteger(s.order) || s.order < 1).length;
  if (missing) problems.push(`${D.orderMissing} ${missing}건`);
  const withOrder = xs.filter((s) => Number.isInteger(s.order) && s.order >= 1).sort((a, b) => a.order - b.order);
  let dup = 0;
  let broken = 0;
  for (let i = 1; i < withOrder.length; i++) {
    const a = withOrder[i - 1];
    const b = withOrder[i];
    if (a.order === b.order) { dup += 1; continue; }
    if (b.delay < a.delay || (b.delay === a.delay && screenRankOf(rules, b.screen) < screenRankOf(rules, a.screen))) broken += 1;
  }
  if (dup || broken) problems.push(`${D.orderBroken} ${dup + broken}건`);
  const ownCtx = new Map();
  let ctxBad = 0;
  for (const s of xs) {
    if (s.parentCell || s.contextId === null || s.contextId === undefined) continue;
    if (ownCtx.has(s.contextId)) ctxBad += 1;
    else ownCtx.set(s.contextId, s);
  }
  for (const s of xs) {
    if (!s.parentCell || s.contextId === null || s.contextId === undefined) continue;
    const p = ownCtx.get(s.contextId);
    if (!p || p.cell !== s.parentCell || p.sampleNo !== s.sampleNo) ctxBad += 1;
  }
  if (ctxBad) problems.push(`${D.contextBroken} ${ctxBad}건`);
  return problems;
}

// judge 쪽 유효 판단(표본의 valid 를 그대로 믿지 않고 미도달 표본은 kbdGate 로 다시 잰다)
//   고침4 G7: 판정 칸 가림 충돌 표본 = device_failure(사유 「판정 칸 가림 충돌」) — 다른 판단보다 먼저
function effective(rules, samples) {
  const afterReach = {};
  for (const s of samples) {
    if (s && s.screen === 'after' && s.kbd && s.kbd.reached === true) afterReach[groupKey(s)] = (afterReach[groupKey(s)] || 0) + 1;
  }
  const eff = new Map();
  const mc = maskConflictCtx(rules);
  for (const s of samples) {
    if (!s) continue;
    // 고침5 결-6 甲(가림 → 누출 → 판독): 판정 칸 값에 가린 자리표(<가림:n>)가 있으면 그 표본 = device_failure · 회차 「판정 불가/미통과」(H1-5)
    const masked = maskedReads(rules, s, mc);
    if (masked.length) {
      eff.set(s, { valid: false, reading: 'device_failure', reason: fix5Text(rules).roundMaskedRead, mismatch: false, maskedReads: masked });
      continue;
    }
    const conf = maskConflicts(rules, s, mc);
    if (conf.length) {
      eff.set(s, { valid: false, reading: 'device_failure', reason: rules.deviceReading.maskConflict, mismatch: false, maskConflicts: conf });
      continue;
    }
    // H4 §7-2: 입력 0 항목(itemSpecs.inputZero · I-8) — 입력 기록이 1 이상이거나 기록 칸이 없으면 그 표본 = device_failure
    const izSpec = rules.plan.itemSpecs[s.item];
    if (izSpec && izSpec.inputZero === true && (!Array.isArray(s.inputs) || s.inputs.length > 0)) {
      eff.set(s, { valid: false, reading: 'device_failure', reason: rules.readingText.h4.i8InputViolation, mismatch: false, inputViolation: Array.isArray(s.inputs) ? s.inputs.length : null });
      continue;
    }
    // J1-B(반려 [3-V] 2-b): I-4/kbd-open 표본(미도달 아님)의 초점 판독값이 참/거짓이 아니면 device_failure —
    //   실패 꼴 「focusOnSameRowBtn === false」 가 null · 빠짐 · 글자를 「실패 아님」 으로 읽어 통과로 새는 길을 막는다
    if (s.item === 'I-4' && s.case === 'kbd-open' && s.valid === true && !(s.kbd && s.kbd.reached === false) && s.deviceReading !== 'keyboard_unreachable'
      && typeof (s.readings || {}).focusOnSameRowBtn !== 'boolean') {
      eff.set(s, { valid: false, reading: 'device_failure', reason: rules.deviceReading.i4FocusNotBool, mismatch: false, focusNotBool: true });
      continue;
    }
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

// 고침5-B ⓐ(PM 결정 11/16): 시도 기록 줄 — run-items.js 가 칸마다 표본보다 먼저 samples.jsonl 에 한 줄(kind = ATTEMPT_KIND) 쓴다.
//   판독기는 이 줄을 표본에서 떼어 칸 · 항목 시도 수로만 쓴다(표본 수 · 순서 · 계획 밖 표본 셈에 안 들어감).
const ATTEMPT_KIND = 'attempt';
const isAttemptLine = (s) => !!(s && typeof s === 'object' && s.kind === ATTEMPT_KIND);

function judge(rules, samples, extra) {
  // 고침5-B ⓐ: 시도 기록 줄을 떼어 둔다(시도 줄 0 이면 옛 동작 그대로)
  const attemptLines = (Array.isArray(samples) ? samples : []).filter(isAttemptLine);
  samples = (Array.isArray(samples) ? samples : []).filter((s) => !isAttemptLine(s));
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
  // 고침5-B ⓐ: 칸별 시도 기록 수 · 계획 밖 시도 줄
  const attemptsByCell = {};
  const attemptUnplanned = [];
  for (const a of attemptLines) {
    if (Object.prototype.hasOwnProperty.call(byCell, a.cell)) attemptsByCell[a.cell] = (attemptsByCell[a.cell] || 0) + 1;
    else attemptUnplanned.push(a.cell || '(cell 없음)');
  }
  const redReasons = [];
  const contrast = n1ContrastOf(rules);
  // 고침5 H1: 판독 글자 묶음 · 같은 묶음(항목 · 경우 · 지연 · 창)의 /before 미도달 Tab 경로 꼴(H1-7 비교 기준)
  const F5 = fix5Text(rules);
  const beforeUrShapes = {};
  for (const s of samples) {
    if (!s || s.screen !== 'before' || !s.kbd || s.kbd.reached !== false) continue;
    (beforeUrShapes[groupKey(s)] = beforeUrShapes[groupKey(s)] || new Set()).add(pathShape(s.kbd.path));
  }

  const cells = plan.cells.map((c) => {
    const all = byCell[c.key];
    const valid = all.filter(isValid).length;
    const out = { key: c.key, item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, planned: c.samples, got: all.length, valid, invalid: all.length - valid };
    // 고침5-B ⓐ: 시도 기록 수(원자료 시도 줄 · 표본과 따로)
    out.attemptRecords = attemptsByCell[c.key] || 0;
    // 고침2 N1-d: 칸 판독 — device_failure(빨강) ↔ keyboard_unreachable(/before · a2 유효 표본 · 판정 = HQA)
    out.kbdUnreachable = all.filter(isKu).length;
    out.afterUnreachExcl = all.filter(isAfterUr).length;
    // 고침4 G5(L-2): /after 미도달 표본 — 유효에서 빼는 규칙 그대로 + 건수 · 표본 번호 · Tab 경로(스키마 칸만) 기록 · 판정 = HQA
    if (c.screen === 'after') {
      const ur = all.filter(isAfterUr);
      out.afterUnreachable = { count: ur.length, samples: ur.map((s) => s.sampleNo), tabPath: ur.map((s) => pathOnly(s.kbd && s.kbd.path)) };
      // 고침5 H1-7: 사유 글자는 칸이 실패하지 않아도 항상 · 제외 표본이 결함을 숨기지 않는지 판독 도움 칸(판정 = HQA)
      out.afterUnreachable.reason = F5.afterUrReason;
      const ref = beforeUrShapes[groupKey(c)] || null;
      const same = ur.length === 0 || !ref ? null : ur.some((s) => ref.has(pathShape(s.kbd && s.kbd.path)));
      out.exclusionReview = {
        count: ur.length, ofGot: all.length, sameShapeAsD1: same,
        reading: ur.length === 0 ? null : (same === null ? F5.exclNoRef : (same ? F5.exclSame : F5.exclDiff))
      };
    }
    // 고침5 H1-1: 칸별 누락(계획 − 받음 · 0 아래 없음) · 제외 사유별 수(서로 겹침 0: 가린 값 · 가림 충돌 → /after 미도달 → 그 밖 장치 실패)
    out.missing = Math.max(0, c.samples - all.length);
    out.excluded = { afterUnreachable: 0, deviceFailure: 0, maskConflict: 0, maskedRead: 0 };
    for (const s of all) {
      const e = eff.get(s);
      if (!e || e.valid) continue;
      if (e.maskedReads) out.excluded.maskedRead += 1;
      else if (e.maskConflicts) out.excluded.maskConflict += 1;
      else if (isAfterUr(s)) out.excluded.afterUnreachable += 1;
      else out.excluded.deviceFailure += 1;
    }
    // 고침5 H1-6: 노출 칸 표지(판정 · 표본 · 기대값 변경 0 — 표지만)
    const ex = exposureOf(rules, c);
    if (ex.length) out.exposed = { source: ex };
    // 고침4 G3(D-2): n1Contrast 가 before 아니면 /before N-1 칸 = 기록만 · 판정에 안 씀
    const ro = recordOnlyTag(rules, c);
    // H4: 옛 칸 n1Role(N-1) 그대로 · 일반화된 「기록만」 칸 표지 = 새 칸 recordOnlyRole(N-1 포함)
    if (ro && c.item === 'N-1') out.n1Role = ro.role;
    if (ro) out.recordOnlyRole = ro.role;
    // 고침4 G4(D-3): 칸 실패 = 유효 < 정해진 표본 수(옛 꼴 「유효 0 일 때만」 대체)
    if (valid < c.samples) {
      out.status = T.cellFail;
      out.reason = `${T.cellShortReason} ${valid}/${c.samples}`;
      if (valid === 0) out.zeroReason = c.screen === 'before' ? T.beforeZeroReason : (c.screen === 'a2' ? T.a2ZeroReason : (c.screen === 'after' ? T.cellZeroReason : T.h4.contrastZeroReason));
      if (out.afterUnreachExcl > 0) out.reason += ' · ' + D.afterUnreachable;
      out.deviceReading = 'device_failure';
      // 고침4 보탬(PM F-6 ③): 「기록만」 칸의 장치 실패 빨강은 그대로 · 「장치 건강 · 판정 무관」 표지로 판정 빨강과 글자로 가른다
      if (ro) out.healthOnly = ro.failTag;
      redReasons.push(ro ? `${ro.failTag} — ${out.reason}: ${c.key}` : `${out.reason}: ${c.key}`);
    } else {
      out.status = '표본 있음';
      out.deviceReading = out.kbdUnreachable > 0 ? 'keyboard_unreachable' : null;
      // 고침4 G2: a2 칸 글자 = 「A2 화면 키보드 도달 0」(주소 글자 0 · C-4)
      if (out.kbdUnreachable > 0) out.deviceReadingText = c.screen === 'a2' ? D.a2Unreachable : (c.screen === 'before' ? `${D.keyboard_unreachable} · ${D.sameShapeD1}` : `${D.contrastUnreachable} ${c.screen}`);
    }
    return out;
  });
  // 고침4 G4: 옛 꼴(유효 0 이면 실패) → 정해진 표본 수 모자람
  const cellFailed = (c) => validOf(c.key).length < c.samples;
  const cellsOf = (item, pred) => plan.cells.filter((c) => c.item === item && (!pred || pred(c)));
  const flat = (cs) => cs.reduce((a, c) => a.concat(validOf(c.key)), []);
  const R = (s) => s.readings || {};

  const items = {};
  const has = (item) => rules.plan.items.includes(item);

  // N-1 — 11/13 §6 판정 글자 그대로
  //   고침4 G1 · G3(D-1 ⓒ): 대조 칸 = rules.n1Contrast 화면(㉡ a2 · ㉠ before). 아래 before… 셈 이름은 「대조 칸」 자리다
  //   (D-1 ⓒ — N-1 에 한해 /before 를 A2 화면으로 읽는다 · 읽기 규칙 = rules.json readingText.N-1.contrastReadRule)
  if (has('N-1')) {
    const t = T['N-1'];
    const cd = rules.plan.n1ContrastDelays;
    const bCells = cellsOf('N-1', (c) => c.screen === contrast && cd.includes(c.delay));
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
    // 고침4 G3: 대조가 before 아니면 /before N-1 칸 = 기록만 · 판정에 안 씀(셈 · 판정 글자 무관 · 칸 장치 실패 빨강은 그대로)
    const recCells = contrast !== 'before' ? cellsOf('N-1', (c) => c.screen === 'before') : [];
    const recValid = flat(recCells);
    items['N-1'] = {
      source: t.source, ruleText: t.ruleText, expectedAfter: t.expectedAfter, expectedBefore: t.expectedBefore,
      contrastScreen: contrast, contrastReadRuleRef: contrast !== 'before' ? 'rules.json readingText.N-1.contrastReadRule' : null,
      deviceFail: fail.length > 0, failCells: fail,
      labels,
      // 고침2 N1-d: 「대조 무효」 글자는 그대로 · 사유 칸만 붙인다(대조 화면을 키보드로 못 연 표본이 있을 때) · 고침4 G2: a2 면 「A2 화면 …」 글자
      contrastReason: fail.length === 0 && reproduced === 0 && bKu > 0 ? (contrast === 'a2' ? D.n1ContrastReasonA2 : D.n1ContrastReason) : null,
      beforeRecordOnly: contrast !== 'before' ? {
        text: t.recordOnly,
        valid: recValid.length,
        circuitDead: recValid.filter((s) => s.circuit && s.circuit.dead).length,
        kbdUnreachable: recValid.filter(isKu).length,
        failCells: recCells.filter(cellFailed).map((c) => c.key),
        failTag: recCells.some(cellFailed) ? t.recordOnlyHealth : null
      } : null,
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
        const b = flat(cellsOf('N-1', (c) => c.screen === contrast && c.delay === d));
        const a = flat(cellsOf('N-1', (c) => c.screen === 'after' && c.delay === d));
        const row = { delay: d, beforeValid: b.length, beforeDead: b.filter((s) => s.circuit && s.circuit.dead).length, afterValid: a.length, afterDead: a.filter((s) => s.circuit && s.circuit.dead).length };
        if (contrast !== 'before') {
          const r = flat(cellsOf('N-1', (c) => c.screen === 'before' && c.delay === d));
          row.recordOnlyBeforeValid = r.length;
          row.recordOnlyBeforeDead = r.filter((s) => s.circuit && s.circuit.dead).length;
        }
        return row;
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

  // ── 고침5 H1-3 · H1-4: 전 항목(I-1~I-7 · N-1)에 칸 넷을 따로 — 옛 칸(contrast · reading · labels)은 그대로 둔다(#1) ──
  //   d1Reproduced = 관찰 사실(/before 키보드 도달 0 표본 수 / 받은 수) · contrastState = 성립 · 대조 무효 · 대조 기준 미정(장치 실패 칸이면 state null)
  //   notMeasured = 시도 · 원자료 0 일 때만 · deviceFailure = 장치 실패 칸 · 사유
  //   고침5-B ⓐ: 「시도 0」 = 그 항목 칸 전부 시도 기록 0 · 표본 0. 시도 기록 ≥ 1 인데 표본 0 = 장치 실패만(미측정 표지 0 · HQA Q2)
  //   (새 대조 칸 이름 = contrastState — I-6 · I-7 의 옛 contrast 글자 칸과 겹치지 않게)
  const cellOut = {};
  for (const c of cells) cellOut[c.key] = c;
  const shapes = rules.readingText.beforeFailShape || {};
  const fourCells = (item) => {
    const cs = cellsOf(item);
    const got = cs.reduce((n, c) => n + byCell[c.key].length, 0);
    const bCs = cs.filter((c) => c.screen === 'before');
    const bGot = bCs.reduce((n, c) => n + byCell[c.key].length, 0);
    // H4 §7-1: 대조 칸 = 칸마다 대조 화면 표(contrastScreenOf)가 가리키는 화면 · N-1 은 대조 단(n1ContrastDelays)만
    //   (옛 꼴 = N-1 이면 n1Contrast 화면 · 그 밖 항목은 before — 표가 없거나 「항목/경우」 열쇠가 없는 항목은 옛 꼴과 같은 칸)
    const isCc = (c) => c.screen === contrastScreenOf(rules, item, c.case) && (item !== 'N-1' || rules.plan.n1ContrastDelays.includes(c.delay));
    const ccs = cs.filter(isCc);
    const st = stateOf(item, ccs);
    const out4 = { state: st.state, reason: st.reason, cells: st.cells };
    let byCase = null;
    let legacy = null;
    if (caseSplitItem(rules, item)) {
      // 결-H4: 경우별 대조 · 항목 contrastState = 모든 경우 성립일 때만 성립 · 고침5 옛 판독(before 칸 · 항목 꼴 하나) = contrastStateFix5 로 남김(#1)
      byCase = {};
      for (const kase of rules.plan.itemSpecs[item].cases) byCase[kase] = Object.assign({ screen: contrastScreenOf(rules, item, kase) }, stateOf(item, ccs.filter((c) => c.case === kase)));
      const vals = Object.values(byCase);
      const H = rules.readingText.h4;
      if (vals.some((v) => v.state === null)) { out4.state = null; out4.reason = F5.contrastDeviceFail; }
      else if (vals.some((v) => v.state === F5.contrastUndefined)) { out4.state = F5.contrastUndefined; out4.reason = F5.contrastNoShapeReason; }
      else if (vals.every((v) => v.state === F5.contrastHolds)) { out4.state = F5.contrastHolds; out4.reason = null; }
      else { out4.state = F5.contrastInvalid; out4.reason = vals.find((v) => v.state !== F5.contrastHolds).reason; }
      out4.rule = H.contrastByCaseRule;
      const lg = stateOf(item, bCs, true);
      legacy = { state: lg.state, reason: lg.reason, cells: lg.cells };
    } else if (item === 'I-7') {
      // 보탬 G-3(K4 (가)): I-7 경우별 대조 칸 — below-list 장치 실패가 row-cell 대조 판독을 덮지 않게(항목 contrastState 옛 판독은 그대로)
      byCase = {};
      for (const kase of rules.plan.itemSpecs[item].cases) byCase[kase] = Object.assign({ screen: contrastScreenOf(rules, item, kase), rule: rules.readingText.h4.contrastByCaseRuleI7 }, stateOf(item, ccs.filter((c) => c.case === kase)));
    }
    const state = out4.state;
    const reason = out4.reason;
    const rows = out4.cells;
    const failCs = cs.filter(cellFailed);
    const att = cs.reduce((n, c) => n + (attemptsByCell[c.key] || 0), 0);
    const nm = got === 0 && att === 0;
    return {
      attempted: { records: att, cells: cs.filter((c) => (attemptsByCell[c.key] || 0) > 0).length, of: cs.length, text: F5.attemptedText },
      d1Reproduced: { count: flat(bCs).filter(isKu).length, of: bGot, text: F5.d1Text },
      contrastState: Object.assign({ state, reason, cells: rows }, out4.rule ? { rule: out4.rule } : {}),
      notMeasured: { flag: nm, reason: nm ? F5.notMeasuredReason : null },
      deviceFailure: { flag: failCs.length > 0, reasons: Array.from(new Set(failCs.map((c) => cellOut[c.key].reason))), cells: failCs.map((c) => c.key) },
      fields5: F5.legacyFields,
      ...(byCase ? { contrastByCase: byCase } : {}),
      ...(legacy ? { contrastStateFix5: legacy } : {})
    };
  };
  // H4: 대조 칸 판독 하나(칸 묶음 → state · reason · cells) — legacyItemShape = 참이면 항목 꼴 하나만(고침5 옛 판독)
  function stateOf(item, ccs, legacyItemShape) {
    const fnFor = (c) => {
      const ck = `${item}/${c.case}`;
      const k = !legacyItemShape && shapes[ck] && shapes[ck].defined === true ? ck : item;
      const sh = shapes[k];
      return sh && sh.defined === true ? (BEFORE_FAIL[k] || null) : null;
    };
    const rows = ccs.map((c) => {
      const v = validOf(c.key);
      const fn = fnFor(c);
      return { key: c.key, valid: v.length, kbdUnreachable: v.filter(isKu).length, reproduced: fn ? v.filter((s) => fn(s, isKu(s), R(s))).length : null };
    });
    let state = null;
    let reason = null;
    if (ccs.some(cellFailed)) reason = F5.contrastDeviceFail;
    else if (ccs.length ? ccs.some((c) => !fnFor(c)) : !(shapes[item] && shapes[item].defined === true)) { state = F5.contrastUndefined; reason = F5.contrastNoShapeReason; }
    else {
      const rep = rows.reduce((n, r) => n + r.reproduced, 0);
      const val = rows.reduce((n, r) => n + r.valid, 0);
      const ku = rows.reduce((n, r) => n + r.kbdUnreachable, 0);
      if (rep > 0) state = F5.contrastHolds;
      // ⬛ 옛 줄(고침5 · 대조 화면과 무관하게 「/before …」 글자): reason = ku > 0 && ku === val ? F5.contrastNoStimulus : F5.contrastNoRepro;
      else { state = F5.contrastInvalid; reason = contrastReasonText(rules, ku > 0 && ku === val ? 'contrastNoStimulus' : 'contrastNoRepro', ccs.map((c) => c.screen)); }
    }
    return { state, reason, cells: rows };
  }
  // H4 §7-2: I-8(T3) — 입력 0 구조 읽기 · 대조 화면 = contrastScreen I-8 · 판독 단위 = rules.t3ReadUnit 한 값만
  if (has('I-8')) {
    const t = T['I-8'];
    const H = T.h4;
    const unit = rules.t3ReadUnit;
    if (unit !== 'item' && unit !== 'cell') throw new Error(`t3ReadUnit=${unit} 는 item · cell 이 아님`);
    const kinds = rules.device.i8.kinds;
    // H4-G-B(검증팀장 [4] R-1 · PM 결정 · 대표 결정 ⓑ · 작업지시서 §7-2): item 단위 = 공백 꼴(itemKind) 줄로만 대조 · 판정
    //   빈 값 · null 꼴 = 꼴별 칸 그대로 기록만(표지 t3KindRecordOnly) · 항목 판정 · 장치 빨강에 끌어오지 않음 · cell 단위는 옛 그대로(세 꼴)
    const itemKind = rules.device.i8.itemKind;
    if (unit === 'item' && !kinds.includes(itemKind)) throw new Error(`device.i8.itemKind=${itemKind} 는 kinds 밖`);
    const judgedKind = (k) => unit === 'cell' || k === itemKind;
    const cs = cellsOf('I-8');
    const fail = cs.filter(cellFailed).map((c) => c.key);
    const cscr = contrastScreenOf(rules, 'I-8', 'blank-rows');
    const kindRead = (screen) => kinds.map((k) => {
      const v = flat(cs.filter((c) => c.screen === screen));
      const rows = v.reduce((a, s) => a.concat(i8Rows(R(s)).filter((r) => r.kind === k)), []);
      return { kind: k, samples: v.length, rows: rows.length, failShape: rows.filter(I8_ROW_FAIL).length, afterShapeMismatch: rows.filter((r) => !I8_ROW_OK(r)).length };
    });
    const noRows = [];
    const noRowsRecordOnly = [];
    for (const sc of screensFor(rules, 'I-8', 'blank-rows')) for (const kr of kindRead(sc)) if (kr.samples > 0 && kr.rows === 0) (judgedKind(kr.kind) ? noRows : noRowsRecordOnly).push(`${sc}:${kr.kind}`);
    if (noRows.length) redReasons.push(`I-8 ${H.t3NoKindRows} ${noRows.join(' · ')}`);
    const ccFail = cs.filter((c) => c.screen === cscr).some(cellFailed);
    const byKind = kindRead(cscr).map((kr) => Object.assign(kr, {
      contrast: ccFail || kr.rows === 0 ? null : (kr.failShape > 0 ? F5.contrastHolds : H.t3KindInvalid),
      recordOnly: judgedKind(kr.kind) ? null : H.t3KindRecordOnly
    }));
    const judged = byKind.filter((k) => judgedKind(k.kind));
    let state = null;
    let reason = null;
    if (ccFail) reason = F5.contrastDeviceFail;
    // 옛 해석(H4 · 대표 결정 ⓑ 로 대체): 거부권 = byKind.some(null) · item 성립 = byKind.some(성립) — 세 꼴 중 하나라도
    else if (judged.some((k) => k.contrast === null)) reason = H.t3NoKindRows;
    else {
      const holds = judged.every((k) => k.contrast === F5.contrastHolds);
      if (holds) state = F5.contrastHolds;
      // ⬛ 옛 줄(고침5): reason = F5.contrastNoRepro;
      else { state = F5.contrastInvalid; reason = contrastReasonText(rules, 'contrastNoRepro', [cscr]); }
    }
    items['I-8'] = {
      source: t.source, expected: [t.expected], contrastRule: t.contrastRule,
      contrastScreen: cscr, t3ReadUnit: unit, t3ReadUnitText: unit === 'item' ? H.t3UnitItem : H.t3UnitCell,
      deviceFail: fail.length > 0, failCells: fail,
      afterByKind: kindRead('after'),
      beforeRecordOnly: cscr !== 'before' ? { text: H.recordOnly, byKind: kindRead('before') } : null,
      reading: fail.length > 0 ? T.cellFail : T.record,
      judgedBy: 'HQA'
    };
    Object.assign(items['I-8'], fourCells('I-8'));
    // 네 칸의 대조(표본 단위)는 꼴 단위 판독으로 바꿔 둔다(한 단위만 · 표본 단위 판독은 쓰지 않음)
    items['I-8'].contrastState = { state, reason, unit, judgedKinds: judged.map((k) => k.kind), kinds: byKind, noRowsRecordOnly: noRowsRecordOnly.length ? { text: H.t3KindRecordOnly, rows: noRowsRecordOnly } : null };
  }
  for (const item of ['I-1', 'I-2', 'I-3', 'I-4', 'I-5', 'I-6', 'I-7', 'N-1']) if (items[item]) Object.assign(items[item], fourCells(item));
  // I-6 · I-7 의 옛 「재현됨」(항목 실패 재현) → 다른 단어 칸으로 가름(D1 재현과 섞지 않음 · 옛 칸 유지)
  for (const item of ['I-6', 'I-7']) if (items[item]) items[item].beforeFailRepro = { count: items[item].beforeReproduced, text: F5.beforeFailRepro };
  // H1-8: I-4 닫힌 뒤 초점 — 독립 판독 칸(두 경우 · 화면 각각 · 새 자극 0 · 있는 읽기에서)
  if (items['I-4']) {
    items['I-4'].focusAfterClose = rules.plan.itemSpecs['I-4'].cases.reduce((a, kase) => a.concat(['before', 'after'].map((sc) => {
      const v = flat(cellsOf('I-4', (c) => c.case === kase && c.screen === sc));
      return {
        case: kase, screen: sc, valid: v.length,
        onSameRowBtn: v.filter((s) => R(s).focusOnSameRowBtn === true).length,
        offSameRowBtn: v.filter((s) => R(s).focusOnSameRowBtn === false).length,
        noRead: v.filter((s) => typeof R(s).focusOnSameRowBtn !== 'boolean').length
      };
    })), []);
  }
  // H1-6: 노출 칸이 있는 항목에 독립성 제약 글자(판정 = HQA)
  for (const item of Object.keys(items)) {
    const ex = cells.filter((c) => c.item === item && c.exposed).map((c) => c.key);
    if (ex.length) { items[item].independenceNote = F5.independenceNote; items[item].exposedCells = ex; }
  }

  const mism = samples.filter((s) => eff.get(s) && eff.get(s).mismatch).length;
  if (mism) redReasons.push(`${D.validMismatch} ${mism}건`);
  // 고침4 G7: 판정 칸 가림 충돌(값 0 · 칸 경로 · 꼴 · 길이만)
  const conflicts = [];
  for (const s of samples) {
    const e = eff.get(s);
    if (e && e.maskConflicts) for (const x of e.maskConflicts) conflicts.push(Object.assign({ cell: s.cell || null, sampleNo: s.sampleNo || null }, x));
  }
  const conflictSamples = samples.filter((s) => eff.get(s) && eff.get(s).maskConflicts).length;
  if (conflictSamples) redReasons.push(`${D.maskConflict} ${conflictSamples}건`);
  // 고침4 G6: 재는 순서 · 문맥 순번
  const orderProblems = orderCheck(rules, samples);
  for (const p of orderProblems) redReasons.push(p);
  if (unplanned.length) redReasons.push(`계획 밖 표본 ${unplanned.length}건`);
  if (attemptUnplanned.length) redReasons.push(`${F5.attemptOutOfPlan} ${attemptUnplanned.length}건`);
  const bad = (extra && extra.badLines) || [];
  if (bad.length) redReasons.push(`읽지 못한 줄 ${bad.length}건`);
  if (extra && extra.samplesMissing) redReasons.push('samples.jsonl 없음');
  if (samples.length === 0) redReasons.push('표본 0');

  // 고침5 결-6 甲: 판정 칸에 가린 값을 읽은 표본(값 0 · 칸 경로만)
  const maskedSamples = samples.filter((s) => eff.get(s) && eff.get(s).maskedReads);
  if (maskedSamples.length) redReasons.push(`${F5.roundMaskedRead} ${maskedSamples.length}건`);
  // 고침5 H1-5 · K-2: 판독에 쓰는 원자료가 보류됐는가(withheld.json 의 파일 이름 끝 = roundInputFiles)
  const withheld = (extra && Array.isArray(extra.withheld)) ? extra.withheld : [];
  const wName = (w) => String(typeof w === 'string' ? w : (w && (w.rel || w.file || w.name)) || '');
  const inputWithheld = withheld.filter((w) => F5.roundInputFiles.some((f) => wName(w) === f || wName(w).endsWith('/' + f)));
  const roundReasons = [];
  if (conflictSamples) roundReasons.push(`${F5.roundMaskConflict} ${conflictSamples}건`);
  if (maskedSamples.length) roundReasons.push(`${F5.roundMaskedRead} ${maskedSamples.length}건`);
  if (inputWithheld.length) roundReasons.push(`${F5.roundWithheld} ${inputWithheld.length}개`);
  if (extra && extra.samplesMissing) roundReasons.push(F5.roundSamplesMissing);
  if (inputWithheld.length) redReasons.push(`${F5.roundWithheld} ${inputWithheld.length}개`);
  // 고침5 H1-9: 열린 칸 = 관문의 허용 닫힘 꼴이 아닌 칸(옛 꼴 = 「HQA 회신 대기」 글자 포함 여부 — 새 글자면 0 으로 거짓 표시라 대체)
  const G = require('./hqa-gate.js');
  const pending = G.openKeys(rules);
  // 고침5 H1-1: 분모 칸(Σ 칸 = totals · 옛 totals.planned = 칸 수 그대로 둠)
  const sum = (f) => cells.reduce((n, c) => n + f(c), 0);
  const excluded = {
    afterUnreachable: sum((c) => c.excluded.afterUnreachable), deviceFailure: sum((c) => c.excluded.deviceFailure),
    outOfPlan: unplanned.length, maskConflict: sum((c) => c.excluded.maskConflict), maskedRead: sum((c) => c.excluded.maskedRead)
  };
  // J1-B(반려 [3-V] 2-b): I-4/kbd-open 초점 판독값이 참/거짓이 아닌 표본 수(표본 device_failure 와 따로 빨강 사유)
  const focusNotBool = samples.filter((s) => eff.get(s) && eff.get(s).focusNotBool).length;
  if (focusNotBool) redReasons.push(`${D.i4FocusNotBool} ${focusNotBool}건`);
  // ⬛ 옛 줄(J1 c6 까지 · verdict 를 바로 돌려줌 · 머리 escWait 검사 0): return {
  const verdict = {
    marker: T.marker,
    rulesVersion: rules.rulesVersion,
    rulesSha256: (extra && extra.rulesSha256) || null,
    // 고침5 H1-2 · H1-5: 판독기 해시 · 장치 계획 · 브라우저 조건 · 지연 단위 사본 · 회차 글자
    judgeSha256: (extra && extra.judgeSha256) || null,
    devicePlan: { cells: plan.cells.length, plannedSamples: sum((c) => c.planned) },
    browserCondition: rules.hqaPending.browserCondition.value,
    delayUnit: rules.hqaPending.delayUnit.value,
    // 고침6 J1-7: I-4 Esc 기다림 · polling 사본(rules device.i4 · 옛 규칙 파일 = null)
    escWait: rules.device && rules.device.i4 && typeof rules.device.i4.escWaitMs === 'number' ? {
      ms: rules.device.i4.escWaitMs, openPollMs: rules.device.i4.openPollMs, ref: rules.device.i4.escWaitRef,
      pollAfterEscMaxMs: rules.device.i4.pollAfterEscMaxMs === undefined ? null : rules.device.i4.pollAfterEscMaxMs,
      menuZeroFailCases: Array.isArray(rules.device.i4.menuZeroFailCases) ? rules.device.i4.menuZeroFailCases : null
    } : null,
    round: { state: roundReasons.length ? F5.roundUnreadable : F5.roundReadable, reasons: roundReasons, red: roundReasons.length > 0 },
    totalsMeaning: { planned: F5.totalsPlannedMeaning },
    hiddenDefectSuspect: cells.filter((c) => c.exclusionReview && c.exclusionReview.sameShapeAsD1 === true).length,
    totals: {
      planned: plan.cells.length, samples: samples.length, valid: samples.filter(isValid).length, unplanned: unplanned.length, badLines: bad.length,
      kbdUnreachable: samples.filter(isKu).length, afterUnreachExcl: samples.filter(isAfterUr).length, validMismatch: mism,
      maskConflictSamples: conflictSamples, orderProblems: orderProblems.length,
      cells: plan.cells.length, plannedSamples: sum((c) => c.planned), gotSamples: sum((c) => c.got), missingSamples: sum((c) => c.missing),
      excluded, withheldFiles: withheld.length, maskedReadSamples: maskedSamples.length,
      // 고침5-B ⓐ: 시도 기록 줄(표본 수에 안 들어감)
      attemptRecords: attemptLines.length, attemptOutOfPlan: attemptUnplanned.length
    },
    hqaPendingOpenCount: pending.length,
    hqaPendingPlaceholder: G.placeholderKeys(rules),
    deviceReadingText: { device_failure: D.device_failure, keyboard_unreachable: D.keyboard_unreachable },
    n1Contrast: contrast,
    // H4 §7-1: 대조 화면 표 · T3 판독 단위 사본 · 미측정 셈(superseded 표지 줄은 미측정으로 안 셈 · 옛 notMeasured 칸은 그대로)
    contrastScreen: contrastTable(rules),
    t3ReadUnit: rules.t3ReadUnit === undefined ? null : rules.t3ReadUnit,
    notMeasuredOpen: (rules.plan.notMeasured || []).filter((x) => x && !x.superseded),
    red: redReasons.length > 0,
    redReasons,
    maskConflicts: conflicts,
    orderProblems,
    hqaPendingOpen: pending,
    notMeasured: rules.plan.notMeasured,
    items,
    cells
  };
  // J1-B(반려 [3-V] 2-b): I-4 칸이 계획에 있는데 머리 escWait 가 null 이거나 값이 깨졌으면 장치 빨강(규칙 값이 빠진 채 잰 회차를 통과로 안 셈)
  if (plan.cells.some((c) => c.item === 'I-4') && escWaitHeadBad(verdict.escWait)) {
    verdict.redReasons.push(D.i4EscWaitMissing);
    verdict.red = true;
  }
  return verdict;
}
// 머리 escWait 검사 — null · 기다림 · 관측 간격 · polling 상한이 양의 유한수 아님 · 메뉴 0 실패 경우가 배열 아님 = 깨짐
function escWaitHeadBad(h) {
  const pos = (v) => typeof v === 'number' && Number.isFinite(v) && v > 0;
  return !h || !pos(h.ms) || !pos(h.openPollMs) || !pos(h.pollAfterEscMaxMs) || !Array.isArray(h.menuZeroFailCases);
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
  // 고침5 K-2: --withheld <withheld.json> — 인자 없거나 파일 없으면 보류 0 · 있는데 못 읽으면 종료 2(보류를 0 으로 착각하지 않게)
  const withheldPath = argOf(argv, '--withheld');
  let withheld = [];
  if (withheldPath && fs.existsSync(withheldPath)) {
    try {
      const w = JSON.parse(fs.readFileSync(withheldPath, 'utf8'));
      withheld = Array.isArray(w) ? w : (w && Array.isArray(w.files) ? w.files : []);
    } catch (e) {
      process.stderr.write(`judge: 보류 목록 읽기 실패 — ${String(e.message).slice(0, 200)}\n`);
      return 2;
    }
  }
  let verdict;
  try {
    verdict = judge(rules, parsed.samples, {
      badLines: parsed.bad, samplesMissing, withheld,
      rulesSha256: crypto.createHash('sha256').update(rulesBuf).digest('hex'),
      // 고침5 H1-2: 판독기 자신(나무 파일 바이트)의 SHA-256
      judgeSha256: crypto.createHash('sha256').update(fs.readFileSync(__filename)).digest('hex')
    });
  } catch (e) {
    process.stderr.write(`judge: 규칙 오류 — ${String(e.message).slice(0, 200)}\n`);
    return 2;
  }
  fs.writeFileSync(outPath, JSON.stringify(verdict, null, 2) + '\n', 'utf8');
  process.stdout.write(JSON.stringify({ red: verdict.red, redReasons: verdict.redReasons.slice(0, 10), totals: verdict.totals }) + '\n');
  return verdict.red ? 1 : 0;
}

module.exports = {
  buildPlan, cellKey, judge, parseJsonl, defaultWindow, groupKey, kStructDefect, uChecks, kbdGate, kCheck, effective,
  // 고침4
  screenSpec, screensFor, n1ContrastOf, screenRankOf, screenUrl, screenCheck, checkScreens, maskConflicts, orderCheck, pathOnly,
  // 고침4 보탬(PM F-6 ② ③ ④)
  delaysFor, recordOnlyTag, a2VariantCheck,
  // 고침5 H1
  fix5Text, maskedReads, exposureOf, pathShape, BEFORE_FAIL, ATTEMPT_KIND, isAttemptLine,
  // H4
  contrastTable, contrastScreenOf, contrastScreensSet, caseSplitItem, i8Rows, I8_ROW_FAIL, I8_ROW_OK,
  // 고침6
  contrastScreenLabel, contrastReasonText
};

if (require.main === module) process.exitCode = main(process.argv.slice(2));
