# 본부장 춘식 PM 전수조사 보고서 (2026-05-13)

> 작성: 본부장 춘식  
> 의뢰: 사장님  
> 기간: 2026-05-13 (오늘 전수)  
> 원칙: 코드 수정 0건. 읽기·진단만. PM 변호 금지.  
> 산출 위치: `docs/audit/20260513_chunsik_pm_audit.md`

---

## 0. 요약 (Executive)

오늘 PM이 친 사고는 **3개 층위**다.

1. **마이그 핵심 SQL 회귀** — `tax_invoices` INSERT는 운영 스키마와 **컬럼 7개 전부 불일치** 상태로 남아 있음. 코드는 `tax_invoice_id/invoice_date/invoice_type/partner_id/supply_amount/vat_amount/total_amount/remark/updated_at`를 가정하나, 운영 DDL은 `invoice_id/delivery_id(NOT NULL+FK)/invoice_no/issued_at/issued_by(NOT NULL)/amount_total/vat_total/status`. → 실행 시 100% 실패, 그러나 내부 try/catch가 LogWarning 후 다음 행 진행 → **데이터 손실은 silent**.
2. **헌법 #15 B안 try/catch SKIP** — `MdbMigrationService.MigrateAsync`의 전체 트랜잭션을 제거하고 12 단계 각각을 `try { ... } catch (Exception ex) { _logger.LogError(...); }`로 감쌈. 로그는 있으니 "빈 catch" 조항(#15)은 아니지만, **트랜잭션 무결성·헌법 #20 워크플로우 끊김 금지** 위반. 한 단계가 실패해도 다음 단계 INSERT가 계속 진행 → partners 일부 + items 일부 + 거래 일부의 모순 상태 가능.
3. **운영 파일 사고(미커밋 잔여물)** — `src/HitPan.API/wwwroot/wwwroot/...` 중첩 디렉토리 발생. Blazor Web publish를 API의 `wwwroot/`에 7회 반복하면서 `wwwroot/wwwroot/` 사고. git status 상 `?? src/HitPan.API/wwwroot/` untracked로 잡혀 있어 커밋 오염은 없으나 디스크 정리 필요.

큐 코드 자체는 깨끗이 제거됨(코드 잔여물 0). DI 등록 자리에 주석 한 줄만 남음.

---

## 1. 오늘 변경 파일 전수 (git 기준)

### 1.1 커밋된 변경 (2건)

| 커밋 | 시각 | 메시지 요약 |
|---|---|---|
| `c5e70ca` | 17:57 KST | MDB 비번 지원 핫픽스 + W3 결재 12건 + 작지서 5종 + 학습 4종 일괄 |
| `669aa28` | 18:02 KST | 마이그 메뉴 500 봉합 — 전체 예외 try/catch + 로그 + 사용자 친화 메시지 |

**`c5e70ca` 파일 영향** (30 files, +3,844 / −14):
- 코드 5종: `MigrationController.cs` (+27), `MdbMigrationService.cs` (+48), `SensitiveFieldMasking.cs` (+1), `SensitiveFieldMaskingTests.cs` (+23), `MdbMigration.razor` (+23)
- 문서 24종: 결재 12 + 작지서 5 + 학습/명세 4 + ledger 1 + e2e 도구 1

**`669aa28` 파일 영향** (1 file, +61 / −11):
- `MigrationController.cs`만 — Preview/Migrate 양쪽에 `DirectoryNotFoundException`, `UnauthorizedAccessException`, `Win32Exception`, 마지막 `Exception` catch 추가. 모든 catch는 `_logger.LogWarning/LogError` 동반 → 헌법 #15 위반 아님.

### 1.2 미커밋 변경 (staged/unstaged, 큐 제거 작업 진행 중)

