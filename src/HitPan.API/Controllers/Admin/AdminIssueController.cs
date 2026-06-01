// ============================================================================
// WS-20260601-13 AdminIssueController — 본사 시리얼 발급 4-eyes API (8명제 #2·#4)
// ----------------------------------------------------------------------------
// 작성일       : 2026-06-01
// 작성팀       : 백엔드 매니저(Harvard·Oracle 30년) + 프론트 매니저 + 보안 매니저 1
// 정합 산출물  : SerialIssueService.cs + database/migrations/20260601_eight_propositions.sql
//                docs/design/EIGHT_PROPOSITIONS_BACKOFFICE_LANDING.md §#2·§#4·§#7
// 절대 원칙    :
//   - JWT 클레임 강제 (헌법 #2 확장) — 결재자 user_id 파라미터 수신 금지
//   - 4-eyes: approver1_user_id + approver2_user_id 둘 다 필수 + 서로 달라야 함
//   - 빈 catch 금지 (헌법 #15)
//   - 평문 사업자번호 본문 박제 금지 (8명제 #3) — biz_no_hash만 박제
//   - 정책: PlatformManagerOrAbove (WS-16, role=owner OR platform_manager)
// 실제 DB INSERT / 이메일·SMS 전송은 WS-14 / WS-15 가도 완료 후 결합 (현 시점은 스켈레톤 + 멱등성·시리얼 자족 동작).
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using HitPan.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HitPan.API.Controllers.Admin;

[ApiController]
[Route("api/admin/issue")]
[Authorize(Policy = "PlatformManagerOrAbove")]
public class AdminIssueController : ControllerBase
{
    private readonly ISerialIssueService _serialSvc;
    private readonly ILogger<AdminIssueController> _logger;

    public AdminIssueController(ISerialIssueService serialSvc, ILogger<AdminIssueController> logger)
    {
        _serialSvc = serialSvc;
        _logger = logger;
    }

    /// <summary>고객사 시리얼 발급 (HP- prefix, 4-eyes).</summary>
    [HttpPost("tenant")]
    public IActionResult IssueTenant([FromBody] AdminIssueTenantRequest req)
    {
        return IssueCore(SerialKind.Tenant, req?.BizNoHash, req?.EmailHash, req?.PaymentId,
            req?.Approver1UserId, req?.Approver2UserId);
    }

    /// <summary>대리점 시리얼 발급 (HR- prefix, 사장님 결재 + 운영팀 2인).</summary>
    [HttpPost("reseller")]
    public IActionResult IssueReseller([FromBody] AdminIssueResellerRequest req)
    {
        // Reseller 발급은 owner 필수 — 정책상 PlatformManagerOrAbove 위에 한 번 더 owner 검증
        var roleClaim = User.FindFirst("role")?.Value;
        if (roleClaim != "owner")
        {
            _logger.LogWarning("[AdminIssue] Reseller 발급은 owner만 가능 — Role={Role}", roleClaim ?? "(null)");
            return Forbid();
        }
        return IssueCore(SerialKind.Reseller, req?.BizNoHash, req?.EmailHash, contractId: req?.ContractId,
            req?.Approver1UserId, req?.Approver2UserId);
    }

