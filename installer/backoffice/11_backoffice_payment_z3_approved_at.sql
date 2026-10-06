-- ============================================================================
-- 히트판 백오피스(hitpan_backoffice) — 작8 갈래 나(Z3) 결제·정산 상태 통일
-- ============================================================================
-- 작성: 개발팀 (작업지시서 20261006작8 §1 Z3 · PM 결재 2026-10-06)
-- 근거: 사장님 Q-2 결재 — 결제 상태 통일값 = 'confirmed' (보고1 20261006)
--       선행검증 B-3-4 — approved_at 이 varchar(32) 토스 원문이라 날짜 변환 실패 위험
--
-- ★ #13 DESCRIBE 실측 (2026-10-06 · 시험 DB hitpan_backoffice):
--   approved_at 은 varchar(32) NULL — 토스 approvedAt 원문("2026-10-15T10:00:00+09:00")
--   이 그대로 들어간다. 이 varchar 칸은 **삭제하지 않고 병행 보존**(#1·#37).
--
-- ★ 하는 일 3가지 (전부 추가·멱등 — SchemaMigrator 가 파일 해시로 1회 적용,
--   재실행돼도 IF NOT EXISTS / WHERE 조건으로 안전):
--   1) approved_at_dt DATETIME 신설 — 월 정산 집계의 날짜 축 (varchar 병행)
--   2) status 'approved' → 'confirmed' 1회 backfill (Q-2 통일값)
--      · tenant_payments 는 결제 메타 — stock_ledger/journal_lines 원장 아님(#3 무관)
--      · 2026-10-06 시험 DB 실측: 기존 행 0건(hitpan_backoffice·hitpan_trgtest 모두
--        tenant_payments 0행) — 운영(NCP)은 #39 로 직접 접근하지 않고, 이 파일이
--        다음 기동 때 SchemaMigrator 경로로 1회 적용된다.
--   3) approved_at_dt backfill — varchar 가 ISO8601 모양일 때만 앞 19자를 파싱
--      (시각대 +09:00 꼬리는 버린다 — 기존 varchar 판독과 같은 의미, 토스 원문 시각)
-- ============================================================================

ALTER TABLE tenant_payments
    ADD COLUMN IF NOT EXISTS approved_at_dt datetime NULL DEFAULT NULL
        COMMENT '승인 시각 DATETIME (작8 Z3 — varchar approved_at 병행 보존)'
        AFTER approved_at;

-- Q-2 통일값 backfill: 'approved' 로 남은 행을 'confirmed' 로 1회 정렬 (재실행 = 0행, 멱등)
UPDATE tenant_payments
   SET status = 'confirmed'
 WHERE status = 'approved';

-- approved_at_dt backfill: ISO8601 모양의 varchar 만 파싱 (그 외는 NULL 유지 — 자동 추정 금지)
UPDATE tenant_payments
   SET approved_at_dt = STR_TO_DATE(SUBSTRING(approved_at, 1, 19), '%Y-%m-%dT%H:%i:%s')
 WHERE approved_at_dt IS NULL
   AND approved_at IS NOT NULL
   AND approved_at REGEXP '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}';
