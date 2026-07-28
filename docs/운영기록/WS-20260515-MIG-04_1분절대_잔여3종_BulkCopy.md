# WS-20260515-MIG-04 — 1분 절대 봉합: bank_transactions / cashbook / expenses BulkCopy 전환

> **발행:** 2026-05-15 PM 브라운킴 (자체 작성)
> **수행:** 5/16 DB매니저 + 백엔드매니저 (입회: 본부장)
> **마감:** 5/17 23:59
> **선행:** 진범 #1 (712ce65) collections BulkCopy 패턴 검증 완료
> **참조:** [MIGRATION_MAPPING_OFFICIAL.md §3-6~§3-8 + §8-A](../governance/MIGRATION_MAPPING_OFFICIAL.md)

---

## 1. 목적

헌법 #26 (1분 절대) 달성 — 마이그 333s → 60s 이하. 잔여 3종 (bank_transactions / cashbook / expenses) 모두 row-by-row INSERT → MySqlBulkCopy 정공법 패턴 적용. collections 봉합 (712ce65) 패턴을 그대로 복제.

## 2. 현황

| 테이블 | 현재 시간 | 행 수 (5/14) | 패턴 |
|---|---|---|---|
| bank_transactions | ~150s | 87,808 | row-by-row INSERT, 멱등 키 컬럼 부재 |
| cashbook | ~67s | 20,175 | row-by-row INSERT, 멱등 키 컬럼 부재 |
| expenses | ~71s | 27,639 | row-by-row INSERT, 멱등 키 컬럼 부재 |
| **합계** | **~288s** | 135,622 | 1분 절대 위반 |

## 3. PK 정답 (MSSQL DOCF6/DOCF7/BANKF sys.indexes 5/15 추출)

| 테이블 | PK 정답 | 현행 코드 누락 |
|---|---|---|
| BANKF → bank_transactions | `BK_NO + BK_YMD + BK_JWASU + BK_JEN` | **BK_JWASU (smallint)** |
| DOCF6 → cashbook | `AC_YMD + AC_JWASU + AC_JEN` | **AC_JWASU + AC_JEN** (AC_SGU만 읽음) |
| DOCF7 → expenses | `SC_KCODE + SC_DT + SC_SAWON + SC_SUN` | **SC_SUN (smallint)** |

## 4. 봉합 액션 (3 단계)

### 단계 1: DDL ALTER (DB매니저)

```sql
-- bank_transactions
ALTER TABLE bank_transactions
  ADD COLUMN source_type VARCHAR(30) NULL,
  ADD COLUMN source_id VARCHAR(80) NULL,
  ADD COLUMN migrated_source_hash VARCHAR(64) NULL,
  ADD UNIQUE KEY uq_bank_tx_source (tenant_id, source_type, source_id);

-- cashbook
ALTER TABLE cashbook
  ADD COLUMN source_type VARCHAR(30) NULL,
  ADD COLUMN source_id VARCHAR(80) NULL,
  ADD COLUMN migrated_source_hash VARCHAR(64) NULL,
  ADD UNIQUE KEY uq_cashbook_source (tenant_id, source_type, source_id);

-- expenses
ALTER TABLE expenses
  ADD COLUMN source_type VARCHAR(30) NULL,
  ADD COLUMN source_id VARCHAR(80) NULL,
  ADD COLUMN migrated_source_hash VARCHAR(64) NULL,
  ADD UNIQUE KEY uq_expenses_source (tenant_id, source_type, source_id);
```

### 단계 2: 마이그 코드 (백엔드매니저)

`MdbMigrationService.cs` 패턴 = collections (712ce65)와 동형:

**공통 구조:**
```csharp
// 1) ORDER BY = PK 정답 순서
var dt = ReadMdbTable(oleConn, "SELECT * FROM <TBL> ORDER BY <PK 정답>");

// 2) in-memory 변환 (XxxRow DTO)
var rows = new List<XxxRow>(dt.Rows.Count);
foreach (DataRow r in dt.Rows) {
    var sourceId = $"mig-{...PK 컬럼 조합...}";  // 인위적 rowIdx 절대 금지
    rows.Add(new XxxRow { ..., SourceId = sourceId, ... });
}

// 3) MySqlConnection + MySqlTransaction 시 BulkCopy 경로 분기
if (Db is MySqlConnection mysqlConn && tx is MySqlTransaction mysqlTx) {
    return await BulkCopyXxxAsync(mysqlConn, mysqlTx, rows, ct);
}

// 4) BulkCopyXxxAsync 정공법:
//    a. CREATE TEMPORARY TABLE <stage> LIKE <target>
//    b. UNIQUE 인덱스 DROP (stage만, target 영향 0)
//    c. MySqlBulkCopy.WriteToServerAsync(dataTable)
//    d. INSERT IGNORE INTO target SELECT * FROM stage
//    e. DROP TEMPORARY TABLE
```

#### 4-1. bank_transactions

```csharp
var dt = ReadMdbTable(oleConn,
    "SELECT * FROM BANKF ORDER BY BK_NO, BK_YMD, BK_JWASU, BK_JEN");

foreach (DataRow r in dt.Rows) {
    var bkNo = GetStr(r, "BK_NO");
    var bkYmd = GetStr(r, "BK_YMD");
    var bkJwasu = GetInt(r, "BK_JWASU");      // ❗ 5/14까지 안 읽던 컬럼
    var bkJen = GetStr(r, "BK_JEN");
    var sourceId = $"mig-{bkNo}-{bkYmd}-{bkJwasu:D5}-{bkJen}";
    // ... DTO에 적재
}
```

