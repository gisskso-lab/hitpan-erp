#!/usr/bin/env node
'use strict';
// 항목 측정 실행기(설계 §4-1 M5 · M6) — 계획 칸(rules.json plan)을 돌며 표본마다 새 브라우저 문맥으로 잰다.
// · 입력 = page.keyboard · page.mouse · locator 동작만(items/_lib.js) · 시험 앱 스크립트 주입 0.
// · 지연 단마다 시험 앱 나무의 proxy/delay-proxy.js 를 사본 없이 그대로 다시 띄운다(인자값은 표본마다 기록).
// · 표본 오류는 그 표본 errors 칸 · 계속. 판독은 judge.js.
// · 고침4 G1: 화면 이름 ↔ 주소 ↔ 기대 data-variant ↔ 기대 CSS 판 = rules.plan.screenTable(judge.js screenUrl · screenCheck) — 주소 글자 0 · 앞머리 판단 0
// · 고침4 G6: 같은 지연 단 안 순서 = rules.plan.screenOrder · 표본마다 order · contextId
//
// 쓰는 법:
//   node run-items.js --plan     계획 칸 수만 출력(브라우저 · 앱 0) — 리허설 · 자체시험용
//   node run-items.js            MODE 를 읽어 rehearsal 이면 항목 0 으로 끝 · armed:… 이면 측정
// 환경 변수:
//   HQA_OUT_DIR      (필수) 출력 폴더 — samples.jsonl · shots/*.png 를 쓴다
//   HQA_CHROME_PATH  (필수) 릴리스 크롬 실행 파일 경로(executablePath) · 브라우저 내려받기 0
//   HQA_FIXTURE_DIR  (선택) 시험 앱 폴더 · 기본 = 이 폴더의 ../hqa-s4-fixture
// 전제: 시험 앱이 127.0.0.1:5799 에 떠 있음(갈래 A boot.sh · M4).
// 종료 코드: 0 = 끝(rehearsal 항목 0 포함 · 표본 오류가 있어도 0 — 빨강 여부는 judge.js)
//           2 = MODE · 환경 변수 · 규칙 오류 · 3 = Playwright/브라우저 판 불일치 · 실행 실패
//           4 = HQA 회신 대기 칸이 남아 장전 측정 거부(작업지시서 §9 P3)
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const { buildPlan, kbdGate, groupKey, screenSpec, screenUrl, screenCheck, screenRankOf, recordOnlyTag, ATTEMPT_KIND } = require('./judge.js');
const L = require('./items/_lib.js');
const ITEMS = Object.assign({}, require('./items/i-items.js'), require('./items/n-items.js'));

function loadRules() {
  return JSON.parse(fs.readFileSync(path.join(__dirname, 'rules.json'), 'utf8'));
}

function readMode() {
  const p = path.join(__dirname, 'MODE');
  if (!fs.existsSync(p)) return null;
  return fs.readFileSync(p, 'utf8').trim();
}

function pendingKeys(rules) {
  // 고침1 F8: M0 와 같은 관문(필수 칸 목록 고정 · 확정 꼴만) — 키 삭제 · 다른 글자로 우회 못 함
  return require('./hqa-gate.js').check(rules, { itemsHave: Object.keys(ITEMS) }).problems.map((p) => p.key + ':' + p.why);
}

