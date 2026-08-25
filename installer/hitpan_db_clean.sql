-- =============================================================
-- 히트판 ERP 출시 빈 스키마 (개발 구조 100% 그대로)
-- 생성: PM 브라운킴 / 2026-06-18
-- 사장님 원칙: 코드·데이터 구조 100% 그대로. 빼고 넣고 없음.
--   137 테이블 + 8 트리거 = 개발 DB 구조 1:1 / 데이터 0
--   백도어 테스트계정 미포함 / common_codes 코드성 시드만
--   DEFINER + DB명 종속 구문 제거 = 회사별DB 이식용
-- =============================================================

/*M!999999\- enable the sandbox mode */ 
-- MariaDB dump 10.19-11.4.10-MariaDB, for Win64 (AMD64)
--
-- Host: localhost    Database: hitpan_erp
-- ------------------------------------------------------
-- Server version	11.4.10-MariaDB

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*M!100616 SET @OLD_NOTE_VERBOSITY=@@NOTE_VERBOSITY, NOTE_VERBOSITY=0 */;

--
-- Table structure for table `__efmigrationshistory`
--

DROP TABLE IF EXISTS `__efmigrationshistory`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `__efmigrationshistory` (
  `MigrationId` varchar(150) NOT NULL,
  `ProductVersion` varchar(32) NOT NULL,
  PRIMARY KEY (`MigrationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `accounts`
--

DROP TABLE IF EXISTS `accounts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `accounts` (
  `account_code` varchar(10) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `account_name` varchar(100) NOT NULL,
  `account_type` varchar(20) NOT NULL COMMENT 'asset/liability/equity/revenue/expense',
  `parent_code` varchar(10) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `sort_order` int(11) NOT NULL DEFAULT 0,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`tenant_id`,`account_code`),
  KEY `idx_acc_type` (`tenant_id`,`account_type`),
  KEY `idx_acc_parent` (`tenant_id`,`parent_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `ai_conversations`
--

DROP TABLE IF EXISTS `ai_conversations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `ai_conversations` (
  `conv_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `user_id` varchar(36) NOT NULL,
  `intent` varchar(30) NOT NULL DEFAULT 'usage_question' COMMENT 'usage_question/functional_issue/remote_support',
  `user_message` text NOT NULL,
  `ai_response` text DEFAULT NULL,
  `matched_article_ids` varchar(500) DEFAULT NULL COMMENT 'KB 매칭된 article_id JSON 배열',
  `confidence_score` decimal(3,2) DEFAULT NULL COMMENT 'AI 확신도 0.00~1.00',
  `was_helpful` tinyint(1) DEFAULT NULL COMMENT 'NULL=미평가, 1=도움됨, 0=도움안됨',
  `escalated_to_support` tinyint(1) NOT NULL DEFAULT 0,
  `led_to_kb_article` tinyint(1) NOT NULL DEFAULT 0 COMMENT '이 대화로 KB 새 문서 생성됐는지',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`conv_id`),
  KEY `idx_tenant_user` (`tenant_id`,`user_id`,`created_at`),
  KEY `idx_intent_helpful` (`intent`,`was_helpful`),
  KEY `fk_aiconv_user` (`user_id`),
  CONSTRAINT `fk_aiconv_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='AI 대화 이력 + 학습 자산 축적';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `ai_usage_logs`
--

DROP TABLE IF EXISTS `ai_usage_logs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `ai_usage_logs` (
  `usage_id` bigint(20) NOT NULL AUTO_INCREMENT,
  `tenant_id` varchar(36) NOT NULL,
  `conv_id` varchar(36) DEFAULT NULL,
  `ai_provider` varchar(30) NOT NULL DEFAULT 'none',
  `input_tokens` int(11) NOT NULL DEFAULT 0,
  `output_tokens` int(11) NOT NULL DEFAULT 0,
  `cached_tokens` int(11) NOT NULL DEFAULT 0,
  `total_tokens` int(11) NOT NULL DEFAULT 0,
  `cost_krw` decimal(10,2) NOT NULL DEFAULT 0.00,
  `charge_krw` decimal(10,2) NOT NULL DEFAULT 0.00,
  `charge_mode` varchar(20) NOT NULL DEFAULT 'hitpan_pool',
  `usage_type` varchar(30) NOT NULL DEFAULT 'chat',
  `ym` char(7) NOT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`usage_id`),
  KEY `idx_tenant_date` (`tenant_id`,`created_at`),
  KEY `idx_tenant_ym` (`tenant_id`,`ym`)
  -- fk_aiusage_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `approval_doc_lines`
--

DROP TABLE IF EXISTS `approval_doc_lines`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `approval_doc_lines` (
  `line_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `doc_type` varchar(30) NOT NULL,
  `seq_no` int(11) NOT NULL,
  `approver_id` varchar(36) NOT NULL,
  `approver_name` varchar(50) NOT NULL,
  `role_label` varchar(30) DEFAULT NULL,
  `delegate_id` varchar(36) DEFAULT NULL,
  `delegate_name` varchar(50) DEFAULT NULL,
  `delegate_start` date DEFAULT NULL,
  `delegate_end` date DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`line_id`),
  KEY `idx_approval_doc_lines_tenant_doc` (`tenant_id`,`doc_type`,`seq_no`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `approval_documents`
--

DROP TABLE IF EXISTS `approval_documents`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `approval_documents` (
  `approval_id` varchar(36) NOT NULL COMMENT '결재문서 PK',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트 ID',
  `doc_type` varchar(30) NOT NULL COMMENT '문서유형',
  `ref_id` varchar(36) NOT NULL COMMENT '원본 문서 ID (quotations.quotation_id 등)',
  `ref_no` varchar(30) DEFAULT NULL COMMENT '원본 문서번호',
  `title` varchar(200) NOT NULL COMMENT '결재 제목',
  `amount` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '금액',
  `status` varchar(20) NOT NULL DEFAULT 'pending' COMMENT '상태 (pending, approved, rejected, cancelled)',
  `current_seq` int(11) NOT NULL DEFAULT 1 COMMENT '현재 결재 순서',
  `total_lines` int(11) NOT NULL DEFAULT 1 COMMENT '총 결재라인 수',
  `requester_id` varchar(36) NOT NULL COMMENT '기안자 사원 ID',
  `requester_name` varchar(50) NOT NULL COMMENT '기안자 이름',
  `requested_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) COMMENT '기안일시',
  `completed_at` datetime(6) DEFAULT NULL COMMENT '최종 결재완료/반려 일시',
  `memo` varchar(500) DEFAULT NULL COMMENT '기안 메모',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(36) DEFAULT NULL,
  PRIMARY KEY (`approval_id`),
  UNIQUE KEY `uq_tenant_ref` (`tenant_id`,`doc_type`,`ref_id`),
  KEY `idx_status` (`tenant_id`,`status`),
  KEY `idx_requester` (`tenant_id`,`requester_id`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='결재 문서 — 결재 요청 건별 상태 관리';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `approval_history`
--

DROP TABLE IF EXISTS `approval_history`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `approval_history` (
  `history_id` varchar(36) NOT NULL COMMENT '이력 PK',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트 ID',
  `approval_id` varchar(36) NOT NULL COMMENT '결재문서 FK',
  `seq_no` int(11) NOT NULL COMMENT '결재 순서',
  `approver_id` varchar(36) NOT NULL COMMENT '실제 결재자 사원 ID (위임결재 시 위임자)',
  `approver_name` varchar(50) NOT NULL COMMENT '실제 결재자 이름',
  `is_delegated` tinyint(1) NOT NULL DEFAULT 0 COMMENT '위임결재 여부',
  `original_approver_id` varchar(36) DEFAULT NULL COMMENT '원래 결재자 (위임 시)',
  `action` varchar(20) NOT NULL COMMENT '결재 액션 (approved, rejected)',
  `comment` varchar(500) DEFAULT NULL COMMENT '결재 의견',
  `acted_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) COMMENT '결재일시',
  PRIMARY KEY (`history_id`),
  KEY `idx_approval` (`tenant_id`,`approval_id`),
  KEY `idx_approver_action` (`tenant_id`,`approver_id`,`action`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='결재 이력 — INSERT ONLY, 수정/삭제 절대 금지';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `approval_line_steps`
--

DROP TABLE IF EXISTS `approval_line_steps`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `approval_line_steps` (
  `step_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `approval_line_id` varchar(36) NOT NULL,
  `step_order` int(11) NOT NULL COMMENT '결재 단계 순서',
  `position_id` varchar(36) DEFAULT NULL COMMENT '직책 ID',
  `employee_id` varchar(36) DEFAULT NULL COMMENT '사원 ID',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`step_id`),
  KEY `idx_approval_line_steps_tenant_line` (`tenant_id`,`approval_line_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `approval_lines`
--

DROP TABLE IF EXISTS `approval_lines`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `approval_lines` (
  `approval_line_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `name` varchar(100) NOT NULL COMMENT '결재라인 이름',
  `description` varchar(200) DEFAULT NULL COMMENT '설명',
  `sort_order` int(11) NOT NULL DEFAULT 0 COMMENT '정렬순서',
  `is_active` tinyint(1) NOT NULL DEFAULT 1 COMMENT '사용여부',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`approval_line_id`),
  KEY `idx_approval_lines_tenant` (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `approval_settings`
--

DROP TABLE IF EXISTS `approval_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `approval_settings` (
  `setting_id` varchar(36) NOT NULL COMMENT '설정 PK',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트 ID',
  `doc_type` varchar(30) NOT NULL COMMENT '문서유형 (quotation, sales_order, delivery, purchase_order, receipt, sales_return, purchase_return, expense, leave, overtime)',
  `is_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '결재 사용 여부 (0=OFF, 1=ON)',
  `threshold_amount` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '기준금액 (이 금액 이상이면 결재 필요, 0=항상)',
  `auto_approve_below` tinyint(1) NOT NULL DEFAULT 0 COMMENT '기준금액 미만 자동승인 여부',
  `max_lines` int(11) NOT NULL DEFAULT 3 COMMENT '최대 결재라인 수',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(36) DEFAULT NULL,
  PRIMARY KEY (`setting_id`),
  UNIQUE KEY `uq_tenant_doctype` (`tenant_id`,`doc_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='결재 설정 — 문서유형별 결재 ON/OFF 및 기준금액';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `attendance`
--

DROP TABLE IF EXISTS `attendance`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `attendance` (
  `attendance_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL,
  `work_date` date NOT NULL,
  `check_in` datetime(6) DEFAULT NULL,
  `check_out` datetime(6) DEFAULT NULL,
  `work_hours` decimal(4,1) DEFAULT NULL COMMENT '?ٹ??ð?',
  `status` varchar(20) NOT NULL DEFAULT 'normal' COMMENT 'normal/late/early_leave/absent/leave',
  `memo` varchar(200) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`attendance_id`),
  UNIQUE KEY `uq_att_date` (`tenant_id`,`employee_id`,`work_date`),
  KEY `idx_att_tenant_emp` (`tenant_id`,`employee_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `audit_logs`
--

DROP TABLE IF EXISTS `audit_logs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `audit_logs` (
  `log_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) DEFAULT NULL,
  `user_id` varchar(36) DEFAULT NULL,
  `account_type` varchar(50) DEFAULT NULL,
  `ip_address` varchar(50) DEFAULT NULL,
  `method` varchar(10) DEFAULT NULL,
  `endpoint` varchar(500) DEFAULT NULL,
  `status_code` int(11) DEFAULT NULL,
  `user_agent` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`log_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_user` (`user_id`),
  KEY `idx_created` (`created_at`),
  KEY `idx_ip` (`ip_address`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `audit_trail`
--

DROP TABLE IF EXISTS `audit_trail`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `audit_trail` (
  `log_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `user_id` varchar(36) DEFAULT NULL,
  `action_type` varchar(30) NOT NULL,
  `entity_type` varchar(50) NOT NULL,
  `entity_id` varchar(36) DEFAULT NULL,
  `before_value` longtext DEFAULT NULL,
  `after_value` longtext DEFAULT NULL,
  `reason` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`log_id`),
  KEY `idx_at_tenant_entity` (`tenant_id`,`entity_type`,`entity_id`),
  KEY `idx_at_tenant_date` (`tenant_id`,`created_at`),
  KEY `idx_at_user` (`tenant_id`,`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `backup_history`
--

DROP TABLE IF EXISTS `backup_history`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `backup_history` (
  `backup_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `started_at` datetime NOT NULL,
  `finished_at` datetime DEFAULT NULL,
  `primary_file` varchar(500) DEFAULT NULL,
  `mirror_file` varchar(500) DEFAULT NULL,
  `file_size_bytes` bigint(20) DEFAULT NULL,
  `status` varchar(20) NOT NULL,
  `error_message` text DEFAULT NULL,
  `triggered_by` varchar(20) NOT NULL DEFAULT 'manual',
  PRIMARY KEY (`backup_id`),
  KEY `ix_backup_history_tenant_started` (`tenant_id`,`started_at` DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `backup_settings`
--

DROP TABLE IF EXISTS `backup_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `backup_settings` (
  `tenant_id` varchar(36) NOT NULL,
  `primary_path` varchar(500) NOT NULL,
  `mirror_path` varchar(500) DEFAULT NULL,
  `schedule_mode` varchar(20) NOT NULL DEFAULT 'manual',
  `retention_count` int(11) NOT NULL DEFAULT 30,
  `last_run_at` datetime DEFAULT NULL,
  `last_status` varchar(20) DEFAULT NULL,
  `last_error` text DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `bank_transactions`
--

DROP TABLE IF EXISTS `bank_transactions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `bank_transactions` (
  `bank_tx_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `account_no` varchar(30) NOT NULL,
  `bank_name` varchar(40) DEFAULT NULL,
  `tx_date` date NOT NULL,
  `tx_type` varchar(1) NOT NULL,
  `amount` decimal(15,2) NOT NULL,
  `balance_after` decimal(15,2) DEFAULT NULL,
  `partner_id` varchar(36) DEFAULT NULL,
  `partner_name_legacy` varchar(50) DEFAULT NULL,
  `description` varchar(50) DEFAULT NULL,
  `remark` varchar(40) DEFAULT NULL,
  `imported_from` varchar(20) NOT NULL DEFAULT 'manual',
  `legacy_source` varchar(20) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `source_type` varchar(30) DEFAULT NULL,
  `source_id` varchar(80) DEFAULT NULL,
  `migrated_source_hash` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`bank_tx_id`),
  UNIQUE KEY `uq_bank_tx_source` (`tenant_id`,`source_type`,`source_id`),
  KEY `ix_banktx_tenant_account_date` (`tenant_id`,`account_no`,`tx_date`),
  KEY `ix_banktx_tenant_date` (`tenant_id`,`tx_date`),
  KEY `ix_banktx_tenant_partner` (`tenant_id`,`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `billing_invoices`
--

DROP TABLE IF EXISTS `billing_invoices`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `billing_invoices` (
  `invoice_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `invoice_no` varchar(40) NOT NULL COMMENT '인보이스 번호 (HP-20260429-001)',
  `subscription_id` varchar(36) DEFAULT NULL,
  `plan_code` varchar(40) NOT NULL,
  `plan_name` varchar(80) NOT NULL,
  `billing_period_start` date NOT NULL,
  `billing_period_end` date NOT NULL,
  `amount` decimal(14,2) NOT NULL COMMENT '공급가액',
  `vat` decimal(14,2) NOT NULL COMMENT '부가세',
  `total_amount` decimal(14,2) NOT NULL COMMENT '합계',
  `status` varchar(20) NOT NULL DEFAULT 'pending' COMMENT 'pending / paid / failed / refunded / cancelled',
  `issued_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `due_date` date DEFAULT NULL,
  `paid_at` datetime(6) DEFAULT NULL,
  `payment_method_id` varchar(36) DEFAULT NULL COMMENT '결제 시 사용된 수단',
  `provider` varchar(20) DEFAULT NULL COMMENT 'toss / manual',
  `provider_payment_key` varchar(120) DEFAULT NULL COMMENT '토스 paymentKey',
  `receipt_url` varchar(500) DEFAULT NULL,
  `tax_invoice_issued` tinyint(1) NOT NULL DEFAULT 0,
  `tax_invoice_no` varchar(40) DEFAULT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(60) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(60) DEFAULT NULL,
  PRIMARY KEY (`invoice_id`),
  UNIQUE KEY `uk_billing_invoice_no` (`tenant_id`,`invoice_no`),
  KEY `ix_billing_invoice_tenant_status` (`tenant_id`,`status`,`issued_at`),
  KEY `ix_billing_invoice_subscription` (`tenant_id`,`subscription_id`)
  -- fk_billing_invoices_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='구독 인보이스 — INSERT ONLY';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `billing_payment_attempts`
--

DROP TABLE IF EXISTS `billing_payment_attempts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `billing_payment_attempts` (
  `attempt_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `invoice_id` varchar(36) NOT NULL,
  `payment_method_id` varchar(36) DEFAULT NULL,
  `provider` varchar(20) NOT NULL,
  `attempted_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `status` varchar(20) NOT NULL COMMENT 'success / failed / pending',
  `error_code` varchar(60) DEFAULT NULL,
  `error_message` varchar(500) DEFAULT NULL,
  `provider_response_json` longtext DEFAULT NULL COMMENT '토스 응답 원본 (디버깅용)',
  PRIMARY KEY (`attempt_id`),
  KEY `ix_billing_attempt_invoice` (`tenant_id`,`invoice_id`,`attempted_at`),
  KEY `ix_billing_attempt_status` (`tenant_id`,`status`,`attempted_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='결제 시도 로그 — 감사·디버깅';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `billing_payment_methods`
--

DROP TABLE IF EXISTS `billing_payment_methods`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `billing_payment_methods` (
  `payment_method_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `provider` varchar(20) NOT NULL COMMENT 'toss / manual',
  `method_type` varchar(20) NOT NULL COMMENT 'card / bank_transfer',
  `provider_billing_key` varbinary(512) DEFAULT NULL COMMENT '토스 빌링키 (AES-256 암호화)',
  `customer_key` varchar(100) DEFAULT NULL COMMENT '토스 customerKey (테넌트별 고정)',
  `display_name` varchar(120) NOT NULL COMMENT '표시명 (예: 신한카드 ****1234)',
  `card_brand` varchar(40) DEFAULT NULL,
  `card_last4` varchar(4) DEFAULT NULL,
  `card_owner_type` varchar(20) DEFAULT NULL COMMENT 'corporate / personal',
  `is_default` tinyint(1) NOT NULL DEFAULT 0,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `registered_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `last_used_at` datetime(6) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(60) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(60) DEFAULT NULL,
  PRIMARY KEY (`payment_method_id`),
  KEY `ix_billing_pm_tenant_active` (`tenant_id`,`is_active`,`is_default`),
  KEY `ix_billing_pm_provider` (`tenant_id`,`provider`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='결제수단 — 토스 빌링키 / 무통장입금';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `billing_settings`
--

DROP TABLE IF EXISTS `billing_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `billing_settings` (
  `setting_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `head_office_bank` varchar(60) DEFAULT NULL COMMENT '본사 입금 은행명',
  `head_office_account` varchar(60) DEFAULT NULL COMMENT '본사 입금 계좌번호',
  `head_office_holder` varchar(60) DEFAULT NULL COMMENT '본사 입금 예금주',
  `auto_billing_day` tinyint(4) NOT NULL DEFAULT 1 COMMENT '자동결제일 (1~28)',
  `grace_period_days` tinyint(4) NOT NULL DEFAULT 7 COMMENT '연체 유예일',
  `notify_email` varchar(120) DEFAULT NULL COMMENT '결제 알림 이메일',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(60) DEFAULT NULL,
  PRIMARY KEY (`setting_id`),
  UNIQUE KEY `uk_billing_settings_tenant` (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='구독결제 운영 설정 — 본사 입금계좌·자동결제일·알림';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `billing_subscriptions`
--

DROP TABLE IF EXISTS `billing_subscriptions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `billing_subscriptions` (
  `subscription_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `plan_code` varchar(40) NOT NULL COMMENT 'starter / business / pro',
  `plan_name` varchar(80) NOT NULL,
  `monthly_amount` decimal(14,2) NOT NULL COMMENT '월 구독료 (VAT 별도)',
  `license_count` int(11) NOT NULL DEFAULT 1,
  `payment_method_id` varchar(36) DEFAULT NULL COMMENT '연결된 결제수단 (NULL=미연결)',
  `started_at` datetime(6) NOT NULL,
  `next_billing_date` date DEFAULT NULL COMMENT '다음 결제 예정일',
  `expires_at` datetime(6) DEFAULT NULL COMMENT '구독 만료일',
  `status` varchar(20) NOT NULL DEFAULT 'active' COMMENT 'active / past_due / cancelled / paused',
  `cancelled_at` datetime(6) DEFAULT NULL,
  `cancel_reason` varchar(255) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`subscription_id`),
  KEY `ix_billing_sub_tenant_status` (`tenant_id`,`status`),
  KEY `ix_billing_sub_next_billing` (`next_billing_date`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='활성 구독 — 플랜·금액·다음 결제일';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `bills`
--

DROP TABLE IF EXISTS `bills`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `bills` (
  `bill_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `bill_type` varchar(1) NOT NULL,
  `bill_no` varchar(30) NOT NULL,
  `bank_name` varchar(40) DEFAULT NULL,
  `issue_place` varchar(40) DEFAULT NULL,
  `partner_id` varchar(36) DEFAULT NULL,
  `partner_name_legacy` varchar(50) DEFAULT NULL,
  `issue_date` date NOT NULL,
  `maturity_date` date DEFAULT NULL,
  `discount_date` date DEFAULT NULL,
  `settled_date` date DEFAULT NULL,
  `amount` decimal(15,2) NOT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'issued',
  `remark` varchar(100) DEFAULT NULL,
  `legacy_source` varchar(20) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `source_id` varchar(80) DEFAULT NULL COMMENT '#82 봉합: 마이그 멱등 키',
  PRIMARY KEY (`bill_id`),
  UNIQUE KEY `uq_bills_source` (`tenant_id`,`source_id`),
  KEY `ix_bills_tenant_type_status` (`tenant_id`,`bill_type`,`status`),
  KEY `ix_bills_tenant_maturity` (`tenant_id`,`maturity_date`),
  KEY `ix_bills_tenant_partner` (`tenant_id`,`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `bom_cost_cache`
--

DROP TABLE IF EXISTS `bom_cost_cache`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `bom_cost_cache` (
  `cache_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `product_item_id` varchar(36) NOT NULL,
  `calculated_cost` decimal(15,2) NOT NULL DEFAULT 0.00,
  `material_count` int(11) NOT NULL DEFAULT 0,
  `is_dirty` tinyint(1) NOT NULL DEFAULT 0,
  `last_calculated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`cache_id`),
  UNIQUE KEY `uk_tenant_item` (`tenant_id`,`product_item_id`),
  KEY `idx_dirty` (`tenant_id`,`is_dirty`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `bom_headers`
--

DROP TABLE IF EXISTS `bom_headers`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `bom_headers` (
  `bom_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `product_item_id` varchar(36) NOT NULL,
  `bom_name` varchar(100) NOT NULL,
  `bom_version` int(11) NOT NULL DEFAULT 1,
  `is_default` tinyint(1) NOT NULL DEFAULT 1,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `memo` text DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `source_id` varchar(80) DEFAULT NULL COMMENT '#82 봉합',
  PRIMARY KEY (`bom_id`),
  UNIQUE KEY `uq_bom_headers_source` (`tenant_id`,`source_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_product` (`tenant_id`,`product_item_id`),
  KEY `idx_tenant_active` (`tenant_id`,`is_active`)
  -- fk_bom_headers_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `bom_items`
--

DROP TABLE IF EXISTS `bom_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `bom_items` (
  `bom_item_id` varchar(36) NOT NULL,
  `bom_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `seq_no` int(11) NOT NULL DEFAULT 1,
  `material_item_id` varchar(36) NOT NULL,
  `qty` decimal(10,2) NOT NULL DEFAULT 1.00,
  `unit` varchar(20) NOT NULL DEFAULT 'EA',
  `loss_rate` decimal(5,2) NOT NULL DEFAULT 0.00,
  `memo` varchar(200) DEFAULT NULL,
  `source_id` varchar(80) DEFAULT NULL COMMENT '#82 봉합',
  PRIMARY KEY (`bom_item_id`),
  UNIQUE KEY `uq_bom_items_source` (`tenant_id`,`source_id`),
  KEY `idx_bom` (`bom_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_material` (`tenant_id`,`material_item_id`),
  CONSTRAINT `fk_bom_items_header` FOREIGN KEY (`bom_id`) REFERENCES `bom_headers` (`bom_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `card_payment_lines`
--

DROP TABLE IF EXISTS `card_payment_lines`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `card_payment_lines` (
  `line_id` varchar(36) NOT NULL,
  `card_payment_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `seq` int(11) NOT NULL,
  `partner_id` varchar(36) DEFAULT NULL,
  `partner_name_legacy` varchar(50) DEFAULT NULL,
  `tx_date` date NOT NULL,
  `amount` decimal(15,2) NOT NULL,
  `remark` varchar(60) DEFAULT NULL,
  PRIMARY KEY (`line_id`),
  KEY `ix_cardline_tenant_master` (`tenant_id`,`card_payment_id`),
  KEY `ix_cardline_tenant_partner` (`tenant_id`,`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `card_payments`
--

DROP TABLE IF EXISTS `card_payments`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `card_payments` (
  `card_payment_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `card_no` varchar(20) NOT NULL,
  `card_company` varchar(20) DEFAULT NULL,
  `holder_name` varchar(20) DEFAULT NULL,
  `payment_date` date NOT NULL,
  `bank_settle_date` date DEFAULT NULL,
  `total_amount` decimal(15,2) NOT NULL,
  `installment_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `installment_months` int(11) NOT NULL DEFAULT 0,
  `settled_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `status` varchar(20) NOT NULL DEFAULT 'pending',
  `remark` varchar(60) DEFAULT NULL,
  `legacy_source` varchar(20) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `source_id` varchar(80) DEFAULT NULL COMMENT '#82 봉합',
  PRIMARY KEY (`card_payment_id`),
  UNIQUE KEY `uq_card_payments_source` (`tenant_id`,`source_id`),
  KEY `ix_cardpay_tenant_date` (`tenant_id`,`payment_date`),
  KEY `ix_cardpay_tenant_status` (`tenant_id`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `cashbook`
--

DROP TABLE IF EXISTS `cashbook`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `cashbook` (
  `cashbook_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `tx_date` date NOT NULL,
  `tx_type` varchar(20) NOT NULL DEFAULT 'income',
  `category` varchar(50) DEFAULT NULL,
  `partner_id` varchar(36) DEFAULT NULL,
  `description` varchar(200) NOT NULL,
  `income_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `expense_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `balance` decimal(15,2) NOT NULL DEFAULT 0.00,
  `payment_method` varchar(20) NOT NULL DEFAULT 'cash',
  `ref_doc_type` varchar(30) DEFAULT NULL,
  `ref_doc_id` varchar(36) DEFAULT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `source_type` varchar(30) DEFAULT NULL,
  `source_id` varchar(80) DEFAULT NULL,
  `migrated_source_hash` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`cashbook_id`),
  UNIQUE KEY `uq_cashbook_source` (`tenant_id`,`source_type`,`source_id`),
  KEY `idx_tenant_date` (`tenant_id`,`tx_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `collections`
--

DROP TABLE IF EXISTS `collections`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `collections` (
  `collection_id` varchar(36) NOT NULL COMMENT '수금 PK',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트 ID',
  `partner_id` varchar(36) NOT NULL COMMENT '거래처 ID',
  `collection_date` date NOT NULL COMMENT '수금일',
  `amount` decimal(15,2) NOT NULL COMMENT '수금액',
  `collection_method` varchar(20) NOT NULL DEFAULT 'cash' COMMENT '수금수단 (cash, bank_transfer, check, card, note)',
  `ref_doc_type` varchar(30) DEFAULT NULL COMMENT '관련 문서유형 (delivery, tax_invoice 등)',
  `ref_doc_id` varchar(36) DEFAULT NULL COMMENT '관련 문서 ID',
  `memo` varchar(500) DEFAULT NULL COMMENT '비고',
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(36) DEFAULT NULL,
  `source_type` varchar(30) DEFAULT NULL COMMENT '???̱? ???? Ű?? - migration/manual',
  `source_id` varchar(50) DEFAULT NULL COMMENT '???̱? ???? ?ĺ???',
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
  PRIMARY KEY (`collection_id`),
  UNIQUE KEY `uq_collections_source` (`tenant_id`,`source_type`,`source_id`),
  UNIQUE KEY `uq_collections_source_hash` (`tenant_id`,`migrated_source_hash`),
  KEY `idx_tenant_partner` (`tenant_id`,`partner_id`),
  KEY `idx_tenant_date` (`tenant_id`,`collection_date`),
  KEY `idx_coll_tenant_active_date` (`tenant_id`,`is_active`,`collection_date` DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='수금 — 거래처로부터 받은 돈 기록';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `common_codes`
--

DROP TABLE IF EXISTS `common_codes`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `common_codes` (
  `code_id` char(36) NOT NULL,
  `tenant_id` char(36) DEFAULT NULL COMMENT 'NULL=?ý??? ????',
  `code_group` varchar(30) NOT NULL,
  `code_value` varchar(30) NOT NULL,
  `code_label` varchar(50) NOT NULL,
  `sort_order` smallint(6) NOT NULL DEFAULT 0,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`code_id`),
  KEY `idx_group` (`tenant_id`,`code_group`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='???? ?ڵ?';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `custom_order_specs`
--

DROP TABLE IF EXISTS `custom_order_specs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `custom_order_specs` (
  `spec_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `order_id` varchar(36) NOT NULL,
  `order_item_id` varchar(36) DEFAULT NULL,
  `width_mm` int(11) DEFAULT NULL,
  `height_mm` int(11) DEFAULT NULL,
  `depth_mm` int(11) DEFAULT NULL,
  `wood_type` varchar(50) DEFAULT NULL COMMENT 'oak/walnut/pine 등',
  `color_code` varchar(30) DEFAULT NULL,
  `finish_type` varchar(30) DEFAULT NULL COMMENT 'matte/glossy/satin',
  `drawing_url` varchar(500) DEFAULT NULL COMMENT '도면 파일',
  `special_requirements` text DEFAULT NULL,
  `customer_signature_hash` varchar(100) DEFAULT NULL,
  `revision_no` int(11) NOT NULL DEFAULT 1,
  `status` varchar(20) NOT NULL DEFAULT 'confirmed' COMMENT 'draft/confirmed/changed/cancelled',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`spec_id`),
  KEY `idx_cos_order` (`tenant_id`,`order_id`),
  KEY `fk_cos_order` (`order_id`),
  CONSTRAINT `fk_cos_order` FOREIGN KEY (`order_id`) REFERENCES `sales_orders` (`order_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='맞춤 가구 주문 사양';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `delivery_tracking`
--

DROP TABLE IF EXISTS `delivery_tracking`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `delivery_tracking` (
  `tracking_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `delivery_date` date NOT NULL,
  `partner_id` varchar(36) DEFAULT NULL COMMENT '배송 대상',
  `address` varchar(300) DEFAULT NULL,
  `status` varchar(30) NOT NULL DEFAULT 'pending' COMMENT 'pending/shipped/delivered/canceled',
  `memo` varchar(500) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
  PRIMARY KEY (`tracking_id`),
  UNIQUE KEY `uq_delivery_tracking_source_hash` (`tenant_id`,`migrated_source_hash`),
  KEY `idx_dt_tenant_date` (`tenant_id`,`delivery_date`),
  KEY `idx_dt_tenant_partner` (`tenant_id`,`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='POTHER.DELIVERY 배송 추적 (WS-11 축 5)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `departments`
--

DROP TABLE IF EXISTS `departments`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `departments` (
  `dept_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `parent_dept_id` varchar(36) DEFAULT NULL,
  `dept_name` varchar(50) NOT NULL,
  `dept_code` varchar(20) DEFAULT NULL,
  `sort_order` smallint(6) NOT NULL,
  `is_active` tinyint(1) NOT NULL,
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  PRIMARY KEY (`dept_id`),
  KEY `fk_departments_tenant` (`tenant_id`)
  -- fk_departments_tenant FK 제거 (무결 봉합 2026-06-18): tenants 삭제 FK 제거. 동명 KEY 인덱스는 보존, tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `device_login_logs`
--

DROP TABLE IF EXISTS `device_login_logs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `device_login_logs` (
  `log_id` bigint(20) NOT NULL AUTO_INCREMENT,
  `device_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `user_id` varchar(36) NOT NULL,
  `ip_address` varchar(50) DEFAULT NULL,
  `login_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `login_result` varchar(20) NOT NULL COMMENT 'success / denied_limit / denied_revoked',
  PRIMARY KEY (`log_id`),
  KEY `idx_tenant_device` (`tenant_id`,`device_id`),
  KEY `idx_login_at` (`login_at`)
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='기기 로그인 이력';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `document_conversions`
--

DROP TABLE IF EXISTS `document_conversions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `document_conversions` (
  `conversion_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `source_type` varchar(30) NOT NULL,
  `source_id` varchar(36) NOT NULL,
  `source_no` varchar(30) DEFAULT NULL,
  `target_type` varchar(30) NOT NULL,
  `target_id` varchar(36) NOT NULL,
  `target_no` varchar(30) DEFAULT NULL,
  `converted_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `converted_by` varchar(36) DEFAULT NULL,
  PRIMARY KEY (`conversion_id`),
  KEY `idx_tenant_source` (`tenant_id`,`source_type`,`source_id`),
  KEY `idx_tenant_target` (`tenant_id`,`target_type`,`target_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `email_attachment_history`
--

DROP TABLE IF EXISTS `email_attachment_history`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `email_attachment_history` (
  `attachment_id` varchar(36) NOT NULL,
  `email_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `filename` varchar(200) NOT NULL,
  `file_path` varchar(500) DEFAULT NULL,
  `file_size_bytes` bigint(20) DEFAULT NULL,
  `mime_type` varchar(60) NOT NULL DEFAULT 'application/pdf',
  PRIMARY KEY (`attachment_id`),
  KEY `ix_attach_email` (`tenant_id`,`email_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `email_send_history`
--

DROP TABLE IF EXISTS `email_send_history`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `email_send_history` (
  `email_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `sent_at` datetime NOT NULL,
  `sent_by_user` varchar(36) DEFAULT NULL,
  `document_type` varchar(20) NOT NULL,
  `document_no` varchar(40) NOT NULL,
  `document_id` varchar(36) DEFAULT NULL,
  `partner_id` varchar(36) DEFAULT NULL,
  `recipient_email` varchar(120) NOT NULL,
  `cc_email` varchar(255) DEFAULT NULL,
  `bcc_email` varchar(255) DEFAULT NULL,
  `subject` varchar(200) NOT NULL,
  `body_text` text DEFAULT NULL,
  `has_attachment` tinyint(1) NOT NULL DEFAULT 0,
  `status` varchar(20) NOT NULL,
  `error_message` text DEFAULT NULL,
  `smtp_response` varchar(500) DEFAULT NULL,
  PRIMARY KEY (`email_id`),
  KEY `ix_emailhist_tenant_date` (`tenant_id`,`sent_at` DESC),
  KEY `ix_emailhist_doc` (`tenant_id`,`document_type`,`document_no`),
  KEY `ix_emailhist_partner` (`tenant_id`,`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `email_settings`
--

--
-- Table structure for table `geocoding_settings`  (DB-105)
--

DROP TABLE IF EXISTS `geocoding_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `geocoding_settings` (
  `tenant_id` varchar(36) NOT NULL,
  `provider` varchar(30) NOT NULL DEFAULT 'kakao',
  `api_key_enc` varbinary(512) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 0,
  `last_test_at` datetime DEFAULT NULL,
  `last_test_result` varchar(20) DEFAULT NULL,
  `last_test_error` text DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `fax_send_history`  (DB-105)
--

DROP TABLE IF EXISTS `fax_send_history`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `fax_send_history` (
  `fax_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `sent_at` datetime NOT NULL,
  `sent_by_user` varchar(36) DEFAULT NULL,
  `document_type` varchar(20) NOT NULL,
  `document_no` varchar(40) NOT NULL,
  `document_id` varchar(36) DEFAULT NULL,
  `partner_id` varchar(36) DEFAULT NULL,
  `recipient_fax_no` varchar(20) NOT NULL,
  `recipient_name` varchar(60) DEFAULT NULL,
  `page_count` int(11) DEFAULT NULL,
  `provider` varchar(30) NOT NULL DEFAULT 'mock',
  `provider_job_id` varchar(100) DEFAULT NULL,
  `status` varchar(20) NOT NULL,
  `error_message` text DEFAULT NULL,
  `provider_response` varchar(500) DEFAULT NULL,
  PRIMARY KEY (`fax_id`),
  KEY `ix_faxhist_tenant_date` (`tenant_id`,`sent_at` DESC),
  KEY `ix_faxhist_doc` (`tenant_id`,`document_type`,`document_no`),
  KEY `ix_faxhist_partner` (`tenant_id`,`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `fax_settings`  (DB-105)
--

DROP TABLE IF EXISTS `fax_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `fax_settings` (
  `tenant_id` varchar(36) NOT NULL,
  `provider` varchar(30) NOT NULL DEFAULT 'mock',
  `api_endpoint` varchar(200) DEFAULT NULL,
  `api_key_enc` varbinary(512) DEFAULT NULL,
  `api_secret_enc` varbinary(512) DEFAULT NULL,
  `sender_fax_no` varchar(20) DEFAULT NULL,
  `sender_name` varchar(60) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 0,
  `last_test_at` datetime DEFAULT NULL,
  `last_test_result` varchar(20) DEFAULT NULL,
  `last_test_error` text DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `email_settings`
--

DROP TABLE IF EXISTS `email_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `email_settings` (
  `tenant_id` varchar(36) NOT NULL,
  `smtp_host` varchar(100) NOT NULL,
  `smtp_port` int(11) NOT NULL DEFAULT 587,
  `smtp_user` varchar(120) NOT NULL,
  `smtp_password_enc` varbinary(512) DEFAULT NULL,
  `use_ssl` tinyint(1) NOT NULL DEFAULT 1,
  `from_address` varchar(120) NOT NULL,
  `from_name` varchar(60) DEFAULT NULL,
  `bcc_self` tinyint(1) NOT NULL DEFAULT 0,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `last_test_at` datetime DEFAULT NULL,
  `last_test_result` varchar(20) DEFAULT NULL,
  `last_test_error` text DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `employees`
--

DROP TABLE IF EXISTS `employees`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `employees` (
  `employee_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `user_id` varchar(36) DEFAULT NULL,
  `emp_no` varchar(20) NOT NULL,
  `emp_name` varchar(50) NOT NULL,
  `dept_id` varchar(36) DEFAULT NULL,
  `position` varchar(30) DEFAULT NULL,
  `job_title` varchar(30) DEFAULT NULL,
  `emp_type` longtext NOT NULL,
  `weekly_hours` decimal(4,1) DEFAULT NULL COMMENT '주당 소정근로시간(약정). NULL=미정 — 반자동 원칙상 임의로 채우지 않는다. DB-94',
  `join_date` datetime(6) NOT NULL,
  `resign_date` datetime(6) DEFAULT NULL,
  `birth_date` varchar(200) DEFAULT NULL,
  `id_no_hash` varchar(256) DEFAULT NULL,
  `phone` varchar(20) DEFAULT NULL,
  `email` varchar(100) DEFAULT NULL,
  `bank_name` varchar(30) DEFAULT NULL,
  `bank_account` varchar(200) DEFAULT NULL,
  `base_salary` varchar(200) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL,
  -- 작(2026-08-13) DB-99 — 사장님: "상태처리 : 재직 휴직 연차"
  -- ⚠️ is_active(재직/퇴사)와 층이 다르다. 이 칸은 '재직 중 지금 어떤 상태냐' 다.
  --    휴직자를 is_active=0 으로 두면 퇴사자와 구분이 안 되고, 1 로 두면 급여가 그대로 나간다.
  `work_status` varchar(20) NOT NULL DEFAULT 'active' COMMENT 'active=재직 · absence=휴직 · leave=연차. DB-99',
  `created_at` datetime(6) NOT NULL,
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL,
  `updated_by` varchar(36) DEFAULT NULL,
  `role` varchar(30) NOT NULL DEFAULT 'sales_user' COMMENT '???? ???? Role',
  `annual_leave_total` decimal(5,1) NOT NULL DEFAULT 0.0 COMMENT '???? ?ο? ???ϼ?',
  `annual_leave_used` decimal(5,1) NOT NULL DEFAULT 0.0 COMMENT '???? ???? ?ϼ?',
  `address` varchar(120) DEFAULT NULL COMMENT '?? (SW_ADDR)',
  `zip_code` varchar(10) DEFAULT NULL COMMENT '???? (SW_POSTNO)',
  `birth_calendar` tinyint(4) DEFAULT 1 COMMENT '1=??, 2=??',
  `birth_lunar_converted` tinyint(4) DEFAULT 0 COMMENT '?? ??',
  `home_phone` varchar(20) DEFAULT NULL COMMENT '??? (SW_TEL)',
  `emergency_contact` varchar(30) DEFAULT NULL COMMENT '????? (SW_TELem)',
  `memo` text DEFAULT NULL COMMENT '?? (SW_REM)',
  `resident_no_encrypted` varbinary(255) DEFAULT NULL COMMENT '???? AES-256 (SW_JUMIN, ???? ?127??164 + 4????)',
  `salary_encrypted` varbinary(255) DEFAULT NULL COMMENT '?? AES-256 (SW_PAY, ????? ?48 + ??????? ?29)',
  `salary_type` tinyint(4) DEFAULT NULL COMMENT '?? ?? (SW_PAYgu)',
  `salary_category` tinyint(4) DEFAULT NULL COMMENT '?? ?? (SW_PAYeuy)',
  `salary_extra_encrypted` varbinary(500) DEFAULT NULL COMMENT '?? ?? AES-256 (SW_PAYoth)',
  `department` varchar(50) DEFAULT NULL COMMENT '?? (SW_BU)',
  `marriage_status` varchar(2) DEFAULT NULL COMMENT '?? ?? (SW_MARRY)',
  `business_type` varchar(50) DEFAULT NULL COMMENT '?? ?? (SW_WORK)',
  `is_resigned` tinyint(4) DEFAULT 0 COMMENT '?? ?? (SW_OUT)',
  `resign_reason` varchar(80) DEFAULT NULL COMMENT '?? ?? (SW_OUTREM)',
  `nationality` varchar(30) DEFAULT NULL COMMENT '?? (SW_NATION)',
  `legacy_bal1` varchar(150) DEFAULT NULL,
  `legacy_bal2` varchar(150) DEFAULT NULL,
  `legacy_bal3` varchar(150) DEFAULT NULL,
  `legacy_bal4` varchar(150) DEFAULT NULL,
  `legacy_bal5` varchar(150) DEFAULT NULL,
  `legacy_bal6` varchar(150) DEFAULT NULL,
  `legacy_bal7` varchar(150) DEFAULT NULL,
  `legacy_bal8` varchar(150) DEFAULT NULL,
  `legacy_bal9` varchar(150) DEFAULT NULL,
  `legacy_bal10` varchar(150) DEFAULT NULL,
  `salary_country` tinyint(4) DEFAULT NULL COMMENT '?? ?? ?? (SW_PAYkuk)',
  PRIMARY KEY (`employee_id`),
  UNIQUE KEY `uq_tenant_empno` (`tenant_id`,`emp_no`),
  KEY `idx_employees_resigned` (`tenant_id`,`is_resigned`),
  KEY `idx_employees_dept` (`tenant_id`,`department`),
  -- 작(2026-08-13) DB-99 — "지금 누가 휴직인가" 를 조직도·급여·연차가 매번 묻는다.
  KEY `idx_emp_work_status` (`tenant_id`,`work_status`)
  -- fk_employees_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `esign_records`
--

DROP TABLE IF EXISTS `esign_records`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `esign_records` (
  `esign_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `user_id` varchar(36) NOT NULL COMMENT '서명한 본인',
  `document_type` varchar(30) NOT NULL COMMENT 'labor_contract / approval / resignation / leave ...',
  `document_id` varchar(36) NOT NULL COMMENT '원본 문서 ID',
  `document_title` varchar(200) DEFAULT NULL COMMENT '문서명 (감사 추적용)',
  `document_hash` varchar(64) NOT NULL COMMENT 'PDF SHA-256 (위변조 방지)',
  `provider` varchar(20) NOT NULL COMMENT 'kakao/toss/finance/pass/manual_upload/handwritten/admin_verified',
  `provider_tx_id` varchar(100) DEFAULT NULL COMMENT '외부 인증 거래 ID (간편인증 4종)',
  `signer_name` varchar(100) NOT NULL,
  `signer_phone_enc` varchar(500) DEFAULT NULL COMMENT '암호화된 휴대폰 번호',
  `signer_birth_enc` varchar(500) DEFAULT NULL COMMENT '암호화된 생년월일',
  `signed_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `ip_address` varchar(50) DEFAULT NULL,
  `user_agent` varchar(500) DEFAULT NULL,
  `device_id` varchar(36) DEFAULT NULL,
  `signature_blob` longblob DEFAULT NULL COMMENT '손글씨 PNG or 수동 업로드 원본',
  `raw_response` longtext DEFAULT NULL COMMENT '인증사 원본 응답 JSON',
  `is_void` tinyint(1) NOT NULL DEFAULT 0 COMMENT '무효화 여부 (hard-delete 금지)',
  `void_reason` varchar(500) DEFAULT NULL,
  `voided_at` datetime(6) DEFAULT NULL,
  `voided_by` varchar(36) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`esign_id`),
  KEY `idx_esign_doc` (`tenant_id`,`document_type`,`document_id`),
  KEY `idx_esign_user` (`user_id`,`signed_at`),
  KEY `idx_esign_provider` (`tenant_id`,`provider`),
  -- fk_esign_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 테이블 삭제에 따른 FK 제거. tenant_id 컬럼 보존
  CONSTRAINT `fk_esign_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='전자서명 이력 — 5년 보관 의무, hard-delete 금지';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `etax_send_history`
--

DROP TABLE IF EXISTS `etax_send_history`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `etax_send_history` (
  `history_id` char(36) NOT NULL,
  `tenant_id` char(36) NOT NULL,
  `tax_invoice_id` char(36) NOT NULL,
  `issue_date` date DEFAULT NULL,
  `sent_at` datetime DEFAULT NULL,
  `nts_read_date` date DEFAULT NULL,
  `nts_report_date` date DEFAULT NULL,
  `nts_approval_no` varchar(50) DEFAULT NULL,
  `nts_response_code` varchar(20) DEFAULT NULL,
  `nts_response_message` varchar(500) DEFAULT NULL,
  `asp_provider` varchar(20) DEFAULT NULL,
  `asp_transaction_id` varchar(100) DEFAULT NULL,
  `status` enum('legacy','pending','sent','approved','rejected','failed','canceled') NOT NULL DEFAULT 'pending',
  `attempt_no` tinyint(3) unsigned NOT NULL DEFAULT 1,
  `is_retry` tinyint(1) NOT NULL DEFAULT 0,
  `raw_request` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL CHECK (json_valid(`raw_request`)),
  `raw_response_encrypted` varbinary(4096) DEFAULT NULL,
  `created_by` char(36) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`history_id`),
  KEY `idx_tenant_invoice` (`tenant_id`,`tax_invoice_id`),
  KEY `idx_status` (`tenant_id`,`status`,`created_at` DESC),
  KEY `idx_sent_date` (`tenant_id`,`sent_at` DESC),
  KEY `idx_asp` (`asp_provider`,`asp_transaction_id`),
  KEY `fk_etax_invoice` (`tax_invoice_id`),
  CONSTRAINT `fk_etax_invoice` FOREIGN KEY (`tax_invoice_id`) REFERENCES `tax_invoices` (`invoice_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `evaluations`
--

DROP TABLE IF EXISTS `evaluations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `evaluations` (
  `eval_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL,
  `eval_period` varchar(20) NOT NULL COMMENT '2025-H1, 2025-H2 ??',
  `eval_grade` varchar(5) NOT NULL COMMENT 'A+/A/B+/B/C ??',
  `score` decimal(5,2) DEFAULT NULL COMMENT '????(0~100)',
  `evaluator_id` varchar(36) DEFAULT NULL COMMENT '?????? employee_id',
  `comment` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`eval_id`),
  KEY `idx_eval_tenant_emp` (`tenant_id`,`employee_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `events`
--

DROP TABLE IF EXISTS `events`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `events` (
  `event_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `event_date` date NOT NULL,
  `title` varchar(200) NOT NULL,
  `memo` varchar(1000) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
  PRIMARY KEY (`event_id`),
  UNIQUE KEY `uq_events_source_hash` (`tenant_id`,`migrated_source_hash`),
  KEY `idx_ev_tenant_date` (`tenant_id`,`event_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='POTHER.CALENDAR 일정/달력 (WS-11 축 5)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `expenses`
--

DROP TABLE IF EXISTS `expenses`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `expenses` (
  `expense_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `expense_date` date NOT NULL,
  `employee_id` varchar(36) NOT NULL,
  `category` varchar(50) NOT NULL,
  `description` varchar(200) NOT NULL,
  `amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `payment_method` varchar(20) NOT NULL DEFAULT 'card',
  `receipt_yn` tinyint(1) NOT NULL DEFAULT 1,
  `approval_status` varchar(20) NOT NULL DEFAULT 'pending',
  `approval_id` varchar(36) DEFAULT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `source_type` varchar(30) DEFAULT NULL,
  `source_id` varchar(80) DEFAULT NULL,
  `migrated_source_hash` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`expense_id`),
  UNIQUE KEY `uq_expenses_source` (`tenant_id`,`source_type`,`source_id`),
  KEY `idx_tenant_date` (`tenant_id`,`expense_date`),
  KEY `idx_tenant_employee` (`tenant_id`,`employee_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `force_edit_logs`
--

DROP TABLE IF EXISTS `force_edit_logs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `force_edit_logs` (
  `log_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `user_id` varchar(36) NOT NULL,
  `table_name` varchar(50) NOT NULL,
  `record_id` varchar(36) NOT NULL,
  `field_name` varchar(50) NOT NULL,
  `before_value` text DEFAULT NULL,
  `after_value` text DEFAULT NULL,
  `reason` varchar(200) DEFAULT NULL,
  `ip_address` varchar(50) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`log_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_record` (`table_name`,`record_id`),
  KEY `idx_created` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `form_templates`
--

DROP TABLE IF EXISTS `form_templates`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `form_templates` (
  `template_id` char(36) NOT NULL,
  `tenant_id` char(36) NOT NULL,
  `form_type` varchar(30) NOT NULL COMMENT 'estimate, sales_order, delivery, purchase_order, receipt, purchase_return, sales_return, tax_invoice',
  `template_name` varchar(100) NOT NULL,
  `paper_mode` varchar(10) NOT NULL DEFAULT 'plain' COMMENT 'plain=A4 순백지 / preprint=양식용지 구입',
  `paper_size` varchar(10) NOT NULL DEFAULT 'A4' COMMENT 'A4·A5·Letter',
  `orientation` varchar(10) NOT NULL DEFAULT 'portrait' COMMENT 'portrait·landscape',
  `margin_top_mm` decimal(5,2) NOT NULL DEFAULT 15.00,
  `margin_left_mm` decimal(5,2) NOT NULL DEFAULT 15.00,
  `margin_right_mm` decimal(5,2) NOT NULL DEFAULT 15.00,
  `margin_bottom_mm` decimal(5,2) NOT NULL DEFAULT 15.00,
  `field_coords_json` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL COMMENT 'preprint 모드 좌표: [{"key":"partner_name","x_mm":30,"y_mm":40,"font_pt":10}]' CHECK (json_valid(`field_coords_json`)),
  `background_image_url` varchar(500) DEFAULT NULL COMMENT '미리보기용 (HITWIN JPG 참조)',
  `show_company_logo` tinyint(1) NOT NULL DEFAULT 1 COMMENT '순백지 모드에서만 적용',
  `show_company_seal` tinyint(1) NOT NULL DEFAULT 1,
  `show_border` tinyint(1) NOT NULL DEFAULT 1 COMMENT '순백지 = 1, 양식용지 = 0',
  `print_copy_mode` varchar(10) NOT NULL DEFAULT 'recipient' COMMENT 'both=공급자+공급받는자 2장 / recipient=공급받는자만 / supplier=공급자만 (DB-90)',
  `style_key` varchar(20) NOT NULL DEFAULT 'basic' COMMENT '디자인 스타일 (DB-90). 4종 확정 전까지 basic 단일',
  `is_default` tinyint(1) NOT NULL DEFAULT 0 COMMENT '1=신규 발행 시 기본 적용',
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3),
  `updated_at` datetime(3) NOT NULL DEFAULT current_timestamp(3) ON UPDATE current_timestamp(3),
  PRIMARY KEY (`template_id`),
  UNIQUE KEY `uk_form_templates_default` (`tenant_id`,`form_type`,`is_default`,`is_active`),
  KEY `idx_form_templates_type` (`tenant_id`,`form_type`,`is_active`,`is_default`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='양식정보설정 — paper_mode 분기 박제 (사장님 헌법 2026-05-31)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `haccp_logs`
--

DROP TABLE IF EXISTS `haccp_logs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `haccp_logs` (
  `haccp_id` bigint(20) NOT NULL AUTO_INCREMENT,
  `tenant_id` varchar(36) NOT NULL,
  `check_date` date NOT NULL,
  `check_type` varchar(30) NOT NULL COMMENT 'temperature/cleanliness/pest/cross_contamination',
  `check_location` varchar(50) DEFAULT NULL,
  `check_value` varchar(50) DEFAULT NULL,
  `pass_fail` varchar(10) NOT NULL DEFAULT 'pass',
  `checker_employee_id` varchar(36) DEFAULT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`haccp_id`),
  KEY `idx_haccp_date` (`tenant_id`,`check_date`),
  KEY `idx_haccp_type` (`tenant_id`,`check_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='HACCP 일일 점검 로그 — INSERT ONLY';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `hitpan_knowledge`
--

DROP TABLE IF EXISTS `hitpan_knowledge`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `hitpan_knowledge` (
  `article_id` varchar(36) NOT NULL,
  `category` varchar(30) NOT NULL COMMENT 'menu_guide/faq/error_fix/workflow/feature',
  `title` varchar(200) NOT NULL,
  `question_keywords` varchar(500) DEFAULT NULL COMMENT '쉼표 구분 검색 키워드',
  `content_markdown` longtext NOT NULL,
  `related_menu_url` varchar(200) DEFAULT NULL COMMENT '클릭 시 이동할 메뉴 경로',
  `source_file` varchar(200) DEFAULT NULL COMMENT '관련 소스 파일 (AI 학습용)',
  `hit_count` int(11) NOT NULL DEFAULT 0,
  `usage_rating` decimal(3,2) NOT NULL DEFAULT 0.00 COMMENT '평균 도움됨 점수',
  `is_public` tinyint(1) NOT NULL DEFAULT 1,
  `created_by` varchar(36) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`article_id`),
  KEY `idx_category` (`category`,`is_public`),
  FULLTEXT KEY `ft_search` (`title`,`question_keywords`,`content_markdown`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='히트판 AI 지식 자산 (우리가 만든 답변 DB)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `hr_expense_requests`
--

DROP TABLE IF EXISTS `hr_expense_requests`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `hr_expense_requests` (
  `request_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL,
  `request_date` date NOT NULL,
  `category` varchar(50) NOT NULL,
  `description` varchar(200) NOT NULL,
  `amount` decimal(15,2) NOT NULL,
  `receipt_url` varchar(500) DEFAULT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'pending',
  `approved_by` varchar(36) DEFAULT NULL,
  `approved_at` datetime(6) DEFAULT NULL,
  `reject_reason` varchar(200) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`request_id`),
  KEY `idx_tenant_emp` (`tenant_id`,`employee_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `labor_policy_settings`
-- 작(2026-08-13) DB-96 — 노무 기준값. 그룹웨어 단계5(연차 엔진)의 토대.
-- 🔴 법정값을 코드에 넣지 않는다(설계도 §0). 법이 바뀌면 이 표의 값만 갈아끼운다.
--    사장님(2026-08-12): "법이라는 게 언제, 어떻게 바뀔지는 몰라. 계속 모니터링 해야 해."
--    값마다 effective_from 을 둬 과거분은 옛 값으로 계산한다.
--

DROP TABLE IF EXISTS `labor_policy_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `labor_policy_settings` (
  `policy_id` varchar(36) NOT NULL COMMENT 'PK',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트(헌법 #2 — JWT 클레임에서만 온다)',
  `policy_key` varchar(60) NOT NULL COMMENT '기준값 열쇠. 예: annual_leave_base_days',
  `policy_value` decimal(12,4) NOT NULL COMMENT '값. 일수·시간·비율 모두 숫자 하나로 담는다',
  `value_unit` varchar(20) NOT NULL DEFAULT 'day' COMMENT '단위: day|hour|rate|count',
  `effective_from` date NOT NULL COMMENT '적용 시작일. 과거분은 옛 값으로 계산한다',
  `effective_to` date DEFAULT NULL COMMENT '적용 종료일. NULL=현재 유효',
  `label` varchar(100) NOT NULL COMMENT '사람이 읽는 이름',
  `description` varchar(300) DEFAULT NULL COMMENT '무엇을 뜻하는 값인지',
  `is_statutory` tinyint(1) NOT NULL DEFAULT 1 COMMENT '1=법정 기준(줄이면 위법) 0=회사 규정',
  `updated_by` varchar(36) DEFAULT NULL,
  `updated_reason` varchar(200) DEFAULT NULL COMMENT '왜 고쳤나 — 반자동은 수정 이력이 필수',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`policy_id`),
  UNIQUE KEY `uk_policy_tenant_key_from` (`tenant_id`,`policy_key`,`effective_from`),
  KEY `idx_policy_lookup` (`tenant_id`,`policy_key`,`effective_from`,`effective_to`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='노무 기준값 — 법이 바뀌면 값만 갈아끼운다. DB-96';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `annual_leave_grants`
-- 작(2026-08-13) DB-97 — 연차 부여 이력. 반자동 3단(제안→수정→확정)의 정본.
-- 🔴 사장님(2026-08-12): "히트판은 100%자동화는 없어. 무조건 반자동이야."
--    수정 가능하면 수정 이력이 필수다 — 누가·언제·뭘·왜.
--

DROP TABLE IF EXISTS `annual_leave_grants`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `annual_leave_grants` (
  `grant_id` varchar(36) NOT NULL COMMENT 'PK',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트(헌법 #2)',
  `employee_id` varchar(36) NOT NULL COMMENT '누구 연차인가',
  `grant_year` smallint(6) NOT NULL COMMENT '귀속 연도',
  `period_start` date NOT NULL COMMENT '이 연차가 쓰이는 기간 시작',
  `period_end` date NOT NULL COMMENT '기간 끝',
  `suggested_days` decimal(5,1) NOT NULL COMMENT '① 자동 계산이 제안한 일수',
  `granted_days` decimal(5,1) NOT NULL COMMENT '② 사람이 확정한 일수',
  `is_adjusted` tinyint(1) NOT NULL DEFAULT 0 COMMENT '제안과 확정이 다른가',
  `adjust_reason` varchar(300) DEFAULT NULL COMMENT '왜 고쳤나 — 고쳤으면 반드시 남긴다',
  `grant_type` varchar(30) NOT NULL DEFAULT 'annual' COMMENT 'annual|monthly|adjust|carryover',
  `service_years` decimal(5,2) DEFAULT NULL COMMENT '계산 당시 근속 연수',
  `calc_basis` varchar(500) DEFAULT NULL COMMENT '어떤 기준값으로 계산했는지',
  `status` varchar(20) NOT NULL DEFAULT 'draft' COMMENT 'draft|confirmed|cancelled',
  `confirmed_by` varchar(36) DEFAULT NULL COMMENT '③ 누가 확정했나',
  `confirmed_at` datetime(6) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`grant_id`),
  UNIQUE KEY `uk_grant_emp_year_type` (`tenant_id`,`employee_id`,`grant_year`,`grant_type`),
  KEY `idx_grant_emp` (`tenant_id`,`employee_id`,`grant_year`),
  KEY `idx_grant_status` (`tenant_id`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='연차 부여 이력 — 제안→수정→확정 3단과 근거를 남긴다. DB-97';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `employee_leave_of_absence`
-- 작(2026-08-13) DB-98 — 휴직(육아·출산·병가 등 장기 부재). 사장님 지시 2026-08-13.
--
-- 🔴 휴가(`leave_requests`)와 왜 나눴나 — 실측 근거 두 가지다.
--   ① `leave_requests.leave_days` 는 decimal(3,1) = 최대 99.9일.
--      육아휴직 1년 6개월(548일)을 넣으면 MySQL 이 거부한다
--      (실측: ERROR 1264 Out of range value for column 'leave_days').
--   ② 휴가가 승인되면 LeaveBalanceHelper 가 annual_leave_used 를 더한다.
--      휴직을 휴가로 올리면 연차 잔여가 마이너스가 되어 복직 후 연차를 못 쓴다.
--
-- 🔴 사장님(2026-08-13): "휴직도 수동으로!!!!" / "상태처리, 상태확인 정도로만".
--    기간은 사람이 넣고, 기준을 넘겨도 막지 않고 알려만 준다.
--    법정 기간은 '최소 보장' 이라 회사가 더 주는 것은 위법이 아니다.
--

DROP TABLE IF EXISTS `employee_leave_of_absence`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `employee_leave_of_absence` (
  `absence_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL,
  `absence_type` varchar(30) NOT NULL DEFAULT 'other' COMMENT '휴직 종류. 화면에서 고르지 않는다(사장님: 사유를 글로 받는다) — 기본 other. DB-102',
  `absence_label` varchar(60) DEFAULT NULL COMMENT '회사가 부르는 이름',
  `start_date` date NOT NULL,
  `end_date` date NOT NULL,
  `actual_return_date` date DEFAULT NULL COMMENT '실제 복직일 — 예정과 다를 수 있어 따로 남긴다',
  `status` varchar(20) NOT NULL DEFAULT 'draft' COMMENT 'draft/pending/approved/active/returned/rejected/cancelled',
  `reason` varchar(500) DEFAULT NULL,
  `exceeds_standard` tinyint(1) NOT NULL DEFAULT 0 COMMENT '1=회사·법정 기준을 넘김',
  `exceed_reason` varchar(300) DEFAULT NULL COMMENT '넘겼는데 왜 승인했나',
  `calc_basis` varchar(500) DEFAULT NULL COMMENT '판정 근거를 글로 남긴다',
  `pay_type` varchar(20) NOT NULL DEFAULT 'unpaid' COMMENT 'unpaid/paid/partial (표기용. 정본은 pay_amount)',
  `pay_amount` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '휴직 중 지급 금액. 사람이 직접 넣는다. 0=무급. DB-99',
  `pay_note` varchar(200) DEFAULT NULL COMMENT '급여 메모. DB-99',
  `approval_id` varchar(36) DEFAULT NULL,
  `approved_by` varchar(36) DEFAULT NULL,
  `approved_at` datetime(6) DEFAULT NULL,
  `reject_reason` varchar(500) DEFAULT NULL COMMENT '폭 500 — 단계3 P0-2(ERROR 1406) 재발 방지',
  `created_by` varchar(36) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`absence_id`),
  KEY `idx_absence_tenant_emp` (`tenant_id`,`employee_id`,`start_date`),
  KEY `idx_absence_status` (`tenant_id`,`status`),
  KEY `idx_absence_period` (`tenant_id`,`status`,`start_date`,`end_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='휴직 — 기간이 있는 부재. 휴가와 다르다. DB-98';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `hr_reports`
-- 작(2026-08-13) DB-92 — 업무보고서 4종(일일·주간·월간·경위서). 사장님 지시 2026-08-12.
-- 🔴 마이그(DB-92)와 출하 DDL 을 함께 고쳐야 한다(헌법 #36). 한쪽만 하면
--    기존 고객사와 신규 설치 고객사의 스키마가 갈린다.
--

DROP TABLE IF EXISTS `hr_reports`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `hr_reports` (
  `report_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL COMMENT '사원 기준(설계서 §3-5 축 확정) — 계정 없어도 작성 가능',
  `report_type` varchar(20) NOT NULL COMMENT 'daily/weekly/monthly/incident',
  `period_start` date NOT NULL,
  `period_end` date NOT NULL,
  `title` varchar(200) NOT NULL,
  `content` text NOT NULL COMMENT 'text — varchar 로는 월간보고서·경위서가 잘린다',
  `cause` text DEFAULT NULL COMMENT '경위서 전용 — 원인',
  `action_plan` text DEFAULT NULL COMMENT '경위서 전용 — 재발방지 대책',
  `status` varchar(20) NOT NULL DEFAULT 'draft' COMMENT 'draft/pending/approved/rejected',
  `submitted_at` datetime(6) DEFAULT NULL,
  `approved_by` varchar(36) DEFAULT NULL,
  `approved_at` datetime(6) DEFAULT NULL,
  `reject_reason` varchar(200) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`report_id`),
  KEY `idx_hr_reports_tenant_emp` (`tenant_id`,`employee_id`,`report_type`,`period_start`),
  KEY `idx_hr_reports_tenant_period` (`tenant_id`,`report_type`,`period_start`),
  KEY `idx_hr_reports_tenant_status` (`tenant_id`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `idempotency_keys`
--

DROP TABLE IF EXISTS `idempotency_keys`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `idempotency_keys` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `tenant_id` varchar(36) NOT NULL,
  `idempotency_key` varchar(64) NOT NULL,
  `endpoint` varchar(128) NOT NULL COMMENT 'METHOD + path, 예: POST /api/sales/tax-invoices',
  `request_hash` char(64) NOT NULL COMMENT 'SHA-256 of request body — 같은 키+다른 본문 차단',
  `status_code` int(11) NOT NULL COMMENT '캐시된 HTTP 상태 코드',
  `response_body` mediumtext NOT NULL COMMENT '캐시된 응답 JSON (민감 시 액션에서 SkipCacheBody)',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `expires_at` datetime(6) NOT NULL COMMENT 'TTL 24h, IdempotencyCleanupService가 1h 주기 정리',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_tenant_key` (`tenant_id`,`idempotency_key`),
  KEY `idx_expires` (`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Idempotency-Key 헤더 처리 캐시 (DESIGN_PRINCIPLES §5.3)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `inventory_lots`
--

DROP TABLE IF EXISTS `inventory_lots`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `inventory_lots` (
  `lot_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `warehouse_id` varchar(36) NOT NULL,
  `lot_no` varchar(50) NOT NULL,
  `manufacture_date` date NOT NULL,
  `expiry_date` date NOT NULL,
  `initial_qty` decimal(15,3) NOT NULL,
  `current_qty` decimal(15,3) NOT NULL,
  `origin_country` varchar(30) DEFAULT 'KR',
  `supplier_partner_id` varchar(36) DEFAULT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'active' COMMENT 'active/expired/depleted/recalled',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`lot_id`),
  UNIQUE KEY `uq_lot_tenant_no` (`tenant_id`,`lot_no`),
  KEY `idx_lot_item` (`tenant_id`,`item_id`),
  KEY `idx_lot_expiry` (`tenant_id`,`expiry_date`,`status`),
  KEY `fk_lot_item` (`item_id`),
  CONSTRAINT `fk_lot_item` FOREIGN KEY (`item_id`) REFERENCES `items` (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='식품 로트 관리 (유통기한·원산지·HACCP)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `item_groups`
--

DROP TABLE IF EXISTS `item_groups`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `item_groups` (
  `group_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `group_name` varchar(50) NOT NULL,
  `sort_order` int(11) NOT NULL DEFAULT 0,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`group_id`),
  UNIQUE KEY `uk_tenant_group` (`tenant_id`,`group_name`),
  KEY `idx_tenant` (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `item_special_prices`
--

DROP TABLE IF EXISTS `item_special_prices`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `item_special_prices` (
  `price_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `partner_id` varchar(36) NOT NULL,
  `price_type` varchar(20) NOT NULL DEFAULT 'fixed',
  `unit_price` decimal(15,2) NOT NULL,
  -- 봉합 (2026-06-23, 17차 P0-1): 코드(ItemService.UpsertSpecialPrice)가 SELECT/INSERT/UPDATE 하는
  --   discount_rate 컬럼이 clean DDL에 누락돼 신규설치 DB에서 상품 특별단가 '할인율' 모드 저장 시
  --   "Unknown column 'discount_rate'" 500 (기능 DOA, 헌법 #36). 할인모드만 값, 고정모드는 NULL(코드 decimal?).
  `discount_rate` decimal(5,2) DEFAULT NULL COMMENT '할인율(%) — price_type=discount 일 때만, 고정모드는 NULL (17차 P0-1)',
  `start_date` date DEFAULT NULL,
  `end_date` date DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`price_id`),
  UNIQUE KEY `uk_item_partner` (`tenant_id`,`item_id`,`partner_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_item` (`tenant_id`,`item_id`),
  KEY `idx_partner` (`tenant_id`,`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `item_specs`
--

DROP TABLE IF EXISTS `item_specs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `item_specs` (
  `spec_id` char(36) NOT NULL COMMENT 'UUID',
  `tenant_id` char(36) NOT NULL,
  `item_id` char(36) NOT NULL,
  `spec_value` varchar(100) NOT NULL COMMENT '예: 100×200×3mm, 1.0T, M8×30',
  `display_order` int(11) NOT NULL DEFAULT 0 COMMENT '콤보박스 정렬 순서',
  `is_default` tinyint(1) NOT NULL DEFAULT 0 COMMENT '1=신규 라인 기본 선택',
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3),
  `updated_at` datetime(3) NOT NULL DEFAULT current_timestamp(3) ON UPDATE current_timestamp(3),
  PRIMARY KEY (`spec_id`),
  UNIQUE KEY `uk_item_specs_value` (`tenant_id`,`item_id`,`spec_value`),
  KEY `idx_item_specs_item` (`tenant_id`,`item_id`,`is_active`,`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='상품 규격 1:N (그리드 콤보박스 옵션)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `item_stock`
--

DROP TABLE IF EXISTS `item_stock`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `item_stock` (
  `stock_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `warehouse_id` varchar(36) NOT NULL DEFAULT 'default',
  `current_qty` decimal(10,2) NOT NULL DEFAULT 0.00,
  `avg_cost` decimal(15,2) NOT NULL DEFAULT 0.00,
  `last_updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`stock_id`),
  UNIQUE KEY `uk_tenant_item_wh` (`tenant_id`,`item_id`,`warehouse_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_low_stock` (`tenant_id`,`current_qty`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `items`
--

DROP TABLE IF EXISTS `items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `items` (
  `item_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `item_code` varchar(30) NOT NULL,
  `item_name` varchar(100) NOT NULL,
  `item_type` longtext NOT NULL,
  `category_id` varchar(36) DEFAULT NULL,
  `unit` varchar(10) NOT NULL,
  `std_price` decimal(15,2) DEFAULT NULL,
  `price_a` decimal(15,2) DEFAULT NULL,
  `price_b` decimal(15,2) DEFAULT NULL,
  `price_c` decimal(15,2) DEFAULT NULL,
  `price_d` decimal(15,2) DEFAULT NULL,
  `price_e` decimal(15,2) DEFAULT NULL,
  `cost_price` decimal(15,2) DEFAULT NULL,
  `tax_type` varchar(20) NOT NULL DEFAULT 'taxable',
  `safe_stock` decimal(15,3) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL,
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL,
  `updated_by` varchar(36) DEFAULT NULL,
  `is_deleted` tinyint(1) NOT NULL DEFAULT 0,
  `deleted_at` datetime(6) DEFAULT NULL,
  `item_group` varchar(50) DEFAULT NULL,
  `spec` varchar(100) DEFAULT NULL,
  `purchase_price` decimal(15,2) NOT NULL DEFAULT 0.00,
  `sale_price` decimal(15,2) NOT NULL DEFAULT 0.00,
  `standard_price` decimal(15,2) NOT NULL DEFAULT 0.00,
  `safety_stock` decimal(10,2) NOT NULL DEFAULT 0.00,
  `barcode` varchar(50) DEFAULT NULL,
  `row_version` int(11) NOT NULL DEFAULT 0,
  `auto_order_enabled` tinyint(1) NOT NULL DEFAULT 0,
  `auto_order_partner_id` varchar(36) DEFAULT NULL,
  `auto_order_qty` decimal(10,2) NOT NULL DEFAULT 0.00,
  `auto_receive_on_order` tinyint(1) NOT NULL DEFAULT 0,
  `spec_detail` varchar(80) DEFAULT NULL COMMENT '?? ?? (S_SPEC)',
  `unit_secondary` varchar(10) DEFAULT NULL COMMENT '2? ?? (S_UNIT2)',
  `reorder_point` decimal(15,3) DEFAULT 0.000 COMMENT '??? ?? (S_REORD)',
  `supplier_default_id` char(36) DEFAULT NULL COMMENT '?? ??? (S_VENDOR FK)',
  `default_warehouse_id` varchar(36) DEFAULT NULL COMMENT 'DB-105 item default warehouse',
  PRIMARY KEY (`item_id`),
  UNIQUE KEY `uq_tenant_code` (`tenant_id`,`item_code`),
  KEY `idx_tenant_name` (`tenant_id`,`item_name`),
  KEY `idx_tenant_active` (`tenant_id`,`is_active`),
  KEY `idx_items_supplier` (`tenant_id`,`supplier_default_id`),
  KEY `idx_items_default_wh` (`tenant_id`,`default_warehouse_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `journal_entries`
--

DROP TABLE IF EXISTS `journal_entries`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `journal_entries` (
  `entry_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `entry_no` varchar(32) NOT NULL,
  `entry_date` date NOT NULL,
  `ym` varchar(7) NOT NULL,
  `description` varchar(200) NOT NULL,
  `source_type` varchar(30) NOT NULL DEFAULT 'manual' COMMENT 'sales/sales_cancel/sales_delivery_cancel/sales_return_cancel/purchase/purchase_return/purchase_return_cancel/bom_production/bom_disassemble/collection/payment/manual/adjustment/migration (16차 P3 정합)',
  `source_id` varchar(36) DEFAULT NULL,
  `is_confirmed` tinyint(1) NOT NULL DEFAULT 0,
  `confirmed_at` datetime(6) DEFAULT NULL,
  `confirmed_by` varchar(36) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  PRIMARY KEY (`entry_id`),
  UNIQUE KEY `uq_je_tenant_entryno` (`tenant_id`,`entry_no`),
  UNIQUE KEY `uq_je_source` (`tenant_id`,`source_type`,`source_id`),
  KEY `idx_je_tenant_date` (`tenant_id`,`entry_date`),
  KEY `idx_je_ym` (`tenant_id`,`ym`),
  KEY `idx_je_source` (`source_type`,`source_id`)
  -- fk_journal_entries_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='복식부기 분개 헤더 — INSERT ONLY (updated_at 없음)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `journal_lines`
--

DROP TABLE IF EXISTS `journal_lines`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `journal_lines` (
  `line_id` bigint(20) NOT NULL AUTO_INCREMENT,
  `entry_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `account_code` varchar(10) NOT NULL,
  `debit_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `credit_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `partner_id` varchar(36) DEFAULT NULL,
  `memo` varchar(200) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `source_id` varchar(80) DEFAULT NULL COMMENT 'WS-F: 라인 멱등 키 (entry source_id + SC_KCODE + SUN)',
  PRIMARY KEY (`line_id`),
  UNIQUE KEY `uq_jl_source` (`tenant_id`,`source_id`),
  KEY `idx_jl_entry` (`entry_id`),
  KEY `idx_jl_tenant_account` (`tenant_id`,`account_code`),
  KEY `idx_jl_partner` (`tenant_id`,`partner_id`),
  KEY `idx_jl_entry_tenant` (`tenant_id`,`entry_id`),
  CONSTRAINT `fk_jl_account` FOREIGN KEY (`tenant_id`, `account_code`) REFERENCES `accounts` (`tenant_id`, `account_code`),
  CONSTRAINT `fk_jl_entry` FOREIGN KEY (`entry_id`) REFERENCES `journal_entries` (`entry_id`),
  CONSTRAINT `chk_jl_debit_or_credit` CHECK (`debit_amount` > 0 and `credit_amount` = 0 or `debit_amount` = 0 and `credit_amount` > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='복식부기 분개 라인 — INSERT ONLY, CHECK로 차변·대변 배타';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `labor_contracts`
--

DROP TABLE IF EXISTS `labor_contracts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `labor_contracts` (
  `contract_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL,
  `employee_name` varchar(100) NOT NULL,
  `contract_type` varchar(20) NOT NULL DEFAULT 'regular' COMMENT 'regular/contract/part_time/daily',
  `start_date` date NOT NULL,
  `end_date` date DEFAULT NULL COMMENT '정규직은 NULL',
  `work_place` varchar(200) DEFAULT NULL,
  `job_description` varchar(500) DEFAULT NULL,
  `working_hours` varchar(100) DEFAULT NULL COMMENT '예: 09:00~18:00 주5일',
  `salary_amount` decimal(15,2) DEFAULT NULL,
  `salary_type` varchar(20) DEFAULT NULL COMMENT 'monthly/hourly/daily/annual',
  `pay_day` varchar(20) DEFAULT NULL COMMENT '급여일',
  `social_insurance` varchar(100) DEFAULT NULL COMMENT '4대보험 적용 여부',
  `annual_leave` varchar(100) DEFAULT NULL,
  `extra_terms` text DEFAULT NULL COMMENT '특약 사항',
  `status` varchar(20) NOT NULL DEFAULT 'draft' COMMENT 'draft/sent/signed/voided',
  `sent_at` datetime(6) DEFAULT NULL,
  `signed_at` datetime(6) DEFAULT NULL,
  `esign_id` varchar(36) DEFAULT NULL COMMENT 'esign_records FK',
  `pdf_blob` longblob DEFAULT NULL COMMENT '서명 완료 PDF',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) NOT NULL,
  PRIMARY KEY (`contract_id`),
  KEY `idx_lc_tenant_emp` (`tenant_id`,`employee_id`),
  KEY `idx_lc_tenant_status` (`tenant_id`,`status`)
  -- fk_lc_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='전자근로계약서';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `resignation_letters`  (DB-106)
--
-- 입사(labor_contracts) 바로 옆에 둔다 — 「입사/퇴사」 한 메뉴의 두 문서다.
-- 🔴 2026-08-24 봉합: 이 표가 clean DDL 에 없어 신규 고객은 표 자체가 없었다(헌법 #36 위반).
--    같은 날 번호 충돌(DB-100 중복)로 기존 고객도 못 받았다 — 결함이 둘이었고 DDL Smoke 가 둘째를 잡았다.
--

DROP TABLE IF EXISTS `resignation_letters`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `resignation_letters` (
  `resignation_id` varchar(36) NOT NULL COMMENT '사직서 ID',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트 (JWT 클레임에서만 — 헌법 #2)',
  `employee_id` varchar(36) NOT NULL COMMENT '사직하는 사원',
  `employee_name` varchar(100) NOT NULL COMMENT '작성 시점 이름 (사원명이 바뀌어도 문서는 그대로여야 한다)',
  `dept_name` varchar(100) DEFAULT NULL COMMENT '작성 시점 부서 (같은 이유로 문자열로 넣는다)',
  `position_name` varchar(50) DEFAULT NULL COMMENT '작성 시점 직급',
  `resign_type` varchar(20) NOT NULL DEFAULT 'voluntary' COMMENT '자발(voluntary) · 권고사직(recommended) · 계약만료(expired) · 정년(retirement)',
  `desired_date` date NOT NULL COMMENT '희망 퇴사일 (직원이 적는 날)',
  `actual_date` date DEFAULT NULL COMMENT '실제 퇴사일 (회사가 수리하며 정한 날 — 다를 수 있다)',
  `reason` varchar(500) DEFAULT NULL COMMENT '사직 사유',
  `handover_to` varchar(100) DEFAULT NULL COMMENT '인수인계 받을 사람',
  `handover_note` text DEFAULT NULL COMMENT '인수인계 내용',
  `return_items` varchar(500) DEFAULT NULL COMMENT '반납물 (사원증·노트북·차량 등)',
  `status` varchar(20) NOT NULL DEFAULT 'draft' COMMENT 'draft · pending · approved · rejected · completed · withdrawn',
  `approval_id` varchar(36) DEFAULT NULL COMMENT 'approval_documents.approval_id — 결재는 그쪽이 돈다',
  `submitted_at` datetime(6) DEFAULT NULL COMMENT '제출(상신) 시각',
  `approved_at` datetime(6) DEFAULT NULL COMMENT '수리 시각',
  `reject_reason` varchar(500) DEFAULT NULL COMMENT '반려 사유',
  `signed_at` datetime(6) DEFAULT NULL COMMENT '직원이 서명한 시각',
  `signature_data` longtext DEFAULT NULL COMMENT '서명 이미지 (data URI)',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(36) DEFAULT NULL,
  PRIMARY KEY (`resignation_id`),
  KEY `idx_tenant_emp` (`tenant_id`,`employee_id`),
  KEY `idx_tenant_status` (`tenant_id`,`status`),
  KEY `idx_approval` (`approval_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='전자 퇴직서(사직서) — 직원이 올리는 문서. 관리자 퇴사처리(employees)와 다른 자리다. DB-106';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `leave_requests`
--

DROP TABLE IF EXISTS `leave_requests`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `leave_requests` (
  `request_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL,
  `leave_type` varchar(20) NOT NULL DEFAULT 'annual' COMMENT '????/????/????/????',
  `leave_days` decimal(3,1) NOT NULL DEFAULT 1.0 COMMENT '?????ϼ?(????=0.5)',
  `start_date` date NOT NULL,
  `end_date` date NOT NULL,
  `reason` varchar(200) DEFAULT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'pending' COMMENT 'pending/approved/rejected',
  `approved_by` varchar(36) DEFAULT NULL COMMENT '?????? employee_id',
  `approved_at` datetime(6) DEFAULT NULL,
  `reject_reason` varchar(200) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`request_id`),
  KEY `idx_leave_tenant_emp` (`tenant_id`,`employee_id`),
  KEY `idx_leave_status` (`tenant_id`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `ledger_balance_snapshot`
--

DROP TABLE IF EXISTS `ledger_balance_snapshot`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `ledger_balance_snapshot` (
  `snapshot_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `year_month` char(6) NOT NULL COMMENT 'YYYYMM',
  `account_code` varchar(40) NOT NULL,
  `ending_balance` decimal(15,2) NOT NULL DEFAULT 0.00,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`snapshot_id`),
  UNIQUE KEY `uk_ledger_snap` (`tenant_id`,`year_month`,`account_code`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_year_month` (`tenant_id`,`year_month`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `local_company`
--

DROP TABLE IF EXISTS `local_company`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `local_company` (
  `tenant_id` varchar(36) NOT NULL COMMENT 'tenants.tenant_id 와 동일값 박제',
  `tenant_code` varchar(20) NOT NULL,
  `company_name` varchar(100) NOT NULL,
  `biz_no` varchar(12) DEFAULT NULL,
  `ceo_name` varchar(50) DEFAULT NULL,
  `biz_type` varchar(50) DEFAULT NULL,
  `biz_item` varchar(100) DEFAULT NULL,
  `tel` varchar(20) DEFAULT NULL,
  `fax` varchar(20) DEFAULT NULL,
  `address` varchar(200) DEFAULT NULL,
  `address_detail` varchar(200) DEFAULT NULL COMMENT '상세주소(동·호수 등). DB-85 — 기본주소와 분리 저장',
  `zip_code` varchar(10) DEFAULT NULL,
  `email` varchar(100) DEFAULT NULL,
  `logo_url` varchar(200) DEFAULT NULL,
  `seal_url` varchar(200) DEFAULT NULL COMMENT '인장 이미지 경로. DB-85 — 거래명세서·견적서 출력용',
  `header_url` varchar(200) DEFAULT NULL COMMENT '출력 헤더 이미지 경로. DB-85',
  `corp_no` varchar(20) DEFAULT NULL,
  `regular_employee_count` int(11) DEFAULT NULL COMMENT '상시근로자수. NULL=미정 — 자동계산 금지(연인원/가동일수, 사람이 확정). DB-95',
  `business_entity_type` varchar(20) DEFAULT NULL COMMENT '법인/개인 구분: corporate|individual. NULL=미정. DB-95',
  `employee_count_asof` date DEFAULT NULL COMMENT '상시근로자수 기준일. 이 숫자가 언제 기준인지. DB-95',
  `subsidiary_no` varchar(20) DEFAULT NULL,
  `homepage` varchar(200) DEFAULT NULL,
  `initial_date` date DEFAULT NULL,
  `e_invoice_server` varchar(100) DEFAULT NULL,
  `e_invoice_id` varchar(100) DEFAULT NULL,
  `e_invoice_enabled` tinyint(1) NOT NULL DEFAULT 0,
  `tax_type` varchar(20) NOT NULL DEFAULT 'taxable',
  `fiscal_month` int(11) NOT NULL DEFAULT 12,
  `is_locked_from_landing` tinyint(1) NOT NULL DEFAULT 0 COMMENT '랜딩 가입 자동 반영 잠금',
  `bootstrap_at` datetime DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`tenant_id`),
  UNIQUE KEY `uq_local_company_code` (`tenant_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `local_subscription`
--

DROP TABLE IF EXISTS `local_subscription`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `local_subscription` (
  `tenant_id` char(36) NOT NULL,
  `subscription_tier` varchar(20) NOT NULL DEFAULT 'basic' COMMENT 'basic / pro / enterprise',
  `status` varchar(20) NOT NULL DEFAULT 'active' COMMENT 'active / suspended / trial / inactive',
  `trial_ends_at` datetime DEFAULT NULL,
  `ai_mode` varchar(20) NOT NULL DEFAULT 'hitpan_pool' COMMENT 'hitpan_pool / byok / hybrid',
  `ai_token_monthly_limit` int(11) NOT NULL DEFAULT 100000,
  `ai_token_extra` int(11) NOT NULL DEFAULT 0,
  `anthropic_api_key_encrypted` varchar(512) DEFAULT NULL COMMENT 'BYOK 모드 AES-256 암호화',
  `anthropic_api_key_last4` varchar(8) DEFAULT NULL,
  `anthropic_key_status` varchar(20) NOT NULL DEFAULT 'none',
  `anthropic_key_saved_at` datetime DEFAULT NULL COMMENT 'BYOK 키 저장 시각 (P0 봉합 2026-06-23: ChatbotService SaveApiKey/GetAiSettings 사용)',
  `anthropic_key_verified_at` datetime DEFAULT NULL COMMENT 'BYOK 키 연결확인 시각 (P0 봉합 2026-06-23)',
  `openai_api_key_encrypted` varchar(512) DEFAULT NULL COMMENT 'BYOK 챗GPT 키 AES-256 암호화 (DB-91)',
  `openai_api_key_last4` varchar(8) DEFAULT NULL COMMENT '챗GPT 키 마지막 4자리 (UI 표시용)',
  `openai_key_status` varchar(20) NOT NULL DEFAULT 'none' COMMENT 'none / valid / invalid / expired',
  `openai_key_saved_at` datetime DEFAULT NULL COMMENT '챗GPT 키 저장 시각',
  `openai_key_verified_at` datetime DEFAULT NULL COMMENT '챗GPT 키 연결확인 시각 (실제 외부 호출 성공 시각)',
  `google_api_key_encrypted` varchar(512) DEFAULT NULL COMMENT 'BYOK 제미나이 키 AES-256 암호화 (DB-91)',
  `google_api_key_last4` varchar(8) DEFAULT NULL COMMENT '제미나이 키 마지막 4자리 (UI 표시용)',
  `google_key_status` varchar(20) NOT NULL DEFAULT 'none' COMMENT 'none / valid / invalid / expired',
  `google_key_saved_at` datetime DEFAULT NULL COMMENT '제미나이 키 저장 시각',
  `google_key_verified_at` datetime DEFAULT NULL COMMENT '제미나이 키 연결확인 시각 (실제 외부 호출 성공 시각)',
  `ai_provider` varchar(20) NOT NULL DEFAULT 'anthropic' COMMENT '현재 사용 AI 공급자: anthropic(클로드AI) / openai(챗GPT) / google(제미나이)',
  `max_users` tinyint(3) unsigned NOT NULL DEFAULT 3,
  `extra_device_slots` int(11) NOT NULL DEFAULT 0,
  `reseller_id` varchar(36) DEFAULT NULL,
  `reseller_tier` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `last_sync_at` datetime(6) DEFAULT NULL COMMENT '백오피스→ERP 마지막 동기화 시각',
  `sync_source` varchar(64) NOT NULL DEFAULT 'bootstrap',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`tenant_id`),
  KEY `idx_local_sub_status` (`status`),
  KEY `idx_local_sub_reseller` (`reseller_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `local_update_consents`
--   업데이트 동의(고리2) 고객 PC 로컬 자가완결 — 헌법 #30·#36. 출처: DB-82.
--

DROP TABLE IF EXISTS `local_update_consents`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `local_update_consents` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `tenant_id` varchar(36) NOT NULL COMMENT '로컬 테넌트 식별자(local_company.tenant_id 와 동일값)',
  `user_id` varchar(64) NOT NULL COMMENT '동의한 ERP 로컬 사용자 식별자',
  `update_version` varchar(20) NOT NULL COMMENT '동의 대상 업데이트 버전',
  `action` enum('approve','reject') NOT NULL COMMENT '고리2 Y/N 동의 결과',
  `consented_at` datetime(3) NOT NULL COMMENT '고객이 ERP에서 동의/거부한 시각',
  `created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3) COMMENT '로컬 적재 시각',
  PRIMARY KEY (`id`),
  KEY `idx_local_update_consents_version` (`update_version`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `local_update_status`
--   로컬 새버전 상태(고리2) — 워치독 발견→ERP 로그인 팝업, 고객 PC 로컬 자가완결. 헌법 #30·#36. 출처: DB-83.
--

DROP TABLE IF EXISTS `local_update_status`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `local_update_status` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `tenant_id` varchar(36) DEFAULT NULL COMMENT '로컬 테넌트 식별자(local_company.tenant_id). 워치독 단일 테넌트라 NULL 허용',
  `latest_version` varchar(20) NOT NULL COMMENT '워치독이 발견한 새버전(SemVer Major.Minor.Build)',
  `update_channel` varchar(20) NOT NULL DEFAULT 'Major' COMMENT '업데이트 채널(Major/Normal/Emergency). 로그인 팝업은 Major 위주',
  `consent_message` text DEFAULT NULL COMMENT 'Major 동의 팝업 안내 문구(manifest.ConsentMessage)',
  `download_url` varchar(500) DEFAULT NULL COMMENT 'manifest.DownloadUrl(참고용 — 적용은 워치독)',
  `requires_migration` tinyint(1) NOT NULL DEFAULT 0 COMMENT 'DB 스키마 마이그 동반 여부(manifest.RequiresMigration)',
  `discovered_at` datetime(3) NOT NULL COMMENT '워치독이 새버전을 발견·적재한 시각',
  `created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3) COMMENT '로컬 적재 시각',
  PRIMARY KEY (`id`),
  KEY `idx_local_update_status_discovered` (`discovered_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `local_update_apply_status`
--   로컬 업데이트 "적용 결과"(고리4 §W4-6) — 워치독이 새버전을 실제 적용(교체·재시작·롤백)한 뒤 결과 기록.
--   local_update_status(새버전 "발견" 적재)와 역할 분리. 버전당 1행 UPSERT(멱등키=applied_version). 헌법 #30·#36.
--

DROP TABLE IF EXISTS `local_update_apply_status`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `local_update_apply_status` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `tenant_id` varchar(36) DEFAULT NULL COMMENT '로컬 테넌트 식별자(local_company.tenant_id). 워치독 단일 테넌트라 NULL 허용',
  `applied_version` varchar(20) NOT NULL COMMENT '적용 시도한 버전(SemVer Major.Minor.Build). 멱등키 — 버전당 1행 UPSERT',
  `result` varchar(20) NOT NULL COMMENT '적용 결과: success|rolled_back|rollback_failed|blocked(마이그게이트 차단 등)',
  `detail` text DEFAULT NULL COMMENT '실패 사유·CS 안내 등 상세(선택)',
  `applied_at` datetime(3) NOT NULL COMMENT '적용 완료/실패 확정 시각',
  `created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3) COMMENT '로컬 적재 시각',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_local_update_apply_version` (`applied_version`),
  KEY `idx_local_update_apply_at` (`applied_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `login_attempts`
--

DROP TABLE IF EXISTS `login_attempts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `login_attempts` (
  `attempt_id` varchar(36) NOT NULL,
  `email` varchar(200) NOT NULL,
  `ip_address` varchar(50) NOT NULL,
  `is_success` tinyint(1) NOT NULL DEFAULT 0,
  `attempted_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`attempt_id`),
  KEY `idx_email_ip` (`email`,`ip_address`),
  KEY `idx_attempted` (`attempted_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `material_price_history`
--

DROP TABLE IF EXISTS `material_price_history`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `material_price_history` (
  `history_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `ym` varchar(7) NOT NULL COMMENT 'YYYY-MM',
  `avg_price` decimal(15,2) NOT NULL,
  `min_price` decimal(15,2) DEFAULT NULL,
  `max_price` decimal(15,2) DEFAULT NULL,
  `change_rate` decimal(6,2) DEFAULT NULL COMMENT '전월 대비 변동률(%)',
  `source` varchar(30) DEFAULT 'auto' COMMENT 'auto/manual/market_feed',
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`history_id`),
  UNIQUE KEY `uq_mph_tenant_item_ym` (`tenant_id`,`item_id`,`ym`),
  KEY `idx_mph_tenant_item` (`tenant_id`,`item_id`),
  KEY `fk_mph_item` (`item_id`),
  CONSTRAINT `fk_mph_item` FOREIGN KEY (`item_id`) REFERENCES `items` (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='원자재 단가 월별 변동 이력 (반도체 등락 추적)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `migration_checkpoints`
--

DROP TABLE IF EXISTS `migration_checkpoints`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `migration_checkpoints` (
  `checkpoint_id` char(36) NOT NULL,
  `job_id` char(36) NOT NULL,
  `tenant_id` char(36) NOT NULL,
  `mdb_file` varchar(50) NOT NULL,
  `table_name` varchar(50) NOT NULL,
  `table_order` smallint(5) unsigned NOT NULL,
  `status` enum('pending','running','done','failed','skipped') NOT NULL DEFAULT 'pending',
  `total_rows` int(10) unsigned DEFAULT 0,
  `processed_count` int(10) unsigned DEFAULT 0,
  `last_pk_value` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL CHECK (json_valid(`last_pk_value`)),
  `chunk_size` smallint(5) unsigned DEFAULT 1000,
  `started_at` datetime DEFAULT NULL,
  `completed_at` datetime DEFAULT NULL,
  `avg_commit_ms` int(10) unsigned DEFAULT 0,
  `last_error` text DEFAULT NULL,
  `retry_count` tinyint(3) unsigned DEFAULT 0,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`checkpoint_id`),
  UNIQUE KEY `uk_job_table` (`job_id`,`table_name`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_chkpt_pending` (`job_id`,`status`,`table_order`),
  CONSTRAINT `fk_checkpoint_job` FOREIGN KEY (`job_id`) REFERENCES `migration_jobs` (`job_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `migration_errors`
--

DROP TABLE IF EXISTS `migration_errors`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `migration_errors` (
  `error_id` char(36) NOT NULL,
  `job_id` char(36) NOT NULL,
  `tenant_id` char(36) NOT NULL,
  `checkpoint_id` char(36) DEFAULT NULL,
  `mdb_file` varchar(50) NOT NULL,
  `table_name` varchar(50) NOT NULL,
  `row_pk_value` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL CHECK (json_valid(`row_pk_value`)),
  `row_offset` int(10) unsigned DEFAULT NULL,
  `error_type` enum('encoding','fk_missing','duplicate','schema','constraint','timeout','other') NOT NULL,
  `error_severity` enum('warning','error','critical') NOT NULL DEFAULT 'error',
  `error_code` varchar(20) DEFAULT NULL,
  `error_message` longblob NOT NULL,
  `error_detail` longblob DEFAULT NULL,
  `raw_data` longblob DEFAULT NULL COMMENT 'AES-256-CBC ??ȣȭ ???̱? ???? row ???? (???? #5)',
  `is_resolved` tinyint(3) unsigned DEFAULT 0,
  `resolved_at` datetime DEFAULT NULL,
  `resolved_by` char(36) DEFAULT NULL,
  `resolution_note` text DEFAULT NULL,
  `occurred_at` datetime NOT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`error_id`),
  KEY `idx_job` (`job_id`,`error_severity`,`occurred_at`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_resolved` (`is_resolved`,`occurred_at`),
  KEY `idx_errors_severity` (`job_id`,`error_severity`,`occurred_at` DESC),
  CONSTRAINT `fk_error_job` FOREIGN KEY (`job_id`) REFERENCES `migration_jobs` (`job_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `migration_jobs`
--

DROP TABLE IF EXISTS `migration_jobs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `migration_jobs` (
  `job_id` char(36) NOT NULL,
  `tenant_id` char(36) NOT NULL,
  `initiated_by` char(36) NOT NULL,
  `source_folder` varchar(500) NOT NULL,
  `status` enum('pending','preview','running','paused','completed','failed','canceled') NOT NULL DEFAULT 'pending',
  `total_tables` smallint(5) unsigned DEFAULT 0,
  `completed_tables` smallint(5) unsigned DEFAULT 0,
  `total_rows` int(10) unsigned DEFAULT 0,
  `processed_rows` int(10) unsigned DEFAULT 0,
  `skipped_rows` int(10) unsigned DEFAULT 0,
  `error_rows` int(10) unsigned DEFAULT 0,
  `preview_at` datetime DEFAULT NULL,
  `started_at` datetime DEFAULT NULL,
  `paused_at` datetime DEFAULT NULL,
  `completed_at` datetime DEFAULT NULL,
  `error_summary` text DEFAULT NULL,
  `checkpoint_data` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL CHECK (json_valid(`checkpoint_data`)),
  `client_ip` varchar(45) DEFAULT NULL,
  `user_agent` varchar(255) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`job_id`),
  KEY `idx_tenant_status` (`tenant_id`,`status`),
  KEY `idx_tenant_created` (`tenant_id`,`created_at` DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `mold_assets`
--

DROP TABLE IF EXISTS `mold_assets`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `mold_assets` (
  `mold_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `mold_code` varchar(30) NOT NULL,
  `mold_name` varchar(100) NOT NULL,
  `product_item_id` varchar(36) NOT NULL COMMENT '생산 대상 완제품',
  `supplier_partner_id` varchar(36) DEFAULT NULL COMMENT '금형 제작업체',
  `acquisition_date` date NOT NULL,
  `acquisition_cost` decimal(15,2) NOT NULL,
  `max_shots` int(11) NOT NULL DEFAULT 1000000,
  `current_shots` int(11) NOT NULL DEFAULT 0,
  `cycle_time_sec` int(11) NOT NULL DEFAULT 30,
  `status` varchar(20) NOT NULL DEFAULT 'active' COMMENT 'active/maintenance/retired',
  `customer_prepaid_amount` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '고객 선불 금형비',
  `amortization_per_unit` decimal(10,2) NOT NULL DEFAULT 0.00 COMMENT '제품당 상각액',
  `amortized_cumulative` decimal(15,2) NOT NULL DEFAULT 0.00,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`mold_id`),
  UNIQUE KEY `uq_mold_tenant_code` (`tenant_id`,`mold_code`),
  KEY `idx_mold_product` (`tenant_id`,`product_item_id`),
  KEY `fk_mold_product` (`product_item_id`),
  CONSTRAINT `fk_mold_product` FOREIGN KEY (`product_item_id`) REFERENCES `items` (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='플라스틱 사출 금형 자산 관리';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `mold_production_log`
--

DROP TABLE IF EXISTS `mold_production_log`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `mold_production_log` (
  `log_id` bigint(20) NOT NULL AUTO_INCREMENT,
  `tenant_id` varchar(36) NOT NULL,
  `mold_id` varchar(36) NOT NULL,
  `production_date` date NOT NULL,
  `shot_count` int(11) NOT NULL,
  `good_count` int(11) NOT NULL,
  `defect_count` int(11) NOT NULL DEFAULT 0,
  `source_type` varchar(30) DEFAULT 'production',
  `source_id` varchar(36) DEFAULT NULL,
  `memo` varchar(200) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`log_id`),
  KEY `idx_mpl_mold` (`tenant_id`,`mold_id`),
  KEY `idx_mpl_date` (`tenant_id`,`production_date`),
  KEY `fk_mpl_mold` (`mold_id`),
  CONSTRAINT `fk_mpl_mold` FOREIGN KEY (`mold_id`) REFERENCES `mold_assets` (`mold_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='금형별 생산 실적 (샷 카운트 누적) — INSERT ONLY';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `monthly_closing`
--

DROP TABLE IF EXISTS `monthly_closing`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `monthly_closing` (
  `closing_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `year_month` char(6) NOT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'open',
  `closed_at` datetime(6) DEFAULT NULL,
  `closed_by` varchar(36) DEFAULT NULL,
  `reopened_at` datetime(6) DEFAULT NULL,
  `reopened_by` varchar(36) DEFAULT NULL,
  `reopen_reason` varchar(500) DEFAULT NULL,
  `sales_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `purchase_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `receipt_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `payment_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`closing_id`),
  UNIQUE KEY `uq_tenant_ym` (`tenant_id`,`year_month`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `monthly_summary`
--

DROP TABLE IF EXISTS `monthly_summary`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `monthly_summary` (
  `summary_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `year_month` char(6) NOT NULL,
  `total_sales` decimal(15,2) NOT NULL DEFAULT 0.00,
  `total_purchase` decimal(15,2) NOT NULL DEFAULT 0.00,
  `total_receipt` decimal(15,2) NOT NULL DEFAULT 0.00,
  `total_payment` decimal(15,2) NOT NULL DEFAULT 0.00,
  `last_updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `gross_profit` decimal(15,2) GENERATED ALWAYS AS (`total_sales` - `total_purchase`) STORED,
  PRIMARY KEY (`summary_id`),
  UNIQUE KEY `uk_tenant_month` (`tenant_id`,`year_month`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_month` (`tenant_id`,`year_month`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `monthly_summary_sources`
--

DROP TABLE IF EXISTS `monthly_summary_sources`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `monthly_summary_sources` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `tenant_id` varchar(36) NOT NULL,
  `year_month` char(6) NOT NULL,
  `source_type` varchar(32) NOT NULL COMMENT '예: sales_delivery_confirm, purchase_receipt_confirm, purchase_return_confirm',
  `source_id` varchar(64) NOT NULL COMMENT 'delivery_id / receipt_id / return_id 등',
  `field_name` varchar(32) NOT NULL COMMENT 'total_sales | total_purchase | total_receipt | total_payment',
  `amount_delta` decimal(15,2) NOT NULL COMMENT '실제 가산된 금액 (감사 추적용, 음수 = 감산)',
  `applied_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_source` (`tenant_id`,`source_type`,`source_id`,`field_name`),
  KEY `idx_tenant_month` (`tenant_id`,`year_month`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='monthly_summary 멱등 추적 — 같은 source의 같은 field 중복 가산 차단';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `overtime`
--

DROP TABLE IF EXISTS `overtime`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `overtime` (
  `overtime_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL,
  `work_date` date NOT NULL,
  `start_time` time NOT NULL,
  `end_time` time NOT NULL,
  `hours` decimal(4,1) NOT NULL,
  `overtime_type` varchar(20) NOT NULL DEFAULT 'weekday',
  `reason` varchar(200) DEFAULT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'pending',
  `approved_by` varchar(36) DEFAULT NULL,
  `approved_at` datetime(6) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`overtime_id`),
  KEY `idx_tenant_emp` (`tenant_id`,`employee_id`),
  KEY `idx_tenant_date` (`tenant_id`,`work_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `partner_balance`
--

DROP TABLE IF EXISTS `partner_balance`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `partner_balance` (
  `balance_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `partner_id` varchar(36) NOT NULL,
  `total_sales` decimal(15,2) NOT NULL DEFAULT 0.00,
  `total_receipt` decimal(15,2) NOT NULL DEFAULT 0.00,
  `total_purchase` decimal(15,2) NOT NULL DEFAULT 0.00,
  `total_payment` decimal(15,2) NOT NULL DEFAULT 0.00,
  `last_updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `balance` decimal(15,2) GENERATED ALWAYS AS (`total_sales` - `total_receipt`) STORED,
  `payable` decimal(15,2) GENERATED ALWAYS AS (`total_purchase` - `total_payment`) STORED,
  PRIMARY KEY (`balance_id`),
  UNIQUE KEY `uk_tenant_partner` (`tenant_id`,`partner_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_balance` (`tenant_id`,`balance`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `partner_contacts`
--

DROP TABLE IF EXISTS `partner_contacts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `partner_contacts` (
  `contact_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `partner_id` varchar(36) DEFAULT NULL COMMENT '연결된 업체(있을 때만)',
  `contact_name` varchar(100) NOT NULL,
  `company_name` varchar(200) DEFAULT NULL,
  `tel` varchar(30) DEFAULT NULL COMMENT '대표 전화 (마스킹 X, 영업용)',
  `hp_encrypted` varbinary(255) DEFAULT NULL COMMENT '개인전화 AES-256 (헌법 #5)',
  `email_encrypted` varbinary(255) DEFAULT NULL COMMENT '이메일 AES-256 (헌법 #5)',
  `address` varchar(300) DEFAULT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
  PRIMARY KEY (`contact_id`),
  UNIQUE KEY `uq_partner_contacts_source_hash` (`tenant_id`,`migrated_source_hash`),
  KEY `idx_pc_tenant_partner` (`tenant_id`,`partner_id`),
  KEY `idx_pc_tenant_name` (`tenant_id`,`contact_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='POTHER.DOCNM 명함/연락처 (WS-11 축 5)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `partner_special_prices`
--

DROP TABLE IF EXISTS `partner_special_prices`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `partner_special_prices` (
  `id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `partner_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `spec` varchar(100) DEFAULT NULL COMMENT '?԰?',
  `unit` varchar(20) DEFAULT NULL COMMENT '????',
  `special_price` decimal(15,2) NOT NULL COMMENT '??ǰ ?????ܰ?',
  `std_price` decimal(15,2) DEFAULT NULL COMMENT 'ǥ???ǸŴܰ?',
  `vs_ratio` decimal(6,2) DEFAULT NULL COMMENT '????%',
  `last_supply_date` date DEFAULT NULL COMMENT '??????ǰ??',
  `employee_id` varchar(36) DEFAULT NULL COMMENT '????????',
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(36) DEFAULT NULL,
  `price_type` varchar(20) NOT NULL DEFAULT 'fixed',
  `unit_price` decimal(15,2) DEFAULT NULL,
  `discount_rate` decimal(5,2) DEFAULT NULL COMMENT '할인율(%) — price_type=discount 일 때만, 고정모드는 NULL (19차 업체특별단가 할인율 봉합)',
  `start_date` date DEFAULT NULL,
  `end_date` date DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_psp_partner_item` (`partner_id`,`item_id`),
  KEY `fk_psp_employee` (`employee_id`),
  KEY `idx_psp_partner` (`partner_id`),
  KEY `idx_psp_item` (`item_id`),
  KEY `idx_psp_tenant` (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!50003 SET @saved_cs_client      = @@character_set_client */ ;
/*!50003 SET @saved_cs_results     = @@character_set_results */ ;
/*!50003 SET @saved_col_connection = @@collation_connection */ ;
/*!50003 SET character_set_client  = utf8mb3 */ ;
/*!50003 SET character_set_results = utf8mb3 */ ;
/*!50003 SET collation_connection  = utf8mb3_general_ci */ ;
/*!50003 SET @saved_sql_mode       = @@sql_mode */ ;
/*!50003 SET sql_mode              = '' */ ;
DELIMITER ;;
/*!50003 CREATE*/ /*!50017*/ /*!50003 TRIGGER trg_psp_ratio_insert BEFORE INSERT ON partner_special_prices FOR EACH ROW SET NEW.id = IF(NEW.id IS NULL OR NEW.id = '', UUID(), NEW.id), NEW.vs_ratio = IF(NEW.std_price > 0, ROUND((NEW.special_price / NEW.std_price) * 100, 2), NEW.vs_ratio) */;;
DELIMITER ;
/*!50003 SET sql_mode              = @saved_sql_mode */ ;
/*!50003 SET character_set_client  = @saved_cs_client */ ;
/*!50003 SET character_set_results = @saved_cs_results */ ;
/*!50003 SET collation_connection  = @saved_col_connection */ ;
/*!50003 SET @saved_cs_client      = @@character_set_client */ ;
/*!50003 SET @saved_cs_results     = @@character_set_results */ ;
/*!50003 SET @saved_col_connection = @@collation_connection */ ;
/*!50003 SET character_set_client  = utf8mb3 */ ;
/*!50003 SET character_set_results = utf8mb3 */ ;
/*!50003 SET collation_connection  = utf8mb3_general_ci */ ;
/*!50003 SET @saved_sql_mode       = @@sql_mode */ ;
/*!50003 SET sql_mode              = '' */ ;
DELIMITER ;;
/*!50003 CREATE*/ /*!50017*/ /*!50003 TRIGGER trg_psp_ratio_update BEFORE UPDATE ON partner_special_prices FOR EACH ROW SET NEW.vs_ratio = IF(NEW.std_price > 0, ROUND((NEW.special_price / NEW.std_price) * 100, 2), NEW.vs_ratio) */;;
DELIMITER ;
/*!50003 SET sql_mode              = @saved_sql_mode */ ;
/*!50003 SET character_set_client  = @saved_cs_client */ ;
/*!50003 SET character_set_results = @saved_cs_results */ ;
/*!50003 SET collation_connection  = @saved_col_connection */ ;

--
-- Table structure for table `payroll_slips`
-- 작(2026-08-13) DB-100 — 급여 명세. 사장님 지시 2026-08-13.
--
-- 🔴 "급여는 자동계산하지 말고 수동으로 int값 직접 받아서 입력하는게 가장 깔끔함"
--    "각 고객사 니즈나 사정도 부합시킬 수 있고."
--    ⇒ 4대보험 요율·간이세액표를 우리가 계산하지 않는다. 금액을 받는다.
--       (요율은 매년 바뀌고, 회사마다 수당·비과세가 다르고, 틀리면 직원 돈이 틀린다)
--
-- 🔴 보호는 **권한 계층**이 한다("권한 계층분리로 급여를 관리해도 충분히 됨").
--    금액은 평문이고 menu_code='PAYROLL' 로 막는다. 일반 직원은 본인 것만 본다.
--

DROP TABLE IF EXISTS `payroll_slips`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `payroll_slips` (
  `slip_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL,
  `pay_year` int(11) NOT NULL COMMENT '귀속 연도',
  `pay_month` int(11) NOT NULL COMMENT '귀속 월(1~12)',
  `pay_date` date DEFAULT NULL COMMENT '실제 지급일. 귀속월과 다를 수 있다',
  `total_payment` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '총지급액',
  `total_deduct` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '총공제액',
  `net_payment` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '실지급액',
  `status` varchar(20) NOT NULL DEFAULT 'draft' COMMENT 'draft/confirmed/paid/cancelled',
  `confirmed_by` varchar(36) DEFAULT NULL,
  `confirmed_at` datetime(6) DEFAULT NULL,
  `absence_id` varchar(36) DEFAULT NULL COMMENT '이 달에 휴직이 있으면 그 건(DB-98 연동)',
  `memo` varchar(500) DEFAULT NULL,
  `created_by` varchar(36) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`slip_id`),
  UNIQUE KEY `uk_payroll_emp_month` (`tenant_id`,`employee_id`,`pay_year`,`pay_month`),
  KEY `idx_payroll_month` (`tenant_id`,`pay_year`,`pay_month`),
  KEY `idx_payroll_status` (`tenant_id`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='급여 명세 — 금액을 사람이 직접 넣는다. DB-100';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `payroll_slip_lines`
-- 작(2026-08-13) DB-100 — 급여 항목. 회사마다 수당이 달라 **이름도 사람이 적는다.**
--

DROP TABLE IF EXISTS `payroll_slip_lines`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `payroll_slip_lines` (
  `line_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `slip_id` varchar(36) NOT NULL,
  `line_type` varchar(20) NOT NULL COMMENT 'payment=지급 · deduct=공제',
  `item_name` varchar(60) NOT NULL COMMENT '항목 이름. 사람이 적는다(기본급·식대·국민연금 등)',
  `amount` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '금액. 사람이 직접 넣는다',
  `sort_order` int(11) NOT NULL DEFAULT 0,
  `is_taxable` tinyint(1) NOT NULL DEFAULT 1 COMMENT '1=과세 0=비과세. 사람이 고른다',
  `memo` varchar(200) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`line_id`),
  KEY `idx_payroll_line_slip` (`tenant_id`,`slip_id`,`line_type`,`sort_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='급여 항목 — 이름도 사람이 적는다. DB-100';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `severance_payments`
-- 작(2026-08-13) DB-100 — 퇴직금. 🔴 법정 산식을 우리가 돌리지 않는다.
--   평균임금에 상여·연차수당을 어떻게 넣는지가 회사마다 다르고 다툼이 잦다.
--   퇴직연금(DB·DC·IRP)이면 산식 자체가 다르다. 틀리면 법적 분쟁이 된다.
--

DROP TABLE IF EXISTS `severance_payments`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `severance_payments` (
  `severance_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL,
  `join_date` date NOT NULL,
  `resign_date` date NOT NULL,
  `service_days` int(11) NOT NULL DEFAULT 0 COMMENT '재직일수',
  `avg_wage` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '평균임금(1일). 사람이 넣는다',
  `severance_amount` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '퇴직금. 사람이 넣는다',
  `tax_amount` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '퇴직소득세 등 공제액',
  `net_amount` decimal(15,2) NOT NULL DEFAULT 0.00 COMMENT '실지급액',
  `pay_type` varchar(20) NOT NULL DEFAULT 'direct' COMMENT 'direct/db/dc/irp',
  `pay_date` date DEFAULT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'draft' COMMENT 'draft/confirmed/paid/cancelled',
  `confirmed_by` varchar(36) DEFAULT NULL,
  `confirmed_at` datetime(6) DEFAULT NULL,
  `calc_basis` varchar(500) DEFAULT NULL COMMENT '산정 근거. 분쟁 시 설명해야 한다',
  `memo` varchar(500) DEFAULT NULL,
  `created_by` varchar(36) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`severance_id`),
  KEY `idx_severance_emp` (`tenant_id`,`employee_id`),
  KEY `idx_severance_status` (`tenant_id`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='퇴직금 — 금액을 사람이 직접 넣는다. DB-100';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `partners`
--

DROP TABLE IF EXISTS `partners`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `partners` (
  `partner_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `partner_code` varchar(20) NOT NULL,
  `partner_name` varchar(100) NOT NULL,
  `partner_type` longtext NOT NULL,
  `biz_no` varchar(12) DEFAULT NULL,
  `ceo_name` varchar(50) DEFAULT NULL,
  `biz_type` varchar(50) DEFAULT NULL,
  `biz_item` varchar(50) DEFAULT NULL,
  `tel` varchar(20) DEFAULT NULL,
  `fax` varchar(20) DEFAULT NULL,
  `email` varchar(100) DEFAULT NULL,
  `address` varchar(200) DEFAULT NULL,
  `address_detail` varchar(200) DEFAULT NULL,
  `latitude` decimal(10,7) DEFAULT NULL COMMENT 'DB-105 kakao map/navi latitude',
  `longitude` decimal(10,7) DEFAULT NULL COMMENT 'DB-105 kakao map/navi longitude',
  `credit_limit` decimal(15,2) DEFAULT NULL,
  `payment_terms` tinyint(3) unsigned DEFAULT NULL,
  `bank_name` varchar(30) DEFAULT NULL,
  `bank_account` varchar(200) DEFAULT NULL,
  `account_holder` varchar(30) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL,
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL,
  `updated_by` varchar(36) DEFAULT NULL,
  `is_deleted` tinyint(1) NOT NULL DEFAULT 0,
  `deleted_at` datetime(6) DEFAULT NULL,
  `zip_code` varchar(10) DEFAULT NULL,
  `manager_name` varchar(50) DEFAULT NULL,
  `manager_tel` varchar(20) DEFAULT NULL,
  `price_grade` char(1) NOT NULL DEFAULT 'A',
  `tax_type` varchar(20) NOT NULL DEFAULT 'taxable',
  `row_version` int(11) NOT NULL DEFAULT 0,
  `card_commission_rate` decimal(5,2) DEFAULT 0.00 COMMENT '?? ???? (buy_cardyul)',
  `classification_code` varchar(30) DEFAULT NULL COMMENT '?? ?? (buy_ccode)',
  `manager_department` varchar(30) DEFAULT NULL COMMENT '?? ?? (buy_damdangbu)',
  `price_grade_code` varchar(10) DEFAULT NULL COMMENT '???? ?? (buy_DOSCODE)',
  `legacy_extra` varchar(30) DEFAULT NULL COMMENT '??? ?? (buy_fil)',
  `discount_rate` decimal(5,2) DEFAULT 0.00 COMMENT '??? (buy_halyul)',
  `keyman_birth` varchar(10) DEFAULT NULL COMMENT '?? ?? (buy_keybirth)',
  `keyman_name` varchar(50) DEFAULT NULL COMMENT '?? ?? (buy_keyname)',
  `keyman_phone` varchar(20) DEFAULT NULL COMMENT '?? ??? (buy_keytel)',
  `margin_rate` decimal(5,2) DEFAULT 0.00 COMMENT '??? (buy_mayul)',
  `sales_employee` varchar(30) DEFAULT NULL COMMENT '?? ???? (buy_sawon)',
  `trade_start_date` date DEFAULT NULL COMMENT '?? ??? (buy_startdt)',
  `business_registration_date` date DEFAULT NULL COMMENT '?????? (buy_taxdt)',
  `tel_secondary` varchar(20) DEFAULT NULL COMMENT '?? 2? (buy_tel1)',
  `tax_classification` varchar(10) DEFAULT NULL COMMENT '?? ?? (buy_taxgubun)',
  `ceo_resident_no_encrypted` varbinary(255) DEFAULT NULL COMMENT '?? ???? AES-256 (buy_topjumin)',
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
  PRIMARY KEY (`partner_id`),
  UNIQUE KEY `uq_tenant_code` (`tenant_id`,`partner_code`),
  UNIQUE KEY `uq_partners_source_hash` (`tenant_id`,`migrated_source_hash`),
  KEY `idx_tenant_name` (`tenant_id`,`partner_name`),
  KEY `idx_tenant_active` (`tenant_id`,`is_active`),
  KEY `idx_partners_price_grade` (`tenant_id`,`price_grade`),
  KEY `idx_partners_sales_emp` (`tenant_id`,`sales_employee`)
  -- fk_partners_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `payments`
--

DROP TABLE IF EXISTS `payments`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `payments` (
  `payment_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `partner_id` varchar(36) NOT NULL,
  `payment_type` varchar(20) NOT NULL COMMENT 'receipt:???? payment:????',
  `amount` decimal(15,2) NOT NULL,
  `payment_date` date NOT NULL,
  `payment_method` varchar(20) NOT NULL DEFAULT 'cash' COMMENT 'cash/card/transfer/check',
  `ref_order_id` varchar(36) DEFAULT NULL COMMENT '???? ?ֹ?ID',
  `memo` varchar(500) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(36) DEFAULT NULL,
  PRIMARY KEY (`payment_id`),
  KEY `idx_pay_partner` (`partner_id`),
  KEY `idx_pay_date` (`payment_date`),
  KEY `idx_pay_type` (`payment_type`),
  KEY `idx_pay_tenant` (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `positions`
--

DROP TABLE IF EXISTS `positions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `positions` (
  `position_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `code` varchar(40) NOT NULL COMMENT '코드 (예: CEO, MANAGER) — 영문 대문자',
  `name` varchar(60) NOT NULL COMMENT '표시명 (예: 대표이사, 팀장)',
  `sort_order` int(11) NOT NULL DEFAULT 0 COMMENT '정렬 (높을수록 상위직급)',
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(60) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(60) DEFAULT NULL,
  PRIMARY KEY (`position_id`),
  UNIQUE KEY `uk_positions_tenant_code` (`tenant_id`,`code`),
  KEY `ix_positions_tenant_active` (`tenant_id`,`is_active`,`sort_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='직급 마스터 — 회사별 어드민이 추가/삭제';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `purchase_order_items`
--

DROP TABLE IF EXISTS `purchase_order_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `purchase_order_items` (
  `po_item_id` varchar(36) NOT NULL,
  `po_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `ordered_qty` decimal(15,3) NOT NULL,
  `received_qty` decimal(15,3) NOT NULL,
  `unit_price` decimal(15,2) NOT NULL,
  `supply_amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL,
  `warehouse_id` varchar(36) DEFAULT NULL,
  `item_status` varchar(20) NOT NULL,
  PRIMARY KEY (`po_item_id`),
  KEY `idx_po` (`po_id`),
  KEY `idx_poi_tenant_item` (`tenant_id`,`item_id`),
  CONSTRAINT `fk_poi_header` FOREIGN KEY (`po_id`) REFERENCES `purchase_orders` (`po_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `purchase_orders`
--

DROP TABLE IF EXISTS `purchase_orders`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `purchase_orders` (
  `po_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `po_no` varchar(20) NOT NULL,
  `partner_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) DEFAULT NULL,
  `po_date` date NOT NULL,
  `expected_date` date DEFAULT NULL,
  `status` enum('draft','ordered','partial','received','cancelled') NOT NULL DEFAULT 'draft',
  `total_amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `is_auto` tinyint(1) NOT NULL DEFAULT 0,
  `created_at` datetime(6) NOT NULL,
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL,
  `updated_by` varchar(36) DEFAULT NULL,
  `is_deleted` tinyint(1) NOT NULL DEFAULT 0,
  `deleted_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`po_id`),
  UNIQUE KEY `uq_po_no` (`tenant_id`,`po_no`),
  KEY `idx_po_status` (`status`),
  KEY `idx_tenant_date` (`tenant_id`,`po_date`),
  KEY `idx_tenant_partner` (`tenant_id`,`partner_id`),
  KEY `idx_tenant_status` (`tenant_id`,`status`),
  KEY `idx_po_tenant_date` (`tenant_id`,`po_date`),
  KEY `fk_po_partner` (`partner_id`),
  CONSTRAINT `fk_po_partner` FOREIGN KEY (`partner_id`) REFERENCES `partners` (`partner_id`) ON DELETE NO ACTION
  -- fk_purchase_orders_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `purchase_receipt_items`
--

DROP TABLE IF EXISTS `purchase_receipt_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `purchase_receipt_items` (
  `receipt_item_id` varchar(36) NOT NULL,
  `receipt_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `po_item_id` varchar(36) DEFAULT NULL,
  `item_id` varchar(36) DEFAULT NULL COMMENT 'WS-F: 마이그 매핑 실패 NULL (헌법 #20)',
  `warehouse_id` varchar(36) DEFAULT NULL COMMENT 'WS-F: 마이그 매핑 실패 NULL',
  `qty` decimal(15,3) NOT NULL,
  `unit_price` decimal(15,2) NOT NULL,
  `supply_amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL,
  `legacy_pum` varchar(100) DEFAULT NULL,
  `legacy_ku` varchar(100) DEFAULT NULL,
  `source_id` varchar(80) DEFAULT NULL,
  PRIMARY KEY (`receipt_item_id`),
  UNIQUE KEY `uq_pri_source` (`tenant_id`,`source_id`),
  KEY `idx_pri_tenant_item` (`tenant_id`,`item_id`),
  KEY `fk_pri_header` (`receipt_id`),
  CONSTRAINT `fk_pri_header` FOREIGN KEY (`receipt_id`) REFERENCES `purchase_receipts` (`receipt_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `purchase_receipts`
--

DROP TABLE IF EXISTS `purchase_receipts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `purchase_receipts` (
  `receipt_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `receipt_no` varchar(32) NOT NULL,
  `po_id` varchar(36) DEFAULT NULL,
  `partner_id` varchar(36) NOT NULL,
  `receipt_date` date NOT NULL,
  `source_type` varchar(20) NOT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'draft',
  `total_amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL,
  `created_by` varchar(36) DEFAULT NULL,
  `source_id` varchar(80) DEFAULT NULL COMMENT 'WS-F: 마이그 멱등 키',
  `legacy_tax_no` int(11) DEFAULT NULL COMMENT 'WS-F: DOCFB.IJ_TAXNO',
  `legacy_buy_code` int(11) DEFAULT NULL COMMENT 'WS-F: DOCFB.IJ_BUY (Q4 그대로)',
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'WS-F: SHA256 무결성',
  PRIMARY KEY (`receipt_id`),
  UNIQUE KEY `uq_receipt_no` (`tenant_id`,`receipt_no`),
  UNIQUE KEY `uq_pr_source` (`tenant_id`,`source_id`),
  KEY `idx_pr_tenant_date` (`tenant_id`,`receipt_date`),
  KEY `fk_pr_partner` (`partner_id`),
  KEY `idx_pr_legacy_tax_no` (`tenant_id`,`legacy_tax_no`),
  CONSTRAINT `fk_pr_partner` FOREIGN KEY (`partner_id`) REFERENCES `partners` (`partner_id`) ON DELETE NO ACTION
  -- fk_purchase_receipts_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `purchase_return_items`
--

DROP TABLE IF EXISTS `purchase_return_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `purchase_return_items` (
  `return_item_id` varchar(36) NOT NULL,
  `return_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `qty` decimal(15,3) NOT NULL,
  `unit_price` decimal(15,2) NOT NULL,
  `supply_amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL,
  `warehouse_id` varchar(36) DEFAULT NULL,
  `memo` varchar(200) DEFAULT NULL,
  PRIMARY KEY (`return_item_id`),
  KEY `idx_return` (`return_id`),
  KEY `idx_rti_tenant_item` (`tenant_id`,`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `purchase_returns`
--

DROP TABLE IF EXISTS `purchase_returns`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `purchase_returns` (
  `return_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `return_no` varchar(20) NOT NULL,
  `receipt_id` varchar(36) DEFAULT NULL,
  `partner_id` varchar(36) NOT NULL,
  `return_date` date NOT NULL,
  `return_type` varchar(20) NOT NULL DEFAULT 'purchase_return',
  `status` varchar(20) NOT NULL DEFAULT 'draft',
  `total_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `vat_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `is_deleted` tinyint(1) NOT NULL DEFAULT 0,
  `created_by` varchar(36) DEFAULT NULL COMMENT '작성자 user_id — employees.user_id 조인(DB-109, 20260825작16)',
  `return_reason` varchar(30) DEFAULT NULL COMMENT '반품 사유 — 자유입력(20260825작16). 코드가 아니라 고객사가 쓴 말이 그대로 들어간다',
  `return_reason_memo` varchar(500) DEFAULT NULL COMMENT '반품 사유 상세 (자유 입력)',
  PRIMARY KEY (`return_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_partner` (`partner_id`),
  KEY `idx_rt_tenant_date` (`tenant_id`,`return_date`),
  KEY `idx_purchase_returns_reason` (`tenant_id`,`return_reason`,`return_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `quotation_items`
--

DROP TABLE IF EXISTS `quotation_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `quotation_items` (
  `id` varchar(36) NOT NULL,
  `quote_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `spec` varchar(100) DEFAULT NULL,
  `unit` varchar(10) DEFAULT NULL,
  `qty` decimal(15,3) NOT NULL,
  `unit_price` decimal(15,2) NOT NULL,
  `discount_rate` decimal(5,2) NOT NULL DEFAULT 0.00,
  `amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `memo` varchar(200) DEFAULT NULL,
  `sort_order` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `fk_qi_item` (`item_id`),
  KEY `idx_qi_quote` (`quote_id`),
  KEY `idx_quotation` (`quote_id`),
  KEY `idx_qi_item` (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `quotations`
--

DROP TABLE IF EXISTS `quotations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `quotations` (
  `quote_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `quote_no` varchar(20) NOT NULL,
  `partner_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) DEFAULT NULL,
  `quote_date` date NOT NULL,
  `valid_until` date DEFAULT NULL,
  `status` enum('draft','submitted','accepted','rejected','expired','converted') NOT NULL DEFAULT 'draft',
  `total_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `vat_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `memo` varchar(500) DEFAULT NULL,
  `converted_order_id` varchar(36) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(36) DEFAULT NULL,
  `is_deleted` tinyint(1) NOT NULL DEFAULT 0,
  `deleted_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`quote_id`),
  KEY `fk_qt_employee` (`employee_id`),
  KEY `idx_qt_partner` (`partner_id`),
  KEY `idx_qt_status` (`status`),
  KEY `idx_qt_tenant` (`tenant_id`),
  KEY `idx_tenant_date` (`tenant_id`,`quote_date`),
  KEY `idx_tenant_partner` (`tenant_id`,`partner_id`),
  KEY `idx_tenant_status` (`tenant_id`,`status`),
  KEY `idx_q_tenant_date` (`tenant_id`,`quote_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `refresh_tokens`
--

DROP TABLE IF EXISTS `refresh_tokens`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `refresh_tokens` (
  `token_id` varchar(36) NOT NULL,
  `user_id` varchar(36) NOT NULL,
  `token_hash` varchar(256) NOT NULL,
  `ip_address` varchar(50) DEFAULT NULL,
  `user_agent` varchar(500) DEFAULT NULL,
  `expires_at` datetime(6) NOT NULL,
  `is_revoked` tinyint(1) NOT NULL DEFAULT 0,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`token_id`),
  KEY `idx_user` (`user_id`),
  KEY `idx_token` (`token_hash`(250)),
  KEY `idx_expires` (`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `restore_history`
--

DROP TABLE IF EXISTS `restore_history`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `restore_history` (
  `restore_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `started_at` datetime NOT NULL,
  `finished_at` datetime DEFAULT NULL,
  `source_file` varchar(500) NOT NULL,
  `pre_restore_backup` varchar(500) DEFAULT NULL,
  `status` varchar(20) NOT NULL,
  `error_message` text DEFAULT NULL,
  `triggered_by_user` varchar(36) DEFAULT NULL,
  PRIMARY KEY (`restore_id`),
  KEY `ix_restore_history_tenant_started` (`tenant_id`,`started_at` DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `sales_deliveries`
--

DROP TABLE IF EXISTS `sales_deliveries`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sales_deliveries` (
  `delivery_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `delivery_no` varchar(32) NOT NULL,
  `order_id` varchar(36) DEFAULT NULL,
  `partner_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) DEFAULT NULL,
  `delivery_date` date NOT NULL,
  `source_type` varchar(20) NOT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'draft',
  `total_amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL,
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL,
  `updated_by` varchar(36) DEFAULT NULL,
  `is_deleted` tinyint(1) NOT NULL DEFAULT 0,
  `deleted_at` datetime(6) DEFAULT NULL,
  `tax_invoice_id` varchar(36) DEFAULT NULL COMMENT '발행된 세금계산서 ID (tax_invoices.invoice_id 역참조). 미발행 시 NULL. 취소 시 NULL로 환원.',
  `source_id` varchar(80) DEFAULT NULL COMMENT 'WS-F: 마이그 멱등 키 (mig-docfb-IJ_DT-IJ_IO-IJ_SEQ-IJ_BUY)',
  `legacy_tax_no` int(11) DEFAULT NULL COMMENT 'WS-F: DOCFB.IJ_TAXNO (tax_invoices.tax_no 연결 키)',
  `legacy_buy_code` int(11) DEFAULT NULL COMMENT 'WS-F: DOCFB.IJ_BUY (사장님 결재 Q4: 음수값 그대로 이관)',
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'WS-F: SHA256 무결성 해시',
  PRIMARY KEY (`delivery_id`),
  UNIQUE KEY `uq_delivery_no` (`tenant_id`,`delivery_no`),
  UNIQUE KEY `uq_sd_source` (`tenant_id`,`source_id`),
  KEY `idx_tenant_date` (`tenant_id`,`delivery_date`),
  KEY `idx_tenant_partner` (`tenant_id`,`partner_id`),
  KEY `idx_tenant_status` (`tenant_id`,`status`),
  KEY `idx_sd_tenant_date` (`tenant_id`,`delivery_date`),
  KEY `idx_sales_deliveries_tax_invoice` (`tax_invoice_id`),
  KEY `fk_sd_partner` (`partner_id`),
  KEY `idx_sd_legacy_tax_no` (`tenant_id`,`legacy_tax_no`),
  -- fk_sales_deliveries_tenant 제거 (무결 봉합 2026-06-18): tenants 삭제 FK 제거. tenant_id 컬럼 보존
  CONSTRAINT `fk_sd_partner` FOREIGN KEY (`partner_id`) REFERENCES `partners` (`partner_id`) ON DELETE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `sales_delivery_items`
--

DROP TABLE IF EXISTS `sales_delivery_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sales_delivery_items` (
  `delivery_item_id` varchar(36) NOT NULL,
  `delivery_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `order_item_id` varchar(36) DEFAULT NULL,
  `item_id` varchar(36) DEFAULT NULL COMMENT 'WS-F: 마이그 매핑 실패 시 NULL 허용 (헌법 #20)',
  `warehouse_id` varchar(36) DEFAULT NULL COMMENT 'WS-F: 마이그 매핑 실패 시 NULL 허용',
  `qty` decimal(15,3) NOT NULL,
  `unit_price` decimal(15,2) NOT NULL,
  `supply_amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL,
  `legacy_pum` varchar(100) DEFAULT NULL COMMENT 'WS-F: DOCFB.IJ_PUM (원본 품목명)',
  `legacy_ku` varchar(100) DEFAULT NULL COMMENT 'WS-F: DOCFB.IJ_KU (원본 규격)',
  `source_id` varchar(80) DEFAULT NULL COMMENT 'WS-F: 마이그 멱등 키 (라인)',
  PRIMARY KEY (`delivery_item_id`),
  UNIQUE KEY `uq_sdi_source` (`tenant_id`,`source_id`),
  KEY `idx_delivery` (`delivery_id`),
  KEY `idx_sdi_tenant_item` (`tenant_id`,`item_id`),
  CONSTRAINT `fk_sdi_header` FOREIGN KEY (`delivery_id`) REFERENCES `sales_deliveries` (`delivery_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `sales_order_items`
--

DROP TABLE IF EXISTS `sales_order_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sales_order_items` (
  `order_item_id` varchar(36) NOT NULL,
  `order_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `ordered_qty` decimal(15,3) NOT NULL,
  `delivered_qty` decimal(15,3) NOT NULL,
  `unit_price` decimal(15,2) NOT NULL,
  `supply_amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL,
  `item_status` varchar(20) NOT NULL,
  PRIMARY KEY (`order_item_id`),
  KEY `idx_soi_tenant_item` (`tenant_id`,`item_id`),
  KEY `fk_soi_header` (`order_id`),
  CONSTRAINT `fk_soi_header` FOREIGN KEY (`order_id`) REFERENCES `sales_orders` (`order_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `sales_orders`
--

DROP TABLE IF EXISTS `sales_orders`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sales_orders` (
  `order_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `order_no` varchar(20) NOT NULL,
  `partner_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) DEFAULT NULL,
  `order_date` date NOT NULL,
  `delivery_date` date DEFAULT NULL,
  `status` enum('draft','quotation','order','confirmed','partial','closed','invoiced','cancelled') NOT NULL DEFAULT 'draft',
  `total_amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL,
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL,
  `updated_by` varchar(36) DEFAULT NULL,
  `is_deleted` tinyint(1) NOT NULL DEFAULT 0,
  `deleted_at` datetime(6) DEFAULT NULL,
  `is_auto` tinyint(1) NOT NULL DEFAULT 0 COMMENT '???̷?Ʈ ?Ǹ? ?? ?ڵ????? ???? (???? ǥ?? ????)',
  PRIMARY KEY (`order_id`),
  UNIQUE KEY `uq_order_no` (`tenant_id`,`order_no`),
  KEY `idx_so_status` (`status`),
  KEY `idx_so_partner_status` (`partner_id`,`status`),
  KEY `idx_tenant_date` (`tenant_id`,`order_date`),
  KEY `idx_tenant_partner` (`tenant_id`,`partner_id`),
  KEY `idx_tenant_status` (`tenant_id`,`status`),
  -- fk_sales_orders_tenant 제거 (무결 봉합 2026-06-18): tenants 삭제 FK 제거. tenant_id 컬럼 보존
  CONSTRAINT `fk_so_partner` FOREIGN KEY (`partner_id`) REFERENCES `partners` (`partner_id`) ON DELETE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `sales_return_items`
--

DROP TABLE IF EXISTS `sales_return_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sales_return_items` (
  `return_item_id` varchar(36) NOT NULL,
  `return_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `delivery_item_id` varchar(36) DEFAULT NULL COMMENT '원 거래명세서 라인 FK — 원단가 보존 추적',
  `item_id` varchar(36) NOT NULL,
  `qty` decimal(15,3) NOT NULL,
  `unit_price` decimal(15,2) NOT NULL COMMENT '반품 당시 환불 단가 (원거래 단가 보존)',
  `original_unit_price` decimal(15,2) DEFAULT NULL COMMENT '원거래 단가 복사본 (I-012 정합성)',
  `supply_amount` decimal(15,2) NOT NULL,
  `vat_amount` decimal(15,2) NOT NULL,
  `warehouse_id` varchar(36) DEFAULT NULL,
  `is_loss` tinyint(1) NOT NULL DEFAULT 0 COMMENT '파손 로스 여부 — 1이면 재고 미반영(팔 수 없는 물건). 매출·미수 차감은 그대로',
  `memo` varchar(200) DEFAULT NULL,
  PRIMARY KEY (`return_item_id`),
  KEY `idx_sri_return` (`return_id`),
  KEY `idx_sri_tenant_item` (`tenant_id`,`item_id`),
  KEY `fk_sri_warehouse` (`warehouse_id`),
  KEY `fk_sri_delivery_item` (`delivery_item_id`),
  KEY `fk_sri_item` (`item_id`),
  CONSTRAINT `fk_sri_delivery_item` FOREIGN KEY (`delivery_item_id`) REFERENCES `sales_delivery_items` (`delivery_item_id`),
  CONSTRAINT `fk_sri_item` FOREIGN KEY (`item_id`) REFERENCES `items` (`item_id`),
  CONSTRAINT `fk_sri_return` FOREIGN KEY (`return_id`) REFERENCES `sales_returns` (`return_id`) ON DELETE CASCADE,
  CONSTRAINT `fk_sri_warehouse` FOREIGN KEY (`warehouse_id`) REFERENCES `warehouses` (`warehouse_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `sales_returns`
--

DROP TABLE IF EXISTS `sales_returns`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sales_returns` (
  `return_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `return_no` varchar(20) NOT NULL,
  `delivery_id` varchar(36) DEFAULT NULL COMMENT '원 거래명세서(판매) FK',
  `partner_id` varchar(36) NOT NULL,
  `return_date` date NOT NULL,
  `return_reason` varchar(30) NOT NULL DEFAULT 'customer_return' COMMENT 'customer_return/defect/exchange',
  `status` varchar(20) NOT NULL DEFAULT 'draft' COMMENT 'draft/confirmed/cancelled',
  `total_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `vat_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `memo` varchar(500) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(36) DEFAULT NULL,
  `is_deleted` tinyint(1) NOT NULL DEFAULT 0,
  `deleted_at` datetime(6) DEFAULT NULL,
  `return_reason_memo` varchar(500) DEFAULT NULL,
  PRIMARY KEY (`return_id`),
  UNIQUE KEY `uq_sret_tenant_returnno` (`tenant_id`,`return_no`),
  KEY `idx_sret_tenant` (`tenant_id`),
  KEY `idx_sret_partner` (`partner_id`),
  KEY `idx_sret_tenant_date` (`tenant_id`,`return_date`),
  KEY `fk_sret_delivery` (`delivery_id`),
  CONSTRAINT `fk_sret_delivery` FOREIGN KEY (`delivery_id`) REFERENCES `sales_deliveries` (`delivery_id`),
  CONSTRAINT `fk_sret_partner` FOREIGN KEY (`partner_id`) REFERENCES `partners` (`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `schedules`
--

DROP TABLE IF EXISTS `schedules`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `schedules` (
  `schedule_id` varchar(36) NOT NULL COMMENT '일정 PK',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트 ID',
  `title` varchar(200) NOT NULL COMMENT '일정 제목',
  `schedule_type` varchar(30) NOT NULL DEFAULT 'issue' COMMENT 'meeting/todo/reminder/issue',
  `start_at` datetime(6) NOT NULL COMMENT '시작 시각',
  `end_at` datetime(6) DEFAULT NULL COMMENT '종료 시각',
  `partner_id` varchar(36) DEFAULT NULL COMMENT '거래처 연결 (선택)',
  `partner_name` varchar(100) DEFAULT NULL COMMENT '거래처명 (조회 편의)',
  `participant_id` varchar(36) DEFAULT NULL COMMENT '관련 직원 (선택)',
  `participant_name` varchar(50) DEFAULT NULL COMMENT '관련 직원명',
  `memo` text DEFAULT NULL COMMENT '메모',
  `is_completed` tinyint(1) NOT NULL DEFAULT 0 COMMENT '완료 여부',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(36) DEFAULT NULL,
  PRIMARY KEY (`schedule_id`),
  KEY `idx_tenant_start` (`tenant_id`,`start_at`),
  KEY `idx_partner` (`tenant_id`,`partner_id`),
  KEY `idx_participant` (`tenant_id`,`participant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='통합 캘린더 일정 — 미팅/todo/리마인더/이슈';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `security_alerts`
--

DROP TABLE IF EXISTS `security_alerts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `security_alerts` (
  `alert_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) DEFAULT NULL,
  `user_id` varchar(36) DEFAULT NULL,
  `alert_type` varchar(50) NOT NULL,
  `description` varchar(500) DEFAULT NULL,
  `ip_address` varchar(50) DEFAULT NULL,
  `is_resolved` tinyint(1) NOT NULL DEFAULT 0,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`alert_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_type` (`alert_type`),
  KEY `idx_created` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `schema_migrations`
--   스키마 마이그(DB-*.sql) 적용 이력(고리4 ①) — 멱등·재적용·롤백 기준점. 헌법 #36. 출처: DB-84.
--   __efmigrationshistory(EF, 비활성 잔재)와 역할이 다르다(EF용 vs Dapper DB-*.sql용).
--

DROP TABLE IF EXISTS `schema_migrations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `schema_migrations` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `migration_id` varchar(100) NOT NULL COMMENT '적용된 마이그 식별자(예: "DB-84"). MigrationRunner 가 파일명에서 추출',
  `app_version` varchar(20) DEFAULT NULL COMMENT '이 마이그를 적용한 앱 버전(SemVer). 수동 적용 시 NULL',
  `checksum` varchar(64) DEFAULT NULL COMMENT '마이그 .sql 내용의 SHA-256(무결성). 미산출 시 NULL',
  `success` tinyint(1) NOT NULL DEFAULT 1 COMMENT '적용 성공 여부. 1=성공(skip 대상), 0=실패(재적용 대상)',
  `applied_at` datetime(3) NOT NULL DEFAULT current_timestamp(3) COMMENT '적용 시각',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_schema_migrations_migration_id` (`migration_id`),
  KEY `idx_schema_migrations_applied` (`applied_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- 신규설치 시드 (20260722작4 봉합 #1 — 사장님 결재 2026-07-23):
--   이 clean DDL 은 아래 마이그(DB-02~DB-87)가 '이미 반영된' 최종 스키마다.
--   그런데 신규설치 DB 는 이 이력이 비어(0행) 있어, 자동업데이트의 마이그 교차검증 게이트
--   (UpdateOrchestrator.PassesMigrationCrossCheckAsync)가 zip 의 DB-*.sql 을 전부 '미적용 신규'로
--   오판·차단했다 → 신규 고객의 첫 자동업데이트가 영구 차단됐다(2026-07-22 Sandbox 실측 확정).
--   → clean DDL 이 담은 마이그를 success=1 로 시드해 "이미 적용됨"을 명시한다(게이트 통과, 헌법 #36).
--
--   ★ 동기화 강제(CTO B-1/B-2): 이 목록은 src/HitPan.API/Migrations/SQL/DB-*.sql 파일 집합과
--     '정확히 같아야' 한다. 다음 릴리스에 DB-85 추가 시 여기 INSERT 도 갱신해야 하며,
--     scripts/ddl-smoke-test.sh 가 "소스 DB-*.sql 파일 수 == 이 시드 success=1 행 수"를 자동 대조해
--     한쪽만 갱신하면 CI 빨간불이 된다(손 매직넘버 금지). id 형식은 MigrationRunner.cs:71 정규화형과 일치.
--   app_version 은 이 시드가 clean DDL 유래임을 표시(수동/러너 적용과 구분). 추적용.
INSERT INTO `schema_migrations` (`migration_id`, `app_version`, `success`) VALUES
('DB-02','clean-ddl',1),('DB-03','clean-ddl',1),('DB-04','clean-ddl',1),('DB-05','clean-ddl',1),
('DB-06','clean-ddl',1),('DB-07','clean-ddl',1),('DB-08','clean-ddl',1),('DB-08b','clean-ddl',1),
('DB-09','clean-ddl',1),('DB-10','clean-ddl',1),('DB-11','clean-ddl',1),('DB-12','clean-ddl',1),
('DB-13','clean-ddl',1),('DB-14','clean-ddl',1),('DB-15','clean-ddl',1),('DB-16','clean-ddl',1),
('DB-18','clean-ddl',1),('DB-19','clean-ddl',1),('DB-20','clean-ddl',1),('DB-21','clean-ddl',1),
('DB-22','clean-ddl',1),('DB-23','clean-ddl',1),('DB-24','clean-ddl',1),('DB-25','clean-ddl',1),
('DB-26','clean-ddl',1),('DB-27','clean-ddl',1),('DB-28','clean-ddl',1),('DB-29','clean-ddl',1),
('DB-30','clean-ddl',1),('DB-31','clean-ddl',1),('DB-32','clean-ddl',1),('DB-33','clean-ddl',1),
('DB-34','clean-ddl',1),('DB-35','clean-ddl',1),('DB-36','clean-ddl',1),('DB-37','clean-ddl',1),
('DB-38','clean-ddl',1),('DB-39','clean-ddl',1),('DB-50','clean-ddl',1),('DB-60','clean-ddl',1),
('DB-70','clean-ddl',1),('DB-71','clean-ddl',1),('DB-72','clean-ddl',1),('DB-73','clean-ddl',1),
('DB-74','clean-ddl',1),('DB-75','clean-ddl',1),('DB-76','clean-ddl',1),('DB-77','clean-ddl',1),
('DB-78','clean-ddl',1),('DB-79','clean-ddl',1),('DB-80','clean-ddl',1),('DB-81','clean-ddl',1),
('DB-82','clean-ddl',1),('DB-83','clean-ddl',1),('DB-84','clean-ddl',1),('DB-85','clean-ddl',1),
('DB-86','clean-ddl',1),('DB-87','clean-ddl',1),('DB-88','clean-ddl',1),('DB-89','clean-ddl',1),('DB-90','clean-ddl',1),('DB-91','clean-ddl',1),('DB-92','clean-ddl',1),('DB-93','clean-ddl',1),('DB-94','clean-ddl',1),('DB-95','clean-ddl',1),('DB-96','clean-ddl',1),('DB-97','clean-ddl',1),('DB-98','clean-ddl',1),('DB-99','clean-ddl',1),('DB-100','clean-ddl',1),('DB-101','clean-ddl',1),('DB-102','clean-ddl',1),('DB-103','clean-ddl',1),('DB-104','clean-ddl',1),('DB-105','clean-ddl',1),('DB-106','clean-ddl',1),('DB-107','clean-ddl',1),('DB-108','clean-ddl',1),('DB-109','clean-ddl',1),('DB-110','clean-ddl',1);

--
-- Table structure for table `service_tickets`
--

DROP TABLE IF EXISTS `service_tickets`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `service_tickets` (
  `ticket_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `service_date` date NOT NULL,
  `partner_id` varchar(36) DEFAULT NULL COMMENT '대상 업체',
  `item_id` varchar(36) DEFAULT NULL COMMENT '대상 상품',
  `problem_desc` varchar(1000) DEFAULT NULL COMMENT 'AS 접수 내용',
  `fix_desc` varchar(1000) DEFAULT NULL COMMENT 'AS 처리 내용',
  `fee` decimal(15,2) NOT NULL DEFAULT 0.00,
  `memo` varchar(500) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
  PRIMARY KEY (`ticket_id`),
  UNIQUE KEY `uq_service_tickets_source_hash` (`tenant_id`,`migrated_source_hash`),
  KEY `idx_st_tenant_date` (`tenant_id`,`service_date`),
  KEY `idx_st_tenant_partner` (`tenant_id`,`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='POTHER.DOCAS AS 티켓 (WS-11 축 5)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `status_history`
--

DROP TABLE IF EXISTS `status_history`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `status_history` (
  `id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `table_name` varchar(50) NOT NULL COMMENT '???? ???̺???',
  `record_id` varchar(36) NOT NULL COMMENT '???? ???ڵ? ID',
  `from_status` varchar(50) DEFAULT NULL,
  `to_status` varchar(50) NOT NULL,
  `changed_by` varchar(36) NOT NULL,
  `changed_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `reason` varchar(500) DEFAULT NULL,
  `ip_address` varchar(45) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `fk_sh_employee` (`changed_by`),
  KEY `idx_sh_table_record` (`table_name`,`record_id`),
  KEY `idx_sh_changed_at` (`changed_at`),
  KEY `idx_sh_tenant` (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `stock_adjust_logs`
--

DROP TABLE IF EXISTS `stock_adjust_logs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `stock_adjust_logs` (
  `adjust_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `warehouse_id` varchar(36) NOT NULL,
  `before_qty` decimal(10,2) NOT NULL,
  `after_qty` decimal(10,2) NOT NULL,
  `adjust_qty` decimal(10,2) NOT NULL,
  `before_cost` decimal(15,2) NOT NULL,
  `after_cost` decimal(15,2) NOT NULL,
  `reason` varchar(200) DEFAULT NULL,
  `user_id` varchar(36) NOT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`adjust_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_item` (`tenant_id`,`item_id`),
  KEY `idx_created` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `stock_alerts`
--

DROP TABLE IF EXISTS `stock_alerts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `stock_alerts` (
  `alert_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `alert_type` varchar(20) NOT NULL,
  `current_qty` decimal(10,2) NOT NULL DEFAULT 0.00,
  `safety_qty` decimal(10,2) NOT NULL DEFAULT 0.00,
  `shortage_qty` decimal(10,2) NOT NULL DEFAULT 0.00,
  `partner_id` varchar(36) DEFAULT NULL,
  `order_qty` decimal(10,2) NOT NULL DEFAULT 0.00,
  `bom_id` varchar(36) DEFAULT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'pending',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`alert_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_item` (`tenant_id`,`item_id`),
  KEY `idx_status` (`tenant_id`,`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `stock_ledger`
--

DROP TABLE IF EXISTS `stock_ledger`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `stock_ledger` (
  `ledger_id` bigint(20) NOT NULL AUTO_INCREMENT,
  `tenant_id` varchar(36) NOT NULL,
  `item_id` varchar(36) NOT NULL,
  `warehouse_id` varchar(36) NOT NULL,
  `partner_id` varchar(36) DEFAULT NULL,
  `employee_id` varchar(36) DEFAULT NULL,
  `ledger_date` date NOT NULL,
  `ym` varchar(7) NOT NULL,
  `move_type` varchar(10) NOT NULL,
  `source_type` varchar(30) NOT NULL COMMENT 'sales_delivery/sales_cancel/sales_return/sales_return_cancel/purchase_receipt/direct_purchase/purchase_return/purchase_return_cancel/bom_explosion/bom_explosion_cancel/bom_production/bom_disassemble/adjust/transfer/migration (16차 P3 정합)',
  `source_id` varchar(36) NOT NULL,
  `doc_no` varchar(20) DEFAULT NULL,
  `qty_in` decimal(15,3) NOT NULL,
  `qty_out` decimal(15,3) NOT NULL,
  `unit_cost` decimal(15,4) DEFAULT NULL,
  `supply_amount` decimal(15,2) DEFAULT NULL,
  `memo` varchar(200) DEFAULT NULL,
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
  `created_by` varchar(36) DEFAULT NULL COMMENT '봉합 2026-06-22 8차 DB-P0-01-REGRESS: 실사조정·이송 추적자(StockService AdjustStock/Transfer INSERT). 헌법 #36 코드↔출하DDL 정합',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) COMMENT '봉합 2026-06-22 8차 DB-P0-01-REGRESS: 원장 기록 시각. 재고원장 조회 SELECT(StockService:146)·조정·이송 INSERT 정합',
  PRIMARY KEY (`ledger_id`),
  UNIQUE KEY `uq_stock_ledger_source` (`tenant_id`,`source_type`,`source_id`,`item_id`,`move_type`),
  UNIQUE KEY `uq_stock_ledger_source_hash` (`tenant_id`,`migrated_source_hash`),
  KEY `idx_tenant_item_date` (`tenant_id`,`item_id`,`ledger_date`),
  KEY `idx_tenant_date` (`tenant_id`,`ledger_date`)
) ENGINE=InnoDB AUTO_INCREMENT=131074 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `subscriptions`
--

DROP TABLE IF EXISTS `subscriptions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `subscriptions` (
  `subscription_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `reseller_id` varchar(36) DEFAULT NULL COMMENT '???? ???? ?븮??',
  `platform_id` varchar(36) DEFAULT NULL COMMENT '????',
  `plan_type` varchar(20) NOT NULL DEFAULT 'basic' COMMENT 'basic/pro/enterprise',
  `base_users` tinyint(3) unsigned NOT NULL,
  `extra_users` tinyint(3) unsigned NOT NULL,
  `base_fee` int(11) NOT NULL,
  `extra_fee_per_user` int(11) NOT NULL,
  `annual_discount_rate` decimal(5,2) NOT NULL DEFAULT 0.00 COMMENT '???????? ??????',
  `billing_cycle` varchar(20) NOT NULL,
  `started_at` date NOT NULL,
  `ends_at` date DEFAULT NULL,
  `next_billing_at` date NOT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'trial' COMMENT 'trial/active/suspended/cancelled',
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  PRIMARY KEY (`subscription_id`),
  KEY `idx_sub_reseller` (`reseller_id`),
  KEY `idx_sub_status` (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `sync_tokens`
--

DROP TABLE IF EXISTS `sync_tokens`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sync_tokens` (
  `token_id` char(36) NOT NULL COMMENT 'UUID',
  `tenant_id` char(36) NOT NULL COMMENT '테넌트 ID',
  `token_hash` varchar(128) NOT NULL COMMENT 'SHA-256 hash (평문 토큰 미저장)',
  `issued_at` timestamp NOT NULL DEFAULT current_timestamp() COMMENT '발급 시각',
  `expires_at` timestamp NOT NULL COMMENT '만료 시각 (issued_at + 24h)',
  `revoked_at` timestamp NULL DEFAULT NULL COMMENT '회수 시각 (회전 시 INSERT)',
  `last_used_at` timestamp NULL DEFAULT NULL COMMENT '마지막 사용 시각',
  PRIMARY KEY (`token_id`),
  UNIQUE KEY `uq_token_hash` (`token_hash`),
  KEY `idx_tenant_active` (`tenant_id`,`revoked_at`,`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='[헌법 #5·#23] Sync 토큰 — SHA-256 해시만 저장, 회전 정책';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `device_register_tokens`
--   모바일기기 등록 QR 토큰 (20260811작1 (D), 사장님 결재 2026-08-11)
--   사장님 오더: "모바일 등록기기 버튼 클릭시 QR생성" / "QR토큰전용 테이블 생성해"
--

DROP TABLE IF EXISTS `device_register_tokens`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `device_register_tokens` (
  `token_id` char(36) NOT NULL COMMENT 'UUID',
  `tenant_id` char(36) NOT NULL COMMENT '테넌트 ID',
  `token_hash` varchar(128) NOT NULL COMMENT 'SHA-256 hash — 평문 토큰은 저장하지 않는다(sync_tokens 와 같은 원칙)',
  `issued_by` varchar(36) DEFAULT NULL COMMENT '발급한 대표계정 user_id — QR 을 띄운 사람이 곧 승인자다',
  `issued_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) COMMENT '발급 시각',
  `expires_at` datetime(6) NOT NULL COMMENT '만료 시각 (발급 + 10분). 짧게 두는 이유: QR 이 화면에 떠 있는 동안만 유효해야 한다',
  `used_at` datetime(6) DEFAULT NULL COMMENT '사용 시각 — 1회용. 한 번 쓰면 다시 못 쓴다',
  `used_device_id` varchar(36) DEFAULT NULL COMMENT '이 토큰으로 등록된 기기',
  PRIMARY KEY (`token_id`),
  UNIQUE KEY `uq_devreg_token_hash` (`token_hash`),
  KEY `idx_devreg_tenant_active` (`tenant_id`,`used_at`,`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='모바일기기 등록 QR 토큰 — 10분 만료·1회용, 해시만 저장';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `tax_invoice_items`
--

DROP TABLE IF EXISTS `tax_invoice_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tax_invoice_items` (
  `tax_invoice_item_id` varchar(36) NOT NULL,
  `invoice_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `line_no` smallint(6) NOT NULL,
  `item_name` varchar(100) NOT NULL,
  `quantity` decimal(15,2) NOT NULL DEFAULT 0.00,
  `unit_price` decimal(15,2) NOT NULL DEFAULT 0.00,
  `supply_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `vat_amount` decimal(15,2) NOT NULL DEFAULT 0.00,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`tax_invoice_item_id`),
  UNIQUE KEY `uq_tax_invoice_item_line` (`invoice_id`,`line_no`),
  KEY `idx_tax_invoice_items_tenant` (`tenant_id`),
  CONSTRAINT `fk_tax_invoice_items_invoice` FOREIGN KEY (`invoice_id`) REFERENCES `tax_invoices` (`invoice_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `tax_invoices`
--

DROP TABLE IF EXISTS `tax_invoices`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tax_invoices` (
  `invoice_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `delivery_id` varchar(36) DEFAULT NULL,
  `invoice_no` varchar(32) NOT NULL COMMENT '계산서 번호 (테넌트별 고유)',
  `issued_at` datetime(6) NOT NULL,
  `issued_by` varchar(36) NOT NULL COMMENT '발행자 user_id (감사 추적)',
  `amount_total` decimal(15,2) NOT NULL COMMENT '공급가액',
  `vat_total` decimal(15,2) NOT NULL COMMENT '부가세',
  `status` varchar(16) NOT NULL DEFAULT 'issued' COMMENT 'issued | canceled',
  -- 회수 (2026-06-23, 메타감사): 16차에 한 거래명세서당 issued 1건 차단용 active_delivery_id 생성컬럼+UNIQUE를
  --   넣었으나, 참조 컬럼 delivery_id 가 FK(fk_tax_invoices_delivery) 대상이라 MariaDB가 STORED generated
  --   표현식에서 FK 컬럼 참조를 금지 → 신규설치 clean DDL import 자체가 ERROR 1901 로 실패(세금계산서 테이블
  --   미생성 = ERP 설치 전체 차단). 16차 단독테이블 스모크는 FK가 없어 통과해 이를 못 봤다(반쪽 검증).
  --   사장님 결재(2026-06-23): 생성컬럼·UNIQUE 제거, 중복발행 차단은 IssueAsync 의 'issued 기발행 있으면 차단'
  --   SELECT 체크로 환원(16차 이전 상태). 동시성(초당 2클릭) 차단은 FK 무손상 방식으로 후순위 재설계.
  `etax_status` varchar(16) NOT NULL DEFAULT 'pending' COMMENT 'pending | issued | failed',
  `etax_issued_at` datetime(6) DEFAULT NULL,
  `idempotency_key` varchar(64) DEFAULT NULL COMMENT '발행 요청 시 사용된 Idempotency-Key 헤더 값',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `etax_scheduled_at` datetime(6) DEFAULT NULL COMMENT '② 전자발행 예약 시각',
  `etax_mode` varchar(20) DEFAULT 'scheduled' COMMENT 'scheduled(기본)/immediate(즉시발행)',
  `etax_confirmed_at` datetime(6) DEFAULT NULL COMMENT '③ 전자발행 확정 시각 (홈택스 전송 완료)',
  `hometax_approval_no` varchar(50) DEFAULT NULL COMMENT '홈택스 승인번호',
  `hometax_response` text DEFAULT NULL COMMENT '홈택스 응답 원문 (감사용)',
  `amendment_of` varchar(36) DEFAULT NULL COMMENT '수정계산서일 경우 원본 PK',
  `amendment_reason` varchar(200) DEFAULT NULL COMMENT '수정계산서 사유',
  `migrated_source_hash` char(64) DEFAULT NULL COMMENT 'WS-11 축 2: SHA256 멱등 키',
  `direction` char(1) DEFAULT NULL COMMENT 'S=????, B=???? (TX_IO)',
  `tax_no` char(8) DEFAULT NULL COMMENT 'TX_NO ???ݰ??꼭 ??ȣ',
  `issue_date_yyyymmdd` char(8) DEFAULT NULL COMMENT 'TX_PDT ??????',
  `partner_code` int(11) DEFAULT NULL COMMENT 'TX_BUY ???Ž? ?ŷ?ó ?ڵ?',
  `seq_no` smallint(6) DEFAULT NULL COMMENT 'TX_SEQ ????',
  `sent_at_yyyymmdd` char(8) DEFAULT NULL COMMENT 'TX_SENDDT ?߼???',
  `read_at_yyyymmdd` char(8) DEFAULT NULL COMMENT 'TX_READDT Ȯ????',
  `reported_at_yyyymmdd` char(8) DEFAULT NULL COMMENT 'TX_REPORTDT ?Ű???',
  `remark1` varchar(100) DEFAULT NULL COMMENT 'TX_REM ????1',
  `remark2` varchar(100) DEFAULT NULL COMMENT 'TX_REM1 ????2',
  `source_type` varchar(30) DEFAULT NULL COMMENT '???? ????',
  `source_id` varchar(80) DEFAULT NULL COMMENT '???Ž? PK (TX_IO+TX_NO)',
  PRIMARY KEY (`invoice_id`),
  UNIQUE KEY `uk_tax_invoices_invoice_no` (`tenant_id`,`invoice_no`),
  UNIQUE KEY `uq_tax_invoices_source_hash` (`tenant_id`,`migrated_source_hash`),
  UNIQUE KEY `uq_tax_invoices_io_no` (`tenant_id`,`direction`,`tax_no`),
  UNIQUE KEY `uq_tax_invoices_source` (`tenant_id`,`source_type`,`source_id`),
  KEY `idx_tax_invoices_tenant_issued` (`tenant_id`,`issued_at`),
  KEY `idx_tax_invoices_etax_status` (`tenant_id`,`etax_status`) COMMENT '2/3계층 일괄 발행 조회용',
  KEY `idx_status_scheduled` (`status`,`etax_scheduled_at`),
  KEY `idx_amendment` (`amendment_of`),
  KEY `idx_tax_invoices_delivery` (`delivery_id`),
  CONSTRAINT `fk_tax_invoices_delivery` FOREIGN KEY (`delivery_id`) REFERENCES `sales_deliveries` (`delivery_id`) ON UPDATE CASCADE
  -- fk_tax_invoices_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='3계층 세금계산서 1계층 — 내부 마킹 (DESIGN_PRINCIPLES §7)';
/*!40101 SET character_set_client = @saved_cs_client */;
/*!50003 SET @saved_cs_client      = @@character_set_client */ ;
/*!50003 SET @saved_cs_results     = @@character_set_results */ ;
/*!50003 SET @saved_col_connection = @@collation_connection */ ;
/*!50003 SET character_set_client  = utf8mb4 */ ;
/*!50003 SET character_set_results = utf8mb4 */ ;
/*!50003 SET collation_connection  = utf8mb4_unicode_ci */ ;
/*!50003 SET @saved_sql_mode       = @@sql_mode */ ;
/*!50003 SET sql_mode              = 'STRICT_TRANS_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION' */ ;
DELIMITER ;;
/*!50003 CREATE*/ /*!50017*/ /*!50003 TRIGGER `trg_tax_invoices_issued_lock`
BEFORE UPDATE ON `tax_invoices`
FOR EACH ROW
BEGIN
  IF OLD.`status` IN ('etax_confirmed','amended')
     AND NOT (NEW.`status` = 'amended' AND OLD.`status` = 'etax_confirmed')
  THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = '발행완료된 세금계산서는 수정할 수 없습니다. 수정계산서 발행을 이용하세요.';
  END IF;
END */;;
DELIMITER ;
/*!50003 SET sql_mode              = @saved_sql_mode */ ;
/*!50003 SET character_set_client  = @saved_cs_client */ ;
/*!50003 SET character_set_results = @saved_cs_results */ ;
/*!50003 SET collation_connection  = @saved_col_connection */ ;

--
-- Table structure for table `tenant_certificates`
--

DROP TABLE IF EXISTS `tenant_certificates`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tenant_certificates` (
  `cert_id` varchar(36) NOT NULL COMMENT '인증서 UUID',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트 UUID (멀티테넌시)',
  `cert_type` varchar(20) NOT NULL DEFAULT 'general' COMMENT 'general/financial/tax',
  `subject_name` varchar(200) NOT NULL COMMENT '주체명 (사업자명)',
  `issuer_name` varchar(200) DEFAULT NULL COMMENT '발급기관',
  `serial_no` varchar(100) DEFAULT NULL COMMENT '시리얼번호',
  `valid_from` date NOT NULL COMMENT '유효기간 시작',
  `valid_until` date NOT NULL COMMENT '유효기간 종료',
  `cert_file_encrypted` longblob NOT NULL COMMENT 'PFX 파일 (AES-256 암호화)',
  `password_ref` text NOT NULL COMMENT 'DPAPI 보호된 비밀번호',
  `is_primary` tinyint(1) NOT NULL DEFAULT 0 COMMENT '기본 인증서 여부',
  `status` varchar(20) NOT NULL DEFAULT 'active' COMMENT 'active/expired/revoked',
  `last_used_at` datetime(6) DEFAULT NULL,
  `uploaded_by` varchar(36) NOT NULL COMMENT '업로드 사용자 ID',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`cert_id`),
  KEY `idx_cert_tenant` (`tenant_id`,`status`),
  KEY `idx_cert_primary` (`tenant_id`,`is_primary`),
  KEY `idx_cert_expiry` (`tenant_id`,`valid_until`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='범용인증서 보관소 (PFX=AES-256, PW=DPAPI 이중 보호)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `tenant_devices`
--

DROP TABLE IF EXISTS `tenant_devices`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tenant_devices` (
  `device_id` varchar(36) NOT NULL COMMENT '기기 UUID (클라이언트 localStorage 저장)',
  `tenant_id` varchar(36) NOT NULL,
  `user_id` varchar(36) DEFAULT NULL COMMENT '최근 로그인 사용자',
  `device_type` varchar(10) NOT NULL COMMENT 'pc / mobile / tablet',
  `device_name` varchar(100) DEFAULT NULL COMMENT '기기 별명 (예: 홍길동 사무실 PC)',
  `fingerprint` varchar(64) NOT NULL COMMENT '[옛 기기 호환 전용] 브라우저 환경해시(HFPv2-)·메인PC(MAINPC-). 새 기기는 hardware_id 로 잡는다 (DB-103 격하)',
  `hardware_id` varchar(128) DEFAULT NULL COMMENT '장비넘버(하드웨어 식별값). 서버가 읽는 변하지 않는 값. NULL=아직 못 받은 옛 기기 (DB-103)',
  `ip_address` varchar(50) DEFAULT NULL,
  `user_agent` varchar(500) DEFAULT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'approved' COMMENT 'pending/approved/revoked',
  `auth_key_hash` varchar(64) DEFAULT NULL COMMENT '기기 인증키 SHA-256 (원문 미저장). NULL=아직 발급 안 됨 (DB-88)',
  `auth_key_issued_at` datetime(6) DEFAULT NULL COMMENT '인증키 발급 시각 (DB-88)',
  `registered_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `approved_by` varchar(36) DEFAULT NULL,
  `approved_at` datetime(6) DEFAULT NULL,
  `last_seen_at` datetime(6) DEFAULT NULL,
  `revoked_at` datetime(6) DEFAULT NULL,
  `revoked_reason` varchar(200) DEFAULT NULL,
  `is_main_pc` tinyint(1) NOT NULL DEFAULT 0 COMMENT '메인PC(히트판 본체·DB 보유) 여부. 테넌트당 1대 — 보장은 애플리케이션에서 한다 (DB-86)',
  PRIMARY KEY (`device_id`),
  UNIQUE KEY `uq_tenant_fp` (`tenant_id`,`fingerprint`),
  -- DB-103: 장비넘버는 회사 안에서 유일하다. NULL 은 몇 개든 공존한다(옛 기기 보존).
  UNIQUE KEY `uq_tenant_hardware` (`tenant_id`,`hardware_id`),
  KEY `idx_tenant_type_status` (`tenant_id`,`device_type`,`status`),
  KEY `idx_auth_key_hash` (`auth_key_hash`),
  KEY `idx_user` (`user_id`),
  -- fk_device_tenant 제거 (무결 봉합 2026-06-18): tenants 삭제 FK 제거. tenant_id 컬럼 보존
  CONSTRAINT `fk_device_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='테넌트 기기 목록 — 기기 대수 과금 + 접근 제어';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `device_slot_policy_settings`
--   기기 슬롯 기준값 (DB-104) — 요금 한도를 코드가 아니라 여기서 읽는다(헌법 #11).
--   ⚠️ 값 시드는 회사가 만들어질 때 넣는다(마이그 DB-104 참조). 신규 설치 시점엔 회사가 없어 0행이다.
--     표가 비어 있으면 서비스가 종전 숫자로 떨어지므로(FallbackLimits) 한도가 없어지지 않는다.
--

DROP TABLE IF EXISTS `device_slot_policy_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `device_slot_policy_settings` (
  `policy_id` varchar(36) NOT NULL COMMENT 'PK',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트(헌법 #2 — JWT 클레임에서만 온다)',
  `policy_key` varchar(60) NOT NULL COMMENT '기준값 열쇠. 예: tier.basic.pc_limit',
  `policy_value` int(11) NOT NULL COMMENT '값. 대수·금액 모두 정수 하나로 담는다',
  `value_unit` varchar(20) NOT NULL DEFAULT 'count' COMMENT '단위: count(대)|krw(원)',
  `label` varchar(100) NOT NULL COMMENT '사람이 읽는 이름. 화면에 그대로 보여준다',
  `description` varchar(300) DEFAULT NULL COMMENT '무엇을 뜻하는 값인지',
  `updated_by` varchar(36) DEFAULT NULL COMMENT '누가 고쳤나',
  `updated_reason` varchar(200) DEFAULT NULL COMMENT '왜 고쳤나 — 요금이라 수정 이력이 필수다',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`policy_id`),
  UNIQUE KEY `uk_slot_policy_tenant_key` (`tenant_id`,`policy_key`),
  KEY `idx_slot_policy_lookup` (`tenant_id`,`policy_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='기기 슬롯 기준값 — 요금제가 바뀌면 값만 갈아끼운다(코드 재배포 없이). DB-104';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `tenant_devices_snapshot`
--

DROP TABLE IF EXISTS `tenant_devices_snapshot`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tenant_devices_snapshot` (
  `snapshot_id` char(36) NOT NULL COMMENT 'UUID',
  `tenant_id` char(36) NOT NULL COMMENT '테넌트 ID',
  `device_id` char(36) NOT NULL COMMENT '기기 ID (원본 tenant_devices 참조)',
  `device_name` varchar(100) DEFAULT NULL COMMENT '기기명',
  `registered_at` timestamp NOT NULL COMMENT '등록일',
  `synced_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp() COMMENT '마지막 Pull 시각',
  PRIMARY KEY (`snapshot_id`),
  UNIQUE KEY `uq_tenant_device` (`tenant_id`,`device_id`),
  KEY `idx_tenant_synced` (`tenant_id`,`synced_at`),
  KEY `idx_synced` (`synced_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='[헌법 #18·#22] 백오피스 Pull 복사본 — 3개 컬럼만';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `tenant_employees_snapshot`
--

DROP TABLE IF EXISTS `tenant_employees_snapshot`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tenant_employees_snapshot` (
  `snapshot_id` char(36) NOT NULL COMMENT 'UUID',
  `tenant_id` char(36) NOT NULL COMMENT '테넌트 ID (원본 tenants.tenant_id 참조)',
  `employee_id` char(36) NOT NULL COMMENT '직원 ID (원본 employees.employee_id 참조)',
  `name` varchar(50) NOT NULL COMMENT '이름',
  `email` varchar(100) NOT NULL COMMENT '이메일',
  `position` varchar(30) DEFAULT NULL COMMENT '직급',
  `is_active` tinyint(1) NOT NULL DEFAULT 1 COMMENT '재직여부',
  `synced_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp() COMMENT '마지막 Pull 시각',
  PRIMARY KEY (`snapshot_id`),
  UNIQUE KEY `uq_tenant_employee` (`tenant_id`,`employee_id`),
  KEY `idx_tenant_synced` (`tenant_id`,`synced_at`),
  KEY `idx_synced` (`synced_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='[헌법 #18·#22] 백오피스 Pull 복사본 — 5개 컬럼만. 업무 데이터 절대 금지';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `tenant_etax_settings`
--

DROP TABLE IF EXISTS `tenant_etax_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tenant_etax_settings` (
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트 ID (PK)',
  `issue_mode` varchar(20) NOT NULL DEFAULT 'scheduled' COMMENT 'scheduled(예약)/manual(건별수동)',
  `batch_time` time NOT NULL DEFAULT '18:00:00' COMMENT '매일 일괄발행 시각 (HH:MM)',
  `batch_weekdays` varchar(20) NOT NULL DEFAULT '1,2,3,4,5' COMMENT '발행 요일 CSV (1=월..7=일)',
  `exclude_holidays` tinyint(1) NOT NULL DEFAULT 1 COMMENT '공휴일 발행 제외 (1=제외/0=발행)',
  `notify_before_min` int(11) DEFAULT 30 COMMENT '발행 N분 전 알림 (NULL=알림없음)',
  `notify_manager_id` varchar(36) DEFAULT NULL COMMENT '알림 수신 담당자 (employees.employee_id)',
  `retry_count` int(11) NOT NULL DEFAULT 3 COMMENT '전송 실패 시 재시도 횟수',
  `retry_interval_min` int(11) NOT NULL DEFAULT 60 COMMENT '재시도 간격 (분)',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  `updated_by` varchar(36) DEFAULT NULL,
  PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='테넌트별 전자세금계산서 발행 설정';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `tenant_settings`
--

DROP TABLE IF EXISTS `tenant_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tenant_settings` (
  `tenant_id` varchar(36) NOT NULL,
  `allow_force_price_input` tinyint(1) NOT NULL DEFAULT 1,
  `allow_force_vat_input` tinyint(1) NOT NULL DEFAULT 0,
  `allow_zero_price` tinyint(1) NOT NULL DEFAULT 0,
  `allow_past_edit` tinyint(1) NOT NULL DEFAULT 0,
  `past_edit_password_hash` varchar(100) DEFAULT NULL,
  `allow_force_stock_adjust` tinyint(1) NOT NULL DEFAULT 1,
  `allow_credit_override` tinyint(1) NOT NULL DEFAULT 0,
  `price_deviation_limit` int(11) NOT NULL DEFAULT 50,
  `force_edit_require_password` tinyint(1) NOT NULL DEFAULT 1,
  `stock_eval_method` varchar(20) NOT NULL DEFAULT 'fifo' COMMENT '재고평가방법: fifo/lifo/avg',
  `use_multi_warehouse` tinyint(1) NOT NULL DEFAULT 0 COMMENT '다중창고 사용여부',
  `stock_shortage_alert` tinyint(1) NOT NULL DEFAULT 1 COMMENT '재고부족 알림',
  `allow_minus_stock` tinyint(1) NOT NULL DEFAULT 0 COMMENT '마이너스 재고 허용',
  `price_input_type` varchar(10) NOT NULL DEFAULT 'vat_in' COMMENT '단가입력방식: vat_in/vat_out',
  `auto_vat_adjust` tinyint(1) NOT NULL DEFAULT 1 COMMENT '부가세 자동계산',
  `vat_round_type` varchar(10) NOT NULL DEFAULT 'round' COMMENT '부가세 반올림방식: round/ceil/floor',
  `price_a_rate` decimal(5,2) NOT NULL DEFAULT 0.00 COMMENT '단가A 할인율',
  `price_b_rate` decimal(5,2) NOT NULL DEFAULT 0.00 COMMENT '단가B 할인율',
  `price_c_rate` decimal(5,2) NOT NULL DEFAULT 0.00 COMMENT '단가C 할인율',
  `price_d_rate` decimal(5,2) NOT NULL DEFAULT 0.00 COMMENT '단가D 할인율',
  `price_e_rate` decimal(5,2) NOT NULL DEFAULT 0.00 COMMENT '단가E 할인율',
  `use_credit_limit` tinyint(1) NOT NULL DEFAULT 0 COMMENT '여신한도 사용여부',
  `credit_limit_amount` decimal(18,2) NOT NULL DEFAULT 0.00 COMMENT '기본 여신한도금액',
  `show_purchase_price` tinyint(1) NOT NULL DEFAULT 0 COMMENT '매입단가 표시여부',
  `use_sales_by_employee` tinyint(1) NOT NULL DEFAULT 0 COMMENT '담당자별 매출 집계',
  `use_personal_info_protect` tinyint(1) NOT NULL DEFAULT 0 COMMENT '개인정보보호모드',
  `industry_type` varchar(30) NOT NULL DEFAULT 'general' COMMENT '업종: general/food/elec/plastic/wood',
  PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `tenants`
--

DROP TABLE IF EXISTS `tenants`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tenants` (
  `tenant_id` varchar(36) NOT NULL,
  `platform_id` varchar(36) DEFAULT NULL,
  `tenant_code` varchar(20) NOT NULL,
  `company_name` varchar(100) NOT NULL,
  `biz_no` varchar(100) NOT NULL,
  `ceo_name` varchar(50) NOT NULL,
  `tel` varchar(100) DEFAULT NULL,
  `address` varchar(200) DEFAULT NULL,
  `reseller_id` varchar(36) DEFAULT NULL,
  `max_users` tinyint(3) unsigned NOT NULL DEFAULT 3,
  `status` varchar(20) NOT NULL DEFAULT 'trial',
  `is_locked_from_landing` tinyint(1) NOT NULL DEFAULT 0 COMMENT '1=랜딩에서 들어온 회사정보, ERP 내 수정 금지 (헌법 #35)',
  `bootstrap_at` datetime(6) DEFAULT NULL COMMENT 'ERP 첫 부팅 자동 반영 시점 (헌법 #20 워크플로우 검증용)',
  `trial_ends_at` datetime(6) DEFAULT NULL,
  `db_host` varchar(100) NOT NULL,
  `db_name` varchar(50) NOT NULL,
  `license_key_hash` varchar(256) NOT NULL,
  `reseller_tier` tinyint(3) unsigned NOT NULL,
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  `biz_type` varchar(50) DEFAULT NULL,
  `biz_item` varchar(100) DEFAULT NULL,
  `fax` varchar(20) DEFAULT NULL,
  `zip_code` varchar(10) DEFAULT NULL,
  `logo_url` varchar(200) DEFAULT NULL,
  `email` varchar(100) DEFAULT NULL,
  `tax_type` varchar(20) NOT NULL DEFAULT 'taxable',
  `fiscal_month` int(11) NOT NULL DEFAULT 12,
  `corp_no` varchar(13) DEFAULT NULL,
  `subsidiary_no` varchar(4) DEFAULT NULL,
  `ai_mode` varchar(20) NOT NULL DEFAULT 'hitpan_pool' COMMENT 'hitpan_pool / byok / hybrid',
  `ai_token_monthly_limit` int(11) NOT NULL DEFAULT 100000 COMMENT '월 토큰 한도 (티어 기본값: basic 100K, pro 500K, enterprise 3M)',
  `ai_token_extra` int(11) NOT NULL DEFAULT 0 COMMENT '추가 구매 토큰',
  `anthropic_api_key_encrypted` varchar(512) DEFAULT NULL COMMENT 'BYOK 모드에서 고객사 Anthropic 키 (AES-256 암호화)',
  `anthropic_api_key_last4` varchar(8) DEFAULT NULL COMMENT 'BYOK 키 마지막 4자리 (UI 표시용)',
  `anthropic_key_status` varchar(20) NOT NULL DEFAULT 'none' COMMENT 'none / valid / invalid / expired',
  `subscription_tier` varchar(20) NOT NULL DEFAULT 'basic' COMMENT 'basic / pro / enterprise (subscriptions.plan_type 미러)',
  `extra_device_slots` int(11) NOT NULL DEFAULT 0 COMMENT '추가 구매 디바이스 슬롯 (1슬롯 = PC 1 또는 모바일 2)',
  `homepage` varchar(200) DEFAULT NULL,
  `initial_date` date DEFAULT NULL,
  `e_invoice_server` varchar(200) DEFAULT NULL,
  `e_invoice_id` varchar(100) DEFAULT NULL,
  `e_invoice_enabled` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`tenant_id`),
  UNIQUE KEY `uq_tenant_code` (`tenant_code`)
  -- fk_tenants_reseller + KEY fk_tenant_reseller 제거 (보안 격벽 2026-06-18):
  --   resellers(대리점 마스터)는 백오피스 전용 테이블이라 ERP에서 DROP됨 → tenants가 더 이상 참조 안 함.
  --   tenants 테이블은 ERP 회사 식별 본체라 보존. reseller_id 컬럼은 흔적으로 남아도 FK 없음.
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `user_permissions`
--

DROP TABLE IF EXISTS `user_permissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `user_permissions` (
  `perm_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `user_id` varchar(36) NOT NULL,
  `menu_code` varchar(30) NOT NULL,
  `can_view` tinyint(1) NOT NULL DEFAULT 0,
  `can_create` tinyint(1) NOT NULL DEFAULT 0,
  `can_update` tinyint(1) NOT NULL DEFAULT 0,
  `can_delete` tinyint(1) NOT NULL DEFAULT 0,
  `can_export` tinyint(1) NOT NULL DEFAULT 0,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`perm_id`),
  UNIQUE KEY `uk_user_menu` (`tenant_id`,`user_id`,`menu_code`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_user` (`tenant_id`,`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `user_sessions`
--

DROP TABLE IF EXISTS `user_sessions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `user_sessions` (
  `session_id` varchar(36) NOT NULL,
  `user_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) DEFAULT NULL,
  `ip_address` varchar(50) DEFAULT NULL,
  `user_agent` varchar(500) DEFAULT NULL,
  `login_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `last_active_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `expires_at` datetime(6) DEFAULT NULL COMMENT '세션 만료일시',
  PRIMARY KEY (`session_id`),
  KEY `idx_user` (`user_id`),
  KEY `idx_tenant` (`tenant_id`),
  KEY `idx_active` (`is_active`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `user_terms_consent`
--

DROP TABLE IF EXISTS `user_terms_consent`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `user_terms_consent` (
  `consent_id` char(36) NOT NULL COMMENT 'UUID',
  `tenant_id` char(36) NOT NULL,
  `user_id` char(36) NOT NULL,
  `terms_version` varchar(20) NOT NULL COMMENT 'v2.0.0 등',
  `agree_service` tinyint(1) NOT NULL DEFAULT 0,
  `agree_privacy` tinyint(1) NOT NULL DEFAULT 0,
  `agree_subscription` tinyint(1) NOT NULL DEFAULT 0,
  `agree_data_ownership` tinyint(1) NOT NULL DEFAULT 0 COMMENT '헌법 #22·#24',
  `agree_marketing` tinyint(1) DEFAULT NULL COMMENT '선택',
  `agreed_at` datetime(3) NOT NULL,
  `client_ip` varchar(45) NOT NULL COMMENT 'IPv4/IPv6',
  `user_agent` varchar(500) DEFAULT NULL,
  PRIMARY KEY (`consent_id`),
  KEY `idx_user_terms_consent_tenant_user` (`tenant_id`,`user_id`,`terms_version`),
  KEY `idx_user_terms_consent_agreed_at` (`agreed_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='첫 로그인 약관 4건 강제 동의 INSERT ONLY (헌법 #24)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `users`
--

DROP TABLE IF EXISTS `users`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `users` (
  `user_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `email` varchar(100) NOT NULL,
  `password_hash` varchar(256) NOT NULL,
  `user_name` varchar(50) NOT NULL,
  `role` longtext NOT NULL,
  `account_type` varchar(20) NOT NULL DEFAULT 'tenant_user' COMMENT 'platform_admin/reseller_admin/tenant_admin/tenant_user',
  `is_parent` tinyint(1) NOT NULL DEFAULT 0 COMMENT '1=부모계정 (랜딩 가입자, tenant당 1명) / 0=자식계정 (ERP 내 생성)',
  `reseller_id` varchar(36) DEFAULT NULL,
  `platform_id` varchar(36) DEFAULT NULL,
  `dept_id` varchar(36) DEFAULT NULL,
  `phone` varchar(20) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL,
  `failed_login_count` int(11) NOT NULL DEFAULT 0,
  `lockout_end` datetime(6) DEFAULT NULL,
  `last_login_at` datetime(6) DEFAULT NULL,
  `password_changed_at` datetime(6) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL,
  `created_by` varchar(36) DEFAULT NULL,
  `updated_at` datetime(6) NOT NULL,
  `updated_by` varchar(36) DEFAULT NULL,
  `is_deleted` tinyint(1) NOT NULL DEFAULT 0,
  `deleted_at` datetime(6) DEFAULT NULL,
  `emp_name` varchar(50) DEFAULT NULL,
  `department` varchar(50) DEFAULT NULL,
  `position` varchar(50) DEFAULT NULL,
  `hire_date` date DEFAULT NULL,
  `memo` varchar(200) DEFAULT NULL,
  PRIMARY KEY (`user_id`),
  UNIQUE KEY `uq_tenant_email` (`tenant_id`,`email`),
  KEY `idx_users_account_type` (`account_type`),
  KEY `idx_users_reseller` (`reseller_id`),
  KEY `idx_tenant_active` (`tenant_id`,`is_active`),
  KEY `idx_email` (`email`),
  KEY `idx_users_tenant_parent` (`tenant_id`,`is_parent`,`is_deleted`)
  -- fk_users_tenant 제거 (사장님 결재 2026-06-18 하브루타 봉합 P0): users.tenant_id는
  --   백오피스 tenants 테이블을 FK 참조하던 멀티테넌트 잔재. ERP 계정 계층은 부모/자식 둘뿐이고
  --   빈 DB에 tenants 시드 0건이라 부모계정 INSERT가 ERROR 1452 → 로그인 영구 불가의 진범이었음.
  --   tenant_id 컬럼·값·인덱스는 "회사 식별자"로 보존(헌법 #20 데이터 무손상). FK 참조 무결성만 제거.
  --   회사정보 마스터는 local_company가 단일 보유.
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Temporary table structure for view `v_partner_aging_buckets`
--

DROP TABLE IF EXISTS `v_partner_aging_buckets`;
/*!50001 DROP VIEW IF EXISTS `v_partner_aging_buckets`*/;
SET @saved_cs_client     = @@character_set_client;
SET character_set_client = utf8mb4;
/*!50001 CREATE VIEW `v_partner_aging_buckets` AS SELECT
 1 AS `tenant_id`,
  1 AS `partner_id`,
  1 AS `partner_name`,
  1 AS `open_invoices`,
  1 AS `bucket_0_30`,
  1 AS `bucket_31_60`,
  1 AS `bucket_61_90`,
  1 AS `bucket_90_plus`,
  1 AS `total_unpaid` */;
SET character_set_client = @saved_cs_client;

--
-- Temporary table structure for view `v_partner_balance`
--

DROP TABLE IF EXISTS `v_partner_balance`;
/*!50001 DROP VIEW IF EXISTS `v_partner_balance`*/;
SET @saved_cs_client     = @@character_set_client;
SET character_set_client = utf8mb4;
/*!50001 CREATE VIEW `v_partner_balance` AS SELECT
 1 AS `partner_id`,
  1 AS `partner_name`,
  1 AS `tenant_id`,
  1 AS `total_sales`,
  1 AS `total_received`,
  1 AS `receivable_balance`,
  1 AS `total_purchase`,
  1 AS `total_paid`,
  1 AS `payable_balance`,
  1 AS `calculated_at` */;
SET character_set_client = @saved_cs_client;

--
-- Temporary table structure for view `v_partner_balance_calc`
--

DROP TABLE IF EXISTS `v_partner_balance_calc`;
/*!50001 DROP VIEW IF EXISTS `v_partner_balance_calc`*/;
SET @saved_cs_client     = @@character_set_client;
SET character_set_client = utf8mb4;
/*!50001 CREATE VIEW `v_partner_balance_calc` AS SELECT
 1 AS `tenant_id`,
  1 AS `partner_id`,
  1 AS `partner_name`,
  1 AS `total_sales`,
  1 AS `total_receipt`,
  1 AS `total_purchase`,
  1 AS `total_payment`,
  1 AS `receivable`,
  1 AS `payable` */;
SET character_set_client = @saved_cs_client;

--
-- Temporary table structure for view `v_partner_collections_total`
--

DROP TABLE IF EXISTS `v_partner_collections_total`;
/*!50001 DROP VIEW IF EXISTS `v_partner_collections_total`*/;
SET @saved_cs_client     = @@character_set_client;
SET character_set_client = utf8mb4;
/*!50001 CREATE VIEW `v_partner_collections_total` AS SELECT
 1 AS `tenant_id`,
  1 AS `partner_id`,
  1 AS `total_collection` */;
SET character_set_client = @saved_cs_client;

--
-- Temporary table structure for view `v_partner_payments_total`
--

DROP TABLE IF EXISTS `v_partner_payments_total`;
/*!50001 DROP VIEW IF EXISTS `v_partner_payments_total`*/;
SET @saved_cs_client     = @@character_set_client;
SET character_set_client = utf8mb4;
/*!50001 CREATE VIEW `v_partner_payments_total` AS SELECT
 1 AS `partner_id`,
  1 AS `total_paid` */;
SET character_set_client = @saved_cs_client;

--
-- Temporary table structure for view `v_partner_purchase_orders_total`
--

DROP TABLE IF EXISTS `v_partner_purchase_orders_total`;
/*!50001 DROP VIEW IF EXISTS `v_partner_purchase_orders_total`*/;
SET @saved_cs_client     = @@character_set_client;
SET character_set_client = utf8mb4;
/*!50001 CREATE VIEW `v_partner_purchase_orders_total` AS SELECT
 1 AS `partner_id`,
  1 AS `total_purchase` */;
SET character_set_client = @saved_cs_client;

--
-- Temporary table structure for view `v_partner_receipts_total`
--

DROP TABLE IF EXISTS `v_partner_receipts_total`;
/*!50001 DROP VIEW IF EXISTS `v_partner_receipts_total`*/;
SET @saved_cs_client     = @@character_set_client;
SET character_set_client = utf8mb4;
/*!50001 CREATE VIEW `v_partner_receipts_total` AS SELECT
 1 AS `partner_id`,
  1 AS `total_received` */;
SET character_set_client = @saved_cs_client;

--
-- Temporary table structure for view `v_partner_sales_orders_total`
--

DROP TABLE IF EXISTS `v_partner_sales_orders_total`;
/*!50001 DROP VIEW IF EXISTS `v_partner_sales_orders_total`*/;
SET @saved_cs_client     = @@character_set_client;
SET character_set_client = utf8mb4;
/*!50001 CREATE VIEW `v_partner_sales_orders_total` AS SELECT
 1 AS `partner_id`,
  1 AS `total_sales` */;
SET character_set_client = @saved_cs_client;

--
-- Temporary table structure for view `v_stock_integrity_check`
--

DROP TABLE IF EXISTS `v_stock_integrity_check`;
/*!50001 DROP VIEW IF EXISTS `v_stock_integrity_check`*/;
SET @saved_cs_client     = @@character_set_client;
SET character_set_client = utf8mb4;
/*!50001 CREATE VIEW `v_stock_integrity_check` AS SELECT
 1 AS `tenant_id`,
  1 AS `item_id`,
  1 AS `item_name`,
  1 AS `stock_qty`,
  1 AS `ledger_qty`,
  1 AS `diff`,
  1 AS `status` */;
SET character_set_client = @saved_cs_client;

--
-- Table structure for table `warehouses`
--

DROP TABLE IF EXISTS `warehouses`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `warehouses` (
  `warehouse_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `wh_code` varchar(20) NOT NULL,
  `wh_name` varchar(50) NOT NULL,
  `wh_type` longtext NOT NULL,
  `location` varchar(100) DEFAULT NULL,
  `zip_code` varchar(10) DEFAULT NULL COMMENT '우편번호 (DB-110, 20260825작19)',
  `address` varchar(255) DEFAULT NULL COMMENT '주소 — 우편번호 찾기로 채운다 (DB-110)',
  `address_detail` varchar(255) DEFAULT NULL COMMENT '상세주소 — 사람이 입력한다 (DB-110)',
  `is_active` tinyint(1) NOT NULL,
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  PRIMARY KEY (`warehouse_id`),
  UNIQUE KEY `uq_tenant_code` (`tenant_id`,`wh_code`)
  -- fk_warehouses_tenant 제거 (무결 봉합 2026-06-18): tenants 백오피스 계층 삭제 FK 제거. tenant_id 컬럼 보존
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `work_in_process`
--

DROP TABLE IF EXISTS `work_in_process`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `work_in_process` (
  `wip_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `order_item_id` varchar(36) DEFAULT NULL,
  `item_id` varchar(36) NOT NULL,
  `stage` varchar(20) NOT NULL COMMENT 'cut/assemble/paint/finished',
  `qty` decimal(15,3) NOT NULL,
  `started_at` datetime(6) DEFAULT NULL,
  `completed_at` datetime(6) DEFAULT NULL,
  `operator_employee_id` varchar(36) DEFAULT NULL,
  `memo` varchar(200) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`wip_id`),
  KEY `idx_wip_stage` (`tenant_id`,`stage`),
  KEY `idx_wip_item` (`tenant_id`,`item_id`),
  KEY `fk_wip_item` (`item_id`),
  CONSTRAINT `fk_wip_item` FOREIGN KEY (`item_id`) REFERENCES `items` (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='재공품 (재단→조립→도장 3단계)';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `workflow_settings`
--

DROP TABLE IF EXISTS `workflow_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `workflow_settings` (
  `setting_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `setting_key` varchar(60) NOT NULL,
  `setting_value` varchar(200) NOT NULL,
  `value_type` varchar(20) NOT NULL,
  `is_active` tinyint(1) NOT NULL,
  PRIMARY KEY (`setting_id`),
  UNIQUE KEY `uq_tenant_key` (`tenant_id`,`setting_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Final view structure for view `v_partner_aging_buckets`
--

/*!50001 DROP VIEW IF EXISTS `v_partner_aging_buckets`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb4 */;
/*!50001 SET character_set_results     = utf8mb4 */;
/*!50001 SET collation_connection      = utf8mb4_unicode_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `v_partner_aging_buckets` AS select `p`.`tenant_id` AS `tenant_id`,`p`.`partner_id` AS `partner_id`,`p`.`partner_name` AS `partner_name`,count(`sd`.`delivery_id`) AS `open_invoices`,sum(case when to_days(curdate()) - to_days(`sd`.`delivery_date`) <= 30 then `sd`.`total_amount` + `sd`.`vat_amount` else 0 end) AS `bucket_0_30`,sum(case when to_days(curdate()) - to_days(`sd`.`delivery_date`) between 31 and 60 then `sd`.`total_amount` + `sd`.`vat_amount` else 0 end) AS `bucket_31_60`,sum(case when to_days(curdate()) - to_days(`sd`.`delivery_date`) between 61 and 90 then `sd`.`total_amount` + `sd`.`vat_amount` else 0 end) AS `bucket_61_90`,sum(case when to_days(curdate()) - to_days(`sd`.`delivery_date`) > 90 then `sd`.`total_amount` + `sd`.`vat_amount` else 0 end) AS `bucket_90_plus`,sum(`sd`.`total_amount` + `sd`.`vat_amount`) AS `total_unpaid` from (`partners` `p` join `sales_deliveries` `sd` on(`sd`.`tenant_id` = `p`.`tenant_id` and `sd`.`partner_id` = `p`.`partner_id` and `sd`.`status` = 'confirmed')) where !exists(select 1 from `collections` `c` where `c`.`tenant_id` = `sd`.`tenant_id` and `c`.`ref_doc_type` = 'sales_delivery' and `c`.`ref_doc_id` = `sd`.`delivery_id` and `c`.`is_active` = 1 limit 1) group by `p`.`tenant_id`,`p`.`partner_id`,`p`.`partner_name` */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;

--
-- Final view structure for view `v_partner_balance`
--

/*!50001 DROP VIEW IF EXISTS `v_partner_balance`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb3 */;
/*!50001 SET character_set_results     = utf8mb3 */;
/*!50001 SET collation_connection      = utf8mb3_general_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `v_partner_balance` AS select `p`.`partner_id` AS `partner_id`,`p`.`partner_name` AS `partner_name`,`p`.`tenant_id` AS `tenant_id`,coalesce(`s`.`total_sales`,0) AS `total_sales`,coalesce(`rc`.`total_received`,0) AS `total_received`,coalesce(`s`.`total_sales`,0) - coalesce(`rc`.`total_received`,0) AS `receivable_balance`,coalesce(`po`.`total_purchase`,0) AS `total_purchase`,coalesce(`py`.`total_paid`,0) AS `total_paid`,coalesce(`po`.`total_purchase`,0) - coalesce(`py`.`total_paid`,0) AS `payable_balance`,current_timestamp() AS `calculated_at` from ((((`partners` `p` left join `v_partner_sales_orders_total` `s` on(`s`.`partner_id` = `p`.`partner_id`)) left join `v_partner_receipts_total` `rc` on(`rc`.`partner_id` = `p`.`partner_id`)) left join `v_partner_purchase_orders_total` `po` on(`po`.`partner_id` = `p`.`partner_id`)) left join `v_partner_payments_total` `py` on(`py`.`partner_id` = `p`.`partner_id`)) */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;

--
-- Final view structure for view `v_partner_balance_calc`
--

/*!50001 DROP VIEW IF EXISTS `v_partner_balance_calc`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb4 */;
/*!50001 SET character_set_results     = utf8mb4 */;
/*!50001 SET collation_connection      = utf8mb4_unicode_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `v_partner_balance_calc` AS select `p`.`tenant_id` AS `tenant_id`,`p`.`partner_id` AS `partner_id`,`p`.`partner_name` AS `partner_name`,coalesce(`s`.`total_sales`,0) AS `total_sales`,coalesce(`r`.`total_receipt`,0) AS `total_receipt`,coalesce(`pu`.`total_purchase`,0) AS `total_purchase`,coalesce(`py`.`total_payment`,0) AS `total_payment`,coalesce(`s`.`total_sales`,0) - coalesce(`r`.`total_receipt`,0) AS `receivable`,coalesce(`pu`.`total_purchase`,0) - coalesce(`py`.`total_payment`,0) AS `payable` from ((((`partners` `p` left join (select `sales_deliveries`.`tenant_id` AS `tenant_id`,`sales_deliveries`.`partner_id` AS `partner_id`,sum(`sales_deliveries`.`total_amount` + `sales_deliveries`.`vat_amount`) AS `total_sales` from `sales_deliveries` where `sales_deliveries`.`status` in ('confirmed','invoiced') and `sales_deliveries`.`is_deleted` = 0 group by `sales_deliveries`.`tenant_id`,`sales_deliveries`.`partner_id`) `s` on(`s`.`tenant_id` = `p`.`tenant_id` and `s`.`partner_id` = `p`.`partner_id`)) left join (select `collections`.`tenant_id` AS `tenant_id`,`collections`.`partner_id` AS `partner_id`,sum(`collections`.`amount`) AS `total_receipt` from `collections` where `collections`.`is_active` = 1 group by `collections`.`tenant_id`,`collections`.`partner_id`) `r` on(`r`.`tenant_id` = `p`.`tenant_id` and `r`.`partner_id` = `p`.`partner_id`)) left join (select `purchase_receipts`.`tenant_id` AS `tenant_id`,`purchase_receipts`.`partner_id` AS `partner_id`,sum(`purchase_receipts`.`total_amount` + `purchase_receipts`.`vat_amount`) AS `total_purchase` from `purchase_receipts` where `purchase_receipts`.`status` = 'confirmed' group by `purchase_receipts`.`tenant_id`,`purchase_receipts`.`partner_id`) `pu` on(`pu`.`tenant_id` = `p`.`tenant_id` and `pu`.`partner_id` = `p`.`partner_id`)) left join (select `payments`.`tenant_id` AS `tenant_id`,`payments`.`partner_id` AS `partner_id`,sum(`payments`.`amount`) AS `total_payment` from `payments` where `payments`.`is_active` = 1 group by `payments`.`tenant_id`,`payments`.`partner_id`) `py` on(`py`.`tenant_id` = `p`.`tenant_id` and `py`.`partner_id` = `p`.`partner_id`)) where `p`.`is_deleted` = 0 */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;

--
-- Final view structure for view `v_partner_collections_total`
--

/*!50001 DROP VIEW IF EXISTS `v_partner_collections_total`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb4 */;
/*!50001 SET character_set_results     = utf8mb4 */;
/*!50001 SET collation_connection      = utf8mb4_unicode_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `v_partner_collections_total` AS select `c`.`tenant_id` AS `tenant_id`,`c`.`partner_id` AS `partner_id`,sum(`c`.`amount`) AS `total_collection` from `collections` `c` where `c`.`is_active` = 1 group by `c`.`tenant_id`,`c`.`partner_id` */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;

--
-- Final view structure for view `v_partner_payments_total`
--

/*!50001 DROP VIEW IF EXISTS `v_partner_payments_total`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb3 */;
/*!50001 SET character_set_results     = utf8mb3 */;
/*!50001 SET collation_connection      = utf8mb3_general_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `v_partner_payments_total` AS select `payments`.`partner_id` AS `partner_id`,sum(`payments`.`amount`) AS `total_paid` from `payments` where `payments`.`payment_type` = 'payment' group by `payments`.`partner_id` */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;

--
-- Final view structure for view `v_partner_purchase_orders_total`
--

/*!50001 DROP VIEW IF EXISTS `v_partner_purchase_orders_total`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb3 */;
/*!50001 SET character_set_results     = utf8mb3 */;
/*!50001 SET collation_connection      = utf8mb3_general_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `v_partner_purchase_orders_total` AS select `purchase_orders`.`partner_id` AS `partner_id`,sum(`purchase_orders`.`total_amount` + `purchase_orders`.`vat_amount`) AS `total_purchase` from `purchase_orders` where `purchase_orders`.`status` not in ('cancelled','draft') group by `purchase_orders`.`partner_id` */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;

--
-- Final view structure for view `v_partner_receipts_total`
--

/*!50001 DROP VIEW IF EXISTS `v_partner_receipts_total`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb3 */;
/*!50001 SET character_set_results     = utf8mb3 */;
/*!50001 SET collation_connection      = utf8mb3_general_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `v_partner_receipts_total` AS select `payments`.`partner_id` AS `partner_id`,sum(`payments`.`amount`) AS `total_received` from `payments` where `payments`.`payment_type` = 'receipt' group by `payments`.`partner_id` */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;

--
-- Final view structure for view `v_partner_sales_orders_total`
--

/*!50001 DROP VIEW IF EXISTS `v_partner_sales_orders_total`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb3 */;
/*!50001 SET character_set_results     = utf8mb3 */;
/*!50001 SET collation_connection      = utf8mb3_general_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `v_partner_sales_orders_total` AS select `sales_orders`.`partner_id` AS `partner_id`,sum(`sales_orders`.`total_amount` + `sales_orders`.`vat_amount`) AS `total_sales` from `sales_orders` where `sales_orders`.`status` not in ('cancelled','draft') group by `sales_orders`.`partner_id` */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;

--
-- Final view structure for view `v_stock_integrity_check`
--

/*!50001 DROP VIEW IF EXISTS `v_stock_integrity_check`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb4 */;
/*!50001 SET character_set_results     = utf8mb4 */;
/*!50001 SET collation_connection      = utf8mb4_unicode_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `v_stock_integrity_check` AS select `s`.`tenant_id` AS `tenant_id`,`s`.`item_id` AS `item_id`,`i`.`item_name` AS `item_name`,`s`.`current_qty` AS `stock_qty`,coalesce(`l`.`ledger_qty`,0) AS `ledger_qty`,`s`.`current_qty` - coalesce(`l`.`ledger_qty`,0) AS `diff`,case when abs(`s`.`current_qty` - coalesce(`l`.`ledger_qty`,0)) > 0.01 then 'MISMATCH' else 'OK' end AS `status` from ((`item_stock` `s` join `items` `i` on(`i`.`item_id` = `s`.`item_id`)) left join (select `stock_ledger`.`tenant_id` AS `tenant_id`,`stock_ledger`.`item_id` AS `item_id`,sum(`stock_ledger`.`qty_in`) - sum(`stock_ledger`.`qty_out`) AS `ledger_qty` from `stock_ledger` group by `stock_ledger`.`tenant_id`,`stock_ledger`.`item_id`) `l` on(`l`.`tenant_id` = `s`.`tenant_id` and `l`.`item_id` = `s`.`item_id`)) */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;

-- ─── 코드성 시드: common_codes ───
INSERT INTO `common_codes` (`code_id`, `tenant_id`, `code_group`, `code_value`, `code_label`, `sort_order`, `is_active`, `created_at`) VALUES ('05362a1e-e39a-423b-8c65-8a9cc4296f4d',NULL,'UNIT','M','M',2,1,'2026-04-12 19:26:22'),
('1b1c9edb-1046-4a51-8a65-b7d9e1dd7e72',NULL,'PAY_METHOD','credit','credit',3,1,'2026-04-12 19:26:22'),
('2f23c61c-e3be-4235-b19c-86388c757d63',NULL,'ITEM_TYPE','expense','expense',3,1,'2026-04-12 19:26:22'),
('4526b077-b225-4ac3-a9c0-afaa10b1e3e7',NULL,'WH_TYPE','return','return',3,1,'2026-04-12 19:26:22'),
('51940728-4f1a-4ac7-8853-977081a0ff5c',NULL,'PAY_METHOD','card','card',1,1,'2026-04-12 19:26:22'),
('567a6deb-9c73-4a3b-bb7b-eb127f7af221',NULL,'WH_TYPE','consign','consign',1,1,'2026-04-12 19:26:22'),
('5a50fe34-32b1-4e0d-9209-1e4083da7684',NULL,'ITEM_TYPE','material','material',1,1,'2026-04-12 19:26:22'),
('631cfd2f-c0a9-4b43-99b7-cd81be0d1fc7',NULL,'PARTNER_TYPE','both','both',2,1,'2026-04-12 19:26:22'),
('649d5dfc-306b-447e-ab53-3dff2222d177',NULL,'PARTNER_TYPE','customer','customer',0,1,'2026-04-12 19:26:22'),
('64cd0f4d-ffbf-4764-89d6-fbe4ec9f03dd',NULL,'PAY_METHOD','transfer','transfer',2,1,'2026-04-12 19:26:22'),
('7862db00-2ef7-4826-8efb-9431a7afa15c',NULL,'PARTNER_TYPE','supplier','supplier',1,1,'2026-04-12 19:26:22'),
('812757f0-6d67-4445-8165-2ce8e9e02bef',NULL,'PAY_METHOD','cash','cash',0,1,'2026-04-12 19:26:22'),
('86fb2c6d-356b-471a-9a69-ee600314f935',NULL,'ITEM_TYPE','semi','semi',2,1,'2026-04-12 19:26:22'),
('9159be3f-a0a2-49e9-809b-b8d0be363ef1',NULL,'WH_TYPE','defect','defect',2,1,'2026-04-12 19:26:22'),
('b0e05533-70d7-4cfc-bb9d-71c3838d9980',NULL,'UNIT','BOX','BOX',4,1,'2026-04-12 19:26:22'),
('b9c0f7f4-9d07-4426-b0fb-50a07f977bc0',NULL,'UNIT','SET','SET',5,1,'2026-04-12 19:26:22'),
('bf1cebff-3bb2-4f4a-87ab-3734de4a5fc8',NULL,'UNIT','KG','KG',1,1,'2026-04-12 19:26:22'),
('d4b003c9-0307-47c6-80fd-e9c7edd6c3c5',NULL,'UNIT','EA','EA',0,1,'2026-04-12 19:26:22'),
('d73f74d1-3195-4d84-933d-f5daf5ca1ddd',NULL,'WH_TYPE','normal','normal',0,1,'2026-04-12 19:26:22'),
('da9b8d49-0e71-4e43-9a9f-b4d0e11be747',NULL,'ITEM_TYPE','product','product',0,1,'2026-04-12 19:26:22'),
('eb6a171c-64b0-4216-8cc9-c402b6a6b481',NULL,'UNIT','L','L',3,1,'2026-04-12 19:26:22');

--
-- ═══════════════════════════════════════════════════════════════
-- 사내 메신저 (그룹웨어 단계9) — DB-101 · 2026-08-13
-- ═══════════════════════════════════════════════════════════════
-- 🔴 사장님 지시: "단체대화, 부서별 대화, 1:1대화 모두 가능해야함"
--    "메신저에서 각 그룹웨어 문서들 연결 가능해야 함" (연결만 — 생성·결재는 원래 화면이)
--    "승인 혹은 반려시, 최초 발신인(신청자)에게 메시지 보내야됨"
--    "결재봇, 메시지봇 공유해도 될듯. 다만 메시지인지, 결재안내인지는 안내" → msg_kind
--    "파일전송은 최소한으로" → 20MB · 디스크 보관 · 3중 한도
--

DROP TABLE IF EXISTS `chat_rooms`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `chat_rooms` (
  `room_id` varchar(36) NOT NULL COMMENT '방 PK',
  `tenant_id` varchar(36) NOT NULL COMMENT '테넌트 (헌법 #2)',
  `room_type` varchar(10) NOT NULL COMMENT 'direct(1:1) · dept(부서) · group(단체)',
  `room_name` varchar(100) DEFAULT NULL COMMENT '단체방만. 1:1 은 NULL',
  `dept_id` varchar(36) DEFAULT NULL COMMENT '부서방만. 부서 0건이어도 정상(고객사가 설정할 일)',
  `direct_key` varchar(80) DEFAULT NULL COMMENT '1:1 중복 방지 — 사원ID 2개 정렬 결합',
  `created_by` varchar(36) NOT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`room_id`),
  UNIQUE KEY `uq_chat_direct` (`tenant_id`,`direct_key`),
  KEY `idx_chat_rooms_tenant_type` (`tenant_id`,`room_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='사내 메신저 방 — 1:1·부서·단체 3종';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `chat_room_members`
--

DROP TABLE IF EXISTS `chat_room_members`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `chat_room_members` (
  `member_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `room_id` varchar(36) NOT NULL,
  `employee_id` varchar(36) NOT NULL COMMENT '사원 ID (user_id 아님 — 알림·결재와 같은 축)',
  `last_read_at` datetime(6) DEFAULT NULL COMMENT '읽음의 유일한 근거',
  `joined_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `left_at` datetime(6) DEFAULT NULL COMMENT 'NULL=참여중. 나가도 줄을 지우지 않는다',
  PRIMARY KEY (`member_id`),
  UNIQUE KEY `uq_chat_member` (`room_id`,`employee_id`),
  KEY `idx_chat_member_my` (`tenant_id`,`employee_id`,`left_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='메신저 방 참여자 — last_read_at 하나로 읽음 판정';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `chat_messages`
--

DROP TABLE IF EXISTS `chat_messages`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `chat_messages` (
  `message_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `room_id` varchar(36) NOT NULL,
  `sender_id` varchar(36) NOT NULL COMMENT '보낸 사원. 결재 안내도 결재한 사람 ID',
  `msg_kind` varchar(10) NOT NULL DEFAULT 'text' COMMENT '딱지 — text(메시지)·approval(결재)·file(파일)',
  `body` varchar(2000) NOT NULL,
  `ref_type` varchar(20) DEFAULT NULL COMMENT 'approval·leave·expense·payroll·contract·report',
  `ref_id` varchar(36) DEFAULT NULL COMMENT '원본 문서 ID — 누르면 그 화면으로',
  `ref_title` varchar(200) DEFAULT NULL COMMENT '미리보기용 제목만. 내용 아님',
  `sent_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `deleted_at` datetime(6) DEFAULT NULL COMMENT '숨김만. 원문은 남는다',
  PRIMARY KEY (`message_id`),
  KEY `idx_chat_msg_room` (`tenant_id`,`room_id`,`sent_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='메신저 메시지 — 문서는 참조만, 삭제는 숨김만';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `chat_files`
--

DROP TABLE IF EXISTS `chat_files`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `chat_files` (
  `file_id` varchar(36) NOT NULL COMMENT 'PK. 저장 파일명도 이것 — 원래 이름 쓰면 경로 조작',
  `tenant_id` varchar(36) NOT NULL,
  `room_id` varchar(36) NOT NULL,
  `message_id` varchar(36) NOT NULL,
  `original_name` varchar(255) NOT NULL COMMENT '보여주기용만',
  `stored_path` varchar(500) NOT NULL COMMENT 'chat-files/{tenant_id}/{yyyyMM}/{file_id}.{ext}',
  `content_type` varchar(100) NOT NULL DEFAULT 'application/octet-stream',
  `file_size` bigint(20) NOT NULL COMMENT '20MB 한도',
  `uploaded_by` varchar(36) NOT NULL,
  `uploaded_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`file_id`),
  KEY `idx_chat_files_room` (`tenant_id`,`room_id`),
  KEY `idx_chat_files_msg` (`message_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='메신저 파일 — 실제 파일은 디스크, 여기엔 경로만';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `chat_file_settings`
--

DROP TABLE IF EXISTS `chat_file_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `chat_file_settings` (
  `tenant_id` varchar(36) NOT NULL,
  `max_file_mb` int(11) NOT NULL DEFAULT 20 COMMENT '파일 한 개 (사장님 확정 20MB)',
  `max_room_mb` int(11) NOT NULL DEFAULT 500 COMMENT '방 하나 누적',
  `max_tenant_mb` int(11) NOT NULL DEFAULT 5120 COMMENT '회사 전체 누적 (5GB)',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='메신저 파일 한도 — 파일이 ERP를 넘어뜨리지 못하게. 업무 데이터는 무관';
/*!40101 SET character_set_client = @saved_cs_client */;

/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*M!100616 SET NOTE_VERBOSITY=@OLD_NOTE_VERBOSITY */;

-- Dump completed on 2026-06-18 14:36:47
