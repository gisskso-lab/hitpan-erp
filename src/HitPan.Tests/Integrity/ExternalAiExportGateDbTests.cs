using System.Data;
using System.Net;
using System.Text.Json;
using Dapper;
using HitPan.Application.DTOs.Chatbot;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Application.Services.Ai;
using HitPan.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>외부 AI 반출 게이트</b> (20261006작9 §5 G-1~G-7) — 「외부 호출이 떠났는가」를 <b>동작으로</b> 잰다.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇을 막나</b> — 선행검증([1-V] 20261006 전수실측): 외부 AI HTTP 호출 지점은 3곳
/// (① 챗봇 폴백 ChatbotService.TryProviderAnswerAsync · ② AI직원 엔진 AiAgentService.RunAsync →
/// CompleteWithToolsAsync · ③ 연결확인 핑). 고지·동의·계약 게이트가 0이라, 키만 valid 면
/// 사용자 질문 + 직전 8턴 history + Tool 반출(사업자번호·매출수치)이 3사로 나갔다.
/// X-2: ②의 답(매출수치)이 history 에 실려 다음 질문에서 ①로 재반출 — 공급자를 챗GPT/제미나이로
/// 골랐으면 그쪽으로도 번진다.
/// </para>
/// <para>
/// 🔴 <b>측정 방식</b> — 외부 실호출 0: 가짜 <see cref="HttpMessageHandler"/> 가 전부 가로채
/// 「호출이 떠났는가」만 센다. DB 는 실제 MariaDB 위 <b>TEMPORARY 표</b>(연결 안에서 실표를 가린다 —
/// 공용 시험 DB 에 행을 남기지 않는다 · 관례 BomLevelProducibleGateTests).
/// </para>
/// <para>
/// ⚠️ DB 게이트는 개발 PC 에서 HITPAN_DB_PASS 없으면 SKIP 이 정상 — CI <c>db-gate</c> 잡
/// (<c>HITPAN_REQUIRE_DB</c>)이 계측 경로다. 로컬 초록을 검증으로 읽지 마라.
/// 운영 무접촉(#39) — 시험 DB(HITPAN_TEST_DB) + TEMPORARY 표만 쓴다.
/// </para>
/// </remarks>
public sealed class ExternalAiExportGateDbTests
{
    // ══ 시험 DB 연결 (관례: BomLevelProducibleGateTests · HITPAN_TEST_DB = CI hitpan_e2e) ══

    private static string TestDb =>
        Environment.GetEnvironmentVariable("HITPAN_TEST_DB") ?? "hitpan_e2e";

