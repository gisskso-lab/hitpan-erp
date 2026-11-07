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

// ⬛ 옛 줄(20261107작 S4 시험 앱 첫 판) — 지우지 않고 남긴다.
//   결함(HQA D-S4-1): data 는 setTimeout 뒤에 쓰는데 상대 소켓 'close' 에서는 반대 소켓을 바로 destroy 했다.
//   응답 바이트가 지연 대기열에 남은 채 상대가 닫으면(EOF) 예약된 바이트를 잃는다
//   (가짜 서버가 17바이트 보내고 끝냄 → 직접 17바이트 · 이 프록시(방향당 150ms) 0바이트).
//   또 allowHalfOpen 기본값(false)이라 한쪽 EOF(half-close) 를 받는 즉시 다른 쪽도 닫혀 버렸다.
//
// net.createServer((client) => {
//   const server = net.connect(targetPort, targetHost);
//   client.on('data', (d) => setTimeout(() => server.write(d), delayMs));
//   server.on('data', (d) => setTimeout(() => client.write(d), delayMs));
//   client.on('close', () => server.destroy());
//   server.on('close', () => client.destroy());
//   client.on('error', () => {});
//   server.on('error', () => {});
// }).listen(listenPort, '127.0.0.1', () => {
//   console.log(`listen ${listenPort} delay ${delayMs}ms each way -> ${targetHost}:${targetPort}`);
// });

// 🔴 20261107작 HQA D-S4-1 고침 — 방향마다 「지연 대기열」 하나(FIFO)에 데이터 · EOF · 오류 닫기를 같은 지연으로 넣는다.
//   · 데이터 → 지연 뒤 반대쪽 write
//   · EOF('end') → 앞서 넣은 데이터가 다 나간 뒤(같은 대기열 뒤) 반대쪽 end() — 반쪽 닫힘(half-close)을 그대로 넘긴다
//   · 'close' 에서 바로 destroy 하지 않는다(destroy 는 아직 못 보낸 버퍼를 버린다)
//   · 오류('error') → 대기열 뒤에서 반대쪽 end() · 1초 안에 안 닫히면 destroy
//   · 대상 연결 실패(오류)도 같은 길 → 지연 뒤 클라이언트를 닫는다(매달림 0)
//   · 양쪽 allowHalfOpen: true — 한쪽 EOF 를 받아도 다른 방향은 계속 흐른다
const ABORT_GRACE_MS = 1000;

function makeDelayQueue() {
  const items = [];
  let timer = null;
  function pump() {
    timer = null;
    const now = Date.now();
    while (items.length && items[0].at <= now) items.shift().run();
    if (items.length) timer = setTimeout(pump, Math.max(1, items[0].at - Date.now()));
  }
  return (run) => {
    items.push({ at: Date.now() + delayMs, run });
    if (!timer) timer = setTimeout(pump, delayMs);
  };
}

function writable(sock) {
  return !sock.destroyed && !sock.writableEnded;
}

// src 에서 받은 것을 지연 뒤 dst 로 넘긴다(한 방향).
function relay(src, dst) {
  const push = makeDelayQueue();
  let sawEnd = false;
  let sawError = false;
  let waitingDrain = false;

  src.on('data', (d) => {
    push(() => {
      if (!writable(dst)) return;
      if (!dst.write(d) && !waitingDrain) {
        // 받는 쪽이 밀리면 보내는 쪽 읽기를 잠시 멈춘다(이미 받은 바이트는 대기열에 그대로 남는다).
        waitingDrain = true;
        src.pause();
        dst.once('drain', () => { waitingDrain = false; src.resume(); });
      }
    });
  });

  src.on('end', () => {
    sawEnd = true;
    push(() => { if (writable(dst)) dst.end(); });
  });

  src.on('error', () => {
    sawError = true;
    push(() => {
      if (writable(dst)) dst.end();
      if (!dst.destroyed) setTimeout(() => { if (!dst.destroyed) dst.destroy(); }, ABORT_GRACE_MS).unref();
    });
  });

  src.on('close', () => {
    // 'end' 도 'error' 도 없이 닫힌 경우의 안전망 — 그래도 앞선 데이터 뒤에서 반대쪽을 닫는다.
    if (!sawEnd && !sawError) push(() => { if (writable(dst)) dst.end(); });
  });
}

net.createServer({ allowHalfOpen: true }, (client) => {
  const server = net.connect({ port: targetPort, host: targetHost, allowHalfOpen: true });
  relay(client, server);
  relay(server, client);
}).listen(listenPort, '127.0.0.1', () => {
  console.log(`listen ${listenPort} delay ${delayMs}ms each way -> ${targetHost}:${targetPort}`);
});
