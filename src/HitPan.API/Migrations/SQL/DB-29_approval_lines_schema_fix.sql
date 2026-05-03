-- DB-29: approval_lines 테이블 스키마 재설계 (코드-DB 불일치 수정)
-- 적용일: 2026-05-03
-- 원인: 기존 테이블은 per-document/per-approver 행 구조였으나
--        ApprovalLineService.cs는 named template 구조(approval_line_id, name, description, sort_order)를 기대함
-- 데이터: approval_lines, approval_line_steps 모두 0건이므로 DROP & RECREATE 적용

-- approval_line_steps FK 참조 제거 후 approval_lines 재생성
DROP TABLE IF EXISTS approval_line_steps;
DROP TABLE IF EXISTS approval_lines;

CREATE TABLE approval_lines (
  approval_line_id  VARCHAR(36)   NOT NULL,
  tenant_id         VARCHAR(36)   NOT NULL,
  name              VARCHAR(100)  NOT NULL            COMMENT '결재라인 이름',
  description       VARCHAR(200)  NULL DEFAULT NULL   COMMENT '설명',
  sort_order        INT           NOT NULL DEFAULT 0  COMMENT '정렬순서',
  is_active         TINYINT(1)    NOT NULL DEFAULT 1  COMMENT '사용여부',
  created_at        DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at        DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (approval_line_id),
  KEY idx_approval_lines_tenant (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE approval_line_steps (
  step_id           VARCHAR(36)   NOT NULL,
  tenant_id         VARCHAR(36)   NOT NULL,
  approval_line_id  VARCHAR(36)   NOT NULL,
  step_order        INT           NOT NULL            COMMENT '결재 단계 순서',
  position_id       VARCHAR(36)   NULL DEFAULT NULL   COMMENT '직책 ID',
  employee_id       VARCHAR(36)   NULL DEFAULT NULL   COMMENT '사원 ID',
  created_at        DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (step_id),
  KEY idx_approval_line_steps_tenant_line (tenant_id, approval_line_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
