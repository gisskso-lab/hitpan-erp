-- ERP 로컬 회사정보 마스터 (사장님 결재 2026-06-04, 헌법 #35 객체 완전 분리)
--
-- 헌법 #35 정합:
--   - 백오피스 DB(hitpan_backoffice.tenants) = 본사 영역만 (라이선스 해시·구독·AI·기기·대리점)
--   - ERP 로컬 DB(hitpan_erp.local_company)  = 고객사 회사정보만 (사업자번호·대표·주소·이메일 등)
--
-- 박제 위치: hitpan_erp DB (고객사 PC)
-- 본 SQL 실행 후 ERP 코드가 tenants → local_company로 정정됨 (사장님 결재 2026-06-04).
-- tenant_id는 동일값 유지 (FK 정합 보존).

USE hitpan_erp;

CREATE TABLE IF NOT EXISTS local_company (
    tenant_id          VARCHAR(36)     NOT NULL PRIMARY KEY COMMENT 'tenants.tenant_id 와 동일값 박제',
    tenant_code        VARCHAR(20)     NOT NULL,
    company_name       VARCHAR(100)    NOT NULL,
    biz_no             VARCHAR(12)     NULL,
    ceo_name           VARCHAR(50)     NULL,
    biz_type           VARCHAR(50)     NULL,
    biz_item           VARCHAR(100)    NULL,
    tel                VARCHAR(20)     NULL,
    fax                VARCHAR(20)     NULL,
    address            VARCHAR(200)    NULL,
    zip_code           VARCHAR(10)     NULL,
    email              VARCHAR(100)    NULL,
    logo_url           VARCHAR(200)    NULL,
    corp_no            VARCHAR(20)     NULL,
    subsidiary_no      VARCHAR(20)     NULL,
    homepage           VARCHAR(200)    NULL,
    initial_date       DATE            NULL,
    e_invoice_server   VARCHAR(100)    NULL,
    e_invoice_id       VARCHAR(100)    NULL,
    e_invoice_enabled  TINYINT(1)      NOT NULL DEFAULT 0,
    tax_type           VARCHAR(20)     NOT NULL DEFAULT 'taxable',
    fiscal_month       INT             NOT NULL DEFAULT 12,
    is_locked_from_landing TINYINT(1)  NOT NULL DEFAULT 0 COMMENT '랜딩 가입 자동 반영 잠금',
    bootstrap_at       DATETIME        NULL,
    created_at         DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at         DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),

    UNIQUE KEY uq_local_company_code (tenant_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ────────────────────────────────────────
-- 기존 tenants 데이터에서 회사정보만 이관 (W1 DB 분리 전 실행)
-- ────────────────────────────────────────
INSERT IGNORE INTO local_company (
    tenant_id, tenant_code, company_name, biz_no, ceo_name,
    biz_type, biz_item, tel, fax, address, zip_code, email,
    logo_url, corp_no, subsidiary_no, homepage, initial_date,
    e_invoice_server, e_invoice_id, e_invoice_enabled, tax_type, fiscal_month,
    is_locked_from_landing, bootstrap_at, created_at, updated_at
)
SELECT
    tenant_id, tenant_code, company_name, biz_no, ceo_name,
    biz_type, biz_item, tel, fax, address, zip_code, email,
    logo_url, corp_no, subsidiary_no, homepage, initial_date,
    e_invoice_server, e_invoice_id, e_invoice_enabled, tax_type, fiscal_month,
    COALESCE(is_locked_from_landing, 0), bootstrap_at, created_at, updated_at
FROM tenants;

-- ────────────────────────────────────────
-- 검증 쿼리 (사장님 실행 후 카운트 일치 확인)
-- ────────────────────────────────────────
-- SELECT 'tenants' AS t, COUNT(*) AS cnt FROM tenants
-- UNION ALL SELECT 'local_company', COUNT(*) FROM local_company;
