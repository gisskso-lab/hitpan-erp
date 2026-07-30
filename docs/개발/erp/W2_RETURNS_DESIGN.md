# W2 D1 — 반품 마이그 설계서 (DOCFB 복합 PK 5컬럼)

> **작성:** 2026-05-12 / W1 D5 야간 선행 (어벤져스 합의안 대안 A')
> **담당:** 본부장 춘식 + DB매니저 + ERP매니저
> **헌법:** #1 (수정 OK), #3 (INSERT ONLY 원장), #16 (순차), #18 (송신 0), #20 (워크플로우 끊김 X)
> **상태:** 설계 — 분기점 마커 포함 (사장님 buy_DOSCODE 확정 후 재검토 필요)

⚠️ **반품 = stock_ledger INSERT ONLY 원장 (헌법 #3 절대 원칙).**

---

## 1. 개요

### 1.1 레거시 DOCFB 구조

| 항목 | 값 |
|---|---|
| 테이블명 | DOCFB |
| PK | **5컬럼 복합** (IJ_DT, IJ_IO, IJ_SEQ, IJ_BUY, IJ_SUN) |
| 컬럼 수 | 18 |
| 현재 행 | 0 (빈 테스트 셋업) |
| 마이그 영역 | A등급 (즉시 작동) |
| 신 매핑 | `stock_ledger` (재고원장) |

### 1.2 PK 컬럼 의미 (ERP매니저 더존 30년 도메인)

| PK 컬럼 | 의미 |
|---|---|
| `IJ_DT` (Text8) | 거래일자 (YYYYMMDD) |
| `IJ_IO` (Text1) | 입출 구분 (I=입고, O=출고) |
| `IJ_SEQ` (Int16) | 순번 |
| `IJ_BUY` (Int32) | 거래처 코드 |
| `IJ_SUN` (Int16) | 행 순번 (한 거래의 품목별 행) |

→ **재고원장 1행 = (날짜·입출·전표번호·거래처·품목행) 5축 식별.**

### 1.3 신 stock_ledger 매핑

```sql
INSERT INTO stock_ledger (
    ledger_id,            -- CHAR(36) 신규 UUID
    tenant_id,            -- JWT 클레임
    item_id,              -- FK items
    direction,            -- 'in'/'out' (IJ_IO 변환)
    transaction_date,     -- IJ_DT 변환
    quantity,             -- 수량
    unit_price,           -- 단가 (분기점 ⚠️)
    amount,               -- 금액 (decimal)
    partner_id,           -- FK partners (IJ_BUY)
    source_type,          -- 'purchase'/'sales'/'return_in'/'return_out'/'adjust'
    source_id,            -- 원전표 UUID
    legacy_pk_json,       -- 5컬럼 PK 보존 (JSON) — 멱등성·재마이그 추적
    created_at
) VALUES (...);
```

---

## 2. 반품 워크플로우 — 헌법 #20 준수

### 2.1 매입 반품 (return_in 역방향)
```
[매입] → 재고 ↑  → [매입 반품] → 재고 ↓ → stock_ledger 2행 INSERT
                                              (매입 1행 + 반품 1행 모두 INSERT ONLY)
```

### 2.2 판매 반품 (return_out 역방향)
```
[판매] → 재고 ↓  → [판매 반품] → 재고 ↑ → stock_ledger 2행 INSERT
                                              (판매 1행 + 반품 1행 모두 INSERT ONLY)
```

### 2.3 헌법 #3 절대 준수
- stock_ledger는 **UPDATE/DELETE 금지**
- 반품도 신규 INSERT (음수 수량 또는 별도 행)
- "재고 복원"은 **새 행 INSERT**로만 표현
- 헌법 #20 워크플로우 끊김 0 — 매입↔반품↔판매↔반품 전부 원장 추적

---

## 3. 18개 컬럼 매핑

### 3.1 PK 5컬럼 (위 §1.2 참조)

### 3.2 데이터 13컬럼 (추정 + W2 D1 사장님 데이터 확인)

| 레거시 | 타입 | 신 매핑 | 비고 |
|---|---|---|---|
| `IJ_PUM` | Int32 | `item_id` (FK) | 품목 코드 |
| `IJ_KU` | Text2 | items.size/spec | 규격 |
| `IJ_SU` | Currency | `quantity` (decimal) | 수량 |
| `IJ_DAN` | Currency | `unit_price` (decimal) | 단가 (옵션 H — IJ_DAN 그대로 보존) |
| `IJ_KUM` | Currency | `amount` (decimal) | 금액 |
| `IJ_VAT` | Currency | `vat_amount` (decimal) | 부가세 |
| `IJ_GU` | Text1 | `source_type` | 거래 구분 ⚠️ 변환 룰 필요 |
| `IJ_GUSU` | TinyInt | `return_flag` | 반품 여부 ⚠️ 핵심 |
| `IJ_CHANG` | Text2 | `warehouse_code` | 창고 |
| `IJ_REM` | Text100 | `memo` | 비고 |
| `IJ_PAYDT` | Text8 | `payment_date` | 결제일 |
| `IJ_PAYGU` | Text1 | `payment_type` | 결제 구분 |
| `IJ_OK` | Text1 | `confirmed` (bool) | 확정 여부 ⚠️ 헌법 #6 |

---

## 4. ✅ 단가 처리 — 옵션 H (하이브리드) 확정 (2026-05-12 사장님 결재)

### 4.0 확정 사유

**PYOJUN.MDB 실측 결과 (2026-05-12 PowerShell):**
- DOCF8 거래처 = 3건 (시스템 기본: 현금매입/현금판매/불량폐기)
- buy_DOSCODE 분포 = 전체 공백
- 실 거래처 데이터 0건 → 옵션 B vs D 양자택일 불가

→ **옵션 H (하이브리드)** 확정: 데이터 존재 여부에 따라 동적 분기 + 기본값 fallback.

### 4.1 옵션 H 결정 트리

```
DOCF8 마이그 시 buy_DOSCODE 읽기
  ├─ 값 있음 + 옳은 형식 (A~E or 1~5)
  │   → partners.price_grade = 매핑된 CHAR (옵션 B 경로)
  │     예: '1'→'A', '2'→'B', ...; 또는 'A'→'A' 그대로
  ├─ 값 없음 + 거래 이력 있음 (DOCFB 행 존재)
  │   → 거래 단가 분석 → 자동 등급 추론 (옵션 D 경로)
  └─ 값 없음 + 거래 이력 없음 (신규 셋업)
      → partners.price_grade = 'A' (기본값, ERP매니저 제안)
```

⚠️ **사장님 결재 2026-05-12 A안:** partners.price_grade는 기존 CHAR(1) DEFAULT 'A' 그대로 사용.
원본 buy_DOSCODE 값은 신규 컬럼 `price_grade_code VARCHAR(10)`에 보존.

### 4.2 stock_ledger.unit_price 처리

**모든 경우 공통:**
- `stock_ledger.unit_price = DOCFB.IJ_DAN` 그대로 보존
- 이유: 거래 시점 단가 = 이력 (헌법 #3 INSERT ONLY 원장 정신)
- 단가등급 변경되어도 과거 거래 단가는 그대로

**즉, 반품 마이그는 unit_price 변환 로직 분기 불필요.** 단가등급은 거래처 마스터(partners) 마이그 시점에만 영향.

### 4.3 옵션 H 구현 코드 (partners 마이그 측)

```csharp
// DOCF8 → partners 마이그 시
var doscode = GetStr(row, "buy_DOSCODE")?.Trim();
string priceGrade;  // CHAR(1) — A~E (A안 결재 2026-05-12)

if (!string.IsNullOrEmpty(doscode) && IsValidGradeFormat(doscode))
{
    // 경로 B: 컬럼 값 직접 매핑 (1~5 → A~E, 또는 A~E 그대로)
    priceGrade = MapDoscodeToGrade(doscode);  // "1"→"A", "A"→"A", etc.
}
else
{
    // 경로 D 또는 기본값: 거래 이력 분석은 W3 post-process
    // 마이그 1차 = 기본값 'A'
    priceGrade = "A";
}

partners.PriceGrade = priceGrade;
partners.PriceGradeCode = doscode;  // 원본 보존
```

### 4.4 반품(DOCFB) 측 영향 = 0

본 설계서(반품 마이그)는 옵션 H 영향 받지 않음. stock_ledger.unit_price = IJ_DAN 그대로 보존만 하면 됨.

---

## 5. JSON 직렬화 — 5컬럼 PK 보존

### 5.1 legacy_pk_json 컬럼 (이미 ALTER 계획)
```sql
ALTER TABLE stock_ledger
    ADD COLUMN legacy_pk_json JSON NULL COMMENT '레거시 5컬럼 PK 보존';
CREATE INDEX idx_stock_legacy ON stock_ledger ((CAST(legacy_pk_json->>'$.IJ_DT' AS CHAR(8))));
```

### 5.2 변환 코드 (헌법 #1 — 기존 코드 추출 패턴 준수)
```csharp
var legacyPk = new Dictionary<string, object>
{
    ["IJ_DT"]   = GetStr(row, "IJ_DT"),
    ["IJ_IO"]   = GetStr(row, "IJ_IO"),
    ["IJ_SEQ"]  = GetInt(row, "IJ_SEQ"),
    ["IJ_BUY"]  = GetInt(row, "IJ_BUY"),
    ["IJ_SUN"]  = GetInt(row, "IJ_SUN")
};

await _checkpointService.UpdateLastPkAsync(jobId, "DOCFB", legacyPk, ct);

await connection.ExecuteAsync(
    @"INSERT INTO stock_ledger (
        ledger_id, tenant_id, item_id, direction, transaction_date,
        quantity, unit_price, amount, partner_id, source_type,
        legacy_pk_json, created_at
      ) VALUES (
        @LedgerId, @TenantId, @ItemId, @Direction, @TransactionDate,
        @Quantity, @UnitPrice, @Amount, @PartnerId, @SourceType,
        @LegacyPkJson, NOW()
      )",
    new {
        LedgerId = Guid.NewGuid().ToString(),
        TenantId = tenantId,
        ItemId = await _itemLookup.GetIdByLegacyAsync(GetInt(row, "IJ_PUM"), tenantId, ct),
        Direction = GetStr(row, "IJ_IO") == "I" ? "in" : "out",
        TransactionDate = DateOnly.ParseExact(GetStr(row, "IJ_DT"), "yyyyMMdd"),
        Quantity = GetDec(row, "IJ_SU"),
        UnitPrice = GetDec(row, "IJ_DAN"),     // 옵션 H: IJ_DAN 그대로 (이력 보존)
        Amount = GetDec(row, "IJ_KUM"),
        PartnerId = await _partnerLookup.GetIdByLegacyAsync(GetInt(row, "IJ_BUY"), tenantId, ct),
        SourceType = MapSourceType(GetStr(row, "IJ_GU"), GetByte(row, "IJ_GUSU")),
        LegacyPkJson = JsonSerializer.Serialize(legacyPk)
    },
    transaction: tx);
```

---

## 6. 멱등성 — 재마이그 시 중복 방지

### 6.1 헌법 #20 멱등성 보장
같은 DOCFB 행을 2번 마이그해도 stock_ledger에 1행만 존재해야 함.

### 6.2 멱등 키
```sql
CREATE UNIQUE INDEX uk_stock_legacy_pk ON stock_ledger (
    tenant_id,
    (CAST(legacy_pk_json->>'$.IJ_DT' AS CHAR(8))),
    (CAST(legacy_pk_json->>'$.IJ_IO' AS CHAR(1))),
    (CAST(legacy_pk_json->>'$.IJ_SEQ' AS UNSIGNED)),
    (CAST(legacy_pk_json->>'$.IJ_BUY' AS UNSIGNED)),
    (CAST(legacy_pk_json->>'$.IJ_SUN' AS UNSIGNED))
);
```

→ 재마이그 시 `INSERT IGNORE` 또는 `ON DUPLICATE KEY UPDATE updated_at=NOW()` 사용 (헌법 #3은 stock_ledger 데이터 UPDATE 금지 — 메타만 OK).

### 6.3 재시작 시
- `migration_checkpoints.last_pk_value` JSON에 마지막 5컬럼 PK 보관
- ORDER BY IJ_DT, IJ_IO, IJ_SEQ, IJ_BUY, IJ_SUN ASC
- WHERE 절: `(IJ_DT, IJ_IO, IJ_SEQ, IJ_BUY, IJ_SUN) > @last_pk_5values`

---

## 7. source_type 변환 룰 (IJ_GU + IJ_GUSU)

⚠️ **W2 D1 사장님 데이터로 분포 확인 필요.**

```
추정 룰 (ERP매니저 더존 30년 경험):
  IJ_GU='1', IJ_GUSU=0  → 'purchase'      (매입)
  IJ_GU='1', IJ_GUSU=1  → 'return_in'     (매입 반품)
  IJ_GU='2', IJ_GUSU=0  → 'sales'         (판매)
  IJ_GU='2', IJ_GUSU=1  → 'return_out'    (판매 반품)
  IJ_GU='3', IJ_GUSU=0  → 'adjust_in'     (조정 입고)
  IJ_GU='3', IJ_GUSU=1  → 'adjust_out'    (조정 출고)
```

**확정 필요 사항:**
- IJ_GU 값 분포 (1/2/3 외 다른 값 있는지)
- IJ_GUSU 값 분포 (0/1 외 다른 값 있는지)

---

## 8. 헌법 #6 — confirmed 시점

### 8.1 IJ_OK = '1' (확정)만 stock_ledger INSERT
draft 상태 데이터는 stock_ledger에 안 들어감. 헌법 #6 절대 준수.

```csharp
if (GetStr(row, "IJ_OK") != "1")
{
    await _errorCollector.AddWarningAsync(jobId, "DOCFB", legacyPk,
        "draft 상태 — stock_ledger 스킵", ct);
    continue;  // INSERT 안 함
}
```

### 8.2 draft 데이터 별도 보관
draft 데이터는 `purchase_orders` 또는 `sales_orders`로 마이그 (확정 전 상태).

---

## 9. ERP매니저 매뉴얼 정합성 체크

### 9.1 매뉴얼 시나리오 매칭 (HITPAN_USER_MANUAL.md)
- **매입 → 매입반품 → 재고 회복:** stock_ledger 2행 INSERT (매입 1, 반품 1)
- **판매 → 판매반품 → 재고 증가:** stock_ledger 2행 INSERT (판매 1, 반품 1)
- **사용자 시선:** "반품해도 매입·판매 이력은 그대로 남는다"

### 9.2 매뉴얼 보강 필요
- 반품 시 재고 변동 표시 화면 캡처 (W4 양식 작업 시)
- 반품 사유 입력란 위치 (현재 IJ_REM → memo 매핑)

---

## 10. 헌법 부합 매트릭스

| 헌법 | 적용 |
|---|---|
| #1 수정 OK 덮어쓰기 X | ✅ 기존 stock_ledger 패턴 추출 |
| #2 tenant_id JWT만 | ✅ 모든 INSERT에 tenantId 클레임 |
| #3 INSERT ONLY 원장 | ✅ stock_ledger UPDATE/DELETE 0 |
| #4 decimal 금액 | ✅ Currency → decimal |
| #5 암호화 | ➖ (반품 데이터 형사영역 없음) |
| #6 confirmed 시점만 원장 | ✅ IJ_OK='1' 필터 |
| #15 빈 catch 금지 | ✅ ErrorCollector 의무 |
| #16 순차 처리 | ✅ await foreach |
| #17 InnoDB | ✅ stock_ledger 기존 |
| #18 본사 송신 0 | ✅ 로컬 |
| #20 워크플로우 끊김 0 | ✅ 매입↔반품 양방향 추적 |
| #22 데이터 최소주의 | ✅ 본사 미송신 |
| #23 5중 검증 | ✅ 작지서 + 매니저 + SAST + DAST + 최소주의 |

---

## 11. EVF 6대 영역 점검

| 영역 | 시나리오 | 대응 |
|---|---|---|
| ① 부하 | 3년치 100만 행 stock_ledger | 청크 1,000 + 인덱스 |
| ② 장애 | 네트워크 끊김 중 INSERT | 멱등 UK + 재시작 |
| ③ 악의 | 다른 tenant의 DOCFB 침투 | tenant_id 검증 |
| ④ 혼돈 | 재마이그 100회 | 멱등 UK 보장 |
| ⑤ 무지 | 사장님이 draft 마이그 시도 | IJ_OK='1' 필터 + 경고 |
| ⑥ 노후 | 5년 후 재고원장 조회 | 인덱스 + 월별 파티션 (Phase 2) |

---

## 12. 사장님 결재 사항

| # | 사항 | 결재 |
|---|---|---|
| 1 | DOCFB 5컬럼 PK → JSON 직렬화 | ⚠️ |
| 2 | source_type 변환 룰 (IJ_GU + IJ_GUSU) | ⚠️ 사장님 데이터 확인 필요 |
| 3 | IJ_OK='1' 필터 (헌법 #6) | ✅ 헌법 명시 |
| 4 | 멱등 UK (legacy_pk_json) | ⚠️ |
| 5 | unit_price = IJ_DAN 그대로 (옵션 H 확정 2026-05-12) | ✅ 결재 완료 |

---

## 13. 다음 작업

### 13.1 W2 D1 완료 (2026-05-12)
1. ✅ `buy_DOSCODE` 분포 확인 → 옵션 H (하이브리드) 확정
2. ⚠️ `IJ_GU + IJ_GUSU` 값 분포 = DOCFB 0건 → 베타 체험단 실 데이터 확인 시점에 재검토
3. ✅ 본 설계서 §4 분기점 마커 제거 + 옵션 H 반영 완료

### 13.2 W2 D2~D3
1. stock_ledger ALTER (legacy_pk_json + UK)
2. DOCFB Mapper 클래스 구현
3. 단위 테스트 (Idempotency 100회 재실행 0 중복)

---

**작성:** 본부장 춘식 + DB매니저 + ERP매니저
**검토:** 보안매니저 (형사영역 없음 확인), 백엔드매니저 (#16 순차), 설계팀장 (#1 추출 패턴)
**최종 검증:** CTO 래리 앨리슨
**적용 시점:** 사장님 buy_DOSCODE 확정 후 작업지시서 발행
