using System.Net.Http.Json;
using HitPan.Web.Models;
using HitPan.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HitPan.Web.Pages.Finance;

/// <summary>
/// 정합성 검사 화면 — 서버 검사 15종 결과를 사람이 읽을 수 있게 보여준다.
/// </summary>
/// <remarks>
/// 🔴 <b>20260827작8 W4.</b> 서버 <c>GET api/finance/integrity-check</c> 는 진작부터
/// 15개 검사를 돌려주고 있었는데 <b>Web 전체에서 호출이 0건</b>이었다.
/// 작6 에서 사슬 검사 7종을 추가하고도 화면이 없어 사장님께 아무것도 보이지 않았다
/// (1.3.28 실측 반려: <i>"회계검산 화면 그대로임"</i>).
/// <para>
/// 사장님 요구: <i>"데이터가 맞아야 하고, 혹시나 데이터가 틀릴 경우
/// <b>빠르게 틀린 데이터를 발견</b>할 수 있어야 해"</i> — 그래서 이 화면은
/// <b>이상 항목을 맨 위로</b> 올리고, 몇 건이 어떻게 틀렸는지(<c>Detail</c>)를 그대로 쓴다.
/// </para>
/// </remarks>
public partial class IntegrityCheckPage : ComponentBase
{
    private IntegrityReportModel? _report;
    private bool _loading;
    private int _failCount;
    private int _warnCount;

    /// <summary>
    /// 🔴 이상(FAIL) → 확인필요(WARN) → 정상(OK) 순. 분류는 그 안에서 묶는다.
    /// </summary>
    /// <remarks>
    /// 15줄을 서버 순서대로 뿌리면 틀린 한 줄이 중간에 묻힌다.
    /// </remarks>
    private IEnumerable<IntegrityItemModel> SortedItems =>
        (_report?.Items ?? new List<IntegrityItemModel>())
            .OrderBy(x => Rank(x.Status))
            .ThenBy(x => x.Category)
            .ThenBy(x => x.CheckName);

    private readonly TableGroupDefinition<IntegrityItemModel> _groupDef = new()
    {
        Selector = x => x.Category,
        Indentation = false,
        Expandable = false
    };

    protected override async Task OnInitializedAsync() => await RunAsync();

    private async Task RunAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            using var resp = await Http.GetAsync("api/finance/integrity-check");
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                Snackbar.Add($"검사를 실행하지 못했습니다 — {ApiErrorText.Extract(body, (int)resp.StatusCode)}",
                    Severity.Error);
                return;
            }

            _report = await resp.Content.ReadFromJsonAsync<IntegrityReportModel>();

            // 🔴 서버의 PassCount/FailCount 를 그대로 쓰지 않는다.
            //    서버는 WARN 을 pass 에도 fail 에도 안 넣어 합이 총계와 안 맞는다.
            //    화면에서 직접 센다 — 숫자가 안 맞으면 사장님이 화면을 못 믿는다.
            var items = _report?.Items ?? new List<IntegrityItemModel>();
            _failCount = items.Count(x => string.Equals(x.Status, "FAIL", StringComparison.OrdinalIgnoreCase));
            _warnCount = items.Count(x => string.Equals(x.Status, "WARN", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Snackbar.Add($"검사 중 오류가 발생했습니다 — {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private static int Rank(string? status) => (status ?? string.Empty).ToUpperInvariant() switch
    {
        "FAIL" => 0,
        "WARN" => 1,
        _ => 2
    };

    /// <summary>🔴 고객 화면이므로 OK/FAIL 같은 영문 코드를 그대로 쓰지 않는다.</summary>
    private static string StatusLabel(string? status) => (status ?? string.Empty).ToUpperInvariant() switch
    {
        "FAIL" => "이상",
        "WARN" => "확인 필요",
        "OK" => "정상",
        _ => string.IsNullOrWhiteSpace(status) ? "-" : status!
    };

    private static Color StatusColor(string? status) => (status ?? string.Empty).ToUpperInvariant() switch
    {
        "FAIL" => Color.Error,
        "WARN" => Color.Warning,
        _ => Color.Success
    };
}
