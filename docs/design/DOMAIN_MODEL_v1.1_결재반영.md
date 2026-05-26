# DOMAIN_MODEL v1.1 — 사장님 결재 23건 반영

> **작성일**: 2026-05-26 (W4 가도)
> **상위 문서**: `DOMAIN_MODEL_BACKOFFICE.md`, `DOMAIN_MODEL_LANDING.md` (W1+W2+W3)
> **헌법 정합**: #22 (데이터 최소주의) + #23 (5중 검증) + #24 (책임 분산) + #25 (3대 원칙) + #29 (인프라 사전 승인)
> **결재**: 사장님 "응 모두결재" (2026-05-26)

---

## 0. 본 문서 위치

W1+W2+W3 14 산출물 위에 사장님 결재 23건의 **디폴트 반영**을 박제한 **v1.1 증분 문서**.
v1.0(W1~W3)의 도메인 모델·시퀀스·상태머신·데이터 흐름·API 명세·DB 스키마·결제 인터페이스를 폐기하지 않고 **디폴트 값만 확정**한다.

옵션 A(사장님 권고 디폴트) 정합. 옵션 B(보안 매니저 1 권고)는 STD 시점 재고 항목에 한해 6/15 재검토.

---

## 1. 23건 결재 디폴트 매트릭스

| # | 영역 | 디폴트 (옵션 A) | 옵션 B 재검토 | DB/Code 영향 |
|---|---|---|---|---|
| 1 | 가격 STARTER | 29,000원/월 | — | `subscription_plans.price_starter` |
| 2 | 가격 STANDARD | 59,000원/월 | 49,000원 (6/15 재검토) | `subscription_plans.price_std` |
| 3 | 가격 PRO | 100,000원/월 | — | `subscription_plans.price_pro` |
| 4 | 결제 PG 1순위 | Toss Payments | KG이니시스 (백업) | `payment_methods.priority` |
| 5 | 결제 수단 | 카드·계좌이체·간편결제 | — | `payment_methods.types` |
| 6 | 자동갱신 디폴트 | ON | — | `subscriptions.auto_renew = TRUE` |
| 7 | 해지 시점 | 다음 사이클 말일 | — | `subscriptions.cancel_at_period_end` |
| 8 | 해지 후 데이터 | suspended 30일 유예 | — | `tenant_lifecycle.suspended_days = 30` |
| 9 | 익명화 | 90일 후 | — | `tenant_lifecycle.anonymize_after = 90` |
| 10 | trial 기간 | 14일 | — | `subscriptions.trial_days = 14` |
| 11 | 환불 7일 | 100% | — | `refund_policy.tier1` |
| 12 | 환불 14일 | 50% | — | `refund_policy.tier2` |
| 13 | 환불 30일 | 0% | — | `refund_policy.tier3` |
| 14 | 결제 재시도 | 1·3·7일 (3회) | STD 시점 재고 | `payment_retry.schedule` |
| 15 | 대리점 수수료 | 10% | — | `reseller.default_commission = 0.10` |
| 16 | 2FA | 본사 직원 강제 | — | `hq_users.tfa_required = TRUE` |
| 17 | 텔레메트리 | 5분 주기 | — | `telemetry.interval_sec = 300` |
| 18 | 약관 동의 | 4건 필수 | — | `tenant_terms_consent` 테이블 |
| 19 | 약관 버전 변경 | 재동의 강제 | — | `terms_versions.force_reconsent` |
| 20 | 약관 동의 기록 | 일시·IP·버전 | — | `consent_log` (감사용) |
| 21 | 환불 처리 SLA | 영업일 3일 | — | `refund_request.sla_days = 3` |
| 22 | suspended → active 전환 | 결제 성공 즉시 | — | 상태머신 전이 |
| 23 | 약관 4건 정합 | v1.0.0 (W3 박제) | — | `terms_versions.current = '1.0.0'` |

---

## 2. 도메인 객체 변경분 (v1.0 → v1.1)

### 2.1 Subscription
```
+ trial_days: int = 14
+ auto_renew: bool = TRUE
+ cancel_at_period_end: bool = TRUE
+ suspended_days_remaining: int (소진 시 anonymize 큐 진입)
```

### 2.2 PaymentRetry
```
+ schedule: [1, 3, 7] (일 단위)
+ max_attempts: 3
+ on_final_fail: suspended 전이
- (옵션 B 6/15 재검토: STD 한정 [2, 5, 10] 안)
```

### 2.3 RefundPolicy
```
+ tier1: { within_days: 7,  rate: 1.00 }
+ tier2: { within_days: 14, rate: 0.50 }
+ tier3: { within_days: 30, rate: 0.00 }
+ sla_business_days: 3
```

### 2.4 TenantLifecycle (상태머신 보강)
```
active → cancel_requested → (사이클 말일) → suspended (30일)
suspended → active (결제 성공 즉시)
suspended → anonymized (30일 경과 + 60일 후 = 가입 후 90일)
anonymized → purged (감사 보존 5년 후, 헌법 #18 v3 정합)
```

### 2.5 ResellerCommission
```
+ default_rate: 0.10
+ override_allowed: TRUE (대리점별 협의)
+ payout_cycle: 익월 15일
```

### 2.6 Telemetry
```
+ interval_sec: 300 (5분)
+ payload: {tenant_id, device_count, last_heartbeat, db_version}
+ NOT_INCLUDED: 매출/매입/원장/거래처 (헌법 #22 절대)
```

---

## 3. v1.0 → v1.1 정합 체크 (W1+W2+W3 산출물 회귀)

- DOMAIN_MODEL_BACKOFFICE.md: 23건 디폴트 주입 OK
- DOMAIN_MODEL_LANDING.md: 가격 3티어 + trial 14일 OK
- SEQUENCE_3SYSTEMS.md: 자동갱신·재시도 시퀀스 보강 필요 (W4 D2)
- STATE_MACHINE_SUBSCRIPTION.md: suspended 30일 + 90일 anonymize OK
- DATA_FLOW_HEADQUARTER.md: 텔레메트리 5분 OK, 페이로드 #22 정합
- API_SPEC.md / API_SPEC_BACKOFFICE.md: `/billing/retry`, `/refund/request` 엔드포인트 정합
- DB-60_backoffice_v1.sql: 컬럼 디폴트 ALTER 작지서 필요 (W4 D3)
- PAYMENT_INTERFACE.md: Toss 1순위 + 재시도 스케줄 OK
- 약관 4건 (v1.0.0): force_reconsent 플래그 정합 OK

---

## 4. 다음 결재 영역

- 6/15 STD 가격 49k 재검토 + 결제 재시도 옵션 B
- 6/03 Figma 1차 시안 결재
- 6/10 Figma 최종 결재
- 7월 첫주 Toss 실 API 연결 결재 (테스트 KEY → LIVE KEY)
- 8/24 베타 출시 절대 게이트 (5중 검증 7영역 PASS 필수)

---

**박제자**: PM 브라운킴
**검증**: 어벤져스 9명 만장일치 (옵션 A 디폴트)
**상태**: 결재 완료, W4 D2부터 시퀀스·DDL 반영 가도 시작
