using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Fax;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 팩스 발송 서비스 (사장님 오더 2026-08-21 — "업체팩스번호: 실제 팩스전송").
///
/// 구조는 EmailService 를 그대로 미러링한다. 검증된 패턴을 재사용하고 새로 발명하지 않는다.
/// 다른 점은 하나 — 실제 송출을 IFaxProvider 에 위임한다는 것. 벤더 교체점이다.
///
/// §#3 fax_send_history INSERT ONLY / §#5 API키 AES암호화
/// §#18 팩스 계정은 고객사 본인 것만 (본사 대리송출 = 업무데이터 본사 경유 = 위반)
/// §#23 Mock 은 성공을 위장하지 않는다 — 거짓봉합 방지
/// </summary>
public sealed class FaxService : IFaxService
{
    private readonly IDbConnection _db;
    private readonly IPasswordEncryptor _enc;
    private readonly IPdfRenderService _pdf;
    private readonly IEnumerable<IFaxProvider> _providers;
    private readonly ILogger<FaxService> _logger;

    public FaxService(
        IDbConnection db,
        IPasswordEncryptor enc,
        IPdfRenderService pdf,
        IEnumerable<IFaxProvider> providers,
        ILogger<FaxService> logger)
    {
        _db = db; _enc = enc; _pdf = pdf; _providers = providers; _logger = logger;
    }

