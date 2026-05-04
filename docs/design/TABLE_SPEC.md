# 히트판 백오피스 — 테이블명세서
> 버전: 1.0 | 기준일: 2026-05-05 | 작성자: DB매니저

---

## 1. 범위

백오피스 신규 테이블 5개 + 연관 기존 테이블 5개 = 총 10개 테이블.

---

## 2. 신규 테이블

### 2-1. `platform_admins` — 본사 관리자

| # | 컬럼명 | 타입 | NULL | 기본값 | 제약 | 설명 |
|---|--------|------|------|--------|------|------|
| 1 | admin_id | CHAR(36) | NO | — | PK | UUID v4 |
| 2 | email | VARCHAR(100) | NO | — | UK | 로그인 이메일 |
| 3 | password_hash | VARCHAR(256) | NO | — | — | bcrypt(cost=12) |
| 4 | admin_name | VARCHAR(50) | NO | — | — | 담당자명 |
| 5 | role | ENUM | NO | — | — | super_admin\|billing_admin\|cs_admin\|readonly |
| 6 | department | VARCHAR(50) | YES | NULL | — | 부서명 |
| 7 | is_active | TINYINT(1) | NO | 1 | — | 1=활성, 0=비활성 |
| 8 | last_login_at | DATETIME | YES | NULL | — | 마지막 로그인 시각 |
| 9 | created_at | DATETIME | NO | CURRENT_TIMESTAMP | — | 생성일시 |
| 10 | updated_at | DATETIME | NO | CURRENT_TIMESTAMP ON UPDATE | — | 수정일시 |

**role 권한표**

| role | 고객사 조회 | 구독 관리 | 대리점 관리 | 정산 승인 | 관리자 관리 |
|------|-----------|-----------|-----------|---------|-----------|
| super_admin | O | O | O | O | O |
| billing_admin | O(읽기) | O | O(읽기) | O | X |
| cs_admin | O | X | X | X | X |
| readonly | O(읽기) | X | X | X | X |

**인덱스**

| 인덱스명 | 컬럼 | 종류 |
|---------|------|------|
| PRIMARY | admin_id | PK |
| uk_email | email | UNIQUE |
| idx_role_active | role, is_active | BTREE |

---

### 2-2. `resellers` — 대리점 마스터

| # | 컬럼명 | 타입 | NULL | 기본값 | 제약 | 설명 |
|---|--------|------|------|--------|------|------|
| 1 | reseller_id | CHAR(36) | NO | — | PK | UUID v4 |
| 2 | reseller_code | VARCHAR(20) | NO | — | UK | 대리점 코드 (예: RS-001) |
| 3 | reseller_name | VARCHAR(100) | NO | — | — | 대리점 상호 |
| 4 | biz_no | VARCHAR(12) | NO | — | UK | 사업자등록번호 (000-00-00000) |
| 5 | ceo_name | VARCHAR(50) | NO | — | — | 대표자명 |
| 6 | tel | VARCHAR(20) | YES | NULL | — | 대표 전화번호 |
| 7 | address | VARCHAR(200) | YES | NULL | — | 사업장 주소 |
| 8 | bank_name | VARCHAR(30) | YES | NULL | — | 은행명 |
| 9 | bank_account | VARCHAR(200) | YES | NULL | — | 계좌번호 (AES-256 암호화) |
| 10 | account_holder | VARCHAR(30) | YES | NULL | — | 예금주명 |
| 11 | contact_person | VARCHAR(50) | YES | NULL | — | 담당자명 |
| 12 | contact_phone | VARCHAR(20) | YES | NULL | — | 담당자 연락처 |
| 13 | contact_email | VARCHAR(100) | YES | NULL | — | 담당자 이메일 |
| 14 | join_date | DATE | NO | — | — | 계약 시작일 |
| 15 | status | ENUM | NO | 'active' | — | active\|suspended\|inactive\|terminated |
| 16 | created_at | DATETIME | NO | CURRENT_TIMESTAMP | — | 생성일시 |
| 17 | updated_at | DATETIME | NO | CURRENT_TIMESTAMP ON UPDATE | — | 수정일시 |
| 18 | created_by | CHAR(36) | YES | NULL | FK→platform_admins | 등록 관리자 ID |

**status 상태 전이**

```
active → suspended (일시중지)
active → terminated (계약종료)
suspended → active (복구)
suspended → terminated (종료)
inactive → active (재계약)
```

**인덱스**

| 인덱스명 | 컬럼 | 종류 |
|---------|------|------|
| PRIMARY | reseller_id | PK |
| uk_reseller_code | reseller_code | UNIQUE |
| uk_biz_no | biz_no | UNIQUE |
| idx_status | status | BTREE |
| idx_join_date | join_date | BTREE |

