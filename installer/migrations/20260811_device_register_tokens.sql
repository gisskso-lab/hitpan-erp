-- ═══════════════════════════════════════════════════════════════════════════
-- 20260811작1 (D) — 모바일기기 등록 QR 토큰 테이블
--   사장님 오더 2026-08-11: "QR토큰전용 테이블 생성해"
--
-- ■ 왜 마이그레이션이 따로 필요한가
--   hitpan_db_clean.sql 은 **신규 설치** 단일 진실원이다(헌법 #36).
--   이미 돌고 있는 고객 DB 에는 자동 반영되지 않는다 — 그래서 이 파일이 있다.
--   (2026-06 감사에서 "clean DDL 에 컬럼을 추가해도 운영 DB 는 안 바뀐다" 가 미결로 남았던 그 자리다)
--
-- ■ 멱등
--   IF NOT EXISTS 라 여러 번 돌려도 안전하다. 업데이트가 두 번 적용돼도 깨지지 않는다.
--
-- ■ 왜 평문 토큰을 저장하지 않나
--   QR 에 담기는 값은 그 자체로 기기 등록 권한이다. DB 가 유출돼도 남의 폰을 등록하지
--   못하게, 해시만 저장한다(sync_tokens 와 같은 원칙 · 헌법 #5).
-- ═══════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS `device_register_tokens` (
  `token_id` char(36) NOT NULL COMMENT 'UUID',
  `tenant_id` char(36) NOT NULL COMMENT '테넌트 ID',
  `token_hash` varchar(128) NOT NULL COMMENT 'SHA-256 hash — 평문 토큰은 저장하지 않는다',
  `issued_by` varchar(36) DEFAULT NULL COMMENT '발급한 대표계정 user_id — QR 을 띄운 사람이 곧 승인자다',
  `issued_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) COMMENT '발급 시각',
  `expires_at` datetime(6) NOT NULL COMMENT '만료 시각 (발급 + 10분)',
  `used_at` datetime(6) DEFAULT NULL COMMENT '사용 시각 — 1회용',
  `used_device_id` varchar(36) DEFAULT NULL COMMENT '이 토큰으로 등록된 기기',
  PRIMARY KEY (`token_id`),
  UNIQUE KEY `uq_devreg_token_hash` (`token_hash`),
  KEY `idx_devreg_tenant_active` (`tenant_id`,`used_at`,`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='모바일기기 등록 QR 토큰 — 10분 만료·1회용, 해시만 저장';
