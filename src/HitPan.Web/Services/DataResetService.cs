using System.Net.Http.Json;

namespace HitPan.Web.Services;

/// <summary>
/// 모든데이터 초기화(히트판 자료 포맷) 프론트 서비스 (사장님 결재 2026-06-23).
/// 부모계정 패스워드 재입력 + 강제 백업 후 실행. §#22 본사 미수신.
/// </summary>
public sealed class DataResetService(HttpClient http)
{
    public sealed class DataResetRequestModel
    {
        public string Password { get; set; } = string.Empty;
        public string ConfirmText { get; set; } = string.Empty;
    }

    public sealed class DataResetResultModel
    {
        public bool Success { get; set; }
        public string? BackupId { get; set; }
        public int ClearedTableCount { get; set; }
        public string? Error { get; set; }
    }

    public async Task<DataResetResultModel> ResetAllAsync(DataResetRequestModel req, CancellationToken ct = default)
    {
        try
        {
            var res = await http.PostAsJsonAsync("api/data-reset", req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadFromJsonAsync<DataResetResultModel>(cancellationToken: ct).ConfigureAwait(false)
                       ?? new DataResetResultModel { Success = false, Error = "응답을 읽지 못했습니다." };
            return body;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DataResetService.ResetAllAsync] {ex.GetType().Name}: {ex.Message}");
            return new DataResetResultModel { Success = false, Error = "초기화 요청 중 통신 오류가 발생했습니다." };
        }
    }
}