    private static string ConnString(string? database = null)
    {
        var host = Environment.GetEnvironmentVariable("HITPAN_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("HITPAN_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("HITPAN_DB_USER") ?? "hitpan";
        // 🔴 비밀번호를 코드에 적지 않는다 — 로컬은 HITPAN_DB_PASS 환경변수(관례 BomLevelProducibleGateTests:92).
        var pass = Environment.GetEnvironmentVariable("HITPAN_DB_PASS") ?? "";
        return $"Server={host};Port={port};Database={database ?? TestDb};User={user};Password={pass};"
             + "DefaultCommandTimeout=90;GuidFormat=None;AllowUserVariables=true;Pooling=false;";
    }

    private static bool ServerAvailable()
    {
        if (DbGateEnvironment.IsCi) return true;   // CI 는 DB 필수 — 못 붙으면 아래에서 실패로 드러난다
        try
        {
            using var c = new MySqlConnection(ConnString());
            c.Open();
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    private static bool Skip(string gate) => DbGateEnvironment.SkipOrFail(gate);

    /// <summary>
    /// ChatbotService 가 닿는 표 전부를 TEMPORARY 표로 가린다(이 연결 안에서만 유효 · 실표 무접촉).
    /// 표 모양은 출하 DDL(installer/hitpan_db_clean.sql) DESCRIBE 실측(#13 · 2026-10-06)과 동일
    /// — 단 TEMPORARY InnoDB 가 못 가지는 FULLTEXT·FK 는 뺀다(이 시험의 쿼리는 LIKE·단순 INSERT 뿐).
    /// </summary>
    private static MySqlConnection ShadowedDb()
    {
        var db = new MySqlConnection(ConnString());
        db.Open();
        db.Execute("""
            CREATE TEMPORARY TABLE local_subscription (
              tenant_id char(36) NOT NULL PRIMARY KEY,
              subscription_tier varchar(20) NOT NULL DEFAULT 'basic',
              status varchar(20) NOT NULL DEFAULT 'active',
              ai_mode varchar(20) NOT NULL DEFAULT 'hitpan_pool',
              ai_token_monthly_limit int NOT NULL DEFAULT 100000,
              ai_token_extra int NOT NULL DEFAULT 0,
              anthropic_api_key_encrypted varchar(512) NULL,
              anthropic_api_key_last4 varchar(8) NULL,
              anthropic_key_status varchar(20) NOT NULL DEFAULT 'none',
              openai_api_key_encrypted varchar(512) NULL,
              openai_api_key_last4 varchar(8) NULL,
              openai_key_status varchar(20) NOT NULL DEFAULT 'none',
              google_api_key_encrypted varchar(512) NULL,
              google_api_key_last4 varchar(8) NULL,
              google_key_status varchar(20) NOT NULL DEFAULT 'none',
              ai_provider varchar(20) NOT NULL DEFAULT 'anthropic'
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        db.Execute("""
            CREATE TEMPORARY TABLE hitpan_knowledge (
              article_id varchar(36) NOT NULL PRIMARY KEY,
              category varchar(30) NOT NULL,
              title varchar(200) NOT NULL,
              question_keywords varchar(500) NULL,
              content_markdown longtext NOT NULL,
              related_menu_url varchar(200) NULL,
              hit_count int NOT NULL DEFAULT 0,
              usage_rating decimal(3,2) NOT NULL DEFAULT 0.00,
              is_public tinyint(1) NOT NULL DEFAULT 1
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        db.Execute("""
            CREATE TEMPORARY TABLE ai_conversations (
              conv_id varchar(36) NOT NULL PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              user_id varchar(36) NOT NULL,
              intent varchar(30) NOT NULL DEFAULT 'usage_question',
              user_message text NOT NULL,
              ai_response text NULL,
              matched_article_ids varchar(500) NULL,
              confidence_score decimal(3,2) NULL,
              was_helpful tinyint(1) NULL,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        db.Execute("""
            CREATE TEMPORARY TABLE ai_usage_logs (
              usage_id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              conv_id varchar(36) NULL,
              ai_provider varchar(30) NOT NULL DEFAULT 'none',
              input_tokens int NOT NULL DEFAULT 0,
              output_tokens int NOT NULL DEFAULT 0,
              cached_tokens int NOT NULL DEFAULT 0,
              total_tokens int NOT NULL DEFAULT 0,
              cost_krw decimal(10,2) NOT NULL DEFAULT 0.00,
              charge_krw decimal(10,2) NOT NULL DEFAULT 0.00,
              charge_mode varchar(20) NOT NULL DEFAULT 'hitpan_pool',
              usage_type varchar(30) NOT NULL DEFAULT 'chat',
              ym char(7) NOT NULL,
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        // 동의 표 — DB-138 과 같은 모양(TEMPORARY 로 가려 공용 DB 실표 무접촉 · 시드 0건 그대로).
        db.Execute("""
            CREATE TEMPORARY TABLE ai_export_consents (
              id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
              tenant_id varchar(36) NOT NULL,
              terms_version varchar(50) NOT NULL,
              agreed_by varchar(64) NOT NULL,
              agreed_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              agreed_ip varchar(45) NOT NULL DEFAULT '',
              created_at datetime(6) NOT NULL DEFAULT current_timestamp(6),
              KEY idx_ai_export_consents_tenant (tenant_id, agreed_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        return db;
    }

    private static void SeedValidKey(IDbConnection db, string tenantId, string provider = "anthropic")
    {
        db.Execute("""
            INSERT INTO local_subscription (
              tenant_id, ai_provider,
              anthropic_api_key_encrypted, anthropic_api_key_last4, anthropic_key_status,
              openai_api_key_encrypted, openai_key_status,
              google_api_key_encrypted, google_key_status)
            VALUES (
              @T, @P,
              'ENC-ANTHROPIC', 'st42', 'valid',
              'ENC-OPENAI', 'valid',
              'ENC-GOOGLE', 'valid')
            """, new { T = tenantId, P = provider });
    }

    // ══ 가짜 HTTP — 외부 실호출 0. 「호출이 떠났는가」만 센다 ══

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly List<Uri> Requests = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests) { Requests.Add(request.RequestUri!); }
            // Anthropic Messages 모양의 응답 — CompleteAsync·CompleteWithToolsAsync 둘 다 파싱된다.
            var body = """
                {"model":"gate-fake","stop_reason":"end_turn",
                 "content":[{"type":"text","text":"게이트 시험 응답"}],
                 "usage":{"input_tokens":3,"output_tokens":2}}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }

        public int Count { get { lock (Requests) { return Requests.Count; } } }
        public string Hosts { get { lock (Requests) { return string.Join(", ", Requests.Select(u => u.Host)); } } }
    }

    private sealed class FakeHttpFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    // ══ 주변 가짜들 — 측정 대상(반출 경로) 밖의 의존성만 가짜다 ══

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            IDbTransaction? tx = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class PassThroughEncryption : IEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => "sk-gate-test-decrypted";
        public byte[] EncryptBytes(byte[] plainBytes) => plainBytes;
        public byte[] DecryptBytes(byte[] cipherBytes) => cipherBytes;
    }

    private sealed class EmptyPrompt : IChatbotSystemPrompt
    {
        public string Value => "";
    }

    private sealed class EmptyAgentPrompt : IAiAgentSystemPrompt
    {
        public string Value => "";
    }

    private sealed class NoAnalysis : IAiEmployeeAnalysisService
    {
        public Task<AiAnalysisResultDto?> TryAnalyzeAsync(
            string message, string tenantId,
            IReadOnlyList<ChatHistoryTurn>? history = null, CancellationToken ct = default)
            => Task.FromResult<AiAnalysisResultDto?>(null);
    }

    /// <summary>문② 측정용 — 엔진 진입(RunAsync 호출) 횟수를 센다. 외부로는 아무것도 안 나간다.</summary>
    private sealed class SpyAgent : IAiAgentService
    {
        public int Entered;
        public Task<AgentRunResult> RunAsync(string decryptedApiKey, string userMessage,
            IReadOnlyList<ChatHistoryTurn> history, ToolContext ctx, CancellationToken ct = default)
        {
            Entered++;
            return Task.FromResult(AgentRunResult.NotHandled());
        }
    }

    private sealed class EmptyToolRegistry : IHitpanToolRegistry
    {
        public JsonElement BuildToolCatalog() => JsonDocument.Parse("[]").RootElement.Clone();
        public IHitpanTool? Find(string name) => null;
    }

    // ══ 조립 ══
    //   ISalesService 는 Moq — 이 시험의 경로(질문→답변)는 부르지 않는다(부르면 Mock 이 기본값 반환).

    /// <summary>실물 게이트 — 같은 연결(TEMPORARY 표가 보이는)을 쓴다. 운영 DI 도 Scoped 한 연결이다.</summary>
    private static ExternalAiGate RealGate(MySqlConnection db)
        => new(db, NullLogger<ExternalAiGate>.Instance);

    private static (ChatbotService svc, RecordingHandler wire, SpyAgent agentSpy) BuildChatbot(
        MySqlConnection db, bool realAgent = false)
    {
        var wire = new RecordingHandler();
        var httpFactory = new FakeHttpFactory(wire);

        var claude = new AnthropicChatProvider(httpFactory, NullLogger<AnthropicChatProvider>.Instance);
        var gpt = new OpenAiChatProvider(httpFactory, NullLogger<OpenAiChatProvider>.Instance);
        var gemini = new GeminiChatProvider(httpFactory, NullLogger<GeminiChatProvider>.Instance);
        var factory = new AiProviderFactory(claude, gpt, gemini, NullLogger<AiProviderFactory>.Instance);

        var gate = RealGate(db);
        var agentSpy = new SpyAgent();
        IAiAgentService agent = realAgent
            ? new AiAgentService(claude, new EmptyToolRegistry(), new EmptyAgentPrompt(),
                gate, NullLogger<AiAgentService>.Instance)
            : agentSpy;

        var svc = new ChatbotService(
            db,
            new NoOpAudit(),
            new PassThroughEncryption(),
            claude,
            new EmptyPrompt(),
            new NoAnalysis(),
            agent,
            new Moq.Mock<HitPan.Application.Interfaces.ISalesService>().Object,
            factory,
            gate,
            NullLogger<ChatbotService>.Instance);

        return (svc, wire, agentSpy);
    }

    private static ChatAskRequest Ask(string message, params (string role, string content)[] history)
        => new()
        {
            Message = message,
            History = history.Select(h => new ChatTurn { Role = h.role, Content = h.content }).ToList()
        };

    // KB 에 절대 없는 질문 — LIKE 매칭 0건을 보장한다.
    private const string KbMissQuestion = "zzqx 외계어 질문 9748 매칭없음";

    // ─────────────────────────────────────────────────────────────
    //  G-2 — 키+valid · 동의 기록 0건 → ① 외부 호출 0 · KB-only + 안내 문구
    //  (🔴 G-1 = 이 시험을 봉합 **전** 코드로 돌려 FAIL(호출 1+)을 실측하는 것.
    //   봉합 전 실측 기록은 개발명세서 §G-1 — 외부 실호출 0, 가짜 핸들러가 전부 가로챘다.)
    // ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task G2_동의0건이면_챗봇폴백_외부호출_0_그리고_안내문구()
    {
        if (!ServerAvailable()) { Assert.True(Skip("G-2 외부반출게이트")); return; }
        using var db = ShadowedDb();
        var tid = Guid.NewGuid().ToString();
        SeedValidKey(db, tid);

        var (svc, wire, _) = BuildChatbot(db);

        var answer = await svc.AskAsync(Ask(KbMissQuestion), tid, "user-1");

        Assert.True(wire.Count == 0,
            $"동의 기록 0건인데 외부 호출 {wire.Count}건이 떠났다 → {wire.Hosts}");
        Assert.Contains("도움말", answer.Answer); // KB-only 폴백 본문
        // 안내 한 줄 (§4-4 초안 — 확정 시 여기 글자도 함께 교체).
        Assert.Contains("외부 도우미 연결은 이용 안내 동의 절차가 마련된 뒤 열립니다", answer.Answer);
    }

    // ─────────────────────────────────────────────────────────────
    //  G-3 — 동의 0건 → ② 경로: 문②(엔진 진입 0) · 문③(CompleteWithToolsAsync 미도달) 각각 측정
    // ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task G3a_동의0건이면_문2_엔진진입_0()
    {
        if (!ServerAvailable()) { Assert.True(Skip("G-3a 외부반출게이트")); return; }
        using var db = ShadowedDb();
        var tid = Guid.NewGuid().ToString();
        SeedValidKey(db, tid);

        var (svc, wire, agentSpy) = BuildChatbot(db);

        await svc.AskAsync(Ask(KbMissQuestion), tid, "user-1");

        Assert.Equal(0, agentSpy.Entered);   // 문②: 키 valid 여도 엔진(RunAsync)에 못 들어간다
        Assert.Equal(0, wire.Count);
    }

    [Fact]
    public async Task G3b_동의0건이면_문3_CompleteWithToolsAsync_미도달()
    {
        if (!ServerAvailable()) { Assert.True(Skip("G-3b 외부반출게이트")); return; }
        using var db = ShadowedDb();
        var tid = Guid.NewGuid().ToString();

        // 문③ 직접 측정 — 문②를 우회하는 새 호출자를 흉내 내 AiAgentService.RunAsync 를 바로 부른다.
        var wire = new RecordingHandler();
        var claude = new AnthropicChatProvider(new FakeHttpFactory(wire), NullLogger<AnthropicChatProvider>.Instance);
        var agent = new AiAgentService(claude, new EmptyToolRegistry(), new EmptyAgentPrompt(),
            RealGate(db), NullLogger<AiAgentService>.Instance);

        var result = await agent.RunAsync("sk-gate-test-decrypted", KbMissQuestion,
            Array.Empty<ChatHistoryTurn>(),
            new ToolContext { TenantId = tid, UserId = "user-1" });

        Assert.False(result.Handled);        // 미처리 반환(호출부 폴백)
        Assert.Equal(0, wire.Count);         // 외부 왕복(CompleteWithToolsAsync) 미도달
    }

    // ─────────────────────────────────────────────────────────────
    //  G-4 — X-2 실측: 매출 수치 든 history + 공급자 openai/gemini + ① 질문 → 외부 호출 0
    // ─────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("openai")]
    [InlineData("google")]
    public async Task G4_X2_매출수치_history가_타사로도_안_나간다(string provider)
    {
        if (!ServerAvailable()) { Assert.True(Skip("G-4 외부반출게이트")); return; }
        using var db = ShadowedDb();
        var tid = Guid.NewGuid().ToString();
        SeedValidKey(db, tid, provider);

        var (svc, wire, _) = BuildChatbot(db);

        // X-2 재료: ②가 만든 답(매출·이익 수치)이 history 에 실린 상태.
        var answer = await svc.AskAsync(
            Ask(KbMissQuestion,
                ("user", "이번 달 수익 분석해줘"),
                ("assistant", "10월 매출 83,450,000원 · 이익 12,300,000원 · 이익률 14.7% (상위: 가나상사 23,000,000원)")),
            tid, "user-1");

        Assert.True(wire.Count == 0,
            $"X-2: 매출 수치 history 가 {provider} 로 {wire.Count}건 떠났다 → {wire.Hosts}");
        Assert.NotEqual("", answer.Answer);
    }

    // ─────────────────────────────────────────────────────────────
    //  G-5 — 대조군(재개 경로 생존): 동의 기록 1건 INSERT → ①·② 호출이 가짜 핸들러에 도달
    //  (죽은 문이 아님을 증명 — 이 게이트가 글자 검사가 아니라 동작임의 음성 대조군이기도 하다)
    // ─────────────────────────────────────────────────────────────
    private static void InsertConsent(IDbConnection db, string tenantId)
        => db.Execute("""
            INSERT INTO ai_export_consents (tenant_id, terms_version, agreed_by, agreed_ip)
            VALUES (@T, 'gate-test-v0', 'gate-test-user', '127.0.0.1')
            """, new { T = tenantId });

    [Fact]
    public async Task G5a_동의1건이면_문12가_다시_열린다()
    {
        if (!ServerAvailable()) { Assert.True(Skip("G-5a 외부반출게이트")); return; }
        using var db = ShadowedDb();
        var tid = Guid.NewGuid().ToString();
        SeedValidKey(db, tid);
        InsertConsent(db, tid);   // TEMPORARY 표 — 연결이 닫히면 함께 사라진다(공용 DB 무접촉)

        var (svc, wire, agentSpy) = BuildChatbot(db);

        var answer = await svc.AskAsync(Ask(KbMissQuestion), tid, "user-1");

        Assert.Equal(1, agentSpy.Entered);   // 문② 재개 — 엔진 진입
        Assert.True(wire.Count >= 1,         // 문① 재개 — 외부 호출이 (가짜 핸들러에) 도달
            "동의 기록이 있는데도 외부 호출 0건 — 게이트가 죽은 문이다(재개 경로 사망)");
        Assert.Contains("게이트 시험 응답", answer.Answer);
    }

    [Fact]
    public async Task G5b_동의1건이면_문3도_외부왕복에_도달한다()
    {
        if (!ServerAvailable()) { Assert.True(Skip("G-5b 외부반출게이트")); return; }
        using var db = ShadowedDb();
        var tid = Guid.NewGuid().ToString();
        InsertConsent(db, tid);

        var wire = new RecordingHandler();
        var claude = new AnthropicChatProvider(new FakeHttpFactory(wire), NullLogger<AnthropicChatProvider>.Instance);
        var agent = new AiAgentService(claude, new EmptyToolRegistry(), new EmptyAgentPrompt(),
            RealGate(db), NullLogger<AiAgentService>.Instance);

        var result = await agent.RunAsync("sk-gate-test-decrypted", "시험 명령",
            Array.Empty<ChatHistoryTurn>(),
            new ToolContext { TenantId = tid, UserId = "user-1" });

        Assert.True(result.Handled);
        Assert.True(wire.Count >= 1, "동의 기록이 있는데 CompleteWithToolsAsync 가 외부 왕복에 못 닿았다");
    }

    // ─────────────────────────────────────────────────────────────
    //  G-6 — fail-closed: 동의 표 조회 실패(표 부재 DB) → 닫힘 · 던지지 않고 경고 로그
    // ─────────────────────────────────────────────────────────────
    private sealed class WarnCountLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public int Warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning) Warnings++;
        }
    }

    [Fact]
    public async Task G6_표가_없는_DB면_닫힘이고_던지지_않는다()
    {
        if (!ServerAvailable()) { Assert.True(Skip("G-6 외부반출게이트")); return; }
        // information_schema 에는 ai_export_consents 가 없다(어느 환경이든 확정) — 표 부재 조회 실패 경로.
        using var db = new MySqlConnection(ConnString("information_schema"));
        db.Open();
        var log = new WarnCountLogger<ExternalAiGate>();
        var gate = new ExternalAiGate(db, log);

        var open = await gate.IsOpenAsync(Guid.NewGuid().ToString());

        Assert.False(open);              // fail-closed — 조회 실패는 닫힘이다
        Assert.Equal(1, log.Warnings);   // 조용히 닫히지 않는다(#15 — 경고 한 줄)
    }

    [Fact]
    public async Task G6b_테넌트_식별이_비면_닫힘()
    {
        if (!ServerAvailable()) { Assert.True(Skip("G-6b 외부반출게이트")); return; }
        using var db = ShadowedDb();
        var gate = RealGate(db);

        Assert.False(await gate.IsOpenAsync(""));
        Assert.False(await gate.IsOpenAsync("   "));
    }

    // ─────────────────────────────────────────────────────────────
    //  G-7 — 장부: G-5 상태에서 외부 호출 1건 → ai_usage_logs.charge_mode='byok'
    // ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task G7_외부호출_1건의_장부는_byok_로_적힌다()
    {
        if (!ServerAvailable()) { Assert.True(Skip("G-7 외부반출게이트")); return; }
        using var db = ShadowedDb();
        var tid = Guid.NewGuid().ToString();
        SeedValidKey(db, tid);
        InsertConsent(db, tid);

        var (svc, wire, _) = BuildChatbot(db);

        await svc.AskAsync(Ask(KbMissQuestion), tid, "user-1");

        Assert.True(wire.Count >= 1, "전제 불성립 — 외부 호출이 떠나지 않았다(G-5 참조)");
        var row = db.QuerySingle<(string ChargeMode, string Provider)>("""
            SELECT charge_mode AS ChargeMode, ai_provider AS Provider
            FROM ai_usage_logs WHERE tenant_id = @T ORDER BY usage_id DESC LIMIT 1
            """, new { T = tid });
        // 종전엔 'hitpan_pool' 하드코딩 — 고객 자기 키 호출이 본사 과금으로 적혔다([1-V] §3).
        Assert.Equal("byok", row.ChargeMode);
        Assert.Equal("anthropic", row.Provider);
    }
}
