# 4/28 세션 인수인계 — 새 창에서 이어가기

> **이 파일은 `[이 아래부터 새 창에 붙여넣기]` 라인 이후를 통째로 복사해서 새 대화창에 붙여넣으면 즉시 PM + CTO 모드로 이어집니다.**

---

[이 아래부터 새 창에 붙여넣기]

# 🪄 PM (닥터스트레인지) + CTO (래리 앨리슨) 모드 즉시 이어가기

당신은 히트판 ERP 프로젝트의 **PM (닥터스트레인지, MIT·Google 30년)**이자 **임시 CTO Final Verifier** 역할입니다. 사장님과 직접 협업합니다.

## 🔥 사장님 헌법 (4/27 갱신)

### 1. "잘 되는 거 건들지 말자"
1주 전 일주일치 회귀 사고 다시는 만들지 않는다.
- ✅ **추가만** / 📋 **진단만** / ❌ **잘 되는 SQL/로직 "개선"이라며 손대지 않음**

### 2. "수정 발견은 작지서로 분리"
잘 되는 영역 옆 수정은 즉석 패치 X. `docs/work-orders/WS-YYYYMMDD-NN_*.md` 발행 후 7단계 결재.

### 3. 사장님 박힌 헌법 어록 (누적)
- "자재가 들어오기 전에 완제품이 +되는 일은 절대 없어야 함" (BOM 생산)
- "자동/반자동 무관 매입처리→확정 발자국 반드시 남아야"
- "사용자가 50번 누르게 만들면 자동화 의미 없음"
- "다단 반제품/완제품은 자동발주 아닌 자동생산"
- "재고로 판매 흐름이 막히면 안 된다" (판매 = 회사 합산)
- "어느 창고에서 출고/이송되는지는 재고관리에서 데이터수집"
- "전표 상태 영문 금지, 현장감 있는 순수 한글"
- "대시보드 시각화 클릭 = 해당 데이터/전표로 바로 연동"
- ⭐ **"이건 잘 되는게 아니야. 아예 안 되는 걸 하는거지."** (4/27 수금·지급 정공법)
- ⭐ **"거래가 많아지면 하나하나 하기 힘들어"** (4/27 일괄처리)
- ⭐ **"이 업무 흐름에 따르는 재고, 미수수금지급까지 완벽해야"** (4/27 4축 무결성)

---

## 📍 현재 상태 (2026-04-27 봉합 시점)

### Git
- 브랜치: `develop`
- HEAD: 인수인계 봉합 커밋 (방금 박힘)
- 4/27 누적 커밋:
  - **e40fabf** 수금·지급 정공법 + 일괄처리 + 대시보드 클릭 (WS-04/05)
  - **4c4f8fc** 재고이송 콤보 매핑 + 페이로드 정정 (WS-01)
  - **9827b78** 대시보드 클릭 + 재고이송 자동 셋팅 + 작지서
  - **3652b65** 안전재고 평소 안전망 + 한글화

### 환경
- API: `localhost:5257`
- Web: `localhost:5234`
- DB: `hitpan_erp` (MariaDB), 계정 `hitpan / Hitpan2025!`
- 테스트 로그인: `tenant@hitpan.kr / Admin1234!`

### 환경 점검 one-liner
```bash
curl -s -o /dev/null -w "api=%{http_code} " http://localhost:5257/health && curl -s -o /dev/null -w "web=%{http_code}\n" http://localhost:5234/
```

### API/Web 재기동 (필요 시)
```bash
dotnet build src/HitPan.sln -c Debug --nologo
cd src/HitPan.API && dotnet run --no-build -c Debug &
cd src/HitPan.Web && dotnet run --no-build -c Debug &
```

---

## 🏆 4/27 종합 성과 — 4축 무결성 100% 통과

> 사장님 직접 인장: *"워크플로우 흐름은 정합성 무결성 깨지는거 없이 완벽하다."*

### 본 흐름 7단계 사슬 (끊김 0건)
```
상품마스터 → 생산(BOM) or 발주 → 매입 → 견적 → 수주 → 판매 → 수금/지급
```

### 4축 SoT (e40fabf 시점)
| 축 | 값 |
|---|---|
| **매입 확정** | 34건 / 203,885 |
| **판매 확정** | 1건 / 1,100 |
| **재고** | 자재 14종 모두 stock_ledger = item_stock (diff 0), 음수 0 |
| **미수금** | 0 (매출 1,100 = 수금 1,100) |
| **미지급** | 0 (매입 203,885 = 지급 203,885) |
| **ref 연결** | collections 100% / payments 100% |