---

### 2-3. `reseller_accounts` — 대리점 로그인 계정

| # | 컬럼명 | 타입 | NULL | 기본값 | 제약 | 설명 |
|---|--------|------|------|--------|------|------|
| 1 | account_id | CHAR(36) | NO | — | PK | UUID v4 |
| 2 | reseller_id | CHAR(36) | NO | — | FK→resellers | 소속 대리점 |
| 3 | email | VARCHAR(100) | NO | — | — | 로그인 이메일 (대리점 내 UK) |
| 4 | password_hash | VARCHAR(256) | NO | — | — | bcrypt(cost=12) |
| 5 | account_name | VARCHAR(50) | NO | — | — | 담당자명 |
| 6 | role | ENUM | NO | — | — | reseller_admin\|reseller_user\|reseller_readonly |
| 7 | phone | VARCHAR(20) | YES | NULL | — | 연락처 |
| 8 | is_active | TINYINT(1) | NO | 1 | — | 1=활성, 0=비활성 |
| 9 | last_login_at | DATETIME | YES | NULL | — | 마지막 로그인 시각 |
| 10 | created_at | DATETIME | NO | CURRENT_TIMESTAMP | — | 생성일시 |
| 11 | updated_at | DATETIME | NO | CURRENT_TIMESTAMP ON UPDATE | — | 수정일시 |

**UK 복합**: `(reseller_id, email)` — 대리점 내 이메일 중복 방지

**role 권한표**

| role | 고객사 조회 | 수수료 조회 | 계정 관리 |
|------|-----------|-----------|---------|
| reseller_admin | 본인 담당만 | 본인만 | O |
| reseller_user | 본인 담당만 | 본인만 | X |
| reseller_readonly | 본인 담당만 읽기 | 본인만 읽기 | X |

**인덱스**

| 인덱스명 | 컬럼 | 종류 |
|---------|------|------|
| PRIMARY | account_id | PK |
| uk_reseller_email | reseller_id, email | UNIQUE |
| idx_reseller | reseller_id | BTREE |
| idx_active | is_active | BTREE |

---

### 2-4. `reseller_commissions` — 수수료 정책

| # | 컬럼명 | 타입 | NULL | 기본값 | 제약 | 설명 |
|---|--------|------|------|--------|------|------|
| 1 | commission_id | CHAR(36) | NO | — | PK | UUID v4 |
| 2 | reseller_id | CHAR(36) | NO | — | FK→resellers | 대리점 ID |
| 3 | plan_code | VARCHAR(20) | NO | — | — | basic\|pro\|enterprise |
| 4 | rate | DECIMAL(5,2) | NO | — | — | 수수료율(%) 예: 20.00 |
| 5 | effective_from | DATE | NO | — | — | 적용 시작일 |
| 6 | effective_to | DATE | YES | NULL | — | 적용 종료일 (NULL=현재 유효) |
| 7 | is_active | TINYINT(1) | NO | 1 | — | 1=활성 정책 |
| 8 | created_at | DATETIME | NO | CURRENT_TIMESTAMP | — | 생성일시 |
| 9 | created_by | CHAR(36) | YES | NULL | FK→platform_admins | 등록 관리자 |

**UK 복합**: `(reseller_id, plan_code, effective_from)` — 같은 기간·플랜 중복 방지

**현재 유효 정책 조회 쿼리**
```sql
SELECT rate FROM reseller_commissions
WHERE reseller_id = ?
  AND plan_code = ?
  AND effective_from <= CURDATE()
  AND (effective_to IS NULL OR effective_to >= CURDATE())
  AND is_active = 1
ORDER BY effective_from DESC
LIMIT 1;
```

**인덱스**

| 인덱스명 | 컬럼 | 종류 |
|---------|------|------|
| PRIMARY | commission_id | PK |
| uk_reseller_plan_from | reseller_id, plan_code, effective_from | UNIQUE |
| idx_reseller_active | reseller_id, is_active | BTREE |
| idx_effective | effective_from, effective_to | BTREE |

---

### 2-5. `commission_settlements` — 월별 수수료 정산

