namespace HitPan.Application.DTOs.Chatbot;

// ─────────────────────────────────────────────────────────────────
// 히트판 AI 챗봇 DTOs — Phase A (FAQ 매칭 + KB + 대화 이력 축적)
// ─────────────────────────────────────────────────────────────────

/// <summary>챗봇 질문 요청</summary>
public class ChatAskRequest
{
    public string Message { get; set; } = "";

    /// <summary>
    /// 직전 대화 맥락 (단기 기억). 사장님 결재(2026-06-20): "AI가 대화를 기억해 이전 분석을 다시 꺼낼 수 있게".
    /// 최근 N턴만 전송(토큰 절약 — 전부 싣으면 느리고 비쌈). 오래된 순서로 정렬.
    /// </summary>
    public List<ChatTurn> History { get; set; } = new();
}

/// <summary>대화 1턴 (사용자/도우미 발화).</summary>
public class ChatTurn
{
    /// <summary>"user" 또는 "assistant"</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}

/// <summary>챗봇 답변 응답</summary>
public class ChatAnswerDto
{
    public string ConvId { get; set; } = "";
    public string Answer { get; set; } = "";
    public List<RelatedArticleDto> RelatedArticles { get; set; } = new();
    public decimal ConfidenceScore { get; set; }
    public int TokensUsed { get; set; }
    public int TokensRemaining { get; set; }
    /// <summary>답변 애매할 때 추가 문의 유도</summary>
    public bool NeedsFollowUp { get; set; }

    /// <summary>
    /// AI 직원이 실제 데이터를 조회·분석한 결과 (있으면 프론트가 표+차트로 렌더).
    /// 단순 도우미 답변일 땐 null. 사장님 결재(2026-06-20): "분석 결과·시각화까지 싹".
    /// </summary>
    public AiAnalysisResultDto? Analysis { get; set; }

    /// <summary>
    /// 생성(쓰기) 명령으로 AI 직원이 초안을 만들었을 때 사람 승인이 필요한 액션.
    /// 채워지면 프론트가 승인/반려 카드를 띄운다. 승인 시 ConfirmUrl 호출 → 확정(+연쇄).
    /// 사장님 비전(2026-06-20): "AI는 준비, 실행은 사람" — 헌법 #6.
    /// </summary>
    public PendingActionDto? PendingAction { get; set; }
}

/// <summary>
/// 사람 승인 대기 액션 (AI 직원이 만든 초안). 프론트가 승인/반려 카드로 렌더.
/// </summary>
public class PendingActionDto
{
    /// <summary>액션 종류 (예: sales-delivery-draft, leave-request-draft) — 프론트 분기·아이콘.</summary>
    public string Kind { get; set; } = "";

    /// <summary>카드 제목 (예: "거래명세서 초안 — (주)대한상사").</summary>
    public string Title { get; set; } = "";

    /// <summary>초안 요약 (사람이 보고 판단할 핵심 — 거래처·품목·금액 등). 줄바꿈 보존.</summary>
    public string Summary { get; set; } = "";

    /// <summary>이미 생성된 초안 리소스 ID (있으면). 승인 = 이 초안을 confirm.</summary>
    public string? DraftId { get; set; }

    /// <summary>승인 시 호출할 API (METHOD + URL). 예: POST /api/sales/deliveries/{id}/confirm.</summary>
    public string? ApproveMethod { get; set; }
    public string? ApproveUrl { get; set; }

    /// <summary>
    /// 승인 후 자동 연쇄 작업 설명(있으면 카드에 "승인하면 수주서도 자동 생성됩니다" 안내).
    /// 워크플로우 끊김 0(헌법 #20). 실제 연쇄 실행은 승인 처리 단계에서.
    /// </summary>
    public string? ChainNote { get; set; }
}

/// <summary>
/// AI 직원 Tool Use 분석 결과 — 표 + 차트 렌더용 구조화 데이터.
/// 읽기 전용 조회(수익성·재고·판매현황 등)의 산출물. 데이터 변경 0(헌법 #6).
/// </summary>
public class AiAnalysisResultDto
{
    /// <summary>분석 종류 (예: sales-profitability) — 프론트 분기용</summary>
    public string Kind { get; set; } = "";

    /// <summary>표 제목 (예: "품목별 수익성 분석 (2026-01 ~ 2026-06)")</summary>
    public string Title { get; set; } = "";

    /// <summary>표 헤더 (예: 품목, 매출, 원가, 이익, 이익률, 건수)</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>표 행 — 각 행은 Columns 순서의 문자열 셀</summary>
    public List<List<string>> Rows { get; set; } = new();