// 고침5 H1-11(결-2 乙): 「지연 N」 = 왕복 N ms → delay-proxy.js 둘째 인자(방향당 ms) = N/2
//   ~~옛 꼴(per-direction-ms · 인자 = N 그대로)은 delayUnit 값이 per-direction-ms 일 때만 남는다(되돌림용 · 지금 rules = round-trip-ms)~~ (고침6 11/16 정정)
// 고침6 결-1(PM 결재 11/16 · 대표 결정 ①): 지금 rules = per-direction-ms(HQA 집행 단위 · #850 댓글 6709803008) → 인자 = N 그대로 · 왕복 약 2N.
//   round-trip-ms 갈래는 고침5 시절 값(rules _옛값_고침5)의 되돌림용으로 남는다. 아래 함수 코드는 무변(두 단위 갈래 모두 그대로).
function proxyDelayArg(rules, delay) {
  const unit = rules.hqaPending.delayUnit.value;
  if (unit === 'round-trip-ms') return { unit, roundTripMs: delay, perDirectionMs: delay / 2 };
  if (unit === 'per-direction-ms') return { unit, roundTripMs: delay * 2, perDirectionMs: delay };
  throw new Error(`delayUnit 값 목록 밖: ${unit}`);
}

function startProxy(rules, fixtureDir, delay) {
  const d = rules.device;
  const script = path.join(fixtureDir, 'proxy', 'delay-proxy.js');
  const args = [String(d.proxyPort), String(proxyDelayArg(rules, delay).perDirectionMs), String(d.appPort), d.host];
  return new Promise((resolve) => {
    if (!fs.existsSync(script)) { resolve({ ok: false, args, error: '프록시 파일 없음(시험 앱 나무 proxy/delay-proxy.js)' }); return; }
    const child = spawn(process.execPath, [script].concat(args), { stdio: ['ignore', 'pipe', 'pipe'] });
    let done = false;
    let errText = '';
    const finish = (r) => { if (!done) { done = true; clearTimeout(timer); resolve(r); } };
    const timer = setTimeout(() => finish({ ok: false, child, args, error: `프록시 ${d.proxyStartTimeoutMs}ms 안 안 뜸 ${errText.slice(0, 200)}` }), d.proxyStartTimeoutMs);
    child.stdout.on('data', (b) => { if (String(b).includes('listen ')) finish({ ok: true, child, args }); });
    child.stderr.on('data', (b) => { errText += String(b); });
    child.on('error', (e) => finish({ ok: false, child, args, error: `프록시 실행 오류 ${L.errText(e)}` }));
    child.on('exit', (code) => finish({ ok: false, child: null, args, error: `프록시 일찍 끝남 code=${code} ${errText.slice(0, 200)}` }));
  });
}

function stopProxy(p) {
  return new Promise((resolve) => {
    if (!p || !p.child || p.child.exitCode !== null) { resolve(); return; }
    const t = setTimeout(resolve, 3000);
    p.child.once('exit', () => { clearTimeout(t); resolve(); });
    p.child.kill();
  });
}

// 고침4 G1 · G6: 표본마다 화면 이름(screen) · 주소(screenPath · 표 값) · 실측 판(variantMeasured · cssMeasured) · 전체 순번(order) · 문맥 순번(contextId)
function baseRecord(cell, sampleNo, meta) {
  return {
    item: cell.item, case: cell.case, screen: cell.screen, screenPath: meta.screenPathOf(cell.screen), variantMeasured: null, cssMeasured: null,
    delay: cell.delay, window: cell.window, cell: cell.key,
    sampleNo, order: null, contextId: null, ts: new Date().toISOString(), inputs: [], readings: null, circuit: null, shot: null,
    errors: [], valid: false, kbd: null, deviceReading: null, proxy: meta.proxy, playwrightVersion: meta.playwrightVersion, browserVersion: meta.browserVersion,
    rulesVersion: meta.rulesVersion
  };
}

// 고침2 N1-c: 키보드 미도달 표본의 유효 여부 — judge.js kbdGate(U1~U4) 와 같은 함수 · U3 = 같은 런 같은 칸 /after 도달 수
function applyKbd(rules, rec, kbd, afterReach) {
  rec.kbd = kbd || null;
  if (!kbd || kbd.reached !== false) { rec.valid = true; return; }
  const g = kbdGate(rules, rec, afterReach[groupKey(rec)] || 0);
  rec.kbd.u = g.u;
  rec.valid = g.valid;
  rec.deviceReading = g.reading;
  if (!g.valid) rec.errors.push(g.reason);
}