| 파일 | 변경량 | 비고 |
|---|---|---|
| `src/HitPan.API/Controllers/MigrationController.cs` | +5 / −2 | `#pragma warning disable CA1416` 추가 + 생성자 줄바꿈 |
| `src/HitPan.API/Program.cs` | +1 | `// 메시지큐 제거 (2026-05-13 사장님 지시)` 주석 한 줄 |
| `src/HitPan.Application/Services/MdbMigrationService.cs` | **+85 / −81 (495라인 diff)** | **B안 try/catch SKIP + 트랜잭션 제거**, EnsureMigrationEmployee fallback 추가, stock_ledger source_id 회전 카운터 추가, purchase_orders/sales_orders 스키마 정정 |
| `src/HitPan.Web/Pages/Settings/MdbMigration.razor` | +3 / −4 | "잠시 기다려주세요" → "잠시만 기다려주세요" 문구 수정 + 빈 줄 |
| `src/HitPan.Web/Program.cs` | +3 / −1 | HttpClient `Timeout = TimeSpan.FromMinutes(10)` 설정 |
| `src/HitPan.Web/wwwroot/index.html` | +9 | Service Worker 강제 해제 + caches 비우기 인라인 스크립트 |
| `tools/mdb-migration-e2e.mjs` | +14 / −4 | Playwright 테스트 보강 (코드 아님) |

### 1.3 Untracked 비코드 잔여물(중대)

- `src/HitPan.API/wwwroot/` (Web publish 잘못 떨군 폴더, **하위에 `wwwroot/wwwroot/` 중첩** 다수 — 사장님 지적한 "publish 7회 반복" 흔적). git 추적 외이나 디스크상 다량 자산.
- `src/HitPan.Web/wwwroot/landing-*.html`, `preview-design*.html` (오늘 변경분 아님, 기존 untracked).
- `tools/tools/` 중첩 (untracked, 오늘 cd 실수 흔적 추정).

---

## 2. 헌법 위반 매트릭스

| # | 헌법 조항 | 위반 위치 | 증거 | 심각도 | 조치 권고 |
|---|---|---|---|---|---|
| A | **#1 덮어쓰기 금지 / 추가만** | `MdbMigrationService.cs:165-241` | 전체 트랜잭션 블록을 통째로 제거하고 12개 try/catch로 **재구조화**. 기존 구조 보존하지 않음. | 🔴 HIGH | 사장님 결재 후 W3 작15 청크 구조로 대체 |
| B | **#3 INSERT ONLY 원장** | `MdbMigrationService` 전체 호출부 | 트랜잭션 제거 → stock_ledger INSERT 도중 PANDATA 실패 시 부분 원장 잔존. INSERT ONLY 원칙은 지키지만 **부분 적재** 상태는 §#20 위반. | 🔴 HIGH | 청크 + 체크포인트(작15) 도입 전까지 마이그 실행 자체 금지 |
| C | **#15 빈 catch 금지 (silent swallow)** | 표면상 위반 아님 — 모든 catch에 `_logger.LogError(ex, ...)` 있음. 그러나 `tax_invoices` INSERT 내부 catch(`MdbMigrationService.cs:1450-1454`)는 `LogWarning`만 하고 다음 행 진행 → **데이터 손실은 로그로만 추적**. | 🟡 MEDIUM | 로그는 있으나 운영자 알람 없음 — `migration_jobs.errors_raw_data`(결재 #2) 도입 시까지 silent 손실 위험 |
| D | **#16 MySqlConnection 병렬 금지** | 위반 없음 — 모든 INSERT가 `await ... ConfigureAwait(false)` 직렬 처리. ✅ |  |  |  |
| E | **#19 warnings 0 = 정합성** | `MigrationController.cs` (미커밋) | `#pragma warning disable CA1416` 파일 상단 무조건 추가. 기존엔 `Program.cs`에 `#pragma warning disable CA1416 ... restore` 범위 한정으로 있었는데, 컨트롤러 전체 영구 disable. **경고 은닉**. | 🟡 MEDIUM | restore 닫기 추가 or `[SupportedOSPlatform("windows")]` 명시로 대체 |
| F | **#20 워크플로우 끊김 절대 금지** | `MdbMigrationService.cs:182-241` | 12 단계 INSERT가 독립 try/catch — **K2 거래(판매·매입) 성공 + stock_ledger 실패** 시 "확정했는데 재고 안 빠짐" 시나리오 그대로 발생. 헌법 #20 격언 본문에 명시된 P0 핫픽스 사유. | 🔴 HIGH | 청크 단위 트랜잭션·체크포인트로 재설계 (작15) |
| G | **#23 5중 검증 — 5중 SAST/DAST 미수행** | 미커밋 + 5/13 핫픽스 전반 | "B안" 적용은 사장님 결재 없이 PM 단독 판단. 5중 검증(작지서·매니저 리뷰·SAST·DAST·#22 검증) 중 ①②만 부분 수행. | 🔴 HIGH | 본 커밋 머지 차단, 검증팀 회수 |
| H | **#26 법령·도메인·실행 3축 (신규)** | `CONSTITUTION_VIOLATION_LEDGER.md` 자기 등재 | ledger에 PM 본인이 **4회 누적** 자기 보고 등재. "밤새 학습 선언 후 미이행"이 ③ 실행 축 위반으로 ledger에 기록됨 (행 #4). 이미 처분 발효 명시. | 🔴 HIGH | 처분(자기비판 보고서 5/15)을 그대로 이행 |

