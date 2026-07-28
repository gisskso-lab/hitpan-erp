-- DB-77: 백오피스 Pull 동기화 — 직원 복사본
-- 사장님 결재 2026-06-01 / 헌법 #18·#22 (데이터 최소주의) 정합
-- 본사가 받는 컬럼: 이름·이메일·직급·재직여부·등록일 5개만
-- 업무 데이터 절대 포함 금지

CREATE TABLE IF NOT EXISTS tenant_employees_snapshot (
    snapshot_id CHAR(36) NOT NULL COMMENT 'UUID',
    tenant_id CHAR(36) NOT NULL COMMENT '테넌트 ID (원본 tenants.tenant_id 참조)',
    employee_id CHAR(36) NOT NULL COMMENT '직원 ID (원본 employees.employee_id 참조)',
    name VARCHAR(50) NOT NULL COMMENT '이름',
    email VARCHAR(100) NOT NULL COMMENT '이메일',
    position VARCHAR(30) NULL COMMENT '직급',
    is_active TINYINT(1) NOT NULL DEFAULT 1 COMMENT '재직여부',
    synced_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '마지막 Pull 시각',
    PRIMARY KEY (snapshot_id),
    UNIQUE KEY uq_tenant_employee (tenant_id, employee_id),
    INDEX idx_tenant_synced (tenant_id, synced_at),
    INDEX idx_synced (synced_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='[헌법 #18·#22] 백오피스 Pull 복사본 — 5개 컬럼만. 업무 데이터 절대 금지';