async function runSample(browser, rules, plan, cell, sampleNo, meta, outDir, afterReach, nextContextId) {
  const d = rules.device;
  const cond = rules.hqaPending.browserCondition.value;
  const rec = baseRecord(cell, sampleNo, meta);
  // 고침4 보탬(PM F-6 ③): 「기록만」 칸 표본에 표지(그 칸의 장치 실패 = 장치 건강 · 판정 무관) — judge.js recordOnlyTag 와 같은 함수
  const ro = recordOnlyTag(rules, cell);
  if (ro) rec.recordOnly = ro;
  const derivedCells = plan.derived[cell.key] || [];
  const extraRecs = {};
  let context = null;
  let sig = null;
  try {
    context = await browser.newContext({
      viewport: { width: cell.window.w, height: cell.window.h },
      deviceScaleFactor: cond.deviceScaleFactor,
      locale: cond.locale
    });
    rec.contextId = nextContextId(); // 고침4 G6: 표본마다 새 문맥 · 문맥 순번
    context.setDefaultTimeout(d.actionTimeoutMs);
    const page = await context.newPage();
    sig = L.attachSignals(page, rules);
    // 고침4 G1: derivedItems = 이 칸에 붙은 파생 항목(N-1 의 a2 칸엔 N-2 없음 → n-items 가 N-2 입력을 안 보냄)
    const ctx = { page, sig, rules, cell, sampleNo, outDir, inputs: rec.inputs, inputAt: null, derivedItems: derivedCells.map((c) => c.item) };
    // 고침4 G1: 주소 = rules.plan.screenTable 값(옛 꼴 = `/${화면 이름}` · 화면 이름 = 주소 가정 대체)
    const url = screenUrl(rules, cell.screen);
    await page.goto(url, { waitUntil: 'load', timeout: d.gotoTimeoutMs + 4 * cell.delay });
    const until = Date.now() + d.circuitWaitMs + 4 * cell.delay;
    while (sig.connectedAt === null && Date.now() < until) await page.waitForTimeout(d.pollMs);
    if (sig.connectedAt === null) throw new Error('회로(웹소켓 프레임) 연결 못 봄');
    await page.waitForTimeout(L.settleMs(rules, cell.delay));
    const st0 = await L.readState(ctx);
    // 고침4 G1(C-3): 화면 확정 = data-variant 가 표 값과 같을 때만(다르면 throw) · CSS 판은 표 값과 다르면 오류 칸
    //   (옛 꼴 = data-variant · data-fixture-css 둘 다 「화면 이름」 과 비교 — a2 는 CSS 가 after 라 표로 바꿈)
    rec.variantMeasured = st0.variant;
    rec.cssMeasured = st0.cssVariant;
    const sc = screenCheck(rules, cell.screen, st0);
    for (const e of sc.errors) rec.errors.push(e);
    const pre = L.circuitSince(ctx, sig.t0);
    if (pre.dead) throw new Error('입력 전 회로 죽음 신호');
    ctx.inputAt = Date.now();
    const outcomes = await ITEMS[cell.item](ctx);
    const own = outcomes.find((o) => !o.item || o.item === cell.item);
    rec.readings = own.readings;
    rec.circuit = own.circuit;
    rec.shot = own.shot || null;
    applyKbd(rules, rec, own.kbd, afterReach);
    for (const o of outcomes) {
      if (!o.item || o.item === cell.item) continue;
      const dc = derivedCells.find((c) => c.item === o.item);
      if (!dc) { rec.errors.push(`계획 밖 파생 결과 ${o.item}`); continue; }
      const r = baseRecord(dc, sampleNo, meta);
      r.readings = o.readings;
      r.circuit = o.circuit;
      r.shot = o.shot || null;
      applyKbd(rules, r, o.kbd, afterReach);
      r.parentCell = cell.key;
      r.inputs = rec.inputs;
      r.contextId = rec.contextId; // 같은 문맥 이어서
      r.variantMeasured = rec.variantMeasured;
      r.cssMeasured = rec.cssMeasured;
      extraRecs[dc.key] = r;
    }
  } catch (e) {
    rec.errors.push(L.errText(e));
  } finally {
    if (sig) sig.ending = true;
    if (context) {
      try { await context.close(); } catch (e) { rec.errors.push(`문맥 닫기 오류 ${L.errText(e)}`); }
    }
  }
  const out = [rec];
  for (const dc of derivedCells) {
    if (extraRecs[dc.key]) { out.push(extraRecs[dc.key]); continue; }
    const r = baseRecord(dc, sampleNo, meta);
    r.parentCell = cell.key;
    r.contextId = rec.contextId;
    r.errors.push(`앞 표본(${cell.item}) 실패로 못 잼`);
    out.push(r);
  }
  return out;
}

