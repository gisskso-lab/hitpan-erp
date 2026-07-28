using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.ESign;
using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Security;

namespace HitPan.Application.Services;

/// <summary>
/// 전자서명 서비스 구현.
/// esign_records 테이블 INSERT 전용 (서명 기록은 불변; 무효화만 허용).
///
/// 두 가지 트랙:
/// - 간편인증 4종(kakao/toss/finance/pass): 외부 제공사 OAuth/본인확인 → Mock 모드
/// - 수동 3종(manual_upload/handwritten/admin_verified): 파일/이미지/사유 기반 관리자 기록
/// </summary>
public sealed class ESignatureService : IESignatureService
{
    private readonly IDbConnection _db;
    private readonly IAuditService _audit;
    private readonly IEncryptionService _aes;  // 전화번호/생년월일 AES-256 암호화

    // Provider 한글 라벨 (UI 표시용)
    private static readonly Dictionary<string, string> ProviderLabels = new()
    {
        ["kakao"] = "카카오톡 인증",
        ["toss"] = "토스 인증",
        ["finance"] = "금융인증서",
        ["pass"] = "휴대폰 PASS",
        ["manual_upload"] = "서면 스캔 업로드",
        ["handwritten"] = "손글씨 서명",
        ["admin_verified"] = "관리자 대리확인"
    };

    // 간편인증 4종 (외부 API 연동 대상)
    private static readonly HashSet<string> EasyAuthProviders = new()
    {
        "kakao", "toss", "finance", "pass"
    };

    // 수동 3종 (파일/이미지/사유 기반)
    private static readonly HashSet<string> ManualProviders = new()
    {
        "manual_upload", "handwritten", "admin_verified"
    };

    public ESignatureService(IDbConnection db, IAuditService audit, IEncryptionService aes)
    {
        _db = db;
        _audit = audit;
        _aes = aes;
    }

