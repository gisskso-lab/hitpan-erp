#!/usr/bin/env node
'use strict';
// 판독기 자체시험 + 측정 스크립트 정적 검사(설계 §4-4 · §4-5). 브라우저 · 앱 · 망 0 — node 만.
// ① 손 JSONL 4벌 → judge.js(명령줄 그대로) → 기대 판독과 대조
//    A after 회로 죽음 → 🔴 · B before 재현 0 → 대조 무효 · C 칸 표본 0 → 빨강 · D 정상 → 기록
// ② 계획 꼴(칸 수 · 파생 칸 · 미측정 글자 · 정본 글자) 대조
// ③ 정적 검사: 측정 스크립트에 금지 입력 0 — 대조군(일부러 심은 나쁜 원문)이 반드시 걸려야 검사가 살아 있다고 본다
// ⑤ 고침2 N1-e 손 JSONL(미도달 · U1~U4) ⑥ K 판정 · K-장치 음성 대조 ⑦ 가짜 page 로 미도달 뒤 키 입력 0 ⑧ probe 정적 검사 + 심은 위반 대조군
// 종료 코드: 0 = 전부 기대대로 · 1 = 하나라도 다름
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const { buildPlan, cellKey, defaultWindow } = require('./judge.js');
// ── 고침7 K1(작업지시서 20261117작 §8 안 (가) · C2-1~C2-4) — 판독기 입구 · 비켜 감 · 호출 셈 ──
//   이 셋은 자체시험 파일에만 있다(판독기 judge.js 는 두 칸을 읽기만 · C2-3).
//   입구 규칙: ① I-4 표본 readings 에 두 칸(hqaFocusRow1Restored · menuZeroObserved)이 모두 없을 때만 덧붙임
//     — HQA 칸 = 판독기에 넘기는 그 순간의 장치 값(focusOnSameRowBtn) 그대로 · menuZeroObserved = 장치 값이 참/거짓이면 참
//   ② 두 칸 중 하나라도 있으면 그 표본은 손대지 않음 ③ I-4 밖 표본 · 다른 키 무변
//   ④ 원 입력은 바꾸지 않음 · 손대는 표본(과 그 readings)만 사본 · 손 안 대는 표본은 같은 객체
//   ⑤ readings 가 없거나 장치 값 칸 자체가 없는 표본은 손대지 않음
//   셈의 눈(C2-2): 실린 판독기 모듈의 judge 를 한 번 감쌈(같은 프로세스의 require('./judge.js') = 같은 객체) ·
//   심은 위반 사본은 사본을 싣는 함수(mutantJudge) 하나에서 감쌈 · 고침 전 판독기(비교용)는 감싸지 않음(PM 재개 결재)
const K1_ENTRY = Symbol('k1-entry');
const K1_PRE = Symbol('k1-pre');
const K1_TAG = Symbol('k1-bypass-tag');
const K1_CONTROL = Symbol('k1-control');
const K1_MM = Symbol('k1-planted-mismatch');
const K1_WRAPPED = Symbol('k1-wrapped');
const K1_BYPASS_OK = ['T7', 'T20'];
// 하한(C2-2): 고침 전 [3] 실측 = 한 런(바깥 + 자기 재실행 안쪽 · 명령줄 판독기 포함) 220 · 안쪽만(재실행 없음) 110
const K1_FLOOR_RUN = 220;
const K1_FLOOR_INNER = 110;
// 고침 전 판독기(바닥 커밋의 judge.js) 내용 sha256(줄끝 LF) — 나무 이력에서 이 값과 같은 판을 찾아 비교용으로 싣는다
const K1_PRE_LF_SHA256 = '5b83faf564a077e13c152e0d26108ccf23a25afa5773a1dd7469ccf53df3a840';
const K1 = { calls: [], cli: [], entryBad: [], entryCalls: 0, entryTouched: 0, t19: [], t21: [], t23: [], t25: [], t25checked: 0, t25mm: 0, judged: 0, pre: { calls: 0, from: null, err: null }, inner: null, out: null };
const k1Own = (o, k) => Object.prototype.hasOwnProperty.call(o, k);
const k1Touchable = (s) => !!s && typeof s === 'object' && s.item === 'I-4' && !!s.readings && typeof s.readings === 'object' && k1Own(s.readings, 'focusOnSameRowBtn');
const k1NeedsTwo = (s) => k1Touchable(s) && !k1Own(s.readings, 'hqaFocusRow1Restored') && !k1Own(s.readings, 'menuZeroObserved');
function k1Entry(samples, opt) {
  if (!Array.isArray(samples)) return samples;
  const before = JSON.stringify(samples);
  let touched = 0;
  const xs = samples.map((s) => {
    if (!k1NeedsTwo(s)) return s;
    touched += 1;
    const dev = s.readings.focusOnSameRowBtn;
    return Object.assign({}, s, { readings: Object.assign({}, s.readings, { hqaFocusRow1Restored: dev, menuZeroObserved: typeof dev === 'boolean' }) });
  });
  K1.entryCalls += 1;
  K1.entryTouched += touched;
  // T25 ①②③ — 이 호출의 입구 전/후 입력
  const bad = [];
  if (JSON.stringify(samples) !== before) bad.push('③ 원 입력 바뀜');
  xs.forEach((o, i) => {
    const s = samples[i];
    if (o === s) { if (k1NeedsTwo(s)) bad.push(`① ${i} 덧붙일 표본을 안 건드림`); return; }
    if (!k1NeedsTwo(s)) bad.push(`① ${i} 손대면 안 되는 표본`);
    const ka = Object.keys(s);
    if (JSON.stringify(ka) !== JSON.stringify(Object.keys(o)) || ka.some((k) => k !== 'readings' && o[k] !== s[k])) bad.push(`① ${i} 다른 키`);
    const ra = Object.keys(s.readings);
    if (JSON.stringify(Object.keys(o.readings)) !== JSON.stringify(ra.concat(['hqaFocusRow1Restored', 'menuZeroObserved'])) || ra.some((k) => o.readings[k] !== s.readings[k])) bad.push(`① ${i} readings 다른 칸`);
    if (o.readings.hqaFocusRow1Restored !== s.readings.focusOnSameRowBtn || o.readings.menuZeroObserved !== (typeof s.readings.focusOnSameRowBtn === 'boolean')) bad.push(`② ${i} 값 규칙`);
  });
  if (bad.length) K1.entryBad.push(bad.slice(0, 3));
  Object.defineProperty(xs, K1_ENTRY, { value: true });
  Object.defineProperty(xs, K1_PRE, { value: samples });
  if (opt && opt.mm) Object.defineProperty(xs, K1_MM, { value: true });
  return xs;
}
// 옛 꼴 시험(T7 · T20)만 쓰는 비켜 감 — 이름표를 붙여 판독기를 바로 부름
function k1Bypass(tag, mod, r, samples, extra) {
  const xs = Array.from(samples);
  Object.defineProperty(xs, K1_TAG, { value: tag });
  return mod.judge(r, xs, extra || {});
}
// 명령줄 판독기 입력 파일(입력 파일을 쓴 뒤 · 띄우기 직전에 한 번) — 이 프로세스가 띄운 명령줄 호출 셈
function k1Cli(file, viaEntry) {
  let xs = null;
  try { xs = fs.readFileSync(file, 'utf8').split(/\r?\n/).filter((l) => l.trim()).map((l) => JSON.parse(l)); } catch (e) { xs = null; }
  K1.cli.push({ viaEntry: viaEntry === true, i4: xs ? xs.filter((s) => s && s.item === 'I-4').length : null, needsTwo: xs ? xs.filter(k1NeedsTwo).length : null });
}
// T19 독립 판별(판독기 꼴과 따로 씀) · T19 · T21 · T23 문제 목록(판정 한 벌)
function k1Cls(s) {
  const X = s && s.readings && typeof s.readings === 'object' ? s.readings : {};
  if (typeof X.focusOnSameRowBtn !== 'boolean') return 'ownPath';
  if (X.hqaFocusRow1Restored === X.focusOnSameRowBtn) return 'match';
  return k1Own(X, 'hqaFocusRow1Restored') || k1Own(X, 'menuZeroObserved') ? 'mismatch' : 'notComparable';
}
function k1Problems(samples, v) {
  const p = { t19: [], t21: [], t23: [] };
  if (!v || !v.items) return p;
  const i4 = v.items['I-4'];
  if (i4) {
    const xs = (Array.isArray(samples) ? samples : []).filter((s) => s && s.item === 'I-4' && !require('./judge.js').isAttemptLine(s));
    const em = i4.exprMismatch;
    const c = em && em.classes;
    const mine = { ownPath: 0, match: 0, notComparable: 0, mismatch: 0 };
    for (const s of xs) mine[k1Cls(s)] += 1;
    if (!c || c.total !== xs.length || c.ownPath + c.match + c.notComparable + c.mismatch !== c.total) p.t19.push({ total: c && c.total, n: xs.length });
    else if (JSON.stringify([c.ownPath, c.match, c.notComparable, c.mismatch]) !== JSON.stringify([mine.ownPath, mine.match, mine.notComparable, mine.mismatch])) p.t19.push({ judge: c, mine });
    else if (em.count !== c.mismatch || em.notComparable.count !== c.notComparable || em.rows.length !== em.count || em.notComparable.rows.length !== em.notComparable.count) p.t19.push('count');
    else {
      const key = (r) => [r.case, r.screen, r.delay, r.sampleNo].join('|');
      const all = xs.map((s) => [s.case, s.screen, s.delay, s.sampleNo].join('|'));
      const uniq = new Set(all).size === all.length;
      const a = new Set(em.rows.map(key));
      if (uniq && em.notComparable.rows.some((r) => a.has(key(r)))) p.t19.push('겹침');
    }
    for (const r of i4.focusAfterClose || []) if (r.valid !== r.onSameRowBtn + r.offSameRowBtn + r.noRead + r.exprMismatchExcluded) p.t21.push(r);
  }
  for (const [item, it] of Object.entries(v.items)) {
    const cs = [];
    if (it.contrastState && Array.isArray(it.contrastState.cells)) cs.push(['contrastState', it.contrastState]);
    for (const [k, x] of Object.entries(it.contrastByCase || {})) cs.push([k, x]);
    for (const [k, x] of cs) {
      const ok = typeof x.exprMismatchChanged === 'boolean' && Number.isInteger(x.exprMismatchRemoved) && x.exprMismatchRemoved >= 0 && (!x.exprMismatchChanged || x.exprMismatchRemoved > 0);
      if (!ok) p.t23.push(`${item}:${k}`);
    }
  }
  return p;
}
const K1_NEW_KEYS = new Set(['exprMismatch', 'exprMismatchExcluded', 'exprMismatchChanged', 'exprMismatchRemoved', 'exposureOnly', 'reproducedCells']);
const k1Strip = (v) => JSON.parse(JSON.stringify(v), (k, x) => (K1_NEW_KEYS.has(k) ? undefined : x));
let k1PreMod;
function k1PreJudge() {
  if (k1PreMod !== undefined) return k1PreMod;
  k1PreMod = null;
  try {
    const crypto = require('crypto');
    const lg = spawnSync('git', ['log', '--format=%H', '--', 'judge.js'], { cwd: HERE, encoding: 'utf8' });
    for (const sha of (lg.stdout || '').split(/\r?\n/).filter(Boolean).slice(0, 60)) {
      const r = spawnSync('git', ['show', sha + ':./judge.js'], { cwd: HERE, encoding: 'utf8', maxBuffer: 1 << 24 });
      const s = (r.stdout || '').split(String.fromCharCode(13)).join('');
      if (crypto.createHash('sha256').update(s).digest('hex') !== K1_PRE_LF_SHA256) continue;
      const p = path.join(tmp, 'judge-pre-fix7.js');
      fs.writeFileSync(p, s.split("require('./mask.js')").join('require(' + JSON.stringify(path.join(HERE, 'mask.js')) + ')')
        .split("require('./hqa-gate.js')").join('require(' + JSON.stringify(path.join(HERE, 'hqa-gate.js')) + ')'), 'utf8');
      k1PreMod = require(p);
      K1.pre.from = sha.slice(0, 8);
      break;
    }
    if (!k1PreMod) K1.pre.err = '나무 이력에 고침 전 판독기 없음';
  } catch (e) { K1.pre.err = String(e && e.message ? e.message : e).slice(0, 120); }
  return k1PreMod;
}
function k1Wrap(mod, origin) {
  if (!mod || typeof mod.judge !== 'function' || mod[K1_WRAPPED]) return mod;
  const real = mod.judge;
  mod.judge = function k1Counted(r, samples, extra) {
    const isArr = Array.isArray(samples);
    const rec = { origin, entry: isArr && samples[K1_ENTRY] === true, tag: (isArr && samples[K1_TAG]) || null, control: isArr && samples[K1_CONTROL] === true, needsTwo: isArr ? samples.filter(k1NeedsTwo).length : 0 };
    K1.calls.push(rec);
    const do25 = origin === 'judge' && rec.entry && !!k1PreJudge();
    const a = do25 ? structuredClone(samples[K1_PRE]) : null;
    const b = do25 ? structuredClone(Array.from(samples)) : null;
    const v = real.apply(this, arguments);
    if (origin === 'judge') {
      K1.judged += 1;
      const p = k1Problems(samples, v);
      if (p.t19.length) K1.t19.push(p.t19[0]);
      if (p.t21.length) K1.t21.push(p.t21[0]);
      if (p.t23.length) K1.t23.push(p.t23[0]);
    }
    if (do25) {
      // T25 ④ 고침 전 판독기: 입구 전 · 뒤 입력 → 판정 통째 같음 / ⑤ 고침 뒤 판독기(입구 뒤) − 새 칸 = 고침 전(입구 전)
      try {
        const P = k1PreJudge();
        const va = P.judge(r, a, extra);
        const vb = P.judge(r, b, extra);
        K1.pre.calls += 2;
        K1.t25checked += 1;
        if (JSON.stringify(va) !== JSON.stringify(vb)) K1.t25.push('④');
        if (samples[K1_MM] === true) K1.t25mm += 1;
        else if (JSON.stringify(k1Strip(v)) !== JSON.stringify(va)) K1.t25.push('⑤');
      } catch (e) { K1.t25.push('비교 예외 ' + String(e && e.message).slice(0, 80)); }
    }
    return v;
  };
  Object.defineProperty(mod, K1_WRAPPED, { value: true });
  return mod;
}
// T26 · T27 판정(셈 기록 한 벌 → 칸) — T28 이 따로 만든 기록으로 이 함수가 진짜 잡는지 본다
function k1CallVerdict(calls, cli) {
  const byTag = {};
  for (const c of calls) if (c.tag) byTag[c.tag] = (byTag[c.tag] || 0) + 1;
  return {
    total: calls.length + cli.length,
    viaEntry: calls.filter((c) => c.entry).length + cli.filter((c) => c.viaEntry).length,
    bypass: calls.filter((c) => !c.entry && c.tag).length,
    byTag,
    missing: calls.filter((c) => !c.entry && !c.tag).length + cli.filter((c) => !c.viaEntry).length,
    badBypass: Object.keys(byTag).filter((t) => !K1_BYPASS_OK.includes(t)),
    badFiles: cli.filter((c) => c.needsTwo !== 0).length,
    entryNeedsTwo: calls.filter((c) => c.entry && c.needsTwo > 0).length
  };
}
k1Wrap(require('./judge.js'), 'judge');

const HERE = __dirname;
const results = [];
function check(name, ok, info) { results.push({ name, ok: !!ok, info: info === undefined ? null : info }); }
// 고침5 K-1: 심은 위반(대조군) — 걸려야(빨강이어야) ok · 셈은 --out 의 plantedRed / plantedTotal
function plant(name, red, info) { check(name, red, info); results[results.length - 1].planted = true; }

const rules = JSON.parse(fs.readFileSync(path.join(HERE, 'rules.json'), 'utf8'));
const T = rules.readingText;

// ── ① 판독기 4벌 ───────────────────────────────────────────────────
const st = JSON.parse(JSON.stringify(rules));
st.plan.items = ['N-1', 'N-2'];
st.plan.nDelays = [300, 500];
// 고침4: 옛 시험(① · ⑤)은 ㉠ 꼴(n1Contrast before · N-1 화면 before/after)과 정해진 표본 수 1(임시값)로 돈다
//   — 정해진 수 1 이면 G4 「유효 < 정해진」 = 옛 「칸 유효 0 = 실패」 와 같은 뜻이라 옛 기대가 그대로 선다(samplesPerCell 값은 HQA 회신 뒤)
st.n1Contrast = 'before';
// H4: 대조 화면 표의 N-1 칸도 ㉠ 짝으로(buildPlan 단언 n1Contrast === contrastScreen["N-1"])
st.contrastScreen = Object.assign({}, st.contrastScreen, { 'N-1': 'before' });
delete st.plan.itemSpecs['N-1'].screens;
st.hqaPending.samplesPerCell.value = { default: 1, 'N-1': 1 };
const win = defaultWindow(st);
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'hqa-s4-selftest-'));
const rulesPath = path.join(tmp, 'rules-selftest.json');
fs.writeFileSync(rulesPath, JSON.stringify(st, null, 2), 'utf8');
// 고침4 ㉡ 꼴(n1Contrast a2 · N-1 화면 before/after/a2 · 정해진 수 1 임시값)
const st2 = JSON.parse(JSON.stringify(rules));
st2.plan.items = ['N-1', 'N-2'];
st2.plan.nDelays = [300, 500];
st2.n1Contrast = 'a2';
st2.hqaPending.samplesPerCell.value = { default: 1, 'N-1': 1 };
const rulesPath2 = path.join(tmp, 'rules-selftest-a2.json');
fs.writeFileSync(rulesPath2, JSON.stringify(st2, null, 2), 'utf8');
// 고침4 G4 꼴(㉠ · 정해진 수 5 임시값)
const st3 = JSON.parse(JSON.stringify(st));
st3.hqaPending.samplesPerCell.value = { default: 5, 'N-1': 5 };
const rulesPath3 = path.join(tmp, 'rules-selftest-n5.json');
fs.writeFileSync(rulesPath3, JSON.stringify(st3, null, 2), 'utf8');

function line(item, screen, delay, o) {
  const opt = o || {};
  const c = { item, case: item === 'N-1' ? 'arrows' : 'enter-twice', screen, delay, window: win };
  const bad = opt.valid === false;
  return JSON.stringify(Object.assign(c, {
    cell: cellKey(c), sampleNo: 1, valid: !bad,
    errors: bad ? ['손 표본 — 일부러 무효'] : [],
    readings: bad ? null : (item === 'N-1'
      ? { opened: true, listClosed: true, focusSameRowBtn: opt.focusOk !== false, otherRowsChanged: !!opt.other }
      : { pickDelta: 1 }),
    circuit: bad ? null : { dead: !!opt.dead, reconnectVisible: !!opt.dead, wsClosedNotNav: false, consoleCircuitError: false }
  }));
}
function n2all() { return [line('N-2', 'before', 300), line('N-2', 'before', 500), line('N-2', 'after', 300), line('N-2', 'after', 500)]; }

const sets = {
  'A_after회로죽음': [line('N-1', 'before', 300, { dead: true }), line('N-1', 'before', 500, { dead: true }), line('N-1', 'after', 300, { dead: true }), line('N-1', 'after', 500)].concat(n2all()),
  'B_before재현0': [line('N-1', 'before', 300), line('N-1', 'before', 500), line('N-1', 'after', 300), line('N-1', 'after', 500)].concat(n2all()),
  'C_칸표본0_before': [line('N-1', 'before', 300, { valid: false }), line('N-1', 'before', 500, { dead: true }), line('N-1', 'after', 300), line('N-1', 'after', 500)].concat(n2all()),
  'C2_칸표본0_after': [line('N-1', 'before', 300, { dead: true }), line('N-1', 'before', 500, { dead: true }), line('N-1', 'after', 300)].concat(n2all()),
  'D_정상': [line('N-1', 'before', 300, { dead: true }), line('N-1', 'before', 500), line('N-1', 'after', 300), line('N-1', 'after', 500)].concat(n2all())
};

// 고침4 G6: 원자료 표본엔 order(전체 순번) · contextId(문맥 순번)가 있다 — 손 표본에 순번이 하나도 없으면
//   run-items.js 와 같은 순서(지연 단 오름 → 같은 단 안 screenOrder → 적은 순서)로 붙인다. 하나라도 있으면 손대지 않는다(G6 시험용).
function canon(lines, r) {
  const objs = lines.map((l) => JSON.parse(l));
  if (objs.some((o) => o.order !== undefined || o.contextId !== undefined)) return objs;
  const rank = (sc) => r.plan.screenOrder.indexOf(sc);
  const idx = objs.map((o, i) => i).sort((a, b) => (objs[a].delay - objs[b].delay) || (rank(objs[a].screen) - rank(objs[b].screen)) || (a - b));
  idx.forEach((i, pos) => { objs[i].order = pos + 1; objs[i].contextId = pos + 1; });
  return objs;
}
const RULES_BY_PATH = { [rulesPath]: st, [rulesPath2]: st2, [rulesPath3]: st3 };
function runJudge(name, rp) {
  const rPath = rp || rulesPath;
  const sp = path.join(tmp, `${name}.jsonl`);
  const op = path.join(tmp, `${name}.json`);
  fs.writeFileSync(sp, k1Entry(canon(sets[name], RULES_BY_PATH[rPath])).map((o) => JSON.stringify(o)).join('\n') + '\n', 'utf8');
  k1Cli(sp, true);
  const r = spawnSync(process.execPath, [path.join(HERE, 'judge.js'), '--rules', rPath, '--samples', sp, '--out', op], { encoding: 'utf8' });
  const v = fs.existsSync(op) ? JSON.parse(fs.readFileSync(op, 'utf8')) : null;
  return { code: r.status, v, stderr: (r.stderr || '').slice(0, 200) };
}
const noInvented = (v) => !/🟢|PASS|통과 판정/.test(JSON.stringify(v));

{
  const { code, v } = runJudge('A_after회로죽음');
  const n = v && v.items['N-1'];
  check('A: 🔴 나옴', n && n.labels.includes(T['N-1'].red), n && n.labels);
  check('A: 대조 무효 아님', n && !n.labels.includes(T['N-1'].contrastInvalid));
  check('A: 빨강 아님 · 종료 0', v && v.red === false && code === 0, { code, red: v && v.red });
  check('A: 지어낸 글자 0', v && noInvented(v));
}
{
  const { code, v } = runJudge('B_before재현0');
  const n = v && v.items['N-1'];
  check('B: 대조 무효 나옴', n && n.labels.includes(T['N-1'].contrastInvalid), n && n.labels);
  check('B: 🔴 아님', n && !n.labels.includes(T['N-1'].red));
  check('B: 빨강 아님 · 종료 0', v && v.red === false && code === 0, { code });
}
{
  const { code, v } = runJudge('C_칸표본0_before');
  const n = v && v.items['N-1'];
  const cell = v && v.cells.find((c) => c.item === 'N-1' && c.screen === 'before' && c.delay === 300);
  check('C: 빨강 · 종료 1', v && v.red === true && code === 1, { code, reasons: v && v.redReasons });
  // 고침4 G4: 옛 기대(reason = 「/before 표본 0」) 대체 → reason = 「정해진 표본 수 모자람 0/1」 · 표본 0 글자는 zeroReason 칸
  check('C: 그 칸 = 장치 실패 · 정해진 표본 수 모자람 0/1 · /before 표본 0(zeroReason)', cell && cell.status === T.cellFail && cell.reason === `${T.cellShortReason} 0/1` && cell.zeroReason === T.beforeZeroReason, cell);
  check('C: 「대조 무효」 와 구별(무효 글자 안 나옴)', n && !n.labels.includes(T['N-1'].contrastInvalid) && n.reading === T.cellFail, n && n.reading);
}
{
  const { code, v } = runJudge('C2_칸표본0_after');
  const cell = v && v.cells.find((c) => c.item === 'N-1' && c.screen === 'after' && c.delay === 500);
  // 고침4 G4: 옛 기대(reason = 「칸 표본 0」) 대체 → zeroReason 칸
  check('C2: after 칸 표본 0 → 빨강 · 칸 표본 0(zeroReason)', v && v.red === true && code === 1 && cell && cell.zeroReason === T.cellZeroReason && cell.reason === `${T.cellShortReason} 0/1`, { code, cell });
}
{
  const { code, v } = runJudge('D_정상');
  const n = v && v.items['N-1'];
  check('D: 판정 글자 없음 → 기록', n && n.labels.length === 0 && n.reading === T.record, n && n.reading);
  check('D: N-2 = 기록 · 관찰 칸', v && v.items['N-2'].reading === T.record);
  check('D: 빨강 아님 · 종료 0', v && v.red === false && code === 0, { code });
  check('D: 머리 표지 = 판정 = HQA', v && v.marker === T.marker);
}
{
  // 계획 밖 표본이 섞이면 빨강(다른 회차 · 다른 규칙 섞임 방지)
  sets['E_계획밖'] = sets['D_정상'].concat([line('N-1', 'after', 150)]);
  const { code, v } = runJudge('E_계획밖');
  check('E: 계획 밖 표본 → 빨강', v && v.red === true && code === 1 && v.totals.unplanned === 1, v && v.redReasons);
}

// ── ② 계획 꼴 · 정본 글자 ────────────────────────────────────────────
{
  const plan = buildPlan(rules);
  // 고침4 G1: 옛 기대 100(I 84 + N-1 8 + N-2 8) 대체 → a2 는 N-1 에만 4칸 더함
  // 고침4 보탬(PM F-6 ②): 옛 기대 104(N-1 12 · a2 4칸) 대체 → a2 는 대조 단 300 · 500 두 칸만 → 102(I 84 + N-1 10 + N-2 8)
  const a2cells = plan.cells.filter((c) => c.screen === 'a2');
  const sumSamples = plan.cells.reduce((n, c) => n + c.samples, 0);
  // 고침5 결-10 甲: 옛 기대 102 · 342 대체 → I-7 below-list(F10) 6칸 더함 → 108(I 90 + N-1 10 + N-2 8) · 표본 360
  // H4 §7-1: 옛 기대 108 · 360 대체 → I-4 kbd-open e1t3 3칸 + I-8 9칸 → 120(I 90 + e1t3 3 + I-8 9 + N-1 10 + N-2 8) · 표본 396
  check('계획 칸 수 = 120(I 90 + e1t3 3 + I-8 9 + N-1 10 + N-2 8) · a2 칸 = N-1 2(지연 300 · 500) · 다른 항목 a2 0 · 계획 표본(3/5) 396', plan.cells.length === 120 && a2cells.length === 2 && a2cells.every((c) => c.item === 'N-1') && JSON.stringify(a2cells.map((c) => c.delay)) === '[300,500]' && sumSamples === 396, { cells: plan.cells.length, a2: a2cells.map((c) => c.delay), sumSamples });
  {
    const throwsP = (r) => { try { buildPlan(r); return null; } catch (e) { return String(e.message); } };
    const noSd = JSON.parse(JSON.stringify(rules)); delete noSd.plan.itemSpecs['N-1'].screenDelays;
    const allSd = JSON.parse(JSON.stringify(rules)); allSd.plan.itemSpecs['N-1'].screenDelays.a2 = 'nDelays';
    const e1 = throwsP(noSd); const e2 = throwsP(allSd);
    check('보탬②: buildPlan 이 a2 의 0 · 150 계획 거부(screenDelays 뺌 · a2 = nDelays 둘 다) · 대조군 지금 rules = 통과',
      e1 !== null && /지연 0 · 150 는 계획 밖/.test(e1) && e2 !== null && /지연 0 · 150 는 계획 밖/.test(e2) && throwsP(rules) === null, { e1, e2 });
  }
  check('N-2 파생 칸 8 = N-1 칸마다 1', Object.keys(plan.derived).length === 8 && Object.values(plan.derived).every((a) => a.length === 1));
  check('I-9 = I 항목 지연 0/150/300', JSON.stringify(rules.plan.iDelays) === '[0,150,300]');
  check('N-1 지연 0/150/300/500 · 대조 단 300/500', JSON.stringify(rules.plan.nDelays) === '[0,150,300,500]' && JSON.stringify(rules.plan.n1ContrastDelays) === '[300,500]');
  const i8 = rules.plan.notMeasured.find((x) => x.item === 'I-8');
  // H4 §7-1: 옛 기대(계획에 없음 · 측정 코드 0) 대체 → 옛 미측정 줄 글자 그대로 + superseded 표지 · 계획 · 측정 코드에 I-8
  check('I-8 T3 = 옛 미측정 줄 글자 그대로 + superseded 「H4 I-8 측정」 · 계획에 I-8 · 측정 코드 I-8', i8 && i8.text === '미측정 · 정의 후보 있음(11/7 작업지시서 L306 · 공백 상태 칸) · HQA 확인 대기' && i8.superseded === 'H4 I-8 측정' && rules.plan.items.includes('I-8') && Object.keys(Object.assign({}, require('./items/i-items.js'), require('./items/n-items.js'))).includes('I-8'));
  check('N-1 판정 글자 = 대조 무효 / 🔴 / 🟡', T['N-1'].contrastInvalid === '대조 무효' && T['N-1'].red === '🔴' && T['N-1'].yellow === '🟡');
  const pend = Object.keys(rules.hqaPending).filter((k) => !k.startsWith('_'));
  // 고침5 H1-12: 옛 단언(6칸 모두 「HQA 회신 대기」) 대체 → 칸 6 그대로 · ①② = HQA 회신 반영 꼴 · ③④⑤ = 사전 고정 꼴(댓글 자리표) · ⑥ T3 = 「HQA 회신 대기」 그대로(대표 자리)
  check('고침5 hqaPending 6칸 상태: ①② 반영 · ③④⑤ 사전 고정(자리표) · T3 회신 대기 무변', pend.length === 6 && pend.includes('T3')
    && ['S1toS4', 'rawFormat'].every((k) => /^HQA 회신 반영 · 댓글 [0-9]{6,}$/.test(rules.hqaPending[k].status))
    && ['samplesPerCell', 'delayUnit', 'browserCondition'].every((k) => rules.hqaPending[k].status === '개발팀 사전 고정 · 장전 전 공개 · 댓글 <사전공개댓글>')
    && rules.hqaPending.T3.status === 'HQA 회신 대기' && rules.hqaPending.T3.value === null, pend.map((k) => rules.hqaPending[k].status));
  // 고침1 F8: 장전 관문(hqa-gate.js) — 필수 칸 목록 고정 · 확정 꼴만 통과 · 대조군 1 + 우회 꼴 6
  {
    const G = require('./hqa-gate.js');
    const conf = (r) => { const c = JSON.parse(JSON.stringify(r)); for (const k of G.REQUIRED) { c.hqaPending[k].status = 'HQA 회신 반영 · 댓글 1234567890'; if (c.hqaPending[k].value === null) c.hqaPending[k].value = 'x'; } c.plan.notMeasured.find((x) => x.item === 'I-8').approval = '미측정 허용 결재 · 댓글 1234567890'; return c; };
    check('관문: 지금 rules = 거부(회신 대기 남음)', !G.check(rules).ok);
    check('관문: 필수 칸 = 6 · T3 포함', G.REQUIRED.length === 6 && G.REQUIRED.includes('T3'));
    const all = conf(rules);
    check('관문: 6칸 모두 확정 꼴 = 통과(대조군)', G.check(all).ok, G.check(all).problems);
    const del = conf(rules); delete del.hqaPending.T3; check('관문: 칸 삭제 = 거부', !G.check(del).ok);
    const tmp = conf(rules); tmp.hqaPending.rawFormat.status = '임시'; check('관문: 다른 글자(임시) = 거부', !G.check(tmp).ok);
    const emp = conf(rules); emp.hqaPending.S1toS4.value = null; check('관문: 빈 값 = 거부', !G.check(emp).ok);
    const ext = conf(rules); ext.hqaPending.extra = { status: 'HQA 회신 반영 · 댓글 1234567890', value: 1 }; check('관문: 목록 밖 칸 = 거부', !G.check(ext).ok);
    const suf = conf(rules); suf.hqaPending.delayUnit.status += ' (임시)'; check('관문: 꼬리 붙은 글자 = 거부', !G.check(suf).ok);
    const pre = conf(rules); pre.hqaPending.T3.status = 'HQA 회신 대기 · HQA 회신 반영 · 댓글 1234567890'; check('관문: 앞 덧붙임 = 거부', !G.check(pre).ok);
    // 고침2 C4 ① 칸별 값 꼴 · C2 T3 확정이면 측정 코드 또는 미측정 허용 결재
    const z = conf(rules); z.hqaPending.S1toS4.value = 0; check('관문 C4: 값 0(숫자 단독) = 거부', !G.check(z).ok);
    const f = conf(rules); f.hqaPending.T3.value = false; check('관문 C4: 값 false = 거부', !G.check(f).ok);
    const sp = conf(rules); sp.hqaPending.samplesPerCell.value = { default: 0 }; check('관문 C4: 표본 수 0 = 거부', !G.check(sp).ok);
    const du = conf(rules); du.hqaPending.delayUnit.value = 'ms'; check('관문 C4: 지연 방식 목록 밖 = 거부', !G.check(du).ok);
    const bc = conf(rules); bc.hqaPending.browserCondition.value = { headless: 'yes' }; check('관문 C4: 브라우저 조건 꼴 틀림 = 거부', !G.check(bc).ok);
    // H4: I-8 이 계획에 들어갔으므로 「측정 코드 0」 꼴은 계획에서 I-8 을 뺀 사본으로 잰다(관문 T3 줄 무변)
    const na = conf(rules); na.plan.items = na.plan.items.filter((x) => x !== 'I-8'); delete na.plan.itemSpecs['I-8']; delete na.plan.notMeasured.find((x) => x.item === 'I-8').approval; check('관문 C2: T3 확정 + 측정 코드 0 + 결재 0 = 거부', !G.check(na).ok && G.check(na).problems.some((p) => p.why === 'no-code-or-approval'));
    const pc = conf(rules); pc.plan.items = pc.plan.items.concat(['I-8']); pc.plan.itemSpecs['I-8'] = { cases: ['blank'], delays: 'iDelays' };
    check('관문 C2: I-8 계획했는데 항목 코드 없음 = 거부', !G.check(pc, { itemsHave: ['I-1'] }).ok);
    check('관문 C2 대조군: I-8 계획 + 항목 코드 있음 = 통과', G.check(pc, { itemsHave: ['I-8'] }).ok, G.check(pc, { itemsHave: ['I-8'] }).problems);
  }
}

