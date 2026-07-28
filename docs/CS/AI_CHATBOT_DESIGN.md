# 히트판 AI 챗봇 통합 설계서

> 문서번호: 20260611-AI-CHATBOT-DESIGN-v1.0
> 작성: AI수석 (KAIST AI 석사 / Google 30년) + 설계팀장 브라운킴 (SAP SE 30년)
> 작성일: 2026-06-11
> 상태: 사장님 결재 명세 반영 초안
> 정합 문서: `PRD_AI_CS_SYSTEM.md` v1.2 (2026-05-25 결재) / `project_ai_cost_model` / `project_vision_ai_platform`

---

## 0. 사장님 결재 명세 (2026-06-11)

### 의무 영역 2가지
1. **간단한 자연어 도움** — 사용 방법 안내·문의 응답
2. **자연어로 모든 기능 가도** — 챗봇에서 ERP 모든 기능 호출 + 조작

### 핵심 영역
- **AI 엔진 = Claude (Anthropic)** 1차 표준
- **사장님 격언**: "만들고 설계한 팀이 히트판 프로그램을 가장 잘 안다"
  → 자체 팀 영역(코드·매뉴얼·내부 문서)을 학습 데이터 1순위로, 외부 RAG 의존 0건

### 기존 자산 정합 점검 결과
- `docs/CS/PRD_AI_CS_SYSTEM.md` v1.2 — BYOK 3종(Anthropic/OpenAI/Google), 토큰 풀, 로컬 RAG(ONNX Runtime DirectML), bge-m3 INT8 양자화, 5중 검증 게이트 통과 명시 — 본 설계의 상위 PRD로 유지
- `src/HitPan.API/Controllers/ChatbotController.cs` — Phase A(FAQ/KB) 영역 가동 중. 본 설계는 Phase B(Claude + Tool Use) 확장
- 메모리 `project_ai_cs_prd` / `project_ai_cost_model` / `project_vision_ai_platform` — 본 설계 일관 반영

본 문서는 PRD를 대체하지 않고, 결재 명세 2가지 의무를 **구현 가능한 아키텍처 7영역**으로 분해한 통합 설계서.

---

## 1. 챗봇 아키텍처 7대 영역

### 1.1 AI 엔진 — Claude 1차 표준 + 멀티 공급자 추상화

```
사용자 발화
  ↓ (고객 PC, 로컬)
ChatbotOrchestrator (HitPan.Application)
  ↓
IAiProvider 선택
  ├─ AnthropicProvider (1차 표준, claude-opus-4-7 / claude-sonnet-4-7)
  ├─ OpenAiProvider    (BYOK 옵션, gpt-4.1)
  └─ GoogleProvider    (BYOK 옵션, gemini-2.5-pro)
  ↓
공급자 API 직통 호출 (본사 프록시 금지, PRD §BYOK 절대조항)
```

- **1차 기본값**: 사장님 명령에 따라 Claude 표준. 풀(고객 BYOK 미설정) 응답도 Claude.
- **Prompt Caching 필수**: System Prompt 90% 이상 캐시 적중 목표 (Anthropic `cache_control: ephemeral`)
- **추상화 인터페이스**: PRD F-11 `IAiProvider` 그대로 채택

### 1.2 RAG 영역 — "팀이 가장 잘 안다" 정합 (외부 의존 0)

#### 색인 대상 (자체 자산)
| 영역 | 위치 | 색인 단위 | 갱신 트리거 |
|---|---|---|---|
| 컨트롤러 | `src/HitPan.API/Controllers/*.cs` | 메서드 단위 + XML doc | 빌드 시 |
| 서비스 | `src/HitPan.Application/Services/*.cs` | 메서드 단위 | 빌드 시 |
| Razor 화면 | `src/HitPan.Web/Pages/**/*.razor` | 페이지 단위 | 빌드 시 |
| 사용자 매뉴얼 | `docs/manual/**/*.md` | 섹션 단위 | 문서 커밋 시 |
| 설계 문서 | `docs/design/**/*.md` | 섹션 단위 | 문서 커밋 시 |
| FAQ/KB | DB `chatbot_kb` | 항목 단위 | 어드민 등록 시 |

