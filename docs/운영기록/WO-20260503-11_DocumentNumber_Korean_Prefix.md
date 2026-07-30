# 작20260503-11 — 전표번호 한글 prefix 통일 (P1, 사용자 친화)

## 🟡 우선순위
P1 (사장님 발견 — 도소매 자재상 현장 직원 가독성)

## 사장님 발견 (2026-05-03)
> "전표번호 자동생성 패턴 좀 쉽게 바꾸자.
>  문서유형 - 년월일자 - 그 일자에 생성된 순서.
>  발주서: 발-20260503-1, 매입처리: 매-20260503-1 이런 식으로"

CTO 권고안 사장님 결재 받음:
- 한글 1자 prefix
- 0패딩 유지 (정렬 안전)
- 하이픈 1개 (`발-20260503-001`)
- 기존 데이터 그대로 (신규만 적용)

## prefix 매핑표
| 문서 | 현재 prefix | 새 prefix |
|---|---|---|
| 발주서 (purchase_orders.po_no) | PO- | 발- |
| 매입처리 (purchase_receipts.receipt_no) | PR- | 매- |
| 매입반품 (purchase_returns.return_no) | RT- | 매반- |
| 견적서 (quotations.quotation_no) | QT- | 견- |
| 수주서 (sales_orders.order_no) | SO- | 수- |
| 거래명세서 (sales_deliveries.delivery_no) | SD- | 명- |
| 세금계산서 (tax_invoices.invoice_no) | TI- | 세- |
| 판매반품 (sales_returns) | (확인) | 판반- |

## 처방 (코드 변경 위치)
1. `PurchaseService.cs:35` — `$"PO-{date:yyyyMMdd}-"` → `$"발-{date:yyyyMMdd}-"`
2. `PurchaseService.cs:96` — receipt prefix → 매-
3. `PurchaseService.cs` — 매입반품 prefix → 매반-
4. `SalesService.cs:44` — 수주 prefix → 수-
5. `SalesService.cs:130` — 거래명세서 prefix → 명-
6. `QuotationService.cs:293` — 수주 prefix → 수-
7. `QuotationService.cs:390` — 견적 prefix → 견-
8. `BomService.cs:809` — BOM 자동 발주 prefix → 발-
9. `SalesService.cs:1325` — 자동 발주 prefix → 발-
10. 세금계산서 컨트롤러 — 세-

**주의 — 변경 안 함 (별도):**
- `MdbMigrationService.cs:701, 747` — `MIG-SO-`, `MIG-PO-` 마이그레이션 prefix 유지
  (기존 데이터 마이그레이션 표시용 — 신규 생성과 구분)

## 헌법 영향
- §절대원칙 #18 미저촉
- §#20 (워크플로우 끊김) 무관 (번호 형식만 변경)
- 헌법 4조 검증 필요 (각 문서 신규 생성 후 새 prefix 확인)
- 사용처 grep 의무 (절대원칙 #12) — 모든 prefix 사용처 추적

## DocumentNumberHelper 패턴 보존
`NextNumberAsync(prefix)` 시그니처 그대로 — prefix만 한글로 바꿔서 호출.
0패딩(`-001`)은 Helper 내부 로직 그대로 유지 (정렬 안전).

## 검증 (헌법 4조)
1. DB 계층: 신규 거래명세서/발주/매입 1건씩 생성 → DB 컬럼에 한글 prefix 저장 확인
2. 백엔드: API 응답 payload에 한글 prefix 정상 (한글 인코딩 미스 없음)
3. 프론트: 화면 목록/상세에 한글 prefix 표시
4. 끊김 0: 견적→수주→거래명세서→세금계산서 워크플로우 시 각 단계 prefix 정확
5. 고객 시선: "발-20260503-001 = 발주서 5월 3일 첫 건" 직관 인지

## 담당
- 메인: CTO (백엔드 매니저 가벼운 변경)
- 검증: ERP 매니저 (현장 시선) + 사장님

## SLA
빠른 트랙
- 코드 변경: 30분
- 빌드 + 검증: 30분
- 합계: 1시간 이내

## 결재 항목 (이미 받음)
- ✅ prefix 매핑 (위 표)
- ✅ B안 (0패딩 유지)
- ✅ 하이픈 패턴 `발-20260503-001`
- ✅ 기존 데이터 그대로 두기 (옵션 가)

## 진행 순서 (CTO Phase)
1. **선결조건:** WO-20260503-10 (partner_balance 봉합) 사장님 Phase C 검증 통과 + 커밋 완료
2. WO-11-1: 사용처 전수 grep (절대원칙 #12)
3. WO-11-2: 코드 변경 (10곳)
4. WO-11-3: 빌드 errors 0 + warnings 0
5. WO-11-4: 사장님 검증 (각 문서 1건씩 생성)
6. WO-11-5: 커밋