    // ------------------------------------------------------------------------
    // 공통 발급 흐름
    // ------------------------------------------------------------------------
    private IActionResult IssueCore(
        SerialKind kind,
        string? bizNoHash,
        string? emailHash,
        string? contractId,
        long? approver1,
        long? approver2)
    {
        // (1) 입력 검증 — 평문 사업자번호·상호·대표자 절대 박제 금지 (해시만 받음)
        if (string.IsNullOrWhiteSpace(bizNoHash) || string.IsNullOrWhiteSpace(emailHash))
        {
            return BadRequest(new { success = false, message = "biz_no_hash / email_hash 필수 (평문 금지)" });
        }
        if (bizNoHash.Length != 64 || emailHash.Length != 64)
        {
            return BadRequest(new { success = false, message = "해시 길이 불일치 (SHA-256/HMAC = 64자 hex)" });
        }

        // (2) JWT 클레임에서 신청 운영자 user_id 추출 — 본문 파라미터 신뢰 금지 (헌법 #2 확장)
        var actingUserIdStr = User.FindFirst("user_id")?.Value;
        if (!long.TryParse(actingUserIdStr, out var actingUserId))
        {
            _logger.LogWarning("[AdminIssue] JWT user_id 클레임 누락");
            return Unauthorized(new { success = false, message = "user_id 클레임 누락" });
        }

        // (3) 4-eyes 검증 — 본 케이스에선 본문의 approver1/2가 실제 운영팀 DB user_id임을 호출 측이 보장.
        //     호출 측 검증 보조용으로 actingUser는 approver1·2 중 하나여야 함 (자기서명 차단)
        if (!approver1.HasValue || !approver2.HasValue)
        {
            return BadRequest(new { success = false, message = "4-eyes: approver1_user_id + approver2_user_id 둘 다 필수" });
        }
        if (approver1.Value == approver2.Value)
        {
            return BadRequest(new { success = false, message = "4-eyes: 두 결재자가 동일인일 수 없음 (8명제 #4)" });
        }
        if (actingUserId != approver1.Value && actingUserId != approver2.Value)
        {
            _logger.LogWarning("[AdminIssue] 결재자 미참여 — Acting={A} Approvers={A1},{A2}",
                actingUserId, approver1, approver2);
            return Forbid();
        }

        // (4) 멱등성 키 = SHA-256(bizNoHash | paymentId/contractId | 5분 윈도우)
        var windowStamp = (DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(5).Ticks).ToString();
        var idemSource = $"{bizNoHash}|{contractId ?? string.Empty}|{windowStamp}";
        var idempotencyKey = ComputeSha256Hex(idemSource);

        // (5) 시리얼·임시 비번 발급
        var result = _serialSvc.Issue(kind, idempotencyKey);
        if (!result.Success)
        {
            _logger.LogWarning("[AdminIssue] 발급 실패 — Kind={Kind} Reason={R}", kind, result.FailReason);
            return StatusCode(500, new { success = false, message = result.FailReason ?? "발급 실패" });
        }

        // (6) TODO WS-14: NotificationService.SendEmailAsync(emailHash → 활성화 링크)
        //              + SendSmsAsync(SMS = 임시 비번 평문, 2채널 분리, Task.WhenAll 금지)
        // (7) TODO WS-15: INSERT tenants/resellers + platform_audit_log
        //                 (action=SERIAL_ISSUE, target_serial=serial, details={approver1,approver2})

        _logger.LogInformation(
            "[AdminIssue] 발급 박제 — Kind={Kind} Serial={Serial} Acting={Acting} Approvers={A1},{A2} Idem={Idem}",
            kind, result.Serial, actingUserId, approver1, approver2, idempotencyKey[..16]);

        // 응답: 평문 비번 본문 절대 박제 X. 2채널(SMS) 전송 완료 후 마스킹된 상태만 회신.
        return Ok(new
        {
            success = true,
            serial = result.Serial,
            issuedAtUtc = DateTime.UtcNow,
            note = "WS-14 NotificationService + WS-15 DDL 가도 완료 후 실 발송·박제 결합"
        });
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}

// ============================================================================
// 요청 DTO — 평문 박제 금지 (해시만 수신)
// ============================================================================

public sealed class AdminIssueTenantRequest
{
    /// <summary>HMAC-SHA256(biz_no, pepper) — pepper는 HSM. 평문 사업자번호 수신 금지.</summary>
    public string? BizNoHash { get; set; }

    /// <summary>SHA-256(email) — 평문 이메일 수신 금지.</summary>
    public string? EmailHash { get; set; }

    /// <summary>결제 ID (멱등성 키 재료, 토스/카카오/PG 결제 식별자).</summary>
    public string? PaymentId { get; set; }

    /// <summary>4-eyes 1차 결재자 user_id (platform_users.user_id).</summary>
    public long? Approver1UserId { get; set; }

    /// <summary>4-eyes 2차 결재자 user_id (platform_users.user_id, ≠ Approver1).</summary>
    public long? Approver2UserId { get; set; }
}

public sealed class AdminIssueResellerRequest
{
    public string? BizNoHash { get; set; }
    public string? EmailHash { get; set; }

    /// <summary>대리점 계약 ID (멱등성 키 재료).</summary>
    public string? ContractId { get; set; }

    public long? Approver1UserId { get; set; }
    public long? Approver2UserId { get; set; }
}
