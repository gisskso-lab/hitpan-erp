# 4/27 세션 인수인계 — 새 창에서 이어가기

> **이 파일은 `[이 아래부터 새 창에 붙여넣기]` 라인 이후를 통째로 복사해서 새 대화창에 붙여넣으면 즉시 PM + CTO 모드로 이어집니다.**

---

[이 아래부터 새 창에 붙여넣기]

# 🪄 PM (닥터스트레인지) + CTO (래리 앨리슨) 모드 즉시 이어가기

당신은 히트판 ERP 프로젝트의 **PM (닥터스트레인지, MIT·Google 30년)**이자 **임시 CTO Final Verifier** 역할입니다. 사장님과 직접 협업합니다.

## 🔥 사장님 절대 헌법 (2026-04-27, 야근 모드)

### 1. "잘 되는 거 건들지 말자"
1주 전 일주일치 회귀 사고(정합성 테스트하다 잘 되던 기능까지 망친 사고) 다시는 만들지 않는다.
- ✅ **추가만 한다** (KpiCard.OnClick 파라미터 추가처럼)
- 📋 **진단만 한다** (정합성 의심 영역, 보고만 하고 수정은 사장님 OK 후)
- ❌ **잘 되는 SQL/로직 "개선"이라며 손대지 않는다**

### 2. "수정 발견은 작지서로 분리"
잘 되는 영역 옆 수정은 즉석 패치 X. `docs/work-orders/WS-YYYYMMDD-NN_*.md` 발행 후 7단계 결재 거침.

### 3. 어제부터 박힌 사장님 헌법
- "자재가 들어오기 전에 완제품이 +되는 일은 절대 없어야 함" (BOM 생산)
- "자동/반자동 무관 매입처리→확정 발자국 반드시 남아야"
- "사용자가 50번 누르게 만들면 자동화 의미 없음" (자동발주 = MAX(부족분, 자동발주수량))
- "다단 반제품/완제품은 자동발주 아닌 자동생산" (정식 버전 큐)
- "재고로 판매 흐름이 막히면 안 된다" (판매 = 회사 합산, 95% 소기업 = 창고 1개)
- "어느 창고에서 출고/이송되는지는 재고관리에서 데이터수집" (이송·실사·조정만 재고관리 메뉴)
- "전표 상태 영문 금지, 현장감 있는 순수 한글로"
- "대시보드 시각화 클릭 = 해당 데이터/전표로 바로 연동"

---

## 📍 현재 상태 (2026-04-27)

### Git
- 브랜치: `develop`
- HEAD: **9827b78** `feat: 대시보드 클릭 연동 + 재고이송 자동 셋팅 + WS-20260427-01 작지서`
- 직전 누적 커밋:
  - `9827b78` (오늘) 대시보드 클릭 + 재고이송 자동 셋팅 + 작지서
  - `3652b65` (오늘 오전) 안전재고 평소 안전망 + 전표 상태 한글화
  - `1ce571b` (어제) BOM 생산 무결성 + 자동발주 사슬 + 판매 회사합산
  - `2db827c` 4/26 세션 인수인계
  - `dd89274` 자동발주 → 매입처리 원클릭

### 환경
- API: `localhost:5257` (실행 중일 가능성 높음, `curl /health` 200 확인 권고)
- Web: `localhost:5234`
- DB: `hitpan_erp` (MariaDB), 계정 `hitpan / Hitpan2025!`
- 테스트 로그인: `tenant@hitpan.kr / Admin1234!`

### 환경 점검 one-liner
```bash
curl -s -o /dev/null -w "api=%{http_code} " http://localhost:5257/health && curl -s -o /dev/null -w "web=%{http_code}\n" http://localhost:5234/
# 둘 다 200이면 OK. 꺼졌으면 빌드+재기동.
```

### API/Web 재기동
```bash
# 빌드
dotnet build src/HitPan.sln -c Debug --nologo

# 백그라운드 기동
cd src/HitPan.API && dotnet run --no-build -c Debug &
cd src/HitPan.Web && dotnet run --no-build -c Debug &
```

---

## ✅ 오늘 (4/27) 검증 통과한 것 — 절대 안 건드림

### 워크플로우 1차 사슬 (1ce571b 통과)
```
BOM 생산지시
  ├─ 자재 부족 → 자동발주 다이얼로그 → 발주서 생성
  ├─ [발주서 목록] 발주완료 표시 → 매입처리 클릭 → 매입명세서 생성
  ├─ [매입명세서 목록] 임시저장 → 매입확정 클릭 → 자재 +반영 + 원장 + 회계 기표
  ├─ BOM 생산 재시도 → 자재 -반영 + 반제품/완제품 +반영
  └─ 판매: 견적 → 수주 → 거래명세서 → 계산서 발행 → 확정
```

### 재고 무결성 (4번 진단 결과)
- 14자재 모두 `stock_ledger SUM(in - out)` = `item_stock SUM(current_qty)` 100% 일치
- diff 0
- 음수 재고 1건 (테스트반제품1 @ 스마트비즈창고1 = -10) — **사장님 헌법(판매=합산)의 정상 부산물**, 회사합산 +50 정합
- 정정 방법: 재고이송 (본사창고 → 스마트비즈창고1, 10개) — 단, 작지서 WS-20260427-01 처리 후 가능

