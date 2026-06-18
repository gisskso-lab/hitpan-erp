-- ============================================================================
-- 히트판 백오피스(hitpan_backoffice) 자립형 통합 스키마 — payment 모듈
-- ============================================================================
-- 작성: PM 브라운킴 (사장님 결재 2026-06-18)
--
-- ★ 모듈 분리 원칙 (사장님 결재 2026-06-18 "결제 모듈처럼 붙였다 뺐다"):
--   - 베타1은 결제 없이 가입→설치→로그인→작동 완주. 결제 이벤트 0건.
--   - 8월 토스 가맹 시: toss_* 테이블이 필요하면 이 파일에 CREATE를 추가 → 자동실행기가
--     다음 기동 때 자동 적용. 코드 재공사 0 (헌법 #34 베타부터 정식 완성도).
--   - 지금은 연결고리(tenant_payments, FK 0)만 정식 포함. 결제 알맹이(카드정보 등) 0.
--     → 헌법 #22: 본사는 결제 메타만, 카드 원본 0건.
--
-- ★ 토스 결제는 이미 코드상 "잠든 모듈" (전수조사 2026-06-18):
--   - TossPaymentsController: 키 미설정 시 503(자고), 키 주입 시 정상(깸).
--   - IBillingProvider 추상화 + ManualBillingProvider. 8월엔 TossBillingProvider 추가만.
--   - 따라서 이 파일에 지금 toss 테이블을 미리 만들 필요 없음(빈 결제 레코드 = 멱등키 오염 위험).
--
-- DDL 출처: db/migrations/20260604_tenant_payments.sql:9-25 (실제 배포 스키마, 추측 0).
-- ============================================================================

SET FOREIGN_KEY_CHECKS=0;

-- ────────────────────────────────────────────────────────────────────────
-- tenant_payments — 결제 메타 (카드정보·CVC 0건. FK 0 — 모듈 독립)
--   signup_token / order_id로 느슨하게 연결. 8월 토스 paymentKey를 받을 자리.
-- ────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tenant_payments (
    payment_id   char(36)     NOT NULL,
    order_id     varchar(64)  NOT NULL,
    signup_token varchar(64)  NULL DEFAULT NULL,
    payment_key  varchar(255) NOT NULL,
    amount       bigint       NOT NULL,
    method       varchar(32)  NULL DEFAULT NULL,
    status       varchar(32)  NOT NULL DEFAULT 'pending',
    approved_at  varchar(32)  NULL DEFAULT NULL,
    raw_status   varchar(32)  NULL DEFAULT NULL,
    created_at   datetime     NOT NULL DEFAULT current_timestamp(),
    updated_at   datetime     NULL DEFAULT NULL ON UPDATE current_timestamp(),
    PRIMARY KEY (payment_id),
    UNIQUE KEY uq_tenant_payments_order (order_id),
    KEY idx_tenant_payments_token (signup_token),
    KEY idx_tenant_payments_status (status, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='결제 메타 (카드원본 0건, 모듈 독립)';

SET FOREIGN_KEY_CHECKS=1;

-- ============================================================================
-- 8월 토스 가맹 시 추가 예정 (지금은 만들지 않음 — 빈 레코드 오염 방지):
--   - 필요 시 toss_payment_events / toss_refunds 등을 본 파일에 CREATE 추가
--   - 환경변수 HITPAN_TOSS_CLIENT_KEY·HITPAN_TOSS_SECRET_KEY 주입(NCP, 사장님 영역)
--   - TossBillingProvider 클래스 1개 + DI 한 줄 → BillingService 코드 변경 0
-- ============================================================================
