-- pricing_plans 테이블 영역 가도 (사장님 결재 2026-06-11)
-- 가격 영역 동적 로드 정합 — 사장님이 백오피스 PricingAdminController 영역에서 자유 변경 가도
--
-- 가도 영역: 사장님 SSH 박힌 후 NCP MariaDB에서 1회 가도
-- 명령:
--   ssh -i "C:\Users\소순근\Downloads\hitpan-key.pem" root@211.188.58.140
--   mysql -u hitpan_back -p7THbr0zkaQ6XTYXgJDNkj320keYdg9PZ hitpan_backoffice < pricing_plans_seed.sql
--
-- 또는 NCP에 SCP 업로드 후 가도:
--   scp -i "C:\Users\소순근\Downloads\hitpan-key.pem" scripts/pricing_plans_seed.sql root@211.188.58.140:/tmp/
--   ssh ... && mysql -u hitpan_back -p... hitpan_backoffice < /tmp/pricing_plans_seed.sql

-- ============================================================
-- pricing_plans 테이블 영역 (이미 존재 시 IGNORE)
-- ============================================================

CREATE TABLE IF NOT EXISTS pricing_plans (
    plan_id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    plan_code VARCHAR(50) NOT NULL UNIQUE,
    plan_name VARCHAR(100) NOT NULL,
    description TEXT,
    monthly_price INT NOT NULL DEFAULT 0,
    yearly_price INT NOT NULL DEFAULT 0,
    price_display VARCHAR(50),
    max_users INT NOT NULL DEFAULT 0,
    max_devices INT NOT NULL DEFAULT 0,
    max_pc_devices INT NOT NULL DEFAULT 0,
    max_mobile_devices INT NOT NULL DEFAULT 0,
    ai_token_monthly INT NOT NULL DEFAULT 0,
    features_json JSON,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_visible BOOLEAN NOT NULL DEFAULT TRUE,
    display_order INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_active_visible (is_active, is_visible),
    INDEX idx_display_order (display_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS pricing_history (
    history_id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    plan_id INT UNSIGNED NOT NULL,
    plan_code VARCHAR(50) NOT NULL,
    field_name VARCHAR(100) NOT NULL,
    old_value TEXT,
    new_value TEXT,
    changed_by VARCHAR(100),
    changed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_plan_id (plan_id),
    INDEX idx_changed_at (changed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================
-- 기본 영역 시드 (사장님 결재 — 29k / 49k / 100k)
-- ============================================================

INSERT INTO pricing_plans
    (plan_code, plan_name, description, monthly_price, yearly_price, price_display,
     max_users, max_devices, max_pc_devices, max_mobile_devices, ai_token_monthly,
     is_active, is_visible, display_order)
VALUES
    ('basic', '베이직', '소규모 사업자 영역 — 기본 ERP 가도',
     29000, 290000, '₩29,000',
     3, 2, 2, 0, 100000,
     TRUE, TRUE, 1),
    ('pro', '프로', '중소기업 영역 — 모바일 + AI 챗봇 가도',
     49000, 490000, '₩49,000',
     10, 5, 3, 2, 500000,
     TRUE, TRUE, 2),
    ('enterprise', '엔터프라이즈', '대형 사업자 영역 — 무제한 + 전담 CS',
     100000, 1000000, '₩100,000',
     30, 20, 10, 10, 3000000,
     TRUE, TRUE, 3)
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
    updated_at = CURRENT_TIMESTAMP;

-- ============================================================
-- 검증 영역
-- ============================================================

SELECT plan_code, plan_name, monthly_price, max_users, display_order, is_active, is_visible
FROM pricing_plans
ORDER BY display_order;