---

## 3. 미해결 회귀 목록 (코드 그대로, 사장님 결재 받고 처리)

### 3.1 tax_invoices INSERT — **운영 스키마와 7컬럼 전부 불일치** (P0)

**위치:** `src/HitPan.Application/Services/MdbMigrationService.cs:1411-1454`

**현 코드 (요약):**
```sql
INSERT INTO tax_invoices
  (tax_invoice_id, tenant_id, invoice_no, invoice_date, invoice_type,
   partner_id, supply_amount, vat_amount, total_amount,
   status, remark, created_at, updated_at)
VALUES
  (@Id, @TenantId, @No, @Date, @Type,
   @PartnerId, @Supply, @Vat, @Total,
   'confirmed', @Remark, @Now, @Now)
```

**운영 DDL (`installer/hitpan_db.sql:4694` + `DB-19_tax_invoices_layer1.sql`):**
```
invoice_id (NOT NULL, PK)
delivery_id varchar(36) NOT NULL  ← FK to sales_deliveries.delivery_id, ON DELETE RESTRICT
invoice_no varchar(32) NOT NULL
issued_at datetime(6) NOT NULL
issued_by varchar(36) NOT NULL    ← 사용자 ID
amount_total decimal(15,2) NOT NULL
vat_total decimal(15,2) NOT NULL
status varchar(16) default 'issued'
etax_status, etax_issued_at, idempotency_key, ...
UNIQUE KEY uk_tax_invoices_delivery (delivery_id)
```

**차이:**
| 코드 가정 | 운영 실제 | 충돌 |
|---|---|---|
| `tax_invoice_id` | `invoice_id` | 컬럼명 다름 → SQL 컴파일 에러 |
| `invoice_date` | `issued_at DATETIME(6) NOT NULL` | 컬럼명 + 타입 다름 |
| `invoice_type` ('sales'/'purchase') | (없음) | 운영 스키마에 invoice_type 없음 — DOCF4 매입/매출 분기 정보 손실 |
| `partner_id NOT NULL` | (없음) | 운영 스키마는 `delivery_id` 통해 `sales_deliveries`로 추적 |
| `supply_amount` | `amount_total` | 컬럼명 다름 |
| `vat_amount` | `vat_total` | 컬럼명 다름 |
| `total_amount` | (없음 — `amount_total+vat_total` 합산 없음) | 컬럼 미존재 |
| `remark` | (없음) | 컬럼 미존재 |
| `updated_at` | 자동 갱신 | 자동이라 무방 |
| (없음) | `delivery_id NOT NULL` | **FK NOT NULL 필수** — 마이그된 sales_deliveries가 없으면 INSERT 자체 불가 |
| (없음) | `issued_by NOT NULL` | NOT NULL 필수 — fallback 사용자 ID 없으면 INSERT 불가 |

