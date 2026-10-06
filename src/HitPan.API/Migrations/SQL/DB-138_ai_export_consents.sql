-- =============================================================
-- DB-138: ai_export_consents 신설 — 외부 AI 반출 게이트의 동의 기록 (시드 0건)
-- =============================================================
-- 근거: 작업지시서 docs/운영기록/20261006작9_외부AI반출_봉합_P0트랙_작업지시서.md §4-3 (PM 결재 §8-3)
--       선행검증   docs/검증/선행/20261006_선행검증서_작9_외부AI반출_전수실측.md
--       사장님 결재 Q-7: "법무 계약·고지 체계 갖추기 전 외부 AI 반출 노출 금지"
--
-- ─────────────────────────────────────────────────────────────
-- 무엇에 쓰나
-- ─────────────────────────────────────────────────────────────
--   서버 단일 게이트 IExternalAiGate.IsOpenAsync(tenantId):
--     이 표에 해당 테넌트의 동의 기록이 **존재**할 때만 외부 AI 호출(챗봇 폴백·AI직원 엔진)이 열린다.
--     표 부재·조회 예외·기록 0건 = 전부 닫힘(fail-closed).
--   🔴 **시드 0건** — 지금은 동의 절차 자체가 없으므로 전 테넌트 닫힘(전면 차단)이 맞다.
--   🔴 **INSERT ONLY 운용** — 동의 기록은 증거다. UPDATE/DELETE 하지 않는다(#3 정신).
--   동의를 쌓는 화면·API 는 이번 봉합 범위 밖(작지 §4-3) — 재개는 작지 §7(법무+사장님 결재) 후.
--   스키마는 최소: 법무 약관 체계가 확정되면 동의 화면·버전 체계가 이 표를 그대로 쓴다.
--
-- 🚫 건드리지 않는 것: 다른 표 전부 · 기존 행 0건 수정(#1) · 삭제 0건
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB)
--          신규 설치 단일 진실원 installer/hitpan_db_clean.sql 에 함께 편입(#36 — DB-126 누락 사고 재발 금지):
--          표 정의 + `schema_migrations` 시드 ('DB-138','clean-ddl',1).
-- #13: 신설 표라 기존 모양 실측 대상 없음. 인접 표는 출하 DDL DESCRIBE 실측(2026-10-06):
--      ai_usage_logs.charge_mode varchar(20) · local_subscription.ai_provider varchar(20) ·
--      tenant_id 는 varchar(36)/char(36) 혼재 — 이 표는 varchar(36)(ai_usage_logs 와 동일 계열).
-- #17: ENGINE=InnoDB 명시.
-- 멱등: CREATE TABLE IF NOT EXISTS — 다시 돌려도 결과가 같다.
-- =============================================================

CREATE TABLE IF NOT EXISTS ai_export_consents (
  id BIGINT NOT NULL AUTO_INCREMENT,
  tenant_id VARCHAR(36) NOT NULL COMMENT '동의 주체 테넌트(서버 게이트가 JWT 유래 tenant_id 로 조회 · 헌법 #2)',
  terms_version VARCHAR(50) NOT NULL COMMENT '동의한 이용 안내(약관) 버전 — 법무 체계 확정 시 그 버전 체계를 그대로 쓴다',
  agreed_by VARCHAR(64) NOT NULL COMMENT '동의한 사용자 식별자(대표 계정)',
  agreed_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '동의 시각',
  agreed_ip VARCHAR(45) NOT NULL DEFAULT '' COMMENT '동의 당시 접속 IP(IPv6 수용 45자)',
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '적재 시각',
  PRIMARY KEY (id),
  KEY idx_ai_export_consents_tenant (tenant_id, agreed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='외부 AI 반출 동의 기록 — 존재=게이트 열림 · 0건=닫힘(fail-closed) · INSERT ONLY · 시드 0건 (DB-138 · 20261006작9)';
