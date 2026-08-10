-- =============================================================
-- DB-87: 모바일기기 등록 QR 토큰 테이블 신설
-- =============================================================
-- 근거: 사장님 오더 2026-08-11 "QR토큰전용 테이블 생성해"
--       작업지시서: docs/운영기록/20260811작1_챕터4_기기인증_슬롯판매_작업지시서.md
--
-- 🔴 왜 이 파일이 뒤늦게 생겼나 — 실측이 잡은 PM 오류
--
--   1.2.63 게시 직후 사장님 실측에서 **"QR생성실패 500"** 이 났다.
--   원인: PM 이 마이그레이션을 `installer/migrations/` 에 두었다. 그 폴더는 **아무도 실행하지 않는다.**
--   히트판의 마이그레이션 진실원은 이 폴더(`src/HitPan.API/Migrations/SQL/DB-NN_*.sql`)이고,
--   MigrationRunner 가 여기만 읽는다. 엉뚱한 곳에 둔 SQL 은 파일로만 존재했고
--   test1 DB 에는 테이블이 안 생겼다 ⇒ INSERT 하려다 500.
--
--   교훈: "SQL 파일을 만들었다" 와 "그 SQL 이 실행된다" 는 다르다.
--         새 테이블을 넣을 때는 **누가 이 파일을 실행하는가**를 먼저 확인한다.
--
-- ⚠️ 이 릴리스는 manifest.requiresMigration=true 로 게시해야 한다.
--    false 로 나가면 워치독 교차검증 게이트가 "SQL 은 있는데 플래그가 false" 로 보고
--    업데이트를 강제 중단한다(UpdateOrchestrator ①번 게이트).
--
-- 무엇을 담나
--   모바일기기 등록 QR 의 1회용 토큰. 대표계정이 [모바일기기 등록] 을 누르면 발급되고,
--   폰이 QR 을 찍어 등록을 마치면 소모된다.
--
-- 왜 평문 토큰을 저장하지 않나
--   이 토큰은 그 자체로 **기기 등록 권한**이다. DB 가 유출돼도 남의 폰을 등록하지 못하도록
--   SHA-256 해시만 남긴다(sync_tokens 와 같은 원칙 · 헌법 #5).
--
-- 왜 10분인가
--   QR 이 화면에 떠 있는 동안만 유효해야 한다. 길게 두면 지나간 화면을 찍은 사진으로도
--   등록이 되어, 대표계정이 승인한 그 순간과 실제 등록이 벌어진다.
--
-- 멱등: IF NOT EXISTS — 업데이트가 두 번 적용돼도 깨지지 않는다.
-- =============================================================

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