async function main(argv) {
  let rules;
  let plan;
  try {
    rules = loadRules();
    plan = buildPlan(rules);
  } catch (e) {
    process.stderr.write(`run-items: 규칙 오류 ${L.errText(e)}\n`);
    return 2;
  }
  const own = plan.cells.filter((c) => !c.derivedFrom);
  const planned = plan.cells.reduce((a, c) => a + c.samples, 0);
  if (argv.includes('--plan')) {
    process.stdout.write(JSON.stringify({ rulesVersion: rules.rulesVersion, cells: plan.cells.length, ownCells: own.length, plannedSamples: planned, hqaPendingOpen: pendingKeys(rules) }) + '\n');
    return 0;
  }
  const mode = readMode();
  if (mode === null) { process.stderr.write('run-items: MODE 파일 없음\n'); return 2; }
  if (mode.startsWith('rehearsal')) {
    process.stdout.write('run-items: MODE=rehearsal — 항목 측정 0 (설계 §4-1 · M4 뒤 끝)\n');
    return 0;
  }
  if (!mode.startsWith('armed:')) { process.stderr.write('run-items: MODE 꼴 모름\n'); return 2; }
  const pending = pendingKeys(rules);
  if (pending.length) {
    process.stderr.write(`run-items: HQA 회신 대기 칸 남음(${pending.join(', ')}) — 장전 측정 거부(작업지시서 §9 P3)\n`);
    return 4;
  }
  const outDir = process.env.HQA_OUT_DIR;
  const chromePath = process.env.HQA_CHROME_PATH;
  const fixtureDir = process.env.HQA_FIXTURE_DIR || path.resolve(__dirname, '..', 'hqa-s4-fixture');
  if (!outDir || !chromePath) { process.stderr.write('run-items: HQA_OUT_DIR · HQA_CHROME_PATH 필요\n'); return 2; }

  let pw;
  let pwVersion = null;
  try {
    pw = require('playwright');
    pwVersion = require('playwright/package.json').version;
  } catch (e) {
    process.stderr.write(`run-items: playwright 불러오기 실패 ${L.errText(e)}\n`);
    return 3;
  }
  if (pwVersion !== rules.device.playwrightVersion) {
    process.stderr.write(`run-items: playwright 판 ${pwVersion} ≠ ${rules.device.playwrightVersion}\n`);
    return 3;
  }
  const cond = rules.hqaPending.browserCondition.value;
  let browser;
  try {
    browser = await pw.chromium.launch({ executablePath: chromePath, headless: cond.headless });
  } catch (e) {
    process.stderr.write(`run-items: 브라우저 실행 실패 ${L.errText(e)}\n`);
    return 3;
  }
  const bver = browser.version();
  if (bver !== rules.device.expectedBrowserVersion) {
    process.stderr.write(`run-items: 브라우저 판 ${bver} ≠ ${rules.device.expectedBrowserVersion}\n`);
    await browser.close();
    return 3;
  }

  fs.mkdirSync(path.join(outDir, 'shots'), { recursive: true });
  const samplesPath = path.join(outDir, 'samples.jsonl');
  let order = 0;
  // 고침4 G6: 원자료 각 표본에 order(전체 순번 · 쓰는 순서) — judge.js orderCheck 가 지연 단 · 화면 순서를 다시 잰다
  const write = (r) => { order += 1; r.order = order; fs.appendFileSync(samplesPath, JSON.stringify(r) + '\n', 'utf8'); };
  // 고침5-B ⓐ(PM 결정 11/16): 시도 기록 줄 — 칸마다 표본보다 먼저 한 줄(order 없음 · 판독기가 표본에서 뗀다). 시도 있음 + 표본 0 = 장치 실패만 · 시도 0 = 미측정
  const writeAttempt = (c, parent) => {
    const a = { kind: ATTEMPT_KIND, item: c.item, case: c.case, screen: c.screen, delay: c.delay, window: c.window, cell: c.key, planned: c.samples, ts: new Date().toISOString(), rulesVersion: rules.rulesVersion };
    if (parent) a.parentCell = parent.key;
    fs.appendFileSync(samplesPath, JSON.stringify(a) + '\n', 'utf8');
  };
  let ctxSeq = 0;
  const nextContextId = () => { ctxSeq += 1; return ctxSeq; };
  let count = 0;
  let valid = 0;
  const delays = Array.from(new Set(own.map((c) => c.delay))).sort((a, b) => a - b);
  // 고침2 N1-c: 같은 지연 단 안에서 /after 칸을 먼저 잰다 — /before 미도달 표본의 U3(같은 칸 /after 도달 ≥ 1)를 쓸 때 알 수 있게
  // 고침4 G6(L-5): 순서를 rules.plan.screenOrder 에 고정(after → a2 → before) — 옛 꼴(after 먼저 · 나머지 뒤) 대체
  const screenRank = (c) => screenRankOf(rules, c.screen);
  const afterReach = {};
  try {
    for (const delay of delays) {
      const proxy = await startProxy(rules, fixtureDir, delay);
      const meta = {
        proxy: Object.assign({ script: 'tools/hqa-s4-fixture/proxy/delay-proxy.js', args: proxy.args, unit: rules.hqaPending.delayUnit.value, started: proxy.ok }, proxyDelayArg(rules, delay)),
        playwrightVersion: pwVersion, browserVersion: bver, rulesVersion: rules.rulesVersion,
        screenPathOf: (sc) => screenSpec(rules, sc).path
      };
      for (const cell of own.filter((c) => c.delay === delay).sort((a, b) => screenRank(a) - screenRank(b))) {
        writeAttempt(cell, null);
        for (const dc of (plan.derived[cell.key] || [])) writeAttempt(dc, cell);
        for (let n = 1; n <= cell.samples; n++) {
          let recs;
          if (!proxy.ok) {
            recs = [baseRecord(cell, n, meta)].concat((plan.derived[cell.key] || []).map((dc) => baseRecord(dc, n, meta)));
            for (const r of recs) r.errors.push(`지연 단 실패: ${proxy.error}`);
          } else {
            recs = await runSample(browser, rules, plan, cell, n, meta, outDir, afterReach, nextContextId);
          }
          for (const r of recs) {
            if (r.screen === 'after' && r.kbd && r.kbd.reached === true) afterReach[groupKey(r)] = (afterReach[groupKey(r)] || 0) + 1;
            write(r); count += 1; if (r.valid) valid += 1;
          }
        }
      }
      await stopProxy(proxy);
    }
  } finally {
    await browser.close();
  }
  process.stdout.write(JSON.stringify({ samples: count, valid, invalid: count - valid, plannedSamples: planned }) + '\n');
  return 0;
}

main(process.argv.slice(2)).then((code) => { process.exitCode = code; }, (e) => {
  process.stderr.write(`run-items: 예외 ${L.errText(e)}\n`);
  process.exitCode = 3;
});