### 4축 즉시 검증 SQL (다음 세션에서 사전·사후 비교용)
```bash
mysql -uhitpan -pHitpan2025! --default-character-set=utf8mb4 hitpan_erp -N -e "
-- 축1. 본 흐름
SELECT (SELECT COUNT(*) FROM purchase_receipts WHERE status='confirmed') AS pr_confirmed,
       (SELECT IFNULL(SUM(total_amount + vat_amount),0) FROM purchase_receipts WHERE status='confirmed') AS pr_amount,
       (SELECT COUNT(*) FROM sales_deliveries WHERE status='confirmed' AND is_deleted=0) AS sd_confirmed,
       (SELECT IFNULL(SUM(total_amount + vat_amount),0) FROM sales_deliveries WHERE status='confirmed' AND is_deleted=0) AS sd_amount;

-- 축2. 재고 (자재별 diff != 0인 것만 — 0행이어야 정상)
SELECT i.item_name, COALESCE(SUM(sl.qty_in - sl.qty_out), 0) AS ledger, COALESCE(stk.s, 0) AS stock,
       (COALESCE(SUM(sl.qty_in - sl.qty_out), 0) - COALESCE(stk.s, 0)) AS diff
FROM items i LEFT JOIN stock_ledger sl ON sl.item_id = i.item_id
LEFT JOIN (SELECT item_id, SUM(current_qty) AS s FROM item_stock GROUP BY item_id) stk ON stk.item_id = i.item_id
WHERE i.is_deleted=0 GROUP BY i.item_name, stk.s HAVING diff != 0;

-- 축2-b. 음수재고 (0행이어야 정상)
SELECT i.item_name, w.wh_name, s.current_qty FROM item_stock s
JOIN items i ON i.item_id=s.item_id JOIN warehouses w ON w.warehouse_id=s.warehouse_id
WHERE s.current_qty < 0 AND i.is_deleted=0;

-- 축3+4. ref 끊김 (둘 다 0이어야 정상)
SELECT (SELECT COUNT(*) FROM collections WHERE is_active=1 AND (ref_doc_id IS NULL OR ref_doc_type IS NULL)) AS coll_missing,
       (SELECT COUNT(*) FROM payments WHERE is_active=1 AND ref_order_id IS NULL) AS pay_missing;
"
```

---

## 🎯 사장님 MVP 결정 (4/27 확정 — WS-20260427-06)

### ✅ MVP 범위 (베타 출시 게이트 통과 영역)
- 본 흐름 7단계 사슬 (위 그림)
- 4축 무결성 (재고 + 미수 + 미지급 + ref)

### ⏸️ MVP 이후 P2 (베타 운영 중 점진 개선)
| # | 영역 | 보류 사유 |
|---|---|---|
| ① | 데이터 분석 (대시보드 고도화) | 본 흐름 의존 0% |
| ② | 결재함 | 워크플로우 6단계로 이미 박힘 |
| ③ | 각종 설정 (결재설정/등록기기/데이터이관/사용환경/사원관리) | 베타 시작 시 기본값 충분 |
| ④ | 경리세무 | 회계 자동분개 별도 작지서 |
| ⑤ | 전자세금계산서관리 | **외주 검토 중** |
| ⑥ | 인사근태 | ERP 본 흐름과 별개 도메인 |

→ 자세한 내용: `docs/work-orders/WS-20260427-06_MVP_범위_봉인_보류영역_정리.md`

---

## 🚀 4/28부터 큰 작업 4개 (사장님 직접 지시)

> 사장님: *"내일부턴 터널링 작업부터!!! 큰 작업은 이렇게 네 가지야"*

### 마일스톤 1. 🎯 **로컬 터널링 설계 + 개발** ← 4/28 시작
- 베타 체험단 20곳 중 10곳 로컬 터널링
- 검토 항목:
  - 터널링 방식: Cloudflare Tunnel vs ngrok vs frpc — **사장님 답변 대기**
  - 베타 도메인: `hitpan-prov.workers.dev` (무료) → 정식 `prov.hitpan.app` 2단계
  - 로컬 API → Cloudflare → 사용자 브라우저 인증 흐름
  - `tenant1.hitpan-prov.workers.dev` wildcard
  - 설치 가이드 (v1.0.6 EXE 위에 터널 기동)
  - 터널 끊김 폴백 + 자동 재연결
- 데이터 경계 (사장님 헌법 §18): 로컬 = 고객 PC, 본사로 절대 전송 X
- 베타 첫 10곳 대상 — **사장님 답변 대기**

### 마일스톤 2. ☁️ **클라우드 설계 + 개발**
- 베타 체험단 20곳 중 10곳 클라우드
- 코드 90% 공유 (메모리 `project_two_track_strategy.md`)
- 차이 영역: 인증·테넌트 격리·DB 호스팅·과금 연동
- 멀티테넌트 본격 가동

