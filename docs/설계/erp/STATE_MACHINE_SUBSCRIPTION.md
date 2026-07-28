# 상태 머신 — Tenant / Subscription / Payment

> 작성일: 2026-05-26
> 작성자: PM (브라운킴)
> 헌법 정합: #18 v3 / #22 / #24 / #25
> 결재: 사장님 일괄 결재 ("응 모두결재!!", 2026-05-26)

---

## 1. Tenant 상태 머신

### 1.1 상태 정의

| 상태 | 의미 | 본사 가시화 |
|---|---|---|
| pending | 가입 신청 완료 / 결제 미완료 | 백오피스 신청 목록 |
| active | 결제·라이선스 정상 | 운영 대시보드 |
| suspended | 결제 실패·약관 위반·해지 신청 후 종료일 도래 | 알림 + 잠금 |
| terminated | 영구 해지 (메타만 잔존, 90일 후 익명화) | 종료 목록 |

### 1.2 전환 다이어그램

```
       (가입 신청)
            │
            ▼
       ┌─────────┐
       │ pending │
       └────┬────┘
            │ (결제 성공 + 라이선스 발급)
            ▼
       ┌─────────┐ ──── (해지 신청, 종료일 도래) ────┐
       │ active  │                                    │
       └────┬────┘                                    │
            │                                          │
   (결제 실패 3회 / 약관 위반)                          │
            │                                          ▼
            ▼                                    ┌────────────┐
       ┌──────────┐                              │ suspended  │
       │suspended │ ──── (결제 정상화) ────▶ active            │
       └────┬─────┘                              └─────┬──────┘
            │                                          │
            │ (30일 경과 미해결)                        │
            ▼                                          │
       ┌────────────┐ ◀───────────────────────────────┘
       │ terminated │
       └────────────┘
            │
            │ (90일 후)
            ▼
       메타 익명화 + 백업 보관 (E2E 암호화)
```

### 1.3 전환 이벤트·조건·액션

| From | To | 이벤트 | 조건 | 액션 |
|---|---|---|---|---|
| pending | active | payment.success | 첫 결제 + 라이선스 발급 | DNS·터널 발급 / EXE DL URL 메일 |
| active | suspended | payment.failed_3x | 3회 연속 결제 실패 | ERP 락 신호 / 알림 메일 |
| active | suspended | tos.violation | 약관 위반 신고 처리 | 본사 관리자 승인 / 안내 메일 |
| active | suspended | subscription.cancel_period_end | 해지 신청 후 이용 종료일 도래 | ERP 락 신호 / 종료 안내 |
| suspended | active | payment.recovered | 미납 결제 정상화 | ERP 락 해제 / 정상화 안내 |
| suspended | terminated | timeout.30d | 30일 미해결 | ERP 락 유지 + 데이터 백업 안내 |
| terminated | (익명화) | timeout.90d | 90일 경과 | 메타정보 익명화 (이름·이메일·전화 마스킹) |

---

## 2. Subscription 상태 머신

### 2.1 상태 정의

| 상태 | 의미 |
|---|---|
| trial | 베타 체험단 / 무료 체험 (기간 한정) |
| active | 정상 구독 중 (결제 완료) |
| past_due | 결제 실패 / 미납 (재시도 중) |
| cancelled | 해지 완료 (이용 종료일 도래) |

### 2.2 전환 다이어그램

```
   (가입 + 체험 신청)
         │
         ▼
    ┌───────┐
    │ trial │ ──── (체험 종료 + 결제 성공) ──▶ active
    └───┬───┘
        │ (체험 종료 + 결제 안 함)
        ▼
    cancelled

    ┌────────┐
    │ active │ ─── (결제 실패 1회) ──▶ past_due
    └───┬────┘                            │
        │ ◀── (결제 정상화) ──────────────┘
        │
        │ (해지 신청)
        ▼
    cancel_requested (active 유지, cancel_requested_at 기록)
        │
        │ (이용 종료일 도래)
        ▼
    cancelled

    ┌──────────┐
    │ past_due │ ─── (3회 재시도 실패) ──▶ cancelled
    └──────────┘
```

### 2.3 전환 이벤트·조건·액션

