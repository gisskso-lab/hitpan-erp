# 히트판 백오피스 — 기능정의서
> 버전: 1.0 | 기준일: 2026-05-05 | 작성자: ERP매니저 + 백엔드매니저

---

## 1. 시스템 개요

### 1-1. 접근 계정 유형

| account_type | JWT 클레임 | 접근 범위 |
|-------------|-----------|---------|
| platform_admin | account_type: platform_admin | 전체 데이터 |
| reseller_admin | account_type: reseller_admin, reseller_id: {id} | 본인 담당만 |

### 1-2. 공통 원칙

- **데이터 격리**: reseller_admin은 본인 `reseller_id` 담당 고객사만 조회 가능
- **INSERT ONLY**: commission_settlements는 DELETE 금지, 취소 = status 변경
- **금액**: 모든 금액 DECIMAL(15,2) — float/double 금지
- **암호화**: resellers.bank_account AES-256 암호화 필수

---

## 2. 인증 (AUTH)

### F-AUTH-01: 본사 관리자 로그인

| 항목 | 내용 |
|------|------|
| 기능명 | 본사 관리자 로그인 |
| 접근 권한 | 비인증 |
| 입력 | email (string), password (string) |
| 처리 | 1. platform_admins에서 email 조회<br>2. bcrypt 검증<br>3. is_active = 1 확인<br>4. JWT 발급 (account_type: platform_admin, role, admin_id)<br>5. last_login_at 업데이트 |
| 출력 | access_token, refresh_token, role |
| 예외 | 401: 이메일 없음/비밀번호 불일치<br>403: is_active=0 |
| 보안 | Rate Limit: IP당 5회/분 초과 시 429<br>로그인 실패 5회 → 30분 잠금 |

### F-AUTH-02: 대리점 계정 로그인

| 항목 | 내용 |
|------|------|
| 기능명 | 대리점 계정 로그인 |
| 접근 권한 | 비인증 |
| 입력 | email (string), password (string) |
| 처리 | 1. reseller_accounts에서 email 조회<br>2. bcrypt 검증<br>3. is_active = 1 확인<br>4. 소속 reseller is_active 확인<br>5. JWT 발급 (account_type: reseller_admin, reseller_id, role, account_id)<br>6. last_login_at 업데이트 |
| 출력 | access_token, refresh_token, role, reseller_id |
| 예외 | 401: 이메일 없음/비밀번호 불일치<br>403: 계정 비활성 또는 대리점 suspended/terminated |
| 보안 | Rate Limit: IP당 5회/분 초과 시 429 |

### F-AUTH-03: 토큰 갱신

| 항목 | 내용 |
|------|------|
| 기능명 | Refresh Token으로 Access Token 재발급 |
| 처리 | refresh_token 검증 → 동일 계정 유형 유지하여 새 access_token 발급 |

---

## 3. 본사 — 고객사 관리 (TENANT)

### F-TENANT-01: 고객사 목록 조회

| 항목 | 내용 |
|------|------|
| 기능명 | 전체 고객사 목록 |
| 접근 권한 | platform_admin (모든 role) |
| 입력 | status (optional), reseller_id (optional), plan_type (optional), page, size |
| 처리 | tenants JOIN subscriptions JOIN resellers → 페이지네이션 |
| 출력 | tenant_id, company_name, biz_no, reseller_name, status, plan_type, trial_ends_at, created_at |
| 정렬 | 기본: created_at DESC |

### F-TENANT-02: 고객사 상세 조회

| 항목 | 내용 |
|------|------|
| 기능명 | 고객사 상세 정보 |
| 접근 권한 | platform_admin |
| 출력 | 기본정보 + 구독정보 + 결제수단 + 청구 이력(최근 12개월) + 담당 대리점 |

### F-TENANT-03: 고객사 상태 변경

| 항목 | 내용 |
|------|------|
| 기능명 | 고객사 상태 변경 (trial→active, active→suspended 등) |
| 접근 권한 | platform_admin (super_admin, billing_admin) |
| 입력 | tenant_id, new_status, reason (optional) |
| 처리 | tenants.status UPDATE + 감사 로그 기록 |
| 예외 | 400: 허용되지 않는 상태 전이 |

