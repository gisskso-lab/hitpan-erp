-- =============================================================
-- DB-104: 기기 슬롯 기준값 설정 (요금 한도를 코드에서 DB 로)
-- =============================================================
-- 근거: 작업지시서 docs/운영기록/20260815작3_기기식별_슬롯과금_1차_작업지시서.md (P2)
--       구조 진실원 docs/설계/erp/20260815_아키텍처명세서_기기식별_슬롯과금_재설계.md §17-4
--       현행 실측   docs/개발/erp/20260815_P0_현행봉인_계수스냅샷_기기슬롯.md ④-C
--
-- ─────────────────────────────────────────────────────────────
-- 왜 이 표가 필요한가
-- ─────────────────────────────────────────────────────────────
--
--   지금 요금 한도는 **코드 안에 숫자로 있다**:
--     TenantDeviceService.cs:662-669  basic (5,3) · pro (10,8) · …
--
--   ⇒ 사장님이 "베이직 PC 를 6대로 올리자" 고 하시면 **코드를 고치고 다시 배포**해야 한다.
--     요금제는 사업이 정하는 것이지 개발이 정하는 것이 아니다.
--     헌법 #11 — 기준값은 어드민이 설정한다. 코드 상수 금지.
--
--   🔴 DB-96(노무 기준값) 이 이미 같은 판단을 했다:
--     *"법정값은 전부 설정 테이블. 하드코딩 금지, **상수로 빼는 것도 금지**
--       (재배포가 필요하면 실패다)"*
--     요금도 똑같다. 상수로 옮기는 것은 설정화가 아니다 — 자리만 옮긴 것이다.
--
-- ─────────────────────────────────────────────────────────────
-- 🔴 왜 appsettings.json 이 아닌가 — 헌법 #21
-- ─────────────────────────────────────────────────────────────
--
--   헌법 #21 은 `appsettings.json`·`appsettings.Development.json` 의 **삭제·수정을 금지**한다
--   (WASM 부트에 필수라 건드리면 화면 자체가 안 뜬다). 작업지시서도 "무접촉" 을 명시했다.
--
--   ⚠️ `DeviceApproval:Enabled`(TenantDeviceService.cs:55)가 이미 appsettings 를 읽고 있으나
--     **그 전례를 근거로 삼지 않는다**(P0 실측 D-13 이 명시적으로 경고한 자리다).
--     그것은 켜고 끄는 스위치고, 이것은 **회사마다 다른 요금 한도**다. 성격이 다르다.
--
--   그리고 파일 설정으로는 애초에 못 담는다:
--     · 한 PC 에 여러 회사가 들어올 수 있다(테넌트별로 값이 달라야 한다)
--     · 대표계정이 화면에서 고칠 수 있어야 한다(파일은 못 고친다)
--     · 누가 언제 왜 고쳤는지 남아야 한다(요금이라 더욱)
--   ⇒ **테넌트 축을 가진 DB 표**가 유일한 답이다. DB-96 과 같은 형태를 쓴다.
--
-- ─────────────────────────────────────────────────────────────
-- 🔴 왜 백오피스(pricing_plans)를 실시간으로 읽지 않는가
-- ─────────────────────────────────────────────────────────────
--
--   백오피스에도 같은 값이 있다(pricing_plans.max_pc_devices / max_mobile_devices).
--   그것을 ERP 가 실시간 조회하면 **본사 의존이 생긴다** — 헌법 #30 위반.
--   본사가 죽거나 인터넷이 끊기면 고객이 로그인을 못 한다.
--   ⇒ **로컬에 내려받아 저장하는 방향**이어야 한다(P0 실측 D-15).
--     이 표가 그 "내려받아 저장하는 자리" 다. 채우는 경로(동기화)는 2차다.
--
-- ─────────────────────────────────────────────────────────────
-- ⚠️ 이 마이그레이션이 바꾸지 않는 것 — 숫자
-- ─────────────────────────────────────────────────────────────
--
--   시드 값은 **현행 코드와 한 글자도 다르지 않다**:
--     basic (5,3) · pro (10,8) · premium (100,80) · trial (10,5)
--   작업지시서 §6: *"현행 값을 초기값 그대로. 숫자 변경은 범위 밖."*
--   ⇒ 이 마이그를 적용해도 **한도가 하나도 안 바뀐다.** 그것이 무회귀의 정의다.
--
--   🔴 단 하나 바뀌는 것은 `enterprise` 다 — 이것은 **정정**이지 변경이 아니다(아래 참조).
--
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB)
-- 헌법: #1 추가만 / #9 / #11 어드민 설정 / #17 InnoDB / #21 appsettings 무접촉 / #30 본사 의존 0 / #36
-- 멱등: CREATE TABLE IF NOT EXISTS + INSERT … WHERE NOT EXISTS.
-- =============================================================

