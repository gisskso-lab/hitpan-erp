-- ============================================================================
-- DB-81: leave_requests (연차/휴가 신청) 테이블 신설
-- ----------------------------------------------------------------------------
-- P1-1 봉합(2026-06-20): 전수검사 결과 leave_requests 가 installer 번들·백업에만 있고
-- 마이그레이션 SQL 디렉토리에는 없었다. 그 결과 신규 고객 DB 를 마이그레이션으로 재생성하면
-- 연차/휴가 테이블이 안 생겨 그룹웨어 휴가 기능 전체가 동작 불가했다.
-- installer/hitpan_db.sql 의 정본 DDL 을 멱등(IF NOT EXISTS)으로 옮긴다.
--
-- 헌법 정합: #17 ENGINE=InnoDB 명시 / utf8mb4_unicode_ci 통일 / #13 컬럼 정본 확인 완료
-- ============================================================================

CREATE TABLE IF NOT EXISTS `leave_requests` (
  `request_id`   varchar(36)  NOT NULL,
  `tenant_id`    varchar(36)  NOT NULL,
  `employee_id`  varchar(36)  NOT NULL,
  `leave_type`   varchar(20)  NOT NULL DEFAULT 'annual' COMMENT '연차/반차/병가/공가',
  `leave_days`   decimal(3,1) NOT NULL DEFAULT 1.0      COMMENT '사용일수(반차=0.5)',
  `start_date`   date         NOT NULL,
  `end_date`     date         NOT NULL,
  `reason`       varchar(200) DEFAULT NULL,
  `status`       varchar(20)  NOT NULL DEFAULT 'pending' COMMENT 'pending/approved/rejected',
  `approved_by`  varchar(36)  DEFAULT NULL COMMENT '실제 처리한 결재자 user_id(계정 id, JWT user_id). LV-02 정정: employee_id 아님',
  `approved_at`  datetime(6)  DEFAULT NULL,
  `reject_reason` varchar(200) DEFAULT NULL,
  `created_at`   datetime(6)  NOT NULL DEFAULT current_timestamp(6),
  `updated_at`   datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`request_id`),
  KEY `idx_leave_tenant_emp` (`tenant_id`,`employee_id`),
  KEY `idx_leave_status`     (`tenant_id`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
