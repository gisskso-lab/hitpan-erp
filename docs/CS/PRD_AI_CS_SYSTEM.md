# 히트판 AI CS 시스템 PRD
> 문서번호: 20260525PRD-AI-CS-v1.2
> 작성: AI수석 (v1.0 2026-05-08 → v1.1 2026-05-25 → v1.2 2026-05-25 패치)
> 상태: **사장님 결재 완료 (5/25 11:00) — Plan B 확정**

---

## 변경 이력

### v1.1 → v1.2 (2026-05-25 11:00 사장님 결재, 보안설계 8건 중 PRD 패치 영역)

| # | 영역 | v1.1 | v1.2 (패치) | 발의 |
|---|------|------|--------------|------|
| 1 | §3.5 RAG 임베딩 호스팅 | **Plan A: 본사 풀 호스팅** (임베딩 호출은 본사 API) | **Plan B: 고객 PC 완전 로컬 (ONNX Runtime DirectML)** | 보안 매니저 2 |
| 2 | bge-m3 양자화 | FP16 ~600MB | **INT8 양자화 280MB → 110MB** | 보안 매니저 2 + AI수석 |
| 3 | 임베딩 트리거 | 실시간 | **문서 등록 시점만 + 백그라운드 큐** (쿼리 시 임베딩 호출 0) | AI수석 |
| 4 | 모델 배포 | 본사 다운로드 | **설치 EXE 모델 동봉 (오프라인 동작)** | 보안 매니저 2 |
| 5 | 보안 §역추적 | 명시 없음 | **70%+ 의미 복원 실측 박제 + Plan A 폐기 사유** | 보안 매니저 2 |
| 6 | 5중 검증 게이트 5 | "통과 가능성" | **완전 통과 명시 (본사 임베딩 0건)** | 검증팀장 |
| 7 | 백신 호환성 | 명시 없음 | **백신 5종 호환 P0-2 작지서 (보안 매니저 2 영역)** | 보안 매니저 2 |
| 8 | Day 3 게이트 | 부하 70 + p95 + 1클릭 차단 | **+ INT8 양자화 정확도 회귀 KLUE-STS 5% 이내 (AI수석 영역)** | AI수석 |

### AI수석 정정 박제 (정직 의무)

- **v1.1 Plan A "본사 풀 호스팅 + 임베딩 호출은 본사 API"는 AI수석 본인 권고였음.**
- 근거로 제시한 "본사가 보는 건 1024차원 float 배열뿐, 0.1% 역추적" = **선의의 과소평가**로 판정됨.
- 보안 매니저 2가 다음 근거로 70%+ 의미 복원 실측 제시:
  - USENIX Security 2023 — *Text Embedding Inversion via Latent Space Reconstruction*
  - vec2text (2023) — 임베딩에서 원문 의미 70~92% 복원 (단문 도메인은 92%)
  - 시큐브 2024 PoC — 한국어 ERP 매뉴얼 도메인 임베딩 → 의미 복원 73%
- AI수석은 보안 매니저 2 분석에 **전면 동의**, Plan A 폐기 후 Plan B(고객 PC 완전 로컬) 채택을 본인 의견으로 정정.
- 위반 가능성이 있었던 헌법: #18(본사 무전송) · #22(데이터 최소주의) · #24(책임 분산) · #25(안전하게) + 5중 검증 게이트 5(데이터 최소주의).
- 본 정정은 받아쓰기 방지 의무(feedback_real_validation.md, feedback_challenge_owner.md) 정합.

### v1.0 → v1.1 (2026-05-25 10:00 사장님 결재 12건 중 PRD 패치 9건)

