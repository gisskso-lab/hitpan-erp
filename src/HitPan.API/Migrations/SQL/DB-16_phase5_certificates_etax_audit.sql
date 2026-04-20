-- ============================================================
-- Phase 5: 도메인 감사 + 인증서 관리 + 전자세금계산서 3중구조
-- 작성일: 2026-04-21
-- 작성자: PM 닥터스트레인지 (DB매니저 설계 기반)
-- ------------------------------------------------------------
-- 실행 순서: DB-15 이후 실행
-- 롤백 정책: 블록 단위 역순 롤백 스크립트 별도 관리
-- ------------------------------------------------------------
-- 블록 구성:
--   ① audit_trail           — 도메인 감사로그 (신규)
--   ② tenant_certificates   — 범용인증서 관리 (신규)
--   ③ tenant_etax_settings  — 테넌트별 예약발행 설정 (신규)
--   ④ tax_invoices ALTER    — 3중구조 상태머신 컬럼 확장
--   ⑤ 트리거 + 마이그레이션  — 발행완료 불변성 + 기존 데이터 정리
-- ============================================================


-- ------------------------------------------------------------
-- ① 도메인 감사로그 (audit_trail)
--    용도: cashbook 수정, 인증서 사용, 세무 상태 전이, 권한 변경
--    audit_logs(HTTP 로그)와 별개 — 비즈니스 이벤트 전건 기록
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `audit_trail` (
  `log_id`         VARCHAR(36)  NOT NULL COMMENT '감사로그 PK',
  `tenant_id`      VARCHAR(36)  NOT NULL COMMENT '테넌트 ID',
  `user_id`        VARCHAR(36)  DEFAULT NULL COMMENT '행위자 사용자 ID',
  `actor_name`     VARCHAR(50)  DEFAULT NULL COMMENT '행위자 이름 (조회 편의)',
  `action_type`    VARCHAR(20)  NOT NULL COMMENT 'create/update/delete/access/state_change',
  `entity_type`    VARCHAR(30)  NOT NULL COMMENT 'cashbook/certificate/tax_invoice/permission/monthly_closing 등',
  `entity_id`      VARCHAR(36)  DEFAULT NULL COMMENT '대상 엔티티 PK',
  `before_value`   TEXT         DEFAULT NULL COMMENT '변경 전 값 (JSON)',
  `after_value`    TEXT         DEFAULT NULL COMMENT '변경 후 값 (JSON)',
  `reason`         VARCHAR(500) DEFAULT NULL COMMENT '사유 (재오픈·폐기 등)',
  `ip_address`     VARCHAR(50)  DEFAULT NULL,
  `user_agent`     VARCHAR(500) DEFAULT NULL,
  `created_at`     DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`log_id`),
  KEY `idx_tenant_created` (`tenant_id`, `created_at`),
  KEY `idx_entity` (`entity_type`, `entity_id`),
  KEY `idx_actor` (`tenant_id`, `user_id`, `created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='도메인 감사로그 — 비즈니스 이벤트 전건 기록 (HTTP 로그는 audit_logs)';


-- ------------------------------------------------------------
-- ② 범용인증서 관리 (tenant_certificates)
--    용도: 전자세금계산서·홈택스 연동용 공동인증서 저장
--    보안: cert_file_encrypted는 AES-256 암호화 (Value Converter)
--          password_ref는 KMS/DPAPI 참조 (실패스워드 저장 금지)
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `tenant_certificates` (
  `cert_id`              VARCHAR(36)    NOT NULL COMMENT '인증서 PK',
  `tenant_id`            VARCHAR(36)    NOT NULL COMMENT '테넌트 ID',
  `cert_type`            VARCHAR(20)    NOT NULL DEFAULT 'general' COMMENT 'general(범용)/financial(금융)/tax(세무전용)',
  `subject_name`         VARCHAR(200)   NOT NULL COMMENT '인증서 주체명 (사업자명)',
  `issuer_name`          VARCHAR(200)   DEFAULT NULL COMMENT '발급기관 (예: yessign, KICA)',
  `serial_no`            VARCHAR(100)   DEFAULT NULL COMMENT '인증서 시리얼 번호',
  `valid_from`           DATETIME       NOT NULL COMMENT '유효기간 시작',
  `valid_until`          DATETIME       NOT NULL COMMENT '유효기간 만료',
  `cert_file_encrypted`  LONGBLOB       NOT NULL COMMENT 'PFX/P12 파일 AES-256 암호화',
  `password_ref`         VARCHAR(100)   NOT NULL COMMENT 'KMS 키 참조 (실패스워드 금지)',
  `is_primary`           TINYINT(1)     NOT NULL DEFAULT 0 COMMENT '기본 인증서 여부',
  `last_used_at`         DATETIME(6)    DEFAULT NULL COMMENT '최근 사용 시각',
  `status`               VARCHAR(20)    NOT NULL DEFAULT 'active' COMMENT 'active/expired/revoked',
  `uploaded_by`          VARCHAR(36)    DEFAULT NULL COMMENT '업로드 사용자',
  `created_at`           DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at`           DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`cert_id`),
  UNIQUE KEY `uq_tenant_serial` (`tenant_id`, `serial_no`),
  KEY `idx_tenant_status` (`tenant_id`, `status`),
  KEY `idx_valid_until` (`valid_until`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='테넌트별 범용인증서 — AES-256 암호화 저장';


-- ------------------------------------------------------------
-- ③ 전자세금계산서 발행 설정 (tenant_etax_settings)
--    용도: 테넌트별 예약발행 시각·요일·공휴일 설정
--    기본값: 매일 18:00 (월~금), 공휴일 제외, 재시도 3회/1시간 간격
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `tenant_etax_settings` (
  `tenant_id`          VARCHAR(36)    NOT NULL COMMENT '테넌트 ID (PK)',
  `issue_mode`         VARCHAR(20)    NOT NULL DEFAULT 'scheduled' COMMENT 'scheduled(예약)/manual(건별수동)',
  `batch_time`         TIME           NOT NULL DEFAULT '18:00:00' COMMENT '매일 일괄발행 시각 (HH:MM)',
  `batch_weekdays`     VARCHAR(20)    NOT NULL DEFAULT '1,2,3,4,5' COMMENT '발행 요일 CSV (1=월..7=일)',
  `exclude_holidays`   TINYINT(1)     NOT NULL DEFAULT 1 COMMENT '공휴일 발행 제외 (1=제외/0=발행)',
  `notify_before_min`  INT            DEFAULT 30 COMMENT '발행 N분 전 알림 (NULL=알림없음)',
  `notify_manager_id`  VARCHAR(36)    DEFAULT NULL COMMENT '알림 수신 담당자 (employees.employee_id)',
  `retry_count`        INT            NOT NULL DEFAULT 3 COMMENT '전송 실패 시 재시도 횟수',
  `retry_interval_min` INT            NOT NULL DEFAULT 60 COMMENT '재시도 간격 (분)',
  `created_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `updated_by`         VARCHAR(36)    DEFAULT NULL,
  PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='테넌트별 전자세금계산서 발행 설정';


-- ------------------------------------------------------------
-- ④ tax_invoices ALTER — 3중구조 상태머신 확장
--    상태 흐름: draft → issued → etax_scheduled → etax_sending
--               → etax_confirmed (또는 etax_failed) → amended
--    기존 nts_status 는 홈택스 응답 세부상태로 역할 유지
-- ------------------------------------------------------------
ALTER TABLE `tax_invoices`
  ADD COLUMN IF NOT EXISTS `status` VARCHAR(20) NOT NULL DEFAULT 'draft'
    COMMENT '라이프사이클 상태 — draft/issued/etax_scheduled/etax_sending/etax_confirmed/etax_failed/amended',
  ADD COLUMN IF NOT EXISTS `issued_at` DATETIME(6) NULL
    COMMENT '① 계산서 발행 시각 (회계 인식 시점)',
  ADD COLUMN IF NOT EXISTS `issued_by` VARCHAR(36) NULL
    COMMENT '발행 담당자',
  ADD COLUMN IF NOT EXISTS `etax_scheduled_at` DATETIME(6) NULL
    COMMENT '② 전자발행 예약 시각',
  ADD COLUMN IF NOT EXISTS `etax_mode` VARCHAR(20) DEFAULT 'scheduled'
    COMMENT 'scheduled(기본)/immediate(즉시발행)',
  ADD COLUMN IF NOT EXISTS `etax_confirmed_at` DATETIME(6) NULL
    COMMENT '③ 전자발행 확정 시각 (홈택스 전송 완료)',
  ADD COLUMN IF NOT EXISTS `hometax_approval_no` VARCHAR(50) NULL
    COMMENT '홈택스 승인번호',
  ADD COLUMN IF NOT EXISTS `hometax_response` TEXT NULL
    COMMENT '홈택스 응답 원문 (감사용)',
  ADD COLUMN IF NOT EXISTS `amendment_of` VARCHAR(36) NULL
    COMMENT '수정계산서일 경우 원본 PK',
  ADD COLUMN IF NOT EXISTS `amendment_reason` VARCHAR(200) NULL
    COMMENT '수정계산서 사유',
  ADD INDEX IF NOT EXISTS `idx_status_scheduled` (`status`, `etax_scheduled_at`),
  ADD INDEX IF NOT EXISTS `idx_amendment` (`amendment_of`);


-- ------------------------------------------------------------
-- ⑤-1 트리거 — 발행완료(etax_confirmed/amended) 상태 변경 차단
--       관리자도 수정 불가 — 수정 필요 시 반드시 수정계산서 발행
-- ------------------------------------------------------------
DROP TRIGGER IF EXISTS `trg_tax_invoices_issued_lock`;
DELIMITER $$
CREATE TRIGGER `trg_tax_invoices_issued_lock`
BEFORE UPDATE ON `tax_invoices`
FOR EACH ROW
BEGIN
  IF OLD.`status` IN ('etax_confirmed','amended')
     AND NOT (NEW.`status` = 'amended' AND OLD.`status` = 'etax_confirmed')
  THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = '발행완료된 세금계산서는 수정할 수 없습니다. 수정계산서 발행을 이용하세요.';
  END IF;
END$$
DELIMITER ;


-- ------------------------------------------------------------
-- ⑤-2 데이터 마이그레이션 — 기존 레코드 status 초기화
--       nts_status 가 sent/accepted 인 건은 etax_confirmed 로 전환
--       나머지는 draft 유지 (기본값)
-- ------------------------------------------------------------
UPDATE `tax_invoices`
   SET `status` = 'etax_confirmed',
       `etax_confirmed_at` = COALESCE(`nts_sent_at`, `created_at`)
 WHERE `nts_status` IN ('sent','accepted')
   AND `status` = 'draft';

UPDATE `tax_invoices`
   SET `status` = 'etax_failed'
 WHERE `nts_status` = 'rejected'
   AND `status` = 'draft';


-- ============================================================
-- DB-16 마이그레이션 종료
-- 실행 후 검증 쿼리:
--   SELECT status, COUNT(*) FROM tax_invoices GROUP BY status;
--   SHOW TRIGGERS LIKE 'tax_invoices';
--   SHOW CREATE TABLE tenant_certificates\G
-- ============================================================
