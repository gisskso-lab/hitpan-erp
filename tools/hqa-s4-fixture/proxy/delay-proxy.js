// 지연 프록시 — 브라우저 ↔ 시험 앱 사이 TCP 바이트를 방향마다 D ms 늦춰 넘긴다(웹소켓 · 정적 파일 모두).
// 쓰는 법: node delay-proxy.js <듣는포트> <방향당지연ms> [대상포트=5799] [대상호스트=127.0.0.1]
//   예) node delay-proxy.js 5800 150      → http://127.0.0.1:5800 으로 열면 왕복 약 300ms 가 더해진다.
// 바깥 꾸러미 0(Node 내장 net 만). 127.0.0.1 에만 듣는다.
const net = require('net');

const listenPort = Number(process.argv[2]);
const delayMs = Number(process.argv[3]);
const targetPort = Number(process.argv[4] || 5799);
const targetHost = process.argv[5] || '127.0.0.1';

if (!Number.isInteger(listenPort) || !Number.isFinite(delayMs) || delayMs < 0) {
  console.error('쓰는 법: node delay-proxy.js <듣는포트> <방향당지연ms> [대상포트=5799] [대상호스트=127.0.0.1]');
  process.exit(2);
}

net.createServer((client) => {
  const server = net.connect(targetPort, targetHost);
  client.on('data', (d) => setTimeout(() => server.write(d), delayMs));
  server.on('data', (d) => setTimeout(() => client.write(d), delayMs));
  client.on('close', () => server.destroy());
  server.on('close', () => client.destroy());
  client.on('error', () => {});
  server.on('error', () => {});
}).listen(listenPort, '127.0.0.1', () => {
  console.log(`listen ${listenPort} delay ${delayMs}ms each way -> ${targetHost}:${targetPort}`);
});
