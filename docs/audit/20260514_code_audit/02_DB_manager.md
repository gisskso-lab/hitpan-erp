# 02. DB 매니저 정독 보고서

**작성:** DB 매니저 (Harvard 석사, Oracle 30년)
**일자:** 2026-05-14 새벽

---

## 1. SQL·트랜잭션 영역 진단 표

| 테이블 | 청크 | 주요 line | BulkInsert | tx 경계 | utf8mb4 | 인덱스 | 헌법 |
|---|---|---|---|---|---|---|---|
| partners | 단일 tx | L:347-361, L:420 | ✅ MySqlBulkCopy | L:165 전체 | ✅ unicode_ci | ✅ tenant FK | ✅ |
| items | 단일 tx | L:437-484 | ✅ | L:165 | ✅ | ✅ | ✅ |
| bom_headers/items | 단일 tx | L:500-551 | ✅ 순차 | L:165 | ✅ | ✅ FK | ✅ |
| employees | 단일 tx | L:568-626 | ✅ AES (L:610-614) | L:165 | ✅ | ✅ | ✅ #5 |
| stock_ledger | 청크 1000 | L:767-771 | ✅ | L:165 ⚠️ | ✅ | ✅ | ⚠️ 단일 큰 tx |
| DOCFB 입출고 | 초기 5000 | L:764, L:794 | ✅ | 동일 전체 | ✅ | ❌ 누락 | ⚠️ 50만 |
| collections | 단일 tx | L:823-855 | ✅ | L:165 ★ 진앙 | ✅ | ✅ | ✅ |
| tax_invoices | 우선 판단 | L:1142-1151 | ✅ 의존성 | L:165 | ✅ | ✅ | ✅ |

---

## 2. 발견한 DB 함정 3개

### 함정 #1: 100만 행 단일 트랜잭션 폭발 (L:165, L:203)
```csharp
using var tx = _db.BeginTransaction();
// EnsureMigrationWarehouse + 16개 테이블 마이그 = 100만+ 행
tx.Commit();  // L:203
```
- DOCFB 50만 + DOCF2/F1 50만 + 수금·경비·세금계산서 ≈ **100만+ 행 단일 tx**
- undo log 폭발: `innodb_log_file_size=96MB(기본)` → W3 §2.3 권고 512MB도 부족 가능
- 좀비 롤백 15분+ 의 근본 원인
- 헌법 #3 검증 불가 (일부 실패 시 전체 ROLLBACK)

### 함정 #2: 세션 변수 누락 (L:155-161)
```csharp
SET SESSION unique_checks=0, foreign_key_checks=0,
            innodb_lock_wait_timeout=600,
            net_read_timeout=600, net_write_timeout=600,
            max_statement_time=0
```
- ✅ lock_wait_timeout 50→600 개선
- ❌ **`innodb_flush_log_at_trx_commit=2` 누락** (W3 §2.3 권고)
- 결과: redo log fsync 매 commit → 5~10배 느림
- 100만 행 60분 목표 달성 불가

### 함정 #3: stock_ledger 복합 키 재개 불가 (L:794)
```csharp
var sourceId = $"mig-{GetStr(row, "IJ_DT")}-{GetShort(row, "IJ_SEQ")}-{io}-{seq}";
if (sourceId.Length > 36) sourceId = sourceId[..36]; // 잘림 위험
```
- DOCFB 5컬럼 복합 PK: `(IJ_DT, IJ_IO, IJ_SEQ, IJ_BUY, IJ_SUN)`
- sourceId 단순 문자열 → 재개 불가 (UUID 아님)
- W3_CHUNK §3.4: "11개 테이블 OLEDB SELECT에 ORDER BY 누락 → 재개 시 중복·누락 위험"

---

## 3. 옵션 B 청크 분리 권고

### 청크 크기 권고
| 단계 | 테이블 | 청크 | 근거 |
|---|---|---|---|
| 1. 마스터 | partners, items, COSTNO | 단일 tx | <5,000행 |
| 2. BOM | bom_headers + items | 헤더 단위 | FK 의존 |
| 3. 거래 헤더-상세 | DOCF2+DOCF1 | 1,000 헤더 | last_header_pk 관리 |
| 4. 입출고 | DOCFB | **5,000 시작 → AIMD** | 50만/600초 = 833 rows/sec |
| 5. 후처리 | 수금, 경비, 세금계산서 | 1,000 | DOCFB 후 FK 안전 |
| 6. 원장 | journal_lines | 1,000 | INSERT ONLY 검증 필수 |

### 인덱스 비활성화
```sql
ALTER TABLE stock_ledger DISABLE KEYS;
-- 마이그
ALTER TABLE stock_ledger ENABLE KEYS;
```

### 체크포인트 재개 흐름
```
1. migration_checkpoints.status = 'running' 로드
2. last_pk_value JSON 파싱 → WHERE PK > VALUES
3. OLEDB SELECT ORDER BY PK ASC 강제
4. 첫 청크 = 저장된 chunk_size (AIMD 상태 보존)
5. 에러 분류: deadlock → 청크 절반 / duplicate → 행 폴백 / fk_missing → 사장님 개입
```

### 체크리스트
- [ ] stock_ledger, journal_lines 스키마 확인 (PK, 인덱스)
- [ ] OLEDB SELECT 23개 전수 ORDER BY 추가
- [ ] `innodb_flush_log_at_trx_commit=2` 마이그 중만 (try/finally)
- [ ] `innodb_log_file_size` 512MB+ (기본 96MB)
- [ ] `max_allowed_packet=64MB`
- [ ] BulkCopy timeout=0 (L:267 OK)
- [ ] unique_checks/foreign_key_checks=1 복귀 (L:219 OK)

---

## 4. 서브에이전트(DB 개발자 3명) 분담

| 담당 | 작업 | 우선 | 시간 |
|---|---|---|---|
| DB매니저 | 1.DOCFB 스키마 재검토 2.청크 tx 경계 최적화 3.MariaDB 튜닝 검증 | P0 | 2h |
| 개발자 1 | 1.OLEDB ORDER BY 23개 추가 2.source_hash 멱등 3.행 폴백 모드 | P0 | 4h |
| 개발자 2 | 1.AIMD 청크 알고리즘 2.migration_checkpoints 로직 3.에러 분류(E001~E007) | P1 | 6h |
| 개발자 3 | 1.100만 시뮬 MDB 생성 2.통합 테스트 + 성능 측정 3.중단·재개 3종 검증 | P1 | 5h |

**선행:** DB매니저 스키마 검증 → 개발자 1 ORDER BY 시작. 개발자 2 AIMD 완료 → 개발자 3 시뮬 가능.
