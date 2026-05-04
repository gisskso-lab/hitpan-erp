-- DB-38: 전체 테이블 InnoDB 강제 전환 (C-1 감사 이슈)
-- MyISAM은 트랜잭션·FK 미지원 → MonthlySummaryGuard 등 무력화
-- 신규 설치 후 또는 MyISAM 잔존 확인 후 1회 실행

SET foreign_key_checks = 0;

ALTER TABLE `audit_logs`                  ENGINE=InnoDB;
ALTER TABLE `bom_cost_cache`              ENGINE=InnoDB;
ALTER TABLE `common_codes`                ENGINE=InnoDB;
ALTER TABLE `departments`                 ENGINE=InnoDB;
ALTER TABLE `employees`                   ENGINE=InnoDB;
ALTER TABLE `items`                       ENGINE=InnoDB;
ALTER TABLE `item_stock`                  ENGINE=InnoDB;
ALTER TABLE `ledger_balance_snapshot`     ENGINE=InnoDB;
ALTER TABLE `login_attempts`              ENGINE=InnoDB;
ALTER TABLE `monthly_summary`             ENGINE=InnoDB;
ALTER TABLE `partners`                    ENGINE=InnoDB;
ALTER TABLE `partner_balance`             ENGINE=InnoDB;
ALTER TABLE `partner_special_prices`      ENGINE=InnoDB;
ALTER TABLE `payments`                    ENGINE=InnoDB;
ALTER TABLE `platforms`                   ENGINE=InnoDB;
ALTER TABLE `purchase_orders`             ENGINE=InnoDB;
ALTER TABLE `purchase_order_items`        ENGINE=InnoDB;
ALTER TABLE `purchase_receipts`           ENGINE=InnoDB;
ALTER TABLE `purchase_receipt_items`      ENGINE=InnoDB;
ALTER TABLE `quotations`                  ENGINE=InnoDB;
ALTER TABLE `quotation_items`             ENGINE=InnoDB;
ALTER TABLE `refresh_tokens`              ENGINE=InnoDB;
ALTER TABLE `resellers`                   ENGINE=InnoDB;
ALTER TABLE `reseller_commission_policies` ENGINE=InnoDB;
ALTER TABLE `reseller_payouts`            ENGINE=InnoDB;
ALTER TABLE `reseller_promotions`         ENGINE=InnoDB;
ALTER TABLE `reseller_revenues`           ENGINE=InnoDB;
ALTER TABLE `sales_deliveries`            ENGINE=InnoDB;
ALTER TABLE `sales_delivery_items`        ENGINE=InnoDB;
ALTER TABLE `sales_orders`               ENGINE=InnoDB;
ALTER TABLE `sales_order_items`           ENGINE=InnoDB;
ALTER TABLE `security_alerts`             ENGINE=InnoDB;
ALTER TABLE `status_history`              ENGINE=InnoDB;
ALTER TABLE `stock_ledger`                ENGINE=InnoDB;
ALTER TABLE `subscriptions`              ENGINE=InnoDB;
ALTER TABLE `users`                       ENGINE=InnoDB;
ALTER TABLE `user_sessions`               ENGINE=InnoDB;
ALTER TABLE `warehouses`                  ENGINE=InnoDB;
ALTER TABLE `workflow_settings`           ENGINE=InnoDB;
ALTER TABLE `__efmigrationshistory`       ENGINE=InnoDB;

SET foreign_key_checks = 1;

-- 검증
SELECT table_name, engine
FROM information_schema.tables
WHERE table_schema = DATABASE() AND engine != 'InnoDB'
ORDER BY table_name;
-- 결과가 0행이면 성공
