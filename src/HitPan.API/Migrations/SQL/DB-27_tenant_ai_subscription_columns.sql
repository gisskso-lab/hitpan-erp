-- DB-27: tenants 테이블에 AI 토큰·구독 티어·기기 슬롯 컬럼 추가
-- 진범: ChatbotService.GetQuotaAsync, TenantDeviceService.GetQuotaAsync 가
--       SQL에서 참조하는 컬럼이 마이그레이션 누락으로 부재
-- 영향: /api/chatbot/quota, /api/tenants/devices, /api/tenants/devices/quota → 500 / 401
-- 헌법: 워크플로우 미영향 (메타·과금 컬럼만), 절대원칙 #18 헌법 #18 미저촉
-- 작성: WO-20260430-2 / 사장님 결재 (2026-04-30)
-- 메모리 정책: project_ai_cost_model.md (AI 토큰 풀/BYOK/혼합 + 티어별 100K/500K/3M)

-- ────────────────────────────────────────────────────
-- ① AI 모드 + 토큰 한도
-- ────────────────────────────────────────────────────
ALTER TABLE tenants
  ADD COLUMN ai_mode VARCHAR(20) NOT NULL DEFAULT 'hitpan_pool'
    COMMENT 'hitpan_pool / byok / hybrid'
    AFTER subsidiary_no,
  ADD COLUMN ai_token_monthly_limit INT NOT NULL DEFAULT 100000
    COMMENT '월 토큰 한도 (티어 기본값: basic 100K, pro 500K, enterprise 3M)'
    AFTER ai_mode,
  ADD COLUMN ai_token_extra INT NOT NULL DEFAULT 0
    COMMENT '추가 구매 토큰'
    AFTER ai_token_monthly_limit,
  ADD COLUMN anthropic_api_key_encrypted VARCHAR(512) NULL
    COMMENT 'BYOK 모드에서 고객사 Anthropic 키 (AES-256 암호화)'
    AFTER ai_token_extra,
  ADD COLUMN anthropic_api_key_last4 VARCHAR(8) NULL
    COMMENT 'BYOK 키 마지막 4자리 (UI 표시용)'
    AFTER anthropic_api_key_encrypted,
  ADD COLUMN anthropic_key_status VARCHAR(20) NOT NULL DEFAULT 'none'
    COMMENT 'none / valid / invalid / expired'
    AFTER anthropic_api_key_last4;

-- ────────────────────────────────────────────────────
-- ② 구독 티어 (tenant 단위 캐시 — subscriptions 테이블의 plan_type 미러)
-- ────────────────────────────────────────────────────
ALTER TABLE tenants
  ADD COLUMN subscription_tier VARCHAR(20) NOT NULL DEFAULT 'basic'
    COMMENT 'basic / pro / enterprise (subscriptions.plan_type 미러)'
    AFTER anthropic_key_status;

-- ────────────────────────────────────────────────────
-- ③ 기기 추가 슬롯 (TenantDeviceService 참조)
-- ────────────────────────────────────────────────────
ALTER TABLE tenants
  ADD COLUMN extra_device_slots INT NOT NULL DEFAULT 0
    COMMENT '추가 구매 디바이스 슬롯 (1슬롯 = PC 1 또는 모바일 2)'
    AFTER subscription_tier;

-- ────────────────────────────────────────────────────
-- 본사 tenant 기본값 적용 (테스트 마스터)
-- ────────────────────────────────────────────────────
UPDATE tenants
   SET ai_mode = 'hitpan_pool',
       ai_token_monthly_limit = 100000,
       subscription_tier = 'basic',
       updated_at = NOW(6)
 WHERE tenant_id = '452ca266-97b9-4cd1-a0ac-2f37830c81f6';
