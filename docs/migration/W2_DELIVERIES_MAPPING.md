# W2 D1 — deliveries 변환 매핑 표 (POTHER.mdb DELIVERY)

> **작성:** 2026-05-12 / W1 D5 야간 선행 (어벤져스 합의안 대안 A')
> **담당:** ERP매니저 + DB매니저 + 본부장
> **헌법:** #1 (수정 OK), #18 (송신 0), #20 (워크플로우)
> **상태:** **베타 후 신설** (사장님 결정 — 본 문서는 미래 W4~W5 대비 사전 매핑)

⚠️ **DELIVERY는 베타 후 신설 메뉴.** 본 문서는 베타 후 즉시 작업 가능하도록 사전 매핑.

---

## 1. 레거시 DELIVERY 구조

| 항목 | 값 |
|---|---|
| MDB 파일 | POTHER.mdb |
| 테이블명 | DELIVERY |
| PK | **5컬럼 복합** |
| 컬럼 수 | 15 |
| 현재 행 | 0 (빈 테스트 셋업) |
| 마이그 영역 | D등급 (베타 후 신설) |
| 신 매핑 | `deliveries` (신규 테이블) |

### 1.1 5컬럼 PK 추정 (ERP매니저 도메인)

```
추정 PK (사장님 실 데이터로 확인 필요):
  DV_DT      Text8    배송 일자 (YYYYMMDD)
  DV_NO      Int32    배송 번호
  DV_SUN     Int16    행 순번
  DV_BUY     Int32    거래처 코드
  DV_PUM     Int32    품목 코드 (또는 DV_TYPE)
```

→ **(날짜·전표번호·행·거래처·품목) 5축 식별.**

---

## 2. 15개 컬럼 추정 매핑

### 2.1 PK 5컬럼 (위 §1.1)

### 2.2 데이터 10컬럼 (추정 — W4 사장님 데이터 확인 필요)

| 레거시 (추정) | 타입 | 신 매핑 | 비고 |
|---|---|---|---|
| `DV_DT` | Text8 | `delivery_date` (DATE) | 배송일 |
| `DV_NO` | Int32 | `delivery_no` (VARCHAR) | 배송 번호 |
| `DV_BUY` | Int32 | `partner_id` (CHAR36 FK) | 거래처 |
| `DV_PUM` | Int32 | `item_id` (CHAR36 FK) | 품목 |
| `DV_SU` | Currency | `quantity` (DECIMAL) | 수량 |
| `DV_ADDR` | Text80 | `delivery_address` | 배송 주소 |
| `DV_RECVR` | Text20 | `receiver_name` | 수령인 |
| `DV_RECTEL` | Text18 | `receiver_phone` | 수령인 전화 |
| `DV_STATUS` | Text1 | `status` ENUM | 배송 상태 |
| `DV_MEMO` | Text100 | `memo` | 비고 |
| `DV_DRIVER` | Text20 | `driver_name` | 기사명 |
| `DV_CARNO` | Text15 | `car_number` | 차량번호 |
| `DV_TRACKING` | Text30 | `tracking_no` | 운송장 번호 |
| `DV_FEE` | Currency | `delivery_fee` (DECIMAL) | 배송비 |

---

## 3. 신규 deliveries 테이블 DDL 제안

```sql
CREATE TABLE IF NOT EXISTS deliveries (
    delivery_id          CHAR(36) PRIMARY KEY,
    tenant_id            CHAR(36) NOT NULL,             -- 헌법 #2

    delivery_no          VARCHAR(30) NOT NULL,
    delivery_date        DATE NOT NULL,
    line_no              SMALLINT UNSIGNED NOT NULL,

    -- 거래처·품목
    partner_id           CHAR(36) NOT NULL,
    item_id              CHAR(36) NULL,                 -- NULL = 전체 배송 (행 분해 안 함)
    quantity             DECIMAL(15,3) NULL,

    -- 배송지
    delivery_address     VARCHAR(200) NULL,
    receiver_name        VARCHAR(30) NULL,
    receiver_phone       VARCHAR(30) NULL,              -- ⚠️ 개인정보 (마스킹)

    -- 배송 상태
    status               ENUM('pending','in_transit','delivered','failed','returned')
                         NOT NULL DEFAULT 'pending',
    delivered_at         DATETIME NULL,

    -- 기사·차량 (선택)
    driver_name          VARCHAR(30) NULL,
    car_number           VARCHAR(20) NULL,
    tracking_no          VARCHAR(50) NULL,

    -- 비용
    delivery_fee         DECIMAL(15,2) NULL DEFAULT 0,

    -- 연결
    source_type          ENUM('sales','purchase','transfer','other') NULL,
    source_id            CHAR(36) NULL,                 -- sales_orders.order_id 등

    -- 메타
    memo                 TEXT NULL,
    legacy_pk_json       JSON NULL,                     -- 5컬럼 PK 보존
    created_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

    INDEX idx_tenant_date (tenant_id, delivery_date DESC),
    INDEX idx_partner (tenant_id, partner_id),
    INDEX idx_status (tenant_id, status, delivery_date),
    INDEX idx_source (source_type, source_id),
    UNIQUE KEY uk_delivery_no_line (tenant_id, delivery_no, line_no),

    CONSTRAINT fk_delivery_partner FOREIGN KEY (partner_id)
        REFERENCES partners(partner_id),
    CONSTRAINT fk_delivery_item FOREIGN KEY (item_id)
        REFERENCES items(item_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

---

## 4. 워크플로우 연결 — 헌법 #20

### 4.1 판매→거래명세서→배송 흐름
```
[판매 견적] → [수주] → [거래명세서 발행] → [배송 등록] → [배송 완료] → [세금계산서]
                                            ↓
                                       deliveries 신규 INSERT
                                            (source_type='sales')
```

### 4.2 매입 입고 배송 (역방향)
```
[발주] → [매입 입고 배송] → [매입] → [재고↑]
              ↓
         deliveries 신규 INSERT
              (source_type='purchase')
```

### 4.3 헌법 #20 준수
- 배송 상태 변경은 별도 이벤트 INSERT (delivery_events 별도 테이블 검토)
- deliveries.status 자체는 UPDATE OK (원장 아님)
- 단, 변경 이력은 별도 audit 테이블

---

## 5. 개인정보 보호 — receiver_phone

### 5.1 수령인 전화번호 = 개인정보
- 화면 표시: `010-****-1234` 마스킹 기본
- [보기] 클릭 시 step-up 인증 → 평문 5분 노출
- 감사로그 INSERT

### 5.2 헌법 부합
- 헌법 #5: 마스킹은 화면 레벨, 저장은 평문 OK (전화번호는 §24의2 대상 아님)
- 단, 운영 정책상 AES-256 적용 검토 (보안매니저 W4 결정)

---

## 6. 마이그 처리 — 단순 5컬럼 PK 보존

```csharp
var legacyPk = new Dictionary<string, object>
{
    ["DV_DT"]   = GetStr(row, "DV_DT"),
    ["DV_NO"]   = GetInt(row, "DV_NO"),
    ["DV_SUN"]  = GetInt(row, "DV_SUN"),
    ["DV_BUY"]  = GetInt(row, "DV_BUY"),
    ["DV_PUM"]  = GetInt(row, "DV_PUM")
};

await connection.ExecuteAsync(@"
    INSERT INTO deliveries (
        delivery_id, tenant_id, delivery_no, delivery_date, line_no,
        partner_id, item_id, quantity,
        delivery_address, receiver_name, receiver_phone,
        status, driver_name, car_number, tracking_no, delivery_fee,
        legacy_pk_json, created_at
    ) VALUES (
        @DeliveryId, @TenantId, @DeliveryNo, @DeliveryDate, @LineNo,
        @PartnerId, @ItemId, @Quantity,
        @Address, @ReceiverName, @ReceiverPhone,
        @Status, @DriverName, @CarNumber, @TrackingNo, @Fee,
        @LegacyPkJson, NOW()
    )",
    new {
        DeliveryId = Guid.NewGuid().ToString(),
        TenantId = tenantId,
        DeliveryNo = GetInt(row, "DV_NO").ToString(),
        DeliveryDate = DateOnly.ParseExact(GetStr(row, "DV_DT"), "yyyyMMdd"),
        LineNo = GetInt(row, "DV_SUN"),
        PartnerId = await _partnerLookup.GetIdByLegacyAsync(GetInt(row, "DV_BUY"), tenantId, ct),
        ItemId = await _itemLookup.GetIdByLegacyAsync(GetInt(row, "DV_PUM"), tenantId, ct),
        Quantity = GetDec(row, "DV_SU"),
        Address = GetStr(row, "DV_ADDR"),
        ReceiverName = GetStr(row, "DV_RECVR"),
        ReceiverPhone = GetStr(row, "DV_RECTEL"),
        Status = MapStatus(GetStr(row, "DV_STATUS")),
        DriverName = GetStr(row, "DV_DRIVER"),
        CarNumber = GetStr(row, "DV_CARNO"),
        TrackingNo = GetStr(row, "DV_TRACKING"),
        Fee = GetDec(row, "DV_FEE"),
        LegacyPkJson = JsonSerializer.Serialize(legacyPk)
    },
    transaction: tx);
```

---

## 7. 멱등성

```sql
CREATE UNIQUE INDEX uk_delivery_legacy_pk ON deliveries (
    tenant_id,
    (CAST(legacy_pk_json->>'$.DV_DT' AS CHAR(8))),
    (CAST(legacy_pk_json->>'$.DV_NO' AS UNSIGNED)),
    (CAST(legacy_pk_json->>'$.DV_SUN' AS UNSIGNED)),
    (CAST(legacy_pk_json->>'$.DV_BUY' AS UNSIGNED)),
    (CAST(legacy_pk_json->>'$.DV_PUM' AS UNSIGNED))
);
```

---

## 8. status ENUM 변환 룰 (추정)

⚠️ **W4 사장님 데이터로 확인 필요.**

```
추정 룰:
  DV_STATUS='1' → 'pending'      (대기)
  DV_STATUS='2' → 'in_transit'   (배송중)
  DV_STATUS='3' → 'delivered'    (배송완료)
  DV_STATUS='4' → 'failed'       (실패)
  DV_STATUS='5' → 'returned'     (반송)
  DV_STATUS=NULL → 'pending'
```

---

## 9. 헌법 부합 매트릭스

| 헌법 | 적용 |
|---|---|
| #1 수정 OK 덮어쓰기 X | ✅ 신규 테이블 |
| #2 tenant_id JWT만 | ✅ 컬럼 명시 |
| #4 decimal 금액 | ✅ 배송비 |
| #5 암호화 | ⚠️ receiver_phone 마스킹 (저장은 평문, W4 결정) |
| #15 빈 catch 금지 | ✅ |
| #17 InnoDB | ✅ |
| #18 본사 송신 0 | ✅ |
| #20 워크플로우 끊김 0 | ✅ source_type 연결 |
| #22 데이터 최소주의 | ✅ |

---

## 10. EVF 6대 영역 점검

| 영역 | 시나리오 | 대응 |
|---|---|---|
| ① 부하 | 일 1만 건 배송 | 일자 인덱스 |
| ② 장애 | 배송 중 정전 | status='in_transit' 유지 + 재시도 |
| ③ 악의 | 다른 tenant 배송 침투 | tenant_id 검증 |
| ④ 혼돈 | 같은 운송장 번호 중복 | UK uk_delivery_no_line |
| ⑤ 무지 | 수령인 전화 평문 노출 | 마스킹 + step-up |
| ⑥ 노후 | 3년 전 배송 조회 | 일자 인덱스 + 파티션 (Phase 2) |

---

## 11. 사장님 결재 사항 (베타 후 적용)

| # | 사항 | 결재 |
|---|---|---|
| 1 | deliveries 신규 테이블 신설 | ⚠️ W4 결재 |
| 2 | receiver_phone 마스킹 + step-up | ⚠️ |
| 3 | status ENUM 5종 (pending/in_transit/delivered/failed/returned) | ⚠️ 사장님 데이터 확인 후 |
| 4 | source_type 연결 (sales/purchase/transfer/other) | ⚠️ |
| 5 | delivery_events 별도 테이블 (이력) | ⚠️ Phase 2 |

---

## 12. 다음 작업 (W4~W5)

### 12.1 W4 D1 (양식 30종 작업 시)
1. 사장님 실 데이터로 DELIVERY 15컬럼 분포 확인
2. status 코드 매핑 확정
3. 본 매핑 표 §2·§8 확정

### 12.2 W4 D3
1. deliveries DDL 작업지시서 발행
2. Mapper 클래스 구현
3. 매뉴얼 시나리오 추가 (배송 등록·완료)

---

**작성:** ERP매니저 (더존 30년) + DB매니저 + 본부장 춘식
**검토:** 보안매니저 (개인정보), 백엔드매니저, 설계팀장
**최종 검증:** CTO 래리 앨리슨
**적용 시점:** 베타 후 W4~W5 작업지시서 발행
