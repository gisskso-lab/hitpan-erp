-- =============================================================
-- DB-75: 반품 전용 컬럼 박제 (사장님 작업지시 2026-05-31 항목 3)
-- - purchase_returns + sales_returns 공통 return_reason 박제
-- - 5종 표준 사유 + 자유 입력 허용 (text)
-- - 헌법 #1 정합 (ADD COLUMN IF NOT EXISTS 안전 박제)
-- =============================================================

ALTER TABLE purchase_returns
    ADD COLUMN IF NOT EXISTS return_reason VARCHAR(30) NULL DEFAULT NULL
        COMMENT '반품 사유 코드: defect(불량) / wrong_item(오배송) / over_qty(수량초과) / customer_cancel(고객취소) / etc(기타)',
    ADD COLUMN IF NOT EXISTS return_reason_memo VARCHAR(500) NULL DEFAULT NULL
        COMMENT '반품 사유 상세 (자유 입력)';

-- sales_returns 테이블 존재 시 동일 박제
SET @sales_returns_exists := (
    SELECT COUNT(*) FROM information_schema.tables
    WHERE table_schema = DATABASE() AND table_name = 'sales_returns'
);
SET @sql := IF(@sales_returns_exists > 0,
    'ALTER TABLE sales_returns
        ADD COLUMN IF NOT EXISTS return_reason VARCHAR(30) NULL DEFAULT NULL,
        ADD COLUMN IF NOT EXISTS return_reason_memo VARCHAR(500) NULL DEFAULT NULL',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 인덱스 (사유별 통계용)
CREATE INDEX IF NOT EXISTS idx_purchase_returns_reason
    ON purchase_returns (tenant_id, return_reason, return_date);
