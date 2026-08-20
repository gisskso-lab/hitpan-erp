-- ============================================================================
-- DB-105: 팩스 전송 인프라 + 업체 좌표 (2026-08-21)
-- ----------------------------------------------------------------------------
-- 근거: 사장님 오더 2026-08-21 / 작업지시서 20260821작1
--       설계: docs/설계/erp/20260821_설계_팩스전송_지오코딩_자동발주멱등_기본창고.md
--
-- 1) 팩스 설정·발송이력 — 이메일(DB-26) 구조를 그대로 미러링. 새 발명 없음.
-- 2) partners 좌표 — 카카오맵·내비 딥링크가 좌표를 요구하는데 보관할 자리가 없었다.
--
-- §#2  tenant_id 우선          §#3  fax_send_history INSERT ONLY
-- §#5  API 키 AES256 암호화     §#17 InnoDB 명시
-- §#18 본사 미수신 — 팩스 계정은 고객사 본인 것만 사용 (본사 대리송출 금지)
-- §#36 출하 DDL(hitpan_db_clean.sql) 동반 반영 필수
-- ============================================================================

-- ─────────────────────────────────────────────────────────────────────────
-- 1) 팩스 설정 (테넌트당 1행)
--    공급자(provider)는 벤더 결재 후 확정. 미설정 시 'mock' 으로 동작하며
--    실제 송출되지 않음을 화면과 이력에 명시한다 (거짓봉합 방지 §#23).
-- ─────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS fax_settings (
    tenant_id           VARCHAR(36)  NOT NULL,
    provider            VARCHAR(30)  NOT NULL DEFAULT 'mock',      -- mock / {벤더코드}
    api_endpoint        VARCHAR(200) NULL,                          -- 벤더 REST 엔드포인트
    api_key_enc         VARBINARY(512) NULL,                        -- AES256 암호화 (§#5)
    api_secret_enc      VARBINARY(512) NULL,                        -- 벤더에 따라 미사용
    sender_fax_no       VARCHAR(20)  NULL,                          -- 발신 팩스번호 (사전등록 필요)
    sender_name         VARCHAR(60)  NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 0,            -- 기본 비활성 — 설정 완료 후 켠다
    last_test_at        DATETIME     NULL,
    last_test_result    VARCHAR(20)  NULL,                          -- success / failed
    last_test_error     TEXT         NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ─────────────────────────────────────────────────────────────────────────
-- 2) 팩스 발송 이력 (INSERT ONLY §#3)
--    status: mock   = 공급자 미설정. 실제 전송 안 됨 (화면에 경고 노출)
--            queued = 벤더 접수 대기 / sent = 접수 성공 / failed = 실패
--            delivered / undelivered = 벤더 콜백 수신 결과 (벤더 연동 후)
-- ─────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS fax_send_history (
    fax_id              VARCHAR(36)  NOT NULL,
    tenant_id           VARCHAR(36)  NOT NULL,
    sent_at             DATETIME     NOT NULL,
    sent_by_user        VARCHAR(36)  NULL,
    document_type       VARCHAR(20)  NOT NULL,                      -- quotation/sales_order/delivery/tax_invoice/purchase_order/purchase_receipt
    document_no         VARCHAR(40)  NOT NULL,
    document_id         VARCHAR(36)  NULL,                          -- 원본 문서 PK (soft 참조 — FK 아님)
    partner_id          VARCHAR(36)  NULL,
    recipient_fax_no    VARCHAR(20)  NOT NULL,
    recipient_name      VARCHAR(60)  NULL,
    page_count          INT          NULL,
    provider            VARCHAR(30)  NOT NULL DEFAULT 'mock',
    provider_job_id     VARCHAR(100) NULL,                          -- 벤더 발급 작업 ID (조회·콜백 대조용)
    status              VARCHAR(20)  NOT NULL,
    error_message       TEXT         NULL,
    provider_response   VARCHAR(500) NULL,
    PRIMARY KEY (fax_id),
    KEY ix_faxhist_tenant_date (tenant_id, sent_at DESC),
    KEY ix_faxhist_doc (tenant_id, document_type, document_no),
    KEY ix_faxhist_partner (tenant_id, partner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ─────────────────────────────────────────────────────────────────────────
-- 3) partners 좌표 — 카카오맵·카카오내비 딥링크용
--    카카오 URL 스킴은 좌표(lat,lng)를 요구한다. 종전엔 주소 문자열을 좌표 자리에
--    넣어 파싱이 실패했고, 지도가 해당 위치가 아닌 기본 위치로 열렸다.
--    (사장님 지적 2026-08-21: "맵이 뜨긴 하지만 실제 해당주소 좌표가 안찍힘")
--    NULL 허용 — 좌표를 못 구한 주소는 현행 주소 폴백이 그대로 받는다 (§#20).
--    DECIMAL(10,7): 소수 7자리 ≈ 1cm 정밀도. 위경도에 float 금지 (§#4 정신).
-- ─────────────────────────────────────────────────────────────────────────
-- IF NOT EXISTS: 마이그 재실행 안전 (DB-10 이래 동일 패턴 — MariaDB 지원)
ALTER TABLE partners
  ADD COLUMN IF NOT EXISTS latitude  DECIMAL(10,7) NULL COMMENT '위도 — 카카오맵/내비 딥링크용' AFTER address_detail,
  ADD COLUMN IF NOT EXISTS longitude DECIMAL(10,7) NULL COMMENT '경도 — 카카오맵/내비 딥링크용' AFTER latitude;

-- ─────────────────────────────────────────────────────────────────────────
-- 4) items 기본창고 (사장님 결재 2026-08-21 — A안)
--
--    사장님 지적: "기본창고 설정하면 발주,매입이 안되는거 같은데"
--    코드리뷰 결과: 상품마스터의 '기본 창고' 드롭다운은 **아무것도 저장하지 않았다.**
--      ItemDetail.razor:121 → `private string? _selectedWarehouseId; // 참조용 — DB에 저장하지 않음`
--      items 실측 40컬럼에 창고 컬럼이 아예 없었다. 저장 버그가 아니라 저장할 자리가 없었다.
--    발주·매입은 테넌트 단위 폴백(wh_code MAIN 우선)만 썼고 품목별 기본창고는 조회조차 안 했다.
--    ⇒ 설정해도 아무 변화가 없었다. 화면이 사용자에게 거짓말을 하고 있었다.
--
--    이 컬럼이 그 자리를 만든다. 발주·매입의 창고 결정 순서는:
--      1. 라인에 직접 지정한 창고
--      2. items.default_warehouse_id   ← 이 컬럼 (신규)
--      3. 테넌트 기본창고 (MAIN 우선)   ← 현행 폴백, 지우지 않는다 (§#1)
--      4. 없으면 명확한 오류            ← 유령창고 기록 원천차단, 현행 유지
--
--    FK 를 걸지 않는 이유: 창고가 비활성·삭제돼도 품목 저장이 막히면 안 된다.
--    유효하지 않은 창고는 3번 폴백이 받는다 (§#20 끊김 금지).
-- ─────────────────────────────────────────────────────────────────────────
ALTER TABLE items
  ADD COLUMN IF NOT EXISTS default_warehouse_id VARCHAR(36) NULL
      COMMENT '품목 기본창고 — 발주·매입 시 라인 미지정이면 이 창고를 쓴다' AFTER supplier_default_id;

CREATE INDEX IF NOT EXISTS idx_items_default_wh ON items (tenant_id, default_warehouse_id);

-- ─────────────────────────────────────────────────────────────────────────
-- 5) 좌표 변환(지오코딩) 설정 — 테넌트당 1행
--
--    카카오맵·내비 딥링크는 좌표를 요구하는데 우편번호 서비스는 좌표를 주지 않는다
--    (공식 문서 확인 — data 필드에 위경도 없음). 주소→좌표 변환에 REST 키가 필요하다.
--
--    🔴 왜 appsettings.json 이 아니라 DB 인가:
--      · 헌법 #21 — appsettings.json 수정 금지
--      · 헌법 #18·#22 — 키는 고객사 것이고 고객사 PC 에만 있어야 한다. 본사 경유 0.
--      · 팩스(fax_settings)와 동일한 취급 — 외부 서비스 자격증명은 테넌트별 암호화 보관.
--
--    미설정이면 좌표 변환을 시도하지 않고, 지도는 현행 주소 방식으로 열린다 (§#20 끊김 금지).
-- ─────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS geocoding_settings (
    tenant_id           VARCHAR(36)  NOT NULL,
    provider            VARCHAR(30)  NOT NULL DEFAULT 'kakao',
    api_key_enc         VARBINARY(512) NULL,                        -- AES256 암호화 (§#5)
    is_active           TINYINT(1)   NOT NULL DEFAULT 0,
    last_test_at        DATETIME     NULL,
    last_test_result    VARCHAR(20)  NULL,
    last_test_error     TEXT         NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