### F-TENANT-04: 고객사 온보딩 (신규 등록)

| 항목 | 내용 |
|------|------|
| 기능명 | 신규 고객사 등록 (수동) |
| 접근 권한 | platform_admin (super_admin) |
| 입력 | company_name, biz_no, ceo_name, tel, address, reseller_id (optional), plan_type, billing_cycle |
| 처리 | 1. tenants INSERT<br>2. subscriptions INSERT (trial 또는 active)<br>3. tenant_code 자동 생성<br>4. 초기 admin 계정 생성 및 이메일 발송 |
| 출력 | tenant_id, tenant_code, 임시 비밀번호 |

---

## 4. 본사 — 구독·결제 관리 (BILLING)

### F-BILLING-01: 청구서 목록 조회

| 항목 | 내용 |
|------|------|
| 기능명 | 전체 청구서 목록 |
| 접근 권한 | platform_admin (super_admin, billing_admin) |
| 입력 | billing_month (optional), status (optional), tenant_id (optional), page, size |
| 출력 | invoice_id, company_name, billing_month, total_amount, status, paid_at |

### F-BILLING-02: 청구서 수동 처리

| 항목 | 내용 |
|------|------|
| 기능명 | 청구서 상태 수동 변경 (실패→재시도, 취소 등) |
| 접근 권한 | platform_admin (billing_admin, super_admin) |
| 입력 | invoice_id, action (retry\|cancel) |
| 처리 | 토스페이먼츠 빌링 API 재호출 또는 status='cancelled' |

### F-BILLING-03: 구독 플랜 변경

| 항목 | 내용 |
|------|------|
| 기능명 | 고객사 구독 플랜 변경 |
| 접근 권한 | platform_admin (super_admin, billing_admin) |
| 입력 | tenant_id, new_plan_type, new_billing_cycle, extra_users |
| 처리 | 기존 subscription status='cancelled' + 신규 subscription INSERT |

---

## 5. 본사 — 대리점 관리 (RESELLER)

### F-RESELLER-01: 대리점 목록 조회

| 항목 | 내용 |
|------|------|
| 기능명 | 대리점 목록 |
| 접근 권한 | platform_admin (super_admin, billing_admin) |
| 입력 | status (optional), page, size |
| 출력 | reseller_id, reseller_code, reseller_name, status, 담당 고객사 수, 이달 수수료 합계, join_date |

### F-RESELLER-02: 대리점 상세 조회

| 항목 | 내용 |
|------|------|
| 기능명 | 대리점 상세 정보 |
| 접근 권한 | platform_admin |
| 출력 | 기본정보 + 담당 고객사 목록 + 수수료 정책 이력 + 정산 이력(12개월) |
| 보안 | bank_account AES-256 복호화 후 마스킹 표시 (****-****-1234) |

### F-RESELLER-03: 대리점 등록

| 항목 | 내용 |
|------|------|
| 기능명 | 신규 대리점 등록 |
| 접근 권한 | platform_admin (super_admin) |
| 입력 | reseller_name, biz_no, ceo_name, tel, address, bank_name, bank_account, account_holder, contact_person, contact_phone, contact_email, join_date |
| 처리 | resellers INSERT + bank_account AES-256 암호화 저장 + reseller_code 자동 생성 (RS-NNN) |

### F-RESELLER-04: 대리점 정보 수정

| 항목 | 내용 |
|------|------|
| 기능명 | 대리점 정보 수정 |
| 접근 권한 | platform_admin (super_admin) |
| 입력 | 수정 가능 필드 (bank_account 포함) |
| 처리 | resellers UPDATE + 변경 이력 audit_log 기록 |

### F-RESELLER-05: 대리점 상태 변경

| 항목 | 내용 |
|------|------|
| 기능명 | 대리점 상태 변경 (active↔suspended, terminated) |
| 접근 권한 | platform_admin (super_admin) |
| 처리 | resellers.status UPDATE + 감사 로그 |

---

## 6. 본사 — 수수료 정책 관리 (COMMISSION POLICY)

### F-POLICY-01: 수수료 정책 조회

