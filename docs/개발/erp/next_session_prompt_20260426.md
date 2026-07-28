# 4/26 세션 인수인계 — 새 창 프롬프트 (v1)

## 사용법
새 창 열고 아래 "[이 아래부터 새 창에 붙여넣기]" 라인 아래를 전체 복사 → 새 대화창에 붙여넣기.

---

## [이 아래부터 새 창에 붙여넣기]

너는 히트판 ERP의 PM 닥터스트레인지 + CTO Final Verifier(래리 앨리슨/데이비드 박/브라운킴) 역할이다. 사장님(소순근)이 새 창을 여신 것은 4/26 세션을 그대로 이어가기 위함이다. 절대 새로 시작하지 말고 누적된 맥락을 그대로 받아 그 위에 작업하라.

### 0. 프로젝트 절대 원칙 (요약)
- **CLAUDE.md 의 §절대원칙 1~20** 모두 준수. 특히 §3 INSERT ONLY 원장 / §13 DESCRIBE 먼저 / §14 Razor C# raw string 금지 / §19 errors 0 + warnings 0 / §20 워크플로우 끊김 금지.
- 사장님 어록 추가:
  - **"취소가 곧 삭제고, 삭제가 곧 취소"** — 화면에 실질적으로 나오는 것만 반영
  - **"자동발주 → 매입처리까지 원클릭 Y/N"** — 사용자가 한 번 OK하면 발주 + 매입전환 + 매입확정까지 자동
  - **"BOM 으로 등록한 반제품도 자재로 사용 가능 / 다단 (2~5단) 공정구조 완전 지원"**
  - **"조립 해체 시 가격·재고 원래대로 회귀"**

### 1. 현재 상태 (커밋 dd89274 기준)
- 브랜치: `develop`
- API: PID 28052, http://localhost:5257 listening
- Web: localhost:5234 (Blazor WASM)
- DB: hitpan_erp / hitpan / Hitpan2025!
- 테스트 계정: tenant@hitpan.kr / Admin1234!

### 2. 오늘 세션 누적 커밋 (위→최신)
| # | 커밋 | 핵심 |
|---|---|---|
| 1 | 67b36a7 | BOM 상품 분리 + 더블클릭 로드 + 합계0 확정 차단 |
| 2 | 1586347 | 상품마스터 BOM 바로보기 + 매입확정행 비활성화 |
| 3 | f1d5930 | 판매 확정 시 안전재고 자동발주 다이얼로그 |
| 4 | 8a4d9e2 | BOM 매핑 기준 필터링 (회귀 시도) |
| 5 | cb2a9b5 | BOM 중복 등록 회귀 근본 해결 |
| 6 | 51ba27e | 상품마스터에 BOM 완제품 표시 + 응답 파싱 |
| 7 | eb4ded8 | BOM 조립 자재 자동발주 |
| 8 | 1b58bc3 | 상품 특별단가 — 할인율 컬럼 |
| 9 | 9239ea2 | BOM 알림 발주 500 + 판매 재고부족 자동발주 |
| 10 | 17c057b | 자재 단가 변경 시 BOM 매입단가 자동 반영 (체인 전파) |
| 11 | 6933cdf | 발주/수주 삭제 500 dynamic 캐스팅 + 거래명세서 권한 삭제 |
| 12 | 2a709b6 | 사장님 표현 다이얼로그 "삭제=취소" |
| 13 | cd3c1bd | SalesListDialog AuthorizeView 캐스케이드 핫픽스 |
| 14 | a696369 | CRUD 일관성 Phase 1 — 삭제·통계·목록 cancelled 필터 |
| 15 | a442557 | 다단 BOM 자재 후보 표시 + 순환 검사 안전망 |
| 16 | 7e56156 | 조립 해체 (DisassembleAsync) — Reverse |
| 17 | **dd89274** | **자동발주 → 매입처리 원클릭 (Y/N)** |

### 3. 사장님이 합격 판정한 영역
- ✅ 상품마스터 (조회·BOM 보기 버튼)
- ✅ BOM 다단 등록·조립
- ✅ 발주서 자동발주 — BOM·거래 양쪽

### 4. 미점검 / 미완 영역
| 영역 | 상태 |
|---|---|
| 특별단가 할인율 (1b58bc3) | 미점검 |
| 매입 확정행 비활성화 (1586347) | 미점검 |
| 합계 0원 확정 차단 (67b36a7) | 미점검 |
| 매입 confirmed 직접 취소 (`CancelConfirmedReceiptAsync`) | **미구현 P1** |
| monthly_summary 역행 차감 (DeleteDelivery draft) | **미구현 P1** |
| 견적 cancelled 처리 (status enum 에 cancelled 없음) | 정책 결정 필요 |
| 페이징 UI (LIMIT 200 하드코딩 해제) | EVF 부하 게이트 위반 — P1 |
| journal_lines CHECK 제약 (4/24 미해결 P0) | dd89274 까지 합계 0 차단으로 *증상* 해결, 근본 검사 필요 |

### 5. 사장님 직전 보고 (해결 완료 dd89274)
1. ✅ BOM 다단 합격
2. ✅ BOM 조립 후 자동발주 다이얼로그가 안 뜨던 이슈 → 3-way 선택지 ("자동발주+매입처리" / "발주서만" / "취소")로 명확화
3. ✅ "자동발주 후 매입처리까지 Y/N 원클릭" → autoReceive 옵션 신설, ConvertOrderToReceipt + ConfirmReceipt 자동 호출
4. ✅ 자동발주 발주서 매입전환 400 에러 → cancelled 매입의 `received_qty` 가 PO 에 잔존했던 데이터 오염. 일회성 SQL 로 정합화 + 향후 재발 방지 위해 [P1: CancelConfirmedReceiptAsync] 가 필요

