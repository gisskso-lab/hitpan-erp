# 히트판 AI CS 시스템 PRD
> 문서번호: 20260508PRD-AI-CS  
> 작성: AI수석  
> 상태: 초안 (사장님 검토 대기)

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
CS 처리 메타 통계만 백오피스로 Push (업무 데이터 전송 금지, 헌법 #18).

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
안녕하세요! 히트판 AI입니다 👋
무엇이든 물어보세요.
• 화면 사용법
• 데이터 조회
• 업무 처리 도움
```

**F-04. 빠른 답변 버튼 (Quick Reply)**
- 초기: [수불부 조회] [발주 방법] [매출 확인] [원격 지원]
- 대화 흐름에 따라 동적 변경

---

### 3.2 AI 엔진 (Claude API 연동)

**F-10. System Prompt — 히트판 매뉴얼 사전 탑재**
- 히트판 전체 화면·기능·워크플로우 지식 포함
- 6단계 업무 흐름 (설정→마스터→매입→판매→현황→재무)
- 각 화면별 사용법, 주의사항, 자주 묻는 질문
- Prompt Caching 적용 (`cache_control: ephemeral`) → 반복 호출 시 비용 90% 절감
- 매뉴얼 업데이트: 서버 재시작 없이 즉시 반영

**F-11. BYOK (Bring Your Own Key)**
- 고객사가 Anthropic API 키 보유 시 직접 연동 가능
- 설정 → 회사정보 → AI 설정 탭에서 키 입력
- 저장: AES-256 암호화 (기존 Value Converter 패턴 동일)
- DB 컬럼: `tenants.anthropic_api_key_encrypted` (DB-27 기존 설계)
- 키 없을 시: 히트판 기본 키 사용 (사용량 월 한도 적용)

**F-12. AI 사용량 제한 (BYOK 없는 경우)**
- Basic 티어: 월 100K 토큰
- Pro 티어: 월 500K 토큰
- Enterprise 티어: 월 3M 토큰
- 한도 초과 시: "이번 달 AI 사용량이 초과되었습니다. BYOK 설정 또는 플랜 업그레이드를 권장합니다."

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
  "parameters": {
    "keyword": "업체명 (부분 일치)"
  }
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

> **원칙:** 모든 데이터 변경은 사용자 승인 후 실행. Claude는 초안만 생성.

**F-30. 발주 생성**
```
사용자: "영신에 OSB 1000장 발주해줘"
→ Claude: 발주 초안 카드 생성
   [업체: (주)영신 | 상품: OSB | 수량: 1,000장 | 단가: 자동조회]
   [확인 후 등록] [취소]
→ 사용자 승인 클릭 → POST /api/purchase-orders
```

**F-31. 경비 처리 등록**
```
사용자: "오늘 점심 식대 35,000원 경비 처리해줘"
→ Claude: 경비 초안 카드 생성
   [항목: 식대 | 금액: 35,000원 | 날짜: 오늘]
   [확인 후 등록] [취소]
→ 사용자 승인 클릭 → POST /api/expenses
```

**F-32. 승인 플로우 규칙**
- 읽기(조회): 즉시 실행, 승인 불필요
- 쓰기(등록·수정): 반드시 카드 미리보기 → 사용자 클릭 후 실행
- 삭제: 지원하지 않음 (챗봇에서 삭제 불가)
- 금액 관련: 추가 확인 메시지 필수

---

### 3.5 RAG — CS 내역 누적 학습

**F-40. 대화 저장**
- 모든 챗봇 대화를 `ai_conversations` 테이블에 저장
- 저장 항목: tenant_id, user_id, 질문, 응답, 타임스탬프, 해결여부
- 개인정보 포함 시 마스킹 처리

**F-41. 유사 케이스 주입 (RAG Phase A)**
- 새 질문 입력 시 과거 유사 대화 top-5 검색
- 검색 기준: 키워드 매칭 (LIKE 검색, 1차)
- 검색 결과를 System Prompt 말미에 추가 주입
- 효과: CS 누적 건수 증가 → 응답 정확도 향상

**F-42. 히트판 지식 베이스 (hitpan_knowledge)**
- 자주 묻는 질문·답변 관리자 직접 등록 가능
- 테이블: `hitpan_knowledge` (질문, 답변, 카테고리, 우선순위)
- 챗봇 응답 시 지식 베이스 우선 참조

**F-43. RAG Phase B (향후)**
- 조건: CS 누적 500건 이상
- Vector DB 도입 → 의미 기반 유사도 검색
- 일정: MVP 이후 별도 작지서

---

### 3.6 원격 CS — 애니소프트 연동

**F-50. 원격 지원 요청 버튼**
- 챗봇 하단 "원격 지원 요청" 버튼 (항상 노출)
- 클릭 시 동의 모달:
  ```
  원격 지원을 요청하시겠습니까?
  
  • 접속 범위: 히트판 화면 조작
  • 접속 시간: 최대 30분
  • 담당자: 히트판 CS팀
  • 세션 녹화: 동의 시 저장됨
  
  [동의하고 요청] [취소]
  ```

**F-51. 애니소프트 세션 생성**
- 레거시 히트판과 동일 구독 계정 사용
- 고객 동의 후 → 애니소프트 API로 세션 ID 자동 생성
- 생성된 세션 ID를 CS 담당자에게 Slack/알림 자동 전송
- 고객 화면에 세션 ID + 대기 안내 표시

**F-52. 세션 관리**
- 최대 30분 자동 만료
- CS 담당자 접속 시 고객 화면에 "CS 담당자가 연결되었습니다" 알림
- 세션 종료 시 처리 내역 입력 → `ai_conversations` 저장 → RAG 누적

**F-53. 감사 로그**
- 원격 세션 전 과정 기록: 요청시각, 동의시각, 담당자, 처리내역, 종료시각
- 테이블: `remote_support_logs`
- 보존 기간: 3년 (법적 분쟁 대비)

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
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_tenant_session (tenant_id, session_id),
    INDEX idx_tenant_created (tenant_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 히트판 지식 베이스
CREATE TABLE hitpan_knowledge (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    tenant_id   VARCHAR(50) NULL,          -- NULL이면 전체 공통
    category    VARCHAR(50) NOT NULL,
    question    VARCHAR(500) NOT NULL,
    answer      TEXT NOT NULL,
    priority    INT DEFAULT 0,
    is_active   TINYINT(1) DEFAULT 1,
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_tenant_category (tenant_id, category),
    FULLTEXT idx_ft_question (question)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- AI 사용량 로그
CREATE TABLE ai_usage_logs (
    id              BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id       VARCHAR(50) NOT NULL,
    year_month      CHAR(7) NOT NULL,      -- '2026-05'
    input_tokens    INT DEFAULT 0,
    output_tokens   INT DEFAULT 0,
    cached_tokens   INT DEFAULT 0,
    total_cost_krw  DECIMAL(10,2) DEFAULT 0,
    updated_at      DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uk_tenant_month (tenant_id, year_month)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 원격 지원 로그
CREATE TABLE remote_support_logs (
    id              BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id       VARCHAR(50) NOT NULL,
    user_id         INT NOT NULL,
    session_id      VARCHAR(100) NOT NULL,
    anydesk_session VARCHAR(100) NULL,
    requested_at    DATETIME NOT NULL,
    consented_at    DATETIME NULL,
    connected_at    DATETIME NULL,
    resolved_at     DATETIME NULL,
    handler         VARCHAR(100) NULL,
    note            TEXT NULL,
    INDEX idx_tenant_requested (tenant_id, requested_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

### 4.2 기존 테이블 변경

```sql
-- tenants 테이블 (DB-27 기존 컬럼 확인 후 없으면 추가)
ALTER TABLE tenants
    ADD COLUMN IF NOT EXISTS anthropic_api_key_encrypted VARCHAR(500) NULL,
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
GET    /api/ai/usage             -- 이번 달 사용량 조회
```

### 5.2 설정 엔드포인트

```
GET    /api/ai/settings          -- AI 설정 조회 (BYOK 여부 등)
PUT    /api/ai/settings/apikey   -- Claude API 키 저장
DELETE /api/ai/settings/apikey   -- Claude API 키 삭제
```

### 5.3 원격 지원 엔드포인트

```
POST   /api/support/remote/request   -- 원격 지원 요청 + 동의 처리
GET    /api/support/remote/status    -- 세션 상태 조회
POST   /api/support/remote/complete  -- CS 처리 완료 + 내역 저장
```

### 5.4 Tool Use 엔드포인트 (내부)

```
GET    /api/ai/tools/stock           -- 재고 조회
GET    /api/ai/tools/sales           -- 매출/매입 조회
GET    /api/ai/tools/partners        -- 업체 검색
POST   /api/ai/tools/purchase-order  -- 발주 생성 (승인 후)
POST   /api/ai/tools/expense         -- 경비 등록 (승인 후)
```

---

## 6. 보안 요구사항

| 항목 | 요구사항 |
|---|---|
| API 키 저장 | AES-256 암호화 필수 (Value Converter) |
| 대화 내역 | tenant_id 필터링 필수 (타 테넌트 접근 불가) |
| Tool Use 실행 | JWT 클레임 기반 권한 확인 필수 |
| 원격 세션 | 명시적 동의 기록 DB 저장 필수 |
| 업무 데이터 | 챗봇 대화 내역은 ERP DB에만 저장 (본사 전송 금지, 헌법 #18) |
| 감사 로그 | 모든 Tool Use 실행 로그 기록 |

---

## 7. 비기능 요구사항

| 항목 | 목표 |
|---|---|
| 응답 속도 | 첫 토큰 2초 이내 (스트리밍) |
| 가용성 | Claude API 장애 시 "현재 AI 서비스 점검 중" 안내 |
| 비용 | Prompt Caching으로 반복 호출 90% 절감 |
| 동시 사용 | 테넌트당 동시 5세션 |
| 지원 브라우저 | Chrome, Edge, Safari (SSE 지원 브라우저) |

---

## 8. 구현 단계

### Phase 1 — 챗봇 기본 (3일)
- [ ] DB 테이블 4개 생성
- [ ] Claude API 연동 (SSE 스트리밍)
- [ ] System Prompt 히트판 매뉴얼 탑재
- [ ] 챗봇 UI 컴포넌트 (Blazor)
- [ ] 대화 저장 (ai_conversations)

### Phase 2 — BYOK + 사용량 (2일)
- [ ] BYOK 설정 UI (설정 → 회사정보)
- [ ] API 키 암호화 저장/복호화
- [ ] 사용량 추적 (ai_usage_logs)
- [ ] 한도 초과 안내

### Phase 3 — Tool Use 조회 (2일)
- [ ] query_stock, query_sales, search_partner 구현
- [ ] 결과 카드 UI (테이블형)
- [ ] RAG Phase A (유사 케이스 주입)

### Phase 4 — Tool Use 실행 (2일)
- [ ] create_purchase_order, create_expense 구현
- [ ] 승인 카드 UI (미리보기 + 확인 버튼)
- [ ] 실행 감사 로그

### Phase 5 — 원격 CS (2일)
- [ ] 애니소프트 API 연동 (세션 생성)
- [ ] 동의 모달 UI
- [ ] 원격 지원 로그 저장
- [ ] CS 담당자 알림 (Slack or 이메일)

**총 구현 기간: 11일**

---

## 9. 성공 지표

| 지표 | 목표 |
|---|---|
| 챗봇 1차 해결률 | 베타 3개월 후 70% 이상 |
| CS 전화 감소율 | 베타 대비 40% 감소 |
| 응답 만족도 | 4.0/5.0 이상 |
| 원격 지원 요청 후 연결 시간 | 5분 이내 |
| AI 비용 대비 CS 인건비 절감 | 3배 이상 |

---

## 10. 오픈 이슈

| # | 이슈 | 결정 필요자 |
|---|---|---|
| OI-1 | 챗봇 대화 내역 보존 기간 (1년? 3년?) | 사장님 |
| OI-2 | BYOK 없는 고객사 토큰 한도 초과 시 유료 추가 구매 허용 여부 | 사장님 |
| OI-3 | 원격 CS 세션 녹화본 저장 위치 (로컬? S3?) | 인프라팀 |
| OI-4 | 애니소프트 API 문서 확보 여부 | 개발팀 확인 필요 |
| OI-5 | Tool Use 실행 권한 (대표만? 관리자도?) | 사장님 |

---

*PRD v1.0 — 2026-05-08 AI수석 작성*  
*다음 단계: 사장님 검토 → 승인 → 작업지시서 발행*