#### 4-2. cashbook

```csharp
var dt = ReadMdbTable(oleConn,
    "SELECT * FROM DOCF6 ORDER BY AC_YMD, AC_JWASU, AC_JEN");

foreach (DataRow r in dt.Rows) {
    var acYmd = GetStr(r, "AC_YMD");
    var acJwasu = GetInt(r, "AC_JWASU");      // ❗ 신규 컬럼
    var acJen = GetStr(r, "AC_JEN");          // ❗ 신규 컬럼 (AC_SGU와 별개)
    var sourceId = $"mig-{acYmd}-{acJwasu:D5}-{acJen}";
    // ...
}
```

**중요:** 기존 AC_SGU 기반 income/expense 분기 로직은 유지. AC_JEN은 멱등 키 용도로만 추가.

#### 4-3. expenses

```csharp
var dt = ReadMdbTable(oleConn,
    "SELECT * FROM DOCF7 ORDER BY SC_KCODE, SC_DT, SC_SAWON, SC_SUN");

foreach (DataRow r in dt.Rows) {
    var scKcode = GetStr(r, "SC_KCODE");
    var scDt = GetStr(r, "SC_DT");
    var scSawon = GetStr(r, "SC_SAWON");
    var scSun = GetInt(r, "SC_SUN");          // ❗ 신규 컬럼
    var sourceId = $"mig-{scKcode}-{scDt}-{scSawon}-{scSun:D5}";
    // ...
}
```

#### 4-4. BulkCopyXxxAsync 헬퍼

collections (712ce65)의 `BulkCopyCollectionsAsync` 그대로 복제:
- TEMPORARY staging 생성
- UNIQUE 인덱스 DROP (staging만, INSERT IGNORE 우회용)
- MySqlBulkCopy.WriteToServerAsync (BulkCopyTimeout=600s)
- INSERT IGNORE INTO target SELECT FROM staging
- 마지막 finally에서 DROP TEMPORARY TABLE

### 단계 3: 검증 (DB매니저)

```sql
-- 1분 절대 게이트
-- 마이그 직후 로그에서:
-- "[MDB마이그레이션] bank_transactions 정공법 완료: 후보 87808행 → INSERT N행, 총 Mms"
-- M < 15,000 (15초 이내) 목표

-- 13/13 PASS 카드 갱신
SELECT 'bank_transactions' AS card, COUNT(*) AS rows, 87808 AS target, COUNT(*)-87808 AS diff
FROM bank_transactions WHERE tenant_id = @tenant
UNION ALL
SELECT 'cashbook', COUNT(*), 20175, COUNT(*)-20175 FROM cashbook WHERE tenant_id = @tenant
UNION ALL
SELECT 'expenses', COUNT(*), 27639, COUNT(*)-27639 FROM expenses WHERE tenant_id = @tenant;

-- 멱등 키 중복 0건
SELECT 'bank_transactions' AS tbl, COUNT(*) - COUNT(DISTINCT source_id) AS dup
FROM bank_transactions WHERE tenant_id = @tenant AND source_type = 'migration'
UNION ALL
SELECT 'cashbook', COUNT(*) - COUNT(DISTINCT source_id) FROM cashbook
WHERE tenant_id = @tenant AND source_type = 'migration'
UNION ALL
SELECT 'expenses', COUNT(*) - COUNT(DISTINCT source_id) FROM expenses
WHERE tenant_id = @tenant AND source_type = 'migration';

-- 두 번 마이그 실행 후 행 수 동일 확인 (멱등성)
-- 1회차 INSERT N행 → 2회차 INSERT 0행 (중복 IGNORE)
```

## 5. 검증 게이트

| 게이트 | 통과 조건 |
|---|---|
| 빌드 | errors 0 + 신규 경고 0 (헌법 #19) |
| ALTER 멱등 | 두 번 실행 시 IF NOT EXISTS 또는 사전 점검으로 안전 |
| 마이그 멱등 | 두 번 실행해도 sourceId 중복 0 (uq_*_source UNIQUE) |
| 1분 절대 | 3 테이블 합계 60초 이내 (collections 5초 + bank 15초 + cashbook 5초 + expenses 5초 = 30초 목표) |
| 13/13 PASS | 3 테이블 모두 target row 수 일치 (diff = 0) |

## 6. 영향 범위

- **컬럼 추가만** — 기존 INSERT 코드는 source_type/source_id NULL로 동작 (영향 0)
- **UNIQUE 인덱스** — 운영 row는 source_type NULL이라 충돌 없음
- **마이그 코드 정공법 전환** — 기존 legacy fallback은 collections와 동일하게 유지

## 7. 보안 (헌법 #29 정합)

- ALTER TABLE 실행은 사장님 결재 + 본부장 입회
- demo.hitpan.kr 검증은 PM 단독 금지

## 8. 예상 절감

| 테이블 | 현재 | BulkCopy 후 | 절감 |
|---|---|---|---|
| bank_transactions | 150s | 15s | -135s |
| cashbook | 67s | 5s | -62s |
| expenses | 71s | 5s | -66s |
| **합계** | **288s** | **25s** | **-263s** |

추가로 진범 #1 collections (이미 봉합)와 합치면 **333s → 30s 이하** 달성 가능. 헌법 #26 1분 절대 통과.

---

**작성: PM 브라운킴 2026-05-15 15:00**
**문서 ID: WS-20260515-MIG-04**
**선행 패턴 참조:** commit `712ce65` (collections BulkCopyCollectionsAsync 200줄)
