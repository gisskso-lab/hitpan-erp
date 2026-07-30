# 01. CTO 풀스택 코드 정독 보고서

**작성:** CTO (래리 앨리슨/데이비드 박급)
**일자:** 2026-05-14 새벽
**대상:** MdbMigrationService 1,764줄 + MigrationController + JobStore + Razor + 옵션 B 작업지시서
**임무:** 풀스택 횡단 분석. 주석까지 정독.

---

## 1. 풀스택 흐름도

```
[MdbMigration.razor]
  • 경로 입력 + 비번 (hotfix 2026-05-13)
  • [미리보기] → GET /api/migration/legacy-mdb/preview
  • [이관 시작] → POST /api/migration/legacy-mdb/start
  • 2초 폴링 → GET /api/migration/legacy-mdb/status/{jobId}
        ↓
[MigrationController]
  • Scoped: MdbMigrationService, IServiceScopeFactory
  • Singleton: MigrationJobStore (_jobStore)
  • POST /start → Create job → Task.Run (HttpContext 끊김 무관, 새 스코프)
  • Exception Handler: OleDb·Win32·FileNotFound 등 (헌법 #15 빈 catch 0건)
        ↓
[MigrationJobStore]
  • ConcurrentDictionary<jobId, MigrationJob>
  • Single-server only (베타). 클러스터 시 Redis 교체 필수
  • 체크포인트 부재 (옵션 B에서 추가)
        ↓
[MdbMigrationService — MigrateCoreAsync] ⚠️ 5/14 새벽 사고 진앙지
  • SET SESSION (unique_checks=0, lock_wait=600 등)
  • BeginTransaction (line 165) ─────────────────┐
  •   EnsureMigrationWarehouse + Employee       │
  •   16개 테이블 순차 마이그:                    │ 단일 거대 tx
  •   ─ Partners (DOCF8) → BulkInsert            │ 100만+ 행
  •   ─ Items (DOCFS) → BulkInsert               │ undo log 폭발
  •   ─ BOM (DOCRT) → 2단                        │ 좀비 롤백 15분+
  •   ─ Employees + AES 암호화                   │
  •   ─ Transactions (DOCF2+F1) → 4단            │
  •   ─ StockLedger (DOCFB, 116K+)               │
  •   ─ Collections (DOCF5, 614K+) ← 진범        │
  •   ─ Cashbook, Expenses, Bills, …             │
  •   tx.Commit() ────────────────────────────── ┘
        ↓
[MariaDB] — 단일 tx COMMIT 또는 전체 ROLLBACK
```

---

## 2. 발견한 구조적 함정 3개

### 함정 #1: 거대 단일 트랜잭션의 본질적 위험
- **위치:** `MdbMigrationService.cs:164` `using var tx = _db.BeginTransaction();`
- **본질:** 헌법 #20 "끊김 금지"의 **잘못된 해석**. 단일 tx = InnoDB undo log 폭발 → 롤백 15분 → 다음 시도 불가 → 좀비 락 누적
- **반증:** 5/14 새벽 사고 진단서 진범 #3 명시
- **코드 임팩트:** `tx.Commit()` (line 202) 전까지 16개 테이블이 같은 tx에서 대기

### 함정 #2: IServiceScopeFactory 패턴의 Lifetime 불일치
- **위치:** `MigrationController.cs:193-200`
- **문제:** JobStore = Singleton vs MdbMigrationService = Scoped. Task.Run 내 새 스코프에서 서비스 해결은 맞으나, JobStore 업데이트와 Service 사이 race condition 가능성
- **현황:** ConcurrentDictionary로 가까스로 안전. 구조적으로 정상 아님 (Redis 전환 시 복잡도 증가)

### 함정 #3: BulkInsert 배치 크기 고정 & 체크포인트 부재
- **위치:** `MdbMigrationService.cs:31` `private const int BatchSize = 2000;`
- **문제:**
  - 614K collections = 307회 라운드트립
  - 재시작 시 멱등성 0건 보장
  - 옵션 B §3에 명시된 `migration_checkpoints` 현재 코드 0건 구현
  - **V4(중단→재시작) 검증 불통과 확정**

---

## 3. 옵션 B 정공법 권고 (CTO 시야)

### 권고 #1: 트랜잭션 경계를 테이블 단위로 축소
```
현재:  BEGIN TX ── [16테이블] ── COMMIT (100만+, 15분 롤백)
개선:  BEGIN TX ─ [테이블1] ─ COMMIT
       BEGIN TX ─ [테이블2] ─ COMMIT
       ...
```
- 구현: line 164-202를 16개 메서드 각각으로 이동
- 효과: 부분 실패 범위 최소화, undo log 적용 < 10초, 재시작 시 완료 테이블 스킵

### 권고 #2: 체크포인트 + 멱등성 인프라 선반영
```csharp
INSERT INTO migration_checkpoints (
  tenant_id, table_name, last_source_hash, row_count, checkpoint_at
) VALUES (?, ?, ?, ?, ?)
```
- 위치: 각 테이블 마이그 후 `BulkInsertAsync` 직후
- 효과: 재시작 시 source_hash 기준 중복 차단

### 권고 #3: 세션 변수 + 명시적 청크 커밋
- 현 상태(line 154-160): SET SESSION 이미 구현 OK
- 추가: 청크 단위 명시적 COMMIT, 매 2000행 경계마다 checkpoint 쓰기
- 목표: 단일 청크 COMMIT < 7초

---

## 4. 서브에이전트(백엔드 개발자 3명) 분담 권고

| # | 담당 | 임무 |
|---|---|---|
| 1 | **TX 전담** | MigrateCoreAsync 리팩 → 테이블별 분리 tx. line 164-202 제거 후 각 Migrate* 메서드에 BEGIN/COMMIT 이동 |
| 2 | **체크포인트 전담** | migration_checkpoints 활용. RecordCheckpoint 메서드 신규. V4 검증용 테이블 스킵 로직 |
| 3 | **명시적 청크 커밋** | 청크 단위 커밋 추적. 매 2000행 경계마다 checkpoint 기록. 청크 실패 시 그 청크만 롤백 후 다음 재시도 |

### 병렬 검증
- **DB 매니저:** 청크 2000 vs innodb_buffer_pool 충돌, 인덱스 충돌 시뮬
- **보안 검토:** tenant_id JWT 클레임 재확인(line 53, 185), AES 5컬럼 평문 INSERT 0건
- **로그 분석:** V3(614K) 실행 후 테이블별 커밋 시간 < 7초 점검

---

## 결론

거대 단일 tx = 헌법 #20 오독의 결과. 정공법: 테이블별 독립 tx + 체크포인트 + 명시적 청크 경계 → V1~V7 전 통과 가능. 백엔드 3 + DB 3 병렬, 5/14 주간 18:00 V7까지 완료 → 사장님 확인 → 써밋.
