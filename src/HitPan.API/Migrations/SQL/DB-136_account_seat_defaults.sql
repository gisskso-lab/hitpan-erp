-- =============================================================
-- DB-136: 계정 과금 — 기본 제공 계정 5 · 추가 구매 계정 칸 · 슬롯 칸 「폐기 · 읽지 않음」 주석
-- =============================================================
-- 근거: 작업지시서 docs/운영기록/20261005작3_직원계정관리_계정과금_작업지시서.md (PM 결재 P-1 P-3)
--       설계문서   docs/설계/erp/20261005_설계_직원계정관리_계정과금.md §6
--
-- #13: 출하 DDL installer/hitpan_db_clean.sql 정의 실측(설치된 히트판 DB 접속 0 · #39)
--   local_subscription.max_users          tinyint(3) unsigned NOT NULL DEFAULT 3  (주석 없음)
--   local_subscription.extra_device_slots int(11) NOT NULL DEFAULT 0              (주석 없음)
--   tenants.max_users                     tinyint(3) unsigned NOT NULL DEFAULT 3  (주석 없음)
--   tenants.extra_device_slots            int(11) NOT NULL DEFAULT 0 (주석 있음)
--   device_slot_policy_settings           표 주석 있음 (DB-104)
--   ⇒ MODIFY 는 타입 NULL DEFAULT 를 위 그대로 옮겨 적고 주석과 기본값만 바꾼다
--
-- 문장
--   (1) local_subscription.max_users 기본값 3 에서 5 로 (베이직 · 대표 포함 총 계정)
--   (2) local_subscription.extra_accounts 새 칸 (본사가 보내는 추가 구매 계정 수 · 기본 0)
--   (3) tenants.max_users 기본값 5 (ERP 안 읽힘 · 기본값 정합만 · 값 무접촉)
--   (4) 옛 출하 기본값 3 그대로인 local_subscription 행만 5 로 (P-3 · 닿는 곳 = 사장님 PC 뿐 · D-6)
--   (5) 슬롯 칸 두 개와 슬롯 기준값 표에 주석만 「2026-10-05 폐기 · 읽지 않음」 (D-12 · #37 칸 값 쓰기 무접촉)
--   (6) 10/5 봉합1 P2-07 — 옛 DELETE 로 지운 계정(is_deleted=1 · 표식 없음)에 계정폐기 표식을 붙여 아이디 자리를 비운다
--       표식 = retired+{user_id}+{LEFT(email,40)} (RetireAsync 와 같은 모양 · 85자 ≤ varchar(100))
--       이미 retired+ 또는 resigned+ 표식이 있으면 건너뛴다 (멱등 · 두 번째엔 고칠 행이 없다)
--       대표(is_parent=1) 행은 건드리지 않는다 (#40 · 옛 DELETE 도 대표는 막았다)
--
-- 지우는 칸 0 · 이름바꿈 0 · 새 표 0 (#17 해당 없음) · extra_device_slots 값 무접촉 (수신 캐시 · 되돌림 판이 읽는다)
-- 멱등: (2) IF NOT EXISTS · (1)(3)(5) 같은 정의 재적용 안전 · (4) 두 번째엔 3 인 행이 없다
-- 되돌림: 옛 판은 새 칸을 안 읽을 뿐이다. 데이터 되돌림 불필요 (설계 §9)
-- ⚠️ 이 주석에는 문장 끝 기호를 일부러 적지 않았다
-- =============================================================

ALTER TABLE local_subscription
    MODIFY COLUMN max_users TINYINT(3) UNSIGNED NOT NULL DEFAULT 5
        COMMENT '기본 제공 총 계정 수 — 대표 포함(베이직 5 · 프로 8 · 본사가 보냄). 한도 = max_users + extra_accounts (DB-136)';

ALTER TABLE local_subscription
    ADD COLUMN IF NOT EXISTS extra_accounts INT(11) NOT NULL DEFAULT 0
        COMMENT '추가 구매 계정 수(본사가 보냄 · 10/5 작3 · DB-136)' AFTER extra_device_slots;

ALTER TABLE tenants
    MODIFY COLUMN max_users TINYINT(3) UNSIGNED NOT NULL DEFAULT 5;

UPDATE local_subscription SET max_users = 5 WHERE max_users = 3;

ALTER TABLE local_subscription
    MODIFY COLUMN extra_device_slots INT(11) NOT NULL DEFAULT 0
        COMMENT '2026-10-05 폐기 · 읽지 않음(계정 과금 전환 · D-12 · DB-136). 본사가 보내면 받아 적기만 한다';

ALTER TABLE tenants
    MODIFY COLUMN extra_device_slots INT(11) NOT NULL DEFAULT 0
        COMMENT '2026-10-05 폐기 · 읽지 않음(계정 과금 전환 · D-12 · DB-136). 옛 뜻: 추가 구매 디바이스 슬롯';

ALTER TABLE device_slot_policy_settings
    COMMENT = '2026-10-05 폐기 · 읽지 않음(계정 과금 전환 · D-12 · DB-136). 옛 뜻: 기기 슬롯 기준값(DB-104)';

UPDATE users SET
    email = CONCAT('retired+', user_id, '+', LEFT(email, 40)),
    updated_at = NOW(6)
WHERE is_deleted = 1
  AND is_parent = 0
  AND email NOT LIKE 'retired+%'
  AND email NOT LIKE 'resigned+%';
