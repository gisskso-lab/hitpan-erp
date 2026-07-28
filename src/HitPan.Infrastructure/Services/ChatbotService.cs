using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Chatbot;
using HitPan.Application.DTOs.Sales;
using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Security;
using Microsoft.Extensions.Logging;

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
    private readonly IEncryptionService _encryption;
    // 신규(2026-06-19): KB 매칭 실패 시 외부 도우미 직통 호출 + 정체성 System Prompt.
    private readonly IChatCompletionProvider _chatProvider;
    private readonly IChatbotSystemPrompt _systemPrompt;
    // 신규(2026-06-20): AI 직원 — 자연어 분석 명령을 실데이터 표+차트로 처리(읽기 전용).
    private readonly IAiEmployeeAnalysisService _analysis;
    // 신규(2026-06-20): AI 직원 엔진(Tool Use) — 클로드가 도구 스스로 선택. 사장님 "FSD" 비전.
    private readonly HitPan.Application.Services.Ai.IAiAgentService _agent;
    // 신규(2026-06-20): Lv.3 워크플로우 연쇄 — 거래명세서 승인 시 확정 + 수주 자동생성.
    private readonly ISalesService _sales;
    private readonly ILogger<ChatbotService> _logger;

    public ChatbotService(
        IDbConnection db,
        IAuditService audit,
        IEncryptionService encryption,
        IChatCompletionProvider chatProvider,
        IChatbotSystemPrompt systemPrompt,
        IAiEmployeeAnalysisService analysis,
        HitPan.Application.Services.Ai.IAiAgentService agent,
        ISalesService sales,
        ILogger<ChatbotService> logger)
    {
        _db = db;
        _audit = audit;
        _encryption = encryption;
        _chatProvider = chatProvider;
        _systemPrompt = systemPrompt;
        _analysis = analysis;
        _agent = agent;
        _sales = sales;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // 질의 → 답변
    // ─────────────────────────────────────────────────────────────
    public async Task<ChatAnswerDto> AskAsync(
        ChatAskRequest req,
        string tenantId,
        string userId,
        IReadOnlySet<string>? policies = null,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(req.Message))
        {
            throw new ArgumentException("질문 내용이 비어있습니다.", nameof(req));
        }

        // 1) 월간 토큰 할당량 조회 (Phase A: 표시용, 차감 로직은 Phase B)
        var quota = await GetQuotaAsync(tenantId, ct).ConfigureAwait(false);

        // 단기 기억: 직전 대화(history)를 provider 형식으로 정제 (최근 8턴까지 — 토큰 절약).
        var history = (req.History ?? new())
            .TakeLast(8)
            .Select(t => new ChatHistoryTurn
            {
                Role = string.Equals(t.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
                Content = t.Content ?? ""
            })
            .Where(t => !string.IsNullOrWhiteSpace(t.Content))
            .ToList();

        // 1.5) AI 직원 엔진 (Tool Use) — 사장님 "FSD" 비전 (2026-06-20).
        //   클로드가 명령을 보고 히트판 도구를 스스로 골라 호출(조회/생성). 키 valid + 호출 성공 시 처리.
        //   키 없음·실패 시 Handled=false → 아래 기존 흐름(하드코딩 분석 → KB → 도우미)으로 폴백.
        //   = FSD 옵션: 켜면(키) 자동, 안 켜도 기존 동작 그대로.
        var agentResult = await TryRunAgentAsync(req.Message, tenantId, userId, history, policies, ct).ConfigureAwait(false);
        if (agentResult is { Handled: true })
        {
            return await BuildAgentAnswerAsync(req, tenantId, userId, quota, agentResult, ct).ConfigureAwait(false);
        }

        // 1.6) (폴백) 로컬 하드코딩 수익분석 — 엔진 미동작 시에도 수익분석은 크레딧 0으로 동작.
        var analysis = await _analysis.TryAnalyzeAsync(req.Message, tenantId, history, ct).ConfigureAwait(false);
        if (analysis is not null)
        {
            return await BuildAnalysisAnswerAsync(req, tenantId, userId, quota, analysis, ct).ConfigureAwait(false);
        }

        // 2) 간단 키워드 분할 (한국어 FULLTEXT 는 토큰 품질이 낮아 LIKE 조합으로 대체)
        var keywords = req.Message
            .Split(new[] { ' ', ',', '?', '!', '.', '？', '！', '。', '、' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(k => k.Length >= 2)
            .Take(5)
            .ToList();

        // 3) KB 매칭
        var matchedArticles = await FindMatchingArticlesAsync(keywords, ct).ConfigureAwait(false);

        // 4) 답변 구성
        //   ① KB 매칭 충분 → KB 본문 합성 (기존 동작 유지)
        //   ② KB 매칭 없음 → 외부 도우미 직통 호출 (BYOK 키 복호화 후 Anthropic Messages API)
        //   ③ 키 없음·호출 실패 → KB-only 폴백("도움말 준비 중")
        string answer;
        decimal confidence;
        // 외부 도우미가 답했을 때 실제 토큰 사용량(없으면 0 → 아래 추정값 사용).
        var providerInputTokens = 0;
        var providerOutputTokens = 0;
        var providerUsed = false;
        var providerModel = "none";

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
            // KB 매칭 실패 → 외부 도우미 시도 (저장된 BYOK 키가 'valid' 일 때만). 직전 대화(history) 전달.
            var providerAnswer = await TryProviderAnswerAsync(req.Message, tenantId, history, ct).ConfigureAwait(false);

            if (providerAnswer is not null && providerAnswer.Succeeded)
            {
                answer = providerAnswer.Answer;
                confidence = 0.7m; // 도우미 응답 신뢰도(KB 미존재이나 모델 답변 확보).
                providerUsed = true;
                providerInputTokens = providerAnswer.InputTokens;
                providerOutputTokens = providerAnswer.OutputTokens;
                providerModel = string.IsNullOrWhiteSpace(providerAnswer.Model) ? "anthropic" : providerAnswer.Model;
            }
            else
            {
                // 키 없음·상태 invalid·호출 실패 → 기존 KB-only 폴백.
                answer =
                    "아직 관련 도움말이 준비되지 않았어요. 이 질문은 우리 팀이 확인 후 답변을 추가하겠습니다.\n\n" +
                    "💡 지금 바로 도움이 필요하면:\n" +
                    "- 사이드바에서 관련 메뉴를 찾아보기\n" +
                    "- 일반 사용문의는 담당자에게 연락";
                confidence = 0.2m;
            }
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

        // 6) 토큰 사용량 기록
        //   - 외부 도우미가 답한 경우: 응답의 실제 토큰 수(input/output) 반영.
        //   - KB/폴백 응답: 기존 추정값(50 + 메시지길이/2) 유지.
        int inTokens, outTokens, tokensUsed;
        string aiProvider;
        if (providerUsed)
        {
            inTokens = providerInputTokens;
            outTokens = providerOutputTokens;
            tokensUsed = providerInputTokens + providerOutputTokens;
            aiProvider = "anthropic";
        }
        else
        {
            tokensUsed = 50 + req.Message.Length / 2;
            inTokens = req.Message.Length / 4;
            outTokens = answer.Length / 4;
            aiProvider = "none";
        }
        var ym = DateTime.UtcNow.ToString("yyyy-MM");

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ai_usage_logs (
              tenant_id, conv_id, ai_provider, input_tokens, output_tokens,
              total_tokens, charge_mode, usage_type, ym)
            VALUES (
              @TenantId, @ConvId, @Provider, @In, @Out, @Total, 'hitpan_pool', 'chat', @Ym)
            """,
            new
            {
                TenantId = tenantId,
                ConvId = convId,
                Provider = aiProvider,
                In = inTokens,
                Out = outTokens,
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
                matchedCount = matchedArticles.Count,
                providerUsed,
                providerModel
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
    // Lv.3 워크플로우 연쇄 — 초안 승인 처리 (사장님 결재 2026-06-20)
    //   거래명세서 초안 승인 → ① 상세 조회 ② 확정(confirm) ③ 대응 수주 자동생성.
    //   사람이 승인 버튼 눌렀을 때만 호출(확정은 사람 — 헌법 #6). 워크플로우 끊김 0(헌법 #20).
    // ─────────────────────────────────────────────────────────────
    public async Task<ApproveActionResultDto> ApproveActionAsync(
        ApproveActionRequest req,
        string tenantId,
        string userId,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(req.DraftId))
        {
            return new ApproveActionResultDto { Succeeded = false, Message = "승인할 초안 정보가 없습니다." };
        }

        // 현재 지원: 거래명세서 초안.
        if (!string.Equals(req.Kind, "sales-delivery-draft", StringComparison.OrdinalIgnoreCase))
        {
            return new ApproveActionResultDto { Succeeded = false, Message = "지원하지 않는 승인 종류입니다." };
        }

        try
        {
            // ① 거래명세서 상세 조회 (거래처·품목 확보).
            var detail = await _sales.GetDeliveryAsync(req.DraftId, tenantId, ct).ConfigureAwait(false);
            if (detail is null)
            {
                return new ApproveActionResultDto { Succeeded = false, Message = "거래명세서 초안을 찾을 수 없습니다." };
            }

            // ② 확정 (draft → confirmed). 재고·원장 반영은 이 시점(헌법 #6).
            await _sales.ConfirmDeliveryAsync(req.DraftId, new ConfirmDeliveryRequest(), ct).ConfigureAwait(false);

            string? chainedId = null;
            string? chainedNo = null;
            var msg = $"거래명세서({detail.DeliveryNo}) 확정 완료";

            // ③ 워크플로우 연쇄 — 대응 수주 자동생성(사장님 예시: "거래명세서 쓰고 수주도").
            // 봉합 (2026-06-23, 5차 전수조사 AICHAT-P2-03 P2): 확정(②)과 수주생성(③)은 서로 다른
            //   UnitOfWork 라 단일 트랜잭션으로 못 묶는다. 종전엔 ③ 실패 시 바깥 catch 가 전체를
            //   Succeeded=false 로 반환해, 사용자는 "오류"만 보고 거래명세서가 이미 확정된 사실을 몰랐다
            //   (재시도 시 이미 confirmed → "draft 만 확정 가능" 혼란). 확정은 이미 성공(원장 반영 완료)이므로,
            //   ③ 만 별도 try 로 분리해 실패해도 "확정 성공 + 수주는 수동 생성 필요"로 정직하게 분리 안내한다.
            if (req.Chain && detail.Items.Count > 0)
            {
                var orderReq = new CreateSalesOrderRequest
                {
                    PartnerId = detail.PartnerId,
                    EmployeeId = detail.EmployeeId,
                    OrderDate = detail.OrderDate == default ? DateTime.Today : detail.OrderDate,
                    Memo = $"AI 직원 워크플로우 연쇄 — 거래명세서 {detail.DeliveryNo} 기준 자동 생성",
                    Items = detail.Items.Select(i => new CreateSalesOrderItemRequest
                    {
                        ItemId = i.ItemId,
                        OrderedQty = i.Qty,
                        UnitPrice = i.UnitPrice,
                        SupplyAmount = i.Amount,
                        VatAmount = i.VatAmount
                    }).ToList()
                };
                try
                {
                    chainedId = await _sales.CreateOrderAsync(orderReq, ct).ConfigureAwait(false);
                    msg += " + 대응 수주 자동생성 완료";
                }
                catch (OperationCanceledException)
                {
                    // 취소는 실패가 아니므로 "수주 실패" 안내로 둔갑시키지 않고 그대로 전파.
                    throw;
                }
                catch (Exception chainEx)
                {
                    // 확정은 이미 반영됨 — 연쇄만 실패. 사용자에게 확정 사실과 후속 수동 조치를 분리 안내.
                    _logger.LogWarning(chainEx, "AI 직원 수주 연쇄 실패(확정은 완료): draft={Draft}", req.DraftId);
                    msg += " (대응 수주 자동생성은 실패했습니다 — 수주는 수동으로 생성해 주세요)";
                }
            }

            await _audit.LogAsync(
                actionType: "approve_chain",
                entityType: "chatbot",
                entityId: req.DraftId,
                afterJson: System.Text.Json.JsonSerializer.Serialize(new { req.Kind, chained = chainedId, chain = req.Chain }),
                ct: ct).ConfigureAwait(false);

            return new ApproveActionResultDto
            {
                Succeeded = true,
                Message = msg,
                ChainedId = chainedId,
                ChainedNo = chainedNo
            };
        }
        catch (OperationCanceledException)
        {
            // 취소는 오류 메시지로 둔갑시키지 않고 그대로 전파(요청 취소 의미 보존).
            throw;
        }
        catch (Exception ex)
        {
            // 헌법 #15: 빈 catch 금지. 승인·연쇄 실패는 경고 + 사용자에 사유 반환.
            _logger.LogWarning(ex, "AI 직원 승인 연쇄 실패: draft={Draft}", req.DraftId);
            return new ApproveActionResultDto { Succeeded = false, Message = $"승인 처리 중 오류: {ex.Message}" };
        }
    }

    // ─────────────────────────────────────────────────────────────
    // AI 직원 엔진(Tool Use) — 키 복호화 후 엔진 실행. 키 없음·실패 시 null(폴백).
    // ─────────────────────────────────────────────────────────────
    private async Task<HitPan.Application.Services.Ai.AgentRunResult?> TryRunAgentAsync(
        string userMessage,
        string tenantId,
        string userId,
        IReadOnlyList<ChatHistoryTurn> history,
        IReadOnlySet<string>? policies,
        CancellationToken ct)
    {
        // 키 행 조회(암호문 + 상태). valid 가 아니면 엔진 미동작(FSD 옵션 OFF).
        var keyRow = await _db.QueryFirstOrDefaultAsync<ByokKeyRow?>(new CommandDefinition(
            """
            SELECT anthropic_api_key_encrypted AS Encrypted,
                   anthropic_key_status        AS Status
            FROM local_subscription
            WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        if (keyRow is null
            || string.IsNullOrWhiteSpace(keyRow.Encrypted)
            || !string.Equals(keyRow.Status, "valid", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string decryptedKey;
        try
        {
            decryptedKey = _encryption.Decrypt(keyRow.Encrypted!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 직원 엔진 — BYOK 키 복호화 실패. 폴백. tenant={Tenant}", tenantId);
            return null;
        }

        var ctx = new HitPan.Application.Services.Ai.ToolContext
        {
            TenantId = tenantId,
            UserId = userId,
            // 봉합 (2026-06-20, 3차 전수조사 AICHAT-SEC-01-F1): 호출자 권한 정책을 주입.
            //   엔진이 쓰기 Tool 의 RequiredPolicy 와 대조해 무권한 실행을 차단(헌법 #7).
            Policies = policies ?? new HashSet<string>()
        };

        var result = await _agent.RunAsync(decryptedKey, userMessage, history, ctx, ct).ConfigureAwait(false);
        return result;
    }

    // ─────────────────────────────────────────────────────────────
    // AI 직원 엔진 결과 → 답변 DTO + 대화/사용량 적재.
    // ─────────────────────────────────────────────────────────────
    private async Task<ChatAnswerDto> BuildAgentAnswerAsync(
        ChatAskRequest req,
        string tenantId,
        string userId,
        TokenQuotaDto quota,
        HitPan.Application.Services.Ai.AgentRunResult agent,
        CancellationToken ct)
    {
        var convId = Guid.NewGuid().ToString();
        var tokensUsed = agent.InputTokens + agent.OutputTokens;

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ai_conversations (
              conv_id, tenant_id, user_id, intent, user_message, ai_response,
              matched_article_ids, confidence_score, created_at)
            VALUES (
              @ConvId, @TenantId, @UserId, 'agent', @Msg, @Answer, NULL, 0.9, NOW(6))
            """,
            new { ConvId = convId, TenantId = tenantId, UserId = userId, Msg = req.Message, Answer = agent.Answer },
            cancellationToken: ct)).ConfigureAwait(false);

        var ym = DateTime.UtcNow.ToString("yyyy-MM");
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ai_usage_logs (
              tenant_id, conv_id, ai_provider, input_tokens, output_tokens,
              total_tokens, charge_mode, usage_type, ym)
            VALUES (
              @TenantId, @ConvId, 'anthropic', @In, @Out, @Total, 'hitpan_pool', 'agent', @Ym)
            """,
            new { TenantId = tenantId, ConvId = convId, In = agent.InputTokens, Out = agent.OutputTokens, Total = tokensUsed, Ym = ym },
            cancellationToken: ct)).ConfigureAwait(false);

        await _audit.LogAsync(
            actionType: "agent",
            entityType: "chatbot",
            entityId: convId,
            afterJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                tokensUsed,
                hasAnalysis = agent.Analysis is not null,
                hasPending = agent.PendingAction is not null
            }),
            ct: ct).ConfigureAwait(false);

        return new ChatAnswerDto
        {
            ConvId = convId,
            Answer = agent.Answer,
            RelatedArticles = new(),
            ConfidenceScore = 0.9m,
            TokensUsed = tokensUsed,
            TokensRemaining = Math.Max(0, quota.Remaining - tokensUsed),
            NeedsFollowUp = false,
            Analysis = agent.Analysis,
            PendingAction = agent.PendingAction
        };
    }

    // ─────────────────────────────────────────────────────────────
    // AI 직원 — 분석 결과 답변 구성 (실데이터 표+차트 + 요약 문구)
    //   클로드 호출 0(로컬 분석) → 크레딧 0. 대화 이력은 동일하게 적재.
    // ─────────────────────────────────────────────────────────────
    private async Task<ChatAnswerDto> BuildAnalysisAnswerAsync(
        ChatAskRequest req,
        string tenantId,
        string userId,
        TokenQuotaDto quota,
        AiAnalysisResultDto analysis,
        CancellationToken ct)
    {
        // 요약 문구: 데이터 있으면 상위 항목 한 줄 요약, 없으면 안내.
        string answer;
        if (analysis.Rows.Count == 0)
        {
            answer = $"**{analysis.Title}**\n\n해당 조건에 맞는 거래 자료가 없습니다. 기간이나 거래처명을 바꿔서 다시 말씀해 주세요.";
        }
        else
        {
            var firstCol = analysis.Columns.Count > 0 ? analysis.Columns[0] : "항목";
            var topName = analysis.Chart.Count > 0 ? analysis.Chart[0].Label : analysis.Rows[0][0];
            answer =
                $"**{analysis.Title}** — 총 {analysis.Rows.Count}개 {firstCol} 기준으로 분석했습니다.\n\n" +
                $"이익이 가장 큰 {firstCol}은 **{topName}**입니다. 아래 표와 그래프에서 전체 내역을 확인하세요.\n\n" +
                "💡 수익성 = 매출 − 원가(매입원가)이며, 확정(confirmed)된 전표만 집계합니다.";
        }

        var convId = Guid.NewGuid().ToString();

        // 대화 이력 저장 (intent=analysis).
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ai_conversations (
              conv_id, tenant_id, user_id, intent, user_message, ai_response,
              matched_article_ids, confidence_score, created_at)
            VALUES (
              @ConvId, @TenantId, @UserId, 'analysis', @Msg, @Answer,
              NULL, 0.95, NOW(6))
            """,
            new { ConvId = convId, TenantId = tenantId, UserId = userId, Msg = req.Message, Answer = answer },
            cancellationToken: ct)).ConfigureAwait(false);

        // 토큰 사용량: 로컬 분석이라 클로드 토큰 0(provider=local).
        var ym = DateTime.UtcNow.ToString("yyyy-MM");
        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ai_usage_logs (
              tenant_id, conv_id, ai_provider, input_tokens, output_tokens,
              total_tokens, charge_mode, usage_type, ym)
            VALUES (
              @TenantId, @ConvId, 'local', 0, 0, 0, 'hitpan_pool', 'analysis', @Ym)
            """,
            new { TenantId = tenantId, ConvId = convId, Ym = ym },
            cancellationToken: ct)).ConfigureAwait(false);

        await _audit.LogAsync(
            actionType: "analyze",
            entityType: "chatbot",
            entityId: convId,
            afterJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                kind = analysis.Kind,
                rows = analysis.Rows.Count,
                title = analysis.Title
            }),
            ct: ct).ConfigureAwait(false);

        return new ChatAnswerDto
        {
            ConvId = convId,
            Answer = answer,
            RelatedArticles = new(),
            ConfidenceScore = 0.95m,
            TokensUsed = 0,
            TokensRemaining = quota.Remaining,
            NeedsFollowUp = false,
            Analysis = analysis
        };
    }

    // ─────────────────────────────────────────────────────────────
    // 외부 도우미 직통 호출 (BYOK)
    //  - anthropic_key_status='valid' + 암호화 키 존재 시에만 호출.
    //  - 키 복호화는 IEncryptionService(헌법 #5). tenant_id 는 JWT 유래 인자(헌법 #2).
    //  - 고객 PC → Anthropic 직통 (본사 프록시 0, 헌법 #18·#22).
    //  - 실패는 예외 대신 null/Fail 반환 → 호출부가 KB-only 폴백.
    // ─────────────────────────────────────────────────────────────
    private async Task<ChatProviderResult?> TryProviderAnswerAsync(
        string userMessage,
        string tenantId,
        IReadOnlyList<ChatHistoryTurn>? history,
        CancellationToken ct)
    {
        // 1) 키 행 조회 (암호문 + 상태). 평문은 DB·로그에 없음(헌법 #5).
        var keyRow = await _db.QueryFirstOrDefaultAsync<ByokKeyRow?>(new CommandDefinition(
            """
            SELECT anthropic_api_key_encrypted AS Encrypted,
                   anthropic_key_status        AS Status
            FROM local_subscription
            WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (keyRow is null
            || string.IsNullOrWhiteSpace(keyRow.Encrypted)
            || !string.Equals(keyRow.Status, "valid", StringComparison.OrdinalIgnoreCase))
        {
            // 키 미설정 또는 상태 invalid → 폴백.
            return null;
        }

        // 2) 복호화 — 평문 키는 이 스코프 안에서만 사용.
        string decryptedKey;
        try
        {
            decryptedKey = _encryption.Decrypt(keyRow.Encrypted!);
        }
        catch (Exception ex)
        {
            // 헌법 #15: 빈 catch 금지. 복호화 실패(키 손상·키 변경 등)는 폴백 + 경고.
            _logger.LogWarning(ex, "BYOK 키 복호화 실패. KB-only 폴백으로 전환합니다. tenant={Tenant}", tenantId);
            return null;
        }

        // 3) 외부 도우미 호출. System Prompt = 정체성 .md(캐싱) + 직전 대화(history) 전달.
        var result = await _chatProvider
            .CompleteAsync(decryptedKey, _systemPrompt.Value, userMessage, history, ct)
            .ConfigureAwait(false);

        return result;
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
                catch (Exception ex)
                {
                    // §절대원칙 #15: 빈 catch 금지. 피드백 기록 자체는 이미 완료됐으므로 흐름은
                    // 끊지 않되, 평점 반영 실패 원인은 운영 추적용으로 남긴다.
                    _logger.LogWarning(ex, "피드백 평점 반영 실패(matched_article_ids JSON 파싱/갱신) — conv={ConvId}", req.ConvId);
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
            FROM local_subscription
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
            // 봉합 2026-06-19: 키 상태 표준값은 'valid'(이전 "verified" 비교는 버그 — 항상 false).
            //   anthropic_key_status 기본값 'none', 키 저장 시 'valid'. 데이터설계서 §7 확인.
            AnthropicKeyConfigured =
                !string.IsNullOrEmpty(tenant.AnthropicKeyLast4)
                && string.Equals(tenant.AnthropicKeyStatus, "valid", StringComparison.OrdinalIgnoreCase),
            AnthropicKeyLast4 = tenant.AnthropicKeyLast4
        };
    }

    // ─────────────────────────────────────────────────────────────
    // BYOK — AI 도우미 연동 키 저장 (AES-256, 헌법 #5)
    // ─────────────────────────────────────────────────────────────
    public async Task SaveApiKeyAsync(string apiKey, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var key = (apiKey ?? string.Empty).Trim();
        if (key.Length < 8)
        {
            throw new ArgumentException("AI 도우미 연동 키 형식이 올바르지 않습니다.", nameof(apiKey));
        }

        // 평문 키는 즉시 AES-256 암호화 — DB·로그·메모리에 평문 잔류 금지(헌법 #5).
        var encrypted = _encryption.Encrypt(key);
        var last4 = key[^4..];

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE local_subscription
            SET anthropic_api_key_encrypted = @Enc,
                anthropic_api_key_last4     = @Last4,
                anthropic_key_status        = 'valid',
                anthropic_key_saved_at      = NOW()
            WHERE tenant_id = @TenantId
            """,
            new { Enc = encrypted, Last4 = last4, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new InvalidOperationException("구독 정보를 찾을 수 없어 키를 저장하지 못했습니다.");
        }

        // 감사 로그 — 평문·암호문 절대 기록 금지, last4만.
        await _audit.LogAsync(
            actionType: "ai_key_save",
            entityType: "ai_settings",
            entityId: tenantId,
            afterJson: System.Text.Json.JsonSerializer.Serialize(new { last4, status = "valid" }),
            ct: ct).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────
    // BYOK — AI 도우미 연동 키 삭제
    // ─────────────────────────────────────────────────────────────
    public async Task DeleteApiKeyAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE local_subscription
            SET anthropic_api_key_encrypted = NULL,
                anthropic_api_key_last4     = NULL,
                anthropic_key_status        = 'none',
                anthropic_key_saved_at      = NULL,
                anthropic_key_verified_at   = NULL
            WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        await _audit.LogAsync(
            actionType: "ai_key_delete",
            entityType: "ai_settings",
            entityId: tenantId,
            afterJson: System.Text.Json.JsonSerializer.Serialize(new { status = "none" }),
            ct: ct).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────
    // BYOK — AI 도우미 설정 현황 (평문 키 반환 금지)
    // ─────────────────────────────────────────────────────────────
    public async Task<AiSettingsDto> GetAiSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var row = await _db.QueryFirstOrDefaultAsync<AiSettingsRow?>(new CommandDefinition(
            """
            SELECT
              ai_mode                   AS AiMode,
              ai_token_monthly_limit    AS MonthlyLimit,
              ai_token_extra            AS ExtraTokens,
              subscription_tier         AS SubscriptionTier,
              anthropic_api_key_last4   AS Last4,
              anthropic_key_status      AS KeyStatus,
              anthropic_key_saved_at    AS KeySavedAt
            FROM local_subscription
            WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (row is null)
        {
            return new AiSettingsDto
            {
                KeyConfigured = false,
                KeyStatus = "none",
                AiMode = "hitpan_pool",
                MonthlyLimit = 0,
                ExtraTokens = 0,
                SubscriptionTier = "basic"
            };
        }

        var status = row.KeyStatus ?? "none";
        return new AiSettingsDto
        {
            KeyConfigured =
                !string.IsNullOrEmpty(row.Last4)
                && string.Equals(status, "valid", StringComparison.OrdinalIgnoreCase),
            KeyLast4 = row.Last4,
            KeyStatus = status,
            KeySavedAt = row.KeySavedAt,
            AiMode = row.AiMode ?? "hitpan_pool",
            MonthlyLimit = row.MonthlyLimit,
            ExtraTokens = row.ExtraTokens,
            SubscriptionTier = row.SubscriptionTier ?? "basic"
        };
    }

    // ─────────────────────────────────────────────────────────────
    // 이번 달 토큰 사용량 집계 (ai_usage_logs / ym 기준)
    // ─────────────────────────────────────────────────────────────
    public async Task<AiUsageDto> GetUsageAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var ym = DateTime.UtcNow.ToString("yyyy-MM");

        var agg = await _db.QueryFirstOrDefaultAsync<UsageAggRow?>(new CommandDefinition(
            """
            SELECT
              COALESCE(SUM(input_tokens), 0)  AS InputTokens,
              COALESCE(SUM(output_tokens), 0) AS OutputTokens,
              COALESCE(SUM(total_tokens), 0)  AS TotalTokens
            FROM ai_usage_logs
            WHERE tenant_id = @TenantId AND ym = @Ym
            """,
            new { TenantId = tenantId, Ym = ym },
            cancellationToken: ct)).ConfigureAwait(false) ?? new UsageAggRow();

        // 한도 = 기본 한도 + 추가 토큰 (local_subscription)
        var limitRow = await _db.QueryFirstOrDefaultAsync<LimitRow?>(new CommandDefinition(
            """
            SELECT ai_token_monthly_limit AS MonthlyLimit, ai_token_extra AS ExtraTokens
            FROM local_subscription
            WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        var totalAllowed = (limitRow?.MonthlyLimit ?? 0) + (limitRow?.ExtraTokens ?? 0);

        return new AiUsageDto
        {
            Ym = ym,
            InputTokens = agg.InputTokens,
            OutputTokens = agg.OutputTokens,
            TotalTokens = agg.TotalTokens,
            MonthlyLimit = totalAllowed,
            Remaining = Math.Max(0, totalAllowed - agg.TotalTokens)
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

    /// <summary>local_subscription 테이블에서 AI 관련 컬럼만 읽는 로컬 행 DTO.</summary>
    private sealed class TenantAiRow
    {
        public string? AiMode { get; set; }
        public int MonthlyLimit { get; set; }
        public int ExtraTokens { get; set; }
        public string? SubscriptionTier { get; set; }
        public string? AnthropicKeyLast4 { get; set; }
        public string? AnthropicKeyStatus { get; set; }
    }

    /// <summary>AI 설정 현황 조회용 로컬 행 DTO.</summary>
    private sealed class AiSettingsRow
    {
        public string? AiMode { get; set; }
        public int MonthlyLimit { get; set; }
        public int ExtraTokens { get; set; }
        public string? SubscriptionTier { get; set; }
        public string? Last4 { get; set; }
        public string? KeyStatus { get; set; }
        public DateTime? KeySavedAt { get; set; }
    }

    /// <summary>당월 사용량 합산용 로컬 행 DTO.</summary>
    private sealed class UsageAggRow
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int TotalTokens { get; set; }
    }

    /// <summary>한도 조회용 로컬 행 DTO.</summary>
    private sealed class LimitRow
    {
        public int MonthlyLimit { get; set; }
        public int ExtraTokens { get; set; }
    }

    /// <summary>BYOK 키 조회용 로컬 행 DTO(암호문 + 상태). 평문은 보관하지 않는다.</summary>
    private sealed class ByokKeyRow
    {
        public string? Encrypted { get; set; }
        public string? Status { get; set; }
    }
}
