# 🟡 W2 박제 — 500 에러 (approver_name) approval_lines 스키마 마이그

> **작성**: 2026-05-26 오전 PM 브라운킴
> **트리거**: 사장님 5/26 옵션 B 결재 (즉시 봉합 보류, W2 별개 가도)
> **마감**: W2 (5/27~5/31) DB 매니저 + 백엔드 매니저 호출 가도
> **5/29 본런 영향**: 0
> **8/24 베타 영향**: 0

---

## 🚨 사고 영역 정직 박제

### 사장님 발견 (5/26 새벽)
```
GET http://localhost:5234/api/approval/pending 500 (Internal Server Error)
MySqlException: Unknown column 'al.approver_name' in 'SELECT'
   at HitPan.Application.Services...
```

### PM 진단 (5/26 오전)

| 영역 | 상태 |
|---|---|
| 코드 (`ApprovalLineService.cs:36`) | `FROM approval_lines al` + `al.approver_name` SELECT |
| DDL (`DB-15_phase4_approval_collection.sql:36`) | `approver_name VARCHAR(50) NOT NULL` 정의 |
| **실제 DB `approval_lines` 스키마** | 🚨 **완전히 다른 스키마** (옛 버전) |

### 실제 DB 옛 스키마
```
Field: approval_line_id, tenant_id, name, description, sort_order, is_active
```

### 코드가 기대하는 신 스키마 (DB-15)
```
Field: line_id, tenant_id, doc_type, seq_no, approver_id, approver_name, role_label, delegate_id, ...
```

### 사고 시간선
1. 4/20 PM 닥터스트레인지 `approval_lines` 옛 스키마 생성 (단순 카테고리)
2. 5/?? PM이 결재 시스템 본격 가도 시 DB-15 작성 (신 스키마)
3. DB-15 SQL `CREATE TABLE IF NOT EXISTS` → 이미 존재해서 **신 스키마 ALTER 안 됨**
4. 코드는 신 스키마 SELECT 시도 → 컬럼 없음 → 500 에러
5. 5/26 사장님 발견

---

## 🎯 봉합 가도 영역 (W2)

### Step 1: 데이터 백업 (안전)
```sql
CREATE TABLE approval_lines_backup_20260527 AS SELECT * FROM approval_lines;
SELECT COUNT(*) FROM approval_lines_backup_20260527;
```

### Step 2: 옛 테이블 RENAME (롤백 가능)
```sql
RENAME TABLE approval_lines TO approval_lines_legacy_20260527;
```

### Step 3: DB-15 신 스키마 생성
```sql
-- DB-15_phase4_approval_collection.sql 30~50줄 그대로 실행
CREATE TABLE `approval_lines` (
  `line_id`            VARCHAR(36)    NOT NULL,
  `tenant_id`          VARCHAR(36)    NOT NULL,
  `doc_type`           VARCHAR(30)    NOT NULL,
  `seq_no`             INT            NOT NULL,
  `approver_id`        VARCHAR(36)    NOT NULL,
  `approver_name`      VARCHAR(50)    NOT NULL,
  `role_label`         VARCHAR(30)    DEFAULT NULL,
  `delegate_id`        VARCHAR(36)    DEFAULT NULL,
  `delegate_name`      VARCHAR(50)    DEFAULT NULL,
  `delegate_start`     DATE           DEFAULT NULL,
  `delegate_end`       DATE           DEFAULT NULL,
  `is_active`          TINYINT(1)     NOT NULL DEFAULT 1,
  `created_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at`         DATETIME(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`line_id`),
  UNIQUE KEY `uq_tenant_doctype_seq` (`tenant_id`, `doc_type`, `seq_no`),
  KEY `idx_approver` (`tenant_id`, `approver_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

### Step 4: 옛 데이터 마이그 (있으면)
```sql
-- approval_lines_legacy의 카테고리 데이터를 신 스키마로 옮길지 결정
-- PM 권고: 옛 데이터는 사용 안 됨 (단순 카테고리), 마이그 불요
SELECT COUNT(*) FROM approval_lines_legacy_20260527;
```

### Step 5: 검증
```sql
-- 500 에러 났던 코드 정합 확인
SELECT COUNT(*) FROM approval_lines al WHERE al.approver_name IS NOT NULL;
```

### Step 6: 브라우저 검증
- `/api/approval/pending` → 200 OK
- 결재 대기 메뉴 정상 표시

---

## 🚨 결재 영역 (W2 가도 시 사장님 결재 필수)

### Step 2 (RENAME) 사장님 사전 결재 영역 ⭐
- 헌법 #29 (인프라 사전 승인) 정합
- 데이터 손실 시 백업 (Step 1)으로 즉시 롤백 가능

### Step 3 (CREATE) PM 자체 가도
- 신규 테이블 생성, 사고 위험 0

### Step 4 (마이그) 사장님 결재
- 옛 데이터 사용 영역 사장님 확인 필요

---

## 📋 5중 검증 (헌법 #23 정합)

| 검증 | 영역 | 가도 |
|---|---|---|
| ① 작업지시서 | W2 작지서 발행 | PM |
| ② 매니저 리뷰 | DB 매니저 + 백엔드 매니저 | 2명 |
| ③ SAST | 영향 없음 (DB ALTER) | - |
| ④ DAST | OWASP ZAP 결재 대기 메뉴 | 검증팀장 |
| ⑤ 데이터 최소주의 | 백업 + 본사 전송 0 | DB 매니저 |

---

## 🟡 영향 영역 (정직 박제)

### 사용자 영향
- 결재 대기 메뉴 1개 사고 (사용자 0명)
- 다른 메뉴 (전자세금계산서, 매출/매입, 마이그) 영향 0

### 절대 게이트 영향
- 5/29 마이그 본런: **0** ✅
- 6/14 작B v3.0: **0** ✅
- 6/29 1단계 ERP: **결재 메뉴 봉합 정합** ⚠️ (필수)
- 8/24 베타: **결재 메뉴 필요 영역** ⚠️ (필수)

### 권고 가도 시점
- **5/27~5/28**: DB 매니저 + 백엔드 매니저 호출
- **5/29 본런 후**: 즉시 봉합 (W2 잔여 영역 우선순위 1)
- **6/14 작B v3.0 전**: 결재 메뉴 정합 확인 필수

---

## 🎯 PM W2 가도 책무

### 5/27 (화)
- DB 매니저 호출 + Step 1~3 가도 결재 받기
- 백엔드 매니저 호출 + 코드 추가 영향 영역 검증

### 5/28 (수)
- Step 1~3 가도 + 검증
- `/api/approval/pending` 200 OK 확정

### 5/29 (목) — 마이그 본런 D-Day
- 결재 메뉴 영향 0 확인
- 본런 통과 우선

### 5/30~5/31
- 5중 검증 통과
- W2 결산 보고

---

**작성**: 2026-05-26 오전 PM 브라운킴
**상태**: W2 박제 완료, 5/27 DB 매니저 호출 가도 결재 예정
**다음 가도**: 5/27 (화) 09:00 DB 매니저 + 백엔드 매니저 동시 호출
