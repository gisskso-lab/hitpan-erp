-- =============================================================
-- DB-89: 메인PC 를 DB 차원에서 보호한다
-- =============================================================
-- 근거: 사장님 실측 지적 2026-08-11
--       *"메인PC가 폐기되는게 말이되?"*
--       *"메인PC는 기기슬롯에서 PK로 보호해야해"*
--
-- 🔴 무엇이 났나
--   메인PC(회사 서버)가 `revoked` 가 되어 **로그인 자체가 막혔다.**
--   그 컴퓨터는 자료가 들어 있는 자리다. 거기서 막히면 대표계정이 등록기기 화면에
--   들어가 폐기를 되돌릴 수도 없다 — **스스로 못 빠져나온다.**
--
-- 🔴 왜 생겼나 — 막는 자리를 화면에만 뒀다
--   화면에는 자물쇠를 달아 폐기 버튼을 막아뒀다(DeviceManagePage).
--   그런데 그것은 **앞으로 눌리는 것**만 막는다. 다른 경로로 들어온 UPDATE 나
--   이미 revoked 인 기록은 손대지 못한다.
--   ⇒ 화면·서비스 코드에 흩어 두면 **입구가 늘어날 때마다 다시 뚫린다.**
--   ⇒ DB 가 마지막 방어선이 되어야 한다. 어디서 들어와도 통과하는 자리이기 때문이다.
--
-- 무엇을 보장하나 (2가지)
--   ① 메인PC 는 폐기 상태가 될 수 없다   — 트리거가 revoked 를 되돌린다
--   ② 테넌트당 메인PC 는 최대 1대        — 생성 컬럼 + UNIQUE
--
-- ⚠️ 왜 CHECK 가 아니라 트리거인가
--   MariaDB 의 CHECK 는 다른 행을 못 본다. "테넌트당 1대" 는 행 간 규칙이라
--   CHECK 로 표현할 수 없다. 그래서 ②는 UNIQUE 로, ①은 트리거로 나눠 건다.
--
-- ⚠️ 왜 막지 않고 되돌리나 (SIGNAL 이 아니라 SET)
--   폐기를 시도하면 오류를 던질 수도 있다. 그러나 그러면 그 UPDATE 를 포함한
--   다른 작업까지 통째로 실패한다. 메인PC 를 지키자고 **고객 화면에 500 을 띄우는 것**은
--   히트판 원칙(#25 쉽게)에 어긋난다. 조용히 approved 로 되돌리는 쪽이 안전하다.
--
-- 멱등: 트리거·컬럼 모두 존재 여부를 확인한 뒤에만 만든다.
-- =============================================================

-- ── ② 테넌트당 메인PC 1대 (UNIQUE) ──────────────────────────
--   main_pc_key = 메인PC 일 때만 tenant_id, 아니면 NULL.
--   MySQL/MariaDB 의 UNIQUE 는 NULL 을 중복으로 보지 않으므로
--   **일반 기기는 몇 대든 자유롭고, 메인PC 만 테넌트당 1행**으로 잠긴다.
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'tenant_devices'
    AND column_name = 'main_pc_key'
);

SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `tenant_devices`
     ADD COLUMN `main_pc_key` char(36)
       GENERATED ALWAYS AS (IF(`is_main_pc` = 1, `tenant_id`, NULL)) VIRTUAL
       COMMENT ''메인PC 1대 보장용 (DB-89). 메인PC 일 때만 tenant_id, 아니면 NULL''',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @idx_exists := (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE()
    AND table_name = 'tenant_devices'
    AND index_name = 'uq_tenant_main_pc'
);

SET @sql := IF(@idx_exists = 0,
  'ALTER TABLE `tenant_devices` ADD UNIQUE KEY `uq_tenant_main_pc` (`main_pc_key`)',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- ── ① 메인PC 는 폐기되지 않는다 (트리거) ────────────────────
--   INSERT·UPDATE 양쪽에 건다. 한쪽만 걸면 그 반대 경로로 들어온다.
DROP TRIGGER IF EXISTS `trg_tenant_devices_mainpc_bu`;
DROP TRIGGER IF EXISTS `trg_tenant_devices_mainpc_bi`;

CREATE TRIGGER `trg_tenant_devices_mainpc_bu`
BEFORE UPDATE ON `tenant_devices`
FOR EACH ROW
BEGIN
  -- 메인PC 를 폐기하려 하면 조용히 되돌린다 (오류를 던지지 않는 이유는 머리말 참조)
  IF NEW.`is_main_pc` = 1 AND NEW.`status` = 'revoked' THEN
    SET NEW.`status` = 'approved';
  END IF;
END;

CREATE TRIGGER `trg_tenant_devices_mainpc_bi`
BEFORE INSERT ON `tenant_devices`
FOR EACH ROW
BEGIN
  IF NEW.`is_main_pc` = 1 AND NEW.`status` = 'revoked' THEN
    SET NEW.`status` = 'approved';
  END IF;
END;


-- ── 이미 폐기된 메인PC 되살리기 (사장님 실측 건) ────────────
--   트리거는 앞으로 들어올 것만 막는다. **이미 revoked 인 기록**은 여기서 되돌린다.
--   이 한 줄이 없으면 사장님은 여전히 로그인하지 못한다.
UPDATE `tenant_devices`
   SET `status` = 'approved'
 WHERE `is_main_pc` = 1
   AND `status` = 'revoked';
