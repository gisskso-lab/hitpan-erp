'use strict';
// [장치] 리허설 점검(probe) — 고침1 F10 (C5 · PM 결정) · 판정 무관
//   언제: 워크플로 M4 뒤 · MODE = rehearsal 일 때만(장전 런에서는 안 돈다)
//   하는 일(이것뿐):
//     ① NODE_PATH 로 playwright 가 풀리는지(참/거짓 · 판 · 걸린 ms)
//     ② /before · /after 각각 측정용 선택자(README §5 + rules.json device.sel 의 상태 칩 · 열린 목록 · 재연결 표시)의
//        존재 여부 · 찾는 데 걸린 ms
//     ③ Tab 으로 첫 줄 상태 단추 도달 여부 · Tab 수 · ms(측정 코드와 같은 함수 items/_lib.js tabTo)
//     ④ 첫 줄에서 Enter 뒤 열린 목록 선택자가 나타나는지 · ms → Escape
//   안 하는 일: 항목 판독 · 표본 · 판정 글자 · 회로 신호 기록 · 그림 · 프록시 — 0
//   이 결과는 「선택자가 맞나」 만 본다. 항목 결과를 보지 않으므로 설계 §6 의 「발행 전 고정 · 결과 본 뒤 변경 = 새 회차」 와 충돌 없음.
//   환경: HQA_OUT_DIR(필수) · HQA_CHROME_PATH(필수) · HQA_BASE_URL(선택 · 기본 rules.device host:appPort) · NODE_PATH
//   결과: <HQA_OUT_DIR>/probe.json · 종료 코드 0 = 파일 씀(점검 결과와 무관) · 1 = 파일 못 씀
const fs = require('fs');
const path = require('path');

const RULES = JSON.parse(fs.readFileSync(path.join(__dirname, 'rules.json'), 'utf8'));
const README_SEL = ['#before', '[data-row]', '#detail', '#rowclick-count', '#rowclicks', '#pick-count', '#picks', '#fixture-root', 'link[data-fixture-css]'];
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

async function probeScreen(browser, base, screen, L) {
  const d = RULES.device;
  const cond = RULES.hqaPending.browserCondition.value;
  const out = { screen, goto_ok: false, goto_ms: null, selectors: [], tab_first_row: null, open_list_after_enter: null, errors: [] };
  const ctxB = await browser.newContext({ viewport: cond.viewport, deviceScaleFactor: cond.deviceScaleFactor, locale: cond.locale });
  const page = await ctxB.newPage();
  page.setDefaultTimeout(d.actionTimeoutMs);
  try {
    const t = Date.now();
    await page.goto(base + '/' + screen, { waitUntil: 'load', timeout: d.gotoTimeoutMs });
    out.goto_ok = true;
    out.goto_ms = Date.now() - t;
    for (const sel of README_SEL) out.selectors.push(Object.assign({ name: 'readme' }, await findSel(page, sel, d.actionTimeoutMs)));
    out.selectors.push(Object.assign({ name: 'statusChip' }, await findSel(page, d.sel.statusChip, d.actionTimeoutMs)));
    // 재연결 표시는 끊겼을 때만 생길 수 있다 — 없음은 기록일 뿐(판정 아님)
    out.selectors.push(Object.assign({ name: 'reconnect', note: 'may be absent unless disconnected' }, await findSel(page, d.sel.reconnect, 2000)));
    const ctx = { page, rules: RULES, inputs: [], sig: { t0: Date.now() }, cell: { delay: 0 } };
    const t2 = Date.now();
    try {
      const r = await L.tabTo(ctx, 1);
      out.tab_first_row = { reached: true, tabs: r.tabs, ms: Date.now() - t2 };
    } catch (e) {
      out.tab_first_row = { reached: false, tabs: null, ms: Date.now() - t2 };
      out.errors.push('tab: ' + errText(e));
    }
    if (out.tab_first_row.reached) {
      await page.keyboard.press('Enter');
      const ol = await findSel(page, d.sel.openList, d.openWaitMs);
      out.open_list_after_enter = { selector: d.sel.openList, found: ol.found, ms: ol.ms };
      await page.keyboard.press('Escape');
    }
  } catch (e) {
    out.errors.push(errText(e));
  } finally {
    await ctxB.close().catch((e) => out.errors.push('close: ' + errText(e)));
  }
  return out;
}

async function main() {
  const outDir = process.env.HQA_OUT_DIR;
  const chromePath = process.env.HQA_CHROME_PATH;
  const doc = {
    stage: 'PROBE', mode: 'rehearsal', note: 'judgement-free probe - selector presence / time / Tab reach / require only - item reading 0',
    rulesVersion: RULES.rulesVersion, node_path_set: !!process.env.NODE_PATH,
    playwright_require: { resolved: false, version: null, ms: null }, browser_launched: false, screens: [], errors: [],
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
    const base = process.env.HQA_BASE_URL || ('http://' + RULES.device.host + ':' + RULES.device.appPort);
    let browser = null;
    try {
      browser = await pw.chromium.launch({ executablePath: chromePath, headless: RULES.hqaPending.browserCondition.value.headless });
      doc.browser_launched = true;
      for (const s of RULES.plan.screens) doc.screens.push(await probeScreen(browser, base, s, L));
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
  process.stdout.write('[probe] 기록 끝 · playwright ' + doc.playwright_require.resolved + ' · 화면 ' + doc.screens.length + ' · 오류 ' + doc.errors.length + '\n');
  return 0;
}

main().then((c) => { process.exitCode = c; }, (e) => { process.stderr.write('probe: ' + errText(e) + '\n'); process.exitCode = 1; });
