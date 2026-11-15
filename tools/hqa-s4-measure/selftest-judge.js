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

const HERE = __dirname;
const results = [];
function check(name, ok, info) { results.push({ name, ok: !!ok, info: info === undefined ? null : info }); }

const rules = JSON.parse(fs.readFileSync(path.join(HERE, 'rules.json'), 'utf8'));
const T = rules.readingText;

// ── ① 판독기 4벌 ───────────────────────────────────────────────────
const st = JSON.parse(JSON.stringify(rules));
st.plan.items = ['N-1', 'N-2'];
st.plan.nDelays = [300, 500];
// 고침4: 옛 시험(① · ⑤)은 ㉠ 꼴(n1Contrast before · N-1 화면 before/after)과 정해진 표본 수 1(임시값)로 돈다
//   — 정해진 수 1 이면 G4 「유효 < 정해진」 = 옛 「칸 유효 0 = 실패」 와 같은 뜻이라 옛 기대가 그대로 선다(samplesPerCell 값은 HQA 회신 뒤)
st.n1Contrast = 'before';
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
  fs.writeFileSync(sp, canon(sets[name], RULES_BY_PATH[rPath]).map((o) => JSON.stringify(o)).join('\n') + '\n', 'utf8');
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
  check('계획 칸 수 = 102(I 84 + N-1 10 + N-2 8) · a2 칸 = N-1 2(지연 300 · 500) · 다른 항목 a2 0 · 계획 표본(임시 3/5) 342', plan.cells.length === 102 && a2cells.length === 2 && a2cells.every((c) => c.item === 'N-1') && JSON.stringify(a2cells.map((c) => c.delay)) === '[300,500]' && sumSamples === 342, { cells: plan.cells.length, a2: a2cells.map((c) => c.delay), sumSamples });
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
  check('I-8 T3 = 「미측정 · 정의 후보 있음 · HQA 확인 대기」 · 계획에 없음 · 측정 코드 0', i8 && i8.text === '미측정 · 정의 후보 있음(11/7 작업지시서 L306 · 공백 상태 칸) · HQA 확인 대기' && !rules.plan.items.includes('I-8') && !Object.keys(Object.assign({}, require('./items/i-items.js'), require('./items/n-items.js'))).includes('I-8'));
  check('N-1 판정 글자 = 대조 무효 / 🔴 / 🟡', T['N-1'].contrastInvalid === '대조 무효' && T['N-1'].red === '🔴' && T['N-1'].yellow === '🟡');
  const pend = Object.keys(rules.hqaPending).filter((k) => !k.startsWith('_'));
  check('HQA 회신 대기 6칸 표지(T3 포함)', pend.length === 6 && pend.includes('T3') && pend.every((k) => String(rules.hqaPending[k].status).includes('HQA 회신 대기')), pend);
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
    const na = conf(rules); delete na.plan.notMeasured.find((x) => x.item === 'I-8').approval; check('관문 C2: T3 확정 + 측정 코드 0 + 결재 0 = 거부', !G.check(na).ok && G.check(na).problems.some((p) => p.why === 'no-code-or-approval'));
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
    check(`대조군 걸림: ${n}`, r.hits.length > 0, r.hits);
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
  if (bareL !== 7) hits.push(`L 이름 쓰임 ${bareL} ≠ 7(매개변수 3 · 넘김 3 · 선언 1)`);
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
  if (!/if \(screen === 'a2'\) \{\s*(?:\/\/[^\n]*\n\s*)?await page\.waitForTimeout\(L\.settleMs\(RULES, PROBE_DELAY\)\);\s*doc\.a2_check = await a2Struct\(ctx, out, L, J\);\s*\} else \{/.test(src)) hits.push('a2 갈래 = 정착 + a2Struct 부름 아님');
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
  if (!src.includes("const PROBE_SCREENS = RULES.plan.screens.concat(RULES.n1Contrast === 'a2' ? ['a2'] : []);")) hits.push('PROBE_SCREENS 줄 바뀜');
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
    check(n + ' → 빨강', s2 !== src && checkProbe(s2).length > 0, typeof add === 'function' && s2 === src ? '바꿈 자리 못 찾음' : null);
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
    check('G4-G6: 순서 표 = after → a2 → before(rules.plan.screenOrder)', JSON.stringify(rules.plan.screenOrder) === '["after","a2","before"]');
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

// ── 결과─────────────────────────────────────────────────────────────
function finish() {
  const fail = results.filter((r) => !r.ok);
  for (const r of results) process.stdout.write(`${r.ok ? 'OK  ' : 'FAIL'} ${r.name}${r.ok ? '' : ' :: ' + JSON.stringify(r.info)}\n`);
  process.stdout.write(JSON.stringify({ selftest: 'judge+static', total: results.length, fail: fail.length }) + '\n');
  fs.rmSync(tmp, { recursive: true, force: true });
  process.exitCode = fail.length ? 1 : 0;
}
asyncChecks().then(finish, (e) => { check('비동기 시험 예외', false, String(e && e.message ? e.message : e).slice(0, 200)); finish(); });
