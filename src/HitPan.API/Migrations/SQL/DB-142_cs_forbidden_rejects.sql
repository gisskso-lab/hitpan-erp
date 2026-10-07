-- =============================================================
-- DB-142: cs_forbidden_rejects 신설 — 금지필드로 막힌 기록 (작14 묶음 B-2 · INSERT ONLY)
-- =============================================================
-- 근거: 작업지시서 20261008작14 §3 B-2·B-4 · 설계 20261007_개발매니저피드백_CS_ERP연동_4인.md §3-1
--       CTO 조건③: 금지필드 값은 어느 문에도 저장하지 않는다
--
-- 무엇에 쓰나
--   금지필드 3문(①입력 안내 ②송신 전 차단 ③백오피스 수신 최종 판정) 중 ERP 쪽 문이 막은 기록.
--   🔴 **막힌 값 자체는 저장하지 않는다** — 저장하면 금지필드를 DB 에 적는 것이다. 규칙코드·시각만.
--   🔴 INSERT ONLY — 「게이트는 글자가 아니라 동작」의 증거가 이 표다(거부 로그 실측·음성 대조군).
--   오탐(예: 긴 전표번호가 번호 규칙에 잡힘)은 이 표를 세어 CS팀장이 규칙을 조정한다(반자동).
--
-- 대상 DB: hitpan_erp · 출하 DDL 같은 커밋(#36) · #17 InnoDB · 멱등 IF NOT EXISTS
-- =============================================================

CREATE TABLE IF NOT EXISTS cs_forbidden_rejects (
    reject_id    bigint      NOT NULL AUTO_INCREMENT,
    tenant_id    varchar(36) NOT NULL,
    rule_code    varchar(30) NOT NULL COMMENT 'resident_no/card_no/account_no/biz_no/tag_not_allowed — 값은 저장 안 함',
    occurred_at  datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (reject_id),
    KEY idx_cs_reject_tenant (tenant_id, occurred_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='금지필드 거부 기록 — 규칙코드·시각만 · INSERT ONLY(작14 B-4)';
