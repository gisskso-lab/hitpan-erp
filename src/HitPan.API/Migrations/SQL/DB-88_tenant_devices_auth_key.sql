-- =============================================================
-- DB-88: 기기 인증키 컬럼 추가 (tenant_devices)
-- =============================================================
-- 근거: 사장님 오더 2026-08-11
--         "사용PC에는 물리적으로 간단한 인증서 같은 인증키를 부여.
--          사용 모바일에는 QR로 모바일기기 인증키를 부여."
--         "인증 슬롯을 식별할 수 있도록 슬롯인증 절차에서 인증키 같은 걸 심자"
--       작업지시서: docs/운영기록/20260811작3_A_기기인증게이트_작업지시서.md
--       선행 합의:  docs/운영기록/20260811작1_챕터4_기기인증_슬롯판매_작업지시서.md
--
-- 무엇을 바꾸나
--   대표계정이 기기를 승인할 때 발급하는 **인증키**를 담을 자리를 만든다.
--
-- 🔴 왜 필요한가 — 묻는 대상을 바꾼다
--
--   지금은 서버가 **브라우저에게 "너 누구냐"** 를 묻는다(지문 = 화면크기·시간대 등을 섞은 추측).
--   브라우저는 자기 안에만 흔적을 남기므로 같은 컴퓨터라도 Edge 와 Chrome 이 각자 다른 답을 한다
--   ⇒ **한 대가 두 대로 세어지고, 고객이 쓰지도 않은 자리에 돈을 낸다.**
--
--   인증키는 묻는 대상을 **"네가 받은 키를 내놔라"** 로 바꾼다.
--   추측을 정교하게 만드는 게 아니라 **애초에 추측을 안 하게** 만든다(사장님 원안).
--
-- 왜 원문을 저장하지 않나
--   이 키는 그 자체로 **접속 권한**이다. DB 가 통째로 새어도 남의 기기로 들어올 수 없도록
--   SHA-256 해시만 남긴다. 원문은 발급 순간 한 번만 그 기기에 주고 우리는 갖지 않는다.
--   (device_register_tokens·sync_tokens 와 같은 원칙 · 헌법 #5)
--
-- 왜 NULL 을 허용하나
--   이미 등록된 기기들은 인증키를 받은 적이 없다. NOT NULL 로 만들면 그 기기들이
--   전부 막힌다 — 규칙을 켜서 쓰던 사람이 막히는 일은 만들지 않는다(2026-08-10 4차 사고 계통).
--   NULL 인 기기는 "아직 키를 안 받은 기기" 이고, 대표가 승인하면 그때 채워진다.
--
-- ⚠️ 이 릴리스는 manifest.requiresMigration=true 로 게시해야 한다.
--    false 로 나가면 워치독 교차검증 게이트가 "SQL 은 있는데 플래그가 false" 로 보고
--    업데이트를 강제 중단한다(UpdateOrchestrator ①번 게이트).
--
-- ⚠️ 같은 내용이 installer/hitpan_db_clean.sql 에도 들어가야 한다(헌법 #36).
--    ①clean DDL ②이 파일 ③requiresMigration — 세 곳 모두. ①만 하면 신규 고객은 되고
--    기존 고객만 깨진다. CI 는 이것을 못 잡는다.
--
-- 멱등: 여러 번 실행해도 안전하다(컬럼이 이미 있으면 건너뛴다).
-- =============================================================

-- auth_key_hash — 발급한 인증키의 SHA-256 해시 (원문 미저장)
SET @col_exists := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'tenant_devices'
      AND COLUMN_NAME  = 'auth_key_hash'
);
SET @sql := IF(@col_exists = 0,
    'ALTER TABLE `tenant_devices`
       ADD COLUMN `auth_key_hash` varchar(64) DEFAULT NULL
       COMMENT ''기기 인증키 SHA-256 (원문 미저장). NULL=아직 발급 안 됨'' AFTER `status`',
    'SELECT ''DB-88: auth_key_hash 이미 있음 — 건너뜀'' AS msg');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- auth_key_issued_at — 인증키를 발급한 시각 (언제부터 이 기기가 인정됐나)
SET @col_exists := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'tenant_devices'
      AND COLUMN_NAME  = 'auth_key_issued_at'
);
SET @sql := IF(@col_exists = 0,
    'ALTER TABLE `tenant_devices`
       ADD COLUMN `auth_key_issued_at` datetime(6) DEFAULT NULL
       COMMENT ''인증키 발급 시각'' AFTER `auth_key_hash`',
    'SELECT ''DB-88: auth_key_issued_at 이미 있음 — 건너뜀'' AS msg');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 인증키로 기기를 찾는 것이 매 요청 일어난다 — 인덱스 필수.
--   UNIQUE 로 두지 않는 이유: 해시가 겹칠 일은 없지만, 만에 하나 겹치면
--   그 순간 로그인이 통째로 막힌다. 유일성보다 **안 멈추는 것**이 우선이다.
SET @idx_exists := (
    SELECT COUNT(*) FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME   = 'tenant_devices'
      AND INDEX_NAME   = 'idx_auth_key_hash'
);
SET @sql := IF(@idx_exists = 0,
    'ALTER TABLE `tenant_devices` ADD KEY `idx_auth_key_hash` (`auth_key_hash`)',
    'SELECT ''DB-88: idx_auth_key_hash 이미 있음 — 건너뜀'' AS msg');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
