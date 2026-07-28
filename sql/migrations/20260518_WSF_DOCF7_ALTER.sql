-- WS-F-D-2 ALTER SQL — 회계 분개 마이그 (DOCF7) 봉합용
-- 작성: 2026-05-18 PM 브라운킴
-- 헌법 #13 DESCRIBE 선행 완료, #3 INSERT ONLY 정합

-- 1. journal_entries: source_id 멱등 유니크 키 (UNIQUE 가드)
ALTER TABLE journal_entries
    ADD UNIQUE KEY uq_je_source (tenant_id, source_type, source_id);

-- 2. journal_lines: source_id 컬럼 신설 + 멱등 키
-- 라인 단위 멱등 (재마이그 시 중복 방지)
ALTER TABLE journal_lines
    ADD COLUMN source_id VARCHAR(80) NULL COMMENT 'WS-F: 라인 멱등 키 (entry source_id + SC_KCODE + SUN)',
    ADD UNIQUE KEY uq_jl_source (tenant_id, source_id);

-- 3. accounts: 마이그 자동 시드용 fallback 계정 신설
-- SC_KCODE 4자리 그대로 저장 (사장님 결재 Q5: ERP 매니저가 추후 매핑 UPDATE)
-- account_type fallback: 'unmapped' (ERP 매니저가 추후 정정)
-- 자동 시드 SQL은 별도 (마이그 진행 시 코드에서 INSERT IGNORE)

-- 검증:
-- SHOW INDEX FROM journal_entries WHERE Key_name = 'uq_je_source';
-- SHOW INDEX FROM journal_lines WHERE Key_name = 'uq_jl_source';
