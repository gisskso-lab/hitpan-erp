// Control test for delay proxy: old vs new x delay 0/150/300 x cases a..e
// usage: node control-test.js <oldProxy> <newProxy> <listenPort> <out.json>
const net = require('net');
const crypto = require('crypto');
const { spawn } = require('child_process');
const fs = require('fs');

const [OLD, NEW, LPORT, OUT] = [process.argv[2], process.argv[3], Number(process.argv[4] || 5952), process.argv[5] || 'result.json'];
const DELAYS = [0, 150, 300];
const W = (ms) => new Promise((r) => setTimeout(r, ms));
const sha = (b) => crypto.createHash('sha256').update(b).digest('hex');

const A_PAYLOAD = Buffer.from('abcdefghijklmnopq'); // 17 bytes
const B_PAYLOAD = crypto.randomBytes(1024 * 1024);
const C_RESP_BODY = crypto.randomBytes(256 * 1024);

let mode = 'a';
let cServerGotReq = null;
function startFake() {
  return new Promise((res) => {
    const srv = net.createServer({ allowHalfOpen: true }, (s) => {
      s.on('error', () => {});
      const m = mode;
      if (m === 'a') { s.write(A_PAYLOAD); s.end(); }
      else if (m === 'b') { s.write(B_PAYLOAD); s.end(); }
      else if (m === 'c') {
        const chunks = [];
        s.on('data', (d) => chunks.push(d));
        s.on('end', () => {
          const req = Buffer.concat(chunks);
          cServerGotReq = req;
          s.write(Buffer.from(sha(req) + '\n'));
          s.write(C_RESP_BODY);
          s.end();
        });
      } else if (m === 'e') {
        let buf = '';
        s.on('data', (d) => {
          buf += d.toString();
          let i;
          while ((i = buf.indexOf('\n')) >= 0) {
            const line = buf.slice(0, i); buf = buf.slice(i + 1);
            s.write('s:' + line.slice(2) + '\n');
          }
        });
        s.on('end', () => s.end());
      }
    });
    srv.listen(0, '127.0.0.1', () => res(srv));
  });
}

function startProxy(file, delay, target) {
  return new Promise((res, rej) => {
    const p = spawn(process.execPath, [file, String(LPORT), String(delay), String(target)], { stdio: ['ignore', 'pipe', 'pipe'] });
    let ok = false;
    p.stdout.on('data', (d) => { if (!ok && /listen/.test(d.toString())) { ok = true; res(p); } });
    p.stderr.on('data', (d) => process.stderr.write('[proxy err] ' + d));
    p.on('exit', (c) => { if (!ok) rej(new Error('proxy exited ' + c)); });
    setTimeout(() => { if (!ok) rej(new Error('proxy start timeout')); }, 5000);
  });
}
const stopProxy = (p) => new Promise((r) => { if (p.exitCode !== null) return r(); p.on('exit', () => r()); p.kill(); });

function collect(opts) {
  // connect, optionally send+end, collect until close or timeout
  return new Promise((res) => {
    const t0 = Date.now();
    const chunks = [];
    const c = net.connect({ port: LPORT, host: '127.0.0.1' });
    let done = false;
    const fin = (why) => { if (done) return; done = true; c.destroy(); res({ buf: Buffer.concat(chunks), ms: Date.now() - t0, why }); };
    c.on('data', (d) => chunks.push(d));
    c.on('error', () => {});
    c.on('close', () => fin('close'));
    if (opts.send) c.on('connect', () => { c.write(opts.send); if (opts.halfClose) c.end(); });
    setTimeout(() => fin('timeout'), opts.timeout);
  });
}

