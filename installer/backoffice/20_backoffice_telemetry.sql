-- ============================================================================
-- 히트판 백오피스(hitpan_backoffice) 자립형 통합 스키마 — telemetry 모듈
-- ============================================================================
-- 작성: 20260806작4 (사장님 오더 ③ "백오피스에 고객사 업데이트 기록")
--
-- ★ 왜 만드나 (사장님 2026-07-20 원 오더 §2 — CS 첫 도구):
--     "고객한테 '몇 버전 쓰세요?' 물으면 그 확인 자체가 CS 업무가 됨. 고객은 자기 버전 모름.
--      본사가 백오피스에서 확인·선제 대응이 맞다."
--   CS팀장 강지원 "MVP 에 꼭": (현재버전 + 마지막보고시각 + 업데이트 성공/실패) 세트.
--     "로컬 ERP는 '업데이트했다'가 '업데이트됐다'를 보장 안 한다. 실패 여부 없으면 CS가 눈 감고 일한다."
--
-- ★ 진범 (실측 2026-08-06): 워치독 송신부는 있는데 **백오피스 수신부가 0건**이었다.
--   커밋 149e604(ERP↔백오피스 분리)에서 WatchdogController 가 삭제됐고 이식되지 않았다.
--   그래서 워치독이 보내는 것이 3개월간 허공으로 갔다(back.hitpan.kr POST → 400).
--
-- ★ 헌법 #18·#22 경계 — 여기 들어오는 것은 '운영 메타데이터'뿐이다:
--     받는다: 버전·채널·성공/실패·시각
--     안 받는다: 거래처·상품·금액·직원명·DB 내용 등 업무 데이터 일체 (전송 자체가 금지)
--   라이선스 키는 **대조에만 쓰고 저장하지 않는다** (평문 0건).
--
-- ★ 헌법 #17: 모든 테이블 ENGINE=InnoDB 명시.
-- ★ 이 파일은 다른 DB(hitpan_erp)에 의존하지 않는다 (LIKE/REFERENCES 금지 — 00 파일 원칙).
-- ============================================================================

SET FOREIGN_KEY_CHECKS=0;

-- ────────────────────────────────────────────────────────────────────────
-- tenant_update_history — 업데이트 이력 (INSERT ONLY)
--
--   ★ INSERT ONLY (헌법 #3 정신): 이력은 고치지 않는다. 정정이 필요하면 새 행을 넣는다.
--     "언제 무엇이 어떻게 됐나"는 사후에 바뀌면 안 되는 기록이다.
--
--   tenant_id 가 NULL 일 수 있는 이유: 설치 직후 등록 지연 등으로 라이선스 대조는 됐으나
--   tenant 행이 아직 없는 순간이 있을 수 있다. 그때도 기록은 남긴다(버리면 CS 가 못 본다).
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tenant_update_history (
    history_id     bigint       NOT NULL AUTO_INCREMENT,
    tenant_id      varchar(36)  NULL DEFAULT NULL,
    tenant_id_hash varchar(80)  NOT NULL,
    from_version   varchar(20)  NULL DEFAULT NULL,
    to_version     varchar(20)  NULL DEFAULT NULL,
    channel        varchar(20)  NULL DEFAULT NULL,
    -- success = 적용 완료 / failed = 적용 실패·롤백 / rejected = 고객이 [나중에] 선택
    result         varchar(20)  NOT NULL,
    failure_reason varchar(500) NULL DEFAULT NULL,
    -- occurred_at = 고객 PC 시각(UTC). received_at = 본사 수신 시각.
    --   둘을 나눠 두는 이유: 고객 PC 시계가 틀어져 있어도 본사 기준 순서를 잃지 않는다.
    occurred_at    datetime(6)  NOT NULL,
    received_at    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    PRIMARY KEY (history_id),
    KEY idx_tuh_tenant (tenant_id, occurred_at),
    KEY idx_tuh_hash (tenant_id_hash, occurred_at),
    KEY idx_tuh_result (result, occurred_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='고객사 업데이트 이력(INSERT ONLY, 운영 메타만)';

-- ────────────────────────────────────────────────────────────────────────
-- tenant_update_state — 고객사별 현황 (UPSERT, 고객사당 1행)
--
--   ★ 사장님 2026-07-20 오더 3건이 이 테이블이다:
--       ① 비고(메모) 컬럼      → cs_note
--       ② 마지막 보고 시각      → last_reported_at
--       ③ 현재 버전 현황        → current_version
--
--   🔴 cs_note 는 **CS 참고용이다. 업데이트 동작에 절대 영향을 주지 않는다.**
--      사장님 원문: "단 비고는 CS 참고용 — 팝업을 막지 않음(고객 통제권 우선)."
--      ⇒ 이 컬럼을 읽어 분기하는 코드를 만들면 사장님 오더 위반이다(작4 §5 V-8 로 검증).
--
--   last_reported_at 이 왜 중요한가(강지원): "미보고 N일" 선제 연락의 근거가 된다.
--   조용한 고객사가 잘 쓰는 건지 죽은 건지 구분하려면 마지막 보고 시각이 있어야 한다.
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tenant_update_state (
    tenant_id_hash   varchar(80)  NOT NULL,
    tenant_id        varchar(36)  NULL DEFAULT NULL,
    current_version  varchar(20)  NULL DEFAULT NULL,
    last_result      varchar(20)  NULL DEFAULT NULL,
    last_channel     varchar(20)  NULL DEFAULT NULL,
    last_reported_at datetime(6)  NULL DEFAULT NULL,
    -- CS 메모. 본사 직원이 손으로 적는 값이라 워치독 수신이 덮어쓰지 않는다(UPSERT 대상 제외).
    cs_note          varchar(500) NULL DEFAULT NULL,
    created_at       datetime(6)  NOT NULL DEFAULT current_timestamp(6),
    updated_at       datetime(6)  NULL DEFAULT NULL ON UPDATE current_timestamp(6),
    PRIMARY KEY (tenant_id_hash),
    KEY idx_tus_tenant (tenant_id),
    KEY idx_tus_reported (last_reported_at),
    KEY idx_tus_version (current_version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='고객사 업데이트 현황(현재버전·마지막보고·CS비고)';

SET FOREIGN_KEY_CHECKS=1;

-- ============================================================================
-- 이번 범위 밖 (사장님 결정 2026-08-06 "받는 문 + 기록표까지"):
--   - 백오피스 조회 화면 배선 (AdminWatchdogMonitor.razor 는 이미 있으나 공급 API 없음)
--   - /watchdog/ping 경로 복구 — 별건. 본 모듈은 /api/telemetry/update-history 신규 경로다.
-- ============================================================================
