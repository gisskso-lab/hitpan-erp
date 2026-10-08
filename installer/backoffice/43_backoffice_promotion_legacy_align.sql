-- ════════════════════════════════════════════════════════════════════════
-- 43_backoffice_promotion_legacy_align.sql
-- 작업지시서 20261008작16 §A (설계 §3 ⓒ · PM 결재 D-1) — 프로모션 500 봉합
--
-- 왜 (선행검증 확정 1 · 설계 §1):
--   한 뜻의 표가 두 벌로 들어왔다 — promotions(admin 모양 · varchar PK) ·
--   promotions_legacy(legacy 모양 · bigint PK). PromotionController 는 legacy 칸 이름
--   (promo_code·title·use_count·target_plan·status)으로 **admin 모양 표**를 읽는 상태로 넉 달을 살았다.
--   운영에서 화면을 처음 열자 Unknown column 으로 500 이 났다.
--   ⇒ 컨트롤러를 promotions_legacy 로 돌린다(ⓒ). 이 파일은 그 표가 **있음을 보장**한다.
--
-- 무엇을 (PM 결재 D-1):
--   promotions_legacy · promotion_usages 를 00_backoffice_core.sql(21번 절)과
--   **한 글자도 같은 정의**로 CREATE TABLE IF NOT EXISTS 한다.
--   운영 promotions 표는 **한 글자도 안 건드린다** — ALTER 0건 · DROP 0건 · 형변경 0건(#1·#37).
--
-- 왜 운영 모양을 몰라도 맞나 (설계 §3-3):
--   운영 promotions 가 admin 모양이든 legacy 모양이든 제3 모양이든, 이 파일이 보장하는 것은
--   promotions_legacy 하나뿐이고 컨트롤러는 그걸 읽는다 ⇒ 어느 경우든 500 이 사라진다.
--   운영에 legacy 모양 행이 이미 있으면 IF NOT EXISTS 가 그 표를 그대로 둔다(데이터 보존).
--
-- 기동거부 위험 (설계 §5-2): 전 문장 IF NOT EXISTS · INSERT 0건 ·
--   신규 FK 는 promotion_usages 의 CREATE 안쪽 자기 참조 1개뿐(표가 같은 문장 묶음에서 함께 생긴다)
--   ⇒ 콜레이션(150)·고아FK(1452)·중복(1062) 경로가 구조적으로 없다. 2회 적재해도 2회 성공(G-MIG-1).
--
-- 헌법 정합:
--   #1  추가만 — 기존 표·칸 삭제·형변경 0건.
--   #3  promotion_usages 는 INSERT ONLY(컨트롤러 로직 무접촉).
--   #4  금액·할인값은 decimal.
--   #13 DESCRIBE 선행 — 정의 출처는 00_backoffice_core.sql:505-544 직독(2026-10-08).
--   #17 신규 표 ENGINE=InnoDB 명시.
--   #36 출하 DDL(00) 이 단일 진실원이고, 이 파일은 그 정의를 **그대로** 복제한다(다른 모양을 새로 만들지 않는다).
--   #39 운영 DB 직접 수술 0건 — 적용 경로는 배포 + 재기동 1회뿐.
--
-- ⚠️ 적재기 주의: `--` 주석 뒤에는 공백 1칸이 반드시 있어야 한다(없으면 그 파일부터 번호순 적재가 멈춘다).
-- ════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS promotions_legacy (
    promotion_id   bigint unsigned NOT NULL AUTO_INCREMENT,
    promo_code     varchar(40)   NOT NULL,
    title          varchar(200)  NOT NULL,
    discount_type  varchar(20)   NOT NULL DEFAULT 'percent',
    discount_value decimal(10,2) NOT NULL,
    starts_at      datetime(6)   NOT NULL,
    ends_at        datetime(6)   NOT NULL,
    max_uses       int(11)       NOT NULL DEFAULT 0,
    use_count      int(11)       NOT NULL DEFAULT 0,
    target_plan    varchar(40)   NULL DEFAULT NULL,
    status         varchar(20)   NOT NULL DEFAULT 'active',
    created_by     varchar(36)   NULL DEFAULT NULL,
    created_at     datetime(6)   NOT NULL DEFAULT current_timestamp(6),
    updated_at     datetime(6)   NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
    PRIMARY KEY (promotion_id),
    UNIQUE KEY uk_promo_code (promo_code),
    KEY ix_status_period (status, starts_at, ends_at),
    KEY ix_target_plan (target_plan)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='프로모션(legacy, PromotionController). 통합 별건 todo';

CREATE TABLE IF NOT EXISTS promotion_usages (
    usage_id       bigint unsigned NOT NULL AUTO_INCREMENT,
    promotion_id   bigint unsigned NOT NULL,
    promo_code     varchar(40)   NOT NULL,
    tenant_id      char(36)      NULL DEFAULT NULL,
    signup_token   varchar(64)   NULL DEFAULT NULL,
    applied_amount decimal(14,2) NOT NULL DEFAULT 0.00,
    applied_at     datetime(6)   NOT NULL DEFAULT current_timestamp(6),
    source         varchar(40)   NOT NULL DEFAULT 'landing_signup',
    PRIMARY KEY (usage_id),
    KEY ix_promotion (promotion_id),
    KEY ix_tenant (tenant_id),
    KEY ix_signup_token (signup_token),
    KEY ix_applied_at (applied_at),
    CONSTRAINT fk_usage_promo FOREIGN KEY (promotion_id)
        REFERENCES promotions_legacy (promotion_id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='프로모션 사용 이력';
