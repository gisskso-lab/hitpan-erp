-- ============================================================
-- Phase 4: 결재 시스템 + 수금 DDL
-- 작성일: 2026-04-20
-- 작성자: PM 닥터스트레인지
-- ============================================================

-- ------------------------------------------------------------
-- 1. 결재 설정 (테넌트별 결재 ON/OFF, 기준금액 등)
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `approval_settings` (
  `setting_id`         VARCHAR(36)    NOT NULL COMMENT '설정 PK',
  `tenant_id`          VARCHAR(36)    NOT NULL COMMENT '테넌트 ID',
  `doc_type`           VARCHAR(30)    NOT NULL COMMENT '문서유형 (quotation, sales_order, delivery, purchase_order, receipt, return, expense, leave, overtime)',
  `is_enabled`         TINYINT(1)     NOT NULL DEFAULT 0 COMMENT '결재 사용 여부 (0=OFF, 1=ON)',
  `threshold_amount`   DECIMAL(15,2)  NOT NULL DEFAULT 0 COMMENT '기준금액 (이 금액 이상이면 결재 필요, 0=항상)',
  `auto_approve_below` TINYINT(1)     NOT NULL DEFAULT 0 COMMENT '기준금액 미만 자동승인 여부',
  `max_lines`          INT            NOT NULL DEFAULT 3 COMMENT '최대 결재라인 수',
  `created_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `created_by`         VARCHAR(36)    DEFAULT NULL,
  `updated_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `updated_by`         VARCHAR(36)    DEFAULT NULL,
  PRIMARY KEY (`setting_id`),
  UNIQUE KEY `uq_tenant_doctype` (`tenant_id`, `doc_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='결재 설정 — 문서유형별 결재 ON/OFF 및 기준금액';

-- ------------------------------------------------------------
-- 2. 결재 라인 (문서유형별 결재자 순서)
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `approval_lines` (
  `line_id`            VARCHAR(36)    NOT NULL COMMENT '라인 PK',
  `tenant_id`          VARCHAR(36)    NOT NULL COMMENT '테넌트 ID',
  `doc_type`           VARCHAR(30)    NOT NULL COMMENT '문서유형',
  `seq_no`             INT            NOT NULL COMMENT '결재 순서 (1,2,3…)',
  `approver_id`        VARCHAR(36)    NOT NULL COMMENT '결재자 사원 ID (employees.employee_id)',
  `approver_name`      VARCHAR(50)    NOT NULL COMMENT '결재자 이름 (조회 편의)',
  `role_label`         VARCHAR(30)    DEFAULT NULL COMMENT '직책 라벨 (팀장, 부장, 대표 등)',
  `delegate_id`        VARCHAR(36)    DEFAULT NULL COMMENT '위임결재자 사원 ID (NULL이면 위임 없음)',
  `delegate_name`      VARCHAR(50)    DEFAULT NULL COMMENT '위임결재자 이름',
  `delegate_start`     DATE           DEFAULT NULL COMMENT '위임 시작일',
  `delegate_end`       DATE           DEFAULT NULL COMMENT '위임 종료일',
  `is_active`          TINYINT(1)     NOT NULL DEFAULT 1,
  `created_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`line_id`),
  UNIQUE KEY `uq_tenant_doctype_seq` (`tenant_id`, `doc_type`, `seq_no`),
  KEY `idx_approver` (`tenant_id`, `approver_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='결재 라인 — 문서유형별 결재자 순서·위임 설정';

-- ------------------------------------------------------------
-- 3. 결재 문서 (결재 요청 건)
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `approval_documents` (
  `approval_id`        VARCHAR(36)    NOT NULL COMMENT '결재문서 PK',
  `tenant_id`          VARCHAR(36)    NOT NULL COMMENT '테넌트 ID',
  `doc_type`           VARCHAR(30)    NOT NULL COMMENT '문서유형',
  `ref_id`             VARCHAR(36)    NOT NULL COMMENT '원본 문서 ID (quotations.quotation_id 등)',
  `ref_no`             VARCHAR(30)    DEFAULT NULL COMMENT '원본 문서번호',
  `title`              VARCHAR(200)   NOT NULL COMMENT '결재 제목',
  `amount`             DECIMAL(15,2)  NOT NULL DEFAULT 0 COMMENT '금액',
  `status`             VARCHAR(20)    NOT NULL DEFAULT 'pending' COMMENT '상태 (pending, approved, rejected, cancelled)',
  `current_seq`        INT            NOT NULL DEFAULT 1 COMMENT '현재 결재 순서',
  `total_lines`        INT            NOT NULL DEFAULT 1 COMMENT '총 결재라인 수',
  `requester_id`       VARCHAR(36)    NOT NULL COMMENT '기안자 사원 ID',
  `requester_name`     VARCHAR(50)    NOT NULL COMMENT '기안자 이름',
  `requested_at`       DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '기안일시',
  `completed_at`       DATETIME(6)    DEFAULT NULL COMMENT '최종 결재완료/반려 일시',
  `memo`               VARCHAR(500)   DEFAULT NULL COMMENT '기안 메모',
  `created_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `created_by`         VARCHAR(36)    DEFAULT NULL,
  `updated_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `updated_by`         VARCHAR(36)    DEFAULT NULL,
  PRIMARY KEY (`approval_id`),
  UNIQUE KEY `uq_tenant_ref` (`tenant_id`, `doc_type`, `ref_id`),
  KEY `idx_status` (`tenant_id`, `status`),
  KEY `idx_requester` (`tenant_id`, `requester_id`, `status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='결재 문서 — 결재 요청 건별 상태 관리';

-- ------------------------------------------------------------
-- 4. 결재 이력 (INSERT ONLY — 수정/삭제 절대 금지)
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `approval_history` (
  `history_id`         VARCHAR(36)    NOT NULL COMMENT '이력 PK',
  `tenant_id`          VARCHAR(36)    NOT NULL COMMENT '테넌트 ID',
  `approval_id`        VARCHAR(36)    NOT NULL COMMENT '결재문서 FK',
  `seq_no`             INT            NOT NULL COMMENT '결재 순서',
  `approver_id`        VARCHAR(36)    NOT NULL COMMENT '실제 결재자 사원 ID (위임결재 시 위임자)',
  `approver_name`      VARCHAR(50)    NOT NULL COMMENT '실제 결재자 이름',
  `is_delegated`       TINYINT(1)     NOT NULL DEFAULT 0 COMMENT '위임결재 여부',
  `original_approver_id` VARCHAR(36)  DEFAULT NULL COMMENT '원래 결재자 (위임 시)',
  `action`             VARCHAR(20)    NOT NULL COMMENT '결재 액션 (approved, rejected)',
  `comment`            VARCHAR(500)   DEFAULT NULL COMMENT '결재 의견',
  `acted_at`           DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '결재일시',
  PRIMARY KEY (`history_id`),
  KEY `idx_approval` (`tenant_id`, `approval_id`),
  KEY `idx_approver_action` (`tenant_id`, `approver_id`, `action`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='결재 이력 — INSERT ONLY, 수정/삭제 절대 금지';

-- ------------------------------------------------------------
-- 5. 수금 테이블 (거래처에서 받은 돈)
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `collections` (
  `collection_id`      VARCHAR(36)    NOT NULL COMMENT '수금 PK',
  `tenant_id`          VARCHAR(36)    NOT NULL COMMENT '테넌트 ID',
  `partner_id`         VARCHAR(36)    NOT NULL COMMENT '거래처 ID',
  `collection_date`    DATE           NOT NULL COMMENT '수금일',
  `amount`             DECIMAL(15,2)  NOT NULL COMMENT '수금액',
  `collection_method`  VARCHAR(20)    NOT NULL DEFAULT 'cash' COMMENT '수금수단 (cash, bank_transfer, check, card, note)',
  `ref_doc_type`       VARCHAR(30)    DEFAULT NULL COMMENT '관련 문서유형 (delivery, tax_invoice 등)',
  `ref_doc_id`         VARCHAR(36)    DEFAULT NULL COMMENT '관련 문서 ID',
  `memo`               VARCHAR(500)   DEFAULT NULL COMMENT '비고',
  `is_active`          TINYINT(1)     NOT NULL DEFAULT 1,
  `created_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `created_by`         VARCHAR(36)    DEFAULT NULL,
  `updated_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `updated_by`         VARCHAR(36)    DEFAULT NULL,
  PRIMARY KEY (`collection_id`),
  KEY `idx_tenant_partner` (`tenant_id`, `partner_id`),
  KEY `idx_tenant_date` (`tenant_id`, `collection_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='수금 — 거래처로부터 받은 돈 기록';

-- ------------------------------------------------------------
-- partner_balance 뷰 업데이트: 수금 합산 추가
-- ------------------------------------------------------------
CREATE OR REPLACE VIEW `v_partner_collections_total` AS
SELECT
  c.tenant_id,
  c.partner_id,
  SUM(c.amount) AS total_collection
FROM `collections` c
WHERE c.is_active = 1
GROUP BY c.tenant_id, c.partner_id;