**결과:** DOCF4 → tax_invoices 마이그는 **단 1건도 성공 못 함**. 코드 라인 1450의 내부 try/catch가 `LogWarning(ex, "[MDB마이그레이션] 세금계산서 {No} INSERT 실패 — 스키마 차이 가능성", no)`로 삼킴. 운영자는 "이관 완료" 메시지를 보지만 세금계산서 0건.

**권고:** 작15 청크 도입 시 같이 재설계. 임시로 본 메서드는 `return 0` + 명시적 "DOCF4 마이그는 보류 — sales_deliveries 마이그 우선" 안내 화면 출력.

### 3.2 stock_ledger source_id 길이 초과 위험 (P1)

**위치:** `MdbMigrationService.cs:1007`

```csharp
var sourceId = $"mig-{GetStr(row, "IJ_DT")}-{GetShort(row, "IJ_SEQ")}-{io}-{count + 1}";
```

운영 DDL: `source_id varchar(36) NOT NULL`.

조합: `"mig-"`(4) + `YYYYMMDD`(8) + `-`(1) + SEQ(최대 5자리) + `-`(1) + `I/O`(1) + `-`(1) + count(최대 7자리) = 최대 **28자**. 36자 이내. 형식상 OK. **그러나** `count`는 모든 행 누적 카운터로, 100만 건 마이그 시 7자리 도달. 8자리 진입 시 36자 초과 발생 위험 있음 — 한계점만 메모.

또한 코드 주석에 "`tenant_id + source_type='migration' + source_id 결합 UNIQUE 키`"라 명시했지만 운영 DDL에 그런 UNIQUE 인덱스 없음 (`installer/hitpan_db.sql:4630-4633` 확인 — `idx_tenant_item_date`, `idx_tenant_date`만). **주석이 사실과 불일치** → 멱등 재실행 시 중복 INSERT 가능.

추가: 운영 stock_ledger 엔진 = `MyISAM` (`installer/hitpan_db.sql:4633`). 헌법 #17 (`ENGINE=InnoDB` 명시) 위반 — 다만 이건 4/22 이전 잔존 부채로 오늘 사고 아님. 다만 트랜잭션 미지원 → §#3 §#20 영향이 더 큼.

### 3.3 정정 완료(O) vs 미정정(X) 매트릭스 — INSERT 14종 전수

| # | INSERT | 위치 | 운영 스키마 DDL | 5/13 정정 여부 | 비고 |
|---|---|---|---|---|---|
| 1 | warehouses | L260 | (잔존 검토 불필요 — 마스터 사전 ensure) | O | 트랜잭션만 null 전달로 변경 |
| 2 | employees (fallback) | L284 | `installer/hitpan_db.sql` employees | O | 신규 추가, 컬럼명 일치 |
| 3 | partners | L318 | DB-W2 ALTER + base | △ | 본 보고서 범위 외 (5/12 W2 D2 작9에서 19컬럼 ALTER 완료 가정). 본문 검증 불가. |
| 4 | items | L463 | DB-W2 작10 5컬럼 ALTER | △ | 동일 |
| 5 | bom_headers / bom_items | L565/L570 | hitpan_db_ddl_FINAL_v1.0 | O | 컬럼명 일치 |
| 6 | employees (DOCSW) | L654 | DB-W2 작11 28컬럼 ALTER | △ | 동일 (5/12 ALTER 가정) |
| 7 | sales_orders (K2) | L788 | hitpan_db_ddl_FINAL | O | order_id/order_no/order_date — 코드와 일치 |
| 8 | sales_order_items (K2) | L796 | hitpan_db_ddl_FINAL | O | order_item_id/ordered_qty/delivered_qty — 일치 |
| 9 | purchase_orders (K2) | L806 | hitpan_db_ddl_FINAL | O | po_id/po_no/po_date — 일치 |
| 10 | purchase_order_items (K2) | L814 | hitpan_db_ddl_FINAL | O | po_item_id/ordered_qty/received_qty/warehouse_id — 일치 |
| 11 | stock_ledger | L971 | installer L4612 | O (회기) | source_id 회기 카운터로 충돌 회피, 36자 한계는 P1 |
| 12 | collections | L1050 | DB-15 L105 | O | 컬럼 일치 |
| 13 | cashbook | L1116 | installer L2069 | O | 컬럼 일치 |
| 14 | expenses | L1183 | installer L2478 | O | 컬럼 일치 |
| 15 | purchase_orders (IU) | L1252 | hitpan_db_ddl_FINAL | O | 본 라운드 정정 완료 (메모/draft) |
| 16 | purchase_order_items (IU) | L1260 | hitpan_db_ddl_FINAL | O | 컬럼 일치 |
| 17 | sales_orders (IO) | L1332 | hitpan_db_ddl_FINAL | O | 본 라운드 정정 완료 |
| 18 | sales_order_items (IO) | L1340 | hitpan_db_ddl_FINAL | O | 컬럼 일치 |
| 19 | **tax_invoices** | **L1412** | **DB-19 + installer L4694** | **X (대규모 불일치)** | **§3.1 참조 — P0** |
| 20 | bills | L1475 | DB-25 L10 | O | 컬럼 완전 일치 |
| 21 | card_payments | L1561 | DB-25 L36 | O | 일치 |
| 22 | card_payment_lines | L1571 | DB-25 L59 | O | 일치 |
| 23 | bank_transactions | L1658 | DB-25 L75 | O | 일치 |

