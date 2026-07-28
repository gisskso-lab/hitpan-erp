-- DB-80: 대리점 신청 — 랜딩에서 가입 신청 받음
-- 사장님 결재 2026-06-01

CREATE TABLE IF NOT EXISTS reseller_applications (
    application_id CHAR(36) NOT NULL COMMENT 'UUID',
    company_name VARCHAR(120) NOT NULL COMMENT '회사명',
    representative_name VARCHAR(50) NOT NULL COMMENT '대표자명',
    contact_name VARCHAR(50) NOT NULL COMMENT '담당자명',
    contact_email VARCHAR(100) NOT NULL COMMENT '담당자 이메일',
    contact_phone VARCHAR(30) NOT NULL COMMENT '담당자 전화',
    business_no VARCHAR(20) NULL COMMENT '사업자번호',
    region VARCHAR(50) NULL COMMENT '영업 지역',
    sales_channel VARCHAR(100) NULL COMMENT '주요 영업 채널',
    expected_customers INT NULL COMMENT '예상 고객사 수',
    motivation TEXT NULL COMMENT '신청 사유',
    status ENUM('pending','reviewing','approved','rejected') NOT NULL DEFAULT 'pending' COMMENT '심사 상태',
    submitted_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '신청 시각',
    reviewed_at TIMESTAMP NULL DEFAULT NULL COMMENT '심사 완료 시각',
    reviewed_by VARCHAR(36) NULL DEFAULT NULL COMMENT '심사 admin_id',
    rejection_reason VARCHAR(500) NULL DEFAULT NULL COMMENT '거절 사유',
    PRIMARY KEY (application_id),
    INDEX idx_status_submitted (status, submitted_at),
    INDEX idx_email (contact_email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='대리점 신청 — 랜딩 /reseller/signup 박제';
