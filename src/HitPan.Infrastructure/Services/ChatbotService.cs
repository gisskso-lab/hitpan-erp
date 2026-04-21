using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Chatbot;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 히트판 AI 챗봇 서비스 (Phase A).
/// - FAQ/KB 매칭 기반 답변 (매칭 실패 시 에스컬레이션 안내)
/// - 대화 이력을 ai_conversations 에 저장 → 학습 자산 축적
/// - 예상 토큰 수를 ai_usage_logs 에 기록 (Phase A 는 실제 차감 없음)
/// </summary>
public sealed class ChatbotService : IChatbotService
{
    private readonly IDbConnection _db;
    private readonly IAuditService _audit;

    public ChatbotService(IDbConnection db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ─────────────────────────────────────────────────────────────
    // 질의 → 답변
    // ─────────────────────────────────────────────────────────────
    public async Task<ChatAnswerDto> AskAsync(
        ChatAskRequest req,
        string tenantId,
        string userId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(req.Message))
        {
            throw new ArgumentException("질문 내용이 비어있습니다.", nameof(req));
        }

        // 1) 월간 토큰 할당량 조회 (Phase A: 표시용, 차감 로직은 Phase B)
        var quota = await GetQuotaAsync(tenantId, ct).ConfigureAwait(false);

        // 2) 간단 키워드 분할 (한국어 FULLTEXT 는 토큰 품질이 낮아 LIKE 조합으로 대체)
        var keywords = req.Message
            .Split(new[] { ' ', ',', '?', '!', '.', '？', '！', '。', '、' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(k => k.Length >= 2)
            .Take(5)
            .ToList();

        // 3) KB 매칭
        var matchedArticles = await FindMatchingArticlesAsync(keywords, ct).ConfigureAwait(false);

        // 4) 답변 구성 (Phase A: KB 본문 그대로 합성)
        string answer;
        decimal confidence;

        if (matchedArticles.Count > 0)
        {
            var top = matchedArticles[0];
            var sb = new System.Text.StringBuilder();
            sb.Append(top.ContentMarkdown);
            sb.Append("\n\n");

            if (matchedArticles.Count > 1)
            {
                sb.AppendLine("**관련 도움말도 참고하세요:**");
                foreach (var art in matchedArticles.Skip(1).Take(3))
                {
                    sb.AppendLine($"- {art.Title}");
                }
            }
            answer = sb.ToString();

            // 매칭 아티클 수에 따라 신뢰도 0.6 ~ 1.0 보정
            confidence = Math.Min(1.0m, 0.6m + matchedArticles.Count * 0.1m);

            // 히트카운트 증가 (Top 아티클)
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE hitpan_knowledge SET hit_count = hit_count + 1 WHERE article_id = @Id",
                new { Id = top.ArticleId },
                cancellationToken: ct)).ConfigureAwait(false);
        }
        else
        {
            answer =
                "아직 관련 도움말이 준비되지 않았어요. 이 질문은 우리 팀이 확인 후 답변을 추가하겠습니다.\n\n" +
                "💡 지금 바로 도움이 필요하면:\n" +
                "- 사이드바에서 관련 메뉴를 찾아보기\n" +
                "- 일반 사용문의는 담당자에게 연락";
            confidence = 0.2m;
        }

        // 5) 대화 이력 저장 (학습 자산)
        var convId = Guid.NewGuid().ToString();
        var articleIds = matchedArticles.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(matchedArticles.Select(a => a.ArticleId).ToArray())
            : null;

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ai_conversations (
              conv_id, tenant_id, user_id, intent, user_message, ai_response,
              matched_article_ids, confidence_score, created_at)
            VALUES (
              @ConvId, @TenantId, @UserId, 'usage_question', @Msg, @Answer,
              @ArticleIds, @Score, NOW(6))
            """,
            new
            {
                ConvId = convId,
                TenantId = tenantId,
                UserId = userId,
                Msg = req.Message,
                Answer = answer,
                ArticleIds = articleIds,
                Score = confidence
            },
            cancellationToken: ct)).ConfigureAwait(false);

        // 6) 토큰 사용량 기록 (Phase A fake — 대화 1건 ≈ 50 + 메시지길이/2)
        var tokensUsed = 50 + req.Message.Length / 2;
        var ym = DateTime.UtcNow.ToString("yyyy-MM");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ai_usage_logs (
              tenant_id, conv_id, ai_provider, input_tokens, output_tokens,
              total_tokens, charge_mode, usage_type, ym)
            VALUES (
              @TenantId, @ConvId, 'none', @In, @Out, @Total, 'hitpan_pool', 'chat', @Ym)
            """,
            new
            {
                TenantId = tenantId,
                ConvId = convId,
                In = req.Message.Length / 4,
                Out = answer.Length / 4,
                Total = tokensUsed,
                Ym = ym
            },
            cancellationToken: ct)).ConfigureAwait(false);

        // 7) 감사 로그 (도메인 이벤트 — 챗봇 질의)
        await _audit.LogAsync(
            actionType: "ask",
            entityType: "chatbot",
            entityId: convId,
            afterJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                confidence,
                tokensUsed,
                matchedCount = matchedArticles.Count
            }),
            ct: ct).ConfigureAwait(false);

        return new ChatAnswerDto
        {
            ConvId = convId,
            Answer = answer,
            RelatedArticles = matchedArticles.Select(a => new RelatedArticleDto
            {
                ArticleId = a.ArticleId,
                Title = a.Title,
                Category = a.Category,
                RelatedMenuUrl = a.RelatedMenuUrl
            }).ToList(),
            ConfidenceScore = confidence,
            TokensUsed = tokensUsed,
            TokensRemaining = Math.Max(0, quota.Remaining - tokensUsed),
            NeedsFollowUp = confidence < 0.5m
        };
    }

    // ─────────────────────────────────────────────────────────────
    // 피드백 기록
    // ─────────────────────────────────────────────────────────────
    public async Task RecordFeedbackAsync(
        ChatFeedbackRequest req,
        string tenantId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(req.ConvId))
        {
            throw new ArgumentException("conv_id 가 필요합니다.", nameof(req));
        }

        // 테넌트 격리 확인 + 피드백 업데이트
        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ai_conversations
            SET was_helpful = @Helpful
            WHERE conv_id = @ConvId AND tenant_id = @TenantId
            """,
            new { ConvId = req.ConvId, TenantId = tenantId, Helpful = req.WasHelpful },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
        {
            return; // 다른 테넌트 또는 존재하지 않는 대화
        }

        // 👍 긍정 피드백 시, 관련 아티클 rating 소폭 증가 (가중 평균 근사)
        if (req.WasHelpful)
        {
            var articleJson = await _db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
                "SELECT matched_article_ids FROM ai_conversations WHERE conv_id = @ConvId",
                new { ConvId = req.ConvId },
                cancellationToken: ct)).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(articleJson))
            {
                try
                {
                    var ids = System.Text.Json.JsonSerializer.Deserialize<string[]>(articleJson);
                    if (ids is not null && ids.Length > 0)
                    {
                        // 평점 소수점 3자리까지 — 0.05 씩 증가, 최대 5.00
                        await _db.ExecuteAsync(new CommandDefinition(
                            """
                            UPDATE hitpan_knowledge
                            SET usage_rating = LEAST(5.00, usage_rating + 0.05)
                            WHERE article_id IN @Ids
                            """,
                            new { Ids = ids },
                            cancellationToken: ct)).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // JSON 파싱 실패는 무시 — 피드백 기록은 이미 완료됨
                }
            }
        }

        // 감사 로그
        await _audit.LogAsync(
            actionType: "feedback",
            entityType: "chatbot",
            entityId: req.ConvId,
            afterJson: System.Text.Json.JsonSerializer.Serialize(new { req.WasHelpful }),
            ct: ct).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────
    // 토큰 할당량 조회
    // ─────────────────────────────────────────────────────────────
    public async Task<TokenQuotaDto> GetQuotaAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var tenant = await _db.QueryFirstOrDefaultAsync<TenantAiRow?>(new CommandDefinition(
            """
            SELECT
              ai_mode                     AS AiMode,
              ai_token_monthly_limit      AS MonthlyLimit,
              ai_token_extra              AS ExtraTokens,
              subscription_tier           AS SubscriptionTier,
              anthropic_api_key_last4     AS AnthropicKeyLast4,
              anthropic_key_status        AS AnthropicKeyStatus
            FROM tenants
            WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (tenant is null)
        {
            // 안전 기본값
            return new TokenQuotaDto
            {
                AiMode = "hitpan_pool",
                MonthlyLimit = 100000,
                ExtraTokens = 0,
                UsedTokens = 0,
                Remaining = 100000,
                SubscriptionTier = "basic",
                AnthropicKeyConfigured = false,
                AnthropicKeyLast4 = null
            };
        }

        // 당월 사용량 합산
        var ym = DateTime.UtcNow.ToString("yyyy-MM");
        var used = await _db.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(total_tokens), 0)
            FROM ai_usage_logs
            WHERE tenant_id = @TenantId AND ym = @Ym
            """,
            new { TenantId = tenantId, Ym = ym },
            cancellationToken: ct)).ConfigureAwait(false) ?? 0;

        var totalAllowed = tenant.MonthlyLimit + tenant.ExtraTokens;

        return new TokenQuotaDto
        {
            AiMode = tenant.AiMode ?? "hitpan_pool",
            MonthlyLimit = tenant.MonthlyLimit,
            ExtraTokens = tenant.ExtraTokens,
            UsedTokens = used,
            Remaining = Math.Max(0, totalAllowed - used),
            SubscriptionTier = tenant.SubscriptionTier ?? "basic",
            AnthropicKeyConfigured =
                !string.IsNullOrEmpty(tenant.AnthropicKeyLast4)
                && string.Equals(tenant.AnthropicKeyStatus, "verified", StringComparison.OrdinalIgnoreCase),
            AnthropicKeyLast4 = tenant.AnthropicKeyLast4
        };
    }

    // ─────────────────────────────────────────────────────────────
    // KB 검색
    // ─────────────────────────────────────────────────────────────
    public async Task<List<KbArticleDto>> SearchKbAsync(
        string query,
        string? category,
        int limit,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        limit = Math.Clamp(limit, 1, 50);

        var kw = (query ?? string.Empty).Trim();
        var hasQuery = kw.Length >= 1;
        var hasCategory = !string.IsNullOrWhiteSpace(category);

        // WHERE 구성 — SQL 인젝션 방지 위해 category 는 파라미터로만 전달
        var where = new List<string> { "is_public = 1" };
        var p = new DynamicParameters();

        if (hasQuery)
        {
            where.Add("(title LIKE @Q OR question_keywords LIKE @Q OR content_markdown LIKE @Q)");
            p.Add("Q", $"%{kw}%");
        }
        if (hasCategory)
        {
            where.Add("category = @Category");
            p.Add("Category", category);
        }
        p.Add("Lim", limit);

        var sql = $"""
            SELECT article_id AS ArticleId, category AS Category, title AS Title,
                   content_markdown AS ContentMarkdown, related_menu_url AS RelatedMenuUrl,
                   hit_count AS HitCount
            FROM hitpan_knowledge
            WHERE {string.Join(" AND ", where)}
            ORDER BY hit_count DESC, usage_rating DESC
            LIMIT @Lim
            """;

        var rows = await _db.QueryAsync<KbArticleDto>(
            new CommandDefinition(sql, p, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    // ─────────────────────────────────────────────────────────────
    // 인기 KB
    // ─────────────────────────────────────────────────────────────
    public async Task<List<KbArticleDto>> GetPopularKbAsync(int limit, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        limit = Math.Clamp(limit, 1, 50);

        var rows = await _db.QueryAsync<KbArticleDto>(new CommandDefinition(
            """
            SELECT article_id AS ArticleId, category AS Category, title AS Title,
                   content_markdown AS ContentMarkdown, related_menu_url AS RelatedMenuUrl,
                   hit_count AS HitCount
            FROM hitpan_knowledge
            WHERE is_public = 1
            ORDER BY hit_count DESC, usage_rating DESC
            LIMIT @Lim
            """,
            new { Lim = limit },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    // ─────────────────────────────────────────────────────────────
    // 내부 헬퍼
    // ─────────────────────────────────────────────────────────────

    /// <summary>키워드 LIKE OR 매칭으로 관련 KB 아티클 TOP 5 반환.</summary>
    private async Task<List<KbArticleDto>> FindMatchingArticlesAsync(
        List<string> keywords,
        CancellationToken ct)
    {
        if (keywords.Count == 0)
        {
            return new List<KbArticleDto>();
        }

        // 각 키워드마다 (title OR keywords OR content) LIKE 조합
        var conditions = string.Join(" OR ", keywords.Select((_, i) =>
            $"(title LIKE @K{i} OR question_keywords LIKE @K{i} OR content_markdown LIKE @K{i})"));

        var sql = $"""
            SELECT article_id AS ArticleId, category AS Category, title AS Title,
                   content_markdown AS ContentMarkdown, related_menu_url AS RelatedMenuUrl,
                   hit_count AS HitCount
            FROM hitpan_knowledge
            WHERE is_public = 1 AND ({conditions})
            ORDER BY hit_count DESC, usage_rating DESC
            LIMIT 5
            """;

        var p = new DynamicParameters();
        for (var i = 0; i < keywords.Count; i++)
        {
            p.Add($"K{i}", $"%{keywords[i]}%");
        }

        var rows = await _db.QueryAsync<KbArticleDto>(
            new CommandDefinition(sql, p, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open)
        {
            return;
        }

        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }

        _db.Open();
    }

    /// <summary>tenants 테이블에서 AI 관련 컬럼만 읽는 로컬 행 DTO.</summary>
    private sealed class TenantAiRow
    {
        public string? AiMode { get; set; }
        public int MonthlyLimit { get; set; }
        public int ExtraTokens { get; set; }
        public string? SubscriptionTier { get; set; }
        public string? AnthropicKeyLast4 { get; set; }
        public string? AnthropicKeyStatus { get; set; }
    }
}