**결론:** 14 INSERT 중 정정 13 / **미정정 1 (tax_invoices)**. PM이 사장님께 보고한 "마이그 SQL 회귀 봉합"은 **부분 봉합**임. `MigrateTaxInvoicesAsync`는 catch로 가려서 실패가 운영자에게 안 보일 뿐, **헌법 #20 워크플로우(매출→세금계산서→경리) 끊김** 확정.

추가: `partners / items / employees (DOCSW)`는 5/12 W2 D2 작9~11 ALTER 적용을 전제로 두고 검증 안 함. 본 감사 범위 외이나, **로컬 운영 DB에 W2 D2 ALTER가 실제 적용됐는지 확인 필수**. 미적용 상태라면 partners/items/employees도 회귀 발생 가능.

---

## 4. 큐 코드 제거 잔여물 (있다면)

### 4.1 코드 잔여물 — **없음 (clean)**

전수 검색 결과(`Grep "MigrationOrchestrator|MigrationWorker|MigrationQueue|migration_jobs|MigrationJob"`):
- 비-wwwroot 영역: **0 hits**
- wwwroot publish 산출물 내부: 일부 hit이나 단순 문자열 매칭(난독화된 라이브러리), 큐 코드 아님

### 4.2 DI 등록 잔여물

`src/HitPan.API/Program.cs:118` —
```csharp
// 메시지큐 제거 (2026-05-13 사장님 지시) — bulk INSERT로 마이그 빠르게 종료, 큐 불필요.
```
주석 한 줄만. 등록 코드 없음. ✅

### 4.3 Razor 폴링 잔여물

`MdbMigration.razor` 미커밋 diff 확인: `StartMigrationAsync()`는 단일 `Http.PostAsJsonAsync("api/migration/legacy-mdb", payload)` 직접 호출. 폴링 loop·SignalR·EventSource 등 없음. ✅

### 4.4 작지서 잔여물

`docs/work-orders/20260513작13_migration_jobs_API_4종.md` 존재. 본 작지서는 큐가 아닌 **migration_jobs 테이블 + REST API 4종**(start/status/cancel/list)을 기술. 큐와 무관한 정공법 W3 설계. 큐 회귀 위험 없음.

### 4.5 미커밋 잔여물 (운영 사고)