| # | 컬럼명 | 타입 | NULL | 기본값 | 제약 | 설명 |
|---|--------|------|------|--------|------|------|
| 1 | settlement_id | CHAR(36) | NO | — | PK | UUID v4 |
| 2 | reseller_id | CHAR(36) | NO | — | FK→resellers | 대리점 ID |
| 3 | settlement_month | CHAR(7) | NO | — | UK복합 | 정산월 (YYYY-MM) |
| 4 | status | ENUM | NO | 'draft' | — | draft\|approved\|paid\|cancelled |
| 5 | active_customer_count | INT | NO | 0 | — | 담당 활성 고객사 수 |
| 6 | total_revenue | DECIMAL(15,2) | NO | 0.00 | — | 담당 고객사 구독료 합계 |
| 7 | total_commission | DECIMAL(15,2) | NO | 0.00 | — | total_revenue × rate / 100 |
| 8 | deduction_amount | DECIMAL(15,2) | NO | 0.00 | — | 공제액 |
| 9 | payment_amount | DECIMAL(15,2) | NO | 0.00 | — | 실지급액 (total_commission - deduction_amount) |
| 10 | payment_date | DATE | YES | NULL | — | 실제 지급일 |
| 11 | approval_date | DATE | YES | NULL | — | 승인일 |
| 12 | approved_by | CHAR(36) | YES | NULL | FK→platform_admins | 승인자 |
| 13 | memo | VARCHAR(500) | YES | NULL | — | 관리자 메모 |
| 14 | created_at | DATETIME | NO | CURRENT_TIMESTAMP | — | 생성일시 |
| 15 | updated_at | DATETIME | NO | CURRENT_TIMESTAMP ON UPDATE | — | 수정일시 |

**UK 복합**: `(reseller_id, settlement_month)` — 대리점당 월 1회만

**status 상태 전이**
```
draft → approved (billing_admin 또는 super_admin 승인)
approved → paid (지급 완료 처리)
draft → cancelled
approved → cancelled
```

**비즈니스 규칙**
- INSERT ONLY 원칙 적용 — 취소 시 status='cancelled' (DELETE 금지)
- 금액은 모두 DECIMAL(15,2) — float/double 금지
- total_commission = total_revenue × rate / 100 (DB 저장 시점 고정)
- payment_amount = total_commission - deduction_amount

**인덱스**

| 인덱스명 | 컬럼 | 종류 |
|---------|------|------|
| PRIMARY | settlement_id | PK |
| uk_reseller_month | reseller_id, settlement_month | UNIQUE |
| idx_status | status | BTREE |
| idx_month | settlement_month | BTREE |
| idx_payment_date | payment_date | BTREE |

---

## 3. 기존 연관 테이블 (변경 없음)

### 3-1. `tenants` — 고객사 (테넌트)

| # | 컬럼명 | 타입 | NULL | 비고 |
|---|--------|------|------|------|
| 1 | tenant_id | CHAR(36) | NO | PK |
| 2 | tenant_code | VARCHAR(20) | NO | UK |
| 3 | company_name | VARCHAR(100) | NO | 상호명 |
| 4 | biz_no | VARCHAR(12) | YES | 사업자번호 |
| 5 | ceo_name | VARCHAR(50) | YES | 대표자명 |
| 6 | tel | VARCHAR(20) | YES | 대표 전화 |
| 7 | address | VARCHAR(200) | YES | 주소 |
| 8 | reseller_id | CHAR(36) | YES | FK→resellers (NULL=직계약) |
| 9 | status | ENUM | NO | trial\|active\|suspended\|expired |
| 10 | trial_ends_at | DATETIME | YES | 체험 만료일 |
| 11 | db_host | VARCHAR(100) | YES | DB 호스트 |
| 12 | db_name | VARCHAR(50) | YES | DB 이름 |
| 13 | license_key_hash | VARCHAR(256) | YES | 라이선스 키 해시 |
| 14 | reseller_tier | TINYINT | NO | 0=일반, 1=대리점, 2=총판 |
| 15 | created_at | DATETIME | NO | — |
| 16 | updated_at | DATETIME | NO | — |

**백오피스 연관 포인트**: `reseller_id` FK 공식화 (ALTER TABLE로 FK 추가 필요)

---

### 3-2. `subscriptions` — 구독 이력

| # | 컬럼명 | 타입 | NULL | 비고 |
|---|--------|------|------|------|
| 1 | subscription_id | CHAR(36) | NO | PK |
| 2 | tenant_id | CHAR(36) | NO | FK→tenants |
| 3 | plan_type | ENUM | NO | basic\|pro\|enterprise |
| 4 | base_users | TINYINT | NO | 기본 사용자 수 |
| 5 | extra_users | TINYINT | NO | 추가 사용자 수 |
| 6 | base_fee | INT | NO | 기본료 (원) |
| 7 | extra_fee_per_user | INT | NO | 추가 사용자 단가 |
| 8 | billing_cycle | ENUM | NO | monthly\|yearly |
| 9 | started_at | DATE | NO | 구독 시작일 |
| 10 | ends_at | DATE | YES | 구독 종료일 |
| 11 | next_billing_at | DATE | YES | 다음 결제일 |
| 12 | status | ENUM | NO | active\|suspended\|cancelled\|expired |
| 13 | created_at | DATETIME | NO | — |
| 14 | updated_at | DATETIME | NO | — |

