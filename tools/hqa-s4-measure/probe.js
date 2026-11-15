'use strict';
// [장치] 리허설 점검(probe) — 고침1 F10 (C5 · PM 결정) · 고침2 N1-a(설계 §12-1 (2) · §12-2) · 판정 무관
//   언제: 워크플로 M4 뒤 · MODE = rehearsal 일 때만(장전 런에서는 안 돈다)
//   하는 일(이것뿐):
//     ① NODE_PATH 로 playwright 가 풀리는지(참/거짓 · 판 · 걸린 ms)
//     ② /before · /after 각각 측정용 선택자(README §5 + rules.json device.sel 의 상태 칩 · 재연결 표시)의 존재 여부 · 찾는 데 걸린 ms
//     ③ (PM 결정) /after 첫 화면 그림 1장 — 입력 0 · 판독 0 · F6 검사(PNG 서명 · 텍스트 덩어리) 결과만 · 보류면 그림 파일 0
//     ④ 고침2: /before 만 · 지연 0 단만(프록시 없이 시험 앱 직접) — R1 줄 구조 읽기(입력 0)
//        rules.json device.probe.distinguish = true 인 리허설(리허설 3 한 번)만 더: R3 행 클릭 양성 대조 → R2 Tab 경로
//        → 구분 판정 K(judge.js kCheck · 글자 = rules.json kCheck) 를 k_check 에 기록
//   입력: /before 에서 #before 누름 + Tab + R3 행 클릭 1회 — 이 셋 뿐. /after 입력 0. 그 밖의 키 0 · 지연 단 0 만.
//         (정적 검사: selftest-judge.js 가 이 파일의 키 호출 · 지연 값을 본다 · 심은 위반 대조군 포함)
//   뺀 것(고침2 N2): 고침1 의 ③ Tab 도달(두 화면) · ④ 목록 열림 확인(두 화면) — 항목 결과와 같은 모양의 정보라서 뺐다
//   안 하는 일: 항목 판독 · 표본 · 판정 글자 · 그 밖의 그림 · 프록시 — 0
//   환경: HQA_OUT_DIR(필수) · HQA_CHROME_PATH(필수) · HQA_BASE_URL(선택 · 기본 rules.device host:appPort) · NODE_PATH
//   결과: <HQA_OUT_DIR>/probe.json · 종료 코드 0 = 파일 씀(점검 결과와 무관) · 1 = 파일 못 씀
const fs = require('fs');
const path = require('path');

const RULES = JSON.parse(fs.readFileSync(path.join(__dirname, 'rules.json'), 'utf8'));
const README_SEL = ['#before', '[data-row]', '#detail', '#rowclick-count', '#rowclicks', '#pick-count', '#picks', '#fixture-root', 'link[data-fixture-css]'];
const PROBE_DELAY = 0;
const errText = (e) => String(e && e.message ? e.message : e).split('\n')[0].slice(0, 160);

async function findSel(page, sel, timeout) {
  const t = Date.now();
  try {
    await page.locator(sel).first().waitFor({ state: 'attached', timeout });
    return { selector: sel, found: true, ms: Date.now() - t };
  } catch (e) {
    return { selector: sel, found: false, ms: Date.now() - t };
  }
}

async function pngCheck(page) {
  const res = { screen: 'after', input_before_shot: 0, taken: false, verdict: null, kinds: [], saved: null };
  try {
    const buf = await page.screenshot();
    res.taken = true;
    res.kinds = require('./leak-scan.js').pngKinds(buf);
    res.verdict = res.kinds.length ? 'withheld' : 'pass';
    if (res.verdict === 'pass') {
      fs.mkdirSync(path.join(process.env.HQA_OUT_DIR, 'shots'), { recursive: true });
      fs.writeFileSync(path.join(process.env.HQA_OUT_DIR, 'shots', 'probe.after.first.png'), buf);
      res.saved = 'shots/probe.after.first.png';
    }
  } catch (e) {
    res.verdict = 'error';
    res.error = errText(e);
  }
  return res;
}

// /before 구분 점검 — R1(입력 0) · R3(행 클릭 1회) · R2(#before 누름 + Tab). 값 = 태그 · role · 줄 번호 · 참거짓 · 수만.
async function distinguish(ctx, out, L, J) {
  const row = RULES.device.kbd.refRow;
  out.r1 = await L.readRowStruct(ctx, row);
  if (!RULES.device.probe.distinguish) return null;
  out.r3 = await L.rowClickControl(ctx, row);
  const t = Date.now();
  const reach = await L.tabTo(ctx, row);
  out.r2 = { reached: reach.reached, tabs: reach.tabs, ms: Date.now() - t, path: reach.path };
  const k = J.kCheck(out.r1, reach.path, out.r3);
  const spec = RULES.kCheck[k.verdict];
  return { screen: 'before', delay: PROBE_DELAY, verdict: k.verdict, meaning: spec.meaning, conds: k.conds, rule_ref: 'rules.json kCheck' };
}

