# 히트판 ERP + 백오피스 — ERD
> 버전: 1.0 | 기준일: 2026-05-05

---

## 전체 구조 개요

```
[백오피스 계층]                    [ERP 계층]
platform_admins                    tenants ─────────────────┐
      │                                │                    │
resellers ◄──────────────────── tenants.reseller_id         │
      │                                │                    │
reseller_accounts              subscriptions          users (ERP)
      │                                │                    │
reseller_commissions            billing_invoices      (ERP 업무 테이블들)
      │                                │
commission_settlements ─────────billing_invoices
```

---

## ERD (Mermaid)

```mermaid
erDiagram

  %% ══════════════════════════════════════
  %% 백오피스 — 본사
  %% ══════════════════════════════════════

  platform_admins {
    CHAR36    admin_id          PK
    VARCHAR100 email            UK
    VARCHAR256 password_hash
    VARCHAR50  admin_name
    ENUM       role             "super_admin|billing_admin|cs_admin|readonly"
    VARCHAR50  department
    TINYINT1   is_active
    DATETIME   last_login_at
    DATETIME   created_at
    DATETIME   updated_at
  }

  %% ══════════════════════════════════════
  %% 백오피스 — 대리점
  %% ══════════════════════════════════════

  resellers {
    CHAR36    reseller_id       PK
    VARCHAR20  reseller_code    UK
    VARCHAR100 reseller_name
    VARCHAR12  biz_no           UK
    VARCHAR50  ceo_name
    VARCHAR20  tel
    VARCHAR200 address
    VARCHAR30  bank_name
    VARCHAR200 bank_account     "AES-256 암호화"
    VARCHAR30  account_holder
    VARCHAR50  contact_person
    VARCHAR20  contact_phone
    VARCHAR100 contact_email
    DATE       join_date
    ENUM       status           "active|suspended|inactive|terminated"
    DATETIME   created_at
    DATETIME   updated_at
    CHAR36     created_by
  }

  reseller_accounts {
    CHAR36    account_id        PK
    CHAR36    reseller_id       FK
    VARCHAR100 email
    VARCHAR256 password_hash
    VARCHAR50  account_name
    ENUM       role             "reseller_admin|reseller_user|reseller_readonly"
    VARCHAR20  phone
    TINYINT1   is_active
    DATETIME   last_login_at
    DATETIME   created_at
    DATETIME   updated_at
  }

  reseller_commissions {
    CHAR36    commission_id     PK
    CHAR36    reseller_id       FK
    VARCHAR20  plan_code        "basic|pro|enterprise"
    DECIMAL52  rate             "수수료율(%) ex: 20.00"
    DATE       effective_from
    DATE       effective_to     "NULL=현재 유효"
    TINYINT1   is_active
    DATETIME   created_at
    CHAR36     created_by
  }

  commission_settlements {
    CHAR36    settlement_id     PK
    CHAR36    reseller_id       FK
    CHAR7      settlement_month UK_복합 "YYYY-MM"
    ENUM       status           "draft|approved|paid|cancelled"
    INT        active_customer_count
    DECIMAL152 total_revenue    "담당 고객사 구독료 합계"
    DECIMAL152 total_commission "total_revenue x rate"
    DECIMAL152 deduction_amount
    DECIMAL152 payment_amount   "실지급액"
    DATE       payment_date
    DATE       approval_date
    CHAR36     approved_by      FK_platform_admins
    VARCHAR500 memo
    DATETIME   created_at
    DATETIME   updated_at
  }

  %% ══════════════════════════════════════
  %% ERP — 테넌트 (기존)
  %% ══════════════════════════════════════

  tenants {
    CHAR36    tenant_id         PK
    VARCHAR20  tenant_code      UK
    VARCHAR100 company_name
    VARCHAR12  biz_no
    VARCHAR50  ceo_name
    VARCHAR20  tel
    VARCHAR200 address
    CHAR36     reseller_id      FK "NULL=직계약"
    ENUM       status           "trial|active|suspended|expired"
    DATETIME   trial_ends_at
    VARCHAR100 db_host
    VARCHAR50  db_name
    VARCHAR256 license_key_hash
    TINYINT    reseller_tier    "0=일반 1=대리점 2=총판"
    DATETIME   created_at
    DATETIME   updated_at
  }

  %% ══════════════════════════════════════
  %% ERP — 사용자 (기존)
  %% ══════════════════════════════════════

  users {
    CHAR36    user_id           PK
    CHAR36    tenant_id         FK
    VARCHAR100 email            UK_복합
    VARCHAR256 password_hash
    VARCHAR50  user_name
    ENUM       role             "tenant_admin|manager|user|readonly"
    CHAR36     dept_id
    VARCHAR20  phone
    TINYINT1   is_active
    DATETIME   last_login_at
    DATETIME   created_at
    DATETIME   updated_at
  }

  %% ══════════════════════════════════════
  %% ERP — 구독·결제 (기존)
  %% ══════════════════════════════════════

  subscriptions {
    CHAR36    subscription_id   PK
    CHAR36    tenant_id         FK
    ENUM      plan_type         "basic|pro|enterprise"
    TINYINT   base_users
    TINYINT   extra_users
    INT       base_fee
    INT       extra_fee_per_user
    ENUM      billing_cycle     "monthly|yearly"
    DATE      started_at
    DATE      ends_at
    DATE      next_billing_at
    ENUM      status            "active|suspended|cancelled|expired"
    DATETIME  created_at
    DATETIME  updated_at
  }

  billing_invoices {
    CHAR36    invoice_id        PK
    CHAR36    tenant_id         FK
    CHAR36    subscription_id   FK
    CHAR7     billing_month     UK_복합 "YYYY-MM"
    TINYINT   user_count
    INT       base_amount
    INT       extra_amount
    INT       total_amount
    ENUM      status            "pending|paid|failed|cancelled"
    DATETIME  paid_at
    VARCHAR200 payment_key
    VARCHAR200 fail_reason
    TINYINT   retry_count
    DATETIME  next_retry_at
    DATETIME  created_at
    DATETIME  updated_at
  }

  billing_keys {
    CHAR36    billing_key_id    PK
    CHAR36    tenant_id         FK
    ENUM      provider          "toss|manual"
    ENUM      method_type       "card|bank"
    VARCHAR200 billing_key
    VARCHAR20  masked_no
    VARCHAR50  card_name
    TINYINT1   is_default
    TINYINT1   is_active
    DATE       expired_at
    DATETIME   created_at
  }

  %% ══════════════════════════════════════
  %% 관계 정의
  %% ══════════════════════════════════════

  resellers                ||--o{ reseller_accounts       : "1 대리점 N 계정"
  resellers                ||--o{ reseller_commissions    : "1 대리점 N 수수료정책"
  resellers                ||--o{ commission_settlements  : "1 대리점 N 월정산"
  resellers                ||--o{ tenants                 : "1 대리점 N 고객사"
  platform_admins          ||--o{ commission_settlements  : "승인자"
  tenants                  ||--o{ users                   : "1 고객사 N 사용자"
  tenants                  ||--o{ subscriptions           : "1 고객사 N 구독이력"
  tenants                  ||--o{ billing_invoices        : "1 고객사 N 청구서"
  tenants                  ||--o{ billing_keys            : "1 고객사 N 결제수단"
  subscriptions            ||--o{ billing_invoices        : "1 구독 N 청구서"
```