| 항목 | 내용 |
|------|------|
| 기능명 | 대리점별 수수료 정책 이력 조회 |
| 접근 권한 | platform_admin (super_admin, billing_admin) |
| 출력 | plan_code, rate, effective_from, effective_to, is_active |

### F-POLICY-02: 수수료 정책 등록

| 항목 | 내용 |
|------|------|
| 기능명 | 신규 수수료 정책 등록 |
| 접근 권한 | platform_admin (super_admin) |
| 입력 | reseller_id, plan_code, rate, effective_from |
| 처리 | 1. 기존 유효 정책 effective_to = effective_from - 1일 로 자동 마감<br>2. reseller_commissions INSERT<br>3. (reseller_id, plan_code, effective_from) UK 충돌 시 400 |
| 예외 | 400: rate < 0 또는 rate > 100<br>409: 같은 기간·플랜 중복 |

---

## 7. 본사 — 수수료 정산 관리 (SETTLEMENT)

### F-SETTLEMENT-01: 정산 목록 조회

| 항목 | 내용 |
|------|------|
| 기능명 | 전체 정산 내역 목록 |
| 접근 권한 | platform_admin (super_admin, billing_admin) |
| 입력 | settlement_month (optional), status (optional), reseller_id (optional), page, size |
| 출력 | settlement_id, reseller_name, settlement_month, active_customer_count, total_revenue, total_commission, payment_amount, status |

### F-SETTLEMENT-02: 정산 생성 (배치/수동)

| 항목 | 내용 |
|------|------|
| 기능명 | 월별 정산 생성 |
| 접근 권한 | platform_admin (billing_admin, super_admin) |
| 입력 | settlement_month (YYYY-MM), reseller_id (optional: 전체 또는 특정) |
| 처리 | 1. billing_invoices WHERE status=paid AND billing_month=? 집계<br>2. reseller_commissions에서 해당 월 유효 요율 조회<br>3. commission_settlements INSERT (status=draft)<br>4. UK 충돌(중복 정산) 시 409 |
| 예외 | 400: 미래 월 정산 시도<br>409: 해당 월 정산 이미 존재 |

### F-SETTLEMENT-03: 정산 승인

| 항목 | 내용 |
|------|------|
| 기능명 | draft → approved 상태 변경 |
| 접근 권한 | platform_admin (billing_admin, super_admin) |
| 입력 | settlement_id, memo (optional) |
| 처리 | status='approved' + approval_date=TODAY + approved_by=요청자 admin_id |
| 예외 | 400: status가 draft가 아닌 경우 |

### F-SETTLEMENT-04: 정산 지급 처리

| 항목 | 내용 |
|------|------|
| 기능명 | approved → paid 상태 변경 |
| 접근 권한 | platform_admin (billing_admin, super_admin) |
| 입력 | settlement_id, payment_date, memo (optional) |
| 처리 | status='paid' + payment_date 업데이트 |

### F-SETTLEMENT-05: 정산 취소

| 항목 | 내용 |
|------|------|
| 기능명 | 정산 취소 (INSERT ONLY 원칙) |
| 접근 권한 | platform_admin (super_admin) |
| 처리 | status='cancelled' (DELETE 절대 금지) |

---

## 8. 본사 — 대리점 계정 관리 (RESELLER ACCOUNT)

### F-RA-01: 대리점 계정 목록

| 항목 | 내용 |
|------|------|
| 기능명 | 특정 대리점 소속 계정 목록 |
| 접근 권한 | platform_admin |
| 출력 | account_id, account_name, email, role, is_active, last_login_at |

### F-RA-02: 대리점 계정 생성

| 항목 | 내용 |
|------|------|
| 기능명 | 대리점 로그인 계정 생성 |
| 접근 권한 | platform_admin (super_admin) |
| 입력 | reseller_id, email, account_name, role, phone |
| 처리 | 임시 비밀번호 생성 → bcrypt 해시 → reseller_accounts INSERT → 이메일 발송 |

### F-RA-03: 대리점 계정 활성/비활성

| 항목 | 내용 |
|------|------|
| 기능명 | 계정 활성/비활성 토글 |
| 접근 권한 | platform_admin (super_admin) |
| 처리 | is_active 반전 + 비활성 시 현재 세션 무효화 |

---

## 9. 대리점 — 내 고객사 관리

