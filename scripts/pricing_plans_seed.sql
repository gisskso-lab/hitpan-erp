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

INSERT INTO pricing_plans
    (plan_id, plan_name, description, monthly_price, yearly_price, price_display,
     max_users, max_devices, max_pc_devices, max_mobile_devices, ai_token_monthly,
     is_active, is_visible, display_order)
VALUES
    ('basic', '베이직', '소규모 사업자 영역 — 기본 ERP 가도',
     29000, 290000, '₩29,000',
     3, 2, 2, 0, 100000,
     1, 1, 1),
    ('pro', '프로', '중소기업 영역 — 모바일 + AI 챗봇 가도',
     49000, 490000, '₩49,000',
     10, 5, 3, 2, 500000,
     1, 1, 2),
    ('enterprise', '엔터프라이즈', '대형 사업자 영역 — 무제한 + 전담 CS',
     100000, 1000000, '₩100,000',
     30, 20, 10, 10, 3000000,
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