---

## 테이블별 상세 설명

### 백오피스 신규 테이블 (5개)

#### `platform_admins` — 본사 관리자
- 히트판 운영팀 계정
- `role`: super_admin(모든권한) / billing_admin(정산) / cs_admin(CS) / readonly
- ERP users와 완전 분리 — 별도 JWT 클레임 `account_type: platform_admin`

#### `resellers` — 대리점 마스터
- 대리점 1개 = 1행
- `bank_account`: AES-256 암호화 (수수료 정산 지급용)
- `status`: active(정상) / suspended(일시중지) / inactive(비활성) / terminated(계약종료)
- `tenants.reseller_id` → 이 테이블 FK

#### `reseller_accounts` — 대리점 로그인 계정
- 대리점당 N명 가능 (reseller_admin / reseller_user / reseller_readonly)
- UK: `(reseller_id, email)` — 대리점 내 이메일 중복 방지
- 로그인 시 JWT에 `account_type: reseller_admin`, `reseller_id` 포함

#### `reseller_commissions` — 수수료 정책
- 대리점별·플랜별·기간별 수수료율 이력 관리
- `effective_to = NULL` = 현재 유효한 정책
- UK: `(reseller_id, plan_code, effective_from)` — 같은 기간 중복 정책 방지
- 과거 정산 시 해당 시점 요율 추적 가능

