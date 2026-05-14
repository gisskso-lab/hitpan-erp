-- WS-11 정공법 축 5: POTHER 4 풀스택 (사장님 직접 명령 2026-05-14)
-- 대상 테이블: partner_contacts(DOCNM), service_tickets(DOCAS), delivery_tracking(DELIVERY), events(CALENDAR)
-- 헌법 #5 AES (HP/EMAIL VARBINARY), #17 InnoDB, #1 tenant_id 멀티테넌트, utf8mb4_unicode_ci 통일

-- ────────────────────────────────────────────────────────
-- 1. partner_contacts (DOCNM → 명함/연락처)
-- ────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS partner_contacts (
    contact_id        VARCHAR(36)  NOT NULL,
    tenant_id         VARCHAR(36)  NOT NULL,
    partner_id        VARCHAR(36)  NULL COMMENT '연결된 업체(있을 때만)',
    contact_name      VARCHAR(100) NOT NULL,
    company_name      VARCHAR(200) NULL,
    tel               VARCHAR(30)  NULL COMMENT '대표 전화 (마스킹 X, 영업용)',
    hp_encrypted      VARBINARY(255) NULL COMMENT '개인전화 AES-256 (헌법 #5)',
    email_encrypted   VARBINARY(255) NULL COMMENT '이메일 AES-256 (헌법 #5)',
    address           VARCHAR(300) NULL,
    memo              VARCHAR(500) NULL,
    is_active         TINYINT(1)   NOT NULL DEFAULT 1,
    created_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    migrated_source_hash CHAR(64)  DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
    PRIMARY KEY (contact_id),
    KEY idx_pc_tenant_partner (tenant_id, partner_id),
    KEY idx_pc_tenant_name (tenant_id, contact_name),
    UNIQUE KEY uq_partner_contacts_source_hash (tenant_id, migrated_source_hash)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='POTHER.DOCNM 명함/연락처 (WS-11 축 5)';

-- ────────────────────────────────────────────────────────
-- 2. service_tickets (DOCAS → AS 접수/처리)
-- ────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS service_tickets (
    ticket_id         VARCHAR(36)  NOT NULL,
    tenant_id         VARCHAR(36)  NOT NULL,
    service_date      DATE         NOT NULL,
    partner_id        VARCHAR(36)  NULL COMMENT '대상 업체',
    item_id           VARCHAR(36)  NULL COMMENT '대상 상품',
    problem_desc      VARCHAR(1000) NULL COMMENT 'AS 접수 내용',
    fix_desc          VARCHAR(1000) NULL COMMENT 'AS 처리 내용',
    fee               DECIMAL(15,2) NOT NULL DEFAULT 0,
    memo              VARCHAR(500) NULL,
    is_active         TINYINT(1)   NOT NULL DEFAULT 1,
    created_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    migrated_source_hash CHAR(64)  DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
    PRIMARY KEY (ticket_id),
    KEY idx_st_tenant_date (tenant_id, service_date),
    KEY idx_st_tenant_partner (tenant_id, partner_id),
    UNIQUE KEY uq_service_tickets_source_hash (tenant_id, migrated_source_hash)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='POTHER.DOCAS AS 티켓 (WS-11 축 5)';

-- ────────────────────────────────────────────────────────
-- 3. delivery_tracking (DELIVERY → 배송 추적)
-- ────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS delivery_tracking (
    tracking_id       VARCHAR(36)  NOT NULL,
    tenant_id         VARCHAR(36)  NOT NULL,
    delivery_date     DATE         NOT NULL,
    partner_id        VARCHAR(36)  NULL COMMENT '배송 대상',
    address           VARCHAR(300) NULL,
    status            VARCHAR(30)  NOT NULL DEFAULT 'pending' COMMENT 'pending/shipped/delivered/canceled',
    memo              VARCHAR(500) NULL,
    is_active         TINYINT(1)   NOT NULL DEFAULT 1,
    created_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    migrated_source_hash CHAR(64)  DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
    PRIMARY KEY (tracking_id),
    KEY idx_dt_tenant_date (tenant_id, delivery_date),
    KEY idx_dt_tenant_partner (tenant_id, partner_id),
    UNIQUE KEY uq_delivery_tracking_source_hash (tenant_id, migrated_source_hash)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='POTHER.DELIVERY 배송 추적 (WS-11 축 5)';

-- ────────────────────────────────────────────────────────
-- 4. events (CALENDAR → 일정/달력)
-- ────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS events (
    event_id          VARCHAR(36)  NOT NULL,
    tenant_id         VARCHAR(36)  NOT NULL,
    event_date        DATE         NOT NULL,
    title             VARCHAR(200) NOT NULL,
    memo              VARCHAR(1000) NULL,
    is_active         TINYINT(1)   NOT NULL DEFAULT 1,
    created_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    migrated_source_hash CHAR(64)  DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
    PRIMARY KEY (event_id),
    KEY idx_ev_tenant_date (tenant_id, event_date),
    UNIQUE KEY uq_events_source_hash (tenant_id, migrated_source_hash)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='POTHER.CALENDAR 일정/달력 (WS-11 축 5)';
