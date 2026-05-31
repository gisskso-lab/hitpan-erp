-- =============================================================
-- DB-74: 양식정보설정 (사장님 작업지시 2026-05-31 작지②·③)
-- - 6대 양식 (견적·수주·거래명세서·발주·매입·반품 + 세금계산서는 베타후)
-- - paper_mode = plain(순백지) / preprint(양식용지 구입)
-- - field_coords JSON = 양식용지 모드 좌표 박제 (mm 단위)
-- - HITWIN 레거시 정합: 컬러 JPG = preprint, 흑백 = plain
-- 절대원칙: tenant_id JWT / 1 form_type당 1 active 박제
-- =============================================================

CREATE TABLE IF NOT EXISTS form_templates (
    template_id CHAR(36) NOT NULL PRIMARY KEY,
    tenant_id CHAR(36) NOT NULL,
    form_type VARCHAR(30) NOT NULL COMMENT 'estimate, sales_order, delivery, purchase_order, receipt, purchase_return, sales_return, tax_invoice',
    template_name VARCHAR(100) NOT NULL,
    paper_mode VARCHAR(10) NOT NULL DEFAULT 'plain' COMMENT 'plain=A4 순백지 / preprint=양식용지 구입',
    paper_size VARCHAR(10) NOT NULL DEFAULT 'A4' COMMENT 'A4·A5·Letter',
    orientation VARCHAR(10) NOT NULL DEFAULT 'portrait' COMMENT 'portrait·landscape',
    margin_top_mm DECIMAL(5,2) NOT NULL DEFAULT 15,
    margin_left_mm DECIMAL(5,2) NOT NULL DEFAULT 15,
    margin_right_mm DECIMAL(5,2) NOT NULL DEFAULT 15,
    margin_bottom_mm DECIMAL(5,2) NOT NULL DEFAULT 15,
    field_coords_json JSON NULL COMMENT 'preprint 모드 좌표: [{"key":"partner_name","x_mm":30,"y_mm":40,"font_pt":10}]',
    background_image_url VARCHAR(500) NULL COMMENT '미리보기용 (HITWIN JPG 참조)',
    show_company_logo TINYINT(1) NOT NULL DEFAULT 1 COMMENT '순백지 모드에서만 적용',
    show_company_seal TINYINT(1) NOT NULL DEFAULT 1,
    show_border TINYINT(1) NOT NULL DEFAULT 1 COMMENT '순백지 = 1, 양식용지 = 0',
    is_default TINYINT(1) NOT NULL DEFAULT 0 COMMENT '1=신규 발행 시 기본 적용',
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    UNIQUE KEY uk_form_templates_default (tenant_id, form_type, is_default, is_active),
    INDEX idx_form_templates_type (tenant_id, form_type, is_active, is_default)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='양식정보설정 — paper_mode 분기 박제 (사장님 헌법 2026-05-31)';

-- 기본 템플릿 시드 (테넌트별 신규 시 6개 자동 박제는 Service에서)
-- 참고: tenant_id별 시드는 Service 가도 (TenantSetupService)에서 INSERT
