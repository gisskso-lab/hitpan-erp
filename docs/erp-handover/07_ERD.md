# 07. ERD — 137개 테이블 관계도

> **작성일**: 2026-06-01 / PM 브라운킴 + DB 매니저
> **DB**: MariaDB 11.4.10 / hitpan_erp / utf8mb4_unicode_ci
> **상세 컬럼**: `08_테이블정의서.md` 참조

---

## 1. 전체 구조 (10대 영역)

```
                        ┌────────────────────┐
                        │   tenants (테넌트)   │
                        │   ↓ tenant_id 키    │
                        └────────────────────┘
                                  │
            ┌─────────────────────┼─────────────────────┐
            ▼                     ▼                     ▼
    ┌──────────────┐      ┌──────────────┐      ┌──────────────┐
    │  마스터        │      │  업무 트랜잭션  │      │  HR·결재      │
    │ ・partners    │      │ ・매입         │      │ ・employees   │
    │ ・items       │      │ ・판매         │      │ ・approval_*  │
    │ ・bom_*       │      │ ・재고         │      │ ・hr_*        │
    │ ・warehouses  │      │ ・재무·회계     │      │ ・labor_*     │
    └──────────────┘      └──────────────┘      └──────────────┘
            │                     │                     │
            └─────────────────────┼─────────────────────┘
                                  ▼
                        ┌────────────────────┐
                        │  원장 INSERT ONLY   │
                        │ ・stock_ledger     │
                        │ ・journal_lines    │
                        └────────────────────┘
```

---

## 2. 영역별 ERD

### 2.1 인증·테넌트
```
tenants ◄──┐
   │       │
   ├──► users ──► refresh_tokens
   │       │
   │       └──► audit_logs
   │       └──► login_attempts
   │       └──► user_terms_consent
   │
   ├──► employees ──► positions, departments
   ├──► permissions
   ├──► devices ──► device_login_logs
   └──► tenant_certificates (AES-256)
```

### 2.2 마스터
```
partners ──► partner_contacts
   │      ──► partner_special_prices ──► items
   │      ──► partner_balance
   │
items ──► item_groups
   │   ──► item_specs (DB-73, 진범 #99 봉합)
   │   ──► item_special_prices ──► partners
   │   ──► item_stock
   │   ──► inventory_lots
   │
bom_headers ──► bom_items ──► items
              ──► bom_cost_cache
```

### 2.3 매입
```
purchase_orders ──► purchase_order_items ──► items
       │                                 ──► warehouses
       │
       ▼ (confirmed)
purchase_receipts ──► purchase_receipt_items
       │                       │
       ▼                       ▼ stock_ledger INSERT (+N)
purchase_returns ──► purchase_return_items
       │           ──► return_reason (DB-75)
       ▼
   stock_ledger INSERT (-N)
```

### 2.4 판매
```
quotations ──► quotation_items ──► items
   │
   ▼ (변환)
sales_orders ──► (custom_order_specs)
   │
   ▼ (변환)
deliveries ──► delivery_tracking
   │   │
   │   └──► stock_ledger INSERT (-N) + journal_lines
   │
   ▼
tax_invoices (L1) ──► etax_send_history (L2 메이크빌)
```

### 2.5 재고
```
warehouses ──► item_stock
            ──► stock_ledger (INSERT ONLY, 헌법 #3)
                  ├─ source_type: purchase / sale / adjust / transfer / bom_in / bom_out
                  └─ partition by tenant_id
```

### 2.6 재무·회계
```
accounts (DB-32 시드)
   │
   ├──► journal_entries ──► journal_lines (INSERT ONLY)
   │                          (UNIQUE source_type+source_id, DB-35)
   │
   ├──► monthly_closing ──► monthly_summary
   │
   ├──► partner_balance (매입·매출 잔액)
   │
   ├──► bank_transactions
   ├──► card_payments ──► card_payment_lines
   ├──► bills (어음·수표)
   ├──► cashbook (현금 출납)
   ├──► expenses
   ├──► collections (수금)
   ├──► payments (지급)
   └──► ledger_balance_snapshot
```

### 2.7 결재·HR
```
approval_lines ──► approval_line_steps ──► users
   │
   ▼
approval_documents ──► approval_doc_lines ──► (원본 문서: quotations, sales_orders, ...)
   │              ──► approval_history
   │
employees ──► attendance
          ──► leave_requests ──► approval_*
          ──► overtime
          ──► hr_expense_requests ──► approval_*
          ──► labor_contracts ──► esign_records
          ──► evaluations
```

### 2.8 AI·CS
```
ai_conversations ◄── users
ai_usage_logs ◄── tenants
hitpan_knowledge (RAG 벡터)
chatbot_sessions (옵션)
```

### 2.9 백오피스·대리점 (본사 영역, 헌법 #18·#22)
```
platforms
   │
   ├──► platform_admins
   ├──► resellers ──► reseller_accounts
   │              ──► reseller_customers (tenant_id 매핑)
   │              ──► reseller_commissions
   │              ──► reseller_promotions
   │              ──► reseller_revenues
   │              ──► reseller_payouts
   │
   ├──► commission_settlements
   └──► backoffice_refresh_tokens
```

