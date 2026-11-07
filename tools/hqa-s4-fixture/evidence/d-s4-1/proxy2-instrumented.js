// Copy of internal proxy2.js (old defect shape, unchanged relay logic) + counting lines only.
// node proxy2-instrumented.js <listenPort> <delayEachWayMs> <targetPort> <log.jsonl>
const net = require('net'); const L = +process.argv[2]; const D = +process.argv[3];
const T = +process.argv[4] || 5951; const fs = require('fs'); const LOG = process.argv[5] || 'loss.jsonl'; // [instr]
let seq = 0; const log = (o) => fs.appendFileSync(LOG, JSON.stringify({ t: Date.now(), ...o }) + '\n'); // [instr]
net.createServer(c => { const s = net.connect(T, '127.0.0.1');
  const id = ++seq; let pendC = 0, pendS = 0, lastReq = '', upgraded = false, closedFirst = ''; // [instr]
  c.on('data', d => { const m = /^(GET|POST|PUT|DELETE|HEAD|OPTIONS|PATCH) (\S+)/.exec(d.toString('latin1', 0, 200)); if (m) { lastReq = m[1] + ' ' + m[2]; if (/upgrade: websocket/i.test(d.toString('latin1'))) upgraded = true; } pendS += d.length; }); // [instr]
  s.on('data', d => { pendC += d.length; }); // [instr]
  c.on('data', d => setTimeout(() => { pendS -= d.length; s.write(d); }, D)); s.on('data', d => setTimeout(() => { pendC -= d.length; c.write(d); }, D)); // [instr: pend-- only]
  s.on('close', () => { if (!closedFirst) closedFirst = 'server'; const buffered = c.destroyed ? 0 : c.writableLength; log({ ev: 'server-close', id, first: closedFirst, lastReq, ws: upgraded, pendingToClient: pendC, clientWriteBuffered: buffered, loss: closedFirst === 'server' && (pendC + buffered) > 0 }); }); // [instr]
  c.on('close', () => { if (!closedFirst) closedFirst = 'client'; const buffered = s.destroyed ? 0 : s.writableLength; log({ ev: 'client-close', id, first: closedFirst, lastReq, ws: upgraded, pendingToServer: pendS, serverWriteBuffered: buffered, pendingToClient: pendC }); }); // [instr]
  c.on('close', () => s.destroy()); s.on('close', () => c.destroy()); c.on('error', ()=>{}); s.on('error', ()=>{});
}).listen(L, '127.0.0.1', () => console.log('listen', L, 'delay', D));