    /// <summary>막대차트용 시리즈 (라벨 + 값). 보통 상위 N개 이익/매출.</summary>
    public List<ChartPointDto> Chart { get; set; } = new();

    /// <summary>차트 축 의미 (예: "이익(원)")</summary>
    public string ChartValueLabel { get; set; } = "";
}

/// <summary>차트 1포인트 (라벨 + 수치)</summary>
public class ChartPointDto
{
    public string Label { get; set; } = "";
    public double Value { get; set; }
}

/// <summary>답변에 연결된 관련 KB 아티클 요약</summary>
public class RelatedArticleDto
{
    public string ArticleId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string? RelatedMenuUrl { get; set; }
}

/// <summary>답변 피드백 (도움됨/도움안됨)</summary>
public class ChatFeedbackRequest
{
    public string ConvId { get; set; } = "";
    public bool WasHelpful { get; set; }
}

/// <summary>AI 직원 초안 승인 요청 (Lv.3 워크플로우 연쇄).</summary>
public class ApproveActionRequest
{
    /// <summary>액션 종류 (예: sales-delivery-draft) — 어떤 확정·연쇄를 할지.</summary>
    public string Kind { get; set; } = "";

    /// <summary>초안 리소스 ID (거래명세서 ID 등).</summary>
    public string DraftId { get; set; } = "";

    /// <summary>워크플로우 연쇄 실행 여부 (true=거래명세서 확정 + 수주 자동생성).</summary>
    public bool Chain { get; set; } = true;
}

/// <summary>승인 처리 결과.</summary>
public class ApproveActionResultDto
{
    public bool Succeeded { get; set; }

    /// <summary>사람이 읽을 처리 결과 메시지 (예: "거래명세서 확정 + 수주 SO-00012 자동생성 완료").</summary>
    public string Message { get; set; } = "";

    /// <summary>연쇄로 생성된 리소스 (있으면) — 예: 수주 ID/번호.</summary>
    public string? ChainedId { get; set; }
    public string? ChainedNo { get; set; }
}

/// <summary>테넌트 월간 토큰 할당량</summary>
public class TokenQuotaDto
{
    public string AiMode { get; set; } = "hitpan_pool";
    public int MonthlyLimit { get; set; }
    public int ExtraTokens { get; set; }
    public int UsedTokens { get; set; }
    public int Remaining { get; set; }
    public string SubscriptionTier { get; set; } = "";
    public bool AnthropicKeyConfigured { get; set; }
    public string? AnthropicKeyLast4 { get; set; }
}

// ─────────────────────────────────────────────────────────────────
// BYOK (Bring Your Own Key) — AI 도우미 연동 키 설정 + 사용량
// ─────────────────────────────────────────────────────────────────

/// <summary>AI 도우미 연동 키 저장 요청 (PUT /api/ai-settings/apikey)</summary>
public class SaveApiKeyRequest
{
    /// <summary>고객사 보유 키 평문 — 서버에서 AES-256 암호화 후 last4만 남기고 폐기</summary>
    public string ApiKey { get; set; } = "";
}

/// <summary>AI 도우미 설정 현황 (GET /api/ai-settings)</summary>
public class AiSettingsDto
{
    /// <summary>키 설정 여부 (status='valid' 이고 last4 존재)</summary>
    public bool KeyConfigured { get; set; }

    /// <summary>마스킹된 키 끝 4자리 (평문 절대 반환 금지)</summary>
    public string? KeyLast4 { get; set; }

    /// <summary>키 상태 — none / valid</summary>
    public string KeyStatus { get; set; } = "none";

    /// <summary>키 저장 시각</summary>
    public DateTime? KeySavedAt { get; set; }

    public string AiMode { get; set; } = "hitpan_pool";
    public int MonthlyLimit { get; set; }
    public int ExtraTokens { get; set; }
    public string SubscriptionTier { get; set; } = "";
}

/// <summary>이번 달 토큰 사용량 집계 (GET /api/chatbot/usage)</summary>
public class AiUsageDto
{
    /// <summary>집계 대상 연-월 (yyyy-MM)</summary>
    public string Ym { get; set; } = "";

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }

    /// <summary>월 한도 (기본 한도 + 추가 토큰)</summary>
    public int MonthlyLimit { get; set; }

    /// <summary>잔여 = 한도 - 사용</summary>
    public int Remaining { get; set; }
}

/// <summary>KB 아티클 DTO (검색/인기 조회용)</summary>
public class KbArticleDto
{
    public string ArticleId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string ContentMarkdown { get; set; } = "";
    public string? RelatedMenuUrl { get; set; }
    public int HitCount { get; set; }
}