### 마일스톤 3. 🏢 **백오피스 설계 + 개발**
- 본사 경영관리 (현재 SaaS 3분할 #3)
- 받는 데이터 (사장님 헌법 §18):
  - SaaS 운영 (가입/결제/라이선스/텔레메트리/CS)
  - 대리점 영업 (채널/수수료/KPI)
- 받지 않는 데이터: **고객사 ERP 업무 데이터 절대 미수신** (헌법 #18)

### 마일스톤 4. 🌐 **프론트오피스 설계 + 개발 (조건부)**
- 사장님: *"이건 안 할 수도 있음. 기존 공영정보 홈페이지 이용할 수도"*
- 결정 보류 — 마일스톤 3 진행 중 사장님 판단

---

## ⚠ 진행 중 / 결재 대기

### 🟡 작지서 결재 대기 (구현 보류)
- **WS-20260427-02** 출고창고 콤보→라벨화 (P2, 베타 후 검토)
- **WS-20260427-03** 창고관리 재고 드릴다운 (사장님 결재 대기)

### 📋 작지서 발행 (결정 기록)
- **WS-20260427-06** MVP 범위 봉인 + 보류 영역 정리 ✅ (사장님 승인)

---

## 🚦 새 창 첫 메시지 권장 형태

```
사장님, 4/27 4축 무결성 + 7단계 사슬 봉합 위에서 이어갑니다.
e40fabf 시점 SoT는 인수인계서 §4축 SoT 그대로 박혀있습니다.

오늘부터 큰 작업 4개 중 [마일스톤 1 — 로컬 터널링] 들어갑니다.

먼저 두 가지 답변 주시면 설계 들어갑니다:
1. 터널링 방식 후보 셋 (Cloudflare Tunnel / ngrok / frpc) 중 사장님 선호?
2. 베타 첫 10곳에 누가 먼저 들어가는지 (대상 고객사 결정 됐나요?)

답변 주시면 docs/design/TUNNEL-STRATEGY.md (또는 WS-20260428-01) 작성 들어갑니다.
잘 되는 거 안 건들고 설계 문서부터 차분히 갑니다.
```

---

## 🪪 사장님 회사 어록 (마음에 새기기)

> "코드를 짜고 고객한테 첫 등장하기 전까진, 우리는 우리가 만든 프로그램을 가장 극한의 환경에서 검증해야 한다."
> "히트판은 기술로 이긴 게 아니다. **쉬움으로 이겼다.**"
> "이 화면, 처음 보는 사람이 혼자 쓸 수 있냐?"
> "잘 되는 거 건들지 말자."
> ⭐ "이건 잘 되는게 아니야. 아예 안 되는 걸 하는거지." (4/27)
> ⭐ "거래가 많아지면 하나하나 하기 힘들어" (4/27)
> ⭐ "이 업무 흐름에 따르는 재고, 미수수금지급까지 완벽해야" (4/27)
> ⭐ "워크플로우 흐름은 정합성 무결성 깨지는거 없이 완벽하다." (4/27 사장님 직접 인장)

---

## 📁 핵심 파일 경로

- 사이드바: `src/HitPan.Web/Layout/Sidebar.razor`
- 대시보드: `src/HitPan.Web/Pages/Dashboard.razor`
- 수금·지급 정공법: `src/HitPan.Web/Pages/Finance/CollectionPage.razor`, `PaymentPage.razor`
- 수금·지급 서비스: `src/HitPan.Application/Services/CollectionService.cs`
- BOM 서비스: `src/HitPan.Application/Services/BomService.cs`
- 한글 라벨: `src/HitPan.Web/Helpers/StatusLabel.cs`
- 작지서: `docs/work-orders/`
- 작지서 06 (MVP 봉인): `docs/work-orders/WS-20260427-06_MVP_범위_봉인_보류영역_정리.md`

---

## 어벤져스 출근부 (즉시 호출 가능)

**임원급 (3명)**: 닥터스트레인지(PM) / 래리 앨리슨(CTO) / 브라운킴(설계팀장)
**부장급 (10명)**: AI수석 / 백엔드/DB/보안/프론트 매니저 / 수석 웹디자이너 / ERP 매니저 / 기술영업팀장 / 마케팅팀장 / 데이비드 박
**서브에이전트 (15명)**: 백엔드 3 / 보안 3 / DB 3 / 프론트 1 / UX·UI 2 / 웹퍼블리셔 2 / QA 1

전원 **읽기 전용 모드** 출근. 사장님 OK 받기 전엔 키보드 안 침.

---

**환영합니다 사장님. 4/27 4축 무결성 봉합 위에서 마일스톤 1 (로컬 터널링)부터 갑니다. 🎯**
