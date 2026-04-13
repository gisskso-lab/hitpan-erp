-- MariaDB 11.4+: partner_balance / monthly_summary GENERATED STORED 복원
-- (로컬 DB에 적용 후 API와 동기화)

ALTER TABLE partner_balance
  MODIFY COLUMN balance
    DECIMAL(15,2) GENERATED ALWAYS AS
    (total_sales - total_receipt) STORED,
  MODIFY COLUMN payable
    DECIMAL(15,2) GENERATED ALWAYS AS
    (total_purchase - total_payment) STORED;

ALTER TABLE monthly_summary
  MODIFY COLUMN gross_profit
    DECIMAL(15,2) GENERATED ALWAYS AS
    (total_sales - total_purchase) STORED;
