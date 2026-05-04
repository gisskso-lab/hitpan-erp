-- DB-35: journal_lines 중복 분개 차단
-- 동일 전표(entry_id)에 동일 계정(account_code)이 중복 INSERT되는 것을 DB 레벨에서 차단.
-- 자동 기표(AutoJournalHelper) 재실행 시 중복 분개 → 월결산 매출 2배 문제 원천 방지.
-- 적용 전 중복 데이터 없음 확인 완료 (2026-05-04).

ALTER TABLE journal_lines
  ADD UNIQUE KEY uk_journal_line_dedup (tenant_id, entry_id, account_code);