async function probeScreen(browser, base, screen, L, J, doc) {
  const d = RULES.device;
  const cond = RULES.hqaPending.browserCondition.value;
  const out = { screen, delay: PROBE_DELAY, goto_ok: false, goto_ms: null, circuit_connected: false, selectors: [], errors: [] };
  const ctxB = await browser.newContext({ viewport: cond.viewport, deviceScaleFactor: cond.deviceScaleFactor, locale: cond.locale });
  const page = await ctxB.newPage();
  page.setDefaultTimeout(d.actionTimeoutMs);
  const sig = L.attachSignals(page, RULES);
  const ctx = { page, rules: RULES, inputs: [], sig, cell: { delay: PROBE_DELAY } };
  try {
    const t = Date.now();
    await page.goto(base + '/' + screen, { waitUntil: 'load', timeout: d.gotoTimeoutMs });
    out.goto_ok = true;
    out.goto_ms = Date.now() - t;
    const until = Date.now() + d.circuitWaitMs;
    while (sig.connectedAt === null && Date.now() < until) await page.waitForTimeout(d.pollMs);
    out.circuit_connected = sig.connectedAt !== null;
    for (const sel of README_SEL) out.selectors.push(Object.assign({ name: 'readme' }, await findSel(page, sel, d.actionTimeoutMs)));
    out.selectors.push(Object.assign({ name: 'statusChip' }, await findSel(page, d.sel.statusChip, d.actionTimeoutMs)));
    // 재연결 표시는 끊겼을 때만 생길 수 있다 — 없음은 기록일 뿐(판정 아님)
    out.selectors.push(Object.assign({ name: 'reconnect', note: 'may be absent unless disconnected' }, await findSel(page, d.sel.reconnect, 2000)));
    if (screen === 'after') out.png_check = await pngCheck(page);
    if (screen === 'before') {
      await page.waitForTimeout(L.settleMs(RULES, PROBE_DELAY));
      doc.k_check = await distinguish(ctx, out, L, J);
    }
  } catch (e) {
    out.errors.push(errText(e));
  } finally {
    sig.ending = true;
    out.inputs = ctx.inputs.map((x) => ({ k: x.k, key: x.key || null, selector: x.selector || null }));
    await ctxB.close().catch((e) => out.errors.push('close: ' + errText(e)));
  }
  return out;
}

async function main() {
  const outDir = process.env.HQA_OUT_DIR;
  const chromePath = process.env.HQA_CHROME_PATH;
  const doc = {
    stage: 'PROBE', mode: 'rehearsal', note: 'judgement-free probe - selector presence / time / require / before-only distinguish check (delay 0) - item reading 0',
    rulesVersion: RULES.rulesVersion, node_path_set: !!process.env.NODE_PATH,
    scope: { distinguish: RULES.device.probe.distinguish === true, screen: 'before', delay: PROBE_DELAY, inputs: '#before + Tab + R3 row click x1' },
    playwright_require: { resolved: false, version: null, ms: null }, browser_launched: false, screens: [], k_check: null, errors: [],
  };
  if (!outDir) { process.stderr.write('probe: HQA_OUT_DIR 필요\n'); return 1; }
  let pw = null;
  const t = Date.now();
  try {
    pw = require('playwright');
    doc.playwright_require = { resolved: true, version: require('playwright/package.json').version, ms: Date.now() - t };
  } catch (e) {
    doc.playwright_require.ms = Date.now() - t;
    doc.errors.push('require: ' + errText(e));
  }
  if (pw && chromePath) {
    const L = require('./items/_lib.js');
    const J = require('./judge.js');
    const base = process.env.HQA_BASE_URL || ('http://' + RULES.device.host + ':' + RULES.device.appPort);
    let browser = null;
    try {
      browser = await pw.chromium.launch({ executablePath: chromePath, headless: RULES.hqaPending.browserCondition.value.headless });
      doc.browser_launched = true;
      for (const s of RULES.plan.screens) doc.screens.push(await probeScreen(browser, base, s, L, J, doc));
    } catch (e) {
      doc.errors.push('browser: ' + errText(e));
    } finally {
      if (browser) await browser.close().catch((e) => doc.errors.push('close: ' + errText(e)));
    }
  } else if (!chromePath) {
    doc.errors.push('HQA_CHROME_PATH 없음');
  }
  try {
    fs.writeFileSync(path.join(outDir, 'probe.json'), JSON.stringify(doc, null, 2) + '\n');
  } catch (e) {
    process.stderr.write('probe: probe.json 쓰기 실패 ' + errText(e) + '\n');
    return 1;
  }
  process.stdout.write('[probe] 기록 끝 · playwright ' + doc.playwright_require.resolved + ' · 화면 ' + doc.screens.length + ' · K ' + (doc.k_check ? doc.k_check.verdict : '-') + ' · 오류 ' + doc.errors.length + '\n');
  return 0;
}

main().then((c) => { process.exitCode = c; }, (e) => { process.stderr.write('probe: ' + errText(e) + '\n'); process.exitCode = 1; });
