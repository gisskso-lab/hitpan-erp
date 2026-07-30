# 백오피스 DB 스키마

> 작성일: 2026-05-26
> 작성: DB 매니저(Harvard·Oracle 30년) + 설계팀장 브라운킴
> 정합: DOMAIN_MODEL_BACKOFFICE.md / API_SPEC_BACKOFFICE.md
> 헌법: #4(decimal)·#17(InnoDB+utf8mb4_unicode_ci)·#18 v3·#22

---

## 0. 공통 규약

### 0.1 표준
- 엔진: InnoDB (헌법 #17)
- Collation: utf8mb4_unicode_ci (헌법 #17)
- PK: BIGINT UNSIGNED AUTO_INCREMENT 또는 BINARY(16) UUID v7
- 타임존: UTC 저장, 표시 시 KST 변환
- 금액: DECIMAL(18, 2) (헌법 #4)
- 율: DECIMAL(7, 4) (수수료율 등)
- soft delete: `deleted_at DATETIME(6) NULL`
- 감사: `created_at`, `updated_at`, `created_by`, `updated_by`

### 0.2 DB 위치
- 본사 클라우드 서버 1대 (MariaDB 11.4.10)
- DB명: `hitpan_backoffice`
- 백업: 일 1회 + 시간당 binlog (E2E 암호화, 헌법 #22)

---

## 1. 고객사 영역 (3 테이블)

### 1.1 tenants
| 컬럼 | 타입 | 제약 | 비고 |
|---|---|---|---|
| tenant_id | BINARY(16) | PK | UUID v7 |
| company_name | VARCHAR(120) | NOT NULL | |
| business_number | VARCHAR(20) | NOT NULL, UNIQUE | 사업자번호 |
| industry_category | VARCHAR(40) | NOT NULL | 서비스/도소매/제조/기타 |
| status | ENUM('ACTIVE','SUSPENDED','CANCELLED','DELETED') | NOT NULL, DEFAULT 'ACTIVE' | |
| current_tier_code | ENUM('LITE','STANDARD','PRO') | NOT NULL | |
| device_count_max | SMALLINT UNSIGNED | NOT NULL | |
| reseller_id | BINARY(16) | NULL, FK resellers | |
| memo | TEXT | NULL | |
| created_at | DATETIME(6) | NOT NULL | |
| updated_at | DATETIME(6) | NOT NULL | |
| deleted_at | DATETIME(6) | NULL | soft delete |

- INDEX: `idx_status (status)`, `idx_business_number (business_number)`, `idx_reseller (reseller_id)`

### 1.2 tenant_business_info
- `info_id` (PK), `tenant_id` (FK), `legal_name`, `representative_name`, `address`, `tax_office`, `business_type`, `business_category`, `business_license_file_url` (R2 URL — 본사 DB는 메타만, 헌법 #22)

### 1.3 tenant_contacts
- `contact_id` (PK), `tenant_id` (FK), `role` (REPRESENTATIVE/BILLING/TECH), `name`, `phone_encrypted` (AES-256), `email_encrypted` (AES-256), `is_primary`

---

## 2. 구독 영역 (3 테이블)

### 2.1 subscriptions
| 컬럼 | 타입 | 제약 |
|---|---|---|
| subscription_id | BINARY(16) | PK |
| tenant_id | BINARY(16) | NOT NULL, FK tenants |
| tier_id | BIGINT UNSIGNED | NOT NULL, FK subscription_plans |
| billing_period | ENUM('MONTHLY','ANNUAL') | NOT NULL |
| status | ENUM('ACTIVE','PAST_DUE','CANCELLATION_SCHEDULED','CANCELLED','PAUSED') | NOT NULL |
| starts_at | DATETIME(6) | NOT NULL |
| current_period_start | DATETIME(6) | NOT NULL |
| current_period_end | DATETIME(6) | NOT NULL |
| cancellation_scheduled_at | DATETIME(6) | NULL |
| cancelled_at | DATETIME(6) | NULL |
| created_at, updated_at | DATETIME(6) | NOT NULL |

- INDEX: `idx_tenant_status (tenant_id, status)`, `idx_period_end (current_period_end)`

### 2.2 subscription_plans
- `plan_id` (PK), `tier_code` (UNIQUE), `tier_name`, `monthly_price DECIMAL(18,2)`, `annual_price DECIMAL(18,2)`, `device_count_max`, `is_active`

### 2.3 billing_cycles
- `cycle_id` (PK), `subscription_id` (FK), `period_start`, `period_end`, `amount DECIMAL(18,2)`, `status` (SCHEDULED/CHARGED/FAILED/SKIPPED), `payment_id` (FK payments NULL)

---

## 3. 계정 영역 (3 테이블, 본사 데이터 최소주의 정합)

### 3.1 accounts (대표자·자식 메타정보만 — 헌법 #18 v3)
- `account_id` (PK), `tenant_id` (FK), `email_encrypted` (AES-256), `is_representative` (bool), `last_login_at`, `created_at`
- **비밀번호 보관 금지** — 인증은 ERP 로컬에서 처리, 본사는 이메일 메타만

### 3.2 account_roles
- `role_id` (PK), `account_id` (FK), `role_code` (TENANT_ADMIN/EMPLOYEE/READONLY)

### 3.3 account_permissions
- `permission_id` (PK), `account_id` (FK), `permission_key`, `granted_at`, `granted_by`

---

## 4. 결제 영역 (4 테이블)

### 4.1 payments
| 컬럼 | 타입 | 제약 |
|---|---|---|
| payment_id | BINARY(16) | PK |
| tenant_id | BINARY(16) | NOT NULL, FK |
| subscription_id | BINARY(16) | NOT NULL, FK |
| billing_cycle_id | BIGINT UNSIGNED | NULL, FK |
| amount | DECIMAL(18,2) | NOT NULL |
| currency | CHAR(3) | NOT NULL, DEFAULT 'KRW' |
| provider | ENUM('TOSS','KCP','MOCK') | NOT NULL |
| provider_payment_id | VARCHAR(120) | NOT NULL |
| payment_method_type | ENUM('CARD','BANK_TRANSFER','TAX_INVOICE') | NOT NULL |
| status | ENUM('PENDING','APPROVED','FAILED','REFUNDED','PARTIAL_REFUNDED') | NOT NULL |
| paid_at | DATETIME(6) | NULL |
| created_at, updated_at | DATETIME(6) | NOT NULL |

- INDEX: `idx_tenant_paid_at (tenant_id, paid_at)`, `idx_provider_pid (provider, provider_payment_id) UNIQUE`

### 4.2 payment_methods (토큰만, 헌법 #22)
- `method_id` (PK), `tenant_id` (FK), `provider`, `provider_billing_key`, `card_last4` (4자리만), `card_brand`, `is_default`, `registered_at`
- **카드 원본·CVC·만료일 보관 절대 금지**

### 4.3 payment_refunds
- `refund_id` (PK), `payment_id` (FK), `amount DECIMAL(18,2)`, `reason`, `provider_refund_id`, `status`, `refunded_at`, `processed_by` (FK admin_users)

### 4.4 invoices
- `invoice_id` (PK), `payment_id` (FK), `invoice_type` (RECEIPT/TAX_INVOICE), `external_url` (이세로·결제사 위임), `issued_at`

---

## 5. 대리점 영역 (5 테이블)

### 5.1 resellers
- `reseller_id` (PK BINARY(16)), `company_name`, `business_number UNIQUE`, `representative_name`, `contact_phone_encrypted`, `commission_rate DECIMAL(7,4)`, `status`, `joined_at`

### 5.2 reseller_contracts
- `contract_id` (PK), `reseller_id` (FK), `contract_type`, `effective_from`, `effective_to`, `commission_rate DECIMAL(7,4)`, `signed_at`, `contract_file_url` (R2)

### 5.3 reseller_customers (매핑)
- `mapping_id` (PK), `reseller_id` (FK), `tenant_id` (FK), `assigned_at`, `released_at NULL`
- UNIQUE: `(reseller_id, tenant_id)` (released_at NULL인 행만)

### 5.4 commissions
- `commission_id` (PK), `reseller_id` (FK), `tenant_id` (FK), `payment_id` (FK), `period_year`, `period_month`, `base_amount DECIMAL(18,2)`, `commission_rate DECIMAL(7,4)`, `commission_amount DECIMAL(18,2)`, `calculated_at`

### 5.5 commission_settlements
- `settlement_id` (PK), `reseller_id` (FK), `period_year`, `period_month`, `total_amount DECIMAL(18,2)`, `settle_method`, `settled_at`, `settled_by` (FK admin_users), `memo`

---

## 6. 본사 직원 영역 (4 테이블, ERP tenant_users와 절대 분리 — 헌법 #7)

### 6.1 admin_users
- `admin_user_id` (PK BINARY(16)), `username UNIQUE`, `email_encrypted`, `password_hash` (Argon2id), `display_name`, `status`, `last_login_at`, `created_at`

### 6.2 admin_roles
- `role_id` (PK), `admin_user_id` (FK), `role_code` (SUPER_ADMIN/OPS/SALES/SUPPORT/FINANCE/RESELLER_ADMIN/READ_ONLY)
- UNIQUE: `(admin_user_id, role_code)`

### 6.3 admin_sessions
- `session_id` (PK BINARY(16)), `admin_user_id` (FK), `refresh_token_hash`, `client_ip`, `user_agent`, `issued_at`, `expires_at`, `revoked_at NULL`

### 6.4 admin_2fa
- `tfa_id` (PK), `admin_user_id` (FK UNIQUE), `totp_secret_encrypted` (AES-256), `backup_codes_encrypted` (AES-256), `enrolled_at`, `last_used_at`

---

## 7. 모니터링 영역 (3 테이블, 메타만 — 헌법 #18 v3)

### 7.1 tenant_heartbeats
- `heartbeat_id` (PK BIGINT), `tenant_id` (FK), `device_id` (워치독 ID), `pinged_at`, `cloudflared_status`, `mariadb_status`, `erp_status`, `version`
- **업무 데이터 카운터 0건** — ping 메타만
- 파티션: pinged_at 기준 월별 파티셔닝

### 7.2 tenant_usage_metrics
- `metric_id` (PK), `tenant_id` (FK), `metric_date`, `device_count_active`, `db_size_mb` (라이선스 검증용), `last_collected_at`

### 7.3 tenant_alerts
- `alert_id` (PK), `tenant_id` (FK), `alert_type` (HEARTBEAT_LOST/PAYMENT_FAILED/DISK_FULL/CERT_EXPIRING), `severity` (INFO/WARNING/CRITICAL), `message`, `triggered_at`, `resolved_at NULL`, `assigned_to` (FK admin_users NULL)

---

## 8. 25 테이블 요약

| # | 테이블 | 영역 | 핵심 FK |
|---|---|---|---|
| 1 | tenants | 고객사 | reseller_id |
| 2 | tenant_business_info | 고객사 | tenant_id |
| 3 | tenant_contacts | 고객사 | tenant_id |
| 4 | subscriptions | 구독 | tenant_id, tier_id |
| 5 | subscription_plans | 구독 | - |
| 6 | billing_cycles | 구독 | subscription_id, payment_id |
| 7 | accounts | 계정 | tenant_id |
| 8 | account_roles | 계정 | account_id |
| 9 | account_permissions | 계정 | account_id |
| 10 | payments | 결제 | tenant_id, subscription_id |
| 11 | payment_methods | 결제 | tenant_id |
| 12 | payment_refunds | 결제 | payment_id |
| 13 | invoices | 결제 | payment_id |
| 14 | resellers | 대리점 | - |
| 15 | reseller_contracts | 대리점 | reseller_id |
| 16 | reseller_customers | 대리점 | reseller_id, tenant_id |
| 17 | commissions | 대리점 | reseller_id, tenant_id, payment_id |
| 18 | commission_settlements | 대리점 | reseller_id |
| 19 | admin_users | 본사 직원 | - |
| 20 | admin_roles | 본사 직원 | admin_user_id |
| 21 | admin_sessions | 본사 직원 | admin_user_id |
| 22 | admin_2fa | 본사 직원 | admin_user_id |
| 23 | tenant_heartbeats | 모니터링 | tenant_id |
| 24 | tenant_usage_metrics | 모니터링 | tenant_id |
| 25 | tenant_alerts | 모니터링 | tenant_id |

---

## 9. 헌법 정합 체크

| 헌법 | 적용 |
|---|---|
| #4 decimal | 모든 금액 DECIMAL(18,2), 모든 율 DECIMAL(7,4) |
| #17 InnoDB+utf8mb4_unicode_ci | 25 테이블 일괄 적용 |
| #18 v3 | tenant_heartbeats·usage_metrics 메타만, 업무 데이터 0 |
| #22 | 카드 원본·비밀번호·CI 0건 / 사업자등록증·계약서 R2 위임 / 백업 E2E 암호화 |

---

## 10. 사장님 결재 영역
- BINARY(16) UUID v7 vs BIGINT AUTO_INCREMENT — UUID 선택 결재
- 파티셔닝 정책 (heartbeats 월별) 결재
- 백업 보관 기한 (30일/90일/1년) 결재
- AES-256 키 회전 주기 결재

## 11. W3 가도 예고
- DDL SQL 본문 박제 (25 테이블 CREATE TABLE)
- 마이그레이션 스크립트 (V1__init.sql)
- 인덱스 튜닝 (slow query 예측)
- Value Converter 명세 (BINARY(16) UUID, AES-256 컬럼)
