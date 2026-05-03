-- DB-28: tenant_settings 누락 컬럼 추가 + user_sessions.expires_at 추가
-- 적용일: 2026-05-03
-- 원인: 초기화 후 스키마 불일치 (코드 참조 컬럼이 DB에 없음)

ALTER TABLE tenant_settings
  ADD COLUMN IF NOT EXISTS stock_eval_method         VARCHAR(20)   NOT NULL DEFAULT 'fifo'    COMMENT '재고평가방법: fifo/lifo/avg',
  ADD COLUMN IF NOT EXISTS use_multi_warehouse       TINYINT(1)   NOT NULL DEFAULT 0          COMMENT '다중창고 사용여부',
  ADD COLUMN IF NOT EXISTS stock_shortage_alert      TINYINT(1)   NOT NULL DEFAULT 1          COMMENT '재고부족 알림',
  ADD COLUMN IF NOT EXISTS allow_minus_stock         TINYINT(1)   NOT NULL DEFAULT 0          COMMENT '마이너스 재고 허용',
  ADD COLUMN IF NOT EXISTS price_input_type          VARCHAR(10)  NOT NULL DEFAULT 'vat_in'   COMMENT '단가입력방식: vat_in/vat_out',
  ADD COLUMN IF NOT EXISTS auto_vat_adjust           TINYINT(1)   NOT NULL DEFAULT 1          COMMENT '부가세 자동계산',
  ADD COLUMN IF NOT EXISTS vat_round_type            VARCHAR(10)  NOT NULL DEFAULT 'round'    COMMENT '부가세 반올림방식: round/ceil/floor',
  ADD COLUMN IF NOT EXISTS price_a_rate              DECIMAL(5,2) NOT NULL DEFAULT 0.00       COMMENT '단가A 할인율',
  ADD COLUMN IF NOT EXISTS price_b_rate              DECIMAL(5,2) NOT NULL DEFAULT 0.00       COMMENT '단가B 할인율',
  ADD COLUMN IF NOT EXISTS price_c_rate              DECIMAL(5,2) NOT NULL DEFAULT 0.00       COMMENT '단가C 할인율',
  ADD COLUMN IF NOT EXISTS price_d_rate              DECIMAL(5,2) NOT NULL DEFAULT 0.00       COMMENT '단가D 할인율',
  ADD COLUMN IF NOT EXISTS price_e_rate              DECIMAL(5,2) NOT NULL DEFAULT 0.00       COMMENT '단가E 할인율',
  ADD COLUMN IF NOT EXISTS use_credit_limit          TINYINT(1)   NOT NULL DEFAULT 0          COMMENT '여신한도 사용여부',
  ADD COLUMN IF NOT EXISTS credit_limit_amount       DECIMAL(18,2) NOT NULL DEFAULT 0.00      COMMENT '기본 여신한도금액',
  ADD COLUMN IF NOT EXISTS show_purchase_price       TINYINT(1)   NOT NULL DEFAULT 0          COMMENT '매입단가 표시여부',
  ADD COLUMN IF NOT EXISTS use_sales_by_employee     TINYINT(1)   NOT NULL DEFAULT 0          COMMENT '담당자별 매출 집계',
  ADD COLUMN IF NOT EXISTS use_personal_info_protect TINYINT(1)   NOT NULL DEFAULT 0          COMMENT '개인정보보호모드',
  ADD COLUMN IF NOT EXISTS industry_type             VARCHAR(30)  NOT NULL DEFAULT 'general'  COMMENT '업종: general/food/elec/plastic/wood';

-- user_sessions.expires_at 추가 (SessionLimitMiddleware 참조)
ALTER TABLE user_sessions
  ADD COLUMN IF NOT EXISTS expires_at DATETIME(6) NULL COMMENT '세션 만료일시';

-- 기존 세션 만료 기본값 (login_at + 24h)
UPDATE user_sessions SET expires_at = DATE_ADD(login_at, INTERVAL 24 HOUR) WHERE expires_at IS NULL;
