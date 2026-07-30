# 작업지시서 20260425작6 — ERP 본문 5개 서비스 UoW 통일 (`_db.BeginTransaction()` 정리)

## 0. 메타

| 항목 | 값 |
|---|---|
| **문서번호** | 20260425작6 |
| **발행일** | 2026-04-25 |
| **발행자** | PM 닥터스트레인지 |
| **A 책임자** | 백엔드 매니저 |
| **결재 트랙** | **풀** |
| **민감 영역** | 원장 / 재고 / 트랜잭션 경계 |
| **Contract-First 대상** | ❌ (기존 서비스 리팩터) |
| **EVF 영향 영역** | ② 장애 / ⑥ 노후 |
| **예상 소요** | 백엔드 2명 × 1.5일 |
| **Sprint** | Sprint 1 후반 (4/28~4/30) |

## 1. 배경 (Why)

감사팀 4/25 전수조사(4병렬×3축) 결과, DB 매니저가 짚은 **듀얼 커넥션 구조**(`IDbConnection`과 `AppDbContext`가 별개 MySqlConnection)는 **P0 재테스트 3 증상의 주범은 아님**으로 판명(크로스체크 #1 반박). tx 내부 쓰기는 이미 `_unitOfWork.GetDbConnection() + tx.DbTransaction`으로 동일 커넥션 공유 성립.

그러나 ERP 본문 5개 서비스의 `_db.BeginTransaction()` 10곳은 **헌법 #20(워크플로우 3흐름 끊김 금지)의 잠재 리스크 실체**로 남음. `_db.BeginTransaction()` 후 같은 스코프에서 `_unitOfWork.SaveChangesAsync`를 부르면 진짜 **듀얼 tx** 상황이 성립하고, 특히 판매 confirm 경로(SalesService:616, 711)에서 터지면 재고 무결성 파괴. 베타 전 정리 필수.

## 2. 목표 산출물 (What)

ERP 본문 5개 서비스의 `_db.BeginTransaction()` → `_unitOfWork.BeginTransactionAsync()` + `_unitOfWork.GetDbConnection()` 패턴으로 통일:

- [SalesService.cs:616](src/HitPan.Application/Services/SalesService.cs#L616) — 판매 취소 경로
- [SalesService.cs:711](src/HitPan.Application/Services/SalesService.cs#L711) — 판매 반품 경로
- [FinanceService.cs:57](src/HitPan.Application/Services/FinanceService.cs#L57) — 회계 경로
- [CollectionService.cs:58, 124, 189, 253](src/HitPan.Application/Services/CollectionService.cs) — 수금·지급 4곳
- [BomService.cs:344](src/HitPan.Application/Services/BomService.cs#L344) — BOM 생산
- [ApprovalService.cs:416](src/HitPan.Application/Services/ApprovalService.cs#L416) — 결재 처리

각 변경 후 단위테스트(정상 커밋 / 중도 예외 롤백 / 중첩 호출 거부) 추가.

## 3. 비범위 (What Not)

- **Dapper-only 인프라 서비스는 건드리지 않음** (EF 엔티티 매핑 없어 과잉 비용):
  - `TenantCertificateService:95, 163`
  - `MdbMigrationService:127`
  - `DeliveryBatchService:31`
- `AddScoped<IDbConnection>` DI 등록 자체는 유지 (read-only SELECT 용도 존속).
  단, `IReadDbConnection` 별도 인터페이스 분리 검토는 작7과 통합 고려.

## 4. RACI

| 역할 | 담당자 |
|---|---|
| **R** | 백엔드 개발자 2명 |
| **A** | 백엔드 매니저 |
| **C** | DB 매니저 |
| **V** | 데이비드 박(DV-D) — 데이터 정합성 검증 / 브라운킴(BK) — 파이프라인 가동 검증 |
| **F** | CTO 래리 앨리슨 → 사장님 |

## 5. 수용 기준 (Done Criteria)

- [ ] `_db.BeginTransaction()` 호출이 ERP 본문 5 서비스에서 0건 (grep)
- [ ] 빌드 errors 0 + warnings 0 (헌법 #19)
- [ ] 판매·매입·결재·수금 각각 정상 커밋 + 예외 롤백 시나리오 통합테스트 통과
- [ ] DV-D: `stock_ledger` INSERT 원자성 확인 (예외 시 row 0)
- [ ] BK: monthly_summary_sources 가산 경로 3흐름 실증