- `src/HitPan.API/wwwroot/wwwroot/` 중첩 폴더 — Web Blazor publish 7회 반복 사고. **운영 API의 정적 파일 폴더 오염**. git untracked라 commit 오염은 0이나, 디스크 정리 권고.
- 미커밋 변경(7파일) — Service Worker 강제 해제 인라인 스크립트(`index.html`), HttpClient 10분 타임아웃(`Web/Program.cs`), 빌드 경고 disable 무범위(`MigrationController.cs`), Razor 문구 1자 수정 등 — **모두 검증 없이 코드에 들어간 상태**. 다음 push 전 반드시 커밋 단위 분리 + 5중 검증.

---

## 5. 보안·데이터 격리 영향

### 5.1 헌법 #18/#22 (본사 데이터 최소주의) — 영향 없음 ✅

오늘 변경 영역은 **고객사 ERP 내부 데이터(MDB → MariaDB)**만. 본사 송신 코드·외부 API 호출 추가 없음. `MigrationController` 모든 엔드포인트는 로컬 처리.

### 5.2 PII/민감정보 처리

- `SensitiveFieldMasking.MaskPhone` — 가운데 자리 `****`(4자리 고정) 일관 마스킹. 자릿수 추정 방지 측면에서 OK.
- `EnsureMigrationEmployeeAsync` (신규) — 모든 K2 거래에 `__MIG_DEFAULT__` 사원 fallback. 운영상 **모든 마이그 거래의 담당자가 동일 익명 사원으로 귀속**됨. 헌법 #5 암호화 컬럼(`base_salary` 등) 영향 없으나, 감사 추적 측면에서 "실제 누가 했는지" 정보 손실. ledger/접근감사 알람(작14·17) 도입 후 보정 필요.
- `MigrationController` 미커밋 diff: `#pragma warning disable CA1416` 파일 상단 무범위 disable — **`[SupportedOSPlatform("windows")]` 컨트랙트 명시 누락**. Linux 컨테이너 배포 시 런타임 PNSE(`PlatformNotSupportedException`) 위험. 사장님 5/6 헌법 #21(외부접속 장애 교훈)과 같은 결의 무결성 사고 재발 위험.

### 5.3 트랜잭션 무결성 손실 (재강조)

전체 트랜잭션 제거 + 12 단계 독립 try/catch → **부분 마이그**가 정상 시나리오가 됨.
- 사례 1: partners 1000건 성공 → items K2_PUM 매핑 키 충돌 → items 0건 → 거래 INSERT에서 itemId NULL 다수 skip → "이관 완료" 메시지에 거래 0건.
- 사례 2: stock_ledger 50,000건 INSERT 도중 source_id 36자 초과 → 한 행만 실패해야 하나 try/catch가 메서드 전체를 감싸므로 **나머지 행도 전부 누락**.

이건 §#20 정면 위반이며 **헌법 #18/#22(데이터 최소주의)와 무관한 자체 무결성** 문제.

### 5.4 Service Worker 강제 해제 (`index.html`)

```html
<script>
    if ('serviceWorker' in navigator) {
        navigator.serviceWorker.getRegistrations().then(rs => rs.forEach(r => r.unregister()));
    }
    if (window.caches) {
        caches.keys().then(keys => keys.forEach(k => caches.delete(k)));
    }
</script>
```

**모든 사용자**의 브라우저에서 매 페이지 로드 시 SW unregister + 전체 캐시 삭제. 단기 디버그용으론 OK이나, **베타 출시 후에도 남으면 PWA 캐시 전략 무력화**(매 방문마다 풀 다운로드 → 회선 약한 고객사 부담). 베타 전 반드시 제거 또는 환경 분기.

---

## 6. 결론 — 사장님 결재 필요 항목

### 6.1 즉시 처리 (P0, 24h 이내)

