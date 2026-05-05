# 테스트 수정 필요 메모 (2026-05-06)

> 테스트는 수정하지 않는다는 원칙에 따라, 아래 항목들은 **프로덕션 코드 수정 후** 테스트를 업데이트할 것.

---

## 즉시 수정 대상 (프로덕션 코드)

| 코드 위치 | 문제 | 우선순위 |
|---|---|---|
| `ConfirmReceiptRequest.cs` | 빈 클래스 — 확정자(userId), 비고(memo) 필드 없음 | P1 |
| `ConfirmDeliveryRequest.cs` | 빈 클래스 — 실제 필드 없음 | P1 |

## 통합 테스트로 검증해야 할 항목 (현재 Mock으로 대체)

| 항목 | 이유 | 테스트 파일 |
|---|---|---|
| 재고 음수 DB 레벨 차단 | `WHERE current_qty >= @Qty` 실제 DB에서만 검증 가능 | `StockIntegrityTests.cs` |
| MonthlySummaryGuard 멱등 보장 | `INSERT IGNORE` 실제 MariaDB에서만 검증 가능 | `MonthlySummaryGuardTests.cs` |
| tenant_id JWT 클레임 주입 격리 | WebApplicationFactory 필요 | `MultiTenantIsolationTests.cs` |
| BOM 자재 부족 시 DB 차단 | `UPDATE WHERE current_qty >= @Qty` 실제 DB 필요 | `BomWorkflowTests.cs` |
| 세금계산서 역분개 | 현재 비범위(작2 §3) — 구현 시 추가 | `TaxInvoiceWorkflowTests.cs` |

## 통합 테스트 인프라 수정 필요 (DbFixture)

| 항목 | 문제 | 수정 방법 |
|---|---|---|
| `DbFixture.InsertTestWarehouseAsync` | `warehouses.tenant_id → tenants` FK 제약으로 신규 UUID tenant_id 삽입 불가 | `InitializeAsync()`에서 테스트 tenant 행 먼저 INSERT, 또는 기존 테스트 계정(tenant-001) 재사용 |
| `DbFixture.InsertTestItemAsync` | 동일 FK 문제 (`items.tenant_id → tenants`) | 동일 해결 |
| `monthly_summary_sources.source_type` | `VARCHAR(32)` — `"purchase_receipt_confirmed"` 30자로 통과하나 확인 필요 | 실행 후 확인 |
| 테스트 데이터 격리 전략 | 신규 UUID tenant는 FK 때문에 사용 불가 | 기존 `tenant-001` 사용 + 테스트 후 생성 데이터만 삭제, 또는 FK_CHECKS=0 세션 설정 |

## 구현 완료 후 추가할 테스트

| 항목 | 조건 |
|---|---|
| 전자세금계산서 EtaxStatus 상태 전이 | 2/3계층 구현 시 |
| 월마감 기간 전표 차단 | `ApprovalTriggerHelper.EnsureNotClosedAsync` 통합 테스트 |
| 자동발주 원클릭 (autoReceive=true) | `SalesService.CreateAutoOrdersAsync` |

---

## 현재 테스트 현황

- **총 39개** | 단위 테스트(Mock 기반)
- 통과: 39 / 실패: 0
- 빌드: errors 0, warnings 0

### 테스트 분류

| 파일 | 테스트 수 | 커버 영역 |
|---|---|---|
| `PurchaseWorkflowTests.cs` | 5 | 매입 확정·반품·전환 |
| `SalesWorkflowTests.cs` | 6 | 판매 확정·취소·전환·자동발주 |
| `TaxInvoiceWorkflowTests.cs` | 6 | 세금계산서 발행·취소·금액 정합성 |
| `StockIntegrityTests.cs` | 4 | 재고 음수 감지·안전재고·조정 |
| `BomWorkflowTests.cs` | 6 | 조립·해체·자재부족·자동발주 |
| `MonthlySummaryGuardTests.cs` | 5 | 열거형 정합성·인자 검증·날짜 변환 |
| `MultiTenantIsolationTests.cs` | 4 | 테넌트 격리 구조 검증 |
