# 결재 #2 — etax_send_history 신설 DDL 설계서

> **결재일:** 2026-05-12
> **작성:** DB매니저 + ERP매니저 + 본부장
> **헌법:** #1 (신규), #2 (tenant_id), #5 (raw_response AES), #17 (InnoDB), #18 (송신 0)
> **트리거:** DOCF4 4컬럼(TX_SENDDT, TX_READDT, TX_REPORTDT, TX_PDT) 보존 + 신규 발행 이력 통합

⚠️ **레거시 DOCF4 발행 이력 4컬럼 + 신규 발행(이세로/메이크빌) 이력 = 단일 테이블 통합 보존.**

---

## 1. 배경

### 1.1 레거시 DOCF4 발행 이력 4컬럼 (PowerShell 실측 2026-05-12)
```
TX_PDT       Text8    발행 일자
TX_SENDDT    Text8    국세청 전송 일자
TX_READDT    Text8    국세청 READ 일자
TX_REPORTDT  Text8    국세청 REPORT 일자
```

### 1.2 미래 신규 발행 (이세로/메이크빌 외주)
신 히트판은 전자세금계산서 발행 API 외주 검토 중 (`project_etax_outsource.md`).
- 외주 ASP의 발행 결과(승인번호·승인일자·실패사유)도 동일 테이블 보존
- 외주 업체 변경 시에도 컬럼 호환 (asp_provider 구분)

### 1.3 통합 보존 이유
- 레거시 이력 + 신규 이력 한 곳 = 사장님 조회·매니저 추적 단순
- tax_invoices 본 테이블은 발행 1회만 유지 (정상 INSERT)
- 발행 시도·재시도·국세청 응답 = etax_send_history N행 (이력)

---

## 2. DDL — etax_send_history

```sql
CREATE TABLE IF NOT EXISTS etax_send_history (
    history_id           CHAR(36) PRIMARY KEY,
    tenant_id            CHAR(36) NOT NULL,                  -- 헌법 #2 JWT 클레임

    -- 대상 세금계산서
    tax_invoice_id       CHAR(36) NOT NULL,                  -- tax_invoices FK

    -- 발행 시점
    issue_date           DATE NULL,                          -- TX_PDT 매핑 (발행일)
    sent_at              DATETIME NULL,                      -- TX_SENDDT 매핑 (전송일시)

    -- 국세청 응답
    nts_read_date        DATE NULL,                          -- TX_READDT 매핑 (READ 일자)
    nts_report_date      DATE NULL,                          -- TX_REPORTDT 매핑 (REPORT 일자)
    nts_approval_no      VARCHAR(50) NULL,                   -- 국세청 승인번호 (신규 발행 시)
    nts_response_code    VARCHAR(20) NULL,                   -- 응답 코드
    nts_response_message VARCHAR(500) NULL,                  -- 응답 메시지

    -- ASP 제공자
    asp_provider         VARCHAR(20) NULL,                   -- 'legacy' / 'ezerro' / 'makebill'
    asp_transaction_id   VARCHAR(100) NULL,                  -- ASP 트랜잭션 ID

    -- 상태
    status               ENUM('legacy','pending','sent','approved','rejected','failed','canceled')
                         NOT NULL DEFAULT 'pending',

    -- 시도·재시도
    attempt_no           TINYINT UNSIGNED NOT NULL DEFAULT 1,  -- 1=최초, 2=재시도 등
    is_retry             TINYINT(1) NOT NULL DEFAULT 0,

    -- 원본 응답 (헌법 #5 AES-256)
    raw_request          JSON NULL,                          -- ASP 요청 본문 (마스킹)
    raw_response_encrypted VARBINARY(4096) NULL,             -- ⚠️ AES-256 원본 응답 (선택)

    -- 감사
    created_by           CHAR(36) NULL,                      -- 누가 시도 (user_id)
    created_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

    INDEX idx_tenant_invoice (tenant_id, tax_invoice_id),
    INDEX idx_status (tenant_id, status, created_at DESC),
    INDEX idx_sent_date (tenant_id, sent_at DESC),
    INDEX idx_asp (asp_provider, asp_transaction_id),

    CONSTRAINT fk_etax_invoice FOREIGN KEY (tax_invoice_id)
        REFERENCES tax_invoices(invoice_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

---

## 3. 마이그 변환 — DOCF4 → etax_send_history

```csharp
// DOCF4 1행 → tax_invoices 1행 + etax_send_history 1행 (legacy 이력)
var historyId = Guid.NewGuid().ToString();
var sendDt = GetStr(row, "TX_SENDDT");
var readDt = GetStr(row, "TX_READDT");
var reportDt = GetStr(row, "TX_REPORTDT");
var pdt = GetStr(row, "TX_PDT");

