-- ════════════════════════════════════════════════════════════════════════
-- 44_backoffice_pricing_plan_columns.sql
-- 작업지시서 20261008작16 §B (설계 §4 · PM 결재 D-2·D-4) — 요금제 500 봉합
--
-- 왜 (선행검증 확정 2 · 설계 §4-1):
--   PricingAdminController(요금제 화면) · LandingPublicController(랜딩 공개 요금표) ·
--   PromotionsAdminController · DeviceRegistrationController 네 곳이 pricing_plans 의
--   price_display · max_pc_devices · max_mobile_devices 를 읽는데, 그 세 칸의 정의가
--   6/11 커밋 82091fb7 에서 사라졌다.
--   🔴 그 커밋은 「세 칸을 폐기한다」는 결재가 아니다 — 지운 것은 **PK 모양이 틀린 CREATE** 였고
--   (커밋 메시지 그대로: "기존 스키마는 plan_id(VARCHAR PK) · CREATE TABLE 제거(이미 존재 보존)"),
--   같은 커밋이 그 세 칸을 **그대로 INSERT 하는 시드를 남겼다** ⇒ 작성자는 칸이 있다고 믿었다.
--   되살리면 안 되는 이유는 커밋 어디에도 없다.
--
-- 무엇을:
--   세 칸을 ADD COLUMN IF NOT EXISTS 로 더한다. 기존 행 UPDATE 0건 · FK 0개 · 인덱스 변경 0개.
--   운영에 세 칸이 이미 있으면(시드가 실행됐을 수도 있다 — 미확인) IF NOT EXISTS 가 그대로 둔다.
--
-- 🔴 기본값 함정 (설계 §4-2 · PM 결재 D-2) — 두 기기 칸에 0 을 주면 안 된다:
--   DeviceRegistrationController:65-66 은 COALESCE(p.max_pc_devices, 5) / COALESCE(…, 3) 이다.
--   COALESCE 는 **NULL 만** 막는다. 0 이면 폴백이 안 돌고 상한이 0 이 되어 **기기 등록이 전부 막힌다**.
--   ⇒ 두 칸은 NULL 허용 · 기본값 NULL. 기존 행은 NULL = 「미설정」 ⇒ 폴백 5/3 이 그대로 산다.
--   (G-PRICE-1 이 이 5/3 을 실제 SQL 실행으로 문다.)
--
-- 🔴 price_display 의 뜻 (설계 §2-6 · PM 결재 D-4) — **표시 모드**다, 금액 글자가 아니다:
--   코드가 쓰는 뜻은 'number' 또는 'contact' 다(PricingAdminController:254 주석 · PlanRow 기본값).
--   6/11 시드(scripts/pricing_plans_seed.sql)는 같은 칸에 **금액 글자**를 넣는다 — 뜻이 다르다.
--   기본값은 코드 쪽을 따른다('number'). 이 칸에 금액 글자를 쓰지 마라.
--
-- 🔴 슬롯 과금 부활이 아니다 (설계 §4-3):
--   9/25 결재로 기기 슬롯 과금은 폐기되고 과금 축은 계정 수다. 이 파일은 **코드가 읽는 칸을 있게만** 한다 —
--   값은 NULL(미설정)이고 과금·동시로그인 판단은 이 칸을 보지 않는다.
--   두 칸을 코드에서 빼는 쪽은 동작 변경이라 2차 별건(사장님 결재 ⑵).
--
-- 기동거부 위험 (설계 §5-2): 전 문장 IF NOT EXISTS · FK·인덱스 0개 · INSERT 0건
--   ⇒ errno 150·1452·1062 경로가 구조적으로 없다. 2회 적재해도 2회 성공(G-MIG-1).
--
-- 헌법 정합:
--   #1  추가만 — 기존 칸 삭제·형변경 0건 · 기존 행 UPDATE 0건.
--   #13 DESCRIBE 선행 — pricing_plans 정의는 00_backoffice_core.sql:400-418 직독(2026-10-08).
--   #17 신규 표 없음(기존 ALTER 만).
--   #36 같은 세 칸을 00_backoffice_core.sql 의 pricing_plans 정의에도 더했다(같은 커밋) —
--       신규 설치가 이 파일 없이도 맞게 선다.
--   #39 운영 DB 직접 수술 0건.
--
-- ⚠️ 적재기 주의: `--` 주석 뒤에는 공백 1칸이 반드시 있어야 한다(없으면 그 파일부터 번호순 적재가 멈춘다).
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE pricing_plans
    ADD COLUMN IF NOT EXISTS price_display varchar(50) NOT NULL DEFAULT 'number'
        COMMENT '가격 표시 모드 — number(금액 노출) 또는 contact(문의). 🔴 금액 글자를 넣는 칸이 아니다';

ALTER TABLE pricing_plans
    ADD COLUMN IF NOT EXISTS max_pc_devices int(11) NULL DEFAULT NULL
        COMMENT 'PC 기기 상한(미설정 = NULL). 🔴 0 금지 — COALESCE 폴백 5 가 안 돌아 등록이 전부 막힌다';

ALTER TABLE pricing_plans
    ADD COLUMN IF NOT EXISTS max_mobile_devices int(11) NULL DEFAULT NULL
        COMMENT '모바일 기기 상한(미설정 = NULL). 🔴 0 금지 — COALESCE 폴백 3 이 안 돌아 등록이 전부 막힌다';
