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
const win = defaultWindow(st);
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'hqa-s4-selftest-'));
const rulesPath = path.join(tmp, 'rules-selftest.json');
fs.writeFileSync(rulesPath, JSON.stringify(st, null, 2), 'utf8');

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

function runJudge(name) {
  const sp = path.join(tmp, `${name}.jsonl`);
  const op = path.join(tmp, `${name}.json`);
  fs.writeFileSync(sp, sets[name].join('\n') + '\n', 'utf8');
  const r = spawnSync(process.execPath, [path.join(HERE, 'judge.js'), '--rules', rulesPath, '--samples', sp, '--out', op], { encoding: 'utf8' });
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
  check('C: 그 칸 = 장치 실패 · /before 표본 0', cell && cell.status === T.cellFail && cell.reason === T.beforeZeroReason, cell);
  check('C: 「대조 무효」 와 구별(무효 글자 안 나옴)', n && !n.labels.includes(T['N-1'].contrastInvalid) && n.reading === T.cellFail, n && n.reading);
}
{
  const { code, v } = runJudge('C2_칸표본0_after');
  const cell = v && v.cells.find((c) => c.item === 'N-1' && c.screen === 'after' && c.delay === 500);
  check('C2: after 칸 표본 0 → 빨강 · 칸 표본 0', v && v.red === true && code === 1 && cell && cell.reason === T.cellZeroReason, { code, cell });
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
  check('계획 칸 수 = 100(I 84 + N-1 8 + N-2 8)', plan.cells.length === 100, plan.cells.length);
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
    const conf = (r) => { const c = JSON.parse(JSON.stringify(r)); for (const k of G.REQUIRED) { c.hqaPending[k].status = 'HQA 회신 반영 · 댓글 1234567890'; if (c.hqaPending[k].value === null) c.hqaPending[k].value = 'x'; } return c; };
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
  base.circuit = { dead: false, reconnectVisible: false, wsClosedNotNav: false, consoleCircuitError: false };
  if (ur) base.deviceReading = base.valid ? 'keyboard_unreachable' : 'device_failure';
  return JSON.stringify(Object.assign(c, base));
}
// 한 묶음: N-1 · N-2 를 같은 꼴로(파생 칸은 부모 kbd 를 물려받는다)
const pair = (screen, delay, o) => [kline('N-1', screen, delay, o), kline('N-2', screen, delay, o)];
const KSETS = {
  'N1e1_before미도달_U충족': [].concat(pair('before', 300, { ur: true }), pair('before', 500, { ur: true }), pair('after', 300), pair('after', 500)),
  'N1e2_before미도달_U2결손': [].concat(pair('before', 300, { ur: true, path: PATH_BODY, valid: false }), pair('before', 500, { ur: true, path: PATH_BODY, valid: false }), pair('after', 300), pair('after', 500)),
  'N1e3_before미도달_U3결손': [].concat(pair('before', 300, { ur: true, valid: false }), pair('before', 500, { ur: true, valid: false }), pair('after', 300, { ur: true, valid: false }), pair('after', 500, { ur: true, valid: false })),
  'N1e4_after미도달_빠짐': [].concat(pair('before', 300), pair('before', 500), pair('after', 300, { ur: true, valid: false }), pair('after', 500)),
  'N1e5_after미도달1_나머지충족': [].concat(pair('before', 300), pair('before', 500), pair('after', 300, { ur: true, valid: false }), pair('after', 300, { no: 2 }), pair('after', 500)),
  'N1e6_유효주장_U결손': [].concat(pair('before', 300, { ur: true, rcOk: false, valid: true }), pair('before', 500, { ur: true }), pair('after', 300), pair('after', 500))
};
for (const k of Object.keys(KSETS)) sets[k] = KSETS[k];
const cellOf = (v, item, screen, delay) => v && v.cells.find((c) => c.item === item && c.screen === screen && c.delay === delay);
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
async function mockRun(itemFn, cell, reachAt) {
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
  const ctx = { page, sig, rules: r, cell, sampleNo: 1, outDir: tmp, inputs: [], inputAt: Date.now() };
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
}

// ── ⑧ 고침2 N1-a probe 정적 검사(C1-4) + 심은 위반 대조군 ──────────────────────────
function checkProbe(src) {
  const hits = [];
  for (const r of GLOBAL_BAN) if (r.test(src)) hits.push(`금지 ${r}`);
  if (evalBodies(src).length) hits.push('evaluate 는 items/_lib.js 에만');
  if (/\.keyboard\b|\.mouse\b/.test(src)) hits.push('page.keyboard/mouse 직접 사용');
  const QK = new RegExp('([\'"`])(?:Enter|NumpadEnter|Escape|Esc|Space|Spacebar| |Arrow(?:Up|Down|Left|Right)|Home|End|PageUp|PageDown)\\1');
  if (QK.test(src)) hits.push('금지 키 글자');
  for (const m of src.matchAll(/\b(press|down|up|type|insertText|sendCharacter)\s*\(([^)]*)\)/g)) {
    if (!/^(?:ctx\s*,\s*)?(['"`])Tab\1$/.test(m[2].trim())) hits.push(`키 호출 ${m[1]} 인자 Tab 아님`);
  }
  for (const m of src.matchAll(/\bPROBE_DELAY\s*=\s*([^;\n]+)/g)) if (m[1].trim() !== '0') hits.push('지연 값 0 아님');
  for (const m of src.matchAll(/\bdelay\s*:\s*([A-Za-z0-9_.]+)/g)) if (m[1] !== '0' && m[1] !== 'PROBE_DELAY') hits.push(`지연 값 0 아님 ${m[1]}`);
  if (/proxyPort|delay-proxy/.test(src)) hits.push('프록시 사용');
  const ALLOWED_L = new Set(['readRowStruct', 'rowClickControl', 'tabTo', 'attachSignals', 'settleMs']);
  for (const m of src.matchAll(/\bL\.(\w+)\s*\(/g)) if (!ALLOWED_L.has(m[1])) hits.push(`L.${m[1]} 허용 목록 밖`);
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
    ['probe 대조군: 지연 300', "\nconst c2 = { delay: 300 };\n"]
  ];
  for (const [n, add] of plantedP) check(n + ' → 빨강', checkProbe(src + add).length > 0);
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
