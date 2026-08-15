-- pricing_plans 시드 영역 (사장님 결재 2026-06-11)
-- 가격 영역 동적 로드 정합 — 사장님이 백오피스 PricingAdminController에서 자유 변경 가도
--
-- 진단 (2026-06-11 19:09):
--   NCP에 pricing_plans 테이블 이미 존재 — PricingAdminController가 가도 중
--   기존 스키마: plan_id (VARCHAR PK, 코드 영역 직접) — 'basic', 'pro', 'enterprise' 등
--   plan_code 컬럼 0건 → 본 SQL은 plan_id 영역으로 정정
--
-- 가도 영역:
--   scp -i "C:\Users\소순근\Downloads\hitpan-key.pem" scripts\pricing_plans_seed.sql root@211.188.58.140:/tmp/
--   ssh -i "C:\Users\소순근\Downloads\hitpan-key.pem" root@211.188.58.140
--   mysql -u hitpan_back -p7THbr0zkaQ6XTYXgJDNkj320keYdg9PZ hitpan_backoffice < /tmp/pricing_plans_seed.sql

-- ============================================================
-- 기존 영역 점검 — 이미 박힌 영역 확인
-- ============================================================

SELECT '=== 기존 영역 ===' AS info;
SELECT plan_id, plan_name, monthly_price, display_order, is_active, is_visible
FROM pricing_plans
ORDER BY display_order;

-- ============================================================
-- 시드 가도 (이미 박힌 영역 = UPDATE / 0건 영역 = INSERT)
-- ============================================================

-- 🔴 2026-08-16 B-3 봉합 — 기기 대수가 ERP 정본과 **완전히 달랐다.**
--
--   [무엇이 났나] 이 표의 max_pc_devices·max_mobile_devices 를
--     DeviceRegistrationController(:65) 가 **그대로 읽어 한도로 쓴다.**
--     그런데 숫자가 ERP 의 정본(SlotPolicyDefaults·DB-104)과 갈라져 있었다:
--       basic       PC 2 / 모바일 0   ← 정본 PC 5 / 모바일 3
--       pro         PC 3 / 모바일 2   ← 정본 PC 10 / 모바일 8
--       enterprise  PC 10 / 모바일 10 ← 정본 PC 100 / 모바일 80
--
--   🔴 특히 **basic 의 모바일이 0** 이라 `typeCount >= deviceLimit` 이 **항상 참**이다
--     ⇒ 베이직 고객은 **휴대기기를 한 대도 등록하지 못한다.** 산 것을 못 쓴다.
--
--   [고침] 숫자를 ERP 정본에 맞춘다. ⚠️ 코드는 각자 갖되(헌법 #35 3시스템 분리)
--     **숫자가 어긋나는 것**은 분리가 아니라 결함이다.
--   ⚠️ max_users 는 손대지 않는다 — 계정은 무제한이 사장님 결재이고(과금 단위는 기기),
--     이 칸의 의미는 별도 확인이 필요하다. 이번 봉합 범위는 **기기 대수뿐**이다.
--
--   🔴 이 파일은 **NCP 백오피스 DB** 를 바꾼다. 실행은 사장님 결재 후에만 한다
--     (헌법 #29 인프라 조작 사전 승인제 · #39 운영 직접 수술 금지).
--
--   ⚠️ 값이 ERP 정본과 갈라지면 게이트가 막는다 — DeviceSlotGuardTests.B3_*
INSERT INTO pricing_plans
    (plan_id, plan_name, description, monthly_price, yearly_price, price_display,
     max_users, max_devices, max_pc_devices, max_mobile_devices, ai_token_monthly,
     is_active, is_visible, display_order)
VALUES
    ('basic', '베이직', '소규모 사업자 영역 — 기본 ERP 가도',
     29000, 290000, '₩29,000',
     3, 8, 5, 3, 100000,
     1, 1, 1),
    ('pro', '프로', '중소기업 영역 — 모바일 + AI 챗봇 가도',
     49000, 490000, '₩49,000',
     10, 18, 10, 8, 500000,
     1, 1, 2),
    ('enterprise', '엔터프라이즈', '대형 사업자 영역 — 무제한 + 전담 CS',
     100000, 1000000, '₩100,000',
     30, 180, 100, 80, 3000000,
     1, 1, 3)
ON DUPLICATE KEY UPDATE
    plan_name = VALUES(plan_name),
    description = VALUES(description),
    monthly_price = VALUES(monthly_price),
    yearly_price = VALUES(yearly_price),
    price_display = VALUES(price_display),
    max_users = VALUES(max_users),
    max_devices = VALUES(max_devices),
    max_pc_devices = VALUES(max_pc_devices),
    max_mobile_devices = VALUES(max_mobile_devices),
    ai_token_monthly = VALUES(ai_token_monthly),
    is_active = VALUES(is_active),
    is_visible = VALUES(is_visible),
    display_order = VALUES(display_order);

-- ============================================================
-- 검증 영역
-- ============================================================

SELECT '=== 가도 후 영역 ===' AS info;
SELECT plan_id, plan_name, monthly_price, max_users, display_order, is_active, is_visible
FROM pricing_plans
ORDER BY display_order;
