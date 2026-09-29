-- =============================================================
-- DB-135: 고객 PC 로컬 ERP 테이블 — 업데이트 「시도 기록」(20260929작3 · 설계 §12 · 사장님 9/30 「1번으로」)
-- - local_update_attempts: 고객이 [예]를 한 번 누르면 워치독이 그 [예]로 **한 번** 시도하고, 그 시도를 한 행으로 남긴다.
--   · 한 [예](local_update_consents.id) = 한 시도 = 한 행. consent_id UNIQUE — 같은 [예]로 두 번 시작하면 DB 가 막는다.
--   · 워치독이 시작 때 result='in_progress' 로 INSERT(덮어쓰기 아님), 끝날 때 결과·ended_at 으로 닫는다.
--   · ERP 로그인 판정(업데이트 미완료 안내)이 이 표의 최신 1행을 읽는다.
-- 왜 새 표인가: 기존 적용결과 표는 옛 워치독이 직접 읽고 쓰는 보호 표라 칸을 늘릴 수 없다(설계 §12 머리말).
--   그래서 기존 표 3개는 **손대지 않고** 새 표만 만든다. 이 파일은 새 표 생성 한 문장뿐이다.
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB). 출하 DDL installer/hitpan_db_clean.sql 에 같은 칸으로 편입(헌법 #36).
--   워치독 자가생성(WatchdogStatusWriter.AttemptsCreateSql)도 같은 칸·키·엔진이다(게이트 G-R3).
-- 헌법: #1 추가만 / #13 DESCRIBE(local_update_consents.update_version = varchar(20)) / #16 무관(단일 문장) /
--       #17 InnoDB / #30 로컬 자가완결 / #36 출하 DDL 편입. FK 없음 — 보호 표에 제약을 걸지 않는다.
-- 되돌림: 추가형(새 표만) — 옛 코드는 이 표를 모른다. 직전 트리 재게시로 되돌림 완결(설계 §12-7).
-- =============================================================

CREATE TABLE IF NOT EXISTS `local_update_attempts` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `consent_id` bigint(20) NOT NULL COMMENT '이 시도를 연 [예](local_update_consents.id) — 한 [예] = 한 시도',
  `update_version` varchar(20) NOT NULL COMMENT '시도한 버전(local_update_consents.update_version 과 같은 형)',
  `result` varchar(20) NOT NULL COMMENT 'in_progress|success|blocked|rolled_back|rollback_failed|failed',
  `detail` text DEFAULT NULL COMMENT '워치독 고정 문구 머리(ERP 판정용 · 화면 비노출)',
  `started_at` datetime(3) NOT NULL COMMENT '시작 시각(DB NOW(3)) — 진행 중 경과는 이 칸',
  `ended_at` datetime(3) DEFAULT NULL COMMENT '끝난 시각(DB NOW(3)) · NULL=진행 중',
  `created_at` datetime(3) NOT NULL DEFAULT current_timestamp(3) COMMENT '로컬 적재 시각',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_local_update_attempts_consent` (`consent_id`),
  KEY `idx_local_update_attempts_ver` (`update_version`,`consent_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