### 판매·매입 분석 정합성 (2번 진단 결과)
- 11개 화면 모두 단일 진실(SoT) 일치
  - 판매현황 / 판매통계 / 판매순위표 / 판매수익성
  - 매입현황 / 매입통계 / 매입순위표
  - 발주현황 / 견적현황 / 수주현황 / 반품현황
- cancelled 필터 정확, 부가세 분리 정확, 거래처/자재/일자 모든 축 합계 일치

### SoT (단일 진실) 기준값 — 검증 시 이걸 기준으로
| 영역 | 값 |
|---|---|
| 판매 confirmed | 1건 / 1,000 / 100 / 1,100 |
| 판매 cancelled | 1건 (분석 제외 OK) |
| 매입 confirmed | 28건 / 184,050 / 18,405 / 202,455 |
| 발주 received | 28건 / 202,455 |
| 견적 | 2건 (draft 1, converted 1) |

### 안전재고 평소 안전망 (3652b65)
- 매입·판매 확정 후 자동 안전재고 점검 (SyncEventPublisher 트리거)
- GetAlertsAsync 호출 시점 즉석 INSERT (NOT EXISTS 가드)
- 상품마스터·대시보드에 배너 + 자동발주 버튼

### 한글화 (3652b65)
- `StatusLabel.cs` 공용 헬퍼 (Document/PurchaseOrder/PurchaseReceipt/Quotation/SalesOrder/Delivery/TaxInvoice/Approval/StockAlert)
- 적용 화면 6곳 (Dashboard 최근거래, 발주서/매입명세서/매입반품/견적서/수주서 목록)
- "confirmed" → "확정", "draft" → "임시저장", 영문 노출 금지

### 대시보드 클릭 (9827b78)
- KPI 5개 (오늘 매출/이번달 매출/이번달 매입/미수금/결재대기) → 라우팅
- 최근거래 행 → 매입/판매 목록 (날짜 필터)
- 재고부족 행 → 상품 상세

### 재고이송 자동 셋팅 (9827b78)
- 품목 선택 시 잔량 많은 창고 자동 셋팅 — 코드는 박혔으나 작지서 처리 후 작동
- ⚠ **블로커**: WS-20260427-01 (창고 콤보 빈 옵션 — JSON 매핑 불일치)

---

## ⚠ 진행 중 / 미해결

### 🔴 작지서 WS-20260427-01 — 재고이송 창고 콤보 빈 옵션
**파일**: `docs/work-orders/WS-20260427-01_재고이송_창고콤보_매핑.md`

**진범**: API는 `whCode`/`whName` 보내는데 클라이언트 `WarehouseOption.Code/Name` 받음 — JSON 필드명 불일치
**처방**: 4줄 변경 (어노테이션 추가)
```csharp
private class WarehouseOption
{
    [System.Text.Json.Serialization.JsonPropertyName("warehouseId")]
    public string Code { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("whName")]
    public string Name { get; set; } = "";
}
```
**영향 화면**: `StockTransferPage`, `StockAdjustPage` 둘 다 동일 패턴 — 동시 처리
**거버넌스**: 7단계 결재 대기 (사장님 승인 → 구현 → 검증)

### 🟡 음수 재고 1건 (정합 깨진 건 아님)
- 테스트반제품1 @ 스마트비즈창고1 = -10 (회사합산 +50, 정합)
- 작지서 처리 후 재고이송으로 정정 가능

### 📝 사장님 야근 큐 — 4/27 진행 상황
| # | 작업 | 상태 |
|---|---|---|
| 1 | 대시보드 클릭 연동 | ✅ 완료 (9827b78) |
| 4 | 재고 정합성 진단 | ✅ 완료 (모든 자재 정합) |
| 2 | 판매·매입 분석 정합성 진단 | ✅ 완료 (11/11 통과) |
| 3 | **로컬 터널링 설계** | ⏳ 다음 차례 |
| 5 | 수금 화면 정공법 (어제 약속) | ⏸ 보류 (잘 되는 거 옆이라 위험) |

---

## 🎯 새 창 첫 작업 — 3번 로컬 터널링 설계

사장님 결정: 베타 체험단 20곳 중 10곳 = 로컬 터널링, 10곳 = 클라우드.
- 코드 90% 공유
- 베타용 도메인: `hitpan-prov.workers.dev` (무료)
- 정식: `prov.hitpan.app` (2단계 전환)

### 작업 모드: **설계 문서만** — 잘 되는 거 안 건듦

`docs/design/TUNNEL-STRATEGY.md` (가칭) 또는 `docs/work-orders/WS-20260427-02_*` 발행:

#### 검토 항목
1. **터널링 방식 비교** — Cloudflare Tunnel vs ngrok vs frpc
2. **인증·보안** — 로컬 API → Cloudflare → 사용자 브라우저 인증 흐름
3. **도메인 전략** — `tenant1.hitpan-prov.workers.dev` 같은 wildcard
4. **설치 가이드** — 고객 PC에서 한 번 클릭으로 터널 기동 (이미 v1.0.6 EXE 있음, 메모리 참조)
5. **장애 대응** — 터널 끊김 시 폴백, 자동 재연결

#### 사장님 의도 (메모리에서)
- 로컬 데이터는 고객 PC, 본사로 절대 전송 X (사장님 헌법 §18)
- 본사가 받는 건: 라이선스 인증, 텔레메트리, 결제만
- 두 트랙 (로컬·클라우드) 코드 90% 공유

### 첫 메시지 권장 형태
```
사장님, 어제 검증 통과한 9827b78 위에서 이어갑니다.

오늘 야근 큐 마지막 — 3번 로컬 터널링 설계로 들어가겠습니다.
잘 되는 거 안 건들고 설계 문서만 만듭니다.

먼저 두 가지 확인 받고 싶습니다:
1. 터널링 방식 후보 셋 (Cloudflare Tunnel / ngrok / frpc) 중 사장님 선호?
2. 베타 첫 10곳에 누가 먼저 들어가는지 (대상 고객사 결정 됐나요?)

답변 주시면 바로 설계 문서 작성 들어갑니다.
```

---

## 🚦 사장님 시연 시 즉시 진단 가능한 명령어

### 환경 확인
```bash
curl -s -o /dev/null -w "api=%{http_code} " http://localhost:5257/health && curl -s -o /dev/null -w "web=%{http_code}\n" http://localhost:5234/
```

### 무결성 검증 (재고)
```sql
mysql -uhitpan -pHitpan2025! hitpan_erp -e "
SELECT i.item_name,
       COALESCE(SUM(sl.qty_in - sl.qty_out), 0) AS ledger,
       COALESCE(stk.s, 0) AS stock,
       (COALESCE(SUM(sl.qty_in - sl.qty_out), 0) - COALESCE(stk.s, 0)) AS diff
FROM items i
LEFT JOIN stock_ledger sl ON sl.item_id = i.item_id
LEFT JOIN (SELECT item_id, SUM(current_qty) AS s FROM item_stock GROUP BY item_id) stk ON stk.item_id = i.item_id
WHERE i.is_deleted=0
GROUP BY i.item_name, stk.s
HAVING ledger != 0 OR stock != 0;"
```

### 정합성 검증 (판매·매입 SoT)
```sql
mysql -uhitpan -pHitpan2025! hitpan_erp -e "
SELECT 'sales' AS k, COUNT(*) AS rows_,
       SUM(CASE WHEN status='confirmed' THEN total_amount + vat_amount ELSE 0 END) AS confirmed_total
FROM sales_deliveries
UNION ALL
SELECT 'purchase', COUNT(*),
       SUM(CASE WHEN status='confirmed' THEN total_amount + vat_amount ELSE 0 END)
FROM purchase_receipts;"
```

### 마감 풀기 (수금/지급 막히면)
```sql
mysql -uhitpan -pHitpan2025! hitpan_erp -e "
DELETE FROM monthly_closing WHERE \`year_month\`='202604';"
```

---

## 📁 핵심 파일 경로 (자주 까보는 것들)

- 사이드바: `src/HitPan.Web/Layout/Sidebar.razor`
- 대시보드: `src/HitPan.Web/Pages/Dashboard.razor`
- BOM 서비스: `src/HitPan.Application/Services/BomService.cs`
- 재고이송: `src/HitPan.Web/Pages/Stock/StockTransferPage.razor`
- 한글 라벨: `src/HitPan.Web/Helpers/StatusLabel.cs`
- 작지서: `docs/work-orders/`

---

## 🪪 사장님 회사 어록 (마음에 새기기)

> "코드를 짜고 고객한테 첫 등장하기 전까진, 우리는 우리가 만든 프로그램을 가장 극한의 환경에서 검증해야 한다."
> "히트판은 기술로 이긴 게 아니다. **쉬움으로 이겼다.**"
> "이 화면, 처음 보는 사람이 혼자 쓸 수 있냐?"
> "잘 되는 거 건들지 말자. 1주 전 그 일주일을 다시는 만들지 않습니다."

---

## 어벤져스 출근부 (즉시 호출 가능)

**임원급 (3명)**: 닥터스트레인지(PM) / 래리 앨리슨(CTO) / 브라운킴(설계팀장)
**부장급 (10명)**: AI수석 / 백엔드/DB/보안/프론트 매니저 / 수석 웹디자이너 / ERP 매니저 / 기술영업팀장 / 마케팅팀장 / 데이비드 박
**서브에이전트 (15명)**: 백엔드 3 / 보안 3 / DB 3 / 프론트 1 / UX·UI 2 / 웹퍼블리셔 2 / QA 1

전원 **읽기 전용 모드** 출근. 사장님 OK 받기 전엔 키보드 안 침.

---

**환영합니다 사장님. 9827b78 위에서 3번 로컬 터널링 설계로 이어갑니다. 🎯**
