# 백오피스 API 명세

> 작성일: 2026-05-26
> 작성: 백엔드 매니저(Harvard·Oracle 30년) + 설계팀장 브라운킴
> 정합: DOMAIN_MODEL_BACKOFFICE.md / SEQUENCE_3SYSTEMS.md / STATE_MACHINE_SUBSCRIPTION.md
> 헌법: #2(tenant_id JWT 클레임)·#4(decimal)·#7(SaaS/ERP 권한 분리)·#18 v3·#22

---

## 0. 공통 규약

### 0.1 베이스 URL
- 운영: `https://backoffice.hitpan.app/api/admin`
- 베타: `https://backoffice-beta.hitpan.app/api/admin`

### 0.2 인증
- 본사 직원 JWT (별도 인증 영역 — ERP tenant JWT와 절대 혼용 금지, 헌법 #7)
- TOTP 2FA 필수 (admin_2fa)
- Authorization: `Bearer {jwt}`
- 헤더: `X-Admin-Session-Id` (admin_sessions FK)

### 0.3 권한 정책
- 역할: SUPER_ADMIN / OPS / SALES / SUPPORT / FINANCE / RESELLER_ADMIN / READ_ONLY
- Policy 매핑: `[Authorize(Policy = "Admin.Tenants.Write")]`

### 0.4 응답 표준
```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "trace_id": "uuid-v7",
  "server_time": "2026-05-26T10:00:00Z"
}
```

### 0.5 에러 코드 (공통)
| 코드 | HTTP | 의미 |
|---|---|---|
| AUTH_REQUIRED | 401 | 인증 누락 |
| FORBIDDEN | 403 | 권한 부족 |
| NOT_FOUND | 404 | 자원 없음 |
| VALIDATION_FAILED | 422 | 입력 검증 실패 |
| CONFLICT | 409 | 상태 충돌 |
| RATE_LIMITED | 429 | 호출 한도 초과 |
| INTERNAL | 500 | 서버 오류 |

---

## 1. 고객사 (Tenants) — 7건

### 1.1 GET /tenants
- 목록 조회 (검색·필터·페이지)
- 권한: `Admin.Tenants.Read`
- 쿼리: `q`(검색), `status`, `tier`, `industry`, `page`, `size`
- 응답: `{ items: TenantSummary[], total, page, size }`

### 1.2 GET /tenants/{id}
- 단건 상세 (사업자정보·구독·결제 요약 포함)
- 권한: `Admin.Tenants.Read`
- 응답: `TenantDetail` (business_info / current_subscription / device_count / last_heartbeat 포함)

### 1.3 POST /tenants
- 수동 생성 (영업 직접 등록, 예외 영역)
- 권한: `Admin.Tenants.Write` + `Role.Sales`
- 요청: `{ company_name, business_number, representative_*, tier_id, reseller_id? }`
- 응답: `TenantDetail`
- 부수효과: provisioning 큐 enqueue → DNS 가도 → EXE 다운로드 토큰 발급

### 1.4 PATCH /tenants/{id}
- 정보 수정 (회사명·연락처·산업·메모)
- 권한: `Admin.Tenants.Write`
- 요청: 부분 갱신 (JSON Patch 또는 partial DTO)
- 비고: business_number 변경은 금지 (별도 영역)

### 1.5 PATCH /tenants/{id}/suspend
- 정지 (미결제·정책 위반)
- 권한: `Admin.Tenants.Suspend`
- 요청: `{ reason, until?: datetime }`
- 부수효과: ERP 라이선스 무효화 큐 enqueue (헌법 #30 — 고객 PC 워치독이 ping 회수)

### 1.6 PATCH /tenants/{id}/resume
- 정지 해제
- 권한: `Admin.Tenants.Suspend`

### 1.7 DELETE /tenants/{id}
- 해지 (soft delete: `deleted_at` set, 30일 후 메타 폐기)
- 권한: `Admin.Tenants.Delete` + 2FA 재확인
- 부수효과: 구독 CANCELLED, 라이선스 revoke, 본사 데이터 30일 후 폐기 (헌법 #22)

---

## 2. 구독 (Subscriptions) — 4건

### 2.1 GET /subscriptions
- 목록 (tenant·tier·status 필터)
- 권한: `Admin.Subscriptions.Read`

### 2.2 POST /subscriptions
- 신규 구독 생성 (수동 영역)
- 권한: `Admin.Subscriptions.Write`
- 요청: `{ tenant_id, tier_id, billing_period, starts_at }`
- 상태: ACTIVE
- 부수효과: 결제 일정 등록 → billing_cycles 첫 회 가도

### 2.3 PATCH /subscriptions/{id}/change-plan
- 티어 변경 (업/다운그레이드)
- 권한: `Admin.Subscriptions.Write`
- 요청: `{ new_tier_id, effective_date?: datetime, proration: true/false }`
- 부수효과: 일할 차액 결제 가도 (proration=true 시)

### 2.4 PATCH /subscriptions/{id}/cancel
- 해지 예약
- 권한: `Admin.Subscriptions.Cancel`
- 요청: `{ effective_date, reason }`
- 상태 전이: ACTIVE → CANCELLATION_SCHEDULED → CANCELLED (effective_date 도래 시)

---

## 3. 결제 (Payments) — 3건

### 3.1 GET /payments
- 결제 이력
- 권한: `Admin.Payments.Read` + `Role.Finance|SuperAdmin`
- 필터: `tenant_id`, `status`, `from`, `to`, `provider`

### 3.2 POST /payments/refund
- 환불 처리
- 권한: `Admin.Payments.Refund` + 2FA 재확인
- 요청: `{ payment_id, amount: decimal, reason }` ← 헌법 #4
- 응답: `RefundResult { refund_id, status, refunded_at, provider_refund_id }`
- 부수효과: 결제사 어댑터 위임 (TossPaymentsAdapter / KcpAdapter)

### 3.3 GET /payments/{id}/invoice
- 영수증·세금계산서 조회
- 권한: `Admin.Payments.Read`
- 응답: `{ invoice_url, invoice_type, issued_at }` (URL은 결제사·이세로 위임, 본사 보유 0)

---

## 4. 대리점 (Resellers) — 5건

### 4.1 GET /resellers
- 목록·검색
- 권한: `Admin.Resellers.Read`

### 4.2 POST /resellers
- 신규 대리점 등록
- 권한: `Admin.Resellers.Write`
- 요청: `{ company_name, business_number, contract_type, commission_rate: decimal, ... }`

### 4.3 PATCH /resellers/{id}
- 정보 수정 (수수료율 변경은 contract 변경 영역 가도)

### 4.4 GET /resellers/{id}/commissions
- 월별 수수료 산정 조회
- 권한: `Admin.Resellers.Read` | `Role.ResellerAdmin`(자기 것만)
- 쿼리: `year`, `month`
- 응답: `{ period, total_commission: decimal, items: CommissionItem[] }`

### 4.5 POST /resellers/{id}/settle
- 수수료 정산 가도
- 권한: `Admin.Resellers.Settle` + `Role.Finance` + 2FA
- 요청: `{ period_year, period_month, settle_method: enum, memo }`
- 부수효과: commission_settlements 생성 + 회계 연동 (외부 위임)

---

## 5. 모니터링 (Telemetry) — 3건

### 5.1 GET /telemetry/heartbeats
- 고객 PC 워치독 ping (헌법 #30 — 메타만)
- 권한: `Admin.Telemetry.Read`
- 응답: 최근 N분 ping 요약 (tenant_id·last_seen·status — 업무 데이터 0)

### 5.2 GET /telemetry/usage
- 사용량 메트릭 (디바이스 수·DB 크기 카운터만)
- 권한: `Admin.Telemetry.Read`
- 헌법 #22 정합: 업무 데이터 카운트는 제외, 라이선스 검증 메트릭만

### 5.3 GET /telemetry/alerts
- 알림 큐 (워치독 이상·결제 실패 등)
- 권한: `Admin.Telemetry.Read`

---

## 6. 인증 (Auth) — 4건

### 6.1 POST /auth/login
- 1차 인증 (ID·비밀번호)
- 응답: `{ pre_auth_token, totp_required: true }`

### 6.2 POST /auth/verify-totp
- 2차 인증 (TOTP 6자리)
- 요청: `{ pre_auth_token, totp_code }`
- 응답: `{ access_token, refresh_token, expires_in, admin_user: AdminUserSummary }`

### 6.3 POST /auth/logout
- 세션 회수
- 부수효과: admin_sessions 무효화 + refresh_token revoke

### 6.4 POST /auth/refresh
- 액세스 토큰 갱신
- 요청: `{ refresh_token }`
- 응답: 신규 access_token

---

## 7. 권한 정책 매핑 (요약)

| Policy | 역할 |
|---|---|
| Admin.Tenants.Read | OPS, SALES, SUPPORT, FINANCE, SUPER_ADMIN |
| Admin.Tenants.Write | OPS, SALES, SUPER_ADMIN |
| Admin.Tenants.Suspend | OPS, SUPER_ADMIN |
| Admin.Tenants.Delete | SUPER_ADMIN (2FA 재확인) |
| Admin.Subscriptions.* | OPS, FINANCE, SUPER_ADMIN |
| Admin.Payments.Refund | FINANCE, SUPER_ADMIN (2FA 재확인) |
| Admin.Resellers.Settle | FINANCE, SUPER_ADMIN (2FA 재확인) |
| Admin.Telemetry.Read | OPS, SUPPORT, SUPER_ADMIN |

---

## 8. 헌법 정합 체크

| 헌법 | 적용 |
|---|---|
| #2 tenant_id 파라미터 금지 | 본 API는 본사 직원용 — tenant_id는 path/query로 명시 가능 (다른 tenant 데이터 조회 권한). 단 ERP API와 절대 혼용 금지 |
| #4 decimal | amount·commission_rate 모두 decimal |
| #7 권한 혼용 금지 | 본사 admin_users JWT ≠ ERP tenant JWT, Issuer·Audience 분리 |
| #18 v3 | telemetry는 메타만, 업무 데이터 0 |
| #22 | 카드 토큰·영수증 URL만, 원본 0 |

---

## 9. 사장님 결재 영역
- 역할 정의 7종 확정 (SUPER_ADMIN / OPS / SALES / SUPPORT / FINANCE / RESELLER_ADMIN / READ_ONLY)
- 2FA 재확인 영역 3종 확정 (DELETE, Refund, Settle)
- 환불 정책 (즉시 환불 / 영업일 환불) 결재

## 10. W3 가도 예고
- 각 엔드포인트별 요청·응답 스키마 JSON 박제
- OpenAPI 3.1 yaml 변환
- Postman 컬렉션 생성
- 백엔드 Controller 스켈레톤 코드 생성 (헌법 #11 — 덮어쓰기 금지, 신규 영역만)
