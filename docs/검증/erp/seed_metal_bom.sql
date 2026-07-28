SET @metal_tenant = 'tenant-metal-a000-aaaa-aaaaaaaaaaaa';

-- 15 완제품 BOM 헤더
INSERT INTO bom_headers (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active, memo, created_at, updated_at) VALUES
('bh-metal-p001-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0001-aaaaaaaaaaaaa', 'P001 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p002-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0002-aaaaaaaaaaaaa', 'P002 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p003-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0003-aaaaaaaaaaaaa', 'P003 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p004-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0004-aaaaaaaaaaaaa', 'P004 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p005-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0005-aaaaaaaaaaaaa', 'P005 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p006-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0006-aaaaaaaaaaaaa', 'P006 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p007-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0007-aaaaaaaaaaaaa', 'P007 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p008-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0008-aaaaaaaaaaaaa', 'P008 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p009-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0009-aaaaaaaaaaaaa', 'P009 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p010-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0010-aaaaaaaaaaaaa', 'P010 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p011-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0011-aaaaaaaaaaaaa', 'P011 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p012-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0012-aaaaaaaaaaaaa', 'P012 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p013-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0013-aaaaaaaaaaaaa', 'P013 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p014-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0014-aaaaaaaaaaaaa', 'P014 BOM', 1, 1, 1, '', NOW(6), NOW(6)),
('bh-metal-p015-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 'im-metal-fin0-0015-aaaaaaaaaaaaa', 'P015 BOM', 1, 1, 1, '', NOW(6), NOW(6));

-- bom_items: 완제품 × (반제품 + 원자재 2) × 15
-- P001 Drive Shaft 표준형: SP001 + M001 + M011
INSERT INTO bom_items (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate, memo) VALUES
('bi-metal-p001-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p001-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0001-aaaaaaaaaaaaa', 1, 'EA', 2, 'shaft 반제품'),
('bi-metal-p001-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p001-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0001-aaaaaaaaaaaaaa', 1.5, 'KG', 3, '원자재 SM45C Φ20'),
('bi-metal-p001-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p001-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0011-aaaaaaaaaaaaaa', 2, 'EA', 1, '볼트 M10'),
-- P002 Drive Shaft 강화형: SP002 + M002 + M011
('bi-metal-p002-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p002-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0002-aaaaaaaaaaaaa', 1, 'EA', 2, 'shaft 반제품 B'),
('bi-metal-p002-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p002-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0002-aaaaaaaaaaaaaa', 2, 'KG', 3, 'SM45C Φ30'),
('bi-metal-p002-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p002-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0011-aaaaaaaaaaaaaa', 2, 'EA', 1, '볼트'),
-- P003 서브프레임 브라켓 A: SP003 + M004 + M011
('bi-metal-p003-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p003-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0003-aaaaaaaaaaaaa', 1, 'EA', 3, '브라켓 반제품 A'),
('bi-metal-p003-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p003-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0004-aaaaaaaaaaaaaa', 1, 'KG', 5, 'S45C 각재'),
('bi-metal-p003-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p003-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0012-aaaaaaaaaaaaaa', 4, 'EA', 1, 'SUS 너트'),
-- P004
('bi-metal-p004-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p004-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0004-aaaaaaaaaaaaa', 1, 'EA', 3, '브라켓 B'),
('bi-metal-p004-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p004-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0004-aaaaaaaaaaaaaa', 1.5, 'KG', 5, 'S45C 각재'),
('bi-metal-p004-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p004-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0011-aaaaaaaaaaaaaa', 4, 'EA', 1, '볼트 M10'),
-- P005
('bi-metal-p005-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p005-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0005-aaaaaaaaaaaaa', 1, 'EA', 3, '하우징 반제품'),
('bi-metal-p005-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p005-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0008-aaaaaaaaaaaaaa', 3, 'KG', 4, 'SCM440'),
('bi-metal-p005-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p005-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0011-aaaaaaaaaaaaaa', 8, 'EA', 1, '볼트'),
-- P006
('bi-metal-p006-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p006-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0005-aaaaaaaaaaaaa', 1, 'EA', 3, '하우징 반제품'),
('bi-metal-p006-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p006-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0008-aaaaaaaaaaaaaa', 5, 'KG', 4, 'SCM440'),
('bi-metal-p006-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p006-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0011-aaaaaaaaaaaaaa', 10, 'EA', 1, '볼트'),
-- P007
('bi-metal-p007-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p007-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0006-aaaaaaaaaaaaa', 1, 'EA', 2, '플랜지 반제품'),
('bi-metal-p007-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p007-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0006-aaaaaaaaaaaaaa', 2, 'KG', 4, 'SUS304 1T'),
('bi-metal-p007-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p007-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0012-aaaaaaaaaaaaaa', 6, 'EA', 1, 'SUS 너트'),
-- P008
('bi-metal-p008-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p008-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0006-aaaaaaaaaaaaa', 1, 'EA', 2, '플랜지 반제품'),
('bi-metal-p008-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p008-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0007-aaaaaaaaaaaaaa', 3, 'KG', 4, 'SUS304 2T'),
('bi-metal-p008-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p008-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0012-aaaaaaaaaaaaaa', 8, 'EA', 1, 'SUS 너트'),
-- P009
('bi-metal-p009-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p009-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0007-aaaaaaaaaaaaa', 1, 'EA', 4, '기어 반제품'),
('bi-metal-p009-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p009-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0008-aaaaaaaaaaaaaa', 2, 'KG', 5, 'SCM440'),
('bi-metal-p009-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p009-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0011-aaaaaaaaaaaaaa', 4, 'EA', 1, '볼트'),
-- P010
('bi-metal-p010-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p010-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0007-aaaaaaaaaaaaa', 1, 'EA', 4, '기어 반제품'),
('bi-metal-p010-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p010-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0008-aaaaaaaaaaaaaa', 3, 'KG', 5, 'SCM440'),
('bi-metal-p010-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p010-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0011-aaaaaaaaaaaaaa', 6, 'EA', 1, '볼트'),
-- P011
('bi-metal-p011-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p011-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0008-aaaaaaaaaaaaa', 1, 'EA', 3, '프레임 반제품'),
('bi-metal-p011-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p011-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0009-aaaaaaaaaaaaaa', 5, 'KG', 4, 'STKM 파이프'),
('bi-metal-p011-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p011-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0011-aaaaaaaaaaaaaa', 12, 'EA', 1, '볼트'),
-- P012
('bi-metal-p012-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p012-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0008-aaaaaaaaaaaaa', 1, 'EA', 3, '프레임 반제품'),
('bi-metal-p012-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p012-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0009-aaaaaaaaaaaaaa', 10, 'KG', 4, 'STKM 파이프'),
('bi-metal-p012-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p012-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0011-aaaaaaaaaaaaaa', 20, 'EA', 1, '볼트'),
-- P013 커스텀 A
('bi-metal-p013-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p013-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0001-aaaaaaaaaaaaa', 1, 'EA', 2, '반제품'),
('bi-metal-p013-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p013-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0010-aaaaaaaaaaaaaa', 1, 'KG', 3, '알루미늄'),
('bi-metal-p013-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p013-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0011-aaaaaaaaaaaaaa', 4, 'EA', 1, '볼트'),
-- P014
('bi-metal-p014-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p014-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0002-aaaaaaaaaaaaa', 1, 'EA', 2, '반제품'),
('bi-metal-p014-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p014-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0010-aaaaaaaaaaaaaa', 2, 'KG', 3, '알루미늄'),
('bi-metal-p014-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p014-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0012-aaaaaaaaaaaaaa', 4, 'EA', 1, 'SUS 너트'),
-- P015 정밀 SUS
('bi-metal-p015-01-aaaaaaaaaaaaaaaaa', 'bh-metal-p015-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 1, 'im-metal-semi-0005-aaaaaaaaaaaaa', 1, 'EA', 2, '반제품'),
('bi-metal-p015-02-aaaaaaaaaaaaaaaaa', 'bh-metal-p015-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 2, 'im-metal-mat-0006-aaaaaaaaaaaaaa', 2, 'KG', 3, 'SUS304 1T'),
('bi-metal-p015-03-aaaaaaaaaaaaaaaaa', 'bh-metal-p015-aaaaaaaaaaaaaaaaaaaa', @metal_tenant, 3, 'im-metal-mat-0012-aaaaaaaaaaaaaa', 6, 'EA', 1, 'SUS 너트');

SELECT
  (SELECT COUNT(*) FROM bom_headers WHERE tenant_id=@metal_tenant) headers,
  (SELECT COUNT(*) FROM bom_items WHERE tenant_id=@metal_tenant) items_cnt;
