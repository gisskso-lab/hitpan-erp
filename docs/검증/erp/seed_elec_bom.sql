SET @elec_tenant = 'tenant-elec0-b000-bbbb-bbbbbbbbbbbb';

-- 18 완제품 BOM 헤더
INSERT INTO bom_headers (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active, memo, created_at, updated_at)
SELECT CONCAT('bh-elec0-', item_code, '-bbbbbbbbbbbbbbbb'), @elec_tenant, item_id, CONCAT(item_name, ' BOM'), 1, 1, 1, 'elec BOM seed', NOW(6), NOW(6)
FROM items WHERE tenant_id=@elec_tenant AND item_type='product';

-- BOM items: 각 완제품 = 반제품 1개 + 원자재 3~4개
-- 공통 부품: 반제품 SMT PCB + 수동소자 + 케이스 + 하네스
-- 파라미터화 — product seq (1~18) → 반제품/재료 매핑
-- P001 온습도: ESP001 + EM007 + EM009 + EM018 (케이스 소형) + EM016
INSERT INTO bom_items (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate, memo) VALUES
-- P001 온습도 센서 모듈
('bi-elec0-p001-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP001-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0009-bbbbbbbbbbbbb', 1, 'EA', 2, '온습 센서모듈'),
('bi-elec0-p001-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP001-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0011-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스 소형 반제품'),
('bi-elec0-p001-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP001-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-mat-0016-bbbbbbbbbbbbbb', 2, 'EA', 2, '하네스'),
('bi-elec0-p001-04-bbbbbbbbbbbbbbbb', 'bh-elec0-EP001-bbbbbbbbbbbbbbbb', @elec_tenant, 4, 'ie-elec0-mat-0015-bbbbbbbbbbbbbb', 1, 'EA', 1, '커넥터'),
-- P002 모션 센서
('bi-elec0-p002-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP002-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0001-bbbbbbbbbbbbb', 1, 'EA', 2, '센서 PCB'),
('bi-elec0-p002-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP002-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0011-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스'),
('bi-elec0-p002-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP002-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-mat-0016-bbbbbbbbbbbbbb', 3, 'EA', 2, '하네스'),
('bi-elec0-p002-04-bbbbbbbbbbbbbbbb', 'bh-elec0-EP002-bbbbbbbbbbbbbbbb', @elec_tenant, 4, 'ie-elec0-mat-0005-bbbbbbbbbbbbbb', 1, 'EA', 1, '레귤레이터'),
-- P003 전원공급 어댑터
('bi-elec0-p003-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP003-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0006-bbbbbbbbbbbbb', 1, 'EA', 2, '전원 반제품'),
('bi-elec0-p003-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP003-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0012-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스 대형'),
('bi-elec0-p003-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP003-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-mat-0020-bbbbbbbbbbbbbb', 1, 'EA', 1, '방열판'),
-- P004 제어기 표준형
('bi-elec0-p004-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP004-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0007-bbbbbbbbbbbbb', 1, 'EA', 2, '제어기판 A'),
('bi-elec0-p004-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP004-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0012-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스'),
('bi-elec0-p004-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP004-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-mat-0017-bbbbbbbbbbbbbb', 3, 'EA', 2, '하네스'),
('bi-elec0-p004-04-bbbbbbbbbbbbbbbb', 'bh-elec0-EP004-bbbbbbbbbbbbbbbb', @elec_tenant, 4, 'ie-elec0-mat-0020-bbbbbbbbbbbbbb', 1, 'EA', 1, '방열판'),
-- P005 제어기 통신형
('bi-elec0-p005-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP005-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0008-bbbbbbbbbbbbb', 1, 'EA', 2, '제어기판 B'),
('bi-elec0-p005-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP005-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0012-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스'),
('bi-elec0-p005-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP005-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-mat-0017-bbbbbbbbbbbbbb', 3, 'EA', 2, '하네스'),
('bi-elec0-p005-04-bbbbbbbbbbbbbbbb', 'bh-elec0-EP005-bbbbbbbbbbbbbbbb', @elec_tenant, 4, 'ie-elec0-mat-0006-bbbbbbbbbbbbbb', 1, 'EA', 1, '센서'),
-- P006 LED 디스플레이
('bi-elec0-p006-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP006-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0010-bbbbbbbbbbbbb', 1, 'EA', 2, '디스플레이 모듈'),
('bi-elec0-p006-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP006-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0011-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스'),
('bi-elec0-p006-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP006-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-mat-0015-bbbbbbbbbbbbbb', 2, 'EA', 1, '커넥터'),
-- P007 센서 허브 소형
('bi-elec0-p007-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP007-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0009-bbbbbbbbbbbbb', 2, 'EA', 2, '센서 모듈 2'),
('bi-elec0-p007-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP007-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0007-bbbbbbbbbbbbb', 1, 'EA', 2, '제어기판'),
('bi-elec0-p007-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP007-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-semi-0012-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스'),
-- P008 센서 허브 대형
('bi-elec0-p008-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP008-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0009-bbbbbbbbbbbbb', 3, 'EA', 2, '센서 모듈 3'),
('bi-elec0-p008-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP008-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0008-bbbbbbbbbbbbb', 1, 'EA', 2, '통신 제어기판'),
('bi-elec0-p008-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP008-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-semi-0012-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스 대형'),
-- P009 모바일 충전기 5W
('bi-elec0-p009-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP009-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0002-bbbbbbbbbbbbb', 1, 'EA', 2, '전원 PCB'),
('bi-elec0-p009-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP009-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0011-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스 소형'),
-- P010 모바일 충전기 20W
('bi-elec0-p010-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP010-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0006-bbbbbbbbbbbbb', 1, 'EA', 2, '전원공급 반제품'),
('bi-elec0-p010-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP010-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0011-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스'),
('bi-elec0-p010-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP010-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-mat-0020-bbbbbbbbbbbbbb', 1, 'EA', 1, '방열판'),
-- P011 하네스 A
('bi-elec0-p011-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP011-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0004-bbbbbbbbbbbbb', 1, 'EA', 1, '하네스 조립 A'),
-- P012 하네스 B
('bi-elec0-p012-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP012-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0005-bbbbbbbbbbbbb', 1, 'EA', 1, '하네스 조립 B'),
-- P013 센서 베어기판
('bi-elec0-p013-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP013-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0001-bbbbbbbbbbbbb', 1, 'EA', 2, 'SMT PCB 센서'),
-- P014 IoT 게이트웨이
('bi-elec0-p014-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP014-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0008-bbbbbbbbbbbbb', 1, 'EA', 2, '통신 제어기판'),
('bi-elec0-p014-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP014-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0010-bbbbbbbbbbbbb', 1, 'EA', 2, '디스플레이'),
('bi-elec0-p014-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP014-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-semi-0012-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스 대형'),
-- P015 스마트 스위치
('bi-elec0-p015-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP015-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0003-bbbbbbbbbbbbb', 1, 'EA', 2, 'MCU 제어'),
('bi-elec0-p015-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP015-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0011-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스'),
-- P016 스마트 센서 패키지
('bi-elec0-p016-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP016-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0009-bbbbbbbbbbbbb', 1, 'EA', 2, '온습 센서'),
('bi-elec0-p016-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP016-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0003-bbbbbbbbbbbbb', 1, 'EA', 2, 'MCU 제어'),
('bi-elec0-p016-03-bbbbbbbbbbbbbbbb', 'bh-elec0-EP016-bbbbbbbbbbbbbbbb', @elec_tenant, 3, 'ie-elec0-semi-0011-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스'),
-- P017 OEM 커스텀 A
('bi-elec0-p017-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP017-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0007-bbbbbbbbbbbbb', 1, 'EA', 2, '제어기판'),
('bi-elec0-p017-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP017-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0012-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스'),
-- P018 OEM 커스텀 B
('bi-elec0-p018-01-bbbbbbbbbbbbbbbb', 'bh-elec0-EP018-bbbbbbbbbbbbbbbb', @elec_tenant, 1, 'ie-elec0-semi-0008-bbbbbbbbbbbbb', 1, 'EA', 2, '통신 제어기판'),
('bi-elec0-p018-02-bbbbbbbbbbbbbbbb', 'bh-elec0-EP018-bbbbbbbbbbbbbbbb', @elec_tenant, 2, 'ie-elec0-semi-0012-bbbbbbbbbbbbb', 1, 'EA', 1, '케이스');

SELECT
  (SELECT COUNT(*) FROM bom_headers WHERE tenant_id=@elec_tenant) headers,
  (SELECT COUNT(*) FROM bom_items WHERE tenant_id=@elec_tenant) items_cnt;