CREATE TABLE IF NOT EXISTS `device_slot_policy_settings` (
  `policy_id`      varchar(36)  NOT NULL COMMENT 'PK',
  `tenant_id`      varchar(36)  NOT NULL COMMENT '테넌트(헌법 #2 — JWT 클레임에서만 온다)',
  `policy_key`     varchar(60)  NOT NULL COMMENT '기준값 열쇠. 예: tier.basic.pc_limit',
  `policy_value`   int(11)      NOT NULL COMMENT '값. 대수·금액 모두 정수 하나로 담는다',
  `value_unit`     varchar(20)  NOT NULL DEFAULT 'count' COMMENT '단위: count(대)|krw(원)',
  `label`          varchar(100) NOT NULL COMMENT '사람이 읽는 이름. 화면에 그대로 보여준다',
  `description`    varchar(300) DEFAULT NULL COMMENT '무엇을 뜻하는 값인지',
  `updated_by`     varchar(36)  DEFAULT NULL COMMENT '누가 고쳤나',
  `updated_reason` varchar(200) DEFAULT NULL COMMENT '🔴 왜 고쳤나 — 요금이라 수정 이력이 필수다',
  `created_at`     datetime(6)  NOT NULL DEFAULT current_timestamp(6),
  `updated_at`     datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`policy_id`),
  UNIQUE KEY `uk_slot_policy_tenant_key` (`tenant_id`, `policy_key`),
  KEY `idx_slot_policy_lookup` (`tenant_id`, `policy_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='기기 슬롯 기준값 — 요금제가 바뀌면 값만 갈아끼운다(코드 재배포 없이). DB-104';

-- ═══════════════════════════════════════════════════════════════
-- 기본값 시드 — 🔴 현행 코드(TenantDeviceService.cs:662-669)와 동일
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 `enterprise` 는 신설이 아니라 **정정**이다 (명세서 §17-4 · PI-14)
--
--   코드만 `premium` 이라 쓰고 있고, **DDL·시리얼 발급·백오피스는 전부 `enterprise`** 다:
--     · local_subscription.subscription_tier  COMMENT 'basic / pro / enterprise'
--     · ResellerSerialController.cs:86         tier != "basic" && != "pro" && != "enterprise"
--     · 백오피스 SerialIssueDialog / 랜딩 PricingSection  전부 enterprise
--   ⇒ 실제로 발급되는 값은 `enterprise` 인데 코드에는 그 갈래가 없어
--     `_ => (5,3)` 으로 떨어진다. **최상위 고객이 basic 한도(PC 5)를 받는다.**
--     10만원을 내고 베이직 한도를 쓰는 것이다 — 지금도 나고 있는 사고다.
--
--   ⚠️ `premium` 도 함께 남긴다 — 지우지 않는다.
--     옛 데이터에 premium 이 들어 있을 수 있고(랜딩 PaymentPage.razor:124 가
--     "옛 키 호환: premium → enterprise" 라고 적고 있다 = 실재했다는 뜻),
--     지우면 그 고객이 조용히 basic 으로 떨어진다(P0 실측 D-16).
--     ⇒ 두 값을 **같은 한도**로 둔다. 어느 쪽이 와도 안 깨진다.
--
-- ⚠️ 추가슬롯 산식 — 🔴 사장님 확정으로 정정한다
--
--   현행 코드: pcLimit += extra · mobileLimit += extra * 2   ← **1+2**
--   사장님 확정: *"추가슬롯 1+1(PC, 모바일)당 1만원이야"*     ← **1+1**
--
--   ⇒ extra_per_slot_mobile 을 **1** 로 시드한다.
--   ⚠️ 이것은 모바일 한도가 **줄어드는** 방향이다(P0 실측 D-14).
--     실사용 고객이 0 이라 지금은 안전하나, 추가슬롯을 산 테스트 환경이 있으면
--     이미 등록된 모바일 기기가 한도를 넘겨 다음 로그인이 막힐 수 있다.
--     ⇒ 갱신 경로는 한도를 다시 보지 않으므로(P0 경우 2) **쓰던 기기는 안 막힌다.**
--       막히는 것은 그 상태에서 **새로 늘리려 할 때**뿐이다.

INSERT INTO device_slot_policy_settings
  (policy_id, tenant_id, policy_key, policy_value, value_unit, label, description)
SELECT UUID(), t.tenant_id, s.policy_key, s.policy_value, s.value_unit, s.label, s.description
FROM (SELECT DISTINCT tenant_id FROM local_subscription) AS t
CROSS JOIN (
    -- ── 티어별 기본 제공 대수 (현행 코드와 동일) ──
              SELECT 'tier.basic.pc_limit'      AS policy_key, 5   AS policy_value, 'count' AS value_unit,
                     '베이직 · 컴퓨터 대수'      AS label,
                     '베이직 요금제가 기본 제공하는 컴퓨터 대수' AS description
    UNION ALL SELECT 'tier.basic.mobile_limit',      3, 'count', '베이직 · 휴대기기 대수',
                     '베이직 요금제가 기본 제공하는 휴대기기 대수'
    UNION ALL SELECT 'tier.pro.pc_limit',           10, 'count', '프로 · 컴퓨터 대수',
                     '프로 요금제가 기본 제공하는 컴퓨터 대수'
    UNION ALL SELECT 'tier.pro.mobile_limit',        8, 'count', '프로 · 휴대기기 대수',
                     '프로 요금제가 기본 제공하는 휴대기기 대수'

    -- 🔴 enterprise = 실제로 발급되는 최상위 값. 없어서 basic 으로 떨어지던 것을 정정한다
    UNION ALL SELECT 'tier.enterprise.pc_limit',   100, 'count', '엔터프라이즈 · 컴퓨터 대수',
                     '엔터프라이즈 요금제가 기본 제공하는 컴퓨터 대수'
    UNION ALL SELECT 'tier.enterprise.mobile_limit', 80, 'count', '엔터프라이즈 · 휴대기기 대수',
                     '엔터프라이즈 요금제가 기본 제공하는 휴대기기 대수'

    -- ⚠️ premium = 옛 이름. enterprise 와 같은 한도로 남긴다(지우면 basic 으로 떨어진다)
    UNION ALL SELECT 'tier.premium.pc_limit',      100, 'count', '프리미엄(옛 이름) · 컴퓨터 대수',
                     '엔터프라이즈의 옛 이름. 같은 한도를 준다'
    UNION ALL SELECT 'tier.premium.mobile_limit',   80, 'count', '프리미엄(옛 이름) · 휴대기기 대수',
                     '엔터프라이즈의 옛 이름. 같은 한도를 준다'

    UNION ALL SELECT 'tier.trial.pc_limit',         10, 'count', '체험 · 컴퓨터 대수',
                     '체험 기간에 제공하는 컴퓨터 대수'
    UNION ALL SELECT 'tier.trial.mobile_limit',      5, 'count', '체험 · 휴대기기 대수',
                     '체험 기간에 제공하는 휴대기기 대수'

    -- ── 모르는 요금제가 왔을 때 (현행 `_ => (5,3)` 과 동일) ──
    UNION ALL SELECT 'tier.default.pc_limit',        5, 'count', '기본값 · 컴퓨터 대수',
                     '모르는 요금제가 들어왔을 때 주는 컴퓨터 대수'
    UNION ALL SELECT 'tier.default.mobile_limit',    3, 'count', '기본값 · 휴대기기 대수',
                     '모르는 요금제가 들어왔을 때 주는 휴대기기 대수'

    -- ── 추가슬롯 1개를 사면 몇 대가 늘어나나 — 🔴 1+1 (사장님 확정) ──
    UNION ALL SELECT 'extra_slot.pc_per_slot',       1, 'count', '추가슬롯 1개당 컴퓨터',
                     '추가슬롯 하나를 사면 늘어나는 컴퓨터 대수'
    UNION ALL SELECT 'extra_slot.mobile_per_slot',   1, 'count', '추가슬롯 1개당 휴대기기',
                     '추가슬롯 하나를 사면 늘어나는 휴대기기 대수. 사장님 확정 1+1'

    -- ── 추가슬롯 가격 ──
    UNION ALL SELECT 'extra_slot.price_krw',     10000, 'krw',   '추가슬롯 1개 가격(원)',
                     '컴퓨터 1대 + 휴대기기 1대를 늘리는 값. 사장님 확정 1만원'
) AS s
WHERE NOT EXISTS (
    -- 🔴 이미 기준값을 손댄 회사는 건드리지 않는다(헌법 #1 덮어쓰기 금지 · #11).
    SELECT 1 FROM device_slot_policy_settings p WHERE p.tenant_id = t.tenant_id
);