// ── ③ 정적 검사 — 신뢰 입력만 ─────────────────────────────────────────
const GLOBAL_BAN = [
  /dispatchEvent/, /KeyboardEvent/, /PointerEvent/, /InputEvent/, /\bMouseEvent\s*\(/, /addInitScript/, /addScriptTag/,
  /evaluateHandle/, /\$\$?eval\b/, /exposeFunction/, /exposeBinding/, /\.focus\s*\(/, /\.blur\s*\(/, /\.dispatch\w*\s*\(/,
  /\bsetContent\s*\(/, /\.route\s*\(/
];
const EVAL_BODY_BAN = [
  /\bclick\s*\(/, /\bfocus\s*\(/, /\bblur\s*\(/, /dispatchEvent/, /Event\s*\(/, /\bsubmit\s*\(/,
  /setAttribute|removeAttribute|toggleAttribute/, /innerHTML|outerHTML|insertAdjacent/, /textContent\s*=[^=]/, /\.value\s*=[^=]/,
  /appendChild|removeChild|replaceChild|\.remove\s*\(/, /scrollIntoView|scrollTo|scrollBy/, /localStorage|sessionStorage|indexedDB|document\.cookie/,
  /\bfetch\s*\(|XMLHttpRequest|WebSocket|sendBeacon|postMessage/, /\blocation\b/, /classList\.(add|remove|toggle)/, /\.style\.\w+\s*=[^=]/
];

// 주석 · 글자 덩어리(따옴표 안)를 비운 코드 — 이름 셈 전용
function codeOnly(src) {
  let o = '';
  let i = 0;
  while (i < src.length) {
    const ch = src[i];
    const nx = src[i + 1];
    if (ch === '/' && nx === '/') { while (i < src.length && src[i] !== '\n') i++; continue; }
    if (ch === '/' && nx === '*') { const e = src.indexOf('*/', i + 2); i = e < 0 ? src.length : e + 2; continue; }
    if (ch === '\'' || ch === '"' || ch === '`') {
      const q = ch;
      i++;
      while (i < src.length && src[i] !== q) { if (src[i] === '\\') i++; i++; }
      i++;
      o += q + q;
      continue;
    }
    o += ch;
    i++;
  }
  return o;
}
// 함수 몸통(첫 { 부터 짝 } 까지 · 따옴표 안 괄호는 셈하지 않음)
function fnBody(src, name) {
  const m = new RegExp('function\\s+' + name + '\\s*\\(').exec(src);
  if (!m) return null;
  let i = src.indexOf('{', m.index + m[0].length);
  if (i < 0) return null;
  const start = i + 1;
  let depth = 1;
  let q = null;
  for (i = start; i < src.length && depth > 0; i++) {
    const ch = src[i];
    if (q) { if (ch === '\\') i++; else if (ch === q) q = null; continue; }
    if (ch === '\'' || ch === '"' || ch === '`') q = ch;
    else if (ch === '{') depth++;
    else if (ch === '}') depth--;
  }
  return src.slice(start, i - 1);
}
// evaluate( … ) 의 괄호 안 원문을 뽑는다(따옴표 안 괄호는 셈하지 않음)
function evalBodies(src) {
  const out = [];
  const re = /\.evaluate\s*\(/g;
  let m;
  while ((m = re.exec(src)) !== null) {
    let i = m.index + m[0].length;
    let depth = 1;
    let q = null;
    const start = i;
    for (; i < src.length && depth > 0; i++) {
      const ch = src[i];
      if (q) { if (ch === '\\') i++; else if (ch === q) q = null; continue; }
      if (ch === '\'' || ch === '"' || ch === '`') q = ch;
      else if (ch === '(') depth++;
      else if (ch === ')') depth--;
    }
    out.push(src.slice(start, i - 1));
  }
  return out;
}

function checkSource(name, src, isLib) {
  const hits = [];
  for (const r of GLOBAL_BAN) if (r.test(src)) hits.push(`금지 ${r}`);
  const bodies = evalBodies(src);
  if (bodies.length && !isLib) hits.push('evaluate 는 items/_lib.js 에만');
  for (const b of bodies) for (const r of EVAL_BODY_BAN) if (r.test(b)) hits.push(`evaluate 안 ${r}`);
  return { name, hits, evaluates: bodies.length };
}

{
  const files = fs.readdirSync(path.join(HERE, 'items')).filter((f) => f.endsWith('.js')).map((f) => path.join('items', f)).concat(['run-items.js', 'judge.js']);
  let libEval = 0;
  for (const f of files) {
    const isLib = f.replace(/\\/g, '/') === 'items/_lib.js';
    const r = checkSource(f, fs.readFileSync(path.join(HERE, f), 'utf8'), isLib);
    if (isLib) libEval = r.evaluates;
    check(`정적: ${f.replace(/\\/g, '/')} 금지 입력 0`, r.hits.length === 0, r.hits);
  }
  check('정적: 검사 대상 파일 ≥ 5', files.length >= 5, files.length);
  check('정적: _lib.js 의 읽기 evaluate ≥ 2(빈 검사 아님)', libEval >= 2, libEval);

  // 대조군 — 반드시 걸려야 한다
  const planted = [
    ['evaluate 안 누름', 'await page.evaluate(() => { document.querySelector("#x").click(); });', true],
    ['evaluate 안 초점', 'await page.evaluate(() => document.activeElement.focus());', true],
    ['합성 키 이벤트', 'el.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab" }));', true],
    ['스크립트 주입', 'await context.addInitScript(() => 1);', true],
    ['lib 밖 evaluate', 'await page.evaluate(() => document.title);', false],
    ['locator 초점', 'await page.locator("#a").focus();', false],
    ['evaluate 안 값 쓰기', 'await page.evaluate(() => { document.querySelector("#i").value = "x"; });', true]
  ];
  for (const [n, src, isLib] of planted) {
    const r = checkSource(n, src, isLib);
    plant(`대조군 걸림: ${n}`, r.hits.length > 0, r.hits); // 고침5 K-1: check → plant(같은 단언 · 셈만 더함)
  }
  const good = checkSource('정상 읽기', 'await page.evaluate((a) => document.querySelectorAll(a.s).length, { s: "#x" });', true);
  check('대조군 통과: 읽기만 하는 evaluate', good.hits.length === 0, good.hits);
}

// ── ④ 출력 칸 이름 · 그림 이름이 가림(설계 §5 M8 ② 22자 이상 토큰)에 안 걸리는가 ──
{
  const LONG = /[A-Za-z0-9_-]{22,}/g;
  const ALLOW = new Set(['expectedBrowserVersion']); // 판 대조에만 쓰고 원자료에 안 씀
  const files = fs.readdirSync(path.join(HERE, 'items')).filter((f) => f.endsWith('.js')).map((f) => path.join('items', f)).concat(['run-items.js', 'judge.js']);
  const found = [];
  for (const f of files) {
    const m = fs.readFileSync(path.join(HERE, f), 'utf8').match(LONG) || [];
    for (const t of m) if (!ALLOW.has(t)) found.push(`${f}:${t}`);
  }
  check('가림 대비: 측정 · 판독 원문에 22자 이상 토큰 0(허용 목록 밖)', found.length === 0, found);
  check('가림 대비 대조군: 22자 토큰을 잡는다', ('x ' + 'abcdefghij'.repeat(3)).match(LONG) !== null);
  const { shotName } = require('./items/_lib.js');
  const nm = shotName({ item: 'N-1', screen: 'before', delay: 500, window: { w: 1280, h: 720 }, case: 'enter-twice' }, 12, 'n1-end');
  const longest = Math.max(...nm.split(/[^A-Za-z0-9_-]/).map((t) => t.length));
  check('가림 대비: 그림 파일 이름 토큰 < 22자', longest < 22 && /\.png$/.test(nm), nm);
}

// ── ⑤ 고침2 N1-e — 키보드 미도달 표본(U1~U4) 손 JSONL ──────────────────────────
const D = rules.deviceReading;
const KU_ROW1 = { row: '1', present: true, candidates: [], focusableVisible: 0, activator: { present: true, display: 'contents', rects: 0 } };
const PATH_PASS = [{ i: 1, row: null, tag: 'button', role: null, tabIndex: 0, inRef: false, isStatusActivator: false, afterRef: true, body: false }];
const PATH_BODY = [{ i: 1, row: null, tag: 'body', role: null, tabIndex: null, inRef: false, isStatusActivator: false, afterRef: false, body: true }];
function kbdUr(o) {
  const x = o || {};
  return { reached: false, cause: 'keyboard-unreachable', tabs: 40, path: x.path || PATH_PASS, row1: x.row1 || KU_ROW1, rowClick: { clicked: true, before: 0, after: x.rcOk === false ? 0 : 1, ok: x.rcOk !== false }, keyPressedAfter: 0 };
}
function kline(item, screen, delay, o) {
  const opt = o || {};
  const c = { item, case: item === 'N-1' ? 'arrows' : 'enter-twice', screen, delay, window: win };
  const ur = !!opt.ur;
  const base = { cell: cellKey(c), sampleNo: opt.no || 1, errors: [], valid: opt.valid !== undefined ? opt.valid : true };
  base.kbd = ur ? kbdUr(opt) : { reached: true, tabs: 7 };
  base.readings = ur
    ? (item === 'N-1' ? { opened: false, reachedByTab: false, keyPressed: 0, listClosed: null, focusSameRowBtn: null, otherRowsChanged: null } : { parentOpened: false, keyPressed: 0, pickDelta: null })
    : (item === 'N-1' ? { opened: true, listClosed: true, focusSameRowBtn: true, otherRowsChanged: false } : { pickDelta: 1 });
  base.circuit = { dead: !!opt.dead, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false };
  if (ur) base.deviceReading = base.valid ? 'keyboard_unreachable' : 'device_failure';
  return JSON.stringify(Object.assign(c, base));
}
// 한 묶음: N-1 · N-2 를 같은 꼴로(파생 칸은 부모 kbd 를 물려받는다)
const pair = (screen, delay, o) => [kline('N-1', screen, delay, o), kline('N-2', screen, delay, o)];
const KSETS = {
  // 고침3 N-A: 미도달(U 충족) + 회로 죽음 → 재현으로 안 셈 → 「대조 무효」 유지 / 대조군: 도달 + 회로 죽음 1 → 재현 1
  'N1eA_before미도달_회로죽음': [].concat(pair('before', 300, { ur: true, dead: true }), pair('before', 500, { ur: true, dead: true }), pair('after', 300), pair('after', 500)),
  'N1eB_미도달죽음1_도달죽음1': [].concat(pair('before', 300, { ur: true, dead: true }), pair('before', 500, { dead: true }), pair('after', 300), pair('after', 500)),
  'N1e1_before미도달_U충족':[].concat(pair('before', 300, { ur: true }), pair('before', 500, { ur: true }), pair('after', 300), pair('after', 500)),
  'N1e2_before미도달_U2결손': [].concat(pair('before', 300, { ur: true, path: PATH_BODY, valid: false }), pair('before', 500, { ur: true, path: PATH_BODY, valid: false }), pair('after', 300), pair('after', 500)),
  'N1e3_before미도달_U3결손': [].concat(pair('before', 300, { ur: true, valid: false }), pair('before', 500, { ur: true, valid: false }), pair('after', 300, { ur: true, valid: false }), pair('after', 500, { ur: true, valid: false })),
  'N1e4_after미도달_빠짐': [].concat(pair('before', 300), pair('before', 500), pair('after', 300, { ur: true, valid: false }), pair('after', 500)),
  'N1e5_after미도달1_나머지충족': [].concat(pair('before', 300), pair('before', 500), pair('after', 300, { ur: true, valid: false }), pair('after', 300, { no: 2 }), pair('after', 500)),
  'N1e6_유효주장_U결손': [].concat(pair('before', 300, { ur: true, rcOk: false, valid: true }), pair('before', 500, { ur: true }), pair('after', 300), pair('after', 500))
};
for (const k of Object.keys(KSETS)) sets[k] = KSETS[k];
const cellOf = (v, item, screen, delay) => v && v.cells.find((c) => c.item === item && c.screen === screen && c.delay === delay);
{
  // 고침3 N-A (재검증 rv2 n1dead.js 꼴): 미도달 표본에 회로 죽음만 켜도 재현 0 · 「대조 무효」 · 사유 칸 그대로
  const { code, v } = runJudge('N1eA_before미도달_회로죽음');
  const n = v && v.items['N-1'];
  check('N-A: 키보드 도달 0 + 회로 죽음 → 재현으로 안 셈(beforeCircuitDead 0 · 따로 센 수 2)', n && n.counts.beforeCircuitDead === 0 && n.counts.beforeKbdUnreachable === 2 && n.counts.beforeKuDead === 2, n && n.counts);
  check('N-A: → 「대조 무효」 + 사유 칸 · 빨강 아님 · 종료 0', n && n.labels.includes(T['N-1'].contrastInvalid) && n.contrastReason === D.n1ContrastReason && v.red === false && code === 0, n && { labels: n.labels, r: n.contrastReason, red: v.red });
}
{
  const { v } = runJudge('N1eB_미도달죽음1_도달죽음1');
  const n = v && v.items['N-1'];
  check('N-A 대조군: 도달 표본 회로 죽음 1 → 재현 1 · 「대조 무효」 안 나옴(빼기가 도달 표본까지 먹지 않음)', n && n.counts.beforeCircuitDead === 1 && n.counts.beforeKuDead === 1 && !n.labels.includes(T['N-1'].contrastInvalid) && n.contrastReason === null, n && { labels: n.labels, c: n.counts });
}
{
  const { code, v } = runJudge('N1e1_before미도달_U충족');
  const n = v && v.items['N-1'];
  const c = cellOf(v, 'N-1', 'before', 300);
  check('N1-e①: /before 미도달 + U1~U4 → keyboard_unreachable(칸)', c && c.deviceReading === 'keyboard_unreachable' && c.valid === 1 && c.kbdUnreachable === 1, c);
  check('N1-e①: 빨강 아님 · 종료 0', v && v.red === false && code === 0, v && v.redReasons);
  check('N1-e①: N-1 「대조 무효」 그대로 + 사유 칸', n && n.labels.includes(T['N-1'].contrastInvalid) && n.contrastReason === D.n1ContrastReason, n && { labels: n.labels, r: n.contrastReason });
  check('N1-e①: 판정 글자 「결함 재현」 안 씀 · 지어낸 글자 0', v && !/결함 재현/.test(JSON.stringify(v)) && noInvented(v));
}
{
  const { code, v } = runJudge('N1e2_before미도달_U2결손');
  const c = cellOf(v, 'N-1', 'before', 300);
  check('N1-e②: U2 결손(BODY 에 머묾) → device_failure · 빨강', v && v.red === true && code === 1 && c && c.deviceReading === 'device_failure' && c.valid === 0, c);
}
{
  const { code, v } = runJudge('N1e3_before미도달_U3결손');
  const c = cellOf(v, 'N-1', 'before', 500);
  const a = cellOf(v, 'N-1', 'after', 500);
  check('N1-e③: U3 결손(/after 도 미도달) → device_failure · 빨강', v && v.red === true && code === 1 && c && c.deviceReading === 'device_failure' && a && a.valid === 0, { c, a });
}
{
  const { code, v } = runJudge('N1e4_after미도달_빠짐');
  const a = cellOf(v, 'N-1', 'after', 300);
  check('N1-e④: /after 미도달 → keyboard_unreachable 아님 · 유효에서 빠짐 · 칸 표본 미달 → 장치 실패 · 빨강',
    v && v.red === true && code === 1 && a && a.valid === 0 && a.afterUnreachExcl === 1 && a.status === T.cellFail && a.deviceReading === 'device_failure' && a.reason.includes(D.afterUnreachable) && v.totals.kbdUnreachable === 0, a);
}
{
  const { code, v } = runJudge('N1e5_after미도달1_나머지충족');
  const a = cellOf(v, 'N-1', 'after', 300);
  check('N1-e⑤: /after 미도달 1 + 나머지 유효 → 칸 정상 · 뺀 수 남음', v && v.red === false && code === 0 && a && a.valid === 1 && a.afterUnreachExcl === 1 && v.items['N-1'].counts.afterUnreachExcl === 1, a);
}
{
  const { code, v } = runJudge('N1e6_유효주장_U결손');
  check('N1-e 추가: 표본이 유효라 주장해도 U1 결손이면 judge 가 다시 재서 빨강(불일치)', v && v.red === true && code === 1 && v.totals.validMismatch >= 1, v && v.redReasons);
}

// ── ⑥ 고침2 K 판정(구분 점검) · K-장치 길 음성 대조 ─────────────────────────────
{
  const J = require('./judge.js');
  const R3OK = { ok: true };
  check('K: R1 결함 꼴 + 1번 줄 지나감 + R3 연결 → K-결함', J.kCheck(KU_ROW1, PATH_PASS, R3OK).verdict === 'K-결함');
  const missIn = [{ i: 1, row: '1', tag: 'div', role: null, tabIndex: 0, inRef: true, isStatusActivator: false, afterRef: false, body: false }];
  check('K: 1번 줄 안 tabIndex 0 요소에 갔는데 판정 false → K-장치', J.kCheck({ present: true, activator: { present: true, display: 'block', rects: 1 }, focusableVisible: 1 }, missIn, R3OK).verdict === 'K-장치');
  check('K: BODY 만 머문 경로 → K-미결', J.kCheck(KU_ROW1, PATH_BODY, R3OK).verdict === 'K-미결');
  check('K: R3 연결 실패 → K-미결', J.kCheck(KU_ROW1, PATH_PASS, { ok: false }).verdict === 'K-미결');
  check('K: R1 이 G1 과 다름(활성 div 보임) → K-미결', J.kCheck({ present: true, activator: { present: true, display: 'inline-flex', rects: 1 }, focusableVisible: 0 }, PATH_PASS, R3OK).verdict === 'K-미결');
  const otherRow = [{ i: 1, row: '2', tag: 'button', role: null, tabIndex: 0, inRef: false, isStatusActivator: false, afterRef: true, body: false }];
  const otherCell = [{ i: 1, row: null, tag: 'a', role: null, tabIndex: 0, inRef: false, isStatusActivator: false, afterRef: false, body: false }];
  const reachedIn = [{ i: 1, row: '1', tag: 'button', role: null, tabIndex: 0, inRef: true, isStatusActivator: true, afterRef: false, body: false }];
  check('K-장치 음성 대조: 다음 줄 요소 초점 → K-장치 아님', J.kCheck(KU_ROW1, otherRow, R3OK).verdict !== 'K-장치');
  check('K-장치 음성 대조: 같은 줄 다른 칸(줄 칸 밖) 초점 → K-장치 아님', J.kCheck(KU_ROW1, otherCell, R3OK).verdict !== 'K-장치');
  check('K-장치 음성 대조: 1번 줄 상태 단추 도달(판정 true) → K-장치 아님 · K-결함 아님', ['K-장치', 'K-결함'].indexOf(J.kCheck(KU_ROW1, reachedIn, R3OK).verdict) < 0);
  const ks = Object.keys(rules.kCheck).filter((k) => !k.startsWith('_'));
  check('K 글자 3개 고정(rules.json kCheck) · U1~U4 글자 고정(keyboardGate)', JSON.stringify(ks) === JSON.stringify(['K-결함', 'K-장치', 'K-미결']) && ['U1', 'U2', 'U3', 'U4'].every((u) => typeof rules.keyboardGate[u] === 'string' && rules.keyboardGate[u].length > 10));
  check('정본 판정 글자 그대로(N-1 · I-6 · I-7 · 장치 실패)', T['N-1'].contrastInvalid === '대조 무효' && T['I-6'].contrastInvalid === '대조 무효' && T['I-7'].contrastInvalid === '대조 무효' && T.cellFail === '장치 실패' && D.device_failure === T.cellFail);
}

// ── ⑦ 고침2 N1-b 합성 시험 — 가짜 page(브라우저 0)로 미도달 표본에 키 입력 0 ─────────────
async function mockRun(itemFn, cell, reachAt, extra) {
  const r = JSON.parse(JSON.stringify(rules));
  Object.assign(r.device, { settle: { baseMs: 0, perDelayFactor: 0 }, pollMs: 1, keyGapMs: 0, openWaitMs: 3, maxTabsToRow: 4 });
  r.device.i1.tabs = 4; r.device.i6.maxTabs = 2; r.device.n1.count = 2; r.device.n1.settleExtraMs = 0;
  r.device.i2.longPress = { firstDelayMs: 0, repeats: 1, intervalMs: 0 };
  let tabs = 0;
  let clicks = 0;
  const reached = () => reachAt > 0 && tabs >= reachAt;
  const focus = () => (reached()
    ? { tag: 'div', id: null, role: 'button', row: '1', inOpenList: false, isStatusActivator: true, text: null, afterRef: false, body: false, tabIndex: 0 }
    : { tag: tabs ? 'button' : 'body', id: null, role: null, row: null, inOpenList: false, isStatusActivator: false, text: null, afterRef: tabs > 0, body: tabs === 0, tabIndex: tabs ? 0 : null });
  const box = { x: 0, y: 0, w: 10, h: 10 };
  const page = {
    keyboard: { press: async (k) => { if (k === 'Tab') tabs += 1; }, down: async () => {}, up: async () => {} },
    mouse: { click: async () => { clicks += 1; } },
    locator: () => ({ first: () => ({ click: async () => {} }) }),
    waitForTimeout: async () => {},
    screenshot: async () => {},
    evaluate: async (fn) => {
      const src = String(fn);
      if (src.includes('focusableVisible')) return KU_ROW1;
      if (src.includes('firstCell')) return { lists: [], rows: { 1: { row: box, firstCell: box, statusCell: box }, 2: { row: box, firstCell: box, statusCell: box } }, viewport: { w: 1280, h: 720 } };
      return { variant: cell.screen, cssVariant: cell.screen, openCount: 0, focus: focus(), pickCount: 0, picks: '', rowclickCount: clicks, rowclicks: '', detailOpen: null, detailRow: null, rows: {}, reconnect: { present: false, visible: false, cls: '' }, viewport: { w: 1280, h: 720 } };
    }
  };
  const sig = { t0: Date.now(), wsCloses: [], consoleMatches: [], reconnectSeen: [], pageErrors: [], navs: 0, navsAtConnect: 0, consoleErrors: 0 };
  const ctx = Object.assign({ page, sig, rules: r, cell, sampleNo: 1, outDir: tmp, inputs: [], inputAt: Date.now() }, extra || {});
  const out = await itemFn(ctx);
  return { out, inputs: ctx.inputs };
}
const KEY_K = new Set(['press', 'down', 'up']);
const keysAfterLastTab = (inputs) => {
  let last = -1;
  inputs.forEach((x, i) => { if (KEY_K.has(x.k) && x.key === 'Tab') last = i; });
  return inputs.slice(last + 1).filter((x) => KEY_K.has(x.k)).length;
};
async function asyncChecks() {
  const ALL = Object.assign({}, require('./items/i-items.js'), require('./items/n-items.js'));
  const cellFor = (item, kase, screen) => ({ item, case: kase, screen, delay: 0, window: win, key: 'mock' });
  const targets = [['I-1', 'tab-walk'], ['I-2', 'enter-short'], ['I-2', 'space-long'], ['I-4', 'kbd-open'], ['I-5', 'tab'], ['I-6', 'esc'], ['N-1', 'arrows']];
  for (const [item, kase] of targets) {
    let res = null;
    let err = null;
    try { res = await mockRun(ALL[item], cellFor(item, kase, 'before'), 0); } catch (e) { err = String(e && e.message ? e.message : e).slice(0, 120); }
    const own = res && res.out.find((o) => !o.item || o.item === item);
    const nonTabKeys = res ? res.inputs.filter((x) => KEY_K.has(x.k) && x.key !== 'Tab').length : -1;
    const mouse = res ? res.inputs.filter((x) => x.k === 'mouse.click').length : -1;
    check(`N1-b 합성: ${item}/${kase} 미도달 → throw 0 · Tab 밖 키 입력 0 · 마지막 Tab 뒤 키 0 · 행 클릭 1 · kbd.reached=false`,
      !err && own && own.kbd && own.kbd.reached === false && nonTabKeys === 0 && keysAfterLastTab(res.inputs) === 0 && mouse === 1 && (item === 'I-1' || own.readings.keyPressed === 0),
      err || { nonTabKeys, mouse, kbd: own && own.kbd && own.kbd.reached });
    if (item === 'N-1') {
      const n2 = res && res.out.find((o) => o.item === 'N-2');
      check('N1-b 합성: N-2 = 부모 칸 열기 불가 · 입력 0', n2 && n2.readings.note === D.n2ParentNotOpened && n2.readings.keyPressed === 0);
    }
  }
  // 대조군 — 도달하면 키를 누른다(검사가 살아 있는지)
  const pos = await mockRun(ALL['I-2'], cellFor('I-2', 'enter-short', 'before'), 2);
  check('N1-b 합성 대조군: 도달 표본은 Tab 뒤 키 입력이 있다(검사 살아 있음)', keysAfterLastTab(pos.inputs) >= 1 && pos.out[0].kbd.reached === true, pos.inputs.map((x) => x.k + ':' + (x.key || '')));
  // 고침4 G1: N-1 의 a2 칸엔 N-2 계획이 없다 → derivedItems 에 N-2 가 없으면 N-2 입력(Enter 두 번) 0 · 결과 0 / 대조군: 옛 호출(derivedItems 없음)은 N-2 까지
  const enters = (inputs) => inputs.filter((x) => KEY_K.has(x.k) && x.key === 'Enter').length;
  const a2Reach = await mockRun(ALL['N-1'], cellFor('N-1', 'arrows', 'a2'), 2, { derivedItems: [] });
  const oldReach = await mockRun(ALL['N-1'], cellFor('N-1', 'arrows', 'before'), 2);
  check('G4-G1 합성: a2 칸(N-2 계획 0) 도달 → N-1 결과만 · Enter 1(열기) · 대조군 옛 호출 = N-2 까지 Enter 3',
    a2Reach.out.length === 1 && a2Reach.out[0].item === 'N-1' && enters(a2Reach.inputs) === 1 && oldReach.out.length === 2 && enters(oldReach.inputs) === 3,
    { a2: a2Reach.out.length, a2Enter: enters(a2Reach.inputs), old: oldReach.out.length, oldEnter: enters(oldReach.inputs) });
  const a2Un = await mockRun(ALL['N-1'], cellFor('N-1', 'arrows', 'a2'), 0, { derivedItems: [] });
  check('G4-G1 합성: a2 칸 미도달 → N-1 결과만 · Tab 밖 키 0', a2Un.out.length === 1 && a2Un.inputs.filter((x) => KEY_K.has(x.k) && x.key !== 'Tab').length === 0, a2Un.out.length);
  await probeContrastChecks();
  await fix6Checks(); // 고침6 J1 — I-4 HQA 절차 동작 시험(가짜 L)
}

// ── ⑧-2 S4 보탬(4_재검증_H4GB ③): probe 비교 화면 대조의 「동작」 시험 — 브라우저 0 · probe.js 무변
//   probe.js 원문에서 contrastVariant 함수 글자와 main 의 종료 코드 꼬리(const cRed … return 0)를 떠서
//   가짜 page(속성 읽기 getAttribute 만 있음 · 그 밖의 부름 = 예외)로 직접 부른다 → 판단 · 종료 코드 경로를 값으로 대조
//   정적 검사(⑧ checkProbe)는 「L.* 0 · 입력 0」 만 본다 — 대조 지움 · 종료 코드 2 지움은 이 시험이 잡는다(심은 위반 3)
function probeFnText(src, name) {
  const m = new RegExp('(?:async\\s+)?function\\s+' + name + '\\s*\\(').exec(src);
  const body = fnBody(src, name);
  if (!m || body === null) return null;
  const at = src.indexOf(body, m.index);
  return at < 0 ? null : src.slice(m.index, at + body.length) + '}';
}
async function probeContrastHits(src) {
  const hits = [];
  const J = require('./judge.js');
  const cvText = probeFnText(src, 'contrastVariant');
  const mainBody = fnBody(src, 'main');
  const tailAt = mainBody ? mainBody.indexOf('const cRed') : -1;
  if (!cvText) hits.push('contrastVariant 함수 글자 못 뜸');
  if (tailAt < 0) hits.push('main 종료 코드 꼬리(const cRed) 못 뜸');
  if (hits.length) return hits;
  let cv;
  let exitOf;
  try {
    cv = new Function('RULES', 'errText', 'return (' + cvText + ');')(rules, (e) => String(e && e.message ? e.message : e));
    exitOf = new Function('doc', 'process', mainBody.slice(tailAt));
  } catch (e) { return ['probe 글자 해석 실패: ' + String(e && e.message ? e.message : e).slice(0, 120)]; }
  const quiet = { stderr: { write: () => true } };
  const ATTR = { '#fixture-root': 'data-variant', 'link[data-fixture-css]': 'data-fixture-css' };
  async function call(screen, variant, css) {
    const reads = [];
    const val = { 'data-variant': variant, 'data-fixture-css': css };
    const page = { locator: (sel) => ({ first: () => ({ getAttribute: async (a) => { reads.push(sel + '|' + a); const v = val[a]; if (v instanceof Error) throw v; return v; } }) }) };
    const out = { errors: [] };
    let r = null;
    let err = null;
    try { r = await cv({ page }, out, J, screen); } catch (e) { err = String(e && e.message ? e.message : e).slice(0, 120); }
    return { r, err, reads, out };
  }
  const screens = Object.keys(rules.device.probe.contrastModes || {});
  if (screens.length < 2) hits.push(`비교 화면 ${screens.length} < 2`);
  let n = 0;
  for (const screen of screens) {
    const sp = J.screenSpec(rules, screen);
    const otherV = J.screenSpec(rules, screens.find((s) => s !== screen)).variant;
    const otherC = sp.css === 'before' ? 'after' : 'before';
    const cases = [
      ['둘 다 맞음', sp.variant, sp.css, 'ok'],
      ['variant 다름', otherV, sp.css, 'red'],
      ['css 다름', sp.variant, otherC, 'red'],
      ['둘 다 다름', otherV, otherC, 'red'],
      ['속성 없음(null)', null, null, 'red'],
      ['variant 읽기 예외', new Error('읽기 실패'), sp.css, 'red']
    ];
    for (const [nm, v, c, want] of cases) {
      n++;
      const tag = `${screen} ${nm}`;
      const { r, err, reads, out } = await call(screen, v, c);
      if (err || !r) { hits.push(`${tag}: 예외 ${err}`); continue; }
      // 입력 0 · 줄 읽기 0 — 부름 = 속성 읽기 둘(정해진 선택자 · 속성) 뿐
      const wantReads = Object.keys(ATTR).map((s) => s + '|' + ATTR[s]);
      if (reads.length !== 2 || wantReads.some((x) => !reads.includes(x))) hits.push(`${tag}: 속성 읽기 ${JSON.stringify(reads)}`);
      if (r.device !== want) hits.push(`${tag}: device ${r.device} ≠ ${want}`);
      if (want === 'ok' && r.deviceReason !== null) hits.push(`${tag}: 사유 있음`);
      if (want === 'red' && !(typeof r.deviceReason === 'string' && r.deviceReason.startsWith(rules.deviceReading.contrastVariantDiff) && r.deviceReason.includes(screen))) hits.push(`${tag}: 사유 글자 ${r.deviceReason}`);
      if (!r.expected || r.expected.variant !== sp.variant || r.expected.css !== sp.css) hits.push(`${tag}: 기대 칸 ${JSON.stringify(r.expected)}`);
      if (v instanceof Error && !out.errors.some((x) => x.startsWith(screen + ' data-variant: '))) hits.push(`${tag}: 오류 칸 기록 없음`);
      // 종료 코드 경로 — 이 화면 판단을 doc 에 넣고 main 꼬리를 그대로 돌림(빨강 = 2 · 정상 = 0)
      let code;
      try { code = exitOf({ contrast_checks: { [screen]: r }, a2_check: null }, quiet); } catch (e) { code = 'throw'; }
      const wantCode = want === 'red' ? 2 : 0;
      if (code !== wantCode) hits.push(`${tag}: 종료 코드 ${code} ≠ ${wantCode}`);
    }
  }
  // 종료 코드 꼬리 단독 — 두 화면 정상 + a2 정상 = 0 / 한 화면만 빨강 = 2 / 꼴 표에 없는 화면 빨강 = 2
  const ex = (doc) => { try { return exitOf(doc, quiet); } catch (e) { return 'throw'; } };
  const okAll = { contrast_checks: Object.fromEntries(screens.map((s) => [s, { device: 'ok', deviceReason: null }])), a2_check: { device: 'ok' } };
  if (ex(okAll) !== 0) hits.push(`꼬리: 전부 정상 → ${ex(okAll)} ≠ 0`);
  const oneRed = JSON.parse(JSON.stringify(okAll));
  oneRed.contrast_checks[screens[screens.length - 1]] = { device: 'red', deviceReason: 'x' };
  if (ex(oneRed) !== 2) hits.push(`꼬리: 한 화면 빨강 → ${ex(oneRed)} ≠ 2`);
  if (ex({ contrast_checks: { zz: { screen: 'zz', mode: null, device: 'red', deviceReason: 'contrastModes 에 zz 없음' } }, a2_check: null }) !== 2) hits.push('꼬리: 꼴 표에 없는 화면 → 2 아님');
  if (n < 12) hits.push(`경우 수 ${n} < 12`);
  return hits;
}
async function probeContrastChecks() {
  const src = fs.readFileSync(path.join(HERE, 'probe.js'), 'utf8');
  const h = await probeContrastHits(src);
  check('S4 보탬 probe 동작: 비교 화면(e1t3 · v2) variant 다름 · css 다름 → 장치 빨강 + 종료 2 · 둘 다 맞음 → 정상 + 종료 0 · 부름 = 속성 읽기 둘뿐(입력 0)', h.length === 0, h);
  const P = [
    ['S4 보탬 probe 대조군: variant 대조 지움', (s) => s.replace('variant !== sp.variant || css !== sp.css', 'css !== sp.css')],
    ['S4 보탬 probe 대조군: css 대조 지움', (s) => s.replace('variant !== sp.variant || css !== sp.css', 'variant !== sp.variant')],
    ['S4 보탬 probe 대조군: 비교 화면 빨강 종료 코드 2 지움', (s) => s.replace(/(if \(cRed\.length\) \{[\s\S]*?)return 2;/, '$1')]
  ];
  for (const [nm, f] of P) {
    const s2 = f(src);
    const h2 = s2 === src ? [] : await probeContrastHits(s2);
    plant(nm + ' → 빨강', s2 !== src && h2.length > 0, s2 === src ? '바꿈 자리 못 찾음' : h2.slice(0, 3));
  }
}

// ── ⑧ 고침2 N1-a probe 정적 검사(C1-4) + 심은 위반 대조군 ──────────────────────────
function checkProbe(src) {
  const hits = [];
  for (const r of GLOBAL_BAN) if (r.test(src)) hits.push(`금지 ${r}`);
  if (evalBodies(src).length) hits.push('evaluate 는 items/_lib.js 에만');
  if (/\.keyboard\b|\.mouse\b/.test(src)) hits.push('page.keyboard/mouse 직접 사용');
  const QK = new RegExp('([\'"`])(?:Enter|NumpadEnter|Escape|Esc|Space|Spacebar| |Arrow(?:Up|Down|Left|Right)|Home|End|PageUp|PageDown)\\1');
  if (QK.test(src)) hits.push('금지 키 글자');
  // 고침3 N-B: 키 호출은 probe.js 에 직접 0 — Tab 은 L.tabTo 안에서만(Tab 인자여도 직접 호출이면 빨강)
  for (const m of src.matchAll(/\b(press|down|up|type|insertText|sendCharacter|pressSequentially)\s*\(([^)]*)\)/g)) {
    hits.push(/^(?:ctx\s*,\s*)?(['"`])Tab\1$/.test(m[2].trim()) ? `키 호출 ${m[1]} 직접(Tab 은 L.tabTo 안에서만)` : `키 호출 ${m[1]} 인자 Tab 아님`);
  }
  for (const m of src.matchAll(/\bPROBE_DELAY\s*=\s*([^;\n]+)/g)) if (m[1].trim() !== '0') hits.push('지연 값 0 아님');
  for (const m of src.matchAll(/\bdelay\s*:\s*([A-Za-z0-9_.]+)/g)) if (m[1] !== '0' && m[1] !== 'PROBE_DELAY') hits.push(`지연 값 0 아님 ${m[1]}`);
  if (/proxyPort|delay-proxy|proxy/i.test(src)) hits.push('프록시 사용');
  const ALLOWED_L = new Set(['readRowStruct', 'rowClickControl', 'tabTo', 'attachSignals', 'settleMs']);
  for (const m of src.matchAll(/\bL\.(\w+)/g)) if (!ALLOWED_L.has(m[1])) hits.push(`L.${m[1]} 허용 목록 밖`);
  // ── 고침3 N-B: 허용 입력 목록 = #before 누름 1 + Tab(둘 다 L.tabTo 1회 안) · R3 행 클릭 1회(L.rowClickControl 1회) — 이것뿐 ──
  const cnt = (re) => (src.match(re) || []).length;
  const code = codeOnly(src); // 이름 셈은 주석 · 글자 덩어리를 뺀 코드에서(설명 글자의 낱말이 섞이지 않게)
  const cntC = (re) => (code.match(re) || []).length;
  if (/[\w)\]]\s*\[\s*['"`]/.test(src)) hits.push('글자 대괄호 접근(obj[\'이름\'])');
  // ① 마우스 · 위치자 · 페이지 동작 금지(probe.js 직접 입력 0)
  const ACT = /\.(click|dblclick|tap|hover|fill|selectOption|check|uncheck|setChecked|dragTo|dragAndDrop|setInputFiles|clear|selectText|scrollIntoViewIfNeeded|reload|goBack|goForward|bringToFront)\s*\(/g;
  for (const m of src.matchAll(ACT)) hits.push(`동작 ${m[1]}() 직접`);
  if (/\.touchscreen\b/.test(src)) hits.push('touchscreen 직접');
  // ② L 은 이름 그대로만(별칭 · 대괄호 · 통째 넘기기 금지) · _lib 은 한 번만 require
  if (/\bL\s*\[/.test(src)) hits.push('L[...] 대괄호 접근');
  if (/=\s*L\b(?!\s*\.)/.test(src)) hits.push('L 별칭');
  const bareL = cntC(/(?<![.\w$])L\b(?!\s*\.)/g);
  // 고침4 G10: 옛 기대 5(매개변수 2 · 넘김 2 · 선언 1) 대체 → a2Struct 매개변수 1 · 넘김 1 더해 7
  // H4 §7-3: 옛 기대 7 대체 → contrastR1 매개변수 1 · 넘김 1 더해 9
  if (bareL !== 9) hits.push(`L 이름 쓰임 ${bareL} ≠ 9(매개변수 4 · 넘김 4 · 선언 1)`);
  if (cnt(/_lib\b/g) !== 1) hits.push('items/_lib 참조 1 아님');
  // ③ 입력 함수 각 1회 · 자리 = distinguish 몸통 안 · 켜짐 확인(return null) 뒤
  if (cntC(/\btabTo\b/g) !== 1 || cntC(/\bL\.tabTo\s*\(/g) !== 1) hits.push(`tabTo ${cntC(/\btabTo\b/g)}회 ≠ 1`);
  if (cntC(/\browClickControl\b/g) !== 1 || cntC(/\bL\.rowClickControl\s*\(/g) !== 1) hits.push(`rowClickControl ${cntC(/\browClickControl\b/g)}회 ≠ 1`);
  const body = fnBody(src, 'distinguish');
  const guard = body ? body.indexOf('if (!RULES.device.probe.distinguish) return null;') : -1;
  if (!body || guard < 0) hits.push('distinguish 몸통 · 켜짐 확인 없음');
  else for (const f of ['L.rowClickControl(', 'L.tabTo(']) if (body.indexOf(f) < guard) hits.push(`${f} 자리 = distinguish 켜짐 확인 뒤 아님`);
  // ④ distinguish 는 정의 1 + 부름 1(/before 갈래 안) — 별칭 · 두 번째 부름 · /after 부름 금지
  const dn = cntC(/(?<![.\w$])distinguish\b(?!\s*:)/g);
  if (dn !== 2) hits.push(`distinguish 이름 쓰임 ${dn} ≠ 2`);
  if (!/if \(screen === 'before'\) \{\s*await page\.waitForTimeout\(L\.settleMs\(RULES, PROBE_DELAY\)\);\s*doc\.k_check = await distinguish\(ctx, out, L, J\);\s*\}/.test(src)) hits.push('distinguish 부름 자리 = /before 갈래 아님');
  // ⑤ 지연: waitForTimeout 인자 = d.pollMs(회로 붙기 기다림) · L.settleMs(RULES, PROBE_DELAY) 만 · settleMs 인자 고정 · 타이머 0
  const WAIT_OK = new Set(['d.pollMs', 'L.settleMs(RULES, PROBE_DELAY)']);
  for (const m of src.matchAll(/\bwaitForTimeout\s*\(((?:[^()]|\([^()]*\))*)\)/g)) if (!WAIT_OK.has(m[1].trim())) hits.push(`waitForTimeout(${m[1].trim()}) 허용 밖`);
  for (const m of src.matchAll(/\bsettleMs\s*\(([^)]*)\)/g)) if (m[1].trim() !== 'RULES, PROBE_DELAY') hits.push(`settleMs(${m[1].trim()}) 지연 0 아님`);
  if (/\bset(?:Timeout|Interval)\s*\(/.test(src)) hits.push('타이머 직접');
  // ⑥ 주소: goto 1회 · base = 시험 앱 직접(rules host:appPort 기본) 한 줄뿐
  //   고침4 G10: 옛 인자(base + '/' + screen) 대체 → base + SCREEN_TABLE[screen].path(주소 = rules 표 값 · 이 파일 안 주소 글자 0)
  if (cnt(/\.goto\s*\(/g) !== 1) hits.push(`goto ${cnt(/\.goto\s*\(/g)}회 ≠ 1`);
  if (!/\.goto\(base \+ SCREEN_TABLE\[screen\]\.path, \{/.test(src)) hits.push('goto 인자 = base + SCREEN_TABLE[screen].path 아님');
  if (!src.includes('const SCREEN_TABLE = RULES.plan.screenTable;') || cnt(/(?<![.\w$])SCREEN_TABLE\s*=(?!=)/g) !== 1) hits.push('SCREEN_TABLE 줄 바뀜');
  if (/(['"`])\/(?:after|before)/.test(src) || /after-a2/.test(src)) hits.push('probe 안 주소 글자');
  // ⑦ 고침4 G10: a2 = R1(입력 0 구조 읽기)만 — a2 갈래 = 정착 기다림 + a2Struct 부름뿐 · a2Struct 몸통 = readRowStruct 하나 · 입력 0
  //   고침4 보탬(PM F-6 ④): 옛 부름 a2Struct(ctx, out, L) 대체 → J 를 넘김(a2VariantCheck)
  if (!/if \(screen === 'a2'\) \{\s*(?:\/\/[^\n]*\n\s*)?await page\.waitForTimeout\(L\.settleMs\(RULES, PROBE_DELAY\)\);\s*doc\.a2_check = await a2Struct\(ctx, out, L, J\);\s*\} else (?:if \(!RULES\.plan\.screens\.includes\(screen\)\) )?\{/.test(src)) hits.push('a2 갈래 = 정착 + a2Struct 부름 아님');
  const a2b = fnBody(src, 'a2Struct');
  if (!a2b) hits.push('a2Struct 몸통 없음');
  else {
    if (cnt(/\bL\.readRowStruct\s*\(/g) < 2 || !/\bL\.readRowStruct\s*\(/.test(a2b)) hits.push('a2Struct 안 readRowStruct 없음');
    // 고침4 보탬(PM F-6 ④): 허용 = 화면 판 속성 둘 getAttribute 읽기만(글자 그대로 각 1회) — 그 자리를 뺀 나머지에 아래 금지 낱말 검사
    const ATTR_READ = ["ctx.page.locator('#fixture-root').first().getAttribute('data-variant')", "ctx.page.locator('link[data-fixture-css]').first().getAttribute('data-fixture-css')"];
    let a2s = a2b;
    for (const r of ATTR_READ) {
      if (a2b.split(r).length - 1 !== 1) hits.push(`a2Struct 안 속성 읽기 1 아님: ${r}`);
      a2s = a2s.split(r).join('ATTR_READ_OK');
    }
    if (!/J\.a2VariantCheck\(RULES, variant, css\)/.test(a2b)) hits.push('a2Struct 안 화면 판 기대 대조(J.a2VariantCheck) 없음');
    const a2bc = codeOnly(a2s);
    for (const m of a2bc.matchAll(/\b(press|down|up|type|insertText|click|dblclick|tap|hover|fill|tabTo|rowClickControl|keyboard|mouse|touchscreen|distinguish|goto|waitForTimeout|locator|evaluate|screenshot)\b/g)) hits.push(`a2Struct 안 ${m[1]}`);
    if ((a2bc.match(/\bL\.\w+/g) || []).length !== 1) hits.push('a2Struct 안 L 부름 1 아님');
  }
  const an = cntC(/(?<![.\w$])a2Struct\b/g);
  if (an !== 2) hits.push(`a2Struct 이름 쓰임 ${an} ≠ 2`);
  // H4 §7-3: 옛 줄은 PROBE_SCREENS_OLD 로 이름만 바뀜(글자 그대로) · 새 목록 = 옛 + contrastScreen 값
  if (!src.includes("const PROBE_SCREENS_OLD = RULES.plan.screens.concat(RULES.n1Contrast === 'a2' ? ['a2'] : []);") || !src.includes('const PROBE_SCREENS = PROBE_SCREENS_OLD.concat(')) hits.push('PROBE_SCREENS 줄 바뀜');
  // H4 §7-3: 비교 화면 — contrastVariantOnly 몸통 L.* 0 · 줄 읽기 0 / contrastR1 몸통 L.* = readRowStruct 하나 · 두 몸통 입력 0
  const cvo = fnBody(src, 'contrastVariantOnly');
  const cr1 = fnBody(src, 'contrastR1');
  const cvb = fnBody(src, 'contrastVariant');
  const INPUT_RE = /\.(press|click|dblclick|tap|type|fill|focus|hover|check|selectOption|dispatchEvent)\s*\(|\bkeyboard\b|\bmouse\b|\btabTo\b|\blocatorClick\b|\browClickControl\b/;
  if (!cvo || /\bL\./.test(cvo) || /readRowStruct|readBlankRows|readState|data-row/.test(cvo) || INPUT_RE.test(cvo)) hits.push('contrastVariantOnly 줄 읽기 · 입력');
  if (!cr1 || (cr1.match(/\bL\.[A-Za-z]+/g) || []).some((x) => x !== 'L.readRowStruct') || INPUT_RE.test(cr1)) hits.push('contrastR1 허용 밖');
  if (!cvb || /\bL\./.test(cvb) || INPUT_RE.test(cvb)) hits.push('contrastVariant 허용 밖');
  if (cnt(/(?<![.\w$])base\s*=(?!=)/g) !== 1 || !src.includes("const base = process.env.HQA_BASE_URL || ('http://' + RULES.device.host + ':' + RULES.device.appPort);")) hits.push('base 주소 줄 바뀜');
  if (cnt(/\bnewContext\s*\(/g) !== 1 || cnt(/\bnewPage\s*\(/g) !== 1) hits.push('newContext · newPage 1회 아님');
  return hits;
}
{
  const src = fs.readFileSync(path.join(HERE, 'probe.js'), 'utf8');
  const h = checkProbe(src);
  check('probe 정적: Enter · Space · Escape · Arrow 키 호출 0 · 지연 0 만 · 입력 함수 허용 목록만', h.length === 0, h);
  check('probe 정적: 입력 있음(빈 검사 아님 — tabTo · rowClickControl 호출)', /L\.tabTo\(/.test(src) && /L\.rowClickControl\(/.test(src));
  check('probe 정적: Tab 도달 · Enter 뒤 열림 칸 없음(tab_first_row · open_list_after_enter 0)', !/tab_first_row|open_list_after_enter/.test(src));
  check('probe 정적: 구분 점검은 /before 만', /if \(screen === 'before'\) \{\s*await page\.waitForTimeout\([^\n]*\);\s*doc\.k_check = await distinguish\(/.test(src) && (src.match(/await distinguish\(/g) || []).length === 1);
  const plantedP = [
    ['probe 대조군: Enter 누름 한 줄', "\nawait page.keyboard.press('Enter');\n"],
    ['probe 대조군: L.press Enter', "\nawait L.press(ctx, 'Enter');\n"],
    ['probe 대조군: Space(글자 공백) 누름', "\nawait L.press(ctx, ' ');\n"],
    ['probe 대조군: 화살표', "\nawait L.down(ctx, 'ArrowDown');\n"],
    ['probe 대조군: 지연 300', "\nconst c2 = { delay: 300 };\n"],
    // 고침3 N-B — 재검증(rv2 pp.js) 19건 꼴 + 더함
    ['probe 대조군: 두 번째 rowClickControl', "\nout.r3b = await L.rowClickControl(ctx, row);\n"],
    ['probe 대조군: 두 번째 tabTo', "\nawait L.tabTo(ctx, row);\n"],
    ['probe 대조군: 위치자 click(상태 칩)', "\nawait page.locator('.bo-status-chip').first().click();\n"],
    ['probe 대조군: page.click', "\nawait page.click('.bo-status-chip');\n"],
    ['probe 대조군: dblclick', "\nawait page.locator('x').dblclick();\n"],
    ['probe 대조군: tap', "\nawait page.locator('x').tap();\n"],
    ['probe 대조군: hover', "\nawait page.hover('x');\n"],
    ['probe 대조군: fill', "\nawait page.fill('x','y');\n"],
    ['probe 대조군: 변수로 만든 키', "\nconst K=['En','ter'].join('');await page.locator('x').press(K);\n"],
    ['probe 대조군: 글자 코드로 Enter', "\nawait ctx.page.keyboard.press(String.fromCharCode(13));\n"],
    ['probe 대조군: selectOption', "\nawait page.selectOption('x','y');\n"],
    ['probe 대조군: check()', "\nawait page.locator('x').check();\n"],
    ['probe 대조군: 프록시 포트로 goto', "\nawait page.goto(base + ':5800/before');\n"],
    ['probe 대조군: waitForTimeout 300', "\nawait page.waitForTimeout(300);\n"],
    ['probe 대조군: settleMs 300', "\nawait page.waitForTimeout(L.settleMs(RULES, 300));\n"],
    ['probe 대조군: /after 에서 tabTo', "\nif (screen === 'after') await L.tabTo(ctx, 1);\n"],
    ['probe 대조군: 별칭으로 distinguish 를 /after 에서', "\nconst dd = distinguish; if (screen==='after') doc.k2 = await dd(ctx,out,L,J);\n"],
    ['probe 대조군: L 별칭', "\nconst LL=L; await LL.press(ctx,'x');\n"],
    ['probe 대조군: L 대괄호', "\nawait L['locatorClick'](ctx,'.bo-status-chip');\n"],
    ['probe 대조군: 위치자 press Tab(직접 Tab)', "\nawait page.locator('#before').press('Tab');\n"],
    ['probe 대조군: L.mouseClick.call', "\nawait L.mouseClick.call(null, ctx, 10, 10);\n"],
    ['probe 대조군: _lib 직접 require', "\nawait require('./items/_lib.js').locatorClick(ctx, '#before');\n"],
    ['probe 대조군: 타이머 지연', "\nawait new Promise((r) => setTimeout(r, 300));\n"],
    ['probe 대조군: newContext 프록시', "\nawait browser.newContext({ proxy: { server: base } });\n"],
    ['probe 대조군(바꿈): /after 갈래에서 distinguish', (s) => s.replace("if (screen === 'after') out.png_check = await pngCheck(page);", "if (screen === 'after') { out.png_check = await pngCheck(page); await distinguish(ctx, out, L, J); }")],
    ['probe 대조군(바꿈): 켜짐 확인 앞에서 tabTo', (s) => s.replace(/( {2}if \(!RULES\.device\.probe\.distinguish\) return null;\r?\n)([\s\S]*?)( {2}const reach = await L\.tabTo\(ctx, row\);\r?\n)/, '$3$1$2')],
    // 고침4 G10: 옛 바꿈 자리(page.goto(base + '/' + screen,)가 없어져 새 goto 줄로 대체
    ['probe 대조군(바꿈): goto 주소 프록시 포트', (s) => s.replace('page.goto(base + SCREEN_TABLE[screen].path,', "page.goto(base.replace(/:\\d+$/, ':5800') + SCREEN_TABLE[screen].path,")],
    ['probe 대조군(바꿈): 정착 지연 300', (s) => s.replace('await page.waitForTimeout(L.settleMs(RULES, PROBE_DELAY));', 'await page.waitForTimeout(L.settleMs(RULES, 300));')],
    // ── 고침4 G10 — a2 = R1 만(입력 0) · 주소 = 표 값 ──
    ['probe 대조군(바꿈) G10: a2Struct 안 keyboard.press', (s) => s.replace('out.r1 = await L.readRowStruct(ctx, RULES.device.kbd.refRow);', "out.r1 = await L.readRowStruct(ctx, RULES.device.kbd.refRow); await ctx.page.keyboard.press('Tab');")],
    ['probe 대조군(바꿈) G10: a2Struct 안 mouse.click', (s) => s.replace('out.r1 = await L.readRowStruct(ctx, RULES.device.kbd.refRow);', 'out.r1 = await L.readRowStruct(ctx, RULES.device.kbd.refRow); await ctx.page.mouse.click(5, 5);')],
    ['probe 대조군(바꿈) G10: a2 갈래 keyboard.press', (s) => s.replace('doc.a2_check = await a2Struct(ctx, out, L, J);', "await page.keyboard.press('Enter'); doc.a2_check = await a2Struct(ctx, out, L, J);")],
    ['probe 대조군(바꿈) G10: a2 갈래 mouse.click', (s) => s.replace('doc.a2_check = await a2Struct(ctx, out, L, J);', 'await page.mouse.click(5, 5); doc.a2_check = await a2Struct(ctx, out, L, J);')],
    ['probe 대조군(바꿈) G10: a2 갈래 L.tabTo', (s) => s.replace('doc.a2_check = await a2Struct(ctx, out, L, J);', 'await L.tabTo(ctx, 1); doc.a2_check = await a2Struct(ctx, out, L, J);')],
    ['probe 대조군(바꿈) G10: a2 갈래 L.rowClickControl', (s) => s.replace('doc.a2_check = await a2Struct(ctx, out, L, J);', 'await L.rowClickControl(ctx, 1); doc.a2_check = await a2Struct(ctx, out, L, J);')],
    ['probe 대조군(바꿈) G10: a2 갈래 distinguish', (s) => s.replace('doc.a2_check = await a2Struct(ctx, out, L, J);', 'doc.a2_check = await a2Struct(ctx, out, L, J); doc.k2 = await distinguish(ctx, out, L, J);')],
    ['probe 대조군(바꿈) G10: a2 갈래 locator click', (s) => s.replace('doc.a2_check = await a2Struct(ctx, out, L, J);', "await page.locator('#before').click(); doc.a2_check = await a2Struct(ctx, out, L, J);")],
    ['probe 대조군(바꿈) G10: a2Struct 안 Tab 지연', (s) => s.replace('out.r1 = await L.readRowStruct(ctx, RULES.device.kbd.refRow);', 'out.r1 = await L.readRowStruct(ctx, RULES.device.kbd.refRow); await ctx.page.waitForTimeout(300);')],
    ['probe 대조군(바꿈) G10: goto 주소 글자(/after-a2)', (s) => s.replace('page.goto(base + SCREEN_TABLE[screen].path,', "page.goto(base + (screen === 'a2' ? '/after-a2' : SCREEN_TABLE[screen].path),")],
    ['probe 대조군(바꿈) G10: 주소 앞머리 글자', (s) => s.replace('page.goto(base + SCREEN_TABLE[screen].path,', "page.goto(base + '/' + screen,")],
    // ── 고침4 보탬(PM F-6 ④) — a2 속성 읽기 자리는 getAttribute 만 · 기대 대조 있음 ──
    ['probe 대조군(바꿈) 보탬④: data-variant 읽기 자리에 click', (s) => s.replace(".first().getAttribute('data-variant')", '.first().click()')],
    ['probe 대조군(바꿈) 보탬④: data-fixture-css 읽기 자리에 click', (s) => s.replace(".first().getAttribute('data-fixture-css')", '.first().click()')],
    ['probe 대조군(바꿈) 보탬④: 속성 읽기를 evaluate 로', (s) => s.replace("ctx.page.locator('#fixture-root').first().getAttribute('data-variant')", "ctx.page.evaluate(() => document.querySelector('#fixture-root').getAttribute('data-variant'))")],
    ['probe 대조군(바꿈) 보탬④: 기대 대조(a2VariantCheck) 뺌', (s) => s.replace('const vc = J.a2VariantCheck(RULES, variant, css);', 'const vc = { variant, css };')]
  ];
  for (const [n, add] of plantedP) {
    const s2 = typeof add === 'function' ? add(src) : src + add;
    plant(n + ' → 빨강', s2 !== src && checkProbe(s2).length > 0, typeof add === 'function' && s2 === src ? '바꿈 자리 못 찾음' : null); // 고침5 K-1: check → plant
  }
  check('probe 대조군 수 ≥ 24(원 5 + 재검증 19)', plantedP.length >= 24, plantedP.length);
  // 고침4 G10
  check('G4-G10 probe 정적: a2 는 n1Contrast = a2 일 때만 · R1 만 · 주소 = 표 값', /PROBE_SCREENS/.test(src) && /doc\.a2_check = await a2Struct\(/.test(src) && (src.match(/await a2Struct\(/g) || []).length === 1);
  check('G4-G10 probe 대조군 수 = 고침4 11 더함(합 ≥ 44)', plantedP.filter((p) => / G10: /.test(p[0])).length === 11 && plantedP.length >= 44, plantedP.length);
  // 고침4 보탬(PM F-6 ④): a2 화면 판 속성 — 읽기만(정적) · 기대와 다르면 장치 빨강(동작)
  check('보탬④ probe 대조군 수 = 4(합 48)', plantedP.filter((p) => / 보탬④: /.test(p[0])).length === 4 && plantedP.length === 48, plantedP.length);
  {
    const J = require('./judge.js');
    const ok = J.a2VariantCheck(rules, 'a2', 'after');
    const wrongV = J.a2VariantCheck(rules, 'after', 'after');
    const wrongC = J.a2VariantCheck(rules, 'a2', 'before');
    const none = J.a2VariantCheck(rules, null, null);
    check('보탬④: a2_check variant a2 · css after = 장치 정상 / data-variant after · CSS before · 둘 다 없음 = 장치 빨강(사유 글자에 주소 글자 0)',
      ok.device === 'ok' && ok.deviceReason === null && ok.variant === 'a2' && ok.css === 'after' && ok.expected.variant === 'a2' && ok.expected.css === 'after' &&
      [wrongV, wrongC, none].every((x) => x.device === 'red' && x.deviceReason.startsWith(rules.deviceReading.a2ProbeVariantDiff) && !x.deviceReason.includes('/after')),
      { ok, wrongV, wrongC, none });
    check('보탬④ 원문: probe 가 a2 장치 빨강이면 종료 코드 2 · a2_check 에 variant · css 를 넘김', /if \(doc\.a2_check && doc\.a2_check\.device === 'red'\) \{[\s\S]{0,120}return 2;/.test(src) && /Object\.assign\(\{ screen: 'a2'[^\n]*\}, vc\)/.test(src));
  }
}

// ── ⑨ 고침4 — G1 · G2 · G3 · G4 · G5 · G6 · G8 · C-2 (작업지시서 §7 G13 ①~⑥ · ⑨~⑪ · ⑦⑧ 은 selftest-leak.js) ──────────────
{
  const J = require('./judge.js');
  const a2n1 = (delay, o) => kline('N-1', 'a2', delay, o); // a2 = N-1 에만(N-2 a2 칸 0)
  // G13 ① a2 미도달(U 충족) + 회로 죽음 → 재현으로 안 셈 → 「대조 무효」 유지 · /before 회로 죽음은 기록만(G3)
  sets['G4a_a2미도달_회로죽음'] = [].concat(pair('before', 300, { dead: true }), pair('before', 500), [a2n1(300, { ur: true, dead: true }), a2n1(500, { ur: true, dead: true })], pair('after', 300), pair('after', 500));
  // G13 ② a2 도달 + 회로 죽음 → 재현 1
  sets['G4b_a2도달_회로죽음'] = [].concat(pair('before', 300), pair('before', 500), [a2n1(300, { dead: true }), a2n1(500)], pair('after', 300), pair('after', 500));
  // a2 칸 표본 0 → 정해진 표본 수 모자람 · A2 화면 표본 0
  sets['G4c_a2칸0'] = [].concat(pair('before', 300), pair('before', 500), [a2n1(300, { dead: true })], pair('after', 300), pair('after', 500));
  // N-2 a2 표본 → 계획 밖(다른 항목 a2 칸 0)
  sets['G4d_N2a2계획밖'] = sets['G4b_a2도달_회로죽음'].concat([kline('N-2', 'a2', 300)]);
  {
    const { code, v } = runJudge('G4a_a2미도달_회로죽음', rulesPath2);
    const n = v && v.items['N-1'];
    const a2c = v && v.cells.filter((c) => c.screen === 'a2');
    const bc = v && v.cells.filter((c) => c.item === 'N-1' && c.screen === 'before');
    check('G4-①: a2 미도달 + 회로 죽음 → 재현 0 · 대조 무효 · 사유 「A2 화면 …」 · 빨강 아님',
      n && n.contrastScreen === 'a2' && n.counts.beforeCircuitDead === 0 && n.counts.beforeKbdUnreachable === 2 && n.counts.beforeKuDead === 2 && n.labels.includes(T['N-1'].contrastInvalid) && n.contrastReason === D.n1ContrastReasonA2 && v.red === false && code === 0,
      n && { labels: n.labels, c: n.counts, r: n.contrastReason, red: v.red, rr: v.redReasons });
    check('G4-① G3: /before N-1 회로 죽음 = 기록만(재현 셈 0 · beforeRecordOnly 1) · /before N-1 칸 표지 「기록만 · 판정에 안 씀」',
      n && n.beforeRecordOnly && n.beforeRecordOnly.circuitDead === 1 && n.beforeRecordOnly.text === T['N-1'].recordOnly && bc.length === 2 && bc.every((c) => c.n1Role === T['N-1'].recordOnly), n && n.beforeRecordOnly);
    check('G4-① a2 칸 = keyboard_unreachable · 글자 「A2 화면 키보드 도달 0」', a2c && a2c.length === 2 && a2c.every((c) => c.deviceReading === 'keyboard_unreachable' && c.deviceReadingText === D.a2Unreachable), a2c);
  }
  {
    const { code, v } = runJudge('G4b_a2도달_회로죽음', rulesPath2);
    const n = v && v.items['N-1'];
    check('G4-②: a2 도달 + 회로 죽음 1 → 재현 1 · 대조 무효 아님 · 빨강 아님', n && n.counts.beforeCircuitDead === 1 && !n.labels.includes(T['N-1'].contrastInvalid) && n.contrastReason === null && v.red === false && code === 0, n && { labels: n.labels, c: n.counts, rr: v.redReasons });
  }
  {
    const { code, v } = runJudge('G4c_a2칸0', rulesPath2);
    const c = cellOf(v, 'N-1', 'a2', 500);
    check('G4 a2 칸 표본 0 → 장치 실패 · 정해진 표본 수 모자람 0/1 · A2 화면 표본 0 · 빨강', v && v.red === true && code === 1 && c && c.status === T.cellFail && c.reason === `${T.cellShortReason} 0/1` && c.zeroReason === T.a2ZeroReason, c);
  }
  // 고침4 보탬(PM F-6 ③): 「기록만」 /before N-1 칸 장치 실패 = 빨강 그대로 + 「장치 건강 · 판정 무관」 표지(원자료 · verdict) · 다른 칸 표지 0
  {
    sets['B3_기록만칸빔'] = [].concat([kline('N-2', 'before', 300)], pair('before', 500), [a2n1(300, { dead: true }), a2n1(500)], pair('after', 300), pair('after', 500));
    const { code, v } = runJudge('B3_기록만칸빔', rulesPath2);
    const b = cellOf(v, 'N-1', 'before', 300);
    const others = v ? v.cells.filter((x) => !(x.item === 'N-1' && x.screen === 'before' && x.delay === 300)) : [];
    const tag = T['N-1'].recordOnlyHealth;
    const n = v && v.items['N-1'];
    check('보탬③: 기록만 칸 장치 실패 → 빨강 그대로 · 칸 healthOnly 「장치 건강 · 판정 무관」 · redReasons 머리 표지 · beforeRecordOnly.failTag · 다른 칸 표지 0 · N-1 판정 셈 무관(deviceFail 거짓)',
      v && v.red === true && code === 1 && tag === '장치 건강 · 판정 무관' && b && b.status === T.cellFail && b.healthOnly === tag && b.n1Role === T['N-1'].recordOnly &&
      v.redReasons.some((x) => x.startsWith(tag + ' — ') && x.endsWith(b.key)) && v.redReasons.filter((x) => x.startsWith(tag)).length === 1 &&
      others.every((x) => x.healthOnly === undefined) && n && n.beforeRecordOnly && n.beforeRecordOnly.failTag === tag && n.deviceFail === false,
      v && { red: v.redReasons, b, rec: n && n.beforeRecordOnly });
    const cb = { item: 'N-1', screen: 'before' };
    const tagA = J.recordOnlyTag(st2, cb);
    check('보탬③ 원자료: recordOnlyTag ㉡ /before N-1 = {기록만 · 장치 건강} · a2 칸 · N-2 /before · ㉠ = 표지 0 · run-items.js 가 표본에 붙임',
      tagA && tagA.role === T['N-1'].recordOnly && tagA.failTag === tag && J.recordOnlyTag(st2, { item: 'N-1', screen: 'a2' }) === null && J.recordOnlyTag(st2, { item: 'N-2', screen: 'before' }) === null && J.recordOnlyTag(st, cb) === null &&
      /const ro = recordOnlyTag\(rules, cell\);\s*if \(ro\) rec\.recordOnly = ro;/.test(fs.readFileSync(path.join(HERE, 'run-items.js'), 'utf8')), tagA);
  }
  {
    const { code, v } = runJudge('G4d_N2a2계획밖', rulesPath2);
    check('G4-G1: N-2 a2 표본 = 계획 밖 → 빨강(다른 항목 a2 칸 0)', v && v.red === true && code === 1 && v.totals.unplanned === 1, v && v.redReasons);
  }
  // G13 ⑪ a2 표본 판독 글자 · 사유 글자에 「/after」 0(C-4) — 판독기 칸 · N-1 사유 · kbdGate 결과 · 정해진 표본 수 모자람 사유
  {
    const texts = [];
    for (const nm of ['G4a_a2미도달_회로죽음', 'G4b_a2도달_회로죽음', 'G4c_a2칸0']) {
      const { v } = runJudge(nm, rulesPath2);
      if (!v) { texts.push('/after(판독 파일 없음)'); continue; }
      for (const c of v.cells.filter((x) => x.screen === 'a2')) texts.push(JSON.stringify(c));
      for (const r of v.redReasons.filter((x) => x.includes('|a2|'))) texts.push(r);
      const n = v.items['N-1'];
      texts.push(String(n.contrastReason), JSON.stringify(n.labels), String(n.reading));
    }
    const ku = J.kbdGate(st2, JSON.parse(a2n1(300, { ur: true })), 1);
    const kuMiss = J.kbdGate(st2, JSON.parse(a2n1(300, { ur: true, path: PATH_BODY })), 0);
    texts.push(JSON.stringify(ku), JSON.stringify(kuMiss));
    let scErr = '';
    try { J.screenCheck(st2, 'a2', { variant: 'after', cssVariant: 'after' }); } catch (e) { scErr = String(e.message); }
    texts.push(scErr, JSON.stringify(J.screenCheck(st2, 'a2', { variant: 'a2', cssVariant: 'before' }).errors));
    const bad = texts.filter((x) => x.includes('/after'));
    check('G4-⑪(C-4): a2 표본 판독 · 사유 글자 전부에서 「/after」 0', bad.length === 0 && texts.length >= 12 && kuMiss.reason.startsWith(D.a2Unreachable) && ku.valid === true, { bad: bad.slice(0, 3), n: texts.length, kuMiss: kuMiss.reason });
  }
  // G13 ⑩ a2 표본이 /after 로 가서 data-variant after → throw(C-3) · 대조군: a2 · CSS after = 정상 · CSS before = 오류 칸
  {
    let thrown = null;
    try { J.screenCheck(st2, 'a2', { variant: 'after', cssVariant: 'after' }); } catch (e) { thrown = String(e.message); }
    const ok = J.screenCheck(st2, 'a2', { variant: 'a2', cssVariant: 'after' });
    const cssBad = J.screenCheck(st2, 'a2', { variant: 'a2', cssVariant: 'before' });
    let bThrown = null;
    try { J.screenCheck(st2, 'before', { variant: 'a2', cssVariant: 'after' }); } catch (e) { bThrown = String(e.message); }
    check('G4-⑩(C-3): a2 표본 data-variant after → throw · a2/after = 정상 · a2/CSS before = 오류 1 · before 칸에 a2 화면 → throw',
      thrown !== null && ok.errors.length === 0 && cssBad.errors.length === 1 && bThrown !== null, { thrown, ok: ok.errors, cssBad: cssBad.errors, bThrown });
    const tbl = rules.plan.screenTable;
    check('G4-G1 표: before → /before · before · before / after → /after · after · after / a2 → /after-a2 · a2 · after',
      JSON.stringify(tbl.before) === JSON.stringify({ path: '/before', variant: 'before', css: 'before' }) && JSON.stringify(tbl.after) === JSON.stringify({ path: '/after', variant: 'after', css: 'after' }) && JSON.stringify(tbl.a2) === JSON.stringify({ path: ['', 'after-a2'].join('/'), variant: 'a2', css: 'after' }), tbl);
    const u = J.screenUrl(rules, 'a2');
    const riSrc = fs.readFileSync(path.join(HERE, 'run-items.js'), 'utf8');
    check('G4-G1: 표본 주소 = 표 값(screenUrl) · run-items.js 에 「/${화면 이름}」 주소 꼴 0 · screenCheck 부름', u.endsWith(tbl.a2.path) && !/\$\{cell\.screen\}/.test(riSrc) && /screenUrl\(rules, cell\.screen\)/.test(riSrc) && /screenCheck\(rules, cell\.screen, st0\)/.test(riSrc), u);
    // n1Contrast ↔ N-1 화면 어긋남 · a2 를 다른 항목에 넣음 → buildPlan 거부
    const bad1 = JSON.parse(JSON.stringify(rules)); bad1.n1Contrast = 'before';
    const bad2 = JSON.parse(JSON.stringify(rules)); bad2.plan.itemSpecs['I-1'].screens = ['before', 'after', 'a2'];
    const bad3 = JSON.parse(JSON.stringify(rules)); delete bad3.plan.itemSpecs['N-1'].screens;
    const throws = (r) => { try { buildPlan(r); return false; } catch (e) { return true; } };
    check('G4-G1: 거부 3(㉠ 값인데 N-1 에 a2 · I-1 에 a2 · ㉡ 값인데 N-1 에 a2 없음) · 대조군 ㉠ 짝(st) = 통과', throws(bad1) && throws(bad2) && throws(bad3) && !throws(st));
  }
  // G13 ③ ④ 정해진 표본 수(임시 5) — 유효 4 → 장치 실패 · 5 → 정상
  {
    const five = (item, screen, delay, o, n) => { const out = []; for (let i = 1; i <= (n || 5); i++) out.push(kline(item, screen, delay, Object.assign({ no: i }, o || {}))); return out; };
    const full = (shortCell) => [].concat(
      five('N-1', 'before', 300, { dead: true }), five('N-2', 'before', 300), five('N-1', 'before', 500), five('N-2', 'before', 500),
      five('N-1', 'after', 300), five('N-2', 'after', 300), five('N-1', 'after', 500, {}, shortCell ? 4 : 5), five('N-2', 'after', 500));
    sets['G4e_5중4'] = full(true);
    sets['G4f_5중5'] = full(false);
    const r3 = runJudge('G4e_5중4', rulesPath3);
    const c = cellOf(r3.v, 'N-1', 'after', 500);
    check('G4-③: 정해진 5 · 유효 4 → 장치 실패 · 「정해진 표본 수 모자람 4/5」 · 빨강 · 종료 1', r3.v && r3.v.red === true && r3.code === 1 && c && c.status === T.cellFail && c.reason === `${T.cellShortReason} 4/5` && c.deviceReading === 'device_failure', c);
    const r4 = runJudge('G4f_5중5', rulesPath3);
    check('G4-④: 정해진 5 · 유효 5 → 칸 정상 · 빨강 아님', r4.v && r4.v.red === false && r4.code === 0 && r4.v.cells.every((x) => x.status === '표본 있음' && x.planned === 5 && x.valid === 5), r4.v && r4.v.redReasons);
    const jsrc = fs.readFileSync(path.join(HERE, 'judge.js'), 'utf8');
    check('G4-G4 원문: 칸 실패 = 유효 < 정해진 표본 수 · 옛 꼴(valid === 0 · length === 0 칸 실패) 0', /if \(valid < c\.samples\)/.test(jsrc) && /validOf\(c\.key\)\.length < c\.samples/.test(jsrc) && !/if \(valid === 0\) \{/.test(jsrc) && !/validOf\(c\.key\)\.length === 0/.test(jsrc));
  }
  // G13 ⑤ /after 미도달 건수 · 표본 번호 · Tab 경로(스키마 칸만) 기록
  {
    const { v } = runJudge('N1e5_after미도달1_나머지충족');
    const a = cellOf(v, 'N-1', 'after', 300);
    const keys = a && a.afterUnreachable && a.afterUnreachable.tabPath[0] && a.afterUnreachable.tabPath[0][0] ? Object.keys(a.afterUnreachable.tabPath[0][0]).sort() : null;
    check('G4-⑤: /after 미도달 → afterUnreachable {count 1 · samples [1] · tabPath 1} · 경로 칸 = 스키마 칸만',
      a && a.afterUnreachable && a.afterUnreachable.count === 1 && JSON.stringify(a.afterUnreachable.samples) === '[1]' && a.afterUnreachable.tabPath.length === 1 && JSON.stringify(keys) === JSON.stringify(['afterRef', 'body', 'i', 'inRef', 'isStatusActivator', 'role', 'row', 'tabIndex', 'tag']), a && a.afterUnreachable);
    const b = cellOf(v, 'N-1', 'before', 300);
    const a5 = cellOf(v, 'N-1', 'after', 500);
    check('G4-⑤ 대조군: /before 칸엔 afterUnreachable 칸 없음 · /after 미도달 0 칸 = count 0', b && b.afterUnreachable === undefined && a5 && a5.afterUnreachable && a5.afterUnreachable.count === 0, a5 && a5.afterUnreachable);
  }
  // G13 ⑥ 순서 어긋난 원자료 → 빨강 · 순서 없음 · 문맥 겹침 → 빨강
  {
    const objs = canon(sets['D_정상'], st);
    const swap = objs.map((o) => Object.assign({}, o));
    const b300 = swap.find((o) => o.item === 'N-1' && o.screen === 'before' && o.delay === 300);
    const a300 = swap.find((o) => o.item === 'N-1' && o.screen === 'after' && o.delay === 300);
    const t = b300.order; b300.order = a300.order; a300.order = t;
    sets['G4g_순서어긋남'] = swap.map((o) => JSON.stringify(o));
    const r1 = runJudge('G4g_순서어긋남');
    check('G4-⑥: 같은 단 안 before 가 after 보다 앞 → 빨강 · 「재는 순서 어긋남」', r1.v && r1.v.red === true && r1.code === 1 && r1.v.redReasons.some((x) => x.startsWith(D.orderBroken)), r1.v && r1.v.redReasons);
    const late = objs.map((o) => Object.assign({}, o));
    const d500 = late.find((o) => o.delay === 500);
    d500.order = 0; // 0 = 순번 없음 꼴
    sets['G4h_순서없음'] = late.map((o) => JSON.stringify(o));
    const r2 = runJudge('G4h_순서없음');
    check('G4-⑥: 순서 기록 없음 → 빨강', r2.v && r2.v.red === true && r2.v.redReasons.some((x) => x.startsWith(D.orderMissing)), r2.v && r2.v.redReasons);
    const cx = objs.map((o) => Object.assign({}, o));
    cx[1].contextId = cx[0].contextId;
    sets['G4i_문맥겹침'] = cx.map((o) => JSON.stringify(o));
    const r3 = runJudge('G4i_문맥겹침');
    check('G4-⑥: 부모 표본 문맥 순번 겹침 → 빨강', r3.v && r3.v.red === true && r3.v.redReasons.some((x) => x.startsWith(D.contextBroken)), r3.v && r3.v.redReasons);
    const back = objs.map((o) => Object.assign({}, o));
    const lastD = back.filter((o) => o.delay === 500).sort((a, b) => a.order - b.order)[0];
    const firstD300 = back.filter((o) => o.delay === 300).sort((a, b) => b.order - a.order)[0];
    const t2 = lastD.order; lastD.order = firstD300.order; firstD300.order = t2;
    sets['G4j_단거꾸로'] = back.map((o) => JSON.stringify(o));
    const r4 = runJudge('G4j_단거꾸로');
    check('G4-⑥: 지연 단 거꾸로(500 단 표본이 300 단보다 앞) → 빨강', r4.v && r4.v.red === true && r4.v.redReasons.some((x) => x.startsWith(D.orderBroken)), r4.v && r4.v.redReasons);
    check('G4-⑥ 대조군: 순서 맞는 원자료(D) → 순서 문제 0', (() => { const { v } = runJudge('D_정상'); return v && v.totals.orderProblems === 0 && v.red === false; })());
    // H4 §7-1: 옛 기대 after → a2 → before 대체(옛 값 = _screenOrder_옛값_H4)
    check('G4-G6: 순서 표 = after → a2 → e1t3 → v2 → before(rules.plan.screenOrder) · 옛 값 보존', JSON.stringify(rules.plan.screenOrder) === '["after","a2","e1t3","v2","before"]' && JSON.stringify(rules.plan._screenOrder_옛값_H4) === '["after","a2","before"]');
  }
  // G13 ⑨ G8 — attempt 2 → 측정 0(M0 실행 사슬 단언 · --guard-only = 망 접속 0)
  {
    const m0 = path.join(HERE, 'm0-assert.sh');
    const head = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: HERE, encoding: 'utf8' });
    const sha = (head.stdout || '').trim();
    const env0 = Object.assign({}, process.env);
    for (const k of ['GITHUB_SHA', 'GITHUB_RUN_ATTEMPT', 'GITHUB_ACTIONS', 'GITHUB_OUTPUT', 'GITHUB_RUN_ID']) delete env0[k];
    const g = (extra) => {
      const d = fs.mkdtempSync(path.join(tmp, 'g-'));
      const r = spawnSync('bash', [m0, d, '--guard-only'], { cwd: path.resolve(HERE, '..', '..'), env: Object.assign({}, env0, extra), encoding: 'utf8' });
      return { rc: r.status, out: (r.stdout || '') + (r.stderr || ''), err: r.error ? String(r.error.message).slice(0, 80) : null, files: fs.readdirSync(d) };
    };
    const ok = g({ GITHUB_ACTIONS: 'true', GITHUB_SHA: sha, GITHUB_RUN_ATTEMPT: '1' });
    const att2 = g({ GITHUB_ACTIONS: 'true', GITHUB_SHA: sha, GITHUB_RUN_ATTEMPT: '2' });
    const other = g({ GITHUB_ACTIONS: 'true', GITHUB_SHA: 'f'.repeat(40), GITHUB_RUN_ATTEMPT: '1' });
    const noSha = g({ GITHUB_ACTIONS: 'true', GITHUB_RUN_ATTEMPT: '1' });
    check('G4-⑨ 대조군: github.sha = HEAD · attempt 1 → 실행 사슬 통과', /^[0-9a-f]{40}$/.test(sha) && ok.rc === 0 && /run_attempt_one 통과/.test(ok.out) && /head_is_event 통과/.test(ok.out), ok);
    check('G4-⑨: run_attempt 2 → 측정 0(M0 빨강 · run_attempt_one 다름)', att2.rc === 1 && /run_attempt_one 다름/.test(att2.out), att2);
    check('G4-G8: github.sha ≠ 체크아웃 HEAD → 측정 0', other.rc === 1 && /head_is_event 다름/.test(other.out), other);
    check('G4-G8: Actions 안인데 github.sha 없음 → 측정 0', noSha.rc === 1 && /head_is_event 다름/.test(noSha.out), noSha);
    const msrc = fs.readFileSync(m0, 'utf8');
    check('G4-G8 원문: 장전 때 원자료 가지 있으면 측정 0(ls-remote · armed_raw_branch_absent) · 원자료에 github_sha · mode_line',
      /armed_raw_branch_absent/.test(msrc) && /ls-remote origin "refs\/heads\/\$RAW_BRANCH"/.test(msrc) && /github_sha:/.test(msrc) && /mode_line:/.test(msrc));
  }
  // C-2(결-1 조건 ①): 장치 폴더 · 워크플로에 주소 앞머리 판단 0 — 꼴은 조각으로 만든다(이 파일이 스스로 걸리지 않게)
  {
    const SW = 'starts' + 'With';
    const pats = [
      new RegExp('[Ss]' + SW.slice(1) + '\\(\\s*[\'"`]\\/after'),
      new RegExp('\\/\\^\\\\\\/' + 'after'),
      new RegExp('Absolute' + 'Path')
    ];
    const walk = (d) => fs.readdirSync(d, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? walk(path.join(d, e.name)) : [path.join(d, e.name)]));
    const files = walk(HERE).concat([path.resolve(HERE, '..', '..', '.github', 'workflows', 'hqa-s4-measure.yml')]).filter((f) => fs.existsSync(f));
    const found = [];
    for (const f of files) { const s = fs.readFileSync(f, 'utf8'); for (const p of pats) if (p.test(s)) found.push(path.basename(f) + ':' + p); }
    check('G4-C-2: 장치 폴더 + 워크플로에 주소 앞머리 판단 꼴 0', found.length === 0 && files.length >= 15, { found, files: files.length });
    const ctrl = ['x.' + SW + "('/after')", 'p.' + SW[0].toUpperCase() + SW.slice(1) + '("/after"', '/^\\/after/', 'u.Absolute' + 'Path'];
    check('G4-C-2 대조군: 꼴 4개를 심으면 전부 걸림', ctrl.every((s) => pats.some((p) => p.test(s))), ctrl.map((s) => pats.some((p) => p.test(s))));
  }
}

// ── 고침5 H1-12 — 판독 칸 넷 · 회차 글자 · 분모 · 노출 · 제외 판독 · 관문 · K-1 · K-2 (대조군을 함께 · 동작을 잰다) ──
{
  const J = require('./judge.js');
  const G = require('./hqa-gate.js');
  const F5 = rules.readingText.fix5;
  const R5 = JSON.parse(JSON.stringify(rules));
  R5.plan.items = ['I-1', 'I-2', 'I-4', 'I-6', 'I-7', 'N-1'];
  R5.plan.iDelays = [0];
  R5.plan.nDelays = [300, 500];
  R5.hqaPending.samplesPerCell.value = { default: 1, 'N-1': 1 };
  const P_D1 = [{ i: 1, row: '1', tag: 'TD', role: null, tabIndex: -1, inRef: true, isStatusActivator: false, afterRef: false, body: false },
    { i: 2, row: '2', tag: 'TD', role: null, tabIndex: -1, inRef: false, isStatusActivator: false, afterRef: true, body: false }];
  const P_OTHER = [{ i: 1, row: null, tag: 'BODY', role: null, tabIndex: -1, inRef: false, isStatusActivator: false, afterRef: false, body: true }];
  const ku = (s, p) => { s.kbd = { reached: false, path: p || P_D1, row1: { present: true, focusableVisible: 0, activator: { present: true, display: 'contents', rects: 0 } }, rowClick: { ok: true } }; s.deviceReading = 'keyboard_unreachable'; s.valid = true; };
  const aur = (s, p) => { ku(s, p); s.valid = false; s.deviceReading = 'device_failure'; };
  const base = (r) => {
    const xs = [];
    for (const c of buildPlan(r).cells) {
      for (let n = 1; n <= c.samples; n++) {
        xs.push({ item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, cell: c.key, sampleNo: n, valid: true, errors: [], deviceReading: null,
          readings: { opened: true, listClosed: true, focusSameRowBtn: true, otherRowsChanged: false, maxOpenCount: 1, rowclickDelta: 0, changedOtherRows: [], openCountAfterEsc: 0, focusOnSameRowBtn: true, overlapAny: false },
          circuit: { dead: false, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false }, kbd: { reached: true, tabs: 3 } });
      }
    }
    return xs;
  };
  const ordered = (r, xs) => {
    const rank = (sc) => r.plan.screenOrder.indexOf(sc);
    xs.sort((a, b) => (a.delay - b.delay) || (rank(a.screen) - rank(b.screen)));
    xs.forEach((s, i) => { s.order = i + 1; s.contextId = i + 1; });
    return xs;
  };
  const run = (r, xs, extra) => J.judge(r, k1Entry(ordered(r, xs)), extra || {});
  const v0 = run(R5, base(R5));
  check('고침5 바탕: 손대지 않은 합성 원자료 = 빨강 아님 · round 판독 가능(아래 대조군의 기준)', v0.red === false && v0.round.state === F5.roundReadable && v0.round.red === false, v0.redReasons);

  // ① Σ 칸 = totals
  {
    const xs = base(R5);
    xs.splice(xs.findIndex((s) => s.item === 'I-2'), 1);
    const add = JSON.parse(JSON.stringify(xs.find((s) => s.item === 'I-4' && s.screen === 'after' && s.case === 'mouse-open')));
    add.sampleNo = 2; aur(add); xs.push(add);
    const un = JSON.parse(JSON.stringify(xs[0])); un.cell = 'X|계획밖'; xs.push(un);
    const v = run(R5, xs);
    const t = v.totals;
    const S = (f) => v.cells.reduce((n, c) => n + f(c), 0);
    check('고침5 ①: Σ 칸 = totals(계획 · 받음 · 누락 · 제외 넷) · 옛 planned = 칸 수 그대로 · 받음 + 계획 밖 = 표본 · devicePlan 같음',
      t.plannedSamples === S((c) => c.planned) && t.gotSamples === S((c) => c.got) && t.missingSamples === S((c) => c.missing)
      && ['afterUnreachable', 'deviceFailure', 'maskConflict', 'maskedRead'].every((k) => t.excluded[k] === S((c) => c.excluded[k]))
      && t.planned === t.cells && t.cells === v.cells.length && t.gotSamples + t.excluded.outOfPlan === t.samples && v.devicePlan.plannedSamples === t.plannedSamples
      && v.totalsMeaning.planned === F5.totalsPlannedMeaning, t);
    plant('고침5 ① 심은 값: 누락 1 · /after 미도달 제외 1 · 계획 밖 1 이 그대로 잡힘(대조군 = 0 · 0 · 0)',
      t.missingSamples === 1 && t.excluded.afterUnreachable === 1 && t.excluded.outOfPlan === 1 && v0.totals.missingSamples === 0 && v0.totals.excluded.afterUnreachable === 0 && v0.totals.excluded.outOfPlan === 0, { t, t0: v0.totals });
    const c = v.cells.find((x) => x.item === 'I-4' && x.screen === 'after' && x.case === 'mouse-open');
    check('고침5 ⑦: /after 미도달이 있는데 실패 안 한 칸에도 사유 글자 · 비교 기준 없으면 exclNoRef', c.status === '표본 있음' && c.afterUnreachable.reason === F5.afterUrReason && c.exclusionReview.count === 1 && c.exclusionReview.ofGot === 2 && c.exclusionReview.sameShapeAsD1 === null && c.exclusionReview.reading === F5.exclNoRef, c);
    check('고침5 ⑦: 미도달 0 인 /after 칸에도 사유 글자 · 판독 null', v0.cells.filter((x) => x.screen === 'after').every((x) => x.afterUnreachable.reason === F5.afterUrReason && x.exclusionReview.count === 0 && x.exclusionReview.reading === null));
  }
  // ⑦ 제외 표본 판독 — 같은 묶음 /before 미도달 경로와 같은 꼴인가
  {
    const mk = (pAfter) => {
      const xs = base(R5);
      for (const s of xs) if (s.item === 'I-6' && s.case === 'esc' && s.screen === 'before') ku(s, P_D1);
      const add = JSON.parse(JSON.stringify(xs.find((s) => s.item === 'I-6' && s.case === 'esc' && s.screen === 'after')));
      add.sampleNo = 2; aur(add, pAfter); xs.push(add);
      const v = run(R5, xs);
      return { v, c: v.cells.find((x) => x.item === 'I-6' && x.case === 'esc' && x.screen === 'after') };
    };
    const same = mk(P_D1);
    const diff = mk(P_OTHER);
    plant('고침5 ⑦: /after 제외 경로 = /before D1 경로 → 「D1 모양과 같음」 · hiddenDefectSuspect 1', same.c.exclusionReview.sameShapeAsD1 === true && same.c.exclusionReview.reading === F5.exclSame && same.v.hiddenDefectSuspect === 1, same.c.exclusionReview);
    check('고침5 ⑦ 대조군: 경로 다름 → 「D1 모양과 다름」 · hiddenDefectSuspect 0', diff.c.exclusionReview.sameShapeAsD1 === false && diff.c.exclusionReview.reading === F5.exclDiff && diff.v.hiddenDefectSuspect === 0, diff.c.exclusionReview);
  }
  // ② · H1-3 · H1-4 칸 넷
  {
    const xs = base(R5);
    for (const s of xs) if (s.item === 'I-6' && s.screen === 'before') ku(s);
    const i6 = run(R5, xs).items['I-6'];
    plant('고침5 ②: /before 미도달 → contrastState 대조 무효 · 사유 자극 실행 불가 · d1Reproduced > 0 · 둘 다 남음 · 옛 contrast 칸 유지',
      i6.contrastState.state === F5.contrastInvalid && i6.contrastState.reason === F5.contrastNoStimulus && i6.d1Reproduced.count === 2 && i6.d1Reproduced.of === 2 && i6.contrast === T['I-6'].contrastInvalid && i6.fields5 === F5.legacyFields, i6);
    const ys = base(R5);
    for (const s of ys) if (s.item === 'I-6' && s.screen === 'before') s.readings.maxOpenCount = 2;
    const w = run(R5, ys).items['I-6'];
    check('고침5 ② 대조군: /before 재현 → 성립 · d1 0 · 옛 「재현됨」 = beforeFailRepro 칸으로 가름', w.contrastState.state === F5.contrastHolds && w.contrastState.reason === null && w.d1Reproduced.count === 0 && w.beforeFailRepro.count === 2 && w.contrast === '재현됨', w);
    const u = v0.items;
    check('고침5 ② 대조군: 도달 · 재현 0 → 대조 무효 · 사유 = 재현 못 함(자극 실행 불가 아님)', u['I-6'].contrastState.state === F5.contrastInvalid && u['I-6'].contrastState.reason === F5.contrastNoRepro, u['I-6'].contrastState);
    check('고침5 H1-4: I-2 = 대조 기준 미정(정본 인용 없음 · 대조 무효와 다른 글자) · I-1 · I-4 · I-6 · I-7 · N-1 = 미정 아님',
      u['I-2'].contrastState.state === F5.contrastUndefined && u['I-2'].contrastState.reason === F5.contrastNoShapeReason && ['I-1', 'I-4', 'I-6', 'I-7', 'N-1'].every((k) => u[k].contrastState.state !== F5.contrastUndefined), Object.keys(u).map((k) => [k, u[k].contrastState && u[k].contrastState.state]));
    const zs = base(R5);
    for (const s of zs) if (s.item === 'I-1' && s.screen === 'before') ku(s);
    plant('고침5 H1-4: I-1 /before 미도달(D1 = 실패 꼴) → 성립 · 대조군(도달) → 대조 무효', run(R5, zs).items['I-1'].contrastState.state === F5.contrastHolds && u['I-1'].contrastState.state === F5.contrastInvalid);
    const es = base(R5);
    es.find((s) => s.item === 'I-4' && s.screen === 'before' && s.case === 'mouse-open').readings.openCountAfterEsc = 1;
    // H4 결-H4: I-4 항목 contrastState 는 경우별(두 경우 성립일 때만) — 고침5 옛 판독(before 칸 · 항목 꼴)은 contrastStateFix5 에 그대로
    plant('고침5 H1-4: I-4 /before Esc 뒤 열림 남음 → 성립 · 대조군(닫힘) → 대조 무효 (H4: 옛 판독 칸 contrastStateFix5)', run(R5, es).items['I-4'].contrastStateFix5.state === F5.contrastHolds && u['I-4'].contrastStateFix5.state === F5.contrastInvalid);
    const f6 = run(R5, base(R5).filter((s) => !(s.item === 'I-6' && s.screen === 'before' && s.case === 'esc'))).items['I-6'];
    plant('고침5 H1-3: 대조 칸 장치 실패 → contrastState.state null(대조 불가 사유) · deviceFailure 칸 · 사유 따로 · 미측정 아님',
      f6.contrastState.state === null && f6.contrastState.reason === F5.contrastDeviceFail && f6.deviceFailure.flag === true && f6.deviceFailure.cells.length === 1 && String(f6.deviceFailure.reasons[0]).startsWith(T.cellShortReason) && f6.notMeasured.flag === false, f6);
    check('고침5 H1-3 대조군: 장치 실패 0 → deviceFailure.flag 거짓 · 전 항목 칸 넷 있음', ['I-1', 'I-2', 'I-4', 'I-6', 'I-7', 'N-1'].every((k) => u[k].deviceFailure.flag === false && u[k].d1Reproduced && u[k].contrastState && u[k].notMeasured));
  }
  // ③ 시도 있는데 「미측정」 0
  {
    const ks = ['I-1', 'I-2', 'I-4', 'I-6', 'I-7', 'N-1'];
    check('고침5 ③: 시도 있음 → 미측정 0(전 항목)', ks.every((k) => v0.items[k].notMeasured.flag === false && v0.items[k].notMeasured.reason === null));
    const w = run(R5, base(R5).filter((s) => s.item !== 'I-7')).items['I-7'];
    // 고침5-B ⓐ: 이 경우는 시도 기록 줄도 0(= 「시도 0」) — 이름만 고침 · 기대 무변. 시도 있음 + 표본 0 은 아래 고침5-B ⓐ 시험
    plant('고침5 ③ 대조군(고침5-B 이름 고침): I-7 시도 기록 0 · 원자료 0 → 미측정 표지 + 장치 실패(칸)', w.notMeasured.flag === true && w.notMeasured.reason === F5.notMeasuredReason && w.deviceFailure.flag === true && w.attempted.records === 0, w.notMeasured);
  }
  // 고침5-B ⓐ(PM 결정 11/16): 시도 기록 줄 — 시도 있음 + 표본 0 = 장치 실패만 · 시도 0 = 미측정
  {
    const attemptsFor = (r, pred) => buildPlan(r).cells.filter(pred).map((c) => ({ kind: J.ATTEMPT_KIND, item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, cell: c.key, planned: c.samples }));
    const i7cells = buildPlan(R5).cells.filter((c) => c.item === 'I-7');
    const va = J.judge(R5, k1Entry(ordered(R5, base(R5).filter((s) => s.item !== 'I-7')).concat(attemptsFor(R5, (c) => c.item === 'I-7'))), {});
    const a7 = va.items['I-7'];
    plant('고침5-B ⓐ: I-7 시도 기록 있음 · 표본 0 → 미측정 표지 0 · 장치 실패만 · attempted = 칸 수 · 칸 attemptRecords 1 · got 0',
      a7.notMeasured.flag === false && a7.notMeasured.reason === null && a7.deviceFailure.flag === true && a7.attempted.records === i7cells.length && a7.attempted.cells === i7cells.length
      && va.cells.filter((c) => c.item === 'I-7').every((c) => c.attemptRecords === 1 && c.got === 0), { nm: a7.notMeasured, att: a7.attempted });
    const vb = J.judge(R5, k1Entry(ordered(R5, base(R5)).concat(attemptsFor(R5, () => true))), {});
    check('고침5-B ⓐ 대조군: 전 칸 시도 줄을 더해도 표본 수 · 빨강 사유 · 순서 문제 = 시도 줄 없을 때와 같음 · totals.attemptRecords = 칸 수',
      vb.red === v0.red && vb.totals.samples === v0.totals.samples && vb.totals.orderProblems === v0.totals.orderProblems && JSON.stringify(vb.redReasons) === JSON.stringify(v0.redReasons)
      && vb.totals.attemptRecords === buildPlan(R5).cells.length && vb.totals.attemptOutOfPlan === 0, vb.redReasons);
    const vc = J.judge(R5, k1Entry(ordered(R5, base(R5)).concat([{ kind: J.ATTEMPT_KIND, item: 'I-9', cell: 'I-9|x' }])), {});
    plant('고침5-B ⓐ: 계획 밖 시도 줄 → 빨강 사유 「계획 밖 시도 기록 1건」', vc.red === true && vc.redReasons.includes(F5.attemptOutOfPlan + ' 1건') && vc.totals.attemptOutOfPlan === 1, vc.redReasons);
    const ri = fs.readFileSync(path.join(HERE, 'run-items.js'), 'utf8');
    check('고침5-B ⓐ 원자료: run-items.js 가 칸마다 표본보다 먼저 시도 줄(kind = ATTEMPT_KIND · order 없음) · 파생 칸도',
      J.ATTEMPT_KIND === 'attempt' && ri.includes('kind: ATTEMPT_KIND') && ri.includes('writeAttempt(dc, cell);') && ri.indexOf('writeAttempt(cell, null);') > 0
      && ri.indexOf('writeAttempt(cell, null);') < ri.indexOf('for (let n = 1; n <= cell.samples; n++)'));
  }
  // 고침5-B ④ — 자체시험 구멍 2(리뷰서 대조군 M1 · M3 초록)를 막는 시험
  {
    // M3: /before 칸만 표본 0 인 항목에 「미측정」 0
    const m6 = run(R5, base(R5).filter((s) => !(s.item === 'I-6' && s.screen === 'before'))).items['I-6'];
    plant('고침5-B ④(M3 막음): I-6 /before 칸만 표본 0 · /after 있음 → 미측정 0 · 장치 실패 · 대조 불가(state null)',
      m6.notMeasured.flag === false && m6.notMeasured.reason === null && m6.deviceFailure.flag === true && m6.contrastState.state === null && m6.contrastState.reason === F5.contrastDeviceFail, { nm: m6.notMeasured, cs: m6.contrastState.state });
    // M1: Q2 사유 글자를 rules 밖 글자(작업지시서 H1-4 의 Q2 글자)에 고정
    const Q2_INVALID = '대조 무효';
    const Q2_NO_STIMULUS = '/before 키보드 도달 0 — 자극 실행 불가(D1 재현) · 비교 성립 안 함';
    const xq = base(R5);
    for (const s of xq) if (s.item === 'I-6' && s.screen === 'before') ku(s);
    const q6 = run(R5, xq.map((s) => JSON.parse(JSON.stringify(s)))).items['I-6'];
    check('고침5-B ④(M1 막음): Q2 글자 고정 — rules 글자 · 판독 결과 사유 = Q2 글자 그대로(rules 자기 대조 아님)',
      F5.contrastInvalid === Q2_INVALID && F5.contrastNoStimulus === Q2_NO_STIMULUS && q6.contrastState.state === Q2_INVALID && q6.contrastState.reason === Q2_NO_STIMULUS, q6.contrastState);
    const Rq = JSON.parse(JSON.stringify(R5));
    Rq.readingText.fix5.contrastNoStimulus = Rq.readingText.fix5.contrastNoStimulus.replace('(D1 재현)', '');
    if (typeof Rq.readingText.fix5.contrastNoStimScreen === 'string') Rq.readingText.fix5.contrastNoStimScreen = Rq.readingText.fix5.contrastNoStimScreen.replace('(D1 재현)', ''); // 고침6 X2: 사유는 새 키(화면 이름 꼴)에서 나옴 → 같은 돌연변이를 새 키에도
    const qm = run(Rq, xq.map((s) => JSON.parse(JSON.stringify(s)))).items['I-6'];
    plant('고침5-B ④ 대조군: 사유 글자에서 「(D1 재현)」 뺀 rules 사본 → 판독 사유 ≠ Q2 글자(위 시험이 잡는 꼴)', qm.contrastState.state === Q2_INVALID && qm.contrastState.reason !== Q2_NO_STIMULUS, qm.contrastState);
  }
  // 고침5-B ① · ③ — MAPPING §10 ↔ rules exposure 글자 · 노4 화면 · I-4 출처
  {
    const mapTxt = fs.readFileSync(path.join(HERE, 'MAPPING.md'), 'utf8').split(String.fromCharCode(13)).join('');
    const mrows = {};
    for (const line of mapTxt.split(String.fromCharCode(10))) {
      if (!line.startsWith('| 노')) continue;
      const cols = line.slice(2, -2).split(' | ');
      mrows[cols[0]] = cols.slice(1);
    }
    const all = (rules.exposure || []).concat(rules.exposureNonCell || []);
    const same = (e) => { const m = mrows[e.no]; return !!m && m.length === 4 && e.shapeText === m[0] && e.rawSource === m[1] && e.exposedText === m[2] && e.q4cList === m[3]; };
    const shapeOk = (e) => typeof e.screen === 'string' && typeof e.shapeText === 'string' && e.shapeText.includes(e.item + ' · ' + e.screen + (e.delay === undefined ? '' : ' · ' + e.delay));
    const bad = all.filter((e) => !same(e)).map((e) => e.no || '(no 없음)').concat((rules.exposure || []).filter((e) => !shapeOk(e)).map((e) => 'shape:' + e.no));
    const covered = new Set(all.map((e) => e.no));
    // J1-B([3-V] 사소): 제목 「11행 전부 짝」 → 실제 값 13 · J1-B 노14(참고 행)를 더해서 이 단언은 노1~노13 범위로 셈(뜻 무변) · 노14 는 아래 새 단언
    const upTo13 = (k) => /^노([1-9]|1[0-3])$/.test(k);
    const m13 = Object.keys(mrows).filter(upTo13);
    const c13 = new Set(Array.from(covered).filter(upTo13));
    check('고침5-B ① · 고침6(옛 단언 11행 → 13행 · 노12 · 노13 더함): MAPPING §10 노1~노13 ↔ rules exposure ∪ exposureNonCell 글자 일치(칸 꼴 · 출처 · 노출 내용 · Q4ⓒ) · 13행 전부 짝 · 항목 칸 행 = 화면 칸 있음 · 칸 꼴 글자에 항목 · 화면 · 지연',
      m13.length === 13 && bad.length === 0 && m13.every((k) => c13.has(k)) && c13.size === 13, { rows: m13.length, bad });
    // J1-B([4] ①): 노14(/after kbd-open 참고 행) · 노12 출처 글자 정정 · 칸별 알려진 결과 · 옛 글자 보존 · 전체 14행 짝
    const N14_TXT = '/after kbd-open(I-4 · 방향당 0·150·300) — 같은 칸 노출 없음. 가족 참고: 11/8 수정안 A 사본(`644e27eb`+1줄 · 레포 미커밋) HQA 절차 36/36 🟢 · 기다림 훑기(방향당 300 · 0~1800) 24/24 🟢. `9c358a00`(A′)은 A2 가드 · F13 · F14 를 더한 다른 글자. A′ 자체의 HQA 700ms 칸 측정 = 찾지 못함';
    const KNOWN12 = { 0: '방향당 0 🟢 12/12(복원)', 150: '방향당 150 🟢 12/12(복원)', 300: '방향당 300 🔴 0/12(끝 초점 BODY)' };
    const j1bExpo = (R) => {
      const allR = (R.exposure || []).concat(R.exposureNonCell || []);
      const cov = new Set(allR.map((e) => e.no));
      const n14 = (R.exposureNonCell || []).filter((e) => e.no === '노14');
      const r12 = (R.exposure || []).filter((e) => e.no === '노12');
      return n14.length === 1 && n14[0].exposedText === N14_TXT && same(n14[0]) && !(R.exposure || []).some((e) => e.no === '노14') && Object.keys(mrows).length === 14 && cov.size === 14 && Object.keys(mrows).every((k) => cov.has(k))
        && r12.length === 3 && r12.every((e) => same(e) && e.source.startsWith('11/8 근본원인 §2 묶음 A(지금 판') && e.rawSource.startsWith('11/8 근본원인 §2 묶음 A(지금 판') && e.rawSource.includes('방향당 0 🟢 12/12 · 150 🟢 12/12 · 300 🔴 0/12 끝 초점 BODY')
          && e.knownResult === KNOWN12[e.delay] && typeof e._source_옛값_J1B === 'string' && e._source_옛값_J1B.startsWith('11/8 근본원인 §2 A(') && typeof e._rawSource_옛값_J1B === 'string' && e._rawSource_옛값_J1B.startsWith('11/8 근본원인 §2 A('));
    };
    check('J1-B ①: 노14 참고 행(exposureNonCell 하나 · 노출 내용 글자 그대로 · 판독기 표지 0) · MAPPING 14행 전부 짝 · 노12 출처 「11/8 근본원인 §2 묶음 A(지금 판)」 + 칸별 알려진 결과(0 🟢 12/12 · 150 🟢 12/12 · 300 🔴 0/12 끝 초점 BODY) · 옛 글자 보존',
      j1bExpo(rules), { rows: Object.keys(mrows).length, r12: (rules.exposure || []).filter((e) => e.no === '노12').map((e) => e.knownResult) });
    const RX1 = JSON.parse(JSON.stringify(rules));
    for (const e of RX1.exposure) if (e.no === '노12') { e.source = e._source_옛값_J1B; e.rawSource = e._rawSource_옛값_J1B; delete e.knownResult; }
    const RX2 = JSON.parse(JSON.stringify(rules)); RX2.exposureNonCell = RX2.exposureNonCell.filter((e) => e.no !== '노14');
    const RX3 = JSON.parse(JSON.stringify(rules)); for (const e of RX3.exposure) if (e.no === '노12' && e.delay === 300) e.knownResult = KNOWN12[0];
    plant('J1-B ① 심은 위반: 노12 를 옛 출처 글자로 되돌린 사본 · 노14 뺀 사본 · 300 칸 결과를 0 칸 글자로 바꾼 사본 → 셋 다 빨강', !j1bExpo(RX1) && !j1bExpo(RX2) && !j1bExpo(RX3));
    const RE = JSON.parse(JSON.stringify(rules));
    RE.hqaPending.samplesPerCell.value = { default: 1, 'N-1': 1 };
    const expo = (r) => {
      const v = J.judge(r, k1Entry(ordered(r, base(r))), {});
      const ce = (item, screen, delay, kase) => v.cells.find((c) => c.item === item && c.screen === screen && c.delay === delay && (!kase || c.case === kase));
      const src = (c) => (c && c.exposed ? c.exposed.source : []);
      return { i4b: src(ce('I-4', 'before', 0, 'kbd-open')), i4a: src(ce('I-4', 'after', 0, 'kbd-open')), i4m: src(ce('I-4', 'before', 0, 'mouse-open')), i5a: src(ce('I-5', 'after', 0, 'tab')), i6a: src(ce('I-6', 'after', 0, 'enter')), n1b: src(ce('N-1', 'before', 0)) };
    };
    const ne = expo(RE);
    check('고침5-B ① 동작: 노4 화면 = after — I-4 /after kbd-open 노출 · /before kbd-open 은 노7 출처 하나만 · mouse-open 노출 0 · 노6 I-5 · I-6 /after 노출 · 노7 N-1 /before 0 노출',
      ne.i4a.includes('리허설 2 런 43748603465') && ne.i4b.length === 1 && !ne.i4b.includes('리허설 2 런 43748603465') && ne.i4m.length === 0 && ne.i5a.length === 1 && ne.i6a.length === 1 && ne.n1b.length === 1, ne);
    const REo = JSON.parse(JSON.stringify(RE));
    REo.exposure = REo.exposure.filter((e) => e.no !== '노4' && e.no !== '노6' && !(e.no === '노7' && e.item === 'N-1')).map((e) => ({ item: e.item, screen: e.screen, case: e.case, delay: e.delay, source: e.source }));
    REo.exposure.splice(3, 0, RE.exposure_옛행_고침5B[0].row);
    const oe = expo(REo);
    plant('고침5-B ① 대조군: 고침5-B 전 exposure(옛 노4 행 · 노6 · 노7 N-1 없음) → /before kbd-open 출처 2 · I-5 /after · N-1 /before 0 노출 0(위 시험이 잡는 꼴)',
      oe.i4b.length === 2 && oe.i5a.length === 0 && oe.n1b.length === 0, oe);
    const s4 = rules.readingText.beforeFailShape['I-4'];
    check('고침5-B ③: rules I-4 /before 실패 꼴 출처 = 「A §3-4(L40) 코드 원문 대조 Esc 원인 문장」 · 옛 글자 = _source_옛값_고침5', s4.source.startsWith('A §3-4(L40) 코드 원문 대조 Esc 원인 문장') && s4._source_옛값_고침5 === 'A §3-2 · §4 코드 원문 대조 Esc 원인 문장', s4);
  }
  // 고침5-B ⓑ · ⓒ — run.json 회차 줄(boot.sh round 를 그대로 돌림 · 임시 폴더)
  {
    const crypto = require('crypto');
    const bs = path.join(HERE, 'boot.sh');
    const mkRaw = (name, files) => {
      const d = path.join(tmp, name); const w = path.join(tmp, name + '-w');
      fs.mkdirSync(d, { recursive: true }); fs.mkdirSync(w, { recursive: true });
      for (const f of Object.keys(files)) fs.writeFileSync(path.join(d, f), JSON.stringify(files[f]), 'utf8');
      return [d, w];
    };
    const roundOf = (dw) => {
      const r = spawnSync('bash', [bs, 'round', dw[0], dw[1]], { encoding: 'utf8' });
      let doc = null;
      try { doc = JSON.parse(fs.readFileSync(path.join(dw[0], 'run.json'), 'utf8')); } catch (e) { doc = null; }
      return { code: r.status, rl: doc && doc.round_line ? doc.round_line : null, err: String(r.stderr || '').slice(0, 300) };
    };
    const a = roundOf(mkRaw('b5-withheld', { 'run.json': { mode: 'armed' }, 'withheld.json': { files: [{ name: 'verdict-device.json' }], stages: { verdict: [{ name: 'verdict-device.json' }] } } }));
    plant('고침5-B ⓒ: 장전 · verdict 보류 → run.json 회차 줄 round = 「판정 불가/미통과」(null 아님) · 사유 = 보류 글자 · red = true · verdict SHA null',
      a.code === 0 && !!a.rl && !!a.rl.round && a.rl.round.state === F5.roundUnreadable && a.rl.round.reasons[0] === F5.roundVerdictWithheld && a.rl.red === true && a.rl.verdict_withheld === true && a.rl.verdict_sha256 === null, a);
    const n = roundOf(mkRaw('b5-absent', { 'run.json': { mode: 'armed' } }));
    plant('고침5-B ⓒ: 장전 · verdict 없음(보류 아님) → round = 「판정 불가/미통과」 · 사유 = 없음 글자', n.code === 0 && !!n.rl && !!n.rl.round && n.rl.round.state === F5.roundUnreadable && n.rl.round.reasons[0] === F5.roundVerdictAbsent && n.rl.red === true, n);
    const vdoc = { round: { state: F5.roundReadable, reasons: [], red: false }, red: false };
    const dwP = mkRaw('b5-present', { 'run.json': { mode: 'armed' }, 'verdict-device.json': vdoc });
    const p = roundOf(dwP);
    const vsha = crypto.createHash('sha256').update(fs.readFileSync(path.join(dwP[0], 'verdict-device.json'))).digest('hex');
    check('고침5-B ⓒ 대조군 · ⓑ: verdict 있음 → round = verdict round 그대로 · verdict 파일 이름 · SHA-256 = 파일 바이트(분모 · 제외 · 은폐 판독은 verdict 안)',
      p.code === 0 && !!p.rl && !!p.rl.round && p.rl.round.state === F5.roundReadable && p.rl.red === false && p.rl.verdict_file === 'verdict-device.json' && p.rl.verdict_sha256 === vsha, p);
    const h = roundOf(mkRaw('b5-rehearsal', { 'run.json': { mode: 'rehearsal' } }));
    check('고침5-B ⓒ 대조군: 리허설(판독기 안 돎) → round null 그대로(옛 동작)', h.code === 0 && !!h.rl && h.rl.round === null && h.rl.red === null, h);
  }
  // ④ 가림 충돌 · 결-6 가린 값 · 대조군 두 벌
  {
    const tok = 'abcdefghij'.repeat(3);
    const xs = base(R5); xs.find((s) => s.item === 'I-2').readings.focusAfter = { role: tok };
    const v = run(R5, xs);
    plant('고침5 ④: 판정 칸 가림 충돌 1 → round 판정 불가/미통과 · red', v.round.state === F5.roundUnreadable && v.round.red === true && v.red === true && v.round.reasons.some((r) => r.startsWith(F5.roundMaskConflict)), v.round);
    const m = base(R5); m.find((s) => s.item === 'I-2').readings.focusAfter = { role: '<가림:3>' };
    const vm = run(R5, m);
    plant('고침5 결-6: 판정 칸에 <가림:n> → 그 표본 장치 실패 · round 판정 불가/미통과 · red', vm.round.state === F5.roundUnreadable && vm.red === true && vm.totals.excluded.maskedRead === 1 && vm.totals.maskedReadSamples === 1 && vm.round.reasons.some((r) => r.startsWith(F5.roundMaskedRead)), vm.round);
    const n = base(R5); n.find((s) => s.item === 'I-2').readings.note = '<가림:3>';
    const vn = run(R5, n);
    check('고침5 결-6 대조군 1: 판정 칸 밖 <가림:n> → 판독 가능 · 빨강 아님', vn.round.state === F5.roundReadable && vn.red === false && vn.totals.excluded.maskedRead === 0, vn.redReasons);
    const ok2 = base(R5); ok2.find((s) => s.item === 'I-2').readings.focusAfter = { role: 'button' };
    const vo = run(R5, ok2);
    check('고침5 결-6 대조군 2: 같은 판정 칸 · 가림 없는 값 → 판독 가능 · 빨강 아님', vo.round.state === F5.roundReadable && vo.red === false, vo.redReasons);
  }
  // ⑤ K-2 보류 · H1-2 머리 (명령줄 그대로)
  {
    const rp5 = path.join(tmp, 'rules-fix5.json');
    fs.writeFileSync(rp5, JSON.stringify(R5), 'utf8');
    const sp5 = path.join(tmp, 's5.jsonl');
    fs.writeFileSync(sp5, k1Entry(ordered(R5, base(R5))).map((o) => JSON.stringify(o)).join('\n') + '\n', 'utf8');
    let n5 = 0;
    const cli = (args) => {
      const op = path.join(tmp, `v5-${n5++}.json`);
      k1Cli(sp5, true);
      const r = spawnSync(process.execPath, [path.join(HERE, 'judge.js'), '--rules', rp5, '--samples', sp5, '--out', op].concat(args), { encoding: 'utf8' });
      return { code: r.status, v: fs.existsSync(op) ? JSON.parse(fs.readFileSync(op, 'utf8')) : null };
    };
    const wf = path.join(tmp, 'withheld-a.json'); fs.writeFileSync(wf, JSON.stringify({ stages: { measure: [{ rel: 'samples.jsonl', kinds: ['long'] }] }, files: [{ rel: 'samples.jsonl', kinds: ['long'] }] }));
    const wo = path.join(tmp, 'withheld-b.json'); fs.writeFileSync(wo, JSON.stringify({ files: [{ rel: 'shots/x.png', kinds: ['long'] }] }));
    const wbad = path.join(tmp, 'withheld-c.json'); fs.writeFileSync(wbad, '{');
    const a = cli(['--withheld', wf]);
    const b = cli(['--withheld', wo]);
    const c0 = cli([]);
    const d = cli(['--withheld', wbad]);
    const e = cli(['--withheld', path.join(tmp, 'none.json')]);
    plant('고침5 ⑤ K-2: 판독 원자료(samples.jsonl) 보류 1 → round 판정 불가/미통과 · red · 종료 1', a.code === 1 && a.v && a.v.round.state === F5.roundUnreadable && a.v.red === true && a.v.totals.withheldFiles === 1, a.v && a.v.round);
    check('고침5 ⑤ K-2 대조군: 판독 밖 파일 보류 → 판독 가능 · withheldFiles 1 / 인자 없음 · 파일 없음 → 보류 0 · 종료 0',
      b.code === 0 && b.v.round.state === F5.roundReadable && b.v.totals.withheldFiles === 1 && c0.code === 0 && c0.v.totals.withheldFiles === 0 && e.code === 0 && e.v.totals.withheldFiles === 0, { b: b.code, c: c0.code, e: e.code });
    plant('고침5 ⑤ K-2: 보류 목록 깨짐 → 종료 2 · 판독 파일 0(보류 0 으로 착각 0)', d.code === 2 && d.v === null, d.code);
    const jsha = require('crypto').createHash('sha256').update(fs.readFileSync(path.join(HERE, 'judge.js'))).digest('hex');
    check('고침5 H1-2: verdict 머리 judgeSha256 = 판독기 파일 SHA-256 · rulesSha256 · devicePlan · browserCondition · delayUnit 사본',
      c0.v.judgeSha256 === jsha && /^[0-9a-f]{64}$/.test(c0.v.rulesSha256) && c0.v.devicePlan.cells === c0.v.totals.cells && JSON.stringify(c0.v.browserCondition) === JSON.stringify(R5.hqaPending.browserCondition.value) && c0.v.delayUnit === R5.hqaPending.delayUnit.value && c0.v.delayUnit === 'per-direction-ms', { j: c0.v.judgeSha256 }); // 고침6 결-1: 옛 단언 「delayUnit === 'round-trip-ms'」 → 사본 = rules 값 · 방향당(옛 → 새 표 = 개발명세 부록 M)
  }
  // ⑥ 노출 표지 — 판정값 무변
  {
    const Rn = JSON.parse(JSON.stringify(R5)); Rn.exposure = [];
    const strip = (v) => JSON.parse(JSON.stringify(v), (k, x) => ((k === 'exposed' || k === 'independenceNote' || k === 'exposedCells') ? undefined : x));
    const va = run(R5, base(R5));
    const vb = run(Rn, base(Rn));
    check('고침5 ⑥: 노출 표 넣은 · 뺀 두 verdict 가 표지 밖 같음(판정 · 표본 · 셈 무변)', JSON.stringify(strip(va)) === JSON.stringify(strip(vb)));
    // 보탬 G-3: 노9 지연 칸(0) 보탬 → N-1 a2 칸(300 · 500) 노출 표지 0 (옛 기대 = 두 칸 모두 표지 · 옛 행 = exposure_옛행_H4)
    plant('고침5 ⑥ 대조군: 넣은 쪽에만 표지(I-4 kbd-open /before 0 · 항목 독립성 글자) · N-1 a2 칸 표지 0(G-3) · 뺀 쪽 0',
      va.cells.some((c) => c.item === 'I-4' && c.case === 'kbd-open' && c.screen === 'before' && c.exposed) && va.cells.filter((c) => c.item === 'N-1' && c.screen === 'a2').every((c) => !c.exposed)
      && va.items['I-4'].independenceNote === F5.independenceNote && vb.cells.every((c) => !c.exposed) && !vb.items['I-4'].independenceNote);
  }
  // H1-8 I-4 닫힌 뒤 초점
  {
    const xs = base(R5); xs.find((s) => s.item === 'I-4' && s.case === 'mouse-open' && s.screen === 'after').readings.focusOnSameRowBtn = false;
    const f = run(R5, xs).items['I-4'].focusAfterClose;
    const f0 = v0.items['I-4'].focusAfterClose;
    plant('고침5 H1-8: I-4 focusAfterClose 4줄(2경우 × 2화면) · 심은 off 1 이 그 줄에만 · 대조군 off 0', f.length === 4 && f.find((r) => r.case === 'mouse-open' && r.screen === 'after').offSameRowBtn === 1 && f.reduce((n, r) => n + r.offSameRowBtn, 0) === 1 && f0.every((r) => r.offSameRowBtn === 0 && r.onSameRowBtn === r.valid), f);
  }
  // ⑧ 관문 칸별 허용 꼴
  {
    const D9 = '1234567890';
    const conf5 = (r) => {
      const c = JSON.parse(JSON.stringify(r));
      for (const k of ['S1toS4', 'rawFormat', 'T3']) c.hqaPending[k].status = 'HQA 회신 반영 · 댓글 ' + D9;
      for (const k of ['samplesPerCell', 'delayUnit', 'browserCondition']) c.hqaPending[k].status = '개발팀 사전 고정 · 장전 전 공개 · 댓글 ' + D9;
      c.hqaPending.T3.value = 'x';
      c.plan.notMeasured.find((x) => x.item === 'I-8').approval = '미측정 허용 결재 · 댓글 ' + D9;
      return c;
    };
    const why = (r, k) => G.check(r).problems.filter((p) => p.key === k).map((p) => p.why);
    check('고침5 ⑧ 관문 대조군: ③④⑤ 사전 고정 꼴 + ①② · T3 반영 꼴 = 통과', G.check(conf5(rules)).ok, G.check(conf5(rules)).problems);
    const a1 = conf5(rules); a1.hqaPending.S1toS4.status = '개발팀 사전 고정 · 장전 전 공개 · 댓글 ' + D9;
    plant('고침5 ⑧ 관문: ① 에 사전 고정 꼴 = 거부', why(a1, 'S1toS4').includes('status'));
    const a2 = conf5(rules); a2.hqaPending.rawFormat.status = '개발팀 사전 고정 · 장전 전 공개 · 댓글 ' + D9;
    plant('고침5 ⑧ 관문: ② 에 사전 고정 꼴 = 거부', why(a2, 'rawFormat').includes('status'));
    const a6 = conf5(rules); a6.hqaPending.T3.status = '개발팀 사전 고정 · 장전 전 공개 · 댓글 ' + D9;
    plant('고침5 ⑧ 관문: ⑥ T3 에 사전 고정 꼴 = 거부(T3 그대로)', why(a6, 'T3').includes('status'));
    const dl = conf5(rules); delete dl.hqaPending.samplesPerCell;
    plant('고침5 ⑧ 관문: ③ 키 삭제 = 거부', why(dl, 'samplesPerCell').includes('missing'));
    const ph = conf5(rules); ph.hqaPending.delayUnit.status = '개발팀 사전 고정 · 장전 전 공개 · 댓글 <사전공개댓글>';
    plant('고침5 ⑧ 관문: 댓글 자리표 남음 = 거부', why(ph, 'delayUnit').includes('status'));
    const rs = conf5(rules); rs.hqaPending.rawFormat.value = 'samples.jsonl · verdict-device.json';
    plant('고침5 ⑧ 관문: ② 값 = 옛 꼴 글자 하나 = 거부(필수 키 넷)', why(rs, 'rawFormat').includes('shape'));
    const rk = conf5(rules); delete rk.hqaPending.rawFormat.value.hashes;
    plant('고침5 ⑧ 관문: ② 값 필수 키(해시 목록) 빠짐 = 거부', why(rk, 'rawFormat').includes('shape'));
    const now = G.check(rules).problems.map((p) => p.key);
    check('고침5 ⑧: 지금 rules = 거부 · 걸린 칸 = ③④⑤(자리표) + T3 · ①② 는 통과', !G.check(rules).ok && JSON.stringify(Array.from(new Set(now))) === '["samplesPerCell","delayUnit","browserCondition","T3"]', G.check(rules).problems);
  }
  // ⑨ hqaPendingOpen = 관문 꼴로 셈
  {
    const filled = JSON.parse(JSON.stringify(R5));
    for (const k of ['samplesPerCell', 'delayUnit', 'browserCondition']) filled.hqaPending[k].status = filled.hqaPending[k].status.replace('<사전공개댓글>', '1234567890');
    const vf = run(filled, base(filled));
    check('고침5 ⑨: 자리표 채운 뒤 hqaPendingOpen = T3 하나 · 지금 rules = 자리표 3 + T3 · 자리표 칸 따로 표시',
      JSON.stringify(vf.hqaPendingOpen) === '["T3"]' && vf.hqaPendingOpenCount === 1 && vf.hqaPendingPlaceholder.length === 0
      && JSON.stringify(v0.hqaPendingOpen) === '["samplesPerCell","delayUnit","browserCondition","T3"]' && v0.hqaPendingPlaceholder.length === 3, { vf: vf.hqaPendingOpen, v0: v0.hqaPendingOpen });
    const nt = JSON.parse(JSON.stringify(filled)); nt.hqaPending.T3.status = '검토 중';
    const old = Object.keys(nt.hqaPending).filter((k) => !k.startsWith('_') && String(nt.hqaPending[k].status).includes('HQA 회신 대기'));
    plant('고침5 ⑨ 대조군: T3 를 새 글자(검토 중)로 → 옛 셈 0(거짓 표시) · 새 셈 = T3 열림', old.length === 0 && JSON.stringify(run(nt, base(nt)).hqaPendingOpen) === '["T3"]');
  }
  // H1-10 · H1-11
  {
    const iSrc = fs.readFileSync(path.join(HERE, 'items', 'i-items.js'), 'utf8');
    check('고침5 H1-10: I-4 키보드 열기 불가 글자 = kbdOpenNoEscV5(「입력 안 함(키보드로 열기 불가)」) · 옛 글자 칸 유지', /escNote: ctx\.rules\.deviceReading\.kbdOpenNoEscV5 \}/.test(iSrc) && !/deviceReading\.kbdOpenNoEsc \}/.test(iSrc) && rules.deviceReading.kbdOpenNoEscV5 === '입력 안 함(키보드로 열기 불가)' && rules.deviceReading.kbdOpenNoEsc === '키보드로 열기 불가 — Esc 측정 0');
    const riSrc5 = fs.readFileSync(path.join(HERE, 'run-items.js'), 'utf8');
    const m = riSrc5.match(/function proxyDelayArg\(rules, delay\) \{[\s\S]*?\r?\n\}/);
    const fnP = m ? new Function('return ' + m[0])() : null;
    const rr = (u) => ({ hqaPending: { delayUnit: { value: u } } });
    // 고침6 결-1(T1): 옛 단언(고침5 H1-11 · 「rules 단위 = round-trip-ms」)은 아래 「고침5 시절」 대조군으로 옮김 — rules 값 단언만 새 단위로(옛 → 새 표 = 개발명세 부록 M)
    const srcUse = /String\(proxyDelayArg\(rules, delay\)\.perDirectionMs\)/.test(riSrc5) && /started: proxy\.ok \}, proxyDelayArg\(rules, delay\)\)/.test(riSrc5);
    const unitPerDir = (r) => !!fnP && r.hqaPending.delayUnit.value === 'per-direction-ms' && fnP(r, 300).perDirectionMs === 300 && fnP(r, 300).roundTripMs === 600 && fnP(r, 150).perDirectionMs === 150 && fnP(r, 0).perDirectionMs === 0;
    check('고침6 T1: rules 단위 = per-direction-ms(HQA 집행 단위) · 방향당 300 → 프록시 인자 300 · 왕복 기록 600 · 프록시 인자 = perDirectionMs · 표본 proxy 칸에 둘 다',
      unitPerDir(rules) && srcUse, { unit: rules.hqaPending.delayUnit.value });
    check('고침6 T1: 옛 값 보존 — _옛값_고침5 = round-trip-ms(고침5 note 그대로) · _옛값_고침4 = per-direction-ms · 상태 글자 자리표 그대로',
      rules.hqaPending.delayUnit._옛값_고침5 && rules.hqaPending.delayUnit._옛값_고침5.value === 'round-trip-ms' && /같은 나무 유일 글자/.test(rules.hqaPending.delayUnit._옛값_고침5.note)
      && rules.hqaPending.delayUnit._옛값_고침4.value === 'per-direction-ms' && rules.hqaPending.delayUnit.status === '개발팀 사전 고정 · 장전 전 공개 · 댓글 <사전공개댓글>');
    check('고침5 시절 대조군(T1 · 옛 단언 보존): 왕복 150 → 방향당 75 · 500 → 250 · 0 → 0(round-trip-ms 갈래 코드 무변)',
      !!fnP && fnP(rr('round-trip-ms'), 150).perDirectionMs === 75 && fnP(rr('round-trip-ms'), 500).perDirectionMs === 250 && fnP(rr('round-trip-ms'), 0).perDirectionMs === 0);
    const rtCopy = JSON.parse(JSON.stringify(rules)); rtCopy.hqaPending.delayUnit.value = 'round-trip-ms';
    plant('고침6 T1 심은 위반: rules 를 round-trip-ms 로 되돌린 사본 → 「단위 = 방향당」 단언 빨강(인자 150)', !unitPerDir(rtCopy) && fnP(rtCopy, 300).perDirectionMs === 150);
    plant('고침5 H1-11 대조군: 옛 단위(per-direction-ms) → 인자 = N 그대로 · 목록 밖 단위 = throw', !!fnP && fnP(rr('per-direction-ms'), 150).perDirectionMs === 150 && (() => { try { fnP(rr('ms'), 1); return false; } catch (e) { return true; } })());
  }
  // ⑩ K-1 — --out 숫자 = 표준 출력 숫자(안쪽 실행은 이 시험을 건너뜀)
  if (!process.env.HQA_SELFTEST_INNER) {
    const op = path.join(tmp, 'k1.json');
    const r = spawnSync(process.execPath, [path.join(HERE, 'selftest-judge.js'), '--out', op], { encoding: 'utf8', env: Object.assign({}, process.env, { HQA_SELFTEST_INNER: '1' }) });
    let last = null;
    try { last = JSON.parse((r.stdout || '').trim().split(/\r?\n/).pop()); } catch (e) { last = null; }
    const k = fs.existsSync(op) ? JSON.parse(fs.readFileSync(op, 'utf8')) : null;
    // 고침7 K1: 안쪽 프로세스의 판독기 호출 셈(안쪽 판정은 안쪽에서 따로 · 바깥은 하한 셈에만 더함)
    K1.inner = k && k.judgeCalls ? k.judgeCalls : null;
    check('고침5 ⑩ K-1: --out 숫자 = 표준 출력 숫자(total · failed) · passed = total − failed · plantedRed ≤ plantedTotal · plantedTotal ≥ 1',
      !!k && !!last && k.total === last.total && k.failed === last.fail && k.passed === k.total - k.failed && k.plantedTotal >= 1 && k.plantedRed <= k.plantedTotal, { k, last });
  }
}

// ── 고침5 결-10 甲 — F10 「목록 밑」 누름(I-7 below-list) · 이 커밋만 빼면 되돌아간다 ──
{
  const J = require('./judge.js');
  const R10 = JSON.parse(JSON.stringify(rules));
  R10.plan.items = ['I-7'];
  R10.plan.iDelays = [0];
  R10.hqaPending.samplesPerCell.value = { default: 1, 'N-1': 1 };
  const p10 = buildPlan(R10);
  const mk = (bad) => {
    const xs = p10.cells.map((c, i) => ({ item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, cell: c.key, sampleNo: 1, valid: true, errors: [], deviceReading: null, order: i + 1, contextId: i + 1,
      readings: { rowclickDelta: 0, changedOtherRows: [] }, circuit: { dead: false, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false } }));
    const rank = (sc) => R10.plan.screenOrder.indexOf(sc);
    xs.sort((a, b) => rank(a.screen) - rank(b.screen)).forEach((s, i) => { s.order = i + 1; s.contextId = i + 1; });
    if (bad) xs.find((s) => s.case === 'below-list' && s.screen === 'after').readings.changedOtherRows = [{ row: '2' }];
    return J.judge(R10, k1Entry(xs), {});
  };
  const ok = mk(false);
  const bad = mk(true);
  check('결-10 F10: I-7 계획에 below-list 칸(화면 2 × 지연) · 기대 글자 F10 무변', p10.cells.filter((c) => c.case === 'below-list').length === 2 && T['I-7'].expectedF10 === '연 줄뿐 · 다른 줄 0' && JSON.stringify(rules.plan.itemSpecs['I-7'].cases) === '["row-cell","below-list"]');
  plant('결-10 F10: /after 목록 밑 누름에서 다른 줄 변화 → I-7 afterMismatchExpected 1 · 대조군(변화 0) → 0', bad.items['I-7'].afterMismatchExpected === 1 && ok.items['I-7'].afterMismatchExpected === 0 && ok.red === false, { bad: bad.items['I-7'].afterMismatchExpected, ok: ok.items['I-7'].afterMismatchExpected });
  const src = fs.readFileSync(path.join(HERE, 'items', 'i-items.js'), 'utf8');
  const g0 = /async function i7Below/.test(src) && /if \(!below \|\| inside \|\| y >= rects\.viewport\.h\) throw new Error/.test(src) && /const below = y > lb\.y \+ lb\.h && x >= lb\.x && x <= lb\.x \+ lb\.w;/.test(src)
    && src.indexOf('if (!below || inside', src.indexOf('async function i7Below')) < src.indexOf('await L.mouseClick(ctx, x, y);', src.indexOf('async function i7Below'));
  check('결-10 F10 원문: i7Below 가 누르기 전에 「목록 상자 밖 · 아래」 를 단언(아니면 throw · 누름 0) · row-cell 길은 i7 그대로', g0 && /if \(ctx\.cell\.case === 'below-list'\) return i7Below\(ctx\);/.test(src));
  const cut = src.replace(/  if \(!below \|\| inside \|\| y >= rects\.viewport\.h\) throw new Error\([^\n]*\n/, '');
  plant('결-10 F10 원문 대조군: 단언 줄을 뺀 원문 → 검사 빨강', cut !== src && !(/if \(!below \|\| inside \|\| y >= rects\.viewport\.h\) throw new Error/.test(cut)));
}

// ── H4 §7-4 ①~⑪ · 보탬 G-1 ~ G-3 (작업지시서 H4 · PM 결재 11/16) — 새 시험마다 대조군(돌연변이 사본 = 빨강) ──
{
  const J = require('./judge.js');
  const M = require('./mask.js');
  const G = require('./hqa-gate.js');
  const H = T.h4;
  const F5 = T.fix5;
  const cp = () => JSON.parse(JSON.stringify(rules));
  const thr = (r) => { try { J.buildPlan(r); return null; } catch (e) { return String(e.message); } };
  // ① contrastScreen ↔ n1Contrast
  const b1 = cp(); b1.contrastScreen['N-1'] = 'before';
  check('H4 ①: 지금 rules = buildPlan 통과(대조군)', thr(rules) === null, thr(rules));
  plant('H4 ①: contrastScreen N-1 ≠ n1Contrast → 거부', /n1Contrast/.test(thr(b1) || ''), thr(b1));
  // ② ③ 비교 화면이 남의 항목(경우)에
  const b2 = cp(); b2.plan.itemSpecs['I-4'].caseScreens = { 'mouse-open': ['e1t3'], 'kbd-open': ['e1t3'] };
  const b2b = cp(); b2b.plan.itemSpecs['I-1'].screens = ['before', 'after', 'e1t3'];
  plant('H4 ②: e1t3 칸이 I-4 kbd-open 밖(I-4 mouse-open · I-1) → 거부', /계획 밖/.test(thr(b2) || '') && /계획 밖/.test(thr(b2b) || ''), [thr(b2), thr(b2b)]);
  const b3 = cp(); b3.plan.itemSpecs['I-6'].screens = ['before', 'after', 'v2'];
  plant('H4 ③: v2 칸이 I-8 밖(I-6) → 거부', /계획 밖/.test(thr(b3) || ''), thr(b3));
  const b3b = cp(); b3b.plan.itemSpecs['I-8'].screens = ['before', 'after'];
  plant('H4 ③b: 표가 가리킨 대조 화면 v2 가 I-8 계획에 없음 → 거부', /칸이 계획에 없음/.test(thr(b3b) || ''), thr(b3b));
  // 독립 반증: after css 화면 = 주소 /after 로 시작(시험 앱 App.razor L9) — e1t3 를 /e1t3 로 두면 빨강
  const cssRule = (r) => Object.keys(r.plan.screenTable).filter((k) => !k.startsWith('_')).every((k) => (r.plan.screenTable[k].css === 'after') === (r.plan.screenTable[k].path.slice(0, 6) === '/' + 'after'));
  const pe = cp(); pe.plan.screenTable.e1t3.path = '/e1t3';
  check('H4 반증: screenTable after css 화면 = 주소 앞머리 /after(시험 쪽 규칙 · 장치 코드 판단 0)', cssRule(rules));
  plant('H4 반증 대조군: e1t3 주소 /e1t3 → 빨강(before css 실림 조용한 실패)', !cssRule(pe));
  // 판독 — I-4 · I-8 만 · 지연 0 · 표본 1
  const RH = cp(); RH.plan.items = ['I-4', 'I-8']; RH.plan.iDelays = [0]; RH.hqaPending.samplesPerCell.value = { default: 1, 'N-1': 1 };
  const RHc = JSON.parse(JSON.stringify(RH)); RHc.t3ReadUnit = 'cell';
  const okRow = (kind, row) => ({ row, kind, buttons: 1, visibleButtons: 1, maxButtonArea: 100, hidden: 0, tabStops: 1, chip: '-' });
  const failRow = (kind, row) => ({ row, kind, buttons: 1, visibleButtons: 0, maxButtonArea: 0, hidden: 1, tabStops: 0, chip: '' });
  const mkH = (tweak, R) => {
    const RR = R || RH;
    const xs = J.buildPlan(RR).cells.map((c) => ({ item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, cell: c.key, sampleNo: 1, valid: true, errors: [], deviceReading: null,
      inputs: c.item === 'I-8' ? [] : [{ k: 'press', key: 'Escape' }],
      readings: c.item === 'I-8' ? { blankRows: [okRow('blank', '3'), okRow('empty', '4'), okRow('null', '6'), okRow('blank', '8')] } : { openCountAfterEsc: 0, focusOnSameRowBtn: true },
      circuit: { dead: false, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false } }));
    const rank = (sc) => RR.plan.screenOrder.indexOf(sc);
    xs.sort((a, b) => rank(a.screen) - rank(b.screen)).forEach((s, i) => { s.order = i + 1; s.contextId = i + 1; });
    if (tweak) tweak(xs);
    return J.judge(RR, k1Entry(xs), {});
  };
  const fx = (xs, item, kase, screen) => xs.find((s) => s.item === item && s.case === kase && s.screen === screen);
  const v0 = mkH();
  check('H4 판독 대조군: 정상 손 표본 → 빨강 아님 · I-8 · e1t3 칸 있음', v0.red === false && v0.cells.some((c) => c.screen === 'e1t3') && !!v0.items['I-8'], v0.redReasons);
  // ④ 두 경우 모두 성립일 때만
  const v4 = mkH((xs) => { fx(xs, 'I-4', 'mouse-open', 'before').readings.openCountAfterEsc = 1; });
  const v4b = mkH((xs) => { fx(xs, 'I-4', 'mouse-open', 'before').readings.openCountAfterEsc = 1; fx(xs, 'I-4', 'kbd-open', 'e1t3').readings.focusOnSameRowBtn = false; });
  check('H4 ④ 대조군: 두 경우 재현 → 항목 성립', v4b.items['I-4'].contrastState.state === F5.contrastHolds, v4b.items['I-4'].contrastByCase);
  plant('H4 ④: mouse-open 성립 + kbd-open 대조 무효 → 항목 성립 아님', v4.items['I-4'].contrastByCase['mouse-open'].state === F5.contrastHolds && v4.items['I-4'].contrastByCase['kbd-open'].state === F5.contrastInvalid && v4.items['I-4'].contrastState.state === F5.contrastInvalid, v4.items['I-4'].contrastByCase);
  // ⑤ /before kbd-open = 기록만(재현해도 대조에 안 들어감)
  const v5 = mkH((xs) => { fx(xs, 'I-4', 'kbd-open', 'before').readings.focusOnSameRowBtn = false; });
  const c5 = v5.cells.find((c) => c.item === 'I-4' && c.case === 'kbd-open' && c.screen === 'before');
  const c5m = v5.cells.find((c) => c.item === 'I-4' && c.case === 'mouse-open' && c.screen === 'before');
  plant('H4 ⑤: I-4 /before kbd-open 재현 → 기록만 · 경우 대조 = 무효 그대로 · mouse-open /before 표지 0', c5.recordOnlyRole === H.recordOnly && !c5m.recordOnlyRole && v5.items['I-4'].contrastByCase['kbd-open'].state === F5.contrastInvalid, c5);
  // ⑥ T3 항목 · 칸 단위
  const t3 = (xs) => { fx(xs, 'I-8', 'blank-rows', 'v2').readings.blankRows = [failRow('blank', '3'), okRow('empty', '4'), okRow('null', '6'), failRow('blank', '8')]; };
  const v6 = mkH(t3);
  const v6c = mkH(t3, RHc);
  const k6 = (v, k) => v.items['I-8'].contrastState.kinds.find((x) => x.kind === k);
  check('H4 ⑥: I-8 공백 재현 + 빈 · null 무재현 → item 단위 성립 · 빈 · null 꼴 = 대조 무효 표지', v6.items['I-8'].contrastState.state === F5.contrastHolds && v6.items['I-8'].contrastState.unit === 'item' && k6(v6, 'blank').contrast === F5.contrastHolds && k6(v6, 'empty').contrast === H.t3KindInvalid && k6(v6, 'null').contrast === H.t3KindInvalid, v6.items['I-8'].contrastState);
  plant('H4 ⑥ 대조군: 같은 원자료 · cell 단위 → 대조 무효', v6c.items['I-8'].contrastState.state === F5.contrastInvalid && v6c.items['I-8'].contrastState.unit === 'cell');
  const Rbad = JSON.parse(JSON.stringify(RH)); Rbad.t3ReadUnit = 'both';
  plant('H4 ⑥b: t3ReadUnit 목록 밖 → 규칙 오류(두 단위 동시 0)', (() => { try { mkH(null, Rbad); return false; } catch (e) { return /t3ReadUnit/.test(e.message); } })());
  // H4-G-B(검증팀장 [4] R-1 · PM 결정 · 대표 결정 ⓑ · 작업지시서 §7-2): item 단위 = 공백 꼴 줄로만 판정 · 빈 값 · null 꼴 = 기록만(항목 판정에 안 씀)
  const gb = (rows, R) => mkH((xs) => { fx(xs, 'I-8', 'blank-rows', 'v2').readings.blankRows = rows; }, R);
  const gbS = (v) => v.items['I-8'].contrastState;
  // 고침6 결-11 X2: 옛 단언 「reason === F5.contrastNoRepro(/before …)」 → 대조 화면 v2 이름을 넣은 글자(옛 → 새 표 = 개발명세 부록 M)
  const F5V2_NOREPRO = typeof F5.contrastNoReproScreen === 'string' && F5.contrastScreenLabel ? F5.contrastNoReproScreen.split('{screen}').join(F5.contrastScreenLabel.v2) : '(새 키 없음)';
  const gbK = (v, k) => gbS(v).kinds.find((x) => x.kind === k);
  const gbRec = (v) => ['empty', 'null'].every((k) => gbK(v, k).recordOnly === H.t3KindRecordOnly) && gbK(v, 'blank').recordOnly === null && JSON.stringify(gbS(v).judgedKinds) === '["blank"]';
  const gb1 = gb([okRow('blank', '3'), okRow('empty', '4'), failRow('null', '6'), okRow('blank', '8')]);
  plant('H4-G-B ①: I-8 null 줄만 재현(공백 무재현) → 항목 「성립」 아님(대조 무효) · null 꼴 칸 = 재현 기록 + 기록만 표지', gbS(gb1).state === F5.contrastInvalid && gbS(gb1).reason === F5V2_NOREPRO && gbK(gb1, 'null').contrast === F5.contrastHolds && gbRec(gb1), gbS(gb1));
  const gb2 = gb([okRow('blank', '3'), failRow('empty', '4'), okRow('null', '6'), okRow('blank', '8')]);
  plant('H4-G-B ②: I-8 빈 값 줄만 재현(공백 무재현) → 항목 「성립」 아님(대조 무효) · 빈 값 꼴 칸 = 재현 기록 + 기록만 표지', gbS(gb2).state === F5.contrastInvalid && gbS(gb2).reason === F5V2_NOREPRO && gbK(gb2, 'empty').contrast === F5.contrastHolds && gbRec(gb2), gbS(gb2));
  const gb3 = gb([failRow('blank', '3'), okRow('empty', '4'), okRow('null', '6'), failRow('blank', '8')]);
  check('H4-G-B ③: I-8 공백 줄만 재현 → 항목 성립 · 판정 꼴 = blank 하나 · 빈 값 · null = 기록만 표지 · 빨강 아님', gbS(gb3).state === F5.contrastHolds && gbRec(gb3) && gb3.red === false, gbS(gb3));
  const gb4 = gb([okRow('blank', '3'), failRow('empty', '4'), failRow('null', '6'), okRow('blank', '8')]);
  plant('H4-G-B ④: I-8 공백 재현 없음 + 빈 값 · null 재현 → 대조 무효(성립 아님)', gbS(gb4).state === F5.contrastInvalid && gbS(gb4).reason === F5V2_NOREPRO, gbS(gb4));
  const gb5 = gb([failRow('blank', '3'), failRow('blank', '8')]);
  check('H4-G-B ⑤: I-8 v2 빈 값 · null 줄 0 + 공백 재현 → 항목 성립 · 빨강 아님 · 줄 0 은 기록만 칸(noRowsRecordOnly v2:empty · v2:null)', gbS(gb5).state === F5.contrastHolds && gb5.red === false && gbK(gb5, 'empty').contrast === null && gbK(gb5, 'null').contrast === null && !!gbS(gb5).noRowsRecordOnly && gbS(gb5).noRowsRecordOnly.text === H.t3KindRecordOnly && JSON.stringify(gbS(gb5).noRowsRecordOnly.rows) === '["v2:empty","v2:null"]', { s: gbS(gb5), red: gb5.redReasons });
  const gb5c = gb([failRow('blank', '3'), failRow('blank', '8')], RHc);
  plant('H4-G-B ⑤ 대조군: 같은 원자료 · cell 단위 → 빈 값 · null 줄 0 = 장치 빨강 · state null(cell 은 옛 그대로)', gb5c.red === true && gbS(gb5c).state === null && gbS(gb5c).reason === H.t3NoKindRows, gb5c.redReasons);
  const gb6 = gb([okRow('empty', '4'), failRow('null', '6')]);
  plant('H4-G-B ⑥: I-8 v2 공백 줄 0(빈 값 · null 줄만) → 판정 꼴 판독 불가 · state null · 장치 빨강', gb6.red === true && gbS(gb6).state === null && gbS(gb6).reason === H.t3NoKindRows, gb6.redReasons);
  const Rik = JSON.parse(JSON.stringify(RH)); Rik.device.i8.itemKind = 'space';
  plant('H4-G-B ⑦: device.i8.itemKind 가 kinds 밖 → 규칙 오류', (() => { try { mkH(null, Rik); return false; } catch (e) { return /itemKind/.test(e.message); } })());
  check('H4-G-B ⑧: rules 글자 — t3UnitItem · t3ReadUnit_표지 에 「하나라도」 · 「어느 꼴이든」 0 · 옛 글자는 _옛해석 칸에 대체 표지와 함께 보존', !/하나라도|어느 꼴이든/.test(rules.readingText.h4.t3UnitItem + rules['t3ReadUnit_표지']) && /어느 꼴이든/.test(rules.readingText.h4['t3UnitItem_옛해석']) && /하나라도/.test(rules['t3ReadUnit_표지_옛해석']) && /대표 결정 ⓑ 로 대체/.test(rules.readingText.h4['t3UnitItem_옛해석'] + rules['t3ReadUnit_표지_옛해석']));
  // ⑦ After 숨김 → 어긋남 판독
  const v7 = mkH((xs) => { fx(xs, 'I-8', 'blank-rows', 'after').readings.blankRows = [failRow('blank', '3'), okRow('empty', '4'), okRow('null', '6'), okRow('blank', '8')]; });
  plant('H4 ⑦: I-8 After 공백 줄 숨김 → afterByKind blank 어긋남 1 · 대조군 0', v7.items['I-8'].afterByKind.find((k) => k.kind === 'blank').afterShapeMismatch === 1 && v0.items['I-8'].afterByKind.every((k) => k.afterShapeMismatch === 0));
  // ⑧ superseded 표지 → 미측정 셈 0
  const r8 = cp(); delete r8.plan.notMeasured.find((x) => x.item === 'I-8').superseded;
  const r8v = (() => { const R = JSON.parse(JSON.stringify(RH)); R.plan.notMeasured = r8.plan.notMeasured; return mkH(null, R); })();
  check('H4 ⑧: notMeasured I-8 대체 표지 → notMeasuredOpen 에 I-8 0 · 옛 notMeasured 칸 그대로', v0.notMeasuredOpen.every((x) => x.item !== 'I-8') && v0.notMeasured.some((x) => x.item === 'I-8'));
  plant('H4 ⑧ 대조군: 대체 표지 뺌 → I-8 미측정으로 셈', r8v.notMeasuredOpen.some((x) => x.item === 'I-8'));
  // ⑨ I-8 입력 기록 → 빨강 · 원문 정적 검사
  const v9 = mkH((xs) => { fx(xs, 'I-8', 'blank-rows', 'v2').inputs = [{ k: 'press', key: 'Tab' }]; });
  plant('H4 ⑨: I-8 표본에 입력 기록 → 그 칸 장치 실패 · 빨강', v9.red === true && v9.cells.find((c) => c.item === 'I-8' && c.screen === 'v2').status === T.cellFail, v9.redReasons);
  const I8 = require('./items/i-items.js')['I-8'].toString();
  const LB = require('./items/_lib.js').readBlankRows.toString();
  const INPUT = /\.(press|click|dblclick|tap|type|fill|focus|hover|check|selectOption|dispatchEvent|down|up)\s*\(|\bkeyboard\b|\bmouse\b|\btabTo\b|\blocatorClick\b|\browClickControl\b|\bmouseClick\b/;
  const i8ok = (src) => (src.match(/\bL\.[A-Za-z]+/g) || []).every((x) => ['L.settle', 'L.settleMs', 'L.readBlankRows', 'L.circuitSince', 'L.slim'].includes(x)) && !INPUT.test(src);
  check('H4 ⑨ 정적: i8 원문 L.* = 허용 목록만 · readBlankRows 원문 입력 0', i8ok(I8) && !INPUT.test(LB));
  plant('H4 ⑨ 정적 대조군: i8 원문에 L.press 심음 → 빨강', !i8ok(I8.replace('const rows', "await L.press(ctx, 'Tab');\n  const rows")));
  // ⑩ 비교 화면 글자에 「/after」 0
  const txts = (r) => ['contrastUnreachable', 'e1t3ProbeKeep', 'e1t3ProbeZero', 'e1t3ProbeNoRow', 'contrastVariantDiff', 'v2ProbeChecked'].map((k) => r.deviceReading[k]).concat(Object.values(r.readingText.h4).filter((x) => typeof x === 'string'));
  const noAfter = (r) => txts(r).every((x) => typeof x === 'string' && !x.includes('/after'));
  const z10 = mkH((xs) => { xs.splice(xs.findIndex((s) => s.screen === 'e1t3'), 1); });
  const zc = z10.cells.find((c) => c.screen === 'e1t3');
  check('H4 ⑩: 비교 화면 판독 글자 · 사유에 「/after」 0 · e1t3 표본 0 사유 = 비교 화면 표본 0', noAfter(rules) && zc.zeroReason === H.contrastZeroReason && !JSON.stringify(zc).includes('/after'), zc);
  const n10 = cp(); n10.deviceReading.contrastUnreachable = '/after' + '-e1t3 키보드 도달 0';
  plant('H4 ⑩ 대조군: 글자에 「/after」 심음 → 빨강', !noAfter(n10));
  // ⑪ hqaPendingOpen T3 = G-값 전 1 · 뒤 0
  const g1 = cp(); g1.hqaPending.T3.status = 'HQA 회신 반영 · 댓글 1234567890'; g1.hqaPending.T3.value = '11/7 작업지시서 L306 글자 고정 · 항목 단위';
  check('H4 ⑪: hqaPendingOpen T3 = G-값 전 1 · 뒤 0 · 관문 T3 = 계획 · 측정 코드 있으면 통과', G.openKeys(rules).includes('T3') && !G.openKeys(g1).includes('T3') && !G.check(g1, { itemsHave: ['I-8'] }).problems.some((p) => p.key === 'T3'));
  plant('H4 ⑪ 대조군: T3 확정 · 측정 코드 목록에 I-8 없음 → 관문 거부', G.check(g1, { itemsHave: ['I-1'] }).problems.some((p) => p.why === 'no-code-or-approval'));
  // probe 비교 화면 심은 위반
  const psrc = fs.readFileSync(path.join(HERE, 'probe.js'), 'utf8');
  const pm1 = psrc.replace("const reading = RULES.deviceReading[screen + 'ProbeChecked'] || null;", "out.rows = await ctx.page.evaluate(() => document.querySelectorAll('[data-row]').length);\n  const reading = RULES.deviceReading[screen + 'ProbeChecked'] || null;");
  const pm2 = psrc.replace("return Object.assign({ screen, mode: 'r1'", "await L.press(ctx, 'Tab');\n  return Object.assign({ screen, mode: 'r1'");
  plant('H4 probe 대조군: v2(variantOnly) 줄 읽기 · e1t3(r1) 키 입력 심음 → 정적 검사 빨강', pm1 !== psrc && pm2 !== psrc && checkProbe(pm1).includes('contrastVariantOnly 줄 읽기 · 입력') && checkProbe(pm2).includes('contrastR1 허용 밖'));
  // 보탬 G-3 ① — I-7 below-list 장치 실패가 row-cell 대조 칸을 덮지 않음
  const R7 = cp(); R7.plan.items = ['I-7']; R7.plan.iDelays = [0]; R7.hqaPending.samplesPerCell.value = { default: 1, 'N-1': 1 };
  const mk7 = (drop) => {
    const xs = J.buildPlan(R7).cells.filter((c) => !(drop && c.case === 'below-list' && c.screen === 'before')).map((c) => ({ item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, cell: c.key, sampleNo: 1, valid: true, errors: [], deviceReading: null,
      readings: { rowclickDelta: 0, changedOtherRows: [] }, circuit: { dead: false, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false } }));
    const rank = (sc) => R7.plan.screenOrder.indexOf(sc);
    xs.sort((a, b) => rank(a.screen) - rank(b.screen)).forEach((s, i) => { s.order = i + 1; s.contextId = i + 1; });
    return J.judge(R7, k1Entry(xs), {});
  };
  const a7 = mk7(false);
  const b7 = mk7(true);
  check('G-3 ① 대조군: 옛 묶음 판독(contrastState) = below-list 실패에 먹힘(가름 전 꼴 · 옛 칸 무변)', a7.items['I-7'].contrastState.state === F5.contrastInvalid && b7.items['I-7'].contrastState.state === null);
  plant('G-3 ①: below-list /before 장치 실패 → contrastByCase row-cell = 대조 무효 그대로 · below-list = 대조 불가(null)', b7.items['I-7'].contrastByCase['row-cell'].state === F5.contrastInvalid && b7.items['I-7'].contrastByCase['below-list'].state === null && a7.items['I-7'].contrastByCase['below-list'].state === F5.contrastInvalid, b7.items['I-7'].contrastByCase);
  // 보탬 G-3 ② — 노9 지연 칸
  const no9 = (r) => J.exposureOf(r, { item: 'N-1', screen: 'a2', delay: 300, case: 'arrows' }).length + J.exposureOf(r, { item: 'N-1', screen: 'a2', delay: 500, case: 'arrows' }).length;
  const r9 = cp(); r9.exposure = r9.exposure.map((e) => (e.no === '노9' ? Object.assign({}, rules.exposure_옛행_H4[0].row) : e));
  check('G-3 ②: 노9 지연 0 → a2 300 · 500 칸 노출 표지 0', no9(rules) === 0);
  plant('G-3 ② 대조군: 옛 노9 행(지연 칸 없음) → 두 칸 모두 표지', no9(r9) === 2);
  // 보탬 G-1 — 단추 조합 클래스 · 「"cls": [ … ]」 경로 한정
  const allow = M.loadAllow();
  const hx = M.hexCtx({}, 'probe.json');
  check('G-1: 조합 클래스 목록 6 · 지문 고정 · 641 목록과 겹침 0 · 22자 이상', M.MUD_CLS_GEN.length === 6 && M.MUD_CLS_GEN_SHA256 === '3c6e50b359cdbbd5655ef1cb5ce17bf598d8ddcbd9f6dad650eb6a0524a29979' && M.MUD_CLS_GEN.every((n) => n.length >= 22 && !M.MUD_CLASS_ALLOW.includes(n)), M.MUD_CLS_GEN);
  const inCls = JSON.stringify({ r1: { candidates: [{ tag: 'button', cls: ['mud-button-root', 'mud-button-text-default', 'mud-button-text-size-medium'] }] } }, null, 2);
  check('G-1: 「"cls": [ … ]」 안 조합 클래스 = 가림 0(줄바꿈 꼴 · 한 줄 꼴)', M.findSecrets(inCls, allow, hx).length === 0 && M.findSecrets(JSON.stringify(JSON.parse(inCls)), allow, hx).length === 0);
  plant('G-1 대조군: 경로 밖 같은 글자 → 가림 그대로', M.findSecrets(JSON.stringify({ note: 'mud-button-text-default', other: ['mud-button-text-size-medium'] }), allow, hx).length === 2);
  const fake = ['mud', 'Ab12', 'Cd34', 'Ef56', 'Gh78'].join('-');
  plant('G-1 대조군: cls 안 하이픈 낀 비밀 꼴 · 목록 이웃 꼴(끝 글자 더함) · 소문자 낱말 꼴 → 가림 3', M.findSecrets(JSON.stringify({ cls: ['mud-button-text-default', fake, 'mud-button-text-default' + 'x', ['mud', 'qwrtzp', 'lkjhgf', 'mnbvcx'].join('-')] }), allow, hx).length === 3);
  // 보탬 G-2 — run.json 칸 이름 21자 이하
  const bootSrc = fs.readFileSync(path.join(HERE, 'boot.sh'), 'utf8');
  const hn = M.hexCtx({}, null);
  const longKeys = (src) => Array.from(src.matchAll(/^\s+([A-Za-z_][A-Za-z0-9_]*):\s/gm)).map((m) => m[1]).filter((k) => M.findSecrets(k, allow, hn).length > 0);
  check('G-2: boot.sh 객체 칸 이름 = 가림 꼴 0 · browser_cond_status 있음 · 옛 24자 이름 0', longKeys(bootSrc).length === 0 && /\bbrowser_cond_status:/.test(bootSrc) && !bootSrc.includes('browser_' + 'condition_status'), longKeys(bootSrc));
  plant('G-2 대조군: 옛 이름으로 되돌린 사본 → 걸림 1', longKeys(bootSrc.replace('browser_cond_status:', 'browser_' + 'condition_status:')).length === 1);
}

// ── 고침6 c6(§8 T7 ~ T10 · J1-7 머리 사본) — 판독기 동작 시험(손 원자료 · 돌연변이 judge 사본) ──
{
  const J = require('./judge.js');
  const F5x = T.fix5;
  // 계획 칸 전부에 손 표본(칸마다 정해진 수) — 순서 = 지연 단 → 화면 순서 · readOf(칸, 번호) = readings · 입력 0 · kbd 없음(유효)
  // 고침7 K1: opt.tag = 옛 꼴 비켜 감(T7 · T20 만) · opt.mm = 일부러 불일치를 심은 입력(T25 ⑤ 비교 제외 표지) · opt.onXs = 입구 전 입력 받기
  const f6Run = (R, readOf, judgeMod, opt) => {
    const p = buildPlan(R);
    const rank = (sc) => R.plan.screenOrder.indexOf(sc);
    const cs = p.cells.filter((c) => !c.derivedFrom).sort((a, b) => a.delay - b.delay || rank(a.screen) - rank(b.screen));
    let order = 0;
    const xs = [];
    for (const c of cs) {
      for (let n = 1; n <= c.samples; n++) {
        order += 1;
        xs.push({ item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, cell: c.key, sampleNo: n, valid: true, errors: [], deviceReading: null, order, contextId: order, inputs: [],
          readings: readOf(c, n), circuit: { dead: !!(readOf(c, n) || {})._dead, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false }, kbd: null });
      }
    }
    if (opt && opt.onXs) opt.onXs(xs);
    if (opt && opt.tag) return k1Bypass(opt.tag, judgeMod || J, R, xs, {});
    return (judgeMod || J).judge(R, k1Entry(xs, opt), {});
  };
  // 돌연변이 judge 사본(임시 파일 · 옆 모듈은 절대 경로) — 바뀐 글자가 안 걸리면 null(시험이 죽은 것 → 빨강)
  let jSeq = 0;
  const mutantJudge = (mutate) => {
    const src = fs.readFileSync(path.join(HERE, 'judge.js'), 'utf8').split(String.fromCharCode(13)).join('');
    const out = mutate(src);
    if (out === src) return null;
    jSeq += 1;
    const p = path.join(tmp, 'judge-mut-' + jSeq + '.js');
    fs.writeFileSync(p, out.split("require('./mask.js')").join('require(' + JSON.stringify(path.join(HERE, 'mask.js')) + ')')
      .split("require('./hqa-gate.js')").join('require(' + JSON.stringify(path.join(HERE, 'hqa-gate.js')) + ')'), 'utf8');
    // 고침7 K1(C2-2): 심은 위반 사본은 사본을 싣는 이 함수 하나에서 셈으로 감쌈
    return k1Wrap(require(p), 'mutant');
  };
  const onlyI4 = (n) => { const R = JSON.parse(JSON.stringify(rules)); R.plan.items = ['I-4']; R.hqaPending.samplesPerCell.value = { default: n, 'N-1': n }; return R; };
  // J1-7 머리 사본
  {
    const R = onlyI4(1);
    const v = f6Run(R, () => ({ openCountAfterEsc: 0, focusOnSameRowBtn: true }));
    const Rc = onlyI4(1); Rc.device.i4.escWaitMs = 650;
    const vc = f6Run(Rc, () => ({ openCountAfterEsc: 0, focusOnSameRowBtn: true }));
    check('고침6 J1-7: verdict 머리 escWait = rules device.i4 사본(ms 700 · openPollMs 50 · ref · pollAfterEscMaxMs 3000 · menuZeroFailCases)',
      !!v.escWait && v.escWait.ms === 700 && v.escWait.openPollMs === 50 && v.escWait.ref === rules.device.i4.escWaitRef && v.escWait.pollAfterEscMaxMs === 3000 && JSON.stringify(v.escWait.menuZeroFailCases) === '["kbd-open"]', v.escWait);
    plant('고침6 J1-7 대조군: rules escWaitMs 를 바꾼 사본 → 머리 사본도 따라 바뀜(상수 아님)', !!vc.escWait && vc.escWait.ms === 650);
  }
  // T7(결-8 대안 · c4 없음): kbd-open 칸 9 · 경우 2 · 계획 120 칸 · 396 표본 · Σ 칸 = totals · 여는 키 칸 0
  {
    const p = buildPlan(rules);
    const kb = p.cells.filter((c) => c.item === 'I-4' && c.case === 'kbd-open');
    const RS = JSON.parse(JSON.stringify(rules));
    const v = f6Run(RS, (c) => (c.item === 'I-8' ? { blankRows: [] } : {}));
    const t7 = (r) => { const q = buildPlan(r); return q.cells.length === 120 && q.cells.reduce((n, c) => n + c.samples, 0) === 396 && JSON.stringify(r.plan.itemSpecs['I-4'].cases) === '["mouse-open","kbd-open"]' && q.cells.filter((c) => c.item === 'I-4' && c.case === 'kbd-open').length === 9; };
    check('고침6 T7(결-8 대안 · Enter 만): 계획 120 칸 · 396 표본 · I-4 경우 2 · kbd-open 칸 9 · 여는 키 칸 0 · caseKeys 없음 · verdict Σ 칸 = totals',
      t7(rules) && kb.every((c) => c.openKey === undefined) && rules.plan.itemSpecs['I-4'].caseKeys === undefined && v.totals.cells === 120 && v.totals.plannedSamples === 396 && v.devicePlan.cells === v.cells.length,
      { cells: p.cells.length, kb: kb.length, totals: v.totals.cells });
    const r5 = JSON.parse(JSON.stringify(rules)); r5.plan.itemSpecs['I-4'].cases = ['mouse-open', 'kbd-open', 'space-0', 'space-150', 'space-300'];
    plant('고침6 T7 심은 위반: 여는 키를 경우로 셈(I-4 경우 5) → 빨강(칸 · 경우 수 어긋남)', !t7(r5));
  }
  // T8 사유 글자(X2) — 대조 화면 이름 · /before 칸 = 옛 글자
  {
    const t8 = (R) => {
      const v4 = f6Run(Object.assign(JSON.parse(JSON.stringify(R)), { plan: Object.assign(JSON.parse(JSON.stringify(R.plan)), { items: ['I-4'] }) }), () => ({ openCountAfterEsc: 0, focusOnSameRowBtn: true }));
      const R8 = JSON.parse(JSON.stringify(R)); R8.plan.items = ['I-8'];
      const okRows = ['blank', 'empty', 'null'].map((k, i) => ({ row: String(i + 3), kind: k, buttons: 1, visibleButtons: 1, maxButtonArea: 100, hidden: 0, tabStops: 1, chip: '-' }));
      const v8 = f6Run(R8, () => ({ blankRows: okRows, rowsRead: 3 }));
      const RN = JSON.parse(JSON.stringify(R)); RN.plan.items = ['N-1'];
      const vN = f6Run(RN, () => ({ opened: true }));
      return { e1t3: v4.items['I-4'].contrastByCase['kbd-open'], mouse: v4.items['I-4'].contrastByCase['mouse-open'], v2: v8.items['I-8'].contrastState, a2: vN.items['N-1'].contrastState };
    };
    const R1 = JSON.parse(JSON.stringify(rules)); R1.hqaPending.samplesPerCell.value = { default: 1, 'N-1': 1 };
    const g = t8(R1);
    const good = (x) => x.e1t3.state === F5x.contrastInvalid && /e1t3/.test(x.e1t3.reason) && !x.e1t3.reason.includes('/before')
      && x.v2.state === F5x.contrastInvalid && /v2/.test(x.v2.reason) && !x.v2.reason.includes('/before')
      && x.a2.state === F5x.contrastInvalid && /A2/.test(x.a2.reason) && !x.a2.reason.includes('/before')
      && x.mouse.state === F5x.contrastInvalid && x.mouse.reason === F5x.contrastNoRepro && x.mouse.reason.includes('/before');
    check('고침6 T8: 대조 무효 사유 — I-4 kbd-open = 「비교 화면 e1t3 …」 · I-8 = 「비교 화면 v2 …」 · N-1 = 「A2 화면 …」(/before 글자 0) · mouse-open(/before) = 옛 글자 그대로 · 옛 키 보존',
      good(g) && F5x.contrastNoRepro === '/before 가 같은 조건에서 실패를 재현 못 함(정본 A L21) · 통과로 안 셈' && typeof F5x.contrastNoReproScreen === 'string' && typeof F5x.contrastNoStimScreen === 'string' && F5x.contrastNoReproScreen.split('{screen}').join('/before') === F5x.contrastNoRepro && F5x.contrastNoStimScreen.split('{screen}').join('/before') === F5x.contrastNoStimulus,
      { e1t3: g.e1t3.reason, v2: g.v2.reason, a2: g.a2.reason, mouse: g.mouse.reason });
    const R0 = JSON.parse(JSON.stringify(R1)); delete R0.readingText.fix5.contrastNoReproScreen; delete R0.readingText.fix5.contrastNoStimScreen;
    const g0 = t8(R0);
    plant('고침6 T8 심은 위반: 사유를 /before 고정 글자로 되돌림(새 키 뺀 rules 사본 = 고침5 꼴) → e1t3 · v2 · A2 사유에 「/before」 → 빨강', !good(g0) && g0.e1t3.reason.includes('/before') && g0.v2.reason.includes('/before'), { e1t3: g0.e1t3.reason });
  }
  // T9 한 칸만 재현(X3) — e1t3 방향당 0 · 150 · 300 칸마다 표본 하나만 실패 꼴 → kbd-open 경우 성립 · 세 칸 0 → 대조 무효
  {
    const R = onlyI4(3);
    const runOne = (jm, d) => f6Run(R, (c, n) => ({ openCountAfterEsc: c.screen === 'before' ? 1 : 0, focusOnSameRowBtn: !(d !== null && c.case === 'kbd-open' && c.screen === 'e1t3' && c.delay === d && n === 2) }), jm).items['I-4'].contrastByCase['kbd-open'];
    const one = [0, 150, 300].map((d) => runOne(null, d).state);
    const zero = runOne(null, null);
    check('고침6 T9: e1t3 kbd-open 칸마다(0 · 150 · 300) 표본 하나만 실패 꼴 → 경우 성립(「하나라도」) · 세 칸 0 → 대조 무효',
      one.every((s) => s === F5x.contrastHolds) && zero.state === F5x.contrastInvalid, { one, zero: zero.state });
    const mj = mutantJudge((s) => s.replace('      if (rep > 0) state = F5.contrastHolds;', '      if (rows.length > 0 && rows.every((r) => r.reproduced > 0)) state = F5.contrastHolds;'));
    const oneM = mj ? [0, 150, 300].map((d) => runOne(mj, d).state) : null;
    plant('고침6 T9 심은 위반(칸별 셋): 집계를 「모든 칸」 으로 바꾼 judge 사본 → 0 · 150 · 300 칸 하나씩 재현 모두 성립 아님 → 빨강', !!oneM && oneM.every((s) => s !== F5x.contrastHolds), oneM);
  }
  // T10 노출 표지 — exposure 넣고 뺀 두 벌 verdict = 표지 밖 바이트 같음 · e1t3 kbd-open 세 칸 표지
  {
    const R = onlyI4(3);
    const Rn = JSON.parse(JSON.stringify(R)); Rn.exposure = [];
    const rd = (c, n) => ({ openCountAfterEsc: c.screen === 'before' ? 1 : 0, focusOnSameRowBtn: !(c.case === 'kbd-open' && c.screen === 'e1t3' && c.delay === 300 && n === 1) });
    const strip = (v) => JSON.parse(JSON.stringify(v), (k, x) => ((k === 'exposed' || k === 'independenceNote' || k === 'exposedCells') ? undefined : x));
    const va = f6Run(R, rd);
    const vb = f6Run(Rn, rd);
    // J1-B ①: 노12 출처 글자 정정(「§2 A」 → 「§2 묶음 A(지금 판)」 · 옛 글자 = rules _source_옛값_J1B)에 맞춤 — 단언 뜻(세 칸에 노12 표지) 무변
    // ⬛ 옛 줄: c.exposed.source.some((s) => s.startsWith('11/8 근본원인 §2 A'))
    const ex = va.cells.filter((c) => c.item === 'I-4' && c.case === 'kbd-open' && c.screen === 'e1t3' && c.exposed && c.exposed.source.some((s) => s.startsWith('11/8 근본원인 §2 묶음 A(지금 판'))).map((c) => c.delay);
    check('고침6 T10: 노출 표 넣은 · 뺀 두 verdict = 표지 밖 같음 · e1t3 kbd-open 0 · 150 · 300 세 칸에 노12 표지 · I-4 독립성 글자',
      JSON.stringify(strip(va)) === JSON.stringify(strip(vb)) && JSON.stringify(ex) === '[0,150,300]' && va.items['I-4'].independenceNote === F5x.independenceNote && va.items['I-4'].contrastByCase['kbd-open'].state === F5x.contrastHolds, { ex });
    const mj = mutantJudge((s) => s.replace('    const ccs = cs.filter(isCc);', '    const ccs = cs.filter(isCc).filter((c) => exposureOf(rules, c).length === 0);'));
    const vm = mj ? f6Run(R, rd, mj) : null;
    const vmn = mj ? f6Run(Rn, rd, mj) : null;
    plant('고침6 T10 심은 위반: 노출 칸을 대조에서 빼는 judge 사본(표지가 판정값을 바꿈) → 두 벌 다름 → 빨강', !!vm && JSON.stringify(strip(vm)) !== JSON.stringify(strip(vmn)));
  }
  // J1-B(반려 [3-V] 2-b) 판독기 — 머리 escWait 없음 · 값 깨짐 = 장치 빨강 / I-4/kbd-open 초점 판독값 참거짓 아님 = 표본 device_failure
  {
    const DRx = rules.deviceReading;
    const rdOk = (c) => ({ openCountAfterEsc: c.screen === 'before' ? 1 : 0, focusOnSameRowBtn: true });
    const hasEw = (v) => v.redReasons.includes(DRx.i4EscWaitMissing);
    const v0 = f6Run(onlyI4(1), rdOk);
    const HB = [
      ['escWaitMs 없음(머리 null)', (r) => { delete r.device.i4.escWaitMs; }, (v) => v.escWait === null],
      ['pollAfterEscMaxMs 없음', (r) => { delete r.device.i4.pollAfterEscMaxMs; }, (v) => !!v.escWait && v.escWait.pollAfterEscMaxMs === null],
      ['openPollMs 0', (r) => { r.device.i4.openPollMs = 0; }, (v) => !!v.escWait],
      ['menuZeroFailCases 글자', (r) => { r.device.i4.menuZeroFailCases = 'kbd-open'; }, (v) => !!v.escWait && v.escWait.menuZeroFailCases === null]
    ];
    const hbRun = (jm) => HB.map(([name, f, shape]) => { const R = onlyI4(1); f(R); const v = f6Run(R, rdOk, jm); return { name, ok: v.red === true && hasEw(v) && shape(v), red: v.red }; });
    const hb = hbRun();
    const Rn = JSON.parse(JSON.stringify(rules)); Rn.plan.items = ['I-1']; Rn.hqaPending.samplesPerCell.value = { default: 1, 'N-1': 1 }; delete Rn.device.i4.escWaitMs;
    const vn = f6Run(Rn, () => ({}));
    check('J1-B 2-b 판독기: I-4 칸 계획 + 머리 escWait 없음 · 값 깨짐(4꼴) → 장치 빨강(사유 「I-4 칸 계획 있음 · verdict 머리 escWait 없음 …」) · 대조군 = 온전한 rules 엔 그 사유 0 · I-4 칸 없는 계획엔 그 사유 0',
      hb.every((x) => x.ok) && !hasEw(v0) && !!v0.escWait && !hasEw(vn), { hb: hb.filter((x) => !x.ok), v0: v0.redReasons.slice(0, 3) });
    const mjE = mutantJudge((s) => s.replace("  if (plan.cells.some((c) => c.item === 'I-4') && escWaitHeadBad(verdict.escWait)) {", '  if (false) {'));
    const hbm = mjE ? hbRun(mjE) : null;
    plant('J1-B 2-b 판독기 심은 위반: 머리 escWait 검사 뺀 judge 사본 → 4꼴 모두 그 사유 없음 → 빨강', !!hbm && hbm.every((x) => !x.ok), hbm && hbm.map((x) => x.red));
    // focusOnSameRowBtn 참/거짓 아님 — /after kbd-open 300 칸 표본 하나에 null · 빠짐 · 글자 「false」 · 숫자 0
    const target = (c) => c.item === 'I-4' && c.case === 'kbd-open' && c.screen === 'after' && c.delay === 300;
    const BADF = [['null', null], ['빠짐', undefined], ['글자 false', 'false'], ['숫자 0', 0]];
    const fnbRun = (jm) => BADF.map(([name, val]) => {
      const v = f6Run(onlyI4(1), (c) => {
        if (target(c)) { const r = { openCountAfterEsc: 0 }; if (val !== undefined) r.focusOnSameRowBtn = val; return r; }
        if (c.case === 'mouse-open' && c.screen === 'after') return { openCountAfterEsc: 1, focusOnSameRowBtn: null }; // mouse-open 메뉴 0 못 봄 = 유효(구현 판단 · 무변)
        return rdOk(c);
      }, jm);
      const cell = v.cells.find((c) => target(c));
      const mo = v.cells.filter((c) => c.item === 'I-4' && c.case === 'mouse-open' && c.screen === 'after');
      return { name, ok: v.red === true && cell.valid === 0 && cell.excluded.deviceFailure === 1 && v.redReasons.includes(`${DRx.i4FocusNotBool} 1건`), moValid: mo.every((c) => c.valid === 1), valid: cell.valid };
    });
    const fb = fnbRun();
    const c0 = v0.cells.find((c) => target(c));
    check('J1-B 2-b 판독기: I-4/kbd-open 표본 focusOnSameRowBtn 이 참/거짓 아님(null · 빠짐 · 「false」 · 0) → 그 표본 device_failure(칸 유효 0 · 제외 1 · 빨강 사유) · mouse-open null 은 유효 그대로 · 대조군 참 = 유효',
      fb.every((x) => x.ok && x.moValid) && c0.valid === 1 && !v0.redReasons.some((r) => r.startsWith(DRx.i4FocusNotBool)), { fb: fb.filter((x) => !x.ok || !x.moValid) });
    const mjF = mutantJudge((s) => s.replace("      && typeof (s.readings || {}).focusOnSameRowBtn !== 'boolean') {", '      && false) {'));
    const fbm = mjF ? fnbRun(mjF) : null;
    plant('J1-B 2-b 판독기 심은 위반: 참거짓 검사 뺀 judge 사본 → 4꼴 모두 유효로 셈(통과로 샘) → 빨강', !!fbm && fbm.every((x) => !x.ok && x.valid === 1), fbm && fbm.map((x) => x.valid));
  }
  // ── 고침7 c1(H11 · 작업지시서 20261117작 §8 T1~T12 · T19~T25) — 두 식 불일치 · 비교 불가 · 같은 줄 셈 · 재현 셈 · G4 (대조군 + 심은 위반) ──
  {
    const F7 = T.fix5;
    const R1 = onlyI4(1);
    const R3 = onlyI4(3);
    const at = (kase, sc, d) => (c, n) => c.case === kase && c.screen === sc && c.delay === d && n === 1;
    const edit = (rd, pred, fx) => (c, n) => { const o = rd(c, n); if (pred(c, n)) fx(o); return o; };
    // 바탕: 마우스 = before 0 하나만 열림 남음(성립) · 키보드 = e1t3 300 하나만 초점 실패(성립 · 그 칸 유일 재현)
    const rdB = (c, n) => ({ openCountAfterEsc: c.case === 'mouse-open' && c.screen === 'before' && c.delay === 0 && n === 1 ? 1 : 0, focusOnSameRowBtn: !(c.case === 'kbd-open' && c.screen === 'e1t3' && c.delay === 300 && n === 1) });
    const E300 = at('kbd-open', 'e1t3', 300);
    const KA0 = at('kbd-open', 'after', 0);
    const MB0 = at('mouse-open', 'before', 0);
    const kb = (v) => v.items['I-4'].contrastByCase['kbd-open'];
    const ms = (v) => v.items['I-4'].contrastByCase['mouse-open'];
    const em = (v) => v.items['I-4'].exprMismatch;
    const fac = (v, kase, sc) => v.items['I-4'].focusAfterClose.find((r) => r.case === kase && r.screen === sc);
    const repSum = (x) => x.cells.reduce((n, r) => n + r.reproduced, 0);
    const vB = f6Run(R1, rdB);
    check('고침7 바탕 대조군: 두 식 같음(입구 덧붙임) → 두 경우 성립 · 불일치 0 · 비교 불가 0 · 상태 바뀜 거짓 · 뺀 수 0',
      kb(vB).state === F7.contrastHolds && ms(vB).state === F7.contrastHolds && em(vB).count === 0 && em(vB).notComparable.count === 0
      && kb(vB).exprMismatchChanged === false && kb(vB).exprMismatchRemoved === 0 && vB.items['I-4'].contrastState.exprMismatchChanged === false, { kb: kb(vB).state, em: em(vB).classes });
    // T1 · T2
    const rd1 = edit(rdB, E300, (o) => { o.hqaFocusRow1Restored = true; o.menuZeroObserved = true; });
    const rd2 = edit(rd1, KA0, (o) => { o.hqaFocusRow1Restored = false; o.menuZeroObserved = true; });
    const v1 = f6Run(R1, rd1, null, { mm: true });
    const v2 = f6Run(R1, rd2, null, { mm: true });
    const t1 = (v) => repSum(kb(v)) === 0 && kb(v).state === F7.contrastInvalid && kb(v).exprMismatchChanged === true && kb(v).exprMismatchRemoved === 1
      && em(v).count >= 1 && em(v).rows.some((r) => JSON.stringify(r) === JSON.stringify({ sampleNo: 1, case: 'kbd-open', screen: 'e1t3', delay: 300, device: false, hqa: true }));
    const t2 = (v) => fac(v, 'kbd-open', 'after').onSameRowBtn === fac(vB, 'kbd-open', 'after').onSameRowBtn - 1 && fac(v, 'kbd-open', 'after').valid === fac(vB, 'kbd-open', 'after').valid
      && fac(v, 'kbd-open', 'after').exprMismatchExcluded === 1 && em(v).count === 2 && JSON.stringify(em(v).samples) === '[1,1]';
    check('고침7 T1: 장치 거짓 · HQA 참(키보드 e1t3 300 · 그 칸 유일 재현) → 재현 셈 0 · 키보드 경우 대조 무효 · 상태 바뀜 참 · 뺀 수 1 · 불일치 칸에 표본 번호 · 경우 · 화면 · 지연 · 두 값', t1(v1), { kb: kb(v1), em: em(v1) });
    check('고침7 T2: 장치 참 · HQA 거짓(키보드 after 0) → 같은 줄 셈 on −1 · valid 무변 · 그 줄 뺀 수 1 · T1 과 합쳐 불일치 2', t2(v2), { f: fac(v2, 'kbd-open', 'after'), em: em(v2).count });
    // T3 일치 대조군 — 같은 칸 · 두 식 같음 → 고침 전 판독기(같은 입력) − 새 칸 = 통째 같음
    const rd3 = edit(edit(rdB, E300, (o) => { o.hqaFocusRow1Restored = false; o.menuZeroObserved = true; }), KA0, (o) => { o.hqaFocusRow1Restored = true; o.menuZeroObserved = true; });
    let x3 = null;
    const v3 = f6Run(R1, rd3, null, { onXs: (xs) => { x3 = structuredClone(xs); } });
    const P3 = k1PreJudge();
    const p3 = P3 ? P3.judge(R1, x3, {}) : null;
    check('고침7 T3: 일치 대조군(같은 칸 · 두 식 같음) → 불일치 0 · 고침 전 판독기와 새 칸 밖 통째 같음(재현 · 같은 줄 셈 · 대조 상태)',
      !!p3 && em(v3).count === 0 && JSON.stringify(k1Strip(v3)) === JSON.stringify(p3) && kb(v3).state === F7.contrastHolds, { pre: K1.pre });
    // T4 심은 위반: 거름 줄 항등
    const mj4 = mutantJudge((s) => s.replace("  const xExcl = (s) => xCls.get(s) === 'notComparable' || xCls.get(s) === 'mismatch';", '  const xExcl = (s) => false;'));
    const v4m = mj4 ? f6Run(R1, rd2, mj4, { mm: true }) : null;
    plant('고침7 T4 심은 위반: 불일치 거름을 항등으로 바꾼 판독기 사본 → T1 · T2 기대 깨짐 → 빨강', !!v4m && !t1(v4m) && !t2(v4m), v4m && kb(v4m).state);
    // T5 · T6
    const v5 = f6Run(R1, edit(rdB, E300, (o) => { o.hqaFocusRow1Restored = null; o.menuZeroObserved = true; }), null, { mm: true });
    const v6 = f6Run(R1, edit(rdB, E300, (o) => { o.menuZeroObserved = true; }), null, { mm: true });
    check('고침7 T5: 장치 거짓 · HQA null · menuZeroObserved 참 → 불일치(셈에서 뺌 · 값 null)', em(v5).count === 1 && em(v5).rows[0].hqa === null && kb(v5).state === F7.contrastInvalid && em(v5).notComparable.count === 0, em(v5).rows);
    check('고침7 T6: menuZeroObserved 있음 · HQA 칸 빠짐 → 불일치(셈에서 뺌 · 값 「빠짐」)', em(v6).count === 1 && em(v6).rows[0].hqa === '빠짐' && kb(v6).state === F7.contrastInvalid && em(v6).notComparable.count === 0, em(v6).rows);
    // T7 옛 꼴(두 칸 다 없음 · 입구 비켜 감) — 키보드 e1t3 300 1건 + 마우스 before 0 1건(열림 남음 · 장치 거짓)
    const rd7v = edit(rdB, MB0, (o) => { o.focusOnSameRowBtn = false; });
    const rd7 = (c, n) => { const o = rd7v(c, n); if (!E300(c, n) && !MB0(c, n)) { o.hqaFocusRow1Restored = o.focusOnSameRowBtn; o.menuZeroObserved = true; } return o; };
    const v7c = f6Run(R1, rd7v);
    const t7 = (v) => {
      const nc = em(v).notComparable;
      const keys = nc.rows.map((r) => [r.case, r.screen, r.delay, r.sampleNo].join('|')).sort();
      const facX = v.items['I-4'].focusAfterClose.reduce((n, r) => n + r.exprMismatchExcluded, 0);
      return nc.count === 2 && JSON.stringify(nc.samples) === '[1,1]' && JSON.stringify(keys) === JSON.stringify(['kbd-open|e1t3|300|1', 'mouse-open|before|0|1']) && em(v).count === 0
        && repSum(kb(v)) === 0 && kb(v).state === F7.contrastInvalid && kb(v).exprMismatchChanged === true
        && repSum(ms(v)) === repSum(ms(v7c)) && ms(v).state === F7.contrastHolds
        && fac(v, 'mouse-open', 'before').exprMismatchExcluded === 1 && fac(v, 'mouse-open', 'before').offSameRowBtn === fac(v7c, 'mouse-open', 'before').offSameRowBtn - 1 && facX === 1
        && v.items['I-4'].contrastState.exprMismatchChanged === true && v.items['I-4'].contrastState.exprMismatchRemoved >= 1
        && JSON.stringify(v.cells.map((c) => c.valid)) === JSON.stringify(v7c.cells.map((c) => c.valid)) && JSON.stringify(v.redReasons) === JSON.stringify(v7c.redReasons);
    };
    const v7 = f6Run(R1, rd7, null, { tag: 'T7' });
    check('고침7 T7(CTO 확인 기대): 옛 꼴 2건 → 비교 불가 2 · 표본 번호 · 키보드 재현 0 → 대조 무효 · 상태 바뀜 참 · 마우스 열림 재현 셈 무변(성립) · 같은 줄 셈에서 뺌(마우스 before 1 · e1t3 은 같은 줄 셈 칸 밖) · 항목 뺀 수 ≥ 1 · valid · 빨강 사유 = 대조군',
      t7(v7) && ms(v7c).state === F7.contrastHolds && kb(v7c).state === F7.contrastHolds, { nc: em(v7).notComparable, kb: kb(v7).state, ms: ms(v7).state });
    // T8 · T9 마우스
    const rd8 = edit(rdB, MB0, (o) => { o.focusOnSameRowBtn = false; o.hqaFocusRow1Restored = true; o.menuZeroObserved = true; });
    const t8 = (v) => ms(v).state === F7.contrastHolds && repSum(ms(v)) === repSum(ms(vB)) && fac(v, 'mouse-open', 'before').exprMismatchExcluded === 1 && em(v).count === 1 && ms(v).exprMismatchRemoved === 0;
    const v8 = f6Run(R1, rd8, null, { mm: true });
    check('고침7 T8: 마우스 before 0 장치 거짓 · HQA 참 · 열림 남음 → 열림 재현 셈 무변(성립) · 같은 줄 셈에서 뺌 · 불일치 1', t8(v8), { ms: ms(v8), f: fac(v8, 'mouse-open', 'before') });
    const mj9 = mutantJudge((s) => s.replace("      const focusShape = !!fn && fn === BEFORE_FAIL['I-4/kbd-open'];", '      const focusShape = !!fn;'));
    const v9m = mj9 ? f6Run(R1, rd8, mj9, { mm: true }) : null;
    plant('고침7 T9 심은 위반: 마우스 열림 재현 셈에도 거름을 건 사본 → T8 깨짐 → 빨강', !!v9m && !t8(v9m), v9m && ms(v9m).state);
    // T10 ~ T12 G4 경계
    const vG = f6Run(R3, rdB);
    const K3 = (c, n) => c.case === 'kbd-open' && c.screen === 'after' && c.delay === 0 && n === 2;
    const rd10 = edit(rdB, K3, (o) => { o.hqaFocusRow1Restored = false; o.menuZeroObserved = true; });
    const cKA = (v) => v.cells.find((c) => c.item === 'I-4' && c.case === 'kbd-open' && c.screen === 'after' && c.delay === 0);
    const t10 = (v) => cKA(v).valid === 3 && cKA(v).status === '표본 있음' && JSON.stringify(v.redReasons) === JSON.stringify(vG.redReasons) && em(v).count === 1;
    const v10 = f6Run(R3, rd10, null, { mm: true });
    check('고침7 T10(G4 경계): 칸당 3표본 중 불일치 1 → 칸 valid 3/3 · 표본 있음 · 빨강 사유 = 대조군', t10(v10), { c: cKA(v10).status, red: v10.redReasons });
    const rd11 = (c, n) => { const o = rdB(c, n); if (c.case === 'kbd-open' && c.screen === 'e1t3' && c.delay === 300) { o.focusOnSameRowBtn = false; o.hqaFocusRow1Restored = true; o.menuZeroObserved = true; } return o; };
    const v11 = f6Run(R3, rd11, null, { mm: true });
    const c11 = v11.cells.find((c) => c.item === 'I-4' && c.case === 'kbd-open' && c.screen === 'e1t3' && c.delay === 300);
    check('고침7 T11(G4): 3/3 불일치 → 칸 장치 실패 0 · 키보드 상태 = 뺀 뒤 값(대조 무효) · 상태 바뀜 참 · 뺀 수 3',
      c11.valid === 3 && c11.status === '표본 있음' && !v11.items['I-4'].deviceFailure.flag && kb(v11).state === F7.contrastInvalid && kb(v11).exprMismatchChanged === true && kb(v11).exprMismatchRemoved === 3 && em(v11).count === 3, { c: c11.status, kb: kb(v11).state });
    const mj12 = mutantJudge((s) => s.replace('  const isValid = (s) => !!(eff.get(s) && eff.get(s).valid);', '  const isValid = (s) => !!(eff.get(s) && eff.get(s).valid) && !xExcl(s);'));
    const v12m = mj12 ? f6Run(R3, rd10, mj12, { mm: true }) : null;
    plant('고침7 T12 심은 위반: 불일치 표본을 유효에서 빼는 사본(결-1 (A)) → T10 칸 실패 → 빨강', !!v12m && !t10(v12m) && cKA(v12m).status !== '표본 있음', v12m && cKA(v12m).status);
    // T20 심은 위반: 옛 꼴을 셈에 넣는 사본(비교 불가 → 일치)
    const mj20 = mutantJudge((s) => s.replace("  if (!has('hqaFocusRow1Restored') && !has('menuZeroObserved')) return 'notComparable';", "  if (!has('hqaFocusRow1Restored') && !has('menuZeroObserved')) return 'match';"));
    const v20m = mj20 ? f6Run(R1, rd7, mj20, { tag: 'T20' }) : null;
    plant('고침7 T20 심은 위반: 옛 꼴(두 칸 없음)을 일치로 보내는 사본 → T7 기대 깨짐 → 빨강', !!v20m && !t7(v20m), v20m && em(v20m).notComparable.count);
    // T22 · T24 심은 위반 — T21 · T23 판정 함수가 사본 판정에서 잡는가
    const mj22 = mutantJudge((s) => s.replace('        exprMismatchExcluded: v.length - vx.length', '        exprMismatchExcluded: 0'));
    const v22m = mj22 ? f6Run(R1, rd2, mj22, { mm: true }) : null;
    plant('고침7 T22 심은 위반: 뺀 수 칸을 늘 0 으로 쓰는 사본 → 칸마다 등식(T21) 빨강', !!v22m && k1Problems(null, v22m).t21.length > 0 && k1Problems(null, v2).t21.length === 0);
    const mj24 = mutantJudge((s) => s.replace('function exprPair(changed, removed) { return { exprMismatchChanged: changed === true, exprMismatchRemoved: removed }; }',
      'function exprPair(changed, removed) { return removed > 0 ? { exprMismatchRemoved: removed } : { exprMismatchChanged: changed === true, exprMismatchRemoved: removed }; }'));
    const v24m = mj24 ? f6Run(R1, rd1, mj24, { mm: true }) : null;
    plant('고침7 T24 심은 위반: 뺀 수 > 0 일 때 상태 바뀜 칸을 안 쓰는 사본 → 함께 나옴(T23) 빨강', !!v24m && k1Problems(null, v24m).t23.length > 0 && k1Problems(null, v1).t23.length === 0);
    // T25 ③ 원 입력 무변 — 지은 뒤 장치 값을 바꿔도 다음 입구가 옛 복사값을 물려받지 않음 · 규칙 ② ⑤ 꼴
    const xs = [{ item: 'I-4', readings: { focusOnSameRowBtn: true } }, { item: 'I-4', readings: { focusOnSameRowBtn: false, menuZeroObserved: true } }, { item: 'I-4', readings: null },
      { item: 'I-4', readings: { openCountAfterEsc: 0 } }, { item: 'N-1', readings: { focusOnSameRowBtn: true } }, { item: 'I-4', readings: { focusOnSameRowBtn: null } }, { item: 'I-4' }];
    const e1 = k1Entry(xs);
    xs[0].readings.focusOnSameRowBtn = false;
    const e2 = k1Entry(xs);
    check('고침7 T25 ③ · 규칙 ①②⑤: 입구 사본만 덧붙임(HQA = 그 순간 장치 값 · 메뉴 0 = 참거짓이면 참) · 원 입력에 두 칸 0 · 바뀐 장치 값을 다음 입구가 그대로 복사 · 한 칸 있음 · readings 없음 · 장치 칸 없음 · I-4 밖 = 같은 객체',
      e1[0].readings.hqaFocusRow1Restored === true && e1[0].readings.menuZeroObserved === true && !k1Own(xs[0].readings, 'hqaFocusRow1Restored') && e2[0].readings.hqaFocusRow1Restored === false
      && e1[1] === xs[1] && e1[2] === xs[2] && e1[3] === xs[3] && e1[4] === xs[4] && e1[6] === xs[6] && e1[5].readings.hqaFocusRow1Restored === null && e1[5].readings.menuZeroObserved === false);
  }
}

// ── 고침6 J1(작업지시서 20261116작 S4관문1 장치고침6 §8) — I-4 HQA 절차 동작 시험 ──
//   가짜 L: _lib 모듈 칸(nowMs · wait · readState · press · …)에 가짜 시계 · 읽기 순서를 끼우고 i4 를 실제로 돌린다(브라우저 · 망 0).
//   page = null 이라 가짜가 덮지 않은 _lib 함수가 page 를 부르면 바로 예외 → 가짜가 덮은 길만 돈다.
//   심은 위반 = i-items.js 원문을 바꾼 돌연변이 사본(임시 파일) · rules 사본 — 걸려야(빨강이어야) ok.
const F6 = { LIB: path.join(HERE, 'items', '_lib.js'), I: path.join(HERE, 'items', 'i-items.js'), seq: 0, T0: 1000, READ_COST: 3 };
function mutantI4(mutate) {
  const src = fs.readFileSync(F6.I, 'utf8').split(String.fromCharCode(13)).join(''); // 줄 끝 LF 로 맞춤(돌연변이 글자가 줄 끝에 안 걸리게)
  const out = mutate(src);
  if (out === src) return null; // 돌연변이가 안 걸림 = 시험이 죽은 것 → 부르는 쪽이 빨강
  F6.seq += 1;
  const p = path.join(tmp, 'i-items-mut-' + F6.seq + '.js');
  fs.writeFileSync(p, out.split("require('./_lib.js')").join('require(' + JSON.stringify(F6.LIB) + ')'), 'utf8');
  return require(p)['I-4'];
}
// 가짜 세계: 여는 키(Enter · Space 뗌 · 칩 누름) 뒤 openLatMs 에 목록 열림 · Esc 뒤 closeLatMs 에 메뉴 0 · 메뉴 0 뒤 restoreAfterZeroMs 에 초점 복원
//   null = 그 일이 안 일어남. hqaSame=false 면 HQA 식만 거짓(두 식이 갈리는 꼴).
function fakeWorld(opt) {
  // J1-B: selfCloseAfterOpenMs = 열린 뒤 그 ms 에 Esc 없이 저절로 닫힘 · reopenAfterMs = 닫힌 뒤 그 ms 에 다시 열림(null = 안 열림) — 기본 null(옛 세계 그대로)
  //   읽기 상한(maxReads) — 규칙 값이 깨진 돌연변이가 끝없이 돌지 않게(넘으면 예외 · 시험은 빨강으로 셈)
  const o = Object.assign({ openLatMs: 137, closeLatMs: 120, restoreAfterZeroMs: 200, hqaSame: true, selfCloseAfterOpenMs: null, reopenAfterMs: null, maxReads: 20000 }, opt || {});
  // J1-C: readLog = readState 마다 읽기 시작 시각(가짜 시계) — 「기다림 동안 readState 0 · Esc 직전 한 번」 을 동작으로 재려고(옛 칸 무변)
  const w = { t: F6.T0, openAt: null, zeroAt: null, restoreAt: null, escAt: null, keyAt: null, opens: 0, reads: 0, readLog: [] };
  const inDip = () => o.selfCloseAfterOpenMs !== null && w.openAt !== null && w.t >= w.openAt + o.selfCloseAfterOpenMs && (o.reopenAfterMs === null || w.t < w.openAt + o.selfCloseAfterOpenMs + o.reopenAfterMs);
  const isOpen = () => w.openAt !== null && w.t >= w.openAt && !(w.zeroAt !== null && w.t >= w.zeroAt) && !inDip();
  const restored = () => w.restoreAt !== null && w.t >= w.restoreAt;
  w.state = () => ({
    openCount: isOpen() ? 1 : 0,
    focus: restored() ? { tag: 'button', id: null, role: null, row: '1', inOpenList: false, isStatusActivator: true, text: null, afterRef: false, body: false, tabIndex: 0 }
      : (isOpen() ? { tag: 'div', id: null, role: 'menu', row: null, inOpenList: true, isStatusActivator: false, text: null, afterRef: true, body: false, tabIndex: -1 }
        : { tag: 'body', id: null, role: null, row: null, inOpenList: false, isStatusActivator: false, text: null, afterRef: false, body: true, tabIndex: null }),
    pickCount: 0, picks: '', rowclickCount: 0, rowclicks: '', detailOpen: null, detailRow: null, rows: {}, reconnect: { present: false, visible: false, cls: '' }
  });
  const openKey = () => { w.keyAt = w.t; w.opens += 1; if (o.openLatMs !== null) w.openAt = w.t + o.openLatMs; };
  const esc = () => {
    w.escAt = w.t;
    if (w.openAt !== null && o.closeLatMs !== null) { w.zeroAt = w.t + o.closeLatMs; if (o.restoreAfterZeroMs !== null) w.restoreAt = w.zeroAt + o.restoreAfterZeroMs; }
  };
  const rec = (ctx, k, key) => ctx.inputs.push({ atMs: w.t - ctx.sig.t0, k, key });
  w.fakes = {
    nowMs: () => w.t,
    wait: async (ctx, ms) => { w.t += ms; },
    readState: async () => { w.reads += 1; if (w.reads > o.maxReads) throw new Error('가짜 세계 읽기 상한'); w.readLog.push(w.t); w.t += F6.READ_COST; return w.state(); },
    readHqaFocusRow: async () => { w.t += F6.READ_COST; return restored() && o.hqaSame ? '1' : null; },
    press: async (ctx, key) => { rec(ctx, 'press', key); if (key === 'Enter') openKey(); if (key === 'Escape') esc(); w.t += 2; },
    down: async (ctx, key) => { rec(ctx, 'down', key); w.t += 2; },
    up: async (ctx, key) => { rec(ctx, 'up', key); if (key === 'Space') openKey(); w.t += 2; },
    locatorClick: async (ctx, sel) => { rec(ctx, 'locator.click', sel); openKey(); w.t += 2; },
    tabTo: async () => ({ reached: true, cause: null, tabs: 3, path: [] }),
    settle: async (ctx, ms) => { w.t += ms; const s = w.state(); return { state: s, maxOpen: s.openCount }; },
    circuitSince: () => ({ dead: false, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false, detail: {} })
  };
  return w;
}
async function runFakeI4(fn, w, cell, r) {
  const L = require('./items/_lib.js');
  const saved = {};
  for (const k of Object.keys(w.fakes)) { saved[k] = Object.prototype.hasOwnProperty.call(L, k) ? { v: L[k] } : null; L[k] = w.fakes[k]; }
  const ctx = { page: null, sig: { t0: F6.T0 }, rules: r || rules, cell, sampleNo: 1, outDir: tmp, inputs: [], inputAt: F6.T0 };
  let out = null;
  let err = null;
  try { out = await fn(ctx); } catch (e) { err = String(e && e.message ? e.message : e); } finally {
    for (const k of Object.keys(saved)) { if (saved[k]) L[k] = saved[k].v; else delete L[k]; }
  }
  const R4 = out && out[0] ? out[0].readings : null;
  const escs = ctx.inputs.filter((x) => x.key === 'Escape').length;
  return { out, err, R: R4, inputs: ctx.inputs, w, escs, escMinusOpen: w.escAt !== null && w.openAt !== null ? w.escAt - w.openAt : null };
}
const f6Cell = (kase, screen, delay, openKey) => Object.assign({ item: 'I-4', case: kase, screen, delay, window: win, key: 'f6' }, openKey ? { openKey } : {});
async function fix6Checks() {
  const ALL = Object.assign({}, require('./items/i-items.js'), require('./items/n-items.js'));
  const i4 = ALL['I-4'];
  const d4 = rules.device.i4;
  const DR = rules.deviceReading;
  // T2 기다림 — 기준점 = 열린 메뉴 관측 · Esc − 관측 ∈ [700, 700 + 읽기 1회] · Esc − 실제 열림 ∈ [700, 700 + 관측 간격 + 읽기 1회]
  const inT2 = (x) => !!x && !x.err && x.escs === 1 && x.R && x.R.actualWaitMs >= d4.escWaitMs && x.R.actualWaitMs <= d4.escWaitMs + F6.READ_COST
    && x.escMinusOpen >= d4.escWaitMs && x.escMinusOpen <= d4.escWaitMs + d4.openPollMs + F6.READ_COST && x.R.escWaitMs === d4.escWaitMs && x.R.openObserved === true;
  const t2k = await runFakeI4(i4, fakeWorld(), f6Cell('kbd-open', 'after', 300));
  const t2m = await runFakeI4(i4, fakeWorld({ openLatMs: 49 }), f6Cell('mouse-open', 'before', 0));
  check('고침6 T2: 기다림 — rules escWaitMs 700 · openPollMs 50 · 열림 관측 → Esc 사이 [700, 700+읽기] · 실제 열림 → Esc 사이 [700, 750+읽기] · kbd-open · mouse-open 모두',
    d4.escWaitMs === 700 && d4.openPollMs === 50 && /6709803008/.test(d4.escWaitRef) && inT2(t2k) && inT2(t2m), { k: t2k.R && t2k.R.actualWaitMs, kOpen: t2k.escMinusOpen, m: t2m.R && t2m.R.actualWaitMs, mOpen: t2m.escMinusOpen, err: t2k.err || t2m.err });
  // J1-B: 기다림 줄이 관측 loop 로 바뀜 → 같은 돌연변이(escWaitMs 무시)를 새 줄에(옛 글자 = 아래 주석)
  // ⬛ 옛 돌연변이: s.replace('  if (restMs > 0) await L.wait(ctx, restMs);\n', '')
  const m0 = mutantI4((s) => s.replace('  const holdEnd = L.nowMs() + Math.max(0, restMs);\n', '  const holdEnd = L.nowMs();\n'));
  const t2z = m0 ? await runFakeI4(m0, fakeWorld(), f6Cell('kbd-open', 'after', 300)) : null;
  plant('고침6 T2 심은 위반: escWaitMs 무시(기다림 줄 뺀 사본) → 기다림 단언 빨강', !!m0 && !t2z.err && t2z.escs === 1 && !inT2(t2z), t2z && { wait: t2z.R && t2z.R.actualWaitMs, err: t2z.err });
  const m1 = mutantI4((s) => s.replace('  const ob = await L.waitOpenObserved(ctx, d4.openPollMs);\n', '  const tStart = L.nowMs() - ctx.sig.t0;\n  const ob = await L.waitOpenObserved(ctx, d4.openPollMs);\n')
    .replace('const restMs = d4.escWaitMs - (L.nowMs() - ctx.sig.t0 - ob.observedAtMs);', 'const restMs = d4.escWaitMs - (L.nowMs() - ctx.sig.t0 - tStart);'));
  const t2b = m1 ? await runFakeI4(m1, fakeWorld(), f6Cell('kbd-open', 'after', 300)) : null;
  plant('고침6 T2 심은 위반: 기다림을 열림 관측 전(여는 키)부터 셈 → 실제 열림 → Esc < 700 → 빨강', !!m1 && !t2b.err && t2b.escs === 1 && t2b.escMinusOpen < d4.escWaitMs && !inT2(t2b), t2b && { escMinusOpen: t2b.escMinusOpen, err: t2b.err });
  // T3 열림 미관측 — Esc 누름 0 · 표본 장치 실패(예외 → run-items 가 errors 칸 · valid=false) · 사유 글자 = rules 새 키
  const t3 = await runFakeI4(i4, fakeWorld({ openLatMs: null }), f6Cell('kbd-open', 'e1t3', 150));
  const t3ok = (x) => !!x && x.escs === 0 && !!x.err && x.err.startsWith(DR.i4OpenNotObserved) && x.out === null;
  check('고침6 T3: 열림 미관측 → Esc 누름 0 · 표본 = 장치 실패(예외 · 사유 「열린 메뉴 관측 안 됨 — HQA 절차 전제 불성립」)', DR.i4OpenNotObserved === '열린 메뉴 관측 안 됨 — HQA 절차 전제 불성립' && t3ok(t3), { err: t3.err, escs: t3.escs });
  // J1-B: 기다림 관측(2-a)이 두 번째로 막아 관측 단언만 뺀 사본은 Esc 를 안 누름 → 옛 꼴(미관측인데 Esc)을 재현하려고 기다림 관측 단언 줄도 함께 뺌
  //   (관측 단언만 뺀 사본 = 아래 「J1-B 이중 막음」 새 단언)
  // ⬛ 옛 돌연변이: s.replace(/  if \(!ob\.observed\) throw new Error\([^\n]*\n/, '')
  // J1-C: 기다림 관측 단언 줄이 loop 안(들여쓰기 4) → loop 밖 한 줄(들여쓰기 2 · 줄 머리 고정 /m — 옛 줄 주석에 안 걸리게)
  // ⬛ J1-B 돌연변이: ….replace(/    if \(stEsc\.openCount === 0\) throw new Error\([^\n]*\n/, '')
  const m3 = mutantI4((s) => s.replace(/  if \(!ob\.observed\) throw new Error\([^\n]*\n/, '').replace(/^  if \(stEsc\.openCount === 0\) throw new Error\([^\n]*\n/m, ''));
  const t3b = m3 ? await runFakeI4(m3, fakeWorld({ openLatMs: null }), f6Cell('kbd-open', 'e1t3', 150)) : null;
  plant('고침6 T3 심은 위반: 미관측인데 Esc 누름(관측 단언 줄 뺀 사본) → 빨강', !!m3 && t3b.escs === 1 && !t3ok(t3b), t3b && { escs: t3b.escs, err: t3b.err });
  // T3 판독기 쪽: 그 꼴 표본(valid=false · errors = 사유) 이 든 칸 → 장치 실패 · 대조 불가(통과로 안 샘)
  {
    const R6 = JSON.parse(JSON.stringify(rules));
    R6.plan.items = ['I-4'];
    R6.hqaPending.samplesPerCell.value = { default: 1, 'N-1': 1 };
    const p6 = buildPlan(R6);
    const rank = (sc) => R6.plan.screenOrder.indexOf(sc);
    const xs = p6.cells.slice().sort((a, b) => a.delay - b.delay || rank(a.screen) - rank(b.screen)).map((c, i) => ({
      item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, cell: c.key, sampleNo: 1, valid: true, errors: [], deviceReading: null, order: i + 1, contextId: i + 1,
      readings: { openCountAfterEsc: c.screen === 'before' ? 1 : 0, focusOnSameRowBtn: c.screen !== 'e1t3' }, circuit: { dead: false, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false },
      kbd: c.case === 'kbd-open' ? (c.screen === 'before' ? null : { reached: true, tabs: 3 }) : null
    }));
    for (const s of xs) if (s.case === 'kbd-open' && s.screen === 'before') { s.readings = { noInput: 'x' }; }
    const okv = require('./judge.js').judge(R6, k1Entry(xs), {});
    const bad = JSON.parse(JSON.stringify(xs));
    const hit = bad.find((s) => s.case === 'kbd-open' && s.screen === 'e1t3' && s.delay === 300);
    hit.valid = false; hit.readings = null; hit.kbd = null; hit.errors = [t3.err];
    const bv = require('./judge.js').judge(R6, k1Entry(bad), {});
    const bc = bv.items['I-4'].contrastByCase['kbd-open'];
    check('고침6 T3 판독기: 열림 미관측 표본 1 → 그 칸 장치 실패 · I-4 kbd-open 대조 = null(대조 불가) · 항목 state null · 빨강 / 대조군 = 성립',
      bv.red === true && bc.state === null && bc.reason === T.fix5.contrastDeviceFail && bv.items['I-4'].contrastState.state === null && okv.items['I-4'].contrastByCase['kbd-open'].state === T.fix5.contrastHolds,
      { bad: bc, ok: okv.items['I-4'].contrastByCase['kbd-open'].state });
  }
  // T6 모든 화면 같게 — 계획 I-4 칸 전부(before · after · e1t3 · 두 경우) 같은 기다림 · 규칙에 화면별 · 경우별 덮어쓰기 칸 0
  // 허용 칸 목록(밖의 칸 = 덮어쓰기 의심 → 빨강) — 옛 꼴(정규식 「after」 등)은 pollAfterEscMaxMs 를 잘못 걸어 c3 에서 목록으로 바꿈
  const I4_SPEC_OK = new Set(['cases', 'delays', 'caseScreens', '_caseScreens_표지', 'caseKeys', '_caseKeys_표지']);
  const I4_DEV_OK = new Set(['row', 'escWaitMs', 'openPollMs', 'escWaitRef', 'pollAfterEscMaxMs', 'menuZeroFailCases', 'openKeys']);
  const sameRule = (r) => {
    const i4r = r.device.i4;
    const extra = Object.keys(i4r).filter((k) => !k.startsWith('_') && !I4_DEV_OK.has(k));
    const specExtra = Object.keys(r.plan.itemSpecs['I-4']).filter((k) => !I4_SPEC_OK.has(k));
    return typeof i4r.escWaitMs === 'number' && typeof i4r.openPollMs === 'number' && extra.length === 0 && specExtra.length === 0;
  };
  const allCells = buildPlan(rules).cells.filter((c) => c.item === 'I-4');
  const waits = async (fn) => {
    const out = [];
    for (const c of allCells) {
      const x = await runFakeI4(fn, fakeWorld(), f6Cell(c.case, c.screen, c.delay, c.openKey));
      out.push({ key: c.key, ok: inT2(x), ran: !x.err && x.escs === 1, wait: x.R ? x.R.actualWaitMs : null });
    }
    return out;
  };
  const wAll = await waits(i4);
  const scr = new Set(allCells.map((c) => c.screen + '/' + c.case));
  check('고침6 T6: 계획 I-4 칸 전부(화면 before · after · e1t3 × 두 경우) 기다림 같음(700) · rules 에 화면별 · 경우별 덮어쓰기 칸 0',
    sameRule(rules) && wAll.length === allCells.length && wAll.every((x) => x.ok) && ['before/mouse-open', 'after/mouse-open', 'before/kbd-open', 'after/kbd-open', 'e1t3/kbd-open'].every((k) => scr.has(k)),
    { n: wAll.length, bad: wAll.filter((x) => !x.ok).slice(0, 3) });
  const rs1 = JSON.parse(JSON.stringify(rules)); rs1.device.i4.escWaitMsE1t3 = 0;
  const rs2 = JSON.parse(JSON.stringify(rules)); rs2.plan.itemSpecs['I-4'].escWaitMs = { e1t3: 0 };
  plant('고침6 T6 심은 위반: rules 에 화면별 덮어쓰기 칸 하나(device.i4 · itemSpecs.I-4 각각) → 빨강', !sameRule(rs1) && !sameRule(rs2));
  const m6 = mutantI4((s) => s.replace('const restMs = d4.escWaitMs - (', "const restMs = (ctx.cell.screen === 'e1t3' ? 0 : d4.escWaitMs) - ("));
  const w6 = m6 ? await waits(m6) : null;
  plant('고침6 T6 심은 위반: 코드가 e1t3 화면만 기다림 0(돌연변이 사본) → 그 칸 빨강', !!w6 && w6.every((x) => x.ran) && w6.some((x) => !x.ok) && w6.filter((x) => !x.ok).every((x) => x.key.includes('|e1t3|')), w6 && w6.filter((x) => !x.ok).length);
  await fix6PollChecks(i4);
  await fix6bChecks(i4); // J1-B 반려 고침 — 700ms 유지 관측 · 규칙 값 단언
}
// 고침6 c3 — T4 polling 판독 · T5 메뉴 0 미관측 · 결-12 두 식 · mouse-open 열림 남음(구현 판단 지킴)
async function fix6PollChecks(i4) {
  const d4 = rules.device.i4;
  const DR = rules.deviceReading;
  const J = require('./judge.js');
  const k300 = f6Cell('kbd-open', 'after', 300);
  // T4 — 메뉴 0 뒤 2.5초 복원 = 참 · 3.2초 = 거짓 · polledMs ≥ 3000
  const t4ok = (x) => !!x && !x.err && x.R.menuZeroObserved === true && x.R.focusOnSameRowBtn === true && x.R.hqaFocusRow1Restored === true
    && x.R.restoredAtMs + F6.T0 - x.w.zeroAt >= 2500 && x.R.restoredAtMs + F6.T0 - x.w.zeroAt <= 2500 + d4.openPollMs + 2 * F6.READ_COST
    && x.R.menuZeroAtMs + F6.T0 - x.w.zeroAt >= 0 && x.R.menuZeroAtMs + F6.T0 - x.w.zeroAt <= d4.openPollMs + F6.READ_COST; // 세계의 실제 닫힘 시각 기준
  const t4no = (x) => !!x && !x.err && x.R.menuZeroObserved === true && x.R.focusOnSameRowBtn === false && x.R.hqaFocusRow1Restored === false && x.R.restoredAtMs === null && x.R.polledMs >= d4.pollAfterEscMaxMs;
  const a = await runFakeI4(i4, fakeWorld({ restoreAfterZeroMs: 2500 }), k300);
  const b = await runFakeI4(i4, fakeWorld({ restoreAfterZeroMs: 3200 }), k300);
  check('고침6 T4: Esc → 메뉴 0 관측 → 최대 3000ms polling — 2.5초 복원 = 참(두 식) · 3.2초 = 거짓 · polledMs ≥ 3000 · rules pollAfterEscMaxMs 3000',
    d4.pollAfterEscMaxMs === 3000 && t4ok(a) && t4no(b), { a: a.R && [a.R.restoredAtMs, a.R.menuZeroAtMs], b: b.R && b.R.polledMs, err: a.err || b.err });
  const m4 = mutantI4((s) => s.replace('  const pa = await L.pollAfterEsc(ctx, d4.openPollMs, d4.pollAfterEscMaxMs);\n',
    '  const sx = await L.settle(ctx, L.settleMs(ctx.rules, ctx.cell.delay));\n  const okx = sx.state.focus.row === row && sx.state.focus.isStatusActivator && !sx.state.focus.inOpenList;\n'
    + '  const pa = { menuZeroObserved: true, menuZeroAtMs: 0, restoredAtMs: okx ? 0 : null, hqaRestoredAtMs: okx ? 0 : null, polledMs: 0, reads: 1, state: sx.state, maxOpen: sx.maxOpen, hqaFocusRowEnd: null };\n'));
  const a4 = m4 ? await runFakeI4(m4, fakeWorld({ restoreAfterZeroMs: 2500 }), k300) : null;
  plant('고침6 T4 심은 위반: polling 을 옛 settle(600 + 4×300 = 1800) 끝 상태로 바꾼 사본 → 2.5초 꼴이 거짓 → 빨강', !!m4 && !a4.err && a4.R.focusOnSameRowBtn === false && !t4ok(a4), a4 && { f: a4.R && a4.R.focusOnSameRowBtn, err: a4.err });
  // T5 — kbd-open 메뉴 안 닫힘 → 장치 실패(예외 · 사유 새 키) · 판독값 0
  const t5ok = (x) => !!x && x.out === null && !!x.err && x.err.startsWith(DR.i4MenuZeroNotObserved) && x.escs === 1;
  const c5 = await runFakeI4(i4, fakeWorld({ closeLatMs: null }), f6Cell('kbd-open', 'e1t3', 300));
  check('고침6 T5: kbd-open Esc 뒤 메뉴 안 닫힘 → 표본 장치 실패(예외 · 「Esc 뒤 메뉴 0 관측 안 됨 — HQA 절차 전제 불성립」) · menuZeroFailCases = kbd-open',
    JSON.stringify(d4.menuZeroFailCases) === '["kbd-open"]' && t5ok(c5), { err: c5.err });
  const m5 = mutantI4((s) => s.replace(/  if \(!pa\.menuZeroObserved && [^\n]*\n[^\n]*\n  \}\n/, ''));
  const c5b = m5 ? await runFakeI4(m5, fakeWorld({ closeLatMs: null }), f6Cell('kbd-open', 'e1t3', 300)) : null;
  plant('고침6 T5 심은 위반: 메뉴 0 못 봤는데 판독(단언 뺀 사본) → 빨강', !!m5 && !c5b.err && !!c5b.R && !t5ok(c5b), c5b && { err: c5b.err });
  // 구현 판단(부록 M) — mouse-open 메뉴 안 닫힘 = 열림 남음 판독값(표본 유효) · 초점 polling 0 · /before 실패 꼴 그대로 읽힘
  const mOk = (x) => !!x && !x.err && x.R.menuZeroObserved === false && x.R.openCountAfterEsc >= 1 && x.R.focusOnSameRowBtn === null && x.R.hqaFocusRow1Restored === null
    && x.R.menuNotClosedNote === DR.i4MenuNotClosedRead && J.BEFORE_FAIL['I-4']({}, false, x.R) === true;
  const mb = await runFakeI4(i4, fakeWorld({ closeLatMs: null }), f6Cell('mouse-open', 'before', 300));
  check('고침6 구현 판단: mouse-open Esc 뒤 메뉴 안 닫힘 → 표본 유효 · openCountAfterEsc ≥ 1(/before 실패 꼴 「열림 남음」 그대로 읽힘) · 초점 두 식 null · 사유 글자', mOk(mb), { err: mb.err, R: mb.R && { o: mb.R.openCountAfterEsc, f: mb.R.focusOnSameRowBtn } });
  const rAll = JSON.parse(JSON.stringify(rules)); rAll.device.i4.menuZeroFailCases = ['kbd-open', 'mouse-open'];
  const mb2 = await runFakeI4(i4, fakeWorld({ closeLatMs: null }), f6Cell('mouse-open', 'before', 300), rAll);
  plant('고침6 구현 판단 대조군: menuZeroFailCases 에 mouse-open 더한 사본(★ 글자 그대로) → /before 마우스 Esc 실패 꼴을 못 읽음(장치 실패) → 빨강', !mOk(mb2) && !!mb2.err && mb2.err.startsWith(DR.i4MenuZeroNotObserved), { err: mb2.err });
  // 결-12 두 식 — 각각 기록 · 갈리면 갈린 대로(판독 식은 바꾸지 않음)
  const dv = await runFakeI4(i4, fakeWorld({ hqaSame: false }), k300);
  const twoOk = (x) => !!x && !x.err && x.R.focusOnSameRowBtn === true && x.R.hqaFocusRow1Restored === false && x.R.hqaRestoredAtMs === null && typeof x.R.restoredAtMs === 'number';
  check('고침6 결-12: 두 식 따로 기록 — 지금 식 참 · HQA 식 거짓인 가짜 세계 → focusOnSameRowBtn true · hqaFocusRow1Restored false(갈린 대로)', twoOk(dv), dv.R && { f: dv.R.focusOnSameRowBtn, h: dv.R.hqaFocusRow1Restored });
  const m12 = mutantI4((s) => s.replace('hqaFocusRow1Restored: pa.menuZeroObserved ? pa.hqaRestoredAtMs !== null : null', 'hqaFocusRow1Restored: pa.menuZeroObserved ? pa.restoredAtMs !== null : null'));
  const dv2 = m12 ? await runFakeI4(m12, fakeWorld({ hqaSame: false }), k300) : null;
  plant('고침6 결-12 심은 위반: HQA 식 칸을 지금 식 값으로 채운 사본 → 갈림이 숨음 → 빨강', !!m12 && !dv2.err && !twoOk(dv2), dv2 && dv2.R && dv2.R.hqaFocusRow1Restored);
  const libSrc = fs.readFileSync(F6.LIB, 'utf8');
  const HQA_EXPR = "document.activeElement?.closest('[data-status-kind]')?.getAttribute('data-row')";
  const hb = fnBody(libSrc, 'readHqaFocusRow');
  check('고침6 결-12 원문: _lib readHqaFocusRow 의 evaluate 식 = HQA 원문 L47 식 글자 그대로 · rules 표지에 같은 식 · 소스 대조 근거(FixturePage L51 · 팝오버 칸 밖 · #detail)',
    !!hb && hb.includes(HQA_EXPR) && evalBodies(hb).length === 1 && rules.device.i4._hqaFocusRow1_표지_고침6.includes(HQA_EXPR + " === '1'") && /FixturePage\.razor L51/.test(rules.device.i4._hqaFocusRow1_표지_고침6) && /#detail/.test(rules.device.i4._hqaFocusRow1_표지_고침6));
  plant('고침6 결-12 원문 대조군: 식을 [data-row] 로 바꾼 사본 → 원문 식 단언 빨강', !!hb && !(fnBody(libSrc.replace("closest('[data-status-kind]')?.getAttribute('data-row')", "closest('[data-row]')?.getAttribute('data-row')"), 'readHqaFocusRow') || '').includes(HQA_EXPR));
}

// ── J1-B(반려 고침 · [3-V] 2-a · 2-b) — I-4 700ms 유지 관측 · 규칙 값 단언 (가짜 L · 돌연변이 사본 · 브라우저 · 망 0) ──
async function fix6bChecks(i4) {
  const d4 = rules.device.i4;
  const DR = rules.deviceReading;
  // 2-a 정상 세계: 기다리는 동안 간격 관측 · Esc 직전 읽기 · 새 칸 openCountAtEsc · 옛 칸 openCountBeforeEsc 그대로
  // ⬛ J1-B 기대(간격 관측 · 읽기 ≥ minReads): const holdOk = (x) => … && x.R.holdReads >= minReads;
  //   J1-C(PM 결정 · 관찰자 영향 줄이기): 기다림 중 읽기 0 · Esc 직전 한 번 → holdReads === 1. minReads 는 아래 (다) 돌연변이 기대값(polling 사본 읽기 수 하한)으로 씀
  const minReads = Math.floor(d4.escWaitMs / (d4.openPollMs + F6.READ_COST));
  const holdOk = (x) => !!x && !x.err && x.escs === 1 && !!x.R && x.R.openCountAtEsc === 1 && x.R.openCountBeforeEsc === 1 && x.R.holdReads === 1;
  // J1-C 동작 잼(가짜 시계 readLog): 첫 열림 관측(그 읽기 끝) 뒤 ~ Esc 사이 읽기 = 기다림 중 읽기(Esc 직전 읽기 제외) · Esc 직전 읽기(끝 = Esc 누른 시각)
  const holdTrace = (x) => {
    if (!x || !x.R || x.w.escAt === null || typeof x.R.openObservedAtMs !== 'number') return null;
    const obsT = F6.T0 + x.R.openObservedAtMs;
    const between = x.w.readLog.filter((t) => t >= obsT && t < x.w.escAt);
    return { duringWait: between.filter((t) => t + F6.READ_COST < x.w.escAt).length, preEsc: between.filter((t) => t + F6.READ_COST === x.w.escAt).length };
  };
  const quietOk = (x) => { const h = holdTrace(x); return !!h && h.duringWait === 0 && h.preEsc === 1; };
  const hk = await runFakeI4(i4, fakeWorld(), f6Cell('kbd-open', 'after', 300));
  const hm = await runFakeI4(i4, fakeWorld(), f6Cell('mouse-open', 'before', 0));
  const keyLen = ['i4OpenNotHeld', 'i4RuleBad', 'i4EscWaitMissing', 'i4FocusNotBool'].every((k) => k.length <= 21 && typeof DR[k] === 'string' && DR[k].length > 0);
  // ⬛ J1-B 제목: `J1-B 2-a: 700ms 기다림 = 간격 ${d4.openPollMs}ms 관측 — Esc 직전 열린 목록 1(openCountAtEsc) · 기다리는 동안 읽기 ≥ ${minReads} · 옛 칸 openCountBeforeEsc 그대로 · 두 경우 · 새 사유 키 4 ≤ 21자`
  check('J1-B 2-a · J1-C: 700ms 기다림 = 읽기 0 · Esc 직전 한 번 읽기 — Esc 직전 열린 목록 1(openCountAtEsc) · holdReads 1 · 옛 칸 openCountBeforeEsc 그대로 · 두 경우 · 새 사유 키 4 ≤ 21자',
    holdOk(hk) && holdOk(hm) && keyLen, { k: hk.R && [hk.R.openCountAtEsc, hk.R.holdReads], m: hm.R && [hm.R.openCountAtEsc, hm.R.holdReads], err: hk.err || hm.err });
  // J1-C (다): 기다림 동안 readState 호출 0 · Esc 직전 읽기 1(읽기 끝 = Esc 시각) — 가짜 시계 readLog 로 잼 · 두 경우 + 계획 I-4 칸 전부
  const allI4 = buildPlan(rules).cells.filter((c) => c.item === 'I-4');
  const qAll = [];
  for (const c of allI4) qAll.push({ key: c.key, ok: quietOk(await runFakeI4(i4, fakeWorld(), f6Cell(c.case, c.screen, c.delay, c.openKey))) });
  check('J1-C (다): 기다림 동안 readState 호출 0 · Esc 직전 한 번(그 읽기 끝 = Esc 누른 시각) — 두 경우 · 계획 I-4 칸 전부(관찰자 영향 회피 · HQA 원 절차에 없는 읽기 0)',
    quietOk(hk) && quietOk(hm) && qAll.length === allI4.length && qAll.length > 0 && qAll.every((x) => x.ok), { k: holdTrace(hk), m: holdTrace(hm), bad: qAll.filter((x) => !x.ok).slice(0, 3) });
  // (다) 심은 위반: polling 사본(기다림을 openPollMs 간격 읽기로 쪼갬 · J1-B 꼴의 읽기) → 기다림 중 읽기 > 0 → 빨강
  const mp = mutantI4((s) => s.replace(/^  if \(left > 0\) await L\.wait\(ctx, left\);\n/m,
    '  for (let rem = left; rem > 0; rem = holdEnd - L.nowMs()) { await L.wait(ctx, Math.min(d4.openPollMs, rem)); await L.readState(ctx); }\n'));
  const pk = mp ? await runFakeI4(mp, fakeWorld(), f6Cell('kbd-open', 'after', 300)) : null;
  plant('J1-C (다) 심은 위반: 기다림을 간격 읽기로 쪼갠 polling 사본 → 기다림 중 readState > 0 → 빨강', !!mp && !pk.err && pk.escs === 1 && !!holdTrace(pk) && holdTrace(pk).duringWait >= minReads - 1 && !quietOk(pk), pk && { trace: holdTrace(pk), err: pk.err });
  // (라) 심은 위반: Esc 직전 한 번 읽기를 뺀 사본(관측 시점 값 재사용) → Esc 직전 읽기 0 · 닫힌 세계에서도 Esc 누름 → 빨강
  const mr = mutantI4((s) => s.replace(/^  const stEsc = await L\.readState\(ctx\);\n/m, '  const stEsc = st1;\n'));
  const rk = mr ? await runFakeI4(mr, fakeWorld(), f6Cell('kbd-open', 'after', 300)) : null;
  const rc = mr ? await runFakeI4(mr, fakeWorld({ selfCloseAfterOpenMs: 300 }), f6Cell('kbd-open', 'e1t3', 300)) : null;
  plant('J1-C (라) 심은 위반: Esc 직전 한 번 읽기 뺀 사본 → Esc 직전 읽기 0 · 기다리는 중 닫힌 메뉴에 Esc 누름 → 빨강', !!mr && !rk.err && !quietOk(rk) && holdTrace(rk).preEsc === 0 && !rc.err && rc.escs === 1,
    rk && { trace: holdTrace(rk), closedEscs: rc && rc.escs, err: rc && rc.err });
  // 2-a 실패 세계: 열린 뒤 300ms 에 Esc 없이 닫힘(그대로 · 또는 100ms 뒤 다시 열림) → Esc 누름 0 · 표본 = 장치 실패(예외 · 사유 i4OpenNotHeld)
  const closeOk = (x) => !!x && x.out === null && x.escs === 0 && !!x.err && x.err.startsWith(DR.i4OpenNotHeld);
  const W_CLOSE = { selfCloseAfterOpenMs: 300 };
  const W_DIP = { selfCloseAfterOpenMs: 300, reopenAfterMs: 100 };
  const ck = await runFakeI4(i4, fakeWorld(W_CLOSE), f6Cell('kbd-open', 'e1t3', 300));
  const cm = await runFakeI4(i4, fakeWorld(W_CLOSE), f6Cell('mouse-open', 'before', 0));
  const cd = await runFakeI4(i4, fakeWorld(W_DIP), f6Cell('kbd-open', 'after', 150));
  // ⬛ J1-B 단언(셋 다 장치 실패 기대): check('J1-B 2-a: 기다리는 동안 메뉴 닫힘(그대로 · 100ms 닫혔다 다시 열림) → Esc 누름 0 · 표본 = 장치 실패(…) · kbd-open · mouse-open',
  //     closeOk(ck) && closeOk(cm) && closeOk(cd), …);
  //   J1-C: 지우지 않고 둘로 — (가) 끝까지 닫힌 세계 = 장치 실패 유지(Esc 직전 한 번 읽기가 막음) · (나) 100ms 닫혔다 다시 열림 = 관측 안 함(기대 바뀜 · 공개)
  check('J1-B 2-a · J1-C (가): 기다리는 동안 메뉴 닫혀 끝까지 닫힘 → Esc 직전 한 번 읽기가 막음 · Esc 누름 0 · 표본 = 장치 실패(예외 · 「Esc 전 기다리는 동안 메뉴 0 — 열림 유지 미확인 …」) · kbd-open · mouse-open',
    closeOk(ck) && closeOk(cm), { k: ck.err, m: cm.err, escs: [ck.escs, cm.escs] });
  // ⬛ J1-B 기대(cd = 100ms 닫혔다 다시 열림 → closeOk(cd) · 장치 실패)
  check('J1-C (나): 기다림 중 순간 닫힘(300ms 에 닫혀 100ms 뒤 다시 열림)은 관측 안 함(관찰자 영향 회피) · 공개 — Esc 누름 1 · 장치 실패 0 · holdReads 1 · Esc 직전 열린 목록 1 · 기다림 중 읽기 0',
    !!cd && !cd.err && cd.escs === 1 && !!cd.R && cd.R.holdReads === 1 && cd.R.openCountAtEsc === 1 && quietOk(cd), { err: cd.err, escs: cd.escs, trace: holdTrace(cd) });
  // J1-C: 단언 줄이 loop 밖 들여쓰기 2 로 바뀜 → 줄 머리 고정 /m(옛 줄 주석에 안 걸리게)
  // ⬛ J1-B 돌연변이: s.replace(/    if \(stEsc\.openCount === 0\) throw new Error\([^\n]*\n/, '')
  const mh = mutantI4((s) => s.replace(/^  if \(stEsc\.openCount === 0\) throw new Error\([^\n]*\n/m, ''));
  const ckm = mh ? await runFakeI4(mh, fakeWorld(W_CLOSE), f6Cell('kbd-open', 'e1t3', 300)) : null;
  plant('J1-B 2-a 심은 위반: 기다림 관측 단언 줄 뺀 사본 → 닫힌 메뉴에 Esc 누름 · 표본 판독 → 빨강', !!mh && !ckm.err && ckm.escs === 1 && !closeOk(ckm), ckm && { escs: ckm.escs, err: ckm.err });
  // ⬛ J1-B 심은 위반(지금 코드가 바로 그 꼴이 되어 뜻이 뒤집힘 → (나) 공개 단언 · (다) polling 사본 심은 위반으로 옮김):
  // const m1r = mutantI4((s) => s.replace('    if (left > 0) await L.wait(ctx, Math.min(d4.openPollMs, left));\n', '    if (left > 0) await L.wait(ctx, left);\n'));
  // const cdm = m1r ? await runFakeI4(m1r, fakeWorld(W_DIP), f6Cell('kbd-open', 'after', 150)) : null;
  // plant('J1-B 2-a 심은 위반: 기다림 끝에 한 번만 읽는 사본(간격 관측 0) → 100ms 닫힘을 못 봄 → Esc 누름 → 빨강', !!m1r && !cdm.err && cdm.escs === 1 && !closeOk(cdm), …);
  // J1-C 대신: J1-B 꼴(간격 관측 + 닫힘 단언) 사본은 100ms 닫힘을 봄 → (나) 공개 단언이 그 사본에서 빨강 = 기대가 바뀐 것을 동작으로 확인
  const mj = mutantI4((s) => s.replace(/^  if \(left > 0\) await L\.wait\(ctx, left\);\n/m,
    '  for (let rem = left; rem > 0; rem = holdEnd - L.nowMs()) { await L.wait(ctx, Math.min(d4.openPollMs, rem)); const sp = await L.readState(ctx); if (sp.openCount === 0) throw new Error(`${ctx.rules.deviceReading.i4OpenNotHeld} · 돌연변이`); }\n'));
  const cdj = mj ? await runFakeI4(mj, fakeWorld(W_DIP), f6Cell('kbd-open', 'after', 150)) : null;
  plant('J1-C (나) 심은 위반: J1-B 꼴(간격 관측 + 닫힘 단언) 사본 → 100ms 닫힘을 봄 · Esc 0 · 장치 실패 → (나) 기대 빨강', !!mj && closeOk(cdj), cdj && { escs: cdj.escs, err: cdj.err });
  // J1-B 이중 막음: 열림 관측 단언만 뺀 사본(옛 T3 돌연변이) → 기다림 관측이 Esc 를 막음(Esc 0 · 사유 i4OpenNotHeld)
  const m3o = mutantI4((s) => s.replace(/  if \(!ob\.observed\) throw new Error\([^\n]*\n/, ''));
  const t3o = m3o ? await runFakeI4(m3o, fakeWorld({ openLatMs: null }), f6Cell('kbd-open', 'e1t3', 150)) : null;
  check('J1-B 이중 막음: 열림 관측 단언만 뺀 사본 + 열림 0 세계 → 기다림 관측이 Esc 를 막음(Esc 0 · 장치 실패)', !!m3o && closeOk(t3o), t3o && { escs: t3o.escs, err: t3o.err });
  // 2-b 규칙 값 단언 — 키 셋 · 배열을 하나씩 빼거나 깨뜨린 rules 사본마다 입력 0 · 표본 = 장치 실패(사유 i4RuleBad)
  const BRK = [
    ['escWaitMs 없음', (r) => { delete r.device.i4.escWaitMs; }],
    ['openPollMs 없음', (r) => { delete r.device.i4.openPollMs; }],
    ['pollAfterEscMaxMs 없음', (r) => { delete r.device.i4.pollAfterEscMaxMs; }],
    ['menuZeroFailCases 없음', (r) => { delete r.device.i4.menuZeroFailCases; }],
    ['escWaitMs 글자', (r) => { r.device.i4.escWaitMs = '700'; }],
    ['escWaitMs 무한', (r) => { r.device.i4.escWaitMs = Infinity; }],
    ['openPollMs 0', (r) => { r.device.i4.openPollMs = 0; }],
    ['pollAfterEscMaxMs 음수', (r) => { r.device.i4.pollAfterEscMaxMs = -5; }],
    ['menuZeroFailCases 글자', (r) => { r.device.i4.menuZeroFailCases = 'kbd-open'; }]
  ];
  const ruleOk = (x) => !!x && x.out === null && !!x.err && x.err.startsWith(DR.i4RuleBad) && x.inputs.length === 0;
  const brkRun = async (fn, kase) => {
    const out = [];
    for (const [name, f] of BRK) {
      const r = JSON.parse(JSON.stringify(rules));
      f(r);
      const x = await runFakeI4(fn, fakeWorld(), f6Cell(kase, kase === 'mouse-open' ? 'before' : 'after', 150), r);
      out.push({ name, ok: ruleOk(x), err: x.err ? x.err.slice(0, 60) : null, inputs: x.inputs.length });
    }
    return out;
  };
  const bk = await brkRun(i4, 'kbd-open');
  const bm = await brkRun(i4, 'mouse-open');
  const iSrc = fs.readFileSync(F6.I, 'utf8');
  const codeOnly = iSrc.split(/\r?\n/).filter((ln) => !/^\s*\/\//.test(ln)).join('\n');
  check(`J1-B 2-b: 규칙 값 ${BRK.length}꼴(키 셋 · 배열 빠짐 · 글자 · 무한 · 0 · 음수) 하나씩 깬 rules 사본 → 입력 0 · 표본 = 장치 실패(「I-4 규칙 값 빠짐 · 깨짐 …」) · 두 경우 · 코드에 「menuZeroFailCases || []」 0`,
    bk.every((x) => x.ok) && bm.every((x) => x.ok) && !/menuZeroFailCases \|\| \[\]/.test(codeOnly), { bad: bk.concat(bm).filter((x) => !x.ok) });
  const ma = mutantI4((s) => s.replace(/  if \(ruleBad\.length\) throw new Error\([^\n]*\n/, ''));
  const bka = ma ? await brkRun(ma, 'kbd-open') : null;
  plant(`J1-B 2-b 심은 위반: 규칙 단언 줄 뺀 사본 → ${BRK.length}꼴 모두 단언 빨강(진행하거나 엉뚱한 예외)`, !!ma && bka.every((x) => !x.ok), bka && bka.map((x) => [x.name, x.err]));
  const mo = mutantI4((s) => s.replace('  if (!pa.menuZeroObserved && d4.menuZeroFailCases.includes(ctx.cell.case)) {', '  if (!pa.menuZeroObserved && (d4.menuZeroFailCases || []).includes(ctx.cell.case)) {')
    .replace(/  if \(ruleBad\.length\) throw new Error\([^\n]*\n/, ''));
  const rNo = JSON.parse(JSON.stringify(rules)); delete rNo.device.i4.menuZeroFailCases;
  const lk = mo ? await runFakeI4(mo, fakeWorld({ closeLatMs: null }), f6Cell('kbd-open', 'e1t3', 300), rNo) : null;
  plant('J1-B 2-b 대조군(옛 꼴 「|| []」 + 단언 0 사본): 목록 빠진 rules · kbd-open 메뉴 안 닫힘 → 장치 실패 없이 판독값이 나옴(새는 길 재현)', !!mo && !lk.err && !!lk.R && lk.R.menuZeroObserved === false, lk && { err: lk.err });
}

// ── 고침7 K1 마지막 판정(모든 판독기 호출 뒤) — T19 · T21 · T23(입력 전부) · T25 ④⑤ · T26 · T27 · T28 ──
function k1Final() {
  check('고침7 T19(CTO 조건 1): 판독기 입력 전부 — classes.total = I-4 표본 수 = 네 칸 합 · 독립 판별과 같음 · 불일치 · 비교 불가 겹침 0', K1.t19.length === 0 && K1.judged > 0, { bad: K1.t19.slice(0, 3), judged: K1.judged });
  check('고침7 T21(CTO ④ 조건): 판독기 입력 전부 — focusAfterClose 줄마다 valid = on + off + noRead + exprMismatchExcluded', K1.t21.length === 0 && K1.judged > 0, K1.t21.slice(0, 3));
  check('고침7 T23(CTO ④ 조건): 판독기 입력 전부 — 경우별 · 항목 대조 칸에 exprMismatchChanged · exprMismatchRemoved 늘 함께 · 바뀜 참이면 뺀 수 > 0', K1.t23.length === 0 && K1.judged > 0, K1.t23.slice(0, 3));
  check('고침7 T25(CTO 조건 2 · 안 (가)): 입구 전/후 입력 — ① 차이 = I-4 두 칸뿐 ② 덧붙인 값 규칙 ③ 원 입력 무변 · ④ 고침 전 판독기 판정 같음 · ⑤ 고침 뒤 − 새 칸 = 고침 전(불일치를 심은 새 시험 입력 빼고)',
    !!k1PreJudge() && K1.entryBad.length === 0 && K1.t25.length === 0 && K1.t25checked > 0 && K1.entryTouched > 0,
    { pre: K1.pre, entryBad: K1.entryBad.slice(0, 3), t25: K1.t25.slice(0, 5), checked: K1.t25checked, plantedMm: K1.t25mm });
  // T28 ⓓ(C2-2 셈의 눈 대조군): 새로 실은 판독기를 입구 · 이름표 없이 직접 1회(대조군 표지 · 본 판정에서 뺌)
  const before = K1.calls.length;
  const ctl = [];
  Object.defineProperty(ctl, K1_CONTROL, { value: true });
  const Jd = require('./judge.js');
  try { Jd.judge(rules, ctl, {}); } catch (e) { check('고침7 T28 ⓓ 대조군 호출 예외', false, String(e && e.message).slice(0, 120)); }
  const ctlCalls = K1.calls.filter((c) => c.control);
  const dAll = k1CallVerdict(K1.calls, K1.cli);
  const main = K1.calls.filter((c) => !c.control);
  const V = k1CallVerdict(main, K1.cli);
  check('고침7 T28 ⓓ(셈의 눈): 새로 실은 판독기 직접 1회 → 셈 +1 · 「빠진 자리」 1 로 잡힘 · 대조군 표지 붙은 호출 = 정확히 1',
    K1.calls.length === before + 1 && ctlCalls.length === 1 && dAll.missing === V.missing + 1, { before, after: K1.calls.length, missing: dAll.missing });
  // T28 ⓐⓑⓒ — 판정 함수를 따로 만든 기록에 돌림(본 기록에 섞지 않음)
  const fa = k1CallVerdict([{ origin: 'judge', entry: false, tag: null, needsTwo: 0 }], []);
  const fb = k1CallVerdict([{ origin: 'judge', entry: false, tag: 'T1', needsTwo: 0 }], []);
  const fc = k1CallVerdict([], [{ viaEntry: true, i4: 3, needsTwo: 1 }]);
  check('고침7 T28 ⓐⓑⓒ(대조군): 입구 · 이름표 없는 호출 → 빠진 자리 1 · T1 이름표 비켜 감 → 허용 밖 · 두 칸 없는 I-4 표본 든 명령줄 파일 → 입구 안 거친 파일 1',
    fa.missing === 1 && fb.badBypass.length === 1 && fb.badBypass[0] === 'T1' && fc.badFiles === 1, { fa: fa.missing, fb: fb.badBypass, fc: fc.badFiles });
  // T26 · T27(본 기록 · 대조군 호출 뺌)
  const floorCount = main.filter((c) => c.origin === 'judge').length + K1.cli.length;
  const inner = !!process.env.HQA_SELFTEST_INNER;
  const innerFloor = K1.inner && Number.isInteger(K1.inner.floorCount) ? K1.inner.floorCount : null;
  const runCount = inner ? floorCount : (innerFloor === null ? null : floorCount + innerFloor);
  const floorOk = inner ? floorCount >= K1_FLOOR_INNER : (runCount !== null && runCount >= K1_FLOOR_RUN);
  check(`고침7 T26: 판독기 호출 총수 = 입구 거친 수 + 비켜 간 수 · 비켜 간 이름표 = T7 · T20 뿐 · 각각 ≥ 1 · 하한(${inner ? '안쪽 ' + K1_FLOOR_INNER : '한 런 ' + K1_FLOOR_RUN} · C2-2)`,
    V.missing === 0 && V.total === V.viaEntry + V.bypass && V.badBypass.length === 0 && (V.byTag.T7 || 0) >= 1 && (V.byTag.T20 || 0) >= 1 && floorOk,
    { total: V.total, viaEntry: V.viaEntry, bypass: V.bypass, byTag: V.byTag, floorCount, runCount, innerFloor });
  check('고침7 T27: 빠진 호출 자리 0 — ① 셈이 받은 판독기 입력마다 입구 표시 또는 T7 · T20 이름표 ② 명령줄 입력 파일(두 자리)의 I-4 표본 전부 두 칸 있음 · 입구 거친 입력에 두 칸 없는 I-4 표본 0',
    V.missing === 0 && V.badFiles === 0 && V.entryNeedsTwo === 0 && K1.cli.length >= 2, { missing: V.missing, badFiles: V.badFiles, cli: K1.cli.length });
  K1.out = { total: V.total, viaEntry: V.viaEntry, bypass: V.bypass, byTag: V.byTag, cli: K1.cli.length, mutant: main.filter((c) => c.origin === 'mutant').length,
    floorCount, runCount, control: ctlCalls.length, preCompare: K1.pre.calls, inner: innerFloor };
}

// ── 결과─────────────────────────────────────────────────────────────
function finish() {
  k1Final();
  const fail = results.filter((r) => !r.ok);
  for (const r of results) process.stdout.write(`${r.ok ? 'OK  ' : 'FAIL'} ${r.name}${r.ok ? '' : ' :: ' + JSON.stringify(r.info)}\n`);
  process.stdout.write(JSON.stringify({ selftest: 'judge+static', total: results.length, fail: fail.length }) + '\n');
  // 고침5 K-1: --out <경로> → {passed, failed, total, plantedRed, plantedTotal} 한 JSON(인자 없으면 표준 출력만 · 옛 꼴 그대로)
  const oi = process.argv.indexOf('--out');
  if (oi >= 0 && process.argv[oi + 1]) {
    const pl = results.filter((r) => r.planted);
    fs.writeFileSync(process.argv[oi + 1], JSON.stringify({ passed: results.length - fail.length, failed: fail.length, total: results.length, plantedRed: pl.filter((r) => r.ok).length, plantedTotal: pl.length, judgeCalls: K1.out }) + '\n', 'utf8');
  }
  fs.rmSync(tmp, { recursive: true, force: true });
  process.exitCode = fail.length ? 1 : 0;
}
asyncChecks().then(finish, (e) => { check('비동기 시험 예외', false, String(e && e.message ? e.message : e).slice(0, 200)); finish(); });
