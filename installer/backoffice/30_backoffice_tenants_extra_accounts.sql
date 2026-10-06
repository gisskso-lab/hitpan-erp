-- ============================================================================
-- 30_backoffice_tenants_extra_accounts.sql — 계정 과금 어휘 전환 Z5 (20261006작8 갈래 다)
--
-- 왜: 웹훅 송신(WebhookOutboundService)이 payload 에 ExtraAccounts 를 실어야 하는데
--     백오피스 tenants 표에 그 칸이 없었다(00_backoffice_core.sql:104-144 실측 — #13).
--     ERP 수신 칸(local_subscription.extra_accounts · DB-136)은 이미 준비됨 — 수신 무접촉(#37).
--
-- 규칙:
--   #1  — 추가만. 기존 칸(extra_device_slots 포함) 제거·변경 0.
--   #37 — extra_device_slots 는 폐기 아님 · 병행 보존(옛 판 ERP 가 읽는다).
--   #17 — 새 표 없음(ALTER 만) — 해당 없음.
--   멱등 — SchemaMigrator 가 파일 해시로 1회 적용하지만, IF NOT EXISTS 로 재실행도 안전.
--   ⬛ 파일 번호 30 — 기존 00·10·20·90·91 과 안 겹침(겹치면 뒤 파일 skip 사고).
--   🔧 번호 주장 정정 2026-10-07 ([4] 작업리뷰서 §5-1): 위 한 줄은 갈래 다 단독 기준이라
--      합류 후 거짓이 된다. 같은 사이클에 10→11(갈래 나)·30(이 파일)·31(갈래 가 · 개명분)이 함께 들어온다.
--      실측 2026-10-07 적용 순서: 00→10→11→20→30→31→90→91 — 이 파일 번호 30 은 **겹치지 않는다**.
--      (SchemaMigrator 는 번호를 파싱하지 않는다 — 파일명 전체 Ordinal 정렬 + file_name PK 멱등.
--       즉 번호 겹침이 skip 을 부르지는 않지만, 작업지시서 §6 이 금지하므로 가 갈래를 31 로 돌렸다.)
-- ============================================================================

ALTER TABLE tenants
    ADD COLUMN IF NOT EXISTS extra_accounts int(11) NOT NULL DEFAULT 0
        COMMENT '추가 구매 계정 수(계정 과금 · 20261006작8 Z5) — 웹훅 ExtraAccounts 로 ERP local_subscription.extra_accounts 에 간다'
        AFTER extra_device_slots;
