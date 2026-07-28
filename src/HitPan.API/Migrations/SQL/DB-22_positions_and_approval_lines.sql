-- DB-22: 직급 마스터 + 결재라인 (사장님 결재 2026-04-29, B안 — 어드민이 직접 설정)
--
-- 사장님 헌법 (2026-04-29): "회사마다 결재라인 시스템이 다르니" → 어드민이 추가/삭제 가능.
-- §절대원칙 #11 (권한은 어드민이 직접 설정, 업종·규모별 템플릿 금지) 정렬.
--
-- 테이블 3개:
--   1) positions             — 직급 마스터 (회사별, 어드민이 추가/삭제)
--   2) approval_lines        — 결재라인 (회사별, 여러 개 가능: "일반결재", "지출결재" 등)
--   3) approval_line_steps   — 결재라인의 단계(순번) — 직급+담당자 조합
--
-- §절대원칙 #17 ENGINE=InnoDB 명시 / Collation utf8mb4_unicode_ci.
-- §절대원칙 #2 tenant_id 모든 테이블 필수 / 모든 인덱스의 첫 키.

-- ─────────────────────────────────────────────
-- 1) 직급 마스터
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS positions (
    position_id    VARCHAR(36)  NOT NULL,
    tenant_id      VARCHAR(36)  NOT NULL,
    code           VARCHAR(40)  NOT NULL  COMMENT '코드 (예: CEO, MANAGER) — 영문 대문자',
    name           VARCHAR(60)  NOT NULL  COMMENT '표시명 (예: 대표이사, 팀장)',
    sort_order     INT          NOT NULL DEFAULT 0  COMMENT '정렬 (높을수록 상위직급)',
    is_active      TINYINT(1)   NOT NULL DEFAULT 1,
    created_at     DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    created_by     VARCHAR(60)  NULL,
    updated_at     DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    updated_by     VARCHAR(60)  NULL,
    PRIMARY KEY (position_id),
    UNIQUE KEY uk_positions_tenant_code (tenant_id, code),
    KEY ix_positions_tenant_active (tenant_id, is_active, sort_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='직급 마스터 — 회사별 어드민이 추가/삭제';

-- ─────────────────────────────────────────────
-- 2) 결재라인 마스터
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS approval_lines (
    approval_line_id  VARCHAR(36)   NOT NULL,
    tenant_id         VARCHAR(36)   NOT NULL,
    name              VARCHAR(100)  NOT NULL  COMMENT '결재라인 이름 (예: 일반결재, 지출결재)',
    description       VARCHAR(255)  NULL,
    sort_order        INT           NOT NULL DEFAULT 0,
    is_active         TINYINT(1)    NOT NULL DEFAULT 1,
    created_at        DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    created_by        VARCHAR(60)   NULL,
    updated_at        DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    updated_by        VARCHAR(60)   NULL,
    PRIMARY KEY (approval_line_id),
    KEY ix_approval_lines_tenant_active (tenant_id, is_active, sort_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='결재라인 마스터 — 회사별 다수 가능';

-- ─────────────────────────────────────────────
-- 3) 결재라인 단계
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS approval_line_steps (
    step_id           VARCHAR(36)  NOT NULL,
    tenant_id         VARCHAR(36)  NOT NULL,
    approval_line_id  VARCHAR(36)  NOT NULL,
    step_order        INT          NOT NULL  COMMENT '결재 순번 (1부터)',
    position_id       VARCHAR(36)  NOT NULL  COMMENT 'positions FK',
    employee_id       VARCHAR(36)  NOT NULL  COMMENT 'employees FK',
    created_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (step_id),
    UNIQUE KEY uk_approval_line_steps_order (tenant_id, approval_line_id, step_order),
    KEY ix_approval_line_steps_line (tenant_id, approval_line_id),
    KEY ix_approval_line_steps_position (tenant_id, position_id),
    KEY ix_approval_line_steps_employee (tenant_id, employee_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='결재라인 단계 — 라인의 순번별 직급+담당자';

-- ─────────────────────────────────────────────
-- 4) 시드 — 신규 가입사 기본 직급 6개 (사장님 결재 2026-04-29)
--    "0개에서 시작" 방지. 어드민이 자유롭게 수정/추가/삭제 가능.
--    tenant-001 에 한정해 시드. 신규 테넌트는 가입 프로비저닝 코드에서 동일 시드 INSERT 권장.
-- ─────────────────────────────────────────────
INSERT IGNORE INTO positions (position_id, tenant_id, code, name, sort_order, is_active)
VALUES
  (UUID(), 'tenant-001', 'CEO',       '대표이사', 100, 1),
  (UUID(), 'tenant-001', 'DIRECTOR',  '부장',      80, 1),
  (UUID(), 'tenant-001', 'DEPUTY',    '차장',      70, 1),
  (UUID(), 'tenant-001', 'MANAGER',   '팀장',      60, 1),
  (UUID(), 'tenant-001', 'PART_LEAD', '파트장',    50, 1),
  (UUID(), 'tenant-001', 'STAFF',     '담당자',    10, 1);
