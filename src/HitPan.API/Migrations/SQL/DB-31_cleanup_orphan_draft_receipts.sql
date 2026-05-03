-- DB-31: 자동사슬 반복 발주 버그로 생성된 고아 draft 매입명세서 정리
-- 원인: stock_alerts idempotency 미적용으로 같은 PO에서 receipt 중복 생성
-- 대상: status='draft' 이고 연결 PO가 이미 'received' 인 영수증 (확정 불가능한 데드 데이터)
-- 관련 커밋: 58ea15b (자동사슬 반복 발주 버그 수정)

-- 1단계: 고아 draft receipt 아이템 삭제
DELETE ri
FROM purchase_receipt_items ri
INNER JOIN purchase_receipts r ON ri.receipt_id = r.receipt_id
INNER JOIN purchase_orders p ON r.po_id = p.po_id
WHERE r.status = 'draft'
  AND p.status = 'received';

-- 2단계: 고아 draft receipt 헤더 삭제
DELETE r
FROM purchase_receipts r
INNER JOIN purchase_orders p ON r.po_id = p.po_id
WHERE r.status = 'draft'
  AND p.status = 'received';
