# Messaging — 본사 ERP ↔ 백오피스 단방향 Outbox (WS-20260601-20)

**단방향 절대 원칙**: 백오피스(클라우드) → 본사 ERP(로컬) Push 만. 역방향(본사 ERP → 백오피스) 절대 금지 (헌법 #18·#22, 8명제 #3).

## 구성
- `IOutboxPublisher` / `OutboxPublisherService` — 백오피스 트랜잭션 내 `messaging_outbox` INSERT (atomic).
- `BackgroundServices/OutboxPollerWorker` — 본사 ERP 측 5분 주기 SELECT + processed_at 갱신.
- DDL: `database/migrations/20260601_eight_propositions.sql` §6.

## 평문 0 가드
`OutboxPublisherService.GuardAgainstPlaintext` 가 payload 에 평문 사업자번호·상호·대표자·주소·이메일·휴대폰·주민번호 박제 시 `InvalidOperationException` 으로 즉시 차단 (헌법 #15 silent swallow 금지).

## 활성화
```json
"Outbox": {
  "Enabled": true,
  "PollIntervalMinutes": 5,
  "BackofficeConnectionString": "Server=...;Database=hitpan_backoffice;..."
}
```
