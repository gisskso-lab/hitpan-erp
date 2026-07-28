-- ============================================================================
-- DB-26: 이메일 SMTP 설정 + 발송이력 (2026-04-29)
-- ----------------------------------------------------------------------------
-- 사장님 결재 2026-04-29: 6종 문서(견적/수주/명세/계산/발주/매입) 자동 이메일 발송 + PDF 첨부.
-- §#2 tenant_id 우선 / §#3 send_history INSERT ONLY / §#5 SMTP 비밀번호 AES암호화 (서버 코드에서 처리)
-- §#17 InnoDB / §#18 본사 미수신 (고객사 본인 SMTP만 사용) / §#19 errors 0 + warnings 0
-- ============================================================================

-- 1) SMTP 설정 (테넌트당 1행)
CREATE TABLE IF NOT EXISTS email_settings (
    tenant_id           VARCHAR(36) NOT NULL,
    smtp_host           VARCHAR(100) NOT NULL,
    smtp_port           INT          NOT NULL DEFAULT 587,
    smtp_user           VARCHAR(120) NOT NULL,
    smtp_password_enc   VARBINARY(512) NULL,                          -- AES256 암호화된 패스워드
    use_ssl             TINYINT(1)   NOT NULL DEFAULT 1,
    from_address        VARCHAR(120) NOT NULL,
    from_name           VARCHAR(60)  NULL,
    bcc_self            TINYINT(1)   NOT NULL DEFAULT 0,              -- 자기자신에게 BCC
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    last_test_at        DATETIME     NULL,
    last_test_result    VARCHAR(20)  NULL,                            -- success / failed
    last_test_error     TEXT         NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2) 이메일 발송 이력 (INSERT ONLY §#3)
CREATE TABLE IF NOT EXISTS email_send_history (
    email_id            VARCHAR(36) NOT NULL,
    tenant_id           VARCHAR(36) NOT NULL,
    sent_at             DATETIME    NOT NULL,
    sent_by_user        VARCHAR(36) NULL,
    document_type       VARCHAR(20) NOT NULL,                          -- quotation/sales_order/delivery/tax_invoice/purchase_order/purchase_receipt
    document_no         VARCHAR(40) NOT NULL,
    document_id         VARCHAR(36) NULL,                              -- 원본 문서 PK (FK 아님 — soft 참조)
    partner_id          VARCHAR(36) NULL,
    recipient_email     VARCHAR(120) NOT NULL,
    cc_email            VARCHAR(255) NULL,
    bcc_email           VARCHAR(255) NULL,
    subject             VARCHAR(200) NOT NULL,
    body_text           TEXT         NULL,
    has_attachment      TINYINT(1)   NOT NULL DEFAULT 0,
    status              VARCHAR(20)  NOT NULL,                         -- queued / sent / failed
    error_message       TEXT         NULL,
    smtp_response       VARCHAR(500) NULL,
    PRIMARY KEY (email_id),
    KEY ix_emailhist_tenant_date (tenant_id, sent_at DESC),
    KEY ix_emailhist_doc (tenant_id, document_type, document_no),
    KEY ix_emailhist_partner (tenant_id, partner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3) 이메일 첨부파일 메타
CREATE TABLE IF NOT EXISTS email_attachment_history (
    attachment_id       VARCHAR(36) NOT NULL,
    email_id            VARCHAR(36) NOT NULL,
    tenant_id           VARCHAR(36) NOT NULL,
    filename            VARCHAR(200) NOT NULL,
    file_path           VARCHAR(500) NULL,                             -- 로컬 PDF 보관 경로 (90일)
    file_size_bytes     BIGINT       NULL,
    mime_type           VARCHAR(60)  NOT NULL DEFAULT 'application/pdf',
    PRIMARY KEY (attachment_id),
    KEY ix_attach_email (tenant_id, email_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
