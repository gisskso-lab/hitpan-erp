-- ═══════════════════════════════════════════════════════════════
-- 히트판 ERP 대규모 샘플 데이터 (200+200+20+100+100)
-- 실행: mysql -u hitpan -p'Hitpan2025!' hitpan_erp < sample-data-full.sql
-- ═══════════════════════════════════════════════════════════════

SET @tid = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';
SET @now = NOW(6);

-- ═══ 창고 3개 ═══
INSERT IGNORE INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, location, is_active, created_at, updated_at)
VALUES
('wh-main', @tid, 'WH-MAIN', '본사창고', 'normal', '서울시 강남구', 1, @now, @now),
('wh-sub1', @tid, 'WH-SUB1', '제2창고', 'normal', '경기도 안양시', 1, @now, @now),
('wh-sub2', @tid, 'WH-SUB2', '제3창고', 'normal', '인천시 남동구', 1, @now, @now);

-- ═══ 사원 10명 ═══
INSERT INTO employees (employee_id, tenant_id, user_id, emp_no, emp_name, dept_id, position, job_title, emp_type, join_date, phone, email, is_active, created_at, updated_at, role)
VALUES
('emp-001', @tid, NULL, 'E001', '김대표', NULL, '대표이사', '대표', 'regular', '2020-01-01', '010-1111-0001', 'ceo@test.com', 1, @now, @now, 'TenantAdmin'),
('emp-002', @tid, NULL, 'E002', '이부장', NULL, '부장', '영업부장', 'regular', '2020-03-01', '010-1111-0002', 'lee@test.com', 1, @now, @now, 'sales_manager'),
('emp-003', @tid, NULL, 'E003', '박과장', NULL, '과장', '구매과장', 'regular', '2021-01-15', '010-1111-0003', 'park@test.com', 1, @now, @now, 'sales_user'),
('emp-004', @tid, NULL, 'E004', '최대리', NULL, '대리', '영업대리', 'regular', '2022-06-01', '010-1111-0004', 'choi@test.com', 1, @now, @now, 'sales_user'),
('emp-005', @tid, NULL, 'E005', '정사원', NULL, '사원', '영업사원', 'regular', '2023-03-01', '010-1111-0005', 'jung@test.com', 1, @now, @now, 'sales_user'),
('emp-006', @tid, NULL, 'E006', '한팀장', NULL, '팀장', '경리팀장', 'regular', '2020-07-01', '010-1111-0006', 'han@test.com', 1, @now, @now, 'sales_user'),
('emp-007', @tid, NULL, 'E007', '오주임', NULL, '주임', '창고주임', 'regular', '2022-09-01', '010-1111-0007', 'oh@test.com', 1, @now, @now, 'sales_user'),
('emp-008', @tid, NULL, 'E008', '유인턴', NULL, '인턴', '영업인턴', 'contract', '2025-01-01', '010-1111-0008', 'yoo@test.com', 1, @now, @now, 'sales_user'),
('emp-009', @tid, NULL, 'E009', '강과장', NULL, '과장', 'IT과장', 'regular', '2021-04-01', '010-1111-0009', 'kang@test.com', 1, @now, @now, 'sales_user'),
('emp-010', @tid, NULL, 'E010', '조차장', NULL, '차장', '생산차장', 'regular', '2019-11-01', '010-1111-0010', 'jo@test.com', 1, @now, @now, 'sales_user');