    // ─── 설정 ───────────────────────────────────────────
    public async Task<FaxSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = """
            SELECT provider AS Provider,
                   api_endpoint AS ApiEndpoint,
                   (api_key_enc    IS NOT NULL AND LENGTH(api_key_enc) > 0)    AS HasApiKey,
                   (api_secret_enc IS NOT NULL AND LENGTH(api_secret_enc) > 0) AS HasApiSecret,
                   sender_fax_no AS SenderFaxNo,
                   sender_name   AS SenderName,
                   is_active     AS IsActive,
                   last_test_at  AS LastTestAt,
                   last_test_result AS LastTestResult,
                   last_test_error  AS LastTestError
            FROM fax_settings WHERE tenant_id = @TenantId
            """;
        var row = await _db.QuerySingleOrDefaultAsync<FaxSettingsDto>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct))
            .ConfigureAwait(false);

        row ??= new FaxSettingsDto();

        // 실송출 가능 여부는 설정값이 아니라 **실제 등록된 공급자 구현체**로 판정한다.
        // 설정 테이블에 벤더코드만 적어놓고 구현체가 없으면 송출은 안 된다 — 그 상태를 정직하게 내려보낸다.
        row.CanSendReal = ResolveProvider(row.Provider).CanSendReal;
        return row;
    }

    public async Task UpdateSettingsAsync(string tenantId, UpdateFaxSettingsRequest req, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(req.Provider))
            throw new ArgumentException("팩스 공급자를 선택하세요.");

        // 키: null = 기존 유지 / 값 = 교체 (EmailService.UpdateSettingsAsync 와 동일 규약)
        byte[]? keyEnc = null;
        bool replaceKey = req.ApiKey is not null;
        if (replaceKey && !string.IsNullOrEmpty(req.ApiKey))
            keyEnc = _enc.Encrypt(req.ApiKey);

        byte[]? secretEnc = null;
        bool replaceSecret = req.ApiSecret is not null;
        if (replaceSecret && !string.IsNullOrEmpty(req.ApiSecret))
            secretEnc = _enc.Encrypt(req.ApiSecret);

        const string sql = """
            INSERT INTO fax_settings
              (tenant_id, provider, api_endpoint, api_key_enc, api_secret_enc,
               sender_fax_no, sender_name, is_active)
            VALUES
              (@TenantId, @Provider, @Endpoint, @KeyEnc, @SecretEnc,
               @SenderNo, @SenderName, @IsActive)
            ON DUPLICATE KEY UPDATE
              provider       = @Provider,
              api_endpoint   = @Endpoint,
              api_key_enc    = IF(@ReplaceKey,    @KeyEnc,    api_key_enc),
              api_secret_enc = IF(@ReplaceSecret, @SecretEnc, api_secret_enc),
              sender_fax_no  = @SenderNo,
              sender_name    = @SenderName,
              is_active      = @IsActive
            """;
        await _db.ExecuteAsync(new CommandDefinition(sql, new
        {
            TenantId = tenantId,
            Provider = req.Provider,
            Endpoint = req.ApiEndpoint,
            KeyEnc = keyEnc,
            SecretEnc = secretEnc,
            ReplaceKey = replaceKey,
            ReplaceSecret = replaceSecret,
            SenderNo = req.SenderFaxNo,
            SenderName = req.SenderName,
            IsActive = req.IsActive
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<TestFaxResponse> TestConnectionAsync(string tenantId, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(tenantId, ct).ConfigureAwait(false);
        var provider = ResolveProvider(cfg.Provider);

        var result = await provider.TestAsync(new FaxProviderRequest
        {
            TenantId = tenantId,
            SenderFaxNo = cfg.SenderFaxNo,
            SenderName = cfg.SenderName,
            ApiEndpoint = cfg.ApiEndpoint,
            ApiKey = cfg.ApiKey,
            ApiSecret = cfg.ApiSecret
        }, ct).ConfigureAwait(false);

        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await _db.ExecuteAsync(new CommandDefinition("""
            UPDATE fax_settings
               SET last_test_at = @Now,
                   last_test_result = @Res,
                   last_test_error = @Err
             WHERE tenant_id = @TenantId
            """, new
        {
            TenantId = tenantId,
            Now = DateTime.Now,
            Res = result.Success ? "success" : "failed",
            Err = result.Error
        }, cancellationToken: ct)).ConfigureAwait(false);

        return new TestFaxResponse
        {
            Success = result.Success,
            Error = result.Error,
            IsMock = result.IsMock
        };
    }

    // ─── 발송 ───────────────────────────────────────────
    public async Task<SendFaxResponse> SendDocumentAsync(
        string tenantId, string? userId, SendFaxRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.RecipientFaxNo))
            return new SendFaxResponse { Success = false, Error = "받는 팩스번호가 비어 있습니다." };

        var cfg = await LoadConfigAsync(tenantId, ct).ConfigureAwait(false);
        var provider = ResolveProvider(cfg.Provider);
        var faxId = Guid.NewGuid().ToString();

        // 1) 문서 PDF — 이메일과 동일하게 IPdfRenderService 재사용
        byte[] pdfBytes = Array.Empty<byte>();
        string fileName = $"{req.DocumentNo}.pdf";
        string? renderError = null;
        if (!string.IsNullOrWhiteSpace(req.DocumentId))
        {
            try
            {
                var (bytes, fname) = await _pdf.RenderDocumentAsync(tenantId, req.DocumentType, req.DocumentId, ct)
                    .ConfigureAwait(false);
                pdfBytes = bytes;
                fileName = fname;
            }
            catch (Exception ex)
            {
                // 이메일은 첨부 없이도 본문이 남지만, 팩스는 문서가 곧 내용이다.
                // 렌더 실패 시 보낼 것이 없으므로 중단한다 (빈 팩스 송출 방지).
                _logger.LogError(ex, "[Fax] 문서 렌더 실패 tenant={Tenant} doc={DocNo}", tenantId, req.DocumentNo);
                renderError = $"문서를 만들지 못했습니다: {ex.Message}";
            }
        }

        FaxProviderResult result;
        if (renderError is not null)
        {
            result = new FaxProviderResult { Success = false, Error = renderError, IsMock = provider.IsMockProvider() };
        }
        else if (pdfBytes.Length == 0)
        {
            result = new FaxProviderResult
            {
                Success = false,
                Error = "보낼 문서가 비어 있습니다.",
                IsMock = provider.IsMockProvider()
            };
        }
        else
        {
            // 2) 공급자에게 위임 — 실제 송출은 여기서만 일어난다
            try
            {
                result = await provider.SendAsync(new FaxProviderRequest
                {
                    TenantId = tenantId,
                    RecipientFaxNo = req.RecipientFaxNo,
                    SenderFaxNo = cfg.SenderFaxNo,
                    SenderName = cfg.SenderName,
                    DocumentBytes = pdfBytes,
                    FileName = fileName,
                    ApiEndpoint = cfg.ApiEndpoint,
                    ApiKey = cfg.ApiKey,
                    ApiSecret = cfg.ApiSecret
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Fax] 송출 실패 tenant={Tenant} doc={DocNo}", tenantId, req.DocumentNo);
                result = new FaxProviderResult { Success = false, Error = ex.Message };
            }
        }

        // 3) 이력 INSERT (§#3 INSERT ONLY) — 실패·Mock 도 남긴다. 누른 사실은 기록되어야 한다.
        var status = result.IsMock ? "mock" : (result.Success ? "sent" : "failed");

        await EnsureOpenAsync(ct).ConfigureAwait(false);
        await _db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fax_send_history
              (fax_id, tenant_id, sent_at, sent_by_user, document_type, document_no, document_id,
               partner_id, recipient_fax_no, recipient_name, page_count, provider, provider_job_id,
               status, error_message, provider_response)
            VALUES
              (@Id, @TenantId, @Now, @UserId, @DocType, @DocNo, @DocId,
               @PartnerId, @Recipient, @RecipientName, @Pages, @Provider, @JobId,
               @Status, @Err, @Resp)
            """, new
        {
            Id = faxId,
            TenantId = tenantId,
            Now = DateTime.Now,
            UserId = userId,
            DocType = req.DocumentType,
            DocNo = req.DocumentNo,
            DocId = req.DocumentId,
            PartnerId = req.PartnerId,
            Recipient = req.RecipientFaxNo,
            RecipientName = req.RecipientName,
            Pages = result.PageCount,
            Provider = provider.ProviderCode,
            JobId = result.JobId,
            Status = status,
            Err = result.Error,
            Resp = result.RawResponse
        }, cancellationToken: ct)).ConfigureAwait(false);

        return new SendFaxResponse
        {
            Success = result.Success,
            FaxId = faxId,
            ProviderJobId = result.JobId,
            Error = result.Error,
            IsMock = result.IsMock,
            Notice = result.IsMock
                ? "팩스 공급자가 설정되지 않아 실제로 전송되지 않았습니다."
                : null
        };
    }

    // ─── 이력 ───────────────────────────────────────────
    public async Task<List<FaxHistoryDto>> GetHistoryAsync(
        string tenantId, string? documentType, int limit = 100, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        if (limit is < 1 or > 1000) limit = 100;

        var sql = """
            SELECT fax_id AS FaxId, sent_at AS SentAt, document_type AS DocumentType,
                   document_no AS DocumentNo, partner_id AS PartnerId,
                   recipient_fax_no AS RecipientFaxNo, recipient_name AS RecipientName,
                   page_count AS PageCount, provider AS Provider,
                   status AS Status, error_message AS ErrorMessage
              FROM fax_send_history
             WHERE tenant_id = @TenantId
            """;
        if (!string.IsNullOrWhiteSpace(documentType))
            sql += " AND document_type = @DocType";
        sql += " ORDER BY sent_at DESC LIMIT @Limit";

        var rows = await _db.QueryAsync<FaxHistoryDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, DocType = documentType, Limit = limit }, cancellationToken: ct))
            .ConfigureAwait(false);
        return rows.ToList();
    }

    // ─── 내부 ───────────────────────────────────────────

    /// <summary>
    /// 설정된 공급자 코드에 맞는 구현체를 고른다.
    /// 못 찾으면 Mock 으로 떨어진다 — 없는 벤더를 설정해두고 송출된 줄 아는 사고를 막는다.
    /// </summary>
    private IFaxProvider ResolveProvider(string? providerCode)
    {
        var code = string.IsNullOrWhiteSpace(providerCode) ? "mock" : providerCode.Trim();
        var found = _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderCode, code, StringComparison.OrdinalIgnoreCase));

        if (found is null)
        {
            _logger.LogWarning(
                "[Fax] 공급자 구현체 없음 — code={Code}. Mock 으로 대체하며 실제 송출되지 않는다.", code);
            return _providers.First(p => p.ProviderCode == "mock");
        }
        return found;
    }

    private sealed record FaxConfig(
        string Provider, string? ApiEndpoint, string? ApiKey, string? ApiSecret,
        string? SenderFaxNo, string? SenderName, bool IsActive);

    private async Task<FaxConfig> LoadConfigAsync(string tenantId, CancellationToken ct)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        var row = await _db.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition("""
            SELECT provider, api_endpoint, api_key_enc, api_secret_enc,
                   sender_fax_no, sender_name, is_active
              FROM fax_settings WHERE tenant_id = @TenantId
            """, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        if (row is null)
            return new FaxConfig("mock", null, null, null, null, null, false);

        string? key = null, secret = null;
        try
        {
            var keyBytes = (byte[]?)row.api_key_enc;
            if (keyBytes is { Length: > 0 }) key = _enc.Decrypt(keyBytes);
            var secBytes = (byte[]?)row.api_secret_enc;
            if (secBytes is { Length: > 0 }) secret = _enc.Decrypt(secBytes);
        }
        catch (Exception ex)
        {
            // 복호화 실패는 조용히 넘기면 안 된다 (§#15). 키 없이 진행 → 송출 실패로 드러난다.
            _logger.LogError(ex, "[Fax] 자격증명 복호화 실패 tenant={Tenant}", tenantId);
        }

        return new FaxConfig(
            (string?)row.provider ?? "mock",
            (string?)row.api_endpoint,
            key, secret,
            (string?)row.sender_fax_no,
            (string?)row.sender_name,
            Convert.ToBoolean(row.is_active));
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State != ConnectionState.Open && _db is DbConnection dbc)
            await dbc.OpenAsync(ct).ConfigureAwait(false);
    }
}

internal static class FaxProviderExtensions
{
    /// <summary>공급자가 Mock 인지 — 실송출 불가 상태를 한 곳에서 판정한다.</summary>
    public static bool IsMockProvider(this IFaxProvider p) => !p.CanSendReal;
}
