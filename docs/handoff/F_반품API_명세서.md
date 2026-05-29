# F — 반품 API 명세서 (코드 침범 0, W1 후 백엔드 매니저 발진용)

> 박제: 2026-05-29 19:30 | PM 브라운킴
> 헌법 #29 정합 (PM 코드 침범 0, 명세만 박제)
> 발진 시점: 6/3 W1 게이트 통과 후 + 백엔드 매니저 작5 작지서 발행
> 박제 갱신: 반품 API 부분 존재 박제 (전부 부재 아님)

---

## 1. 현재 상태 박제 (5/29 19:30 실측)

### 1.1. 존재하는 반품 API
| 메서드 | 경로 | 기능 |
|---|---|---|
| GET | `/api/purchase/returns/{id}` | 반품 상세 조회 |
| GET | `/api/purchase/returns` | 반품 목록 조회 |
| POST | `/api/purchase/receipts/{id}/convert-to-return` | 매입→반품 전환 |
| POST | `/api/purchase/returns/{id}/confirm` | 반품 확정 (재고 반영) |
| DELETE | `/api/purchase/returns/{id}` | 반품 취소 |

### 1.2. **부재한 반품 API (헌법 #20 끊김 잠재)** ⚠️
| 부재 | 영향 |
|---|---|
| **`POST /api/purchase/returns` (신규 직접 작성)** | 매입 없이 반품만 직접 발행 불가 |
| **`PUT /api/purchase/returns/{id}` (수정)** | draft 상태 반품 수정 불가 |

**현장 시나리오**:
1. 매입 거래처에서 별도 반품 (창고 청소, 재고 정리 시) → **POST 신규 필요**
2. draft 반품 수정 → **PUT 필요**

---

## 2. 신규 반품 작성 API 명세 (W1 후 봉합 대상)

### 2.1. Endpoint
```
POST /api/purchase/returns
Authorization: Bearer {jwt}
Content-Type: application/json
```

### 2.2. Request DTO
```csharp
public class CreatePurchaseReturnRequest
{
    public string PartnerId { get; set; }           // 거래처 ID (필수)
    public DateTime ReturnDate { get; set; }        // 반품일 (필수)
    public string? Memo { get; set; }               // 비고
    public List<ReturnLineItem> Items { get; set; } // 반품 항목 (필수, 1건 이상)
}

public class ReturnLineItem
{
    public string ItemId { get; set; }      // 상품 ID
    public decimal Quantity { get; set; }   // 반품 수량 (decimal, float 금지 §헌법 #4)
    public decimal UnitPrice { get; set; }  // 단가
    public decimal SubTotal { get; set; }   // 소계
    public string? WarehouseId { get; set; } // 창고 ID
}
```

### 2.3. Response
```json
{
  "returnId": "string",
  "returnNo": "RT-20260603-001",
  "status": "draft",
  "createdAt": "2026-06-03T10:00:00+09:00"
}
```

### 2.4. 처리 로직
1. JWT에서 tenant_id 추출 (§ 절대원칙 #2)
2. DTO 검증 (Items 1건 이상, Quantity > 0)
3. tx 시작 (§ 절대원칙 #6 INSERT ONLY, draft 상태 = 원장 미반영)
4. `purchase_returns` INSERT + `purchase_return_items` INSERT (N건)
5. tx commit
6. 응답 반환

### 2.5. 헌법 정합 박제
- ✅ #2 tenant_id JWT (파라미터 0)
- ✅ #4 decimal (float 0)
- ✅ #6 draft 상태 = 원장 미반영
- ✅ #14 raw string 0 (`$"..."` 사용)
- ✅ #15 빈 catch 0 (`_logger.LogWarning`)
- ✅ #17 ENGINE=InnoDB (기존 테이블 정합)
- ✅ #20 흐름 끊김 0 (Confirm → 재고↓ 별도 API 정합)

---

## 3. 신규 반품 수정 API 명세

### 3.1. Endpoint
```
PUT /api/purchase/returns/{id}
```

### 3.2. 처리 로직
1. JWT tenant_id 추출
2. `purchase_returns` SELECT `status` (draft만 허용)
3. confirmed 또는 deleted = 400 에러
4. DTO 검증
5. tx 시작
6. `purchase_returns` UPDATE + `purchase_return_items` DELETE + INSERT (N건)
7. tx commit
8. 응답 반환

### 3.3. 절대 원칙
- **confirmed 상태 수정 절대 금지** (§ 절대원칙 #6)
- **stock_ledger UPDATE/DELETE 금지** (§ 절대원칙 #3)

---

## 4. 단위 테스트 명세 (xUnit)

### 4.1. 신규 반품 작성 (POST)
1. ✅ 정상 요청 → 201 + returnId 박제
2. ⚠️ Items 0건 → 400
3. ⚠️ Quantity 0 → 400
4. ⚠️ Quantity 음수 → 400
5. ⚠️ tenant_id 불일치 → 401/403
6. ⚠️ PartnerId 부재 → 400

### 4.2. 수정 (PUT)
1. ✅ draft 수정 → 200
2. ⚠️ confirmed 수정 → 400
3. ⚠️ deleted 수정 → 404
4. ⚠️ tenant_id 불일치 → 401/403

---

## 5. W1 후 작5 작지서 (백엔드 매니저)

### 5.1. 발행 일자
6/4 (목) 09:00 — W1 게이트 통과 직후

### 5.2. 산출
- `PurchaseController.cs` — `CreatePurchaseReturn` + `UpdatePurchaseReturn` 2 메서드 추가 (코드 추가만, 기존 메서드 0 수정)
- `PurchaseService.cs` — `CreatePurchaseReturnAsync` + `UpdatePurchaseReturnAsync` 2 메서드
- xUnit 신규 10건 (POST 6 + PUT 4)
- 단위 테스트 100% PASS

### 5.3. 게이트
- xUnit 100% PASS
- W1 게이트 18/18 → 20/20 PASS (신규 시나리오 2건 추가)
- 헌법 정합 8개

### 5.4. 가도 기간
6/4~6/10 (W2 D1~D7)

---

## 6. PM 코드 침범 0건 박제

| 점검 | 결과 |
|---|---|
| 본 문서 작성 중 코드 수정 | 0건 ✅ |
| `PurchaseController.cs` 수정 | 0건 ✅ |
| `PurchaseService.cs` 수정 | 0건 ✅ |
| 헌법 #29 정합 | ✅ |

---

## 7. 추가 박제 — 반품 흐름 통합 점검

### 7.1. 현재 흐름 (5/29)
```
매입 (receipt) → convert-to-return → 반품 (return draft) → confirm → 재고↓
```

### 7.2. W1 후 흐름 (6/10 봉합)
```
매입 (receipt) → convert-to-return → 반품 (return draft) → confirm → 재고↓
                                          ↑
       POST /returns (신규 직접) ─────────┘
       PUT /returns/{id} (draft 수정) ────┘
```

**헌법 #20 끊김 0건 가도 정합.**

---

**문서 끝.** 백엔드 매니저 작5 작지서 발진 대기.
