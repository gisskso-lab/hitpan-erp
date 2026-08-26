using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 급여·퇴직금 클라이언트. 작(2026-08-13) 그룹웨어 단계8.
/// </summary>
/// <remarks>
/// 🔴 실패는 <c>null</c> 로 돌린다. 빈 목록이 아니다 —
/// 실패를 빈 목록으로 뭉개면 화면이 <b>"급여가 없다"</b> 로 보여준다.
/// 급여는 있는데 없다고 보이면 담당자가 다시 만들어 <b>이중 지급</b>이 난다.
///
/// ⚠️ 권한이 없으면 서버가 403 을 준다. 그것도 실패로 다뤄 화면이 사유를 보여준다.
/// </remarks>
public sealed class PayrollService(HttpClient http, ILogger<PayrollService> logger)
{
    // ── 급여 명세 ──

    public async Task<List<PayrollSlipModel>?> GetSlipsAsync(int year, int month,
        string? employeeId = null, CancellationToken ct = default)
    {
        try
        {
            var url = $"api/payroll/slips?year={year}&month={month}";
            if (!string.IsNullOrWhiteSpace(employeeId))
                url += $"&employeeId={Uri.EscapeDataString(employeeId)}";

            return await http.GetFromJsonAsync<List<PayrollSlipModel>>(url, ct).ConfigureAwait(false)
                   ?? new List<PayrollSlipModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "급여 명세 조회 실패 ({Year}-{Month})", year, month);
            return null;
        }
    }

    public async Task<PayrollSlipModel?> GetSlipAsync(string slipId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<PayrollSlipModel>($"api/payroll/slips/{slipId}", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "급여 명세 조회 실패 (id={Id})", slipId);
            return null;
        }
    }

    /// <summary>
    /// 그 달 급여를 만들 때 참고할 것들. 🔴 자동으로 채우지 않고 보여만 준다.
    /// </summary>
    public async Task<List<PayrollContextModel>?> GetContextAsync(int year, int month,
        CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<PayrollContextModel>>(
                       $"api/payroll/context?year={year}&month={month}", ct).ConfigureAwait(false)
                   ?? new List<PayrollContextModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "급여 참고자료 조회 실패 ({Year}-{Month})", year, month);
            return null;
        }
    }

    public async Task<(bool Ok, string? SlipId, string? Message)> SaveSlipAsync(
        SavePayrollSlipModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/payroll/slips", request, ct)
                .ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<SlipIdBody>(cancellationToken: ct)
                    .ConfigureAwait(false);
                return (true, body?.SlipId, null);
            }

            var msg = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("급여 명세 저장 실패 (status={Status})", (int)res.StatusCode);
            return (false, null, msg ?? Describe(res));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "급여 명세 저장 중 오류");
            return (false, null, "저장하지 못했습니다. 잠시 후 다시 시도해주세요.");
        }
    }

    public Task<(bool Ok, string? Message)> ConfirmSlipAsync(string slipId, CancellationToken ct = default)
        => PostAsync($"api/payroll/slips/{slipId}/confirm", null, ct);

    public Task<(bool Ok, string? Message)> MarkPaidAsync(string slipId, DateTime payDate,
        CancellationToken ct = default)
        => PostAsync($"api/payroll/slips/{slipId}/pay", new { PayDate = payDate }, ct);

    public Task<(bool Ok, string? Message)> CancelSlipAsync(string slipId, CancellationToken ct = default)
        => PostAsync($"api/payroll/slips/{slipId}/cancel", null, ct);

    // ── 퇴직금 ──

    public async Task<List<SeverancePaymentModel>?> GetSeveranceAsync(string? employeeId = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = "api/payroll/severance";
            if (!string.IsNullOrWhiteSpace(employeeId))
                url += $"?employeeId={Uri.EscapeDataString(employeeId)}";

            return await http.GetFromJsonAsync<List<SeverancePaymentModel>>(url, ct).ConfigureAwait(false)
                   ?? new List<SeverancePaymentModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "퇴직금 조회 실패");
            return null;
        }
    }

    public async Task<(bool Ok, string? Message)> SaveSeveranceAsync(SaveSeveranceModel request,
        CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/payroll/severance", request, ct)
                .ConfigureAwait(false);

            if (res.IsSuccessStatusCode) return (true, null);

            var msg = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("퇴직금 저장 실패 (status={Status})", (int)res.StatusCode);
            return (false, msg ?? Describe(res));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "퇴직금 저장 중 오류");
            return (false, "저장하지 못했습니다. 잠시 후 다시 시도해주세요.");
        }
    }

    public Task<(bool Ok, string? Message)> ConfirmSeveranceAsync(string severanceId,
        CancellationToken ct = default)
        => PostAsync($"api/payroll/severance/{severanceId}/confirm", null, ct);

    // ───────────────────────────────────────────────────────────────

    private async Task<(bool Ok, string? Message)> PostAsync(string url, object? body,
        CancellationToken ct)
    {
        try
        {
            using var res = body is null
                ? await http.PostAsync(url, null, ct).ConfigureAwait(false)
                : await http.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);

            if (res.IsSuccessStatusCode) return (true, null);

            var msg = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("급여 요청 실패 (url={Url}, status={Status})", url, (int)res.StatusCode);
            return (false, msg ?? Describe(res));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "급여 요청 중 오류 (url={Url})", url);
            return (false, "처리하지 못했습니다. 잠시 후 다시 시도해주세요.");
        }
    }

    /// <summary>
    /// 서버가 보낸 이유를 그대로 꺼낸다.
    /// "이미 있습니다" 같은 말이 담당자에게 닿아야 다음 행동을 안다.
    /// </summary>
    private async Task<string?> ReadMessageAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            var body = await res.Content.ReadFromJsonAsync<MessageBody>(cancellationToken: ct)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body?.Message) ? null : body!.Message;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "급여 오류 응답 해석 실패 (status={Status})", (int)res.StatusCode);
            return null;
        }
    }

    /// <summary>
    /// 본문이 없을 때 상태코드로 사유를 만든다.
    /// 🔴 403 은 <b>권한 없음</b>이다 — 급여는 권한 계층으로 막으므로 이 안내가 꼭 필요하다.
    /// </summary>
    private static string Describe(HttpResponseMessage res) => (int)res.StatusCode switch
    {
        401 => "로그인이 필요합니다.",
        403 => "급여를 볼 권한이 없습니다. 관리자에게 권한을 요청하세요.",
        404 => "찾을 수 없습니다.",
        _ => "처리하지 못했습니다.",
    };

    // ═══════════════════════════════════════════════════════════════
    //  급여명세서 일괄 메일발송 — 20260826작6 W6
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 발송 전 확인 명단. 🔴 실패는 <c>null</c> — <b>빈 목록이 아니다</b>.
    /// </summary>
    /// <remarks>
    /// 실패를 빈 목록으로 뭉개면 화면이 <i>"보낼 사람이 없습니다"</i> 로 보여준다.
    /// 경리는 <b>다 보냈다고 알거나, 아무도 못 받는다고 오해</b>한다. 둘 다 사고다.
    /// </remarks>
    public async Task<PayslipSendPreviewModel?> GetSendPreviewAsync(int year, int month,
        CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<PayslipSendPreviewModel>(
                $"api/payroll/slips/send-mail/preview?year={year}&month={month}", ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GetSendPreviewAsync failed {Year}-{Month}", year, month);
            return null;
        }
    }

    /// <summary>
    /// 급여명세서를 <b>일괄 발송</b>한다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>되돌릴 수 없다</b> — 화면은 반드시 사람에게 확인을 받고 부른다.
    /// ⚠️ 서버가 각 건을 <b>다시 판정</b>하므로, 여기서 보낸 목록이 그대로 나가는 것이 아니다.
    /// </remarks>
    public async Task<SendPayslipMailResultModel?> SendMailAsync(int year, int month,
        List<string> slipIds, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/payroll/slips/send-mail",
                new { year, month, slipIds }, ct);

            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("SendMailAsync failed status={Status}", (int)res.StatusCode);
                return null;
            }

            return await res.Content.ReadFromJsonAsync<SendPayslipMailResultModel>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SendMailAsync failed {Year}-{Month}", year, month);
            return null;
        }
    }

    /// <summary>
    /// 급여명세서 PDF 를 받아온다. 🔴 <b>본인 것만</b> 열린다(서버가 판정).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <c>/api/email/preview-pdf</c> 를 쓰면 <b>안 된다</b> — 거기엔 사원 확인이 없어
    /// 남의 명세서가 나간다(W5 봉합). 급여명세서는 <b>이 길로만</b> 받는다.
    /// </para>
    /// <para>
    /// 🔴 <b><c>&lt;a href&gt;</c> 로 걸면 안 된다.</b> 브라우저가 그 주소를 직접 부르면
    /// <b>로그인 토큰이 안 실려</b> 401 이 온다. 이 레포가 PDF 를 받는 방식은
    /// <b>인증된 HttpClient 로 바이트를 받아</b> 브라우저에 넘기는 것이다
    /// (<c>EmailClientService.DownloadPdfAsync</c> 와 같은 방식).
    /// </para>
    /// <para>
    /// 실패는 <c>null</c> — 화면이 사유를 보여준다. 🔴 결재 전이면 서버가 400 과 사유를 준다.
    /// </para>
    /// </remarks>
    public async Task<(byte[]? Bytes, string? Error)> DownloadSlipPdfAsync(string slipId,
        CancellationToken ct = default)
    {
        try
        {
            using var res = await http.GetAsync(
                $"api/payroll/slips/{Uri.EscapeDataString(slipId)}/pdf", ct);

            if (res.IsSuccessStatusCode)
                return (await res.Content.ReadAsByteArrayAsync(ct), null);

            // 🔴 서버가 준 사유를 그대로 전한다 — "받지 못했습니다" 로 뭉개면
            //    직원은 결재 대기 중인지 권한이 없는지 알 수 없다.
            var body = await TryReadMessageAsync(res, ct);
            return (null, body ?? Describe(res));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DownloadSlipPdfAsync failed slipId={SlipId}", slipId);
            return (null, "명세서를 받지 못했습니다.");
        }
    }

    /// <summary>응답 본문의 <c>message</c> 를 읽는다. 없으면 <c>null</c>.</summary>
    /// <remarks>
    /// ⚠️ 본문이 JSON 이 아닐 수 있다(빈 응답·HTML 오류 쪽). 그건 <b>정상 경로</b>라
    /// 삼키되, 빈 catch 로 두지 않는다 — 왜 사유를 못 읽었는지 남긴다(헌법 #15).
    /// </remarks>
    private async Task<string?> TryReadMessageAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            var body = await res.Content.ReadFromJsonAsync<MessageBody>(cancellationToken: ct);
            return string.IsNullOrWhiteSpace(body?.Message) ? null : body!.Message;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TryReadMessageAsync: 본문에서 사유를 읽지 못했다 status={Status}",
                (int)res.StatusCode);
            return null;
        }
    }

    private sealed class MessageBody
    {
        public string? Message { get; set; }
    }

    private sealed class SlipIdBody
    {
        public string? SlipId { get; set; }
    }
}