async function caseA(D) {
  mode = 'a';
  const r = await collect({ timeout: 2 * D + 3000 });
  return { pass: r.buf.length === 17 && r.buf.equals(A_PAYLOAD), detail: `${r.buf.length}B ${r.why} ${r.ms}ms` };
}
async function caseB(D) {
  mode = 'b';
  const r = await collect({ timeout: 2 * D + 10000 });
  const ok = r.buf.length === B_PAYLOAD.length && sha(r.buf) === sha(B_PAYLOAD);
  return { pass: ok, detail: `${r.buf.length}B sha:${ok ? 'match' : 'diff'} ${r.why} ${r.ms}ms` };
}
async function caseC(D) {
  mode = 'c'; cServerGotReq = null;
  const req = crypto.randomBytes(1000);
  const r = await collect({ send: req, halfClose: true, timeout: 4 * D + 10000 });
  const expect = Buffer.concat([Buffer.from(sha(req) + '\n'), C_RESP_BODY]);
  const srvOk = cServerGotReq !== null && cServerGotReq.equals(req);
  const ok = srvOk && r.buf.equals(expect);
  return { pass: ok, detail: `serverGotReq:${cServerGotReq ? cServerGotReq.length : 'none'}B resp:${r.buf.length}/${expect.length}B ${r.why} ${r.ms}ms` };
}
async function caseD(D) {
  const r = await collect({ timeout: D + 5000 });
  return { pass: r.why === 'close' && r.ms <= D + 1000, detail: `${r.why} ${r.ms}ms (limit ${D + 1000}ms) ${r.buf.length}B` };
}
function caseE(D) {
  mode = 'e';
  return new Promise((res) => {
    const t0 = Date.now();
    const c = net.connect({ port: LPORT, host: '127.0.0.1' });
    let buf = ''; let i = 0; let got = 0; let bad = 0; let done = false;
    const fin = (why) => { if (done) return; done = true; c.destroy(); res({ pass: got === 100 && bad === 0, detail: `${got}/100 inOrder bad:${bad} ${why} ${Date.now() - t0}ms` }); };
    c.on('error', () => {});
    c.on('connect', () => c.write('c:' + i + '\n'));
    c.on('data', (d) => {
      buf += d.toString();
      let k;
      while ((k = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, k); buf = buf.slice(k + 1);
        if (line === 's:' + i) got++; else bad++;
        i++;
        if (i < 100) c.write('c:' + i + '\n'); else { c.end(); }
      }
    });
    c.on('close', () => fin('close'));
    setTimeout(() => fin('timeout'), 100 * (2 * D + 100) + 5000);
  });
}

(async () => {
  const fake = await startFake();
  const fakePort = fake.address().port;
  const tmp = await new Promise((r) => { const s = net.createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => r(p)); }); });
  const rows = [];
  for (const [name, file] of [['old', OLD], ['new', NEW]]) {
    for (const D of DELAYS) {
      const row = { proxy: name, delay: D };
      let p = await startProxy(file, D, fakePort);
      row.a = await caseA(D); await W(200);
      row.b = await caseB(D); await W(200);
      row.c = await caseC(D); await W(200);
      row.e = await caseE(D); await W(200);
      await stopProxy(p);
      p = await startProxy(file, D, tmp);
      row.d = await caseD(D);
      await stopProxy(p);
      rows.push(row);
      console.log(JSON.stringify(row));
    }
  }
  fake.close();
  fs.writeFileSync(OUT, JSON.stringify({ closedPortUsed: tmp, rows }, null, 1));
  console.log('\n| proxy | delay | a 17B+end | b 1MB+end sha | c half-close | d target closed | e 100 round trips |');
  console.log('|---|---|---|---|---|---|---|');
  for (const r of rows) {
    const f = (x) => (x.pass ? 'PASS ' : 'FAIL ') + x.detail;
    console.log(`| ${r.proxy} | ${r.delay} | ${f(r.a)} | ${f(r.b)} | ${f(r.c)} | ${f(r.d)} | ${f(r.e)} |`);
  }
  process.exit(0);
})().catch((e) => { console.error(e); process.exit(1); });
