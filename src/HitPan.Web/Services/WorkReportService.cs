using System.Net.Http.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>
/// 업무보고서 API 클라이언트. 작(2026-08-13) 그룹웨어 단계3.
/// </summary>
/// <remarks>
/// ⚠️ 서버의 <c>api/work-reports</c> 와 짝이다. 현황 리포트(<c>api/reports</c>)와 다른 것이다.
/// </remarks>
public sealed class WorkReportService(HttpClient http, ILogger<WorkReportService> logger)
{
    /// <summary>보고서 목록.</summary>
    public async Task<List<WorkReportListModel>> GetListAsync(
        string? reportType = null, DateTime? from = null, DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(reportType))
            {
                query.Add($"reportType={Uri.EscapeDataString(reportType)}");
            }
            if (from is not null)
            {
                query.Add($"from={from:yyyy-MM-dd}");
            }
            if (to is not null)
            {
                query.Add($"to={to:yyyy-MM-dd}");
            }

            var url = "api/work-reports" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

            return await http.GetFromJsonAsync<List<WorkReportListModel>>(url, ct).ConfigureAwait(false)
                   ?? new List<WorkReportListModel>();
        }
        catch (Exception ex)
        {
            // 🔴 빈 목록을 돌려주면 화면에서 "서버 오류" 와 "보고서 없음" 이 똑같아 보인다.
            //    최소한 로그에는 남긴다(헌법 #15).
            logger.LogWarning(ex, "보고서 목록 조회 실패 (reportType={ReportType})", reportType);
            return new List<WorkReportListModel>();
        }
    }

    /// <summary>보고서 상세.</summary>
    public async Task<WorkReportDetailModel?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<WorkReportDetailModel>(
                $"api/work-reports/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "보고서 조회 실패 (id={ReportId})", id);
            return null;
        }
    }

    /// <summary>
    /// 보고서를 저장한다. <paramref name="id"/> 가 비면 새로 만들고, 있으면 고친다.
    /// </summary>
    /// <returns>
    /// 성공 여부와 실패 사유.
    /// 🔴 사유를 함께 돌려주는 이유 — "결재 중이라 못 고친다" 같은 것은
    /// 서버만 아는 사실인데, 그냥 "실패" 라고만 하면 고객이 계속 다시 눌러 본다.
    /// </returns>
    public async Task<WorkReportSaveOutcome> SaveAsync(
        string? id, SaveWorkReportModel model, CancellationToken ct = default)
    {
        try
        {
            HttpResponseMessage res;

            if (string.IsNullOrWhiteSpace(id))
            {
                res = await http.PostAsJsonAsync("api/work-reports", model, ct).ConfigureAwait(false);
            }
            else
            {
                res = await http.PutAsJsonAsync(
                    $"api/work-reports/{Uri.EscapeDataString(id)}", model, ct).ConfigureAwait(false);
            }

            using (res)
            {
                if (!res.IsSuccessStatusCode)
                {
                    var message = await ReadMessageAsync(res, ct).ConfigureAwait(false);
                    logger.LogWarning("보고서 저장 실패 (id={ReportId}, status={Status})", id, res.StatusCode);
                    return new WorkReportSaveOutcome { Ok = false, Message = message };
                }

                var body = await res.Content
                    .ReadFromJsonAsync<SaveReportResponse>(cancellationToken: ct)
                    .ConfigureAwait(false);

                return new WorkReportSaveOutcome
                {
                    Ok = true,
                    ReportId = string.IsNullOrWhiteSpace(id) ? body?.ReportId : id,
                    ApprovalCreated = body?.ApprovalCreated ?? false,
                    ApprovalSkipReason = body?.ApprovalSkipReason
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "보고서 저장 실패 (id={ReportId})", id);
            return new WorkReportSaveOutcome
            {
                Ok = false,
                Message = "저장하지 못했습니다. 잠시 후 다시 시도해 주세요."
            };
        }
    }

    /// <summary>작성중 보고서를 결재에 올린다.</summary>
    public async Task<WorkReportSaveOutcome> SubmitAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(
                $"api/work-reports/{Uri.EscapeDataString(id)}/submit",
                new { }, ct).ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content
                    .ReadFromJsonAsync<SaveReportResponse>(cancellationToken: ct)
                    .ConfigureAwait(false);

                return new WorkReportSaveOutcome
                {
                    Ok = true,
                    ReportId = id,
                    ApprovalCreated = body?.ApprovalCreated ?? false,
                    ApprovalSkipReason = body?.ApprovalSkipReason
                };
            }

            var message = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("보고서 상신 실패 (id={ReportId}, status={Status})", id, res.StatusCode);
            return new WorkReportSaveOutcome { Ok = false, Message = message };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "보고서 상신 실패 (id={ReportId})", id);
            return new WorkReportSaveOutcome
            {
                Ok = false,
                Message = "결재에 올리지 못했습니다. 잠시 후 다시 시도해 주세요."
            };
        }
    }

    /// <summary>작성중 보고서를 지운다.</summary>
    public async Task<(bool Ok, string? Message)> DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.DeleteAsync(
                $"api/work-reports/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var message = await ReadMessageAsync(res, ct).ConfigureAwait(false);
            logger.LogWarning("보고서 삭제 실패 (id={ReportId}, status={Status})", id, res.StatusCode);
            return (false, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "보고서 삭제 실패 (id={ReportId})", id);
            return (false, "삭제하지 못했습니다. 잠시 후 다시 시도해 주세요.");
        }
    }

    /// <summary>서버가 보낸 안내 문구를 꺼낸다. 없으면 기본 문구.</summary>
    private static async Task<string> ReadMessageAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            var body = await res.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body?.Message)
                ? "처리하지 못했습니다."
                : body.Message;
        }
        catch
        {
            // 본문이 JSON 이 아닐 수 있다(403 등). 그건 오류가 아니라 정상 경로다.
            return "처리하지 못했습니다.";
        }
    }

    /// <summary>
    /// 저장·상신 응답. <b>결재가 실제로 올라갔는지</b> 를 함께 받는다.
    /// </summary>
    /// <remarks>
    /// 🔴 봉합 (2026-08-13, 검증 P0-1). 서버가 <c>approvalCreated=false</c> 와 이유를 준다.
    /// ⚠️ 타입은 API 응답 그대로여야 한다 — 단계0 에서 <c>decimal</c> vs <c>decimal?</c> 하나로
    /// 목록 전체가 빈 화면이 됐다(JsonException 이 catch 에 먹힘). 여기는 전부 nullable 로 받는다.
    /// </remarks>
    private sealed class SaveReportResponse
    {
        public string? ReportId { get; set; }
        public bool? ApprovalCreated { get; set; }
        public string? ApprovalSkipReason { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Message { get; set; }
    }
}