### F-MY-TENANT-01: 내 담당 고객사 목록

| 항목 | 내용 |
|------|------|
| 기능명 | 담당 고객사 목록 조회 |
| 접근 권한 | reseller_admin (모든 role) |
| 보안 | JWT reseller_id 자동 필터 — WHERE tenants.reseller_id = {JWT.reseller_id} |
| 출력 | company_name, status, plan_type, user_count, next_billing_at |

### F-MY-TENANT-02: 내 담당 고객사 상세

| 항목 | 내용 |
|------|------|
| 기능명 | 담당 고객사 상세 조회 (읽기전용) |
| 접근 권한 | reseller_admin |
| 보안 | tenant.reseller_id ≠ JWT.reseller_id → 403 |
| 출력 | 기본정보 + 구독정보 + 청구 이력 (결제수단 상세 제외) |

---

## 10. 대리점 — 내 실적·수수료

### F-MY-COMMISSION-01: 내 수수료 정책 조회

| 항목 | 내용 |
|------|------|
| 기능명 | 현재 유효 수수료 정책 |
| 접근 권한 | reseller_admin |
| 출력 | plan_code, rate, effective_from 목록 |

### F-MY-COMMISSION-02: 내 정산 내역

| 항목 | 내용 |
|------|------|
| 기능명 | 본인 정산 이력 조회 |
| 접근 권한 | reseller_admin |
| 보안 | JWT reseller_id 자동 필터 |
| 출력 | settlement_month, active_customer_count, total_revenue, total_commission, payment_amount, status, payment_date |
| 정렬 | settlement_month DESC |

### F-MY-COMMISSION-03: 내 월별 실적 대시보드

| 항목 | 내용 |
|------|------|
| 기능명 | 이달 / 전월 실적 요약 |
| 접근 권한 | reseller_admin |
| 출력 | 담당 고객사 수, 이달 구독료 합계, 예상 수수료, 이달 신규 고객사 수 |

---

## 11. 대리점 — 내 계정 관리

### F-MY-ACCOUNT-01: 내 프로필 조회/수정

| 항목 | 내용 |
|------|------|
| 기능명 | 본인 계정 정보 조회 및 수정 |
| 접근 권한 | reseller_admin (본인만) |
| 수정 가능 | account_name, phone, password |

### F-MY-ACCOUNT-02: 비밀번호 변경

| 항목 | 내용 |
|------|------|
| 기능명 | 비밀번호 변경 |
| 처리 | 현재 비밀번호 검증 → bcrypt 새 비밀번호 해시 → 업데이트 |

---

## 12. 대시보드 (DASHBOARD)

### F-DASH-01: 본사 대시보드

| 항목 | 내용 |
|------|------|
| 접근 권한 | platform_admin |
| KPI 카드 | 전체 고객사 수 / 이달 신규 가입 / 이달 청구 총액 / 미납 고객사 수 / 활성 대리점 수 / 이달 수수료 지급 예정액 |
| 차트 | 월별 가입 추이(12개월) / 플랜별 분포 / 대리점별 실적 Top 5 |

### F-DASH-02: 대리점 대시보드

| 항목 | 내용 |
|------|------|
| 접근 권한 | reseller_admin |
| KPI 카드 | 담당 고객사 수 / 이달 신규 / 이달 예상 수수료 / 전월 수수료(확정) |
| 차트 | 담당 고객사 플랜별 분포 / 월별 수수료 추이(6개월) |

---

## 부록 A. 공통 에러 코드

| HTTP | 코드 | 설명 |
|------|------|------|
| 400 | INVALID_INPUT | 입력값 검증 실패 |
| 401 | UNAUTHORIZED | 인증 실패 (토큰 없음/만료) |
| 403 | FORBIDDEN | 권한 없음 또는 데이터 격리 위반 |
| 404 | NOT_FOUND | 리소스 없음 |
| 409 | CONFLICT | 중복 데이터 (UK 위반) |
| 429 | RATE_LIMITED | 요청 횟수 초과 |
| 500 | INTERNAL_ERROR | 서버 내부 오류 |

---

*이 문서 기준으로 API명세서(API_SPEC.md) 및 화면정의서(SCREEN_SPEC.md) 작성*
