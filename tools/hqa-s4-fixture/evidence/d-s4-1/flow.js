// node flow.js <proxyPort> <delay> <reps> <out.json>
// per run: load page -> open status cell (row 1) -> Esc -> open other cell (row 5) -> click outside (#blank)
const { chromium } = require('playwright');
const fs = require('fs');
const [PORT, D, REPS, OUT] = [process.argv[2], +process.argv[3], +(process.argv[4] || 5), process.argv[5] || 'flow.json'];
const W = (ms) => new Promise((r) => setTimeout(r, ms));
(async () => {
  const b = await chromium.launch({ channel: 'msedge' });
  const res = [];
  for (const v of ['before', 'after']) {
    for (let i = 0; i < REPS; i++) {
      const ctx = await b.newContext({ viewport: { width: 1600, height: 900 } });
      const p = await ctx.newPage();
      const errs = [];
      p.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 160)); });
      p.on('requestfailed', (r) => errs.push('reqfailed ' + r.url() + ' ' + (r.failure() || {}).errorText));
      p.on('response', (r) => { if (r.status() >= 400) errs.push(r.status() + ' ' + r.url()); });
      const t0 = Date.now();
      await p.goto(`http://127.0.0.1:${PORT}/${v}`, { waitUntil: 'load' }); const tLoad = Date.now() - t0;
      await p.waitForFunction(() => window.Blazor && document.querySelector('#fixture-root'));
      await W(2000 + D * 4);
      const isOpen = async () => (await p.locator('.mud-popover-open .mud-menu-item').count()) > 0;
      const opener = (row) => v === 'after' ? p.locator(`[data-row="${row}"] button.mud-menu-button-activator`) : p.locator(`[data-row="${row}"] span.bo-status-chip`).first();
      const waitOpen = async () => { const s = Date.now(); while (!(await isOpen()) && Date.now() - s < 1500 + D * 4) await W(25); return await isOpen(); };
      await opener(1).click(); const open1 = await waitOpen(); await W(300);
      await p.keyboard.press('Escape'); await W(500 + D * 3); const closedAfterEsc = !(await isOpen());
      await opener(5).click(); const open5 = await waitOpen(); await W(300);
      await p.locator('#blank').click(); await W(500 + D * 3); const closedAfterOutside = !(await isOpen());
      await ctx.close();
      res.push({ v, run: i + 1, tLoad, open1, closedAfterEsc, open5, closedAfterOutside, ms: Date.now() - t0, errs });
      console.log(JSON.stringify(res[res.length - 1]));
      await W(D * 2 + 500);
    }
  }
  await b.close();
  fs.writeFileSync(OUT, JSON.stringify(res, null, 1));
})().catch((e) => { console.error(e); process.exit(1); });
