-- ============================================================================
-- 히트판 ERP — AI 챗봇 BYOK + 장기 기억 마이그레이션
-- ============================================================================
-- 문서번호: 20260619 / 데이터설계서: docs/CS/20260619_AI챗봇_데이터설계서.md
-- 작성: PM 브라운킴 + DB 매니저 (사장님 결재 2026-06-19 "챗봇 테이블 전면 수정 OK")
--
-- 헌법 정합:
--   #5  — BYOK 키 암호화. anthropic_api_key_encrypted(실재)에 AES-256 저장(앱 계층 IEncryptionService).
--   #13 — 실DB DESCRIBE 완료(데이터설계서 §7). local_subscription·ai_usage_logs·ai_conversations 실재 확인.
--   #17 — 신규 테이블 ENGINE=InnoDB 명시.
--   #22 — 장기 기억은 고객 PC 로컬 전용. 본사 전송 0건.
--
-- 적용 대상 DB: hitpan_erp (고객사 PC 로컬)
-- 실행 주체: 설치 마법사 / 업데이트 워치독 (본 SQL은 형상관리 정의)
-- 멱등성: ALTER ... IF NOT EXISTS / CREATE TABLE IF NOT EXISTS — 재실행 안전.
-- ============================================================================

-- ────────────────────────────────────────────────────────────────────────
-- 1) local_subscription ALTER — BYOK 키 저장/연결확인 시각 컬럼 추가
--    출처: 데이터설계서 §3.1. anthropic_api_key_encrypted/last4/status는 이미 실재.
--    여기서는 "언제 저장/확인했나" 시각만 최소 추가.
-- ────────────────────────────────────────────────────────────────────────
ALTER TABLE local_subscription
    ADD COLUMN IF NOT EXISTS anthropic_key_saved_at    DATETIME NULL COMMENT 'BYOK 키 저장 시각',
    ADD COLUMN IF NOT EXISTS anthropic_key_verified_at DATETIME NULL COMMENT 'BYOK 키 연결확인 시각';

-- ────────────────────────────────────────────────────────────────────────
-- 2) ai_work_memory — AI 장기 기억 (데이터설계서 §2.1 구조 그대로)
--    profile=고객사 특성 / faq_pattern=자주 묻는 것 / resolution=과거 해결법 / preference=선호
--    파일 미러: %LOCALAPPDATA%\HitPan\AiMemory\{tenant_id}_worklog.md (본사 0건, 헌법 #22)
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS ai_work_memory (
    id           BIGINT       NOT NULL AUTO_INCREMENT,
    tenant_id    VARCHAR(50)  NOT NULL,
    memory_type  ENUM('profile','faq_pattern','resolution','preference') NOT NULL,
    summary      VARCHAR(1000) NOT NULL                COMMENT '압축 요약 (대화 통째 아님)',
    importance   INT          NOT NULL DEFAULT 50      COMMENT '0~100, 낮으면 정리(망각) 대상',
    ref_count    INT          NOT NULL DEFAULT 1       COMMENT '재참조 횟수(자주 쓰면 안 지움)',
    last_used_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_tenant_type (tenant_id, memory_type),
    INDEX idx_importance (importance DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI 장기 기억 (인수인계 파일)';
