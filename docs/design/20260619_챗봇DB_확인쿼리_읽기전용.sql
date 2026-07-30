-- ============================================================
-- 히트판 AI 챗봇 — 실DB 확인 쿼리 (읽기 전용, 헌법 #13)
-- 작성: 2026-06-19 / DB매니저 검수
-- ⚠️ 전부 읽기 전용(DESCRIBE/SHOW/SELECT COUNT). 데이터 변경 0. 안전.
-- 실행: 사장님 영역(헌법 #29). 결과를 PM에 주시면 데이터설계서 확정.
-- 사용 DB: hitpan_erp (고객 ERP 로컬 DB)
-- ============================================================

-- [1] AI 설정 SoT 확인 — local_subscription 실컬럼 (ALTER 대상 확정용)
DESCRIBE local_subscription;

-- [2] 대화 테이블 실컬럼 (신규 테이블 참조 타입 정합용)
DESCRIBE ai_conversations;

-- [3] 사용량 테이블 실컬럼 (BYOK 사용량 화면 재료 확인)
DESCRIBE ai_usage_logs;

-- [4] 지식베이스 실컬럼
DESCRIBE hitpan_knowledge;

-- [5] tenants의 AI 관련 컬럼 — SoT 중복 여부 (local_subscription과 비교)
SHOW COLUMNS FROM tenants LIKE '%ai%';
SHOW COLUMNS FROM tenants LIKE '%anthropic%';

-- [6] 미생성 의심 테이블 실재 여부 (정적 덤프엔 없었음)
SHOW TABLES LIKE '%remote%';
SHOW TABLES LIKE '%denial%';
SHOW TABLES LIKE '%work_memory%';
SHOW TABLES LIKE '%intervention%';
SHOW TABLES LIKE '%handoff%';

-- [7] 실데이터 적재량 — 전면 재설계 안전 여부 확인 (베타 전 = 거의 0건 예상)
SELECT 'ai_conversations' AS tbl, COUNT(*) AS rows FROM ai_conversations
UNION ALL SELECT 'ai_usage_logs', COUNT(*) FROM ai_usage_logs
UNION ALL SELECT 'hitpan_knowledge', COUNT(*) FROM hitpan_knowledge
UNION ALL SELECT 'local_subscription', COUNT(*) FROM local_subscription;

-- [8] 키 상태 표준값 확인 — GetQuotaAsync 'verified' vs 'valid' 버그 검증
SELECT DISTINCT anthropic_key_status FROM local_subscription;