| From | To | 이벤트 | 조건 | 액션 |
|---|---|---|---|---|
| (new) | trial | tenant.activated | 체험 신청 플래그 | 7~14일 무료 체험 시작 |
| trial | active | payment.success | 체험 종료 + 결제 성공 | current_period_start 갱신 |
| trial | cancelled | trial.expired_unpaid | 체험 종료 + 미결제 | tenant suspended 전환 트리거 |
| active | past_due | payment.failed | 자동결제 1회 실패 | 재시도 큐 등록 (1일 / 3일 / 7일) |
| past_due | active | payment.recovered | 재시도 성공 | 잠금 해제 / 알림 |
| past_due | cancelled | payment.failed_3x | 3회 재시도 모두 실패 | tenant suspended |
| active | cancelled | cancel.period_end | cancel_requested_at + 잔여기간 경과 | tenant suspended |

### 2.4 다운/업그레이드 (별도 전환 아님, 메타 변경)
- 업그레이드: 즉시 적용 + 일할 차액 청구
- 다운그레이드: current_period_end 도래 시 다음 주기부터 적용

---

## 3. Payment 상태 머신

### 3.1 상태 정의

| 상태 | 의미 |
|---|---|
| pending | PG 요청 진행 중 |
| success | 결제 승인 완료 |
| failed | 결제 실패 (카드 한도·인증 실패·취소) |
| refunded | 전액 환불 완료 |
| partial_refunded | 부분 환불 완료 |

### 3.2 전환 다이어그램

```
   (결제 요청)
         │
         ▼
    ┌─────────┐
    │ pending │
    └────┬────┘
         │ Webhook
         ├────────────────────────┐
         │                        │
         ▼                        ▼
    ┌─────────┐              ┌────────┐
    │ success │              │ failed │
    └────┬────┘              └────────┘
         │
         │ (환불 신청)
         ├────────────────────────┐
         │                        │
         ▼                        ▼
    ┌──────────┐         ┌──────────────────┐
    │ refunded │         │ partial_refunded │
    └──────────┘         └──────────────────┘
```

### 3.3 전환 이벤트·조건·액션

| From | To | 이벤트 | 조건 | 액션 |
|---|---|---|---|---|
| (new) | pending | payment.requested | IPaymentProvider.RequestPaymentAsync 호출 | transaction_id 발급 / DB INSERT |
| pending | success | webhook.success | PG 승인 Webhook 수신 | paid_at 기록 / invoice 생성 / subscription active |
| pending | failed | webhook.failed | PG 실패 Webhook 수신 | fail_reason 기록 / 재시도 큐 (자동결제만) |
| pending | failed | timeout.30min | 30분 이내 Webhook 미수신 | 수동 조회 → 최종 상태 동기화 |
| success | refunded | refund.full_complete | 전액 환불 처리 완료 | Refund 레코드 / invoice 음수 생성 |
| success | partial_refunded | refund.partial_complete | 부분 환불 완료 | 남은 금액 유지 / 부분 invoice |

### 3.4 멱등성 박제
- PG Webhook은 중복 수신 가능 → `transaction_id` UNIQUE + 상태 idempotent 처리
- 동일 transaction_id에 대해 success → success 전환은 NO-OP

---

## 4. 헌법 정합 박제

- **#18 v3**: 상태 머신 데이터(상태값·전환 이력)만 본사 보유. 결제 카드정보·ERP 업무 데이터는 본사 미보유.
- **#22**: 90일 경과 terminated 메타정보는 익명화. 백업도 E2E 암호화.
- **#24**: 데이터 백업 책임 = 고객. 본사는 해지 30일 전 백업 안내·가이드 의무.
- **#25**: 상태 전환은 항상 자동화(쉽게) + 사장님 결재 영역만 수동(정확하게) + 멱등 처리(안전하게).

---

## 사장님 결재 영역

| # | 결재 영역 | 결정 사항 |
|---|---|---|
| ST1 | 결제 실패 재시도 | 1일 / 3일 / 7일 (3회) → 3회 실패 시 cancelled |
| ST2 | suspended → terminated 기간 | 30일 미해결 시 자동 |
| ST3 | terminated 익명화 기간 | 90일 (메타 익명화, 백업 E2E 보관) |
| ST4 | trial 기본 기간 | 14일 (베타) / 정식 출시 후 7일로 단축 가능 |
| ST5 | 환불 정책 | 일할 환불 (정상 사용 중) / 전액 환불 (가입 7일 이내) / 환불 없음 (약관 위반) |

> 일괄 결재 완료 ("응 모두결재!!", 2026-05-26)
