# 병렬이슈 13 — 이관 명세서 `total_amount` 에 **부가세가 두 번** 들어갔다

| 항목 | 값 |
|---|---|
| 적발 | **[3-V] 동시검증** · 2026-09-07 · 봉합 후 실측 숫자 대조(대사표 ①② 가 레거시 공급가와 **부가세만큼** 어긋남) |
| 작업 | [20260904작21](../../운영기록/20260904작21_MDB마이그v2_대사표_P0봉합_작업지시서.md) — 작지서 밖 · **5월 WS-F(진범 #9) 코드부터 있던 결함** |
| 등급 | 🔴 **P0** — 오더 1 *"레거시와 같은 값"* 위반. 이관 고객의 매출·미수·매입·미지급이 **부가세만큼 부풀어** 보인다 |
| 상태 | ✅ **봉합 완료** (같은 세션 · 2줄 + 게이트 W11) |

---

## 무엇이 잘못됐나

ERP 의 `sales_deliveries.total_amount` / `purchase_receipts.total_amount` 는 **공급가 합계**다.

| 자리 | 근거 |
|---|---|
| `SalesService.cs:68,276,318` | `TotalAmount = request.Items.Sum(x => x.SupplyAmount)` — 화면이 저장하는 값 |
| `FinanceService.cs:826-843` | 매출·미수·매입·미지급 KPI 전부 `SUM(total_amount + vat_amount)` — 합계는 **더해서** 만든다 |

이관은 `TotalAmount = supplyTotal + vatTotal` 을 넣었다(`MigrateDeliveriesAndReceiptsAsync` 매출·매입 2곳).
⇒ 이관 행만 `total_amount` 가 이미 합계라 KPI 가 **부가세를 한 번 더** 더한다.

```
레거시 DOCFE IO=2 공급가(AMT1)     11,074,294,207
히트판 sales_deliveries Σtotal_amount 12,103,528,418   ← 차이 1,029,234,211 ≈ Σvat_amount 1,029,201,152
```

## 🔴 왜 5월엔 못 잡았나 — **비교 대상이 자기 자신이었다**

5월 30차 *"99.7%"* 는 DOCFE `a3`(합계) 와 ERP `total_amount` 를 맞췄다 — **둘 다 합계**니까 맞았다.
ERP 화면이 `total_amount` 를 공급가로 읽는다는 사실은 아무도 안 봤다.
대사표(갈래 B)가 **레거시 공급가(AMT1) ↔ ERP `total_amount`** 로 짝을 지으니 부가세만큼 어긋나 드러났다.

## 봉합

| 자리 | 변경 |
|---|---|
| `MdbMigrationService.MigrateDeliveriesAndReceiptsAsync` 매출·매입 헤더 | `TotalAmount = supplyTotal` (2곳) · `VatAmount = vatTotal` 그대로 |
| 게이트 `W11` | `TotalAmount = supplyTotal,` 2개 + `supplyTotal + vatTotal` 부재 — 되돌리면 빨간불 |
| 해시 | `ComputeSourceHash($"sd:{sourceId}:{supplyTotal}:{vatTotal}")` 는 원래 공급가·부가세로 만들어 **멱등키 불변** — 재이관 시 같은 PK 로 제자리 정정 |

## 남는 것

- `sales_orders`·`purchase_orders` 이관(`:1594`·`:1653` `TotalAmount = amt`)도 같은 의미 검사를 안 했다 — 대사표 9항목 밖. **작22 후보**
- `purchase_receipt_items`·`sales_delivery_items` 라인의 `supply_amount`/`vat_amount` 는 원래 분리돼 있었다(라인은 정상)

## 교훈

| | |
|---|---|
| 🔴 | **"맞다" 는 무엇과 맞췄느냐로 갈린다** — 코드 출력을 코드 출력과 맞추면 늘 맞는다. 정답 칸은 **화면이 읽는 어휘**로 세운다 |
| 🔴 | 같은 이름의 컬럼이 표마다 다른 뜻일 수 있다 — 넣기 전에 **그 표를 저장하는 화면 코드** 한 줄을 본다 |
