-- =============================================================
-- DB-84: 스키마 버전 추적 테이블 — schema_migrations (고리4 ①단계)
-- =============================================================
-- 무엇:
--   MigrationRunner(Dapper DB-*.sql 적용 주체)가 "어떤 DB-NN_*.sql 이 적용됐는지"를 기록하는 단일 진실원.
--   업데이트 시 ALTER 를 자동 적용하려면 "무엇이 이미 적용됐나"의 기준점이 있어야 멱등(중복 적용 0)·
--   재적용·롤백 판단이 가능하다. 현재 시스템엔 이 기준점이 전무했다(설계서 §1-4·R5).
--
-- __efmigrationshistory 와의 관계 (중복 아님 — 역할이 다르다):
--   · __efmigrationshistory = EF Core 의 마이그레이션 이력 테이블. 단, 본 프로젝트는
--     src/HitPan.Infrastructure/Migrations/*.cs (EF 마이그 클래스)가 0개이며 EF Core 는
--     런타임 DbContext 쿼리 전용으로만 쓴다. clean DDL 의 __efmigrationshistory 는 초기
--     dotnet ef 사용 흔적의 비활성(inert) 잔재로, 현재 어떤 코드도 INSERT/SELECT 하지 않는다.
--   · schema_migrations = Dapper 계열 raw SQL 마이그(src/HitPan.API/Migrations/SQL/DB-*.sql)의
--     적용 이력. 본 프로젝트의 실제 ALTER 이력은 전적으로 DB-*.sql 이며, 이를 추적할 테이블이
--     없었다. 따라서 EF 용(__efmigrationshistory)과 Dapper 용(schema_migrations)은 추적 대상이
--     서로 달라 역할이 겹치지 않는다. EF 마이그를 부활시킬 계획도 없으므로 둘은 공존 가능.
--
-- 대상 DB: hitpan_erp (고객 PC 로컬 ERP DB). 신규 고객 PC 설치 단일 진실원
--          installer/hitpan_db_clean.sql 에 편입한다(헌법 #36, 122→123→124).
--
-- 헌법: #1 추가만(기존 무수정) / #13 DESCRIBE 정신(기존 추적테이블 부재 → 신설) /
--       #16 무관(단일 DDL) / #17 InnoDB 명시 + utf8mb4_unicode_ci /
--       #19 errors0/warnings0 / #36 clean DDL 편입
-- 이력: 2026-06-29 고리4 ①단계 — 롤백·재적용·멱등의 기준점 신설.
--       ②Velopack PoC·③롤백 상태머신은 본 단계 범위 밖(설계서 §6.1 단계분할 결재).
--
-- 멱등 설계:
--   · migration_id 를 UNIQUE 로 두어 같은 DB-NN 을 두 번 기록할 수 없게 한다(MigrationRunner skip 근거).
--   · success=0(실패) 행도 남긴다 → 재시도 시 "지난번 어디서 실패했나" 추적 + 동일 id 재기록은
--     INSERT ... ON DUPLICATE KEY UPDATE 로 success/applied_at 갱신(부분 실패 후 재적용 멱등).
-- =============================================================

CREATE TABLE IF NOT EXISTS schema_migrations (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    migration_id VARCHAR(100) NOT NULL COMMENT '적용된 마이그 식별자(예: "DB-84" 또는 파일명 "DB-84_schema_migrations.sql"). MigrationRunner 가 파일명에서 추출',
    app_version VARCHAR(20) NULL COMMENT '이 마이그를 적용한 앱 버전(SemVer, 예: 1.3.0). 업데이트 동반 마이그의 출처 추적용. 수동 적용 시 NULL 허용',
    checksum VARCHAR(64) NULL COMMENT '마이그 .sql 파일 내용의 SHA-256(무결성). 적용된 파일이 사후 변조됐는지 검출용. 미산출 시 NULL',
    success TINYINT(1) NOT NULL DEFAULT 1 COMMENT '적용 성공 여부. 1=성공(다음번 skip 대상). 0=실패(재적용 대상, 실패 지점 추적)',
    applied_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '적용 시각',
    UNIQUE KEY uq_schema_migrations_migration_id (migration_id),
    INDEX idx_schema_migrations_applied (applied_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='스키마 마이그(DB-*.sql) 적용 이력(고리4 ①) — 멱등·재적용·롤백 기준점. __efmigrationshistory(EF, inert)와 별개 — 헌법 #36';