### 2.10 시스템·운영
```
backup_settings ──► backup_history
migration_jobs ──► migration_checkpoints, migration_errors
watchdog_pings (헌법 #27·#30)
licenses (헌법 #71 token_hash)
billing_subscriptions ──► billing_invoices
                      ──► billing_payment_methods (AES-256)
                      ──► billing_payment_attempts
email_settings ──► email_send_history, email_attachment_history
form_templates (DB-74, 양식 박제)
custom_order_specs
events (도메인 이벤트)
idempotency_keys (POST 멱등성)
common_codes
audit_trail
```

---

## 3. 핵심 외래키 (54건)

| 자식 테이블 | FK 컬럼 | 부모 테이블 | 부모 컬럼 |
|---|---|---|---|
| ai_conversations | user_id | users | user_id |
| ai_usage_logs | tenant_id | tenants | tenant_id |
| billing_invoices | tenant_id | tenants | tenant_id |
| bom_headers | tenant_id | tenants | tenant_id |
| bom_items | bom_id | bom_headers | bom_id |
| commission_settlements | approved_by | platform_admins | admin_id |
| commission_settlements | reseller_id | resellers | reseller_id |
| custom_order_specs | order_id | sales_orders | order_id |
| departments | tenant_id | tenants | tenant_id |
| employees | tenant_id | tenants | tenant_id |
| esign_records | user_id | users | user_id |
| esign_records | tenant_id | tenants | tenant_id |
| etax_send_history | tax_invoice_id | tax_invoices | invoice_id |
| inventory_lots | item_id | items | item_id |
| journal_entries | tenant_id | tenants | tenant_id |
| journal_lines | entry_id | journal_entries | entry_id |
| journal_lines | account_code | accounts | account_code |
| journal_lines | tenant_id | accounts | tenant_id |
| labor_contracts | tenant_id | tenants | tenant_id |
| material_price_history | item_id | items | item_id |
| migration_checkpoints | job_id | migration_jobs | job_id |
| migration_errors | job_id | migration_jobs | job_id |
| mold_assets | product_item_id | items | item_id |
| mold_production_log | mold_id | mold_assets | mold_id |
| partners | tenant_id | tenants | tenant_id |
| purchase_orders | partner_id | partners | partner_id |
| purchase_orders | tenant_id | tenants | tenant_id |
| purchase_order_items | po_id | purchase_orders | po_id |
| purchase_receipts | partner_id | partners | partner_id |
| purchase_receipts | tenant_id | tenants | tenant_id |
| purchase_receipt_items | receipt_id | purchase_receipts | receipt_id |
| reseller_accounts | reseller_id | resellers | reseller_id |
| reseller_commissions | reseller_id | resellers | reseller_id |
| sales_deliveries | partner_id | partners | partner_id |
| sales_deliveries | tenant_id | tenants | tenant_id |
| sales_delivery_items | delivery_id | sales_deliveries | delivery_id |
| sales_orders | partner_id | partners | partner_id |
| sales_orders | tenant_id | tenants | tenant_id |
| sales_order_items | order_id | sales_orders | order_id |
| sales_returns | partner_id | partners | partner_id |
| sales_returns | delivery_id | sales_deliveries | delivery_id |
| sales_return_items | warehouse_id | warehouses | warehouse_id |
| sales_return_items | return_id | sales_returns | return_id |
| sales_return_items | item_id | items | item_id |
| sales_return_items | delivery_item_id | sales_delivery_items | delivery_item_id |
| tax_invoices | tenant_id | tenants | tenant_id |
| tax_invoices | delivery_id | sales_deliveries | delivery_id |
| tax_invoice_items | invoice_id | tax_invoices | invoice_id |
| tenants | reseller_id | resellers | reseller_id |
| tenant_devices | user_id | users | user_id |
| tenant_devices | tenant_id | tenants | tenant_id |
| users | tenant_id | tenants | tenant_id |
| warehouses | tenant_id | tenants | tenant_id |
| work_in_process | item_id | items | item_id |

---

## 4. 절대 원칙 정합 (ERD 관점)

- **헌법 #2 (멀티테넌트)**: 모든 업무 테이블 `tenant_id` 컬럼 + JWT 클레임 검증
- **헌법 #3 (INSERT ONLY 원장)**: `stock_ledger`, `journal_lines` UPDATE/DELETE 금지
- **헌법 #5 (암호화)**: `tenant_certificates.private_key_enc`, `billing_payment_methods.card_number_enc`, `email_settings.smtp_password_enc` 등 AES-256
- **헌법 #6 (confirmed 시점 원장)**: status='confirmed' 트리거로만 원장 INSERT
- **헌법 #17 (InnoDB)**: 137개 전체 ENGINE=InnoDB 명시
- **헌법 #18·#22 (본사 데이터 최소주의)**:
  - 본사 보유: `tenants`, `licenses`, `billing_*`, `platforms`, `platform_admins`, `resellers`, `reseller_*`, `commission_*`, `watchdog_pings`, `beta_signups`
  - 본사 미보유: 매입·매출·원장·거래처·직원·재고·세금계산서·결재 — 전부 고객 PC