await connection.ExecuteAsync(@"
    INSERT INTO etax_send_history (
        history_id, tenant_id, tax_invoice_id,
        issue_date, sent_at,
        nts_read_date, nts_report_date,
        asp_provider, status,
        attempt_no, created_at
    ) VALUES (
        @HistoryId, @TenantId, @InvoiceId,
        @IssueDate, @SentAt,
        @ReadDate, @ReportDate,
        'legacy',
        CASE
          WHEN @ReportDate IS NOT NULL THEN 'approved'
          WHEN @ReadDate IS NOT NULL THEN 'sent'
          WHEN @SentAt IS NOT NULL THEN 'sent'
          ELSE 'legacy'
        END,
        1, NOW()
    )",
    new {
        HistoryId = historyId,
        TenantId = tenantId,
        InvoiceId = invoiceId,
        IssueDate = ParseDateOrNull(pdt),
        SentAt = ParseDateTimeOrNull(sendDt),
        ReadDate = ParseDateOrNull(readDt),
        ReportDate = ParseDateOrNull(reportDt)
    },
    transaction: tx);
```

---

## 4. 헌법 부합 매트릭스

| 헌법 | 적용 |
|---|---|
| #1 신규 테이블 (수정 OK) | ✅ |
| #2 tenant_id JWT만 | ✅ 모든 INSERT |
| #5 암호화 | ✅ raw_response_encrypted VARBINARY |
| #15 빈 catch 금지 | ✅ ErrorCollector |
| #17 InnoDB + utf8mb4_unicode_ci | ✅ |
| #18 본사 송신 0 | ✅ 로컬 보관 |
| #19 errors 0 + warnings 0 | ✅ FK 명시 |
| #20 워크플로우 끊김 0 | ✅ status 전수 명시 |
| #22 데이터 최소주의 | ✅ raw_response 별도 권한 |
| #23 5중 검증 | ⚠️ 작업지시서 발행 시 |

---

## 5. EVF 6대 영역 점검

| 영역 | 시나리오 | 대응 |
|---|---|---|
| ① 부하 | 일 1만 건 발행 + N회 재시도 | 인덱스 idx_status |
| ② 장애 | ASP 다운 → 재시도 | attempt_no + is_retry 추적 |
| ③ 악의 | 다른 tenant 이력 침투 | tenant_id 검증 |
| ④ 혼돈 | 같은 invoice 발행 100회 시도 | attempt_no 증가 (N행 INSERT) |
| ⑤ 무지 | 사장님이 재시도 사유 모름 | nts_response_message 화면 표시 |
| ⑥ 노후 | 5년 후 발행 이력 조회 | 인덱스 + 보관 정책 |

---

## 6. 보관 정책

```
[보관 기간]
  etax_send_history    영구 (세무 감사 + 국세청 5년 보존 의무)

[보관 사유]
  - 국세청 전자세금계산서 5년 보존 의무 (전자거래기본법)
  - 사장님 추적 가능성 영구 확보
```

---

## 7. 사장님 결재 사항

| # | 사항 | 결재 |
|---|---|---|
| 1 | etax_send_history 테이블 신설 | ✅ 사장님 결재 2026-05-12 |
| 2 | raw_response_encrypted VARBINARY AES-256 | ✅ 헌법 #5 |
| 3 | FK tax_invoices ON DELETE CASCADE | ✅ |
| 4 | 보관 영구 (5년 의무 + 감사) | ✅ |
| 5 | 외주 ASP 변경 시 asp_provider 구분만 | ✅ |

---

## 8. 적용 시점

- **DDL 적용:** W2 D2 작업지시서 발행 후
- **마이그 변환 코드 구현:** W2 D3
- **외주 ASP 연동:** 7/1~14 (베타 7/15 전 완료 목표)

---

**작성:** DB매니저 + ERP매니저 + 본부장 춘식
**검토:** 보안매니저 (raw_response AES), 백엔드매니저 (FK), 설계팀장 브라운킴
**최종 검증:** CTO 래리 앨리슨
**결재:** 사장님 2026-05-12