1. **미커밋 변경 7파일 — push 차단.** 5중 검증 완료 전까지 머지 금지. 특히 `MdbMigrationService.cs`의 B안 try/catch SKIP 구조는 헌법 #20 위반.
2. **`MigrateTaxInvoicesAsync` 실행 금지.** 임시로 `return 0` + 안내 메시지로 봉합. 운영 마이그 메뉴에서 "세금계산서 단계는 sales_deliveries 마이그 완료 후 별도 메뉴" 라벨 추가. 작15(청크 + 체크포인트) 또는 작2(3계층 세금계산서) 흐름에 맞춰 재설계.
3. **`src/HitPan.API/wwwroot/wwwroot/` 중첩 폴더 디스크 정리.** Web publish 대상은 별도 디렉토리(`hitpan-publish-web/` 등) 강제. publish 스크립트에서 대상 경로 가드 추가(작지서 발행 권고).
4. **`MigrationController.cs` `#pragma warning disable CA1416` 무범위 disable 철회.** `[SupportedOSPlatform("windows")]` 또는 `restore` 짝 추가.

### 6.2 다음 세션 (P1, 48~72h)

5. **W3 작15(청크 + AIMD + 체크포인트) 정공법 가동.** B안 try/catch SKIP을 청크 트랜잭션으로 교체. partners/items/employees ALTER 적용 여부 운영 DB에서 SELECT 확인.
6. **`stock_ledger` UNIQUE 키 결재.** 코드 주석은 `tenant_id+source_type+source_id` UNIQUE를 가정하나 실제 인덱스 없음 — DDL ALTER로 UNIQUE 추가 or 코드 주석 정정 중 택일.
7. **헌법 #15 강화 — `LogWarning` ≠ silent.** `tax_invoices` 같은 도메인 P0 실패는 `migration_jobs.errors_raw_data`(결재 #2)로 적재 + 운영자 알람. catch에서 다음 행 진행 자체가 위험.
8. **헌법 #26 처분 이행.** PM 자기비판 보고서 5/15 09:00 P0 보고서와 함께 제출 (ledger 행 #4).

### 6.3 보류 (W3 게이트 통과 후)

9. **stock_ledger 엔진 MyISAM → InnoDB 전환.** 헌법 #17 부채 청산. 데이터량 크면 별도 작지서.
10. **Service Worker 강제 해제 스크립트 환경 분기.** 운영 배포 빌드는 PWA 캐시 정상화.
11. **`HttpClient.Timeout = 10분`** — 마이그 전용 named client로 분리. 모든 API 호출에 10분은 EVF 부하 영역에서 데드락 위험.

---

## 7. 부록 — 인용 라인 번호

| 사항 | 파일:라인 |
|---|---|
| 트랜잭션 제거 + B안 SKIP 진입점 | `src/HitPan.Application/Services/MdbMigrationService.cs:165-241` |
| `EnsureMigrationEmployeeAsync` 신규 | `src/HitPan.Application/Services/MdbMigrationService.cs:269-294` |
| stock_ledger source_id 회기 카운터 | `src/HitPan.Application/Services/MdbMigrationService.cs:1004-1007` |
| tax_invoices 회귀 INSERT | `src/HitPan.Application/Services/MdbMigrationService.cs:1411-1454` |
| 운영 tax_invoices DDL | `installer/hitpan_db.sql:4694-4724`, `src/HitPan.API/Migrations/SQL/DB-19_tax_invoices_layer1.sql:27-53` |
| `#pragma warning disable CA1416` 무범위 | `src/HitPan.API/Controllers/MigrationController.cs:6` (미커밋) |
| HttpClient 10분 타임아웃 | `src/HitPan.Web/Program.cs:81-82` (미커밋) |
| SW 강제 해제 | `src/HitPan.Web/wwwroot/index.html:50-58` (미커밋) |
| 큐 제거 주석 | `src/HitPan.API/Program.cs:118` (미커밋) |
| 헌법 #26 ledger 자기 등재 | `docs/governance/CONSTITUTION_VIOLATION_LEDGER.md:16` |
| 작지서 5종 | `docs/work-orders/20260513작9~17_*.md` |
| 결재 12건 | `docs/decisions/20260513결재1~12_*.md` |

---

**서명:** 본부장 춘식  
**감사 종료:** 2026-05-13  
**다음 검증:** P0 4건 처리 후 W3 게이트 재평가
