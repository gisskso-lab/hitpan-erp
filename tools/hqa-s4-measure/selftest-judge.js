#!/usr/bin/env node
'use strict';
// 판독기 자체시험 + 측정 스크립트 정적 검사(설계 §4-4 · §4-5). 브라우저 · 앱 · 망 0 — node 만.
// ① 손 JSONL 4벌 → judge.js(명령줄 그대로) → 기대 판독과 대조
//    A after 회로 죽음 → 🔴 · B before 재현 0 → 대조 무효 · C 칸 표본 0 → 빨강 · D 정상 → 기록
// ② 계획 꼴(칸 수 · 파생 칸 · 미측정 글자 · 정본 글자) 대조
// ③ 정적 검사: 측정 스크립트에 금지 입력 0 — 대조군(일부러 심은 나쁜 원문)이 반드시 걸려야 검사가 살아 있다고 본다
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
  check('I-8 T3 = 「미측정 · 정의 없음」 · 계획에 없음', i8 && i8.text === '미측정 · 정의 없음' && !rules.plan.items.includes('I-8'));
  check('N-1 판정 글자 = 대조 무효 / 🔴 / 🟡', T['N-1'].contrastInvalid === '대조 무효' && T['N-1'].red === '🔴' && T['N-1'].yellow === '🟡');
  const pend = Object.keys(rules.hqaPending).filter((k) => !k.startsWith('_'));
  check('HQA 회신 대기 5칸 표지', pend.length === 5 && pend.every((k) => String(rules.hqaPending[k].status).includes('HQA 회신 대기')), pend);
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

// ── 결과─────────────────────────────────────────────────────────────
const fail = results.filter((r) => !r.ok);
for (const r of results) process.stdout.write(`${r.ok ? 'OK  ' : 'FAIL'} ${r.name}${r.ok ? '' : ' :: ' + JSON.stringify(r.info)}\n`);
process.stdout.write(JSON.stringify({ selftest: 'judge+static', total: results.length, fail: fail.length }) + '\n');
fs.rmSync(tmp, { recursive: true, force: true });
process.exitCode = fail.length ? 1 : 0;