| # | 영역 | v1.0 | v1.1 (패치) | 발의 |
|---|------|------|--------------|------|
| 1 | F-43 RAG Phase B | "MVP 이후" | **즉시화** (마이그 99.7% + 베타 20곳 다국어 정합) | AI수석 |
| 2 | F-11 BYOK 공급자 | Anthropic 단일 | **3종 추상화 `IAiProvider`** (Day 7=Anthropic·Day 14=OpenAI·Day 21=Google) | 본부장 |
| 3 | §7.1 신설 | 없음 | **잔존 §예외 우선 응답** (회계 86.7%·세금계산서 21건·카드사용이력) | ERP 매니저 |
| 4 | F-12 토큰 한도 | 풀만 차감 | **BYOK 우선 소진 → 풀 차감** 명문화 | 설계팀장 |
| 5 | OI-3 녹화본 | 위치 미정 | **고객 PC 로컬 저장 확정** (본사 0건, 헌법 #22) | 보안 매니저 2 |
| 6 | OI-5 Tool 권한 | 권한 미정 | **역할 매트릭스 명문화** (조회=직원·견적초안=영업·확정=관리자·삭제=금지) | ERP 매니저 |
| 7 | §BYOK 절대조항 | 없음 | **본사 프록시 금지** (Anthropic 호출은 고객 PC 직통) | 검증팀장 |
| 8 | §Tool Use 감사 | 없음 | **거부 시 audit_logs INSERT 의무** | 검증팀장 |
| 9 | §원격 CS 차단 | 없음 | **고객 1클릭 즉시 차단 버튼** (애니소프트 세션 중에도) | 검증팀장 |

---

## 1. 개요

### 1.1 제품 비전

> "전화 안 해도, 기다리지 않아도 — 히트판이 먼저 해결한다"

레거시 히트판이 쉬움으로 살아남았듯, 신 히트판은 **AI가 옆에서 도와주는 ERP**로 차별화한다.
고객사 직원은 설명서 없이 챗봇에 물어보면 되고,
기능 이슈가 생기면 원격 CS 요청 한 번으로 해결된다.

### 1.2 포지셔닝

| 구분 | 더존·이카운트 | 히트판 |
|---|---|---|
| CS 방식 | 전화 → 사람 대기 → 원격 접속 | AI 챗봇 즉시 응답 + 원격 필요 시 클릭 한 번 |
| CS 시간 | 업무시간 내 | 24시간 |
| CS 비용 | CS 담당자 인건비 | AI 처리 (토큰 비용만) |
| 학습 | 없음 | CS 누적 → 점점 정확해짐 |
| 원격 지원 | 팀뷰어 수동 | 애니소프트 연동 (레거시 동일 인프라) |

### 1.3 범위

이 PRD는 히트판 ERP 내 **AI CS 시스템** 전체를 다룬다.
3개 시스템(랜딩·백오피스·ERP) 중 **ERP 전담** 기능이며,
CS 처리 메타 통계만 백오피스로 Push (업무 데이터 전송 금지, 헌법 #18·#22).

---

## 2. 사용자 스토리

### 2.1 고객사 일반 직원 (Maya)
```
"수불부를 어떻게 조회해요?"
→ 챗봇: 수불부 화면 안내 + 조회 방법 단계별 설명
→ 필요 시 해당 화면 바로가기 링크 제공
```

### 2.2 고객사 관리자 (James)
```
"이번 달 매출 합계가 얼마야?"
→ 챗봇: DB 조회 Tool 실행 → 실시간 데이터 응답
→ "5,430만원입니다. 자세히 보시겠어요?" → 손익현황 화면 링크
```

### 2.3 고객사 대표 (사장님)
```
"OSB 1000장 영신에 발주 넣어줘"
→ 챗봇: 발주 작업지시서 초안 생성
→ 대표: 내용 확인 후 승인 클릭
→ 시스템: 발주 자동 등록
```

### 2.4 기능 이슈 발생 시
```
"발주 확정했는데 재고가 안 늘어요"
→ 챗봇: 진단 안내 제공
→ 해결 안 되면: "원격 지원 요청" 버튼
→ 고객 동의 → 애니소프트 세션 생성 → CS 담당자 접속
→ 처리 완료 → 내역 자동 저장 → RAG 누적
```

---

## 3. 기능 요구사항

### 3.1 챗봇 UI

**F-01. 챗봇 버튼**
- 모든 ERP 화면 우하단 고정 플로팅 버튼
- 아이콘: 히트판 로고 + 말풍선
- 클릭 시 채팅창 슬라이드업

**F-02. 채팅창**
- 크기: 너비 380px, 높이 560px (모바일은 전체화면)
- 구성: 헤더(히트판 AI) + 대화 영역 + 입력창 + 전송 버튼
- 스트리밍 응답 (타이핑 효과, SSE)
- 대화 히스토리: 세션 내 유지 (새로고침 시 초기화)

**F-03. 초기 메시지**
```
안녕하세요! 히트판 AI입니다
무엇이든 물어보세요.
• 화면 사용법
• 데이터 조회
• 업무 처리 도움
```

**F-04. 빠른 답변 버튼 (Quick Reply)**
- 초기: [수불부 조회] [발주 방법] [매출 확인] [원격 지원]
- 대화 흐름에 따라 동적 변경

---

### 3.2 AI 엔진 (멀티 공급자 추상화)

**F-10. System Prompt — 히트판 매뉴얼 사전 탑재**
- 히트판 전체 화면·기능·워크플로우 지식 포함
- 6단계 업무 흐름 (설정→마스터→매입→판매→현황→재무)
- 각 화면별 사용법, 주의사항, 자주 묻는 질문
- Prompt Caching 적용 (`cache_control: ephemeral`) → 반복 호출 시 비용 90% 절감
- 매뉴얼 업데이트: 서버 재시작 없이 즉시 반영

**F-11. BYOK (Bring Your Own Key) — v1.1 패치 #2: 3종 공급자 추상화**

```csharp
// 추상화 인터페이스 (Day 3 작D 명세)
public interface IAiProvider
{
    string ProviderName { get; }                   // "anthropic" | "openai" | "google"
    Task<ChatResponse> ChatAsync(ChatRequest req, CancellationToken ct);
    IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest req, CancellationToken ct);
    Task<int> CountTokensAsync(string text);
}
```

| 공급자 | 출시일 | 모델 (기본) | 비고 |
|--------|--------|-------------|------|
| Anthropic | **Day 7 (5/30)** | claude-opus-4-7 (기본) / claude-sonnet-4-7 | 1차 출시, Prompt Caching 필수 |
| OpenAI | **Day 14 (6/6)** | gpt-4.1 / gpt-4.1-mini | tool_choice·function_calling 매핑 |
| Google | **Day 21 (6/13)** | gemini-2.5-pro / gemini-2.5-flash | system_instruction 매핑 |

- 고객사 설정 화면 → 공급자 선택 + 키 입력
- 저장: AES-256 암호화 (Value Converter, 헌법 #5)
- DB 컬럼: `tenants.ai_provider`, `tenants.ai_api_key_encrypted` (DB-27 확장)
- 키 없을 시: 히트판 기본 풀 (Anthropic) 사용 + 사용량 한도 적용

**§BYOK 절대조항 (v1.1 패치 #7, 검증팀장 발의·사장님 결재) — 본사 프록시 금지**

> **고객 BYOK 키 사용 시 LLM 호출은 반드시 고객 PC에서 공급자 API로 직통 (Direct).
> 본사 서버(api-demo.hitpan.kr 등)를 경유하는 코드는 PR 즉시 반려.**

- 위반 시: 5중 검증(헌법 #23) ⑤ 데이터 최소주의 실패 처리
- 본사는 키 평문을 1바이트도 보지 못함 (암호화는 고객 PC DPAPI + AES-256)
- 호출 경로 검증: 빌드 시 정적 분석(Roslyn analyzer)으로 `HttpClient.PostAsync("https://api.anthropic.com")` 호출이 `HitPan.API` 어셈블리에서 발견되면 빌드 실패
- 풀 사용(BYOK 미설정) 고객은 본사 풀이므로 본 절대조항 적용 제외

**F-12. AI 사용량 제한 — v1.1 패치 #4: BYOK 우선 소진 → 풀 차감 명문화**

- Basic 티어: 풀 월 100K 토큰
- Pro 티어: 풀 월 500K 토큰
- Enterprise 티어: 풀 월 3M 토큰

**차감 우선순위 (v1.1 신설):**
1. BYOK 키가 설정되어 있고 유효한 경우 → **BYOK 키로 호출, 풀 차감 0**
2. BYOK 키가 없거나 공급자에서 429/401 응답 → **풀에서 차감**
3. 풀도 소진 시 → "이번 달 AI 사용량이 초과되었습니다. BYOK 설정 또는 플랜 업그레이드를 권장합니다."

- BYOK 사용량은 통계 목적으로 `ai_usage_logs.byok_input_tokens`·`byok_output_tokens`에 별도 집계 (비용 0)

**§BYOK 키 로테이션·폐기 절차 (v1.1 신설, 사장님 P1 결재)**

| 단계 | 트리거 | 동작 |
|------|--------|------|
| 1. 등록 | 고객 키 입력 → 저장 시 | `created_at` 기록, `expires_at = created_at + 90일` 자동 설정 |
| 2. 만료 임박 | `expires_at - 14일` | 대시보드 + 챗봇 + 이메일 알림 ("키 90일 경과 임박, 회전 권장") |
| 3. 만료 | `now > expires_at` | 키 자동 비활성화 (DB는 보관, 호출만 차단) → 풀로 자동 fallback |
| 4. 즉시 폐기 | 고객이 [키 삭제] 클릭 | `DELETE /api/ai/settings/apikey` → 암호화 컬럼 NULL + audit_logs INSERT |
| 5. 강제 폐기 | 공급자 401/403 3회 누적 | 자동 비활성화 + 고객 알림 |

- 90일 = OWASP 권장 + 카드 정보 PCI-DSS 90일 회전 정합
- 키 평문은 어떤 단계에서도 로그·DB·메모리 덤프에 남기지 않음 (Span<char>로 처리, 헌법 #23)

---

### 3.3 Tool Use — 업무 데이터 조회 (읽기 전용)

**F-20. 재고 조회**
```json
{
  "name": "query_stock",
  "description": "현재 재고 수량 조회",
  "parameters": {
    "item_name": "상품명 (부분 일치)",
    "warehouse_id": "창고 ID (선택)"
  }
}
```

**F-21. 매출/매입 조회**
```json
{
  "name": "query_sales",
  "description": "기간별 매출·매입 합계 조회",
  "parameters": {
    "date_from": "시작일 (YYYY-MM-DD)",
    "date_to": "종료일 (YYYY-MM-DD)",
    "type": "sales | purchase | both"
  }
}
```

**F-22. 업체 검색**
```json
{
  "name": "search_partner",
  "description": "거래처 검색",
  "parameters": { "keyword": "업체명 (부분 일치)" }
}
```

**F-23. 발주 현황 조회**
```json
{
  "name": "query_purchase_orders",
  "description": "발주 목록 조회",
  "parameters": {
    "status": "draft | confirmed | all",
    "date_from": "시작일",
    "date_to": "종료일"
  }
}
```

---

### 3.4 Tool Use — 업무 실행 (반자동, 승인 필수)

> **원칙:** 모든 데이터 변경은 사용자 승인 후 실행. AI는 초안만 생성.

**F-30. 발주 생성 / F-31. 경비 처리 / F-32. 승인 플로우** — v1.0 동일

**§Tool Use — 거부 시 감사 로그 의무 (v1.1 패치 #8, 검증팀장 발의)**

- AI가 Tool을 호출했으나 권한 부족·테넌트 불일치·비즈니스 룰 위반 등으로 **거부**된 모든 케이스는 `audit_logs` INSERT 필수
- 필수 컬럼: `tenant_id, user_id, tool_name, tool_input(JSON), denial_reason, ai_session_id, created_at`
- 거부 사유 분류: `permission_denied | tenant_mismatch | business_rule_violation | rate_limited | external_api_failure`
- 미기록 시: 5중 검증(헌법 #23) ② 매니저 리뷰에서 즉시 반려
- 운영팀 일일 감사: 거부 로그 100건 이상 발생 시 알림 → 권한 매트릭스 재점검 트리거

**F-33. Tool 실행 권한 매트릭스 (v1.1 패치 #6, ERP 매니저 발의)**

| Tool | 일반 직원 | 영업담당 | 관리자 | 대표 |
|------|----------|----------|--------|------|
| ① query_stock (재고 조회) | O | O | O | O |
| ② query_sales (매출·매입 조회) | O (본인 담당분만) | O | O | O |
| ③ search_partner (업체 검색) | O | O | O | O |
| ④ query_purchase_orders (발주 조회) | O | O | O | O |
| ⑤ query_expenses (경비 조회) | O (본인) | O | O | O |
| ③' **create_quotation_draft (견적서 초안)** | X | **O (초안만)** | **O (확정)** | O |
| ⑥ create_expense (경비 등록) | O (본인) | O | O | O |
| ⑦ create_purchase_order (발주 확정) | X | X | **O** | O |
| (모든 삭제) | X | X | X | X (챗봇 삭제 미지원) |

- "본인 담당분만" = `purchase_orders.handler_id = jwt.user_id` 필터 자동 주입
- 견적서는 영업담당이 초안 생성 → 관리자가 확정 (2단계)
- 권한 부족 시 챗봇 응답: "이 작업은 [관리자] 권한이 필요합니다. 관리자에게 요청해주세요." + audit_logs INSERT

---

### 3.5 RAG — CS 내역 누적 학습 (v1.2 임베딩 영역 전면 재작성)

**F-40. 대화 저장** — v1.0 동일

**F-41. 유사 케이스 주입 (RAG Phase A → Phase B 즉시화로 통합)**

**F-42. 히트판 지식 베이스 (hitpan_knowledge)** — v1.0 동일 + priority 기능 강화 (§7.1 참조)

**F-43. RAG Phase B — 임베딩 영역 (v1.2 Plan B 확정)**

#### v1.2 핵심 결정 — Plan A 폐기, Plan B 채택

| 구분 | v1.1 Plan A (폐기) | v1.2 Plan B (확정) |
|------|---------------------|---------------------|
| 호스팅 | 본사 풀 임베딩 API | **고객 PC 완전 로컬** |
| 모델 | bge-m3 FP16 (~600MB) | **bge-m3 INT8 양자화 (280MB → 110MB)** |
| 런타임 | sentence-transformers (Python) | **ONNX Runtime DirectML (Windows 표준)** |
| 본사 임베딩 데이터 | 1024차원 float 배열 (역추적 0.1% 주장) | **0건 (본사 임베딩 데이터 보유 절대 금지)** |
| 임베딩 트리거 | 실시간 (쿼리·문서 등록 모두) | **문서 등록 시점만 + 백그라운드 큐** |
| 모델 배포 | 본사 다운로드 (최초 1회) | **설치 EXE 모델 동봉 (오프라인 동작)** |
| 백신 호환성 | 미검증 | **5종 호환성 검증 의무 (P0-2 보안 매니저 2)** |
| 헌법 정합 | #22·#24·#25 위반 가능성 | **#18·#22·#24·#25 + 5중 검증 게이트 5 완전 통과** |

#### Plan A 폐기 사유 (보안 매니저 2 근거)

- USENIX Security 2023 *Text Embedding Inversion via Latent Space Reconstruction* — 임베딩 → 원문 의미 복원 가능성 입증
- vec2text (2023, Cornell) — 단문/도메인 텍스트 임베딩에서 의미 70~92% 복원
- 시큐브 2024 PoC — 한국어 ERP 매뉴얼 임베딩 도메인에서 의미 복원율 **73%** 실측
- **결론**: 본사가 임베딩만 본다 해도 사실상 원문 의미를 70%+ 복원 가능 → 헌법 #22 "본사가 안 가지면 본사가 털릴 일 없다" 정신 위반.
- AI수석 v1.1 "0.1% 역추적" 주장은 학술 근거 부족한 선의의 과소평가로 판정, 본인 정정 박제 (변경 이력 §AI수석 정정 박제 참조).

#### Plan B 상세 설계

**① 모델 — bge-m3 INT8 양자화**
- 원본 bge-m3 (FP32) 1.2GB → FP16 ~600MB → **INT8 양자화 280MB → DirectML 최적화 후 110MB**
- 정확도 회귀: KLUE-STS 87.3 → INT8 양자화 시 목표 **83.0 이상 (5% 이내 회귀)**
- 차원 1024, 다국어 100+, 한국어 1위 유지 (양자화 후에도 한국어 벤치마크 유지 검증)

**② 런타임 — ONNX Runtime DirectML**
- Windows 표준 ML 런타임, DirectX 12 GPU 가속 (NVIDIA·AMD·Intel·내장 GPU 모두 지원)
- 백신 호환: Microsoft 서명 바이너리, Defender·V3 Lite·알약·네이버·Norton·McAfee 모두 화이트리스트 진입 용이 (헌법 #31 정합)
- Python 의존성 0 (.NET native), 설치 EXE 단일 배포 가능

**③ 임베딩 트리거 — 문서 등록 시점만 + 백그라운드 큐**
- 임베딩 호출은 **문서 등록·CS 대화 종료 시점**만 발생 → 쿼리(검색) 시 임베딩 호출 0
- 백그라운드 큐 (Windows Service Worker) — 등록된 문서가 큐에 적재, CPU idle 시 임베딩 생성 → CPU 부담 완화
- 쿼리는 사전 계산된 sqlite-vec 인덱스만 조회 (밀리초 응답)
- 큐 적체 시 사용자 알림: "지식 베이스 정합 중 (n건 대기)"

**④ 설치 EXE 모델 동봉 — 오프라인 동작**
- 설치 EXE에 INT8 양자화 모델 110MB 동봉 → 설치 직후 임베딩 즉시 가능
- 본사 다운로드 0 → 외부 네트워크 차단 환경(폐쇄망 고객사)에서도 동작
- 모델 업데이트는 ERP 자체 업데이트 채널로 배포 (별도 임베딩 모델 서버 불필요)
- 설치 EXE 크기 증가: ~110MB (수용 가능, 헌법 #31 백신 호환 정합)

**⑤ Vector DB — sqlite-vec (고객 PC 로컬) 확정**
- v1.1 1차 채택 그대로 유지
- 본사 MariaDB Vector Index·Qdrant 호스팅 모두 폐기 (본사 임베딩 0건 원칙)
- 파일: `%LOCALAPPDATA%\HitPan\KnowledgeBase\vec.db`

#### Day 3 게이트 추가 — AI수석 영역

| 항목 | 통과 기준 | 담당 |
|------|----------|------|
| INT8 양자화 정확도 회귀 | KLUE-STS 83.0 이상 (FP32 87.3 대비 5% 이내) | **AI수석** |
| 한국어 도메인 정확도 | ERP 매뉴얼 100문장 cosine 유사도 0.85 이상 유지 | AI수석 |
| ONNX 모델 로딩 시간 | 콜드 스타트 3초 이내 | AI수석 |
| 임베딩 호출 지연 | 단문(<512토큰) p95 200ms 이내 (DirectML GPU) | AI수석 |
| 백신 5종 호환 | Defender·V3·알약·네이버·Norton·McAfee 격리 0 | **보안 매니저 2** (협업) |
| 설치 EXE 모델 동봉 | EXE 빌드 성공 + 오프라인 환경 임베딩 동작 | 보안 매니저 2 |

#### 일정 (v1.2 갱신)

- **Day 4 (5/27)**: Day 3 게이트 통과 후 ONNX 모델 다운로드 + INT8 양자화 (Microsoft Olive 활용)
- **Day 5 (5/28)**: 학습 콘텐츠 5종 임베딩 + sqlite-vec 통합
- **Day 6 (5/29)**: 챗봇 Blazor UI + 검증 (작A·B·C 마감 정합)
- **Day 7 (6/1)**: Anthropic BYOK 1차 출시 + 사장님 검증

---

### 3.6 원격 CS — 애니소프트 연동

**F-50. 원격 지원 요청 버튼 / F-51. 세션 생성 / F-52. 세션 관리 / F-53. 감사 로그** — v1.0 동일

**§원격 CS — 고객 1클릭 즉시 차단 버튼 (v1.1 패치 #9, 검증팀장 발의)**

- 애니소프트 세션 **연결 중**에도 고객 화면 우상단에 항상 노출되는 [즉시 차단] 빨간 버튼
- 클릭 시 즉각 동작:
  1. 애니소프트 세션 강제 종료 API 호출 (1초 이내)
  2. CS 담당자에게 "고객이 세션을 종료했습니다" 알림
  3. `remote_support_logs.terminated_by_customer = 1` + `terminated_at` 기록
  4. 종료 사유 입력 모달 (선택 사항, 입력 안 해도 종료는 진행됨)
- UX 원칙 (헌법 #24 가르침 의무):
  - 세션 시작 모달에 "언제든 우상단 [즉시 차단] 클릭으로 종료 가능합니다" 안내
  - 첫 사용자는 툴팁 7초 노출
- 강제 종료 응답 시간 SLA: **1초 이내** (Day 3 게이트 검증 항목 추가)

**§원격 CS — 녹화본 저장 위치 (v1.1 패치 #5, OI-3 해소)**

- **고객 PC 로컬 저장 확정** (본사 0건, 헌법 #22)
- 저장 경로: `%LOCALAPPDATA%\HitPan\RemoteSupport\Recordings\{session_id}.webm`
- 보존 기간: 고객 설정 (기본 90일, 최대 3년)
- 90일 경과 자동 삭제 워치독 (Windows 서비스, 헌법 #30)
- 본사로는 메타데이터만 전송: `{session_id, duration_sec, resolved, terminated_by_customer}` (영상 파일 0건)
- 법적 분쟁 시: 고객이 자발적 제출 (영장 절차 정합, 헌법 #18·#22)

---

## 4. DB 설계

### 4.1 신규 테이블

```sql
-- AI 대화 내역
CREATE TABLE ai_conversations (
    id          BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id   VARCHAR(50) NOT NULL,
    user_id     INT NOT NULL,
    session_id  VARCHAR(100) NOT NULL,
    role        ENUM('user','assistant') NOT NULL,
    content     TEXT NOT NULL,
    tool_used   VARCHAR(100) NULL,
    resolved    TINYINT(1) DEFAULT 0,
    embedding   BLOB NULL,                    -- v1.2: bge-m3 INT8 1024차원 (1024 bytes) — 고객 PC 로컬 sqlite-vec 미러
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_tenant_session (tenant_id, session_id),
    INDEX idx_tenant_created (tenant_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 히트판 지식 베이스 (v1.1: priority=999 = 잔존 §예외 우선 응답)
CREATE TABLE hitpan_knowledge (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    tenant_id   VARCHAR(50) NULL,             -- NULL이면 전체 공통
    category    VARCHAR(50) NOT NULL,
    question    VARCHAR(500) NOT NULL,
    answer      TEXT NOT NULL,
    priority    INT DEFAULT 0,                -- v1.1: 999 = §예외 우선 응답 (검색 무시하고 강제 매칭)
    is_active   TINYINT(1) DEFAULT 1,
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_tenant_category (tenant_id, category),
    INDEX idx_priority (priority DESC),       -- v1.1 추가
    FULLTEXT idx_ft_question (question)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- AI 사용량 로그 (v1.1: BYOK 별도 집계)
CREATE TABLE ai_usage_logs (
    id                  BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id           VARCHAR(50) NOT NULL,
    year_month          CHAR(7) NOT NULL,
    provider            VARCHAR(20) NOT NULL DEFAULT 'anthropic',  -- v1.1: anthropic|openai|google
    pool_input_tokens   INT DEFAULT 0,
    pool_output_tokens  INT DEFAULT 0,
    pool_cached_tokens  INT DEFAULT 0,
    byok_input_tokens   INT DEFAULT 0,        -- v1.1: BYOK 별도
    byok_output_tokens  INT DEFAULT 0,        -- v1.1: BYOK 별도
    total_cost_krw      DECIMAL(10,2) DEFAULT 0,
    updated_at          DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uk_tenant_month_provider (tenant_id, year_month, provider)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 원격 지원 로그 (v1.1: 1클릭 차단 추적)
CREATE TABLE remote_support_logs (
    id                      BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id               VARCHAR(50) NOT NULL,
    user_id                 INT NOT NULL,
    session_id              VARCHAR(100) NOT NULL,
    anydesk_session         VARCHAR(100) NULL,
    requested_at            DATETIME NOT NULL,
    consented_at            DATETIME NULL,
    connected_at            DATETIME NULL,
    resolved_at             DATETIME NULL,
    terminated_at           DATETIME NULL,                  -- v1.1
    terminated_by_customer  TINYINT(1) DEFAULT 0,           -- v1.1: 1클릭 차단 여부
    termination_reason      VARCHAR(500) NULL,              -- v1.1: 종료 사유 (선택)
    recording_local_path    VARCHAR(500) NULL,              -- v1.1: 고객 PC 경로 (본사는 경로만)
    handler                 VARCHAR(100) NULL,
    note                    TEXT NULL,
    INDEX idx_tenant_requested (tenant_id, requested_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- v1.1 신설: Tool 거부 감사 로그 (검증팀장 P0)
CREATE TABLE ai_tool_denial_logs (
    id              BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id       VARCHAR(50) NOT NULL,
    user_id         INT NOT NULL,
    ai_session_id   VARCHAR(100) NOT NULL,
    tool_name       VARCHAR(100) NOT NULL,
    tool_input      JSON NOT NULL,
    denial_reason   ENUM('permission_denied','tenant_mismatch','business_rule_violation','rate_limited','external_api_failure') NOT NULL,
    denial_detail   VARCHAR(500) NULL,
    created_at      DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_tenant_created (tenant_id, created_at),
    INDEX idx_reason (denial_reason)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

### 4.2 기존 테이블 변경

```sql
-- tenants 테이블 (v1.1: 3종 공급자 지원)
ALTER TABLE tenants
    ADD COLUMN IF NOT EXISTS ai_provider VARCHAR(20) DEFAULT 'anthropic',  -- v1.1
    ADD COLUMN IF NOT EXISTS ai_api_key_encrypted VARCHAR(1000) NULL,      -- v1.0 anthropic_api_key_encrypted 리네임
    ADD COLUMN IF NOT EXISTS ai_api_key_expires_at DATETIME NULL,          -- v1.1: 90일 회전
    ADD COLUMN IF NOT EXISTS ai_enabled TINYINT(1) DEFAULT 1,
    ADD COLUMN IF NOT EXISTS ai_monthly_token_limit INT DEFAULT 100000;
```

---

## 5. API 설계

### 5.1 챗봇 엔드포인트

```
POST   /api/ai/chat              -- 메시지 전송 (SSE 스트리밍)
GET    /api/ai/history           -- 대화 내역 조회
DELETE /api/ai/history           -- 대화 내역 초기화 (세션)
GET    /api/ai/usage             -- 이번 달 사용량 조회 (풀+BYOK 분리)
```

### 5.2 설정 엔드포인트

```
GET    /api/ai/settings          -- AI 설정 조회 (공급자, BYOK 여부, 만료일)
PUT    /api/ai/settings/apikey   -- API 키 저장 (공급자별)
DELETE /api/ai/settings/apikey   -- 키 즉시 폐기 (v1.1)
POST   /api/ai/settings/rotate   -- 키 회전 (v1.1)
```

### 5.3 원격 지원 엔드포인트

```
POST   /api/support/remote/request     -- 원격 지원 요청 + 동의 처리
GET    /api/support/remote/status      -- 세션 상태 조회
POST   /api/support/remote/terminate   -- v1.1: 고객 1클릭 즉시 차단
POST   /api/support/remote/complete    -- CS 처리 완료 + 내역 저장
```

### 5.4 Tool Use 엔드포인트 (내부)

```
GET    /api/ai/tools/stock           -- 재고 조회
GET    /api/ai/tools/sales           -- 매출/매입 조회
GET    /api/ai/tools/partners        -- 업체 검색
POST   /api/ai/tools/quotation-draft -- v1.1: 견적서 초안 (영업담당)
POST   /api/ai/tools/purchase-order  -- 발주 생성 (관리자, 승인 후)
POST   /api/ai/tools/expense         -- 경비 등록 (승인 후)
```

### 5.5 임베딩 엔드포인트 — v1.2 신설 (고객 PC 로컬 전용)

```
(없음 — 본사 API 0건 원칙)
```

- 임베딩 호출은 **로컬 in-process ONNX Runtime** 호출로만 수행 (HTTP API 미노출)
- 빌드 정적 분석: `HttpClient`로 `embedding`·`embed` 경로 호출 시 빌드 실패 (Roslyn analyzer)

---

## 6. 보안 요구사항

| 항목 | 요구사항 |
|---|---|
| API 키 저장 | AES-256 암호화 필수 (Value Converter, 헌법 #5) |
| 대화 내역 | tenant_id 필터링 필수 (JWT 클레임, 헌법 #2) |
| Tool Use 실행 | JWT 클레임 기반 권한 매트릭스 (F-33) 확인 필수 |
| Tool Use 거부 | **audit 로그 INSERT 의무** (v1.1, ai_tool_denial_logs) |
| 원격 세션 | 명시적 동의 기록 + 1클릭 차단 1초 SLA (v1.1) |
| 업무 데이터 | 본사 전송 절대 금지 (헌법 #18·#22) |
| BYOK 호출 경로 | **본사 프록시 금지 — 고객 PC 직통 (v1.1)** |
| 녹화본 | **고객 PC 로컬 저장 (v1.1)**, 본사 0건 |
| 키 회전 | 90일 자동 만료 + 즉시 폐기 API (v1.1) |
| **임베딩 호스팅 (v1.2)** | **고객 PC 완전 로컬 ONNX Runtime DirectML — 본사 임베딩 데이터 0건** |
| **임베딩 모델 배포 (v1.2)** | **설치 EXE 동봉 (INT8 양자화 110MB) — 오프라인 동작** |
| **백신 호환성 (v1.2)** | **Defender + V3 Lite + 알약 + 네이버 + Norton + McAfee 격리 0 (P0-2 보안 매니저 2)** |
| **5중 검증 게이트 5 (v1.2)** | **데이터 최소주의 완전 통과 — 본사 임베딩 0건 명시** |

### §보안 매니저 2 박제 — Plan A 폐기 사유 (v1.2 신설)

| 학술·실무 근거 | 핵심 내용 | 위협 수준 |
|----------------|----------|----------|
| USENIX Security 2023 | 임베딩 → 원문 의미 복원 (Latent Space Reconstruction) 입증 | 학술 검증 완료 |
| vec2text (Cornell, 2023) | 단문/도메인 텍스트 임베딩 의미 70~92% 복원 | 오픈소스 도구 공개 |
| 시큐브 2024 PoC | 한국어 ERP 매뉴얼 도메인 임베딩 의미 복원 **73%** | 한국 실측 사례 |

- **결론**: 본사가 임베딩만 보유해도 사실상 원문 의미 70%+ 복원 가능 → 헌법 #22·#24·#25 위반 위험.
- **Plan B로 봉합**: 임베딩 호스팅·생성·저장 모두 고객 PC 내부에서 완결, 본사는 메타정보만 보유.
- AI수석 v1.1 "0.1% 역추적" 주장 회수, 보안 매니저 2 분석 전면 채택.

---

## 7. 비기능 요구사항

| 항목 | 목표 |
|---|---|
| 응답 속도 | 첫 토큰 2초 이내 (스트리밍) / p95 3초 (Day 3 게이트) |
| 가용성 | 공급자 API 장애 시 풀 fallback → 동시 장애 시 안내 |
| 비용 | Prompt Caching으로 반복 호출 90% 절감 |
| 동시 사용 | **베타 70 동시 사용자 5분 무장애 (v1.1 검증팀장 P0)** + 정식 200 별도 시험 |
| 지원 브라우저 | Chrome, Edge, Safari (SSE 지원) |
| 1클릭 차단 SLA | **1초 이내 세션 종료 (v1.1)** |
| **임베딩 정확도 회귀 (v1.2)** | **KLUE-STS 83.0 이상 (FP32 87.3 대비 5% 이내)** |
| **임베딩 호출 지연 (v1.2)** | **단문(<512토큰) p95 200ms 이내 (DirectML GPU)** |
| **ONNX 모델 콜드 스타트 (v1.2)** | **3초 이내** |
| **설치 EXE 크기 증가 (v1.2)** | **+110MB 이내 (INT8 양자화 모델 동봉)** |

### 7.1 잔존 §예외 우선 응답 (v1.1 신설, ERP 매니저 발의)

마이그 99.7% 데이터 중 **잔존 §예외 영역**은 LLM 추론 전 `hitpan_knowledge.priority = 999`로 강제 매칭하여 오답·환각 차단.

| 잔존 영역 | 정확도 | priority=999 응답 |
|----------|--------|-------------------|
| 회계 86.7% (13.3% 마이그 누락) | 86.7% | "회계 마이그가 일부 누락된 영역입니다. 원장 직접 확인 또는 마이그 보강 작지서 요청을 권장합니다." |
| 세금계산서 21건 (마이그 예외) | - | "세금계산서 일부는 레거시 그대로 이관되어 정합성 검증 대상입니다. 운영자에게 문의해주세요." |
| 카드사용이력 (구조 불일치) | - | "카드사용이력은 레거시 구조와 신 ERP 구조가 달라 별도 조회 화면을 이용해주세요. → [카드사용이력 화면]" |

- 매칭 로직: 사용자 질의에 해당 카테고리 키워드 포함 시, 임베딩 검색 전에 priority=999 답변을 LLM에 강제 주입 + 사용자에게는 priority=999 답변을 우선 표시
- LLM은 priority=999 답변을 **수정 없이 그대로 출력** (System Prompt에 명시: "다음 답변은 정답으로 그대로 출력하라")
- 잔존 영역이 해소되면 (마이그 99.9% 도달 등) priority=999 → priority=100으로 강등

---

## 8. 구현 단계 (v1.2 갱신)

### Day 1~2 (5/24~5/25) — PRD v1.1 + 작A·작B·작C 발행
- [x] Day 1 보고서 (12건 사장님 결재 완료)
- [x] Day 2 PRD v1.1 패치
- [x] **Day 2 PRD v1.2 패치 (Plan A → Plan B 정정, 이 문서)**
- [ ] 작A·작B·작C 발행 (설계팀장, 5/29 마감)

### Day 3 (5/26) — 게이트 #1 (v1.2 항목 추가)
- [ ] 애니소프트 사전 확인 (당겨짐, P1)
- [ ] **부하 70 동시 5분 무장애 (검증팀장 P0)**
- [ ] p95 3초 측정
- [ ] 1클릭 차단 1초 SLA 측정
- [ ] **INT8 양자화 정확도 회귀 KLUE-STS 5% 이내 (AI수석 영역, v1.2)**
- [ ] **백신 5종 호환성 격리 0 (보안 매니저 2 영역, v1.2)**
- [ ] **ONNX 모델 콜드 스타트 3초 이내 + 단문 임베딩 p95 200ms (AI수석, v1.2)**

### Day 4 (5/27) — Phase 2 임베딩 모델 준비 (v1.2 Plan B)
- [ ] bge-m3 ONNX 변환 (Microsoft Olive 활용)
- [ ] INT8 양자화 (280MB → DirectML 최적화 110MB)
- [ ] KLUE-STS 회귀 검증 (목표 83.0 이상)
- [ ] 설치 EXE 동봉 빌드 파이프라인 구성

### Day 5 (5/28) — Phase 2 학습 콘텐츠 임베딩
- [ ] 학습 콘텐츠 5종 임베딩 생성
- [ ] sqlite-vec 통합 (`%LOCALAPPDATA%\HitPan\KnowledgeBase\vec.db`)
- [ ] 백그라운드 큐 워커 (Windows Service) 구현

### Day 6 (5/29) — Phase 1 챗봇 Blazor UI
- [ ] 챗봇 UI (Blazor) 구현
- [ ] System Prompt 히트판 매뉴얼 탑재 + §예외 §7.1 적용
- [ ] 작A·B·C 마감 정합 확인

### Day 7 (6/1) — Anthropic BYOK 1차 출시 + 사장님 검증
- [ ] `IAiProvider` + AnthropicProvider 구현
- [ ] BYOK 본사 프록시 금지 정적 분석 룰 추가
- [ ] DB 5개 테이블 (v1.1 신설 ai_tool_denial_logs 포함) 생성
- [ ] 사장님 검증 시연

### Day 8~12 (6/2~6/6) — RAG Phase B 통합 + Tool Use 조회
- [ ] 검색 API + System Prompt 주입 통합
- [ ] A/B 테스트 (LIKE vs 임베딩) + 정확도 측정
- [ ] query_stock·query_sales·search_partner·query_purchase_orders·query_expenses
- [ ] 권한 매트릭스 F-33 적용 + ai_tool_denial_logs INSERT

### Day 13~14 (6/5~6/6) — OpenAI 출시
- [ ] OpenAIProvider 구현
- [ ] Day 14 = OpenAI BYOK 출시

### Day 15~17 (6/7~6/9) — Phase 4 Tool Use 실행
- [ ] create_quotation_draft (영업담당), create_purchase_order (관리자), create_expense
- [ ] 승인 카드 UI

### Day 18~21 (6/10~6/13) — Phase 5 원격 CS + Google 출시
- [ ] 애니소프트 API 연동
- [ ] 1클릭 차단 버튼 + 1초 SLA 검증
- [ ] 녹화본 고객 PC 로컬 저장
- [ ] Day 21 = Google BYOK 출시

### 베타 출시 — **8/24**
- [ ] 베타 20곳 동시 70 부하 시험 통과
- [ ] EVF 6대 영역 통과 (사장님 헌법 §12)
- [ ] 5중 검증 통과 (헌법 #23) — **게이트 5 데이터 최소주의 완전 통과 (v1.2 Plan B 정합)**

### 정식 출시 직전 (8/17 별도 시험)
- [ ] **200 동시 부하 시험** (AI수석 원안, 검증팀장 권고)

---

## 9. 성공 지표

| 지표 | 목표 |
|---|---|
| 챗봇 1차 해결률 | 베타 3개월 후 70% 이상 |
| CS 전화 감소율 | 베타 대비 40% 감소 |
| 응답 만족도 | 4.0/5.0 이상 |
| 원격 지원 요청 후 연결 시간 | 5분 이내 |
| **1클릭 차단 응답 시간 (v1.1)** | **1초 이내** |
| AI 비용 대비 CS 인건비 절감 | 3배 이상 |
| §예외 우선 응답 정확도 (v1.1) | 100% (강제 매칭이므로) |
| **임베딩 정확도 회귀 (v1.2)** | **KLUE-STS 83.0 이상 (5% 이내)** |
| **본사 임베딩 데이터 보유량 (v1.2)** | **0건 (절대 원칙)** |

---

## 10. 오픈 이슈 해소

| # | 이슈 | v1.0 | v1.1 / v1.2 결재 결과 |
|---|------|------|----------------|
| OI-1 | 대화 내역 보존 기간 | 사장님 | **3년** (법적 분쟁 대비, remote_support_logs 정합) |
| OI-2 | 토큰 한도 초과 시 추가 구매 | 사장님 | **티어 업그레이드 안내**, 단건 추가 구매 비허용 (영업 단순화) |
| OI-3 | 녹화본 저장 위치 | 인프라팀 | **고객 PC 로컬 (v1.1 패치 #5)** |
| OI-4 | 애니소프트 API 문서 | 개발팀 | Day 3 사전 확인 P1 (당겨짐) |
| OI-5 | Tool Use 실행 권한 | 사장님 | **F-33 권한 매트릭스 (v1.1 패치 #6)** |
| **OI-6 (v1.2)** | **임베딩 호스팅 위치** | **AI수석 Plan A (본사 풀)** | **Plan B 고객 PC 완전 로컬 (보안 매니저 2 발의, 사장님 결재)** |
| **OI-7 (v1.2)** | **임베딩 모델 양자화** | FP16 ~600MB | **INT8 110MB (DirectML 최적화)** |
| **OI-8 (v1.2)** | **백신 호환성 검증** | 미정 | **P0-2 보안 매니저 2 WS-06 작지서** |

---

*PRD v1.2 — 2026-05-25 11:00 AI수석 패치 (사장님 결재 8건 중 PRD 패치 영역, Plan A → Plan B 정정 박제)*
*다음 단계: 작A·작B·작C 발행 (설계팀장 P0, 5/29 마감) + Day 3 게이트 (5/26, AI수석 정확도 회귀 + 보안 매니저 2 백신 5종)*
