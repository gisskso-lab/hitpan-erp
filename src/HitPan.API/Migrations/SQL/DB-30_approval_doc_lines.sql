-- DB-30: approval_doc_lines 신설
-- 적용일: 2026-05-03
-- 배경: approval_lines 테이블이 named-template 구조(DB-29)로 재설계되면서
--        ApprovalService가 사용하던 per-document 결재자 행 구조(doc_type/seq_no/approver_id)가 사라짐.
--        충돌 없이 두 구조를 공존시키기 위해 별도 테이블로 분리.

CREATE TABLE IF NOT EXISTS approval_doc_lines (
  line_id          VARCHAR(36)   NOT NULL,
  tenant_id        VARCHAR(36)   NOT NULL,
  doc_type         VARCHAR(30)   NOT NULL              COMMENT '문서 유형',
  seq_no           INT           NOT NULL              COMMENT '결재 순서',
  approver_id      VARCHAR(36)   NOT NULL              COMMENT '결재자 ID (employee_id)',
  approver_name    VARCHAR(50)   NOT NULL              COMMENT '결재자 이름',
  role_label       VARCHAR(30)   NULL DEFAULT NULL     COMMENT '직책 표시',
  delegate_id      VARCHAR(36)   NULL DEFAULT NULL     COMMENT '위임결재자 ID',
  delegate_name    VARCHAR(50)   NULL DEFAULT NULL     COMMENT '위임결재자 이름',
  delegate_start   DATE          NULL DEFAULT NULL     COMMENT '위임 시작일',
  delegate_end     DATE          NULL DEFAULT NULL     COMMENT '위임 종료일',
  is_active        TINYINT(1)    NOT NULL DEFAULT 1    COMMENT '사용여부',
  created_at       DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at       DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (line_id),
  KEY idx_approval_doc_lines_tenant_doc (tenant_id, doc_type, seq_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