### 6. 다음 세션 첫 행동 (우선순위)

**[P0] 사장님 dd89274 시연 검증**
- BOM 조립 → 다이얼로그 3-way 뜨는지
- "자동발주+매입처리" 선택 시 PO 생성 + 매입명세서 자동 생성 + 매입확정 + 자재 재고 +반영 한 번에 되는지
- "발주서만" 선택 시 PO 만 draft 로 생성되는지

**[P1-1] CancelConfirmedReceiptAsync 신규 구현**
- 경로: `PurchaseService` 에 `SalesService.CancelConfirmedDeliveryAsync` 패턴 모방
- 처리: stock_ledger Reverse OUT, item_stock 차감, monthly_summary -=, **PO.received_qty 차감 + item_status 재계산**, journal_lines Reverse 기표
- DeletePurchaseReceiptAsync 가 confirmed 일 때 자동 호출 (현재는 hard DELETE 만)
- DDL 결정: `purchase_receipts` 에 `is_deleted` 컬럼 추가? 아니면 status='cancelled' 만으로?

**[P1-2] monthly_summary 역행 차감**
- DeleteDelivery (draft) 시에도 `MonthlySummaryGuard.TryApplyAsync(amount: -X)` 호출
- 현재는 confirmed 거래 cancelled 시에만 차감되고 draft 삭제는 누적 유지

**[P1-3] 미점검 3영역 사장님 시연 + 보고**
- 특별단가 할인율
- 매입 확정행 비활성화
- 합계 0원 확정 차단

**[P2] 페이징 UI**
- 매입/판매/발주/수주 목록 LIMIT 200 → 무한스크롤 또는 페이지네이션
- EVF 부하 게이트 해제용

### 7. 핵심 파일 경로
- 백엔드 핵심:
  - `src/HitPan.Application/Services/PurchaseService.cs`
  - `src/HitPan.Application/Services/SalesService.cs`
  - `src/HitPan.Application/Services/BomService.cs`
  - `src/HitPan.Application/Services/AutoJournalHelper.cs`
  - `src/HitPan.API/Controllers/SalesController.cs` / `PurchaseController.cs` / `BomController.cs`
- 프론트 핵심:
  - `src/HitPan.Web/Pages/BomDetail.razor`
  - `src/HitPan.Web/Pages/Sales/DeliveryPage.razor`
  - `src/HitPan.Web/Components/Sales/SalesListDialog.razor`
  - `src/HitPan.Web/Pages/Items.razor`
  - `src/HitPan.Web/Pages/Items/ItemSpecialPricePage.razor`

### 8. 디버깅 시 유의사항
- **API 재기동 명령**:
  ```bash
  # 기존 죽이기
  Get-NetTCPConnection -LocalPort 5257 -State Listen | %{ Stop-Process -Id $_.OwningProcess -Force }
  # 띄우기 (백그라운드)
  dotnet run --project src/HitPan.API/HitPan.API.csproj --launch-profile http
  ```
- **MariaDB 직접 쿼리**:
  ```bash
  mysql -u hitpan -pHitpan2025! -D hitpan_erp -e "..." 2>/dev/null
  ```
- **빌드 정합성**:
  ```bash
  dotnet build src/HitPan.Application/HitPan.Application.csproj -nologo
  dotnet build src/HitPan.Web/HitPan.Web.csproj -nologo
  dotnet build src/HitPan.API/HitPan.API.csproj -nologo -t:Compile
  ```
  errors 0 + warnings 0 아니면 사장님 §19 헌법 위반 — 즉시 수정.

### 9. 주의: 회귀 다발 영역 (4/26 세션 학습)
- **dynamic 캐스팅 금지** — `(long)row.is_deleted` 같은 패턴은 InvalidCastException으로 500. 강타입 record 사용.
- **AuthorizeView 다이얼로그 안에서 금지** — Task<AuthenticationState> 캐스케이드 단절. 권한은 컨트롤러 [Authorize(Policy="...")] 한 곳만.
- **bom_items.auto_order_* 컬럼 없음** — items.auto_order_* 만 사용.
- **excludeBom 옵션 사용 주의** — 다단 BOM 시나리오 (반제품을 자재로) 깨짐. 자재 콤비박스에선 false 또는 미사용.
- **자동발주 dynamic 흐름** — autoReceive 분기 시 SalesService → IPurchaseService 가 IServiceProvider lazy 해결 (순환 회피).

### 10. 첫 메시지 권장 형태
사장님께 다음과 같이 첫 인사:

> 사장님, 4/26 세션 인수인계 받았습니다. dd89274 커밋까지 적용 완료, API 재기동 상태입니다.
>
> 직전 보고 4건 중 3건 (다단 BOM 합격, 자동발주 다이얼로그 회귀, 매입전환 400) 은 dd89274 에서 정리됐고, 마지막 1건 ("자동발주 → 매입처리 원클릭 Y/N") 은 3-way 다이얼로그 + autoReceive 옵션으로 구현했습니다.
>
> 시연 부탁드립니다. 그 외 미점검 P1 영역 (특별단가 할인율 / 매입 확정행 비활성화 / 합계 0 확정 차단) 도 차례로 보고 받겠습니다.

---

## 끝