#### `commission_settlements` — 월별 수수료 정산
- 월 1회 생성 (배치 또는 수동)
- UK: `(reseller_id, settlement_month)` — 중복 정산 방지
- `status` 흐름: draft → approved → paid (→ cancelled)
- `total_revenue`: 담당 고객사 `billing_invoices.total_amount` 합계
- `total_commission`: total_revenue × rate / 100
- `payment_amount`: total_commission - deduction_amount

---

### 기존 테이블 관계 (변경 없음)

#### `tenants` ← 핵심 변경점
- 기존: `reseller_id CHAR(36)` 컬럼 존재
- 변경: `reseller_id` → `resellers.reseller_id` FK 공식화 (ALTER TABLE로 FK 추가)
- NULL = 히트판 직계약 고객사

#### `tenants` → `users` (1:N)
- 고객사 1개에 ERP 사용자 N명
- `users.tenant_id` FK

#### `tenants` → `subscriptions` (1:N)
- 구독 이력 관리 (플랜 변경 시 새 행 추가)

#### `tenants` → `billing_invoices` (1:N)
- 월별 청구서 (billing_month UNIQUE per tenant)

#### `subscriptions` → `billing_invoices` (1:N)
- 어느 구독 플랜 기준으로 청구됐는지 추적

---

## INDEX 전략

| 테이블 | 인덱스 | 용도 |
|--------|--------|------|
| resellers | idx_status | 상태별 대리점 조회 |
| resellers | idx_join_date | 계약일 기준 정렬 |
| reseller_accounts | idx_reseller | 대리점별 계정 목록 |
| reseller_accounts | idx_active | 활성 계정 필터 |
| reseller_commissions | idx_reseller_active | 현재 유효 수수료율 조회 |
| reseller_commissions | idx_effective | 기간별 정책 조회 |
| commission_settlements | idx_status | 상태별 정산 조회 |
| commission_settlements | idx_month | 월별 정산 조회 |
| commission_settlements | idx_payment_date | 지급일 기준 정렬 |

---

## 데이터 흐름 — 수수료 정산

```
매월 말:
  1. billing_invoices (status=paid) 집계
     WHERE tenant_id IN (담당 고객사 목록)
     → total_revenue 계산

  2. reseller_commissions에서 해당 월 유효 요율 조회
     WHERE reseller_id = ? AND plan_code = ? 
     AND effective_from <= 정산월 AND (effective_to IS NULL OR effective_to >= 정산월)
     → rate 확인

  3. commission_settlements 행 생성 (status=draft)
     total_commission = total_revenue × rate / 100
     payment_amount = total_commission - deduction_amount

  4. platform_admin이 검토 후 approved → paid 처리
```

---

## 보안 원칙

| 원칙 | 적용 대상 |
|------|---------|
| AES-256 암호화 | resellers.bank_account |
| JWT 클레임 격리 | platform_admin / reseller_admin 완전 분리 |
| reseller_id 자동 필터 | Reseller API 전체 — 타 대리점 데이터 차단 |
| INSERT ONLY | commission_settlements — 취소는 status='cancelled' |
| decimal 금액 | 모든 금액 컬럼 DECIMAL(15,2) |

---

*ERD 기준으로 DB 작업지시서 발행 → DDL 작성 순서로 진행.*