#### 임베딩
- **모델**: bge-m3 INT8 양자화 110MB (PRD v1.2 Plan B 정합)
- **런타임**: ONNX Runtime DirectML — 고객 PC GPU/CPU 로컬 실행
- **벡터 저장**: 로컬 SQLite 또는 MariaDB BLOB 인덱스 (본사 0건, 헌법 #18·#22)
- **임베딩 트리거**: 문서 등록 시점 + 백그라운드 큐 (쿼리 시 호출 0)
- **외부 RAG 영역 의존**: **0건** (사장님 격언 정합)

#### 코드 임베딩 전처리 표준 (본 설계 신규)
1. C# 컨트롤러 → 메서드별 `[Route]` + XML doc + 시그니처 + 호출 Service 시그니처 묶어 1청크
2. Razor → `@page` 라우트 + 페이지 제목 + 주요 컴포넌트 + 폼 필드 라벨 묶어 1청크
3. 청크당 평균 400 토큰, 최대 1024 토큰 (bge-m3 정합)
4. 메타데이터: `{ type, file, route, method, version }` — Tool Use 라우팅에 재사용

### 1.3 Tool Use 영역 — 자연어로 모든 기능 가도 (본 결재 핵심)

#### Tool 카탈로그 표준
| Tool 이름 규칙 | 동작 분류 | 권한 정합 (PRD OI-5) |
|---|---|---|
| `query_*` | 조회 (SELECT) | 직원 이상 |
| `draft_*` | 초안 생성 (DB 미반영) | 영업 이상 |
| `commit_*` | 확정 (원장 반영) | 관리자만 |
| `cancel_*` | 취소·환원 | 관리자만 + 확인 다이얼로그 |

#### 컨트롤러 → Tool 매핑 (47개 컨트롤러 전수)

본사 점검 결과 `src/HitPan.API/Controllers/` 47개 컨트롤러 존재. Tool 자동 생성 표준:

```csharp
// Roslyn Source Generator (빌드 시 자동 생성)
// [HttpGet("api/stock/low")] StockController.GetLowStock(int threshold)
//   → Tool 정의:
{
  "name": "query_stock_low",
  "description": "재고가 임계치 이하인 상품을 조회한다",
  "input_schema": {
    "type": "object",
    "properties": { "threshold": { "type": "integer", "description": "임계 수량" } },
    "required": ["threshold"]
  }
}
```

#### 예시 매핑 (사장님 명세 예시 정합)
| 사용자 발화 | Tool 호출 | 컨트롤러 |
|---|---|---|
| "거래처 김철수 매출 조회해줘" | `query_partner_sales` | `PartnerController` + `SalesController` |
| "오늘 매입 거래 5건 등록해줘" | `draft_purchase_bulk` → 확인 → `commit_purchase` | `PurchaseController` |
| "재고 100개 이하 상품 알려줘" | `query_stock_low(threshold=100)` | `StockController` |
| "OSB 1000장 영신 발주 넣어줘" | `draft_purchase_order` → 확인 → `commit_purchase_order` | `PurchaseController` |
| "이번 달 매출 합계?" | `query_sales_monthly_total` | `SalesController` |

#### Tool 정의 표준화 규칙
1. 이름: snake_case, 동사_명사 또는 동사_명사_수식
2. 설명: 사용자 발화 키워드 1개 이상 포함 (한국어)
3. 파라미터: JSON Schema, 필수 항목 명시
4. tenant_id·user_id 파라미터 금지 (JWT 클레임에서만, 헌법 #2)
5. 응답: `{ success, data, message, suggested_next_actions[] }` 표준

### 1.4 권한 영역 (헌법 #2 / #18 / #22 정합)

- **tenant_id**: JWT 클레임 추출만. Tool 입력 스키마에 절대 노출 금지
- **호출 흐름**: 챗봇 발화 → Tool 호출 → 동일 ASP.NET Core 컨트롤러 경로 통과 → 기존 Authorization Policy 그대로 적용
- **권한 매트릭스** (PRD OI-5 정합):
  - 조회 = 직원
  - 초안 = 영업
  - 확정 = 관리자
  - 삭제·일괄취소 = 금지 (관리자도 챗봇 경로로는 불가, UI 직접 조작만)
- **본사 데이터 0건**: 챗봇 대화 본문은 고객 PC 로컬 저장. 본사로는 메타 통계(횟수, 토큰 합계, 실패율)만 Push

### 1.5 UI 영역

- **위치**: 모든 ERP 화면 우하단 플로팅 버튼 (PRD F-01)
- **카피**: "히트판에게 물어보세요" — 히트판 정신 "쓰기가 겁나 쉬웠다" 정합
- **컴포넌트**: `src/HitPan.Web/Shared/ChatbotWidget.razor` (신설)
- **24/7 가동**
- **Phase 2 음성 입력**: Web Speech API (브라우저 무료) — 베타 이후
- **확인 다이얼로그**: `commit_*` / `cancel_*` 호출 직전 사용자 확인 모달 강제

### 1.6 안전 영역

- **위험 Tool 확인**: `commit_*` 계열은 변경 항목 요약 + 사용자 명시 확인 후에만 호출
- **감사 로그**: `chat_session_logs` + `tool_invocation_logs` 두 테이블
  - 호출 Tool, 입력 파라미터(개인정보 마스킹), 결과 상태, user_id, tenant_id, 호출 시각
  - PRD v1.1 패치 #8 "Tool 거부 시 audit_logs INSERT 의무" 정합
- **미확인 Tool 호출 금지**: LLM이 카탈로그 외 Tool 호출 시도 시 즉시 거부 + 감사 로그
- **PII 마스킹**: 주민번호·계좌번호 등은 LLM 컨텍스트 진입 전 마스킹

### 1.7 비용 영역

- **토큰 모니터링**: 풀(본사 Anthropic 키) + BYOK 분리 집계 (PRD F-12 정합)
- **티어 한도**: Basic 100K / Pro 500K / Enterprise 3M (메모리 `project_ai_cost_model` 정합)
- **차감 우선순위**: BYOK 우선 → 풀 차감 (PRD v1.1 패치 #4)
- **한도 초과 안내**: "이번 달 AI 사용량 초과 — BYOK 설정 또는 플랜 업그레이드 권장"
- **Prompt Caching**: System Prompt + RAG 컨텍스트 캐시로 90% 비용 절감 목표

---

## 2. 산출물

| 산출물 | 위치 | 상태 |
|---|---|---|
| **본 통합 설계서** | `docs/CS/AI_CHATBOT_DESIGN.md` | 2026-06-11 신설 |
| 상위 PRD | `docs/CS/PRD_AI_CS_SYSTEM.md` v1.2 | 기존 유지 |
| API 컨트롤러 (Phase A) | `src/HitPan.API/Controllers/ChatbotController.cs` | 가동 중 |
| 위젯 UI (신설 예정) | `src/HitPan.Web/Shared/ChatbotWidget.razor` | 미작성 |
| Tool 자동 생성 (신설 예정) | `src/HitPan.Application/AI/ToolCatalogGenerator.cs` | 미작성 |
| 임베딩 색인 (신설 예정) | `src/HitPan.Application/AI/CodebaseIndexer.cs` | 미작성 |

---

## 3. 다음 단계 (구현 일정 — 베타1 8/3 정합)

| 단계 | 기간 | 산출물 | 검증 |
|---|---|---|---|
| S1. Tool 카탈로그 생성기 | 1주 | Roslyn 분석 → 47개 컨트롤러 Tool 자동 생성 | Tool 수 ≥ 80개, 시그니처 100% 일치 |
| S2. Claude IAiProvider 구현 | 1주 | AnthropicProvider + Prompt Caching | 캐시 적중률 90%+ 실측 |
| S3. RAG 코드베이스 색인 | 1주 | bge-m3 ONNX 색인 + 로컬 SQLite | 검색 Top-3 정확도 ≥ 85% |
| S4. ChatbotOrchestrator + Tool Use 라우팅 | 1주 | 발화 → Tool 호출 → 응답 | E2E 시나리오 10건 PASS |
| S5. ChatbotWidget UI + 확인 다이얼로그 | 3일 | 우하단 플로팅 + commit 확인 | UI 검증 + 권한 매트릭스 |
| S6. 5중 검증 + 감사 로그 | 2일 | 보안 매니저 1·2 + 검증팀장 | 헌법 #23 통과 |

---

## 4. 헌법 정합 점검

- 헌법 #2 (tenant_id JWT 클레임만): Tool 입력 스키마 금지 명시
- 헌법 #5 (암호화 컬럼): BYOK 키 AES-256 Value Converter (PRD 그대로)
- 헌법 #15 (빈 catch 금지): Tool 실행 실패 시 `_logger.LogWarning` 의무
- 헌법 #18 / #22 (본사 업무 데이터 0): 대화 본문 고객 PC 로컬 저장, 메타만 Push
- 헌법 #23 (5중 검증): S6 단계 게이트
- 헌법 #25 (쉽게·정확하게·안전하게): 자연어 입력 = 쉽게 / Tool 시그니처 강제 = 정확하게 / 권한·확인 다이얼로그 = 안전하게
- 헌법 #33 (의중 이해 후 코드): 본 설계서가 의중 정리·재확인용 산출물. 사장님 결재 후 S1 착수
- 헌법 #35 (3시스템 분리): 챗봇은 ERP 로컬 전용. 백오피스·랜딩 진입 금지