    public async Task<List<ESignRecordDto>> GetListAsync(
        string tenantId,
        string? documentType = null,
        string? documentId = null,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              esign_id        AS EsignId,
              document_type   AS DocumentType,
              document_id     AS DocumentId,
              document_title  AS DocumentTitle,
              provider        AS Provider,
              signer_name     AS SignerName,
              signed_at       AS SignedAt,
              is_void         AS IsVoid
            FROM esign_records
            WHERE tenant_id = @TenantId
              AND (@DocumentType IS NULL OR document_type = @DocumentType)
              AND (@DocumentId IS NULL OR document_id = @DocumentId)
            ORDER BY signed_at DESC
            """;

        var rows = (await _db.QueryAsync<ESignRecordDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, DocumentType = documentType, DocumentId = documentId },
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        foreach (var r in rows)
        {
            r.ProviderLabel = ProviderLabels.TryGetValue(r.Provider, out var label) ? label : r.Provider;
        }
        return rows;
    }

    public async Task<ESignRecordDto?> GetAsync(string esignId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              esign_id        AS EsignId,
              document_type   AS DocumentType,
              document_id     AS DocumentId,
              document_title  AS DocumentTitle,
              provider        AS Provider,
              signer_name     AS SignerName,
              signed_at       AS SignedAt,
              is_void         AS IsVoid
            FROM esign_records
            WHERE esign_id = @EsignId AND tenant_id = @TenantId
            """;

        var row = await _db.QueryFirstOrDefaultAsync<ESignRecordDto>(new CommandDefinition(
            sql,
            new { EsignId = esignId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (row != null)
        {
            row.ProviderLabel = ProviderLabels.TryGetValue(row.Provider, out var label) ? label : row.Provider;
        }
        return row;
    }

    public async Task<ESignResultDto> SignAsync(
        ESignRequestDto req,
        string tenantId,
        string userId,
        string ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken ct = default)
    {
        // 1) 기본 입력 검증
        if (string.IsNullOrWhiteSpace(req.DocumentType)) throw new InvalidOperationException("문서 종류(document_type)는 필수입니다.");
        if (string.IsNullOrWhiteSpace(req.DocumentId)) throw new InvalidOperationException("문서 ID(document_id)는 필수입니다.");
        if (string.IsNullOrWhiteSpace(req.SignerName)) throw new InvalidOperationException("서명자 이름(signer_name)은 필수입니다.");

        // 2) provider 검증 (7가지 중 하나)
        if (!ProviderLabels.ContainsKey(req.Provider))
        {
            throw new InvalidOperationException(
                $"지원하지 않는 서명 제공사입니다: {req.Provider}. " +
                "허용: kakao, toss, finance, pass, manual_upload, handwritten, admin_verified");
        }

        string? providerTxId = null;
        string? rawResponse = null;

        if (EasyAuthProviders.Contains(req.Provider))
        {
            // 3-A) 간편인증 4종 → Mock 본인확인
            // ⚠️ Mock 모드 — 실제 API 계약 후 Provider별 구현체로 교체 필요
            if (string.IsNullOrWhiteSpace(req.SignerPhone))
                throw new InvalidOperationException("간편인증은 서명자 휴대전화번호(signer_phone)가 필수입니다.");
            if (string.IsNullOrWhiteSpace(req.SignerBirth))
                throw new InvalidOperationException("간편인증은 서명자 생년월일(signer_birth)이 필수입니다.");

            var (success, txId, raw) = await MockVerifyAsync(req.Provider, req.SignerPhone!, req.SignerBirth!).ConfigureAwait(false);
            if (!success)
            {
                return new ESignResultDto
                {
                    EsignId = string.Empty,
                    Success = false,
                    Message = "본인확인 실패(Mock)",
                    SignedAt = DateTime.UtcNow
                };
            }
            providerTxId = txId;
            rawResponse = raw;
        }
        else if (ManualProviders.Contains(req.Provider))
        {
            // 3-B) 수동 3종 — 파일/이미지/사유 필수값 검증
            switch (req.Provider)
            {
                case "handwritten":
                    if (req.SignatureBlob is null || req.SignatureBlob.Length == 0)
                        throw new InvalidOperationException("손글씨 서명 이미지(signature_blob)가 필요합니다.");
                    break;
                case "manual_upload":
                    if (req.SignatureBlob is null || req.SignatureBlob.Length == 0)
                        throw new InvalidOperationException("서면 스캔 파일(signature_blob)이 필요합니다.");
                    break;
                case "admin_verified":
                    if (string.IsNullOrWhiteSpace(req.ManualReason))
                        throw new InvalidOperationException("관리자 대리확인은 사유(manual_reason)가 필수입니다.");
                    break;
            }
            providerTxId = $"MANUAL-{req.Provider.ToUpperInvariant()}-{Guid.NewGuid().ToString()[..8]}";
            rawResponse = $"{{\"manual\":true,\"provider\":\"{req.Provider}\",\"reason\":{JsonString(req.ManualReason)}}}";
        }

        // 4) 민감 정보 AES-256 암호화
        string? phoneEnc = string.IsNullOrWhiteSpace(req.SignerPhone) ? null : _aes.Encrypt(req.SignerPhone!);
        string? birthEnc = string.IsNullOrWhiteSpace(req.SignerBirth) ? null : _aes.Encrypt(req.SignerBirth!);

        var esignId = Guid.NewGuid().ToString();
        var signedAt = DateTime.UtcNow;

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 5) INSERT esign_records (원장처럼 불변)
        const string insertSql = """
            INSERT INTO esign_records (
              esign_id, tenant_id, user_id,
              document_type, document_id, document_title, document_hash,
              provider, provider_tx_id,
              signer_name, signer_phone_enc, signer_birth_enc,
              signed_at, ip_address, user_agent, device_id,
              signature_blob, raw_response,
              is_void, created_at)
            VALUES (
              @EsignId, @TenantId, @UserId,
              @DocumentType, @DocumentId, @DocumentTitle, @DocumentHash,
              @Provider, @ProviderTxId,
              @SignerName, @SignerPhoneEnc, @SignerBirthEnc,
              @SignedAt, @IpAddress, @UserAgent, @DeviceId,
              @SignatureBlob, @RawResponse,
              0, NOW(6))
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            insertSql,
            new
            {
                EsignId = esignId,
                TenantId = tenantId,
                UserId = userId,
                DocumentType = req.DocumentType,
                DocumentId = req.DocumentId,
                DocumentTitle = req.DocumentTitle,
                DocumentHash = req.DocumentHash,
                Provider = req.Provider,
                ProviderTxId = providerTxId,
                SignerName = req.SignerName,
                SignerPhoneEnc = phoneEnc,
                SignerBirthEnc = birthEnc,
                SignedAt = signedAt,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                DeviceId = deviceId,
                SignatureBlob = req.SignatureBlob,
                RawResponse = rawResponse
            },
            cancellationToken: ct)).ConfigureAwait(false);

        // 6) 감사로그 — 민감 정보 제외, 메타만
        var afterJson = $"{{\"document_type\":\"{req.DocumentType}\",\"document_id\":\"{req.DocumentId}\",\"provider\":\"{req.Provider}\"}}";
        await _audit.LogAsync("sign", "esign", esignId, afterJson: afterJson, ct: ct).ConfigureAwait(false);

        return new ESignResultDto
        {
            EsignId = esignId,
            Success = true,
            ProviderTxId = providerTxId,
            Message = ProviderLabels[req.Provider] + " 서명 기록 완료",
            SignedAt = signedAt
        };
    }

    public async Task VoidAsync(
        string esignId,
        string tenantId,
        string userId,
        string reason,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("무효화 사유(reason)는 필수입니다.");

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            UPDATE esign_records
            SET is_void = 1,
                void_reason = @Reason,
                voided_at = NOW(6),
                voided_by = @UserId
            WHERE esign_id = @EsignId
              AND tenant_id = @TenantId
              AND is_void = 0
            """;

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { EsignId = esignId, TenantId = tenantId, UserId = userId, Reason = reason },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
            throw new InvalidOperationException("무효화할 서명 기록을 찾지 못했거나 이미 무효화된 상태입니다.");

        await _audit.LogAsync(
            "void",
            "esign",
            esignId,
            afterJson: "{\"is_void\":true}",
            reason: reason,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// ⚠️ Mock 모드 — 실제 API 계약 후 Provider별 구현체로 교체 필요.
    /// 4/21 현재: kakao/toss/finance/pass 모두 외부 API 키 미발급 → 항상 성공 반환.
    /// 실제 연동 시 이 메서드를 각 Provider 전용 클래스(IKakaoAuthProvider 등)로 분리.
    /// </summary>
    private static Task<(bool success, string txId, string rawResponse)> MockVerifyAsync(
        string provider, string phone, string birth)
    {
        // Mock: 항상 성공, 가짜 txId 발급
        var txId = $"MOCK-{provider.ToUpperInvariant()}-{Guid.NewGuid().ToString()[..8]}";
        var raw = $"{{\"mock\":true,\"provider\":\"{provider}\",\"verified_at\":\"{DateTime.UtcNow:O}\"}}";
        return Task.FromResult((true, txId, raw));
    }

    /// <summary>null-safe JSON 문자열 직렬화 유틸.</summary>
    private static string JsonString(string? value)
    {
        if (value is null) return "null";
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbConn)
        {
            await dbConn.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        _db.Open();
    }
}
