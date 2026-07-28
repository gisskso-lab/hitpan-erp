# 작업지시서 20260425작5 — sales_deliveries.tax_invoice_id 역참조 추가

## 0. 메타

| 항목 | 값 |
|---|---|
| **문서번호** | 20260425작5 |
| **발행일** | 2026-04-25 |
| **발행자** | PM 닥터스트레인지 |
| **A 책임자** | ERP 매니저 + 브라운킴(파이프라인) |
| **결재 트랙** | **풀** |
| **민감 영역** | DB 스키마 (기존 테이블 컬럼 추가) |
| **Contract-First 대상** | ❌ (기존 DTO만 갱신, 신규 API 없음) |
| **EVF 영향 영역** | ④ 혼돈 (TaxInvoice 발행 시 동시성) |
| **예상 소요** | 30분 |
| **Sprint** | Sprint 1 — 검증팀 발견 후속 |
| **트리거** | 검증팀 회의 (2026-04-25 BK #1) |

## 1. 배경 (Why)

### 1.1 검증팀 발견 사항

2026-04-25 검증팀 회의에서 브라운킴(BK) 발견:

> *"**계산서가 발행되어도 거래명세서(`sales_deliveries`)는 모릅니다.** 즉 거래명세서 조회 화면에서 '이미 계산서 발행됐는지' 알 수 없습니다. 작2 §2.2에 'delivery.tax_invoice_id 갱신 (역참조)' 적혀있는데 코드에 안 들어갔습니다."*

### 1.2 사용자 영향 (현 상태로는)

- 거래명세서 목록에서 발행 여부 표시 불가
- "발행" 버튼이 두 번 눌려도 사용자는 "왜 안 됐지?" → 불필요 클릭 반복
- ERP 매니저 무지 테스트 EVF ⑤ 통과 불가능 — "신입 직원이 발행 여부 못 봄"

### 1.3 P0-2 프론트 단계 진입 전 필수

작2의 프론트 단계에서 거래명세서 화면에 "계산서 발행됨" 칩을 표시해야 한다. 본 작5 없이는 프론트 진입 불가.

## 2. 목표 산출물 (What)

### 2.1 DB — DB-20 마이그레이션

```sql
-- 컬럼 추가
ALTER TABLE sales_deliveries
  ADD COLUMN tax_invoice_id VARCHAR(36) NULL
    COMMENT '발행된 세금계산서 ID (tax_invoices.invoice_id 역참조). 미발행 시 NULL.';

-- 인덱스 (조회 성능)
ALTER TABLE sales_deliveries
  ADD INDEX idx_sales_deliveries_tax_invoice (tax_invoice_id);
```

> ⚠️ FK는 안 걸음. tax_invoices가 ON DELETE RESTRICT를 sales_deliveries 측에 걸어두었기에, 양방향 FK는 순환 참조 위험. 역참조 인덱스만 운영.

### 2.2 백엔드 수정

`TaxInvoiceService.IssueAsync`:
- 단일 INSERT → **UoW 트랜잭션** 으로 변경
- INSERT tax_invoices + UPDATE sales_deliveries.tax_invoice_id 한 트랜잭션
- 둘 중 하나 실패 시 전체 롤백

`TaxInvoiceService.CancelAsync`:
- UPDATE tax_invoices.status='canceled' + **UPDATE sales_deliveries.tax_invoice_id = NULL** 한 트랜잭션
- 같은 거래명세서를 다시 발행 가능하게 (역참조 해제)

### 2.3 비범위 (별도 작업)

- 프론트 거래명세서 화면 칩 표시 (P0-2 프론트 단계)
- 취소 시 역분개·summary 차감 (별도 라운드)

## 3. RACI

| 역할 | 담당자 |
|---|---|
| **R** (실행) | 백엔드 개발팀(서비스 수정) + DB 개발팀(DDL) |
| **A** (책임) | **ERP 매니저 + 브라운킴 공동** |
| **C** (협의) | 백엔드 매니저 / 데이비드 박(정합성) |
| **V** (검증) | DV-D(역참조 정합성·트랜잭션) / BK(파이프라인) |
| **F** (결재) | CTO → 사장님 |

## 4. 결재 라인

**풀 트랙** 7단계.

## 5. EVF 검증 계획

| 영역 | 시나리오 | 책임자 | 통과 기준 |
|---|---|---|---|
| ④ 혼돈 | 발행 100회 동시 클릭 후 sales_deliveries.tax_invoice_id 1건만 박힘 | DV-D | UoW 트랜잭션 + UNIQUE로 보장 |

## 6. 완료 기준

- [ ] DB-20 마이그레이션 SQL 완성
- [ ] TaxInvoiceService 두 메서드 UoW 적용
- [ ] 빌드 0 errors
- [ ] DV-D 검증 사인
- [ ] CTO + 사장님 승인
- [ ] 써밋

## 7. 일정

| 단계 | 작업 |
|---|---|
| D+0 (오늘) | 작지서 발행 → DB-20 + 서비스 수정 → 빌드 → 써밋 (15번째) |