---

### 3-3. `billing_invoices` — 청구서

| # | 컬럼명 | 타입 | NULL | 비고 |
|---|--------|------|------|------|
| 1 | invoice_id | CHAR(36) | NO | PK |
| 2 | tenant_id | CHAR(36) | NO | FK→tenants |
| 3 | subscription_id | CHAR(36) | NO | FK→subscriptions |
| 4 | billing_month | CHAR(7) | NO | UK복합 YYYY-MM |
| 5 | user_count | TINYINT | NO | 청구 기준 사용자 수 |
| 6 | base_amount | INT | NO | 기본료 |
| 7 | extra_amount | INT | NO | 추가료 |
| 8 | total_amount | INT | NO | 합계 |
| 9 | status | ENUM | NO | pending\|paid\|failed\|cancelled |
| 10 | paid_at | DATETIME | YES | 결제 완료 시각 |
| 11 | payment_key | VARCHAR(200) | YES | PG 결제키 |
| 12 | fail_reason | VARCHAR(200) | YES | 실패 사유 |
| 13 | retry_count | TINYINT | NO | 재시도 횟수 |
| 14 | next_retry_at | DATETIME | YES | 다음 재시도 예정 |
| 15 | created_at | DATETIME | NO | — |
| 16 | updated_at | DATETIME | NO | — |

**수수료 정산 집계 쿼리**
```sql
SELECT SUM(bi.total_amount) AS total_revenue
FROM billing_invoices bi
JOIN tenants t ON bi.tenant_id = t.tenant_id
WHERE t.reseller_id = ?
  AND bi.billing_month = ?
  AND bi.status = 'paid';
```

---

### 3-4. `billing_keys` — 결제수단

| # | 컬럼명 | 타입 | NULL | 비고 |
|---|--------|------|------|------|
| 1 | billing_key_id | CHAR(36) | NO | PK |
| 2 | tenant_id | CHAR(36) | NO | FK→tenants |
| 3 | provider | ENUM | NO | toss\|manual |
| 4 | method_type | ENUM | NO | card\|bank |
| 5 | billing_key | VARCHAR(200) | NO | 토스 빌링키 |
| 6 | masked_no | VARCHAR(20) | YES | 마스킹 카드번호 |
| 7 | card_name | VARCHAR(50) | YES | 카드사명 |
| 8 | is_default | TINYINT(1) | NO | 기본 결제수단 여부 |
| 9 | is_active | TINYINT(1) | NO | 활성 여부 |
| 10 | expired_at | DATE | YES | 카드 만료일 |
| 11 | created_at | DATETIME | NO | — |

---

### 3-5. `users` — ERP 사용자

| # | 컬럼명 | 타입 | NULL | 비고 |
|---|--------|------|------|------|
| 1 | user_id | CHAR(36) | NO | PK |
| 2 | tenant_id | CHAR(36) | NO | FK→tenants |
| 3 | email | VARCHAR(100) | NO | UK복합 (tenant_id, email) |
| 4 | password_hash | VARCHAR(256) | NO | bcrypt |
| 5 | user_name | VARCHAR(50) | NO | — |
| 6 | role | ENUM | NO | tenant_admin\|manager\|user\|readonly |
| 7 | dept_id | CHAR(36) | YES | 부서 ID |
| 8 | phone | VARCHAR(20) | YES | — |
| 9 | is_active | TINYINT(1) | NO | — |
| 10 | last_login_at | DATETIME | YES | — |
| 11 | created_at | DATETIME | NO | — |
| 12 | updated_at | DATETIME | NO | — |

---

## 4. DDL 작업지시 순서

```
1. platform_admins       (의존성 없음)
2. resellers             (의존성 없음)
3. reseller_accounts     (→ resellers)
4. reseller_commissions  (→ resellers)
5. commission_settlements (→ resellers, platform_admins)
6. ALTER TABLE tenants ADD CONSTRAINT FK reseller_id → resellers
```

---

## 5. 공통 DDL 원칙

- `ENGINE=InnoDB` 명시 (헌법 #17)
- `DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci` 통일
- 금액 컬럼: `DECIMAL(15,2)` — float/double 금지 (헌법 #4)
- 암호화 컬럼: `bank_account` → AES-256 Value Converter 필수 (헌법 #5)
- 모든 PK: `CHAR(36)` UUID v4

---

*이 문서 기준으로 DB-39 DDL 작업지시서 발행*
