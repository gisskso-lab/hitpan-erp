# D-S4-1 증거 (PM 내부 · 레포 밖에서 만든 원파일 그대로)

- `control-test.js` · `control.log` · `result.json` — 옛/새 프록시 × 지연 0/150/300 × 5경우 대조.
- `proxy2-instrumented.js` — 내부 [4] 옛 프록시에 「닫힘 순서 · 대기 바이트」 세는 줄만 더한 사본(중계 동작 무변).
- `loss150.jsonl` · `loss300.jsonl` — 위 계측 원로그(연결마다 client-close/server-close · 먼저 닫은 쪽 · 마지막 요청 줄 · 대기 바이트). 시각 = epoch ms.
- `flow.js` · `flow150.log` · `flow300.log` — 계측 때 돌린 흐름(로드 → 열기 → Esc → 다른 칸 → 바깥 · /before · /after 각 5회) · `flownew*.log` = 고친 프록시로 같은 흐름.
- `summ.js` — 집계.
- 가짜 데이터 시험 앱만 · 운영 · DB 0. 회로 id 는 시험 앱이 만든 임시 값.
