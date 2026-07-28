-- 랜딩 가입 메타 테이블 (사장님 모두결재 2026-06-01, 잔여 #2)
--
-- 8명제 정합:
--   #1 — 랜딩 zero-DB passthrough: 평문 사업자정보 0건, 메타·해시·토큰만 박제
--   #2 — signup_token = OTP·결제 단계 연결용 단발성 토큰
--   #3 — biz_no_hash CHAR(64) HMAC-SHA256 + pepper (HSM 결재 후 환경변수)
--
-- 헌법 정합:
--   #17 — ENGINE=InnoDB 명시
--   #18·#22 — 평문 사업자번호·상호·대표자 미보유 (company_name·email은 가입 안내용)
--   #19 — utf8mb4_unicode_ci 통일
--
-- 운영 박제:
--   - landing_signups → otp_verified → tenant 생성 → 자식계정 발급 (백오피스 분리)
--   - 본사 백오피스 DB(hitpan_backoffice)로 이관 예정 (현재는 기본 스키마)

CREATE TABLE IF NOT EXISTS landing_signups (
    signup_token       VARCHAR(48)  NOT NULL PRIMARY KEY,
    biz_no_hash        CHAR(64)     NOT NULL COMMENT 'HMAC-SHA256(biz_no, pepper)',
    company_name       VARCHAR(120) NOT NULL COMMENT '가입 안내용 (영업 표시명, 본사 ERP 자동등록 후 마스킹)',
    email              VARCHAR(160) NOT NULL,
    phone              VARCHAR(32)  NULL,
    plan_type          VARCHAR(20)  NOT NULL DEFAULT 'basic',
    reseller_code      VARCHAR(32)  NULL COMMENT '대리점 추천 코드 (선택)',
    agree_terms        TINYINT(1)   NOT NULL DEFAULT 0,
    agree_privacy      TINYINT(1)   NOT NULL DEFAULT 0,
    status             VARCHAR(20)  NOT NULL DEFAULT 'submitted'
                       COMMENT 'submitted · otp_verified · paid · provisioned · expired',
    submitted_at       DATETIME     NOT NULL,
    otp_verified_at    DATETIME     NULL,
    paid_at            DATETIME     NULL,
    provisioned_at     DATETIME     NULL,

    UNIQUE KEY uk_biz_no_hash (biz_no_hash),
    INDEX idx_email_status (email, status),
    INDEX idx_status_submitted (status, submitted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='랜딩 가입 메타 (zero-DB passthrough 정합, 평문 사업자정보 0건)';
