using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 휴직 클라이언트. 작(2026-08-13) 그룹웨어 단계6.
/// </summary>
/// <remarks>
/// 🔴 실패는 <c>null</c> 로 돌린다. 빈 목록이 아니다 —
/// 실패를 빈 목록으로 뭉개면 화면이 <b>"휴직자가 없다"</b> 로 보여준다.
/// </remarks>
public sealed class AbsenceService(HttpClient http, ILogger<AbsenceService> logger)
{
    /// <summary>목록. 관리자는 전원, 일반 직원은 본인 것만(서버가 정한다).</summary>
    public async Task<List<AbsenceModel>?> GetListAsync(string? status = null,
        string? employeeId = null, DateTime? from = null, DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            var q = new List<string>();
            if (!string.IsNullOrWhiteSpace(status)) q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrWhiteSpace(employeeId)) q.Add($"employeeId={Uri.EscapeDataString(employeeId)}");
            if (from is { } f) q.Add($"from={f:yyyy-MM-dd}");
            if (to is { } t) q.Add($"to={t:yyyy-MM-dd}");

            var url = "api/absence" + (q.Count > 0 ? "?" + string.Join("&", q) : "");

            return await http.GetFromJsonAsync<List<AbsenceModel>>(url, ct).ConfigureAwait(false)
                   ?? new List<AbsenceModel>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "휴직 목록 조회 실패 (status={Status})", status);
            return null;
        }
    }

    /// <summary>한 건.</summary>
    public async Task<AbsenceModel?> GetAsync(string absenceId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<AbsenceModel>($"api/absence/{absenceId}", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "휴직 조회 실패 (id={Id})", absenceId);
            return null;
        }
    }

    /// <summary>저장(신규·수정).</summary>
    public async Task<(bool Ok, SaveAbsenceResultModel? Result, string? Message)> SaveAsync(
        SaveAbsenceModel request, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync("api/absence", request, ct).ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                var result = await res.Content
                    .ReadFromJsonAsync<SaveAbsenceResultModel>(cancellationToken: ct)
                    .ConfigureAwait(false);
                return (true, result, null);
            }

            var message = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("휴직 저장 실패 (empId={EmpId}, status={Status})",
                request.EmployeeId, (int)res.StatusCode);
            return (false, null, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "휴직 저장 중 오류 (empId={EmpId})", request.EmployeeId);
            return (false, null, "휴직을 저장하지 못했습니다. 잠시 후 다시 시도해주세요.");
        }
    }

    /// <summary>결재에 올린다.</summary>
    public async Task<(bool Ok, SaveAbsenceResultModel? Result, string? Message)> SubmitAsync(
        string absenceId, CancellationToken ct = default)
        => await PostAsync<SaveAbsenceResultModel>($"api/absence/{absenceId}/submit", null, ct)
            .ConfigureAwait(false);

    /// <summary>승인.</summary>
    public async Task<(bool Ok, string? Message)> ApproveAsync(string absenceId,
        CancellationToken ct = default)
    {
        var (ok, _, msg) = await PostAsync<object>($"api/absence/{absenceId}/approve", null, ct)
            .ConfigureAwait(false);
        return (ok, msg);
    }

    /// <summary>반려.</summary>
    public async Task<(bool Ok, string? Message)> RejectAsync(string absenceId, string? reason,
        CancellationToken ct = default)
    {
        var (ok, _, msg) = await PostAsync<object>($"api/absence/{absenceId}/reject",
            new { Reason = reason }, ct).ConfigureAwait(false);
        return (ok, msg);
    }

    /// <summary>취소(신청 철회).</summary>
    public async Task<(bool Ok, string? Message)> CancelAsync(string absenceId,
        CancellationToken ct = default)
    {
        var (ok, _, msg) = await PostAsync<object>($"api/absence/{absenceId}/cancel", null, ct)
            .ConfigureAwait(false);
        return (ok, msg);
    }

    /// <summary>복직 처리.</summary>
    public async Task<(bool Ok, string? Message)> ReturnAsync(ReturnFromAbsenceModel request,
        CancellationToken ct = default)
    {
        var (ok, _, msg) = await PostAsync<object>("api/absence/return", request, ct).ConfigureAwait(false);
        return (ok, msg);
    }

    /// <summary>시작일이 된 건을 '휴직중' 으로 맞춘다.</summary>
    public async Task<(bool Ok, string? Message)> SyncAsync(CancellationToken ct = default)
    {
        var (ok, _, msg) = await PostAsync<object>("api/absence/sync", null, ct).ConfigureAwait(false);
        return (ok, msg);
    }

    // ───────────────────────────────────────────────────────────────

    private async Task<(bool Ok, T? Result, string? Message)> PostAsync<T>(string url, object? body,
        CancellationToken ct) where T : class
    {
        try
        {
            using var res = body is null
                ? await http.PostAsync(url, null, ct).ConfigureAwait(false)
                : await http.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                T? parsed = null;
                if (typeof(T) != typeof(object))
                {
                    try
                    {
                        parsed = await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // 본문이 없거나 형태가 달라도 '성공' 자체는 유효하다.
                        logger.LogWarning(ex, "휴직 응답 본문 해석 실패 (url={Url})", url);
                    }
                }
                return (true, parsed, null);
            }

            var message = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("휴직 요청 실패 (url={Url}, status={Status})", url, (int)res.StatusCode);
            return (false, null, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "휴직 요청 중 오류 (url={Url})", url);
            return (false, null, "처리하지 못했습니다. 잠시 후 다시 시도해주세요.");
        }
    }

    /// <summary>
    /// 서버가 보낸 이유를 그대로 꺼낸다.
    /// "사유를 남겨야 합니다" 같은 말이 직원에게 닿아야 다음 행동을 안다.
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
            logger.LogWarning(ex, "휴직 오류 응답 해석 실패 (status={Status})", (int)res.StatusCode);
            return null;
        }
    }

    private sealed class MessageBody
    {
        public string? Message { get; set; }
    }
}
