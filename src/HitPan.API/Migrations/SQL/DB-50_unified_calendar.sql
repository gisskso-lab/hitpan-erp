-- ============================================================================
-- DB-50: 통합 캘린더 일정 테이블 (2026-05-26)
-- ----------------------------------------------------------------------------
-- 사장님 결재 2026-05-26: 통합 캘린더 + 카드 모달 가도.
-- 4축 통합: 수금(collections) + 입금(bank_transactions tx_type='2' / card_payments) +
--          연차(leave_requests) + 이슈(schedules ← 본 테이블)
-- §#2 tenant_id 우선 인덱스 / §#4 decimal / §#17 InnoDB / §#19 errors 0 + warnings 0
-- §#22 본사 데이터 0 — 모두 고객 PC 로컬에만 보관
-- ============================================================================

CREATE TABLE IF NOT EXISTS `schedules` (
  `schedule_id`      VARCHAR(36)    NOT NULL COMMENT '일정 PK',
  `tenant_id`        VARCHAR(36)    NOT NULL COMMENT '테넌트 ID',
  `title`            VARCHAR(200)   NOT NULL COMMENT '일정 제목',
  `schedule_type`    VARCHAR(30)    NOT NULL DEFAULT 'issue' COMMENT 'meeting/todo/reminder/issue',
  `start_at`         DATETIME(6)    NOT NULL COMMENT '시작 시각',
  `end_at`           DATETIME(6)    DEFAULT NULL COMMENT '종료 시각',
  `partner_id`       VARCHAR(36)    DEFAULT NULL COMMENT '거래처 연결 (선택)',
  `partner_name`     VARCHAR(100)   DEFAULT NULL COMMENT '거래처명 (조회 편의)',
  `participant_id`   VARCHAR(36)    DEFAULT NULL COMMENT '관련 직원 (선택)',
  `participant_name` VARCHAR(50)    DEFAULT NULL COMMENT '관련 직원명',
  `memo`             TEXT           DEFAULT NULL COMMENT '메모',
  `is_completed`     TINYINT(1)     NOT NULL DEFAULT 0 COMMENT '완료 여부',
  `created_at`       DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `created_by`       VARCHAR(36)    DEFAULT NULL,
  `updated_at`       DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `updated_by`       VARCHAR(36)    DEFAULT NULL,
  PRIMARY KEY (`schedule_id`),
  KEY `idx_tenant_start` (`tenant_id`, `start_at`),
  KEY `idx_partner` (`tenant_id`, `partner_id`),
  KEY `idx_participant` (`tenant_id`, `participant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='통합 캘린더 일정 — 미팅/todo/리마인더/이슈';
