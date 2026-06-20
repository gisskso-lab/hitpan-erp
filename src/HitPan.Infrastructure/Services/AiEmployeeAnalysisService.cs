using System.Globalization;
using System.Text.RegularExpressions;
using HitPan.Application.DTOs.Chatbot;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

// ─────────────────────────────────────────────────────────────────
// AI 직원 — 데이터 분석 실행 계층 (1단계: 조회·분석, 읽기 전용)
//
// 사장님 결재 (2026-06-20): 챗봇이 "길 안내"만 하는 일반 도우미를 넘어,
//   자연어 명령 → 실제 DB 조회·계산 → 표 + 차트 결과를 직접 제시하는 "AI 직원".
//   1단계는 조회·분석만(데이터 변경 0, 헌법 #6). 생성(거래명세서·발주서)은 다음 단계.
//
//   접근: 완전한 Tool Use 루프(클로드가 tool 선택→실행→재호출) 대신,
//     "분석 의도 감지 → 기존 ReportService 직호출 → 구조화 결과 반환" 하이브리드.
//     이유: ① 크레딧 절약(분석 의도는 로컬 감지, 클로드 호출 0) — 사장님 "도우미에 크레딧 낭비 말라"
//           ② 정확성(기존 검증된 ReportService SQL 재사용, 환각 0 — 헌법 #23)
//           ③ 헌법 #2(tenant_id JWT) 그대로 통과.
//   결과 요약 문구는 ChatbotService 가 KB/도우미와 함께 처리. 여긴 데이터만.
// ─────────────────────────────────────────────────────────────────

/// <summary>자연어 분석 명령을 감지해 실데이터 분석 결과(표+차트)를 만든다.</summary>
public interface IAiEmployeeAnalysisService
{
    /// <summary>
    /// 메시지가 분석 명령이면 결과를 반환, 아니면 null(→ 호출부가 기존 도우미 흐름).
    /// history(직전 대화)로 "다시/그거 말고 거래처별로" 같은 재명령(리오더)도 처리한다(사장님 결재 2026-06-20).
    /// 읽기 전용. tenantId 는 JWT 유래(헌법 #2).
    /// </summary>
    Task<AiAnalysisResultDto?> TryAnalyzeAsync(
        string message, string tenantId,
        IReadOnlyList<ChatHistoryTurn>? history = null,
        CancellationToken ct = default);
}

public sealed class AiEmployeeAnalysisService : IAiEmployeeAnalysisService
{
    private readonly IReportService _report;
    private readonly ILogger<AiEmployeeAnalysisService> _logger;

    public AiEmployeeAnalysisService(IReportService report, ILogger<AiEmployeeAnalysisService> logger)
    {
        _report = report;
        _logger = logger;
    }

    public async Task<AiAnalysisResultDto?> TryAnalyzeAsync(
        string message, string tenantId,
        IReadOnlyList<ChatHistoryTurn>? history = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        // ── 재명령(리오더) 처리 (사장님 결재 2026-06-20) ──
        //   "그거 말고 거래처별로", "다시 해줘", "6월만 빼고" 등 후속 수정 명령은
        //   그 자체론 분석 키워드(수익/마진)가 없을 수 있다. 직전 사용자 발화가 분석 명령이었다면
        //   직전 발화 + 이번 수정을 합쳐 재분석한다(맥락 이어받기).
        var effective = message;
        if (LooksLikeReorder(message) && TryFindLastAnalysisUserTurn(history, out var prevAnalysisMsg))
        {
            // 직전 분석 명령 뒤에 이번 수정 지시를 덧붙여 새 조건을 반영.
            effective = prevAnalysisMsg + " " + message;
        }

        // 1) 분석 명령인지 감지: "수익" 또는 "마진" + "분석/분석해/알려/보여" 류.
        var isProfit = (effective.Contains("수익") || effective.Contains("마진") || effective.Contains("이익"))
                       && (effective.Contains("분석") || effective.Contains("알려") || effective.Contains("보여")
                           || effective.Contains("뽑아") || effective.Contains("해줘") || effective.Contains("해 줘")
                           || LooksLikeReorder(message));
        if (!isProfit)
        {
            return null;
        }

        // 새 지시(이번 메시지)를 우선 본다 — 재명령 시 "거래처별로"가 직전 "품목별"을 이겨야 한다.
        var newCommand = message;
        message = effective; // 기간·거래처 등 보조 정보는 합쳐진 명령에서도 보충.

        // 2) 집계 기준(view) 감지 — 새 지시에 기준이 명시돼 있으면 그것을 최우선.
        //    (재명령 "거래처별로 다시"가 직전 "품목별"을 덮어쓰도록.)
        string view = DetectView(newCommand) ?? DetectView(effective) ?? "partner";

        // 3) 거래처명 추출(있으면 필터). 새 지시 우선, 없으면 합쳐진 명령에서.
        string? partner = ExtractPartnerName(newCommand) ?? ExtractPartnerName(message);

        // 4) 기간 추출 — 새 지시에 기간이 있으면 우선, 없으면 합쳐진 명령에서.
        var (from, to, periodLabel) = HasPeriodHint(newCommand)
            ? ExtractPeriod(newCommand)
            : ExtractPeriod(message);

        try
        {
            var rows = await _report.GetSalesProfitabilityAsync(view, tenantId, from, to, partner, ct)
                .ConfigureAwait(false);

            if (rows is null || rows.Count == 0)
            {
                // 분석 명령은 맞으나 데이터 0 → 빈 결과(호출부가 "해당 기간 거래 없음" 안내).
                return new AiAnalysisResultDto
                {
                    Kind = "sales-profitability",
                    Title = BuildTitle(view, partner, periodLabel) + " — 해당 조건의 거래 자료가 없습니다.",
                    Columns = ColumnsFor(view),
                    Rows = new(),
                    Chart = new(),
                    ChartValueLabel = "이익(원)"
                };
            }

            // 5) 표 구성 (상위 50행까지 — 화면 가독성).
            var top = rows.Take(50).ToList();
            var tableRows = top.Select(r => new List<string>
            {
                r.Label,
                FormatWon(r.Revenue),
                FormatWon(r.Cost),
                FormatWon(r.Profit),
                $"{r.ProfitRate:0.0}%",
                r.Count.ToString("N0", CultureInfo.InvariantCulture)
            }).ToList();

            // 6) 차트 구성: 이익 기준 상위 10개 막대.
            var chart = rows
                .OrderByDescending(r => r.Profit)
                .Take(10)
                .Select(r => new ChartPointDto { Label = r.Label, Value = (double)r.Profit })
                .ToList();

            return new AiAnalysisResultDto
            {
                Kind = "sales-profitability",
                Title = BuildTitle(view, partner, periodLabel),
                Columns = ColumnsFor(view),
                Rows = tableRows,
                Chart = chart,
                ChartValueLabel = "이익(원)"
            };
        }
        catch (Exception ex)
        {
            // 헌법 #15: 빈 catch 금지. 분석 실패는 경고 후 null(→ 기존 도우미 흐름으로 안전 폴백).
            _logger.LogWarning(ex, "AI 직원 수익성 분석 실패 — 도우미 흐름으로 폴백. tenant={Tenant}", tenantId);
            return null;
        }
    }

    /// <summary>집계 기준(view) 명시 감지. 없으면 null(상위 호출이 폴백 결정).</summary>
    private static string? DetectView(string message)
    {
        if (message.Contains("거래처") || message.Contains("업체") || message.Contains("고객사"))
        {
            return "partner";
        }
        if (message.Contains("품목") || message.Contains("상품") || message.Contains("제품"))
        {
            return "item";
        }
        if (message.Contains("기간") || message.Contains("월별") || message.Contains("월간") || message.Contains("추이"))
        {
            return "period";
        }
        return null;
    }

    /// <summary>메시지에 기간 힌트(월/연도/전체)가 있는지.</summary>
    private static bool HasPeriodHint(string message)
    {
        return Regex.IsMatch(message, @"\d{1,2}\s*월") || Regex.IsMatch(message, @"20\d{2}")
            || message.Contains("올해") || message.Contains("전체") || message.Contains("누적") || message.Contains("작년");
    }

    /// <summary>"다시/말고/대신/바꿔/그게 아니라" 등 직전 결과를 고치라는 재명령(리오더) 신호.</summary>
    private static bool LooksLikeReorder(string message)
    {
        return message.Contains("다시") || message.Contains("말고") || message.Contains("대신")
            || message.Contains("바꿔") || message.Contains("바꺼") || message.Contains("아니라")
            || message.Contains("아니고") || message.Contains("틀렸") || message.Contains("그게 아니")
            || message.Contains("다른") || message.Contains("재분석") || message.Contains("다시해");
    }

    /// <summary>history 에서 가장 최근의 '분석 명령이었던 사용자 발화'를 찾는다(재명령 맥락 이어받기).</summary>
    private static bool TryFindLastAnalysisUserTurn(IReadOnlyList<ChatHistoryTurn>? history, out string prev)
    {
        prev = "";
        if (history is null || history.Count == 0)
        {
            return false;
        }

        // 뒤에서부터 사용자 발화 중 '수익/마진/이익' + 분석류를 포함한 마지막 명령을 찾는다.
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var t = history[i];
            if (!string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var c = t.Content ?? "";
            var isAnalysis = (c.Contains("수익") || c.Contains("마진") || c.Contains("이익"))
                             && (c.Contains("분석") || c.Contains("알려") || c.Contains("보여")
                                 || c.Contains("뽑아") || c.Contains("해줘"));
            if (isAnalysis)
            {
                prev = c;
                return true;
            }
        }
        return false;
    }

    private static List<string> ColumnsFor(string view)
    {
        var first = view switch
        {
            "item" => "품목",
            "period" => "기간",
            _ => "거래처"
        };
        return new List<string> { first, "매출", "원가", "이익", "이익률", "건수" };
    }

    private static string BuildTitle(string view, string? partner, string periodLabel)
    {
        var by = view switch { "item" => "품목별", "period" => "기간별", _ => "거래처별" };
        var who = string.IsNullOrEmpty(partner) ? "" : $"[{partner}] ";
        return $"{who}{by} 수익성 분석 ({periodLabel})";
    }

    private static string FormatWon(decimal v) => v.ToString("N0", CultureInfo.InvariantCulture) + "원";

    /// <summary>"00상사 …" 형태에서 거래처명 후보 추출. 못 찾으면 null(전체 집계).</summary>
    private static string? ExtractPartnerName(string message)
    {
        // 거래처를 시사하는 접미사로 끝나는 토큰을 찾는다(레거시 거래처명 관습).
        var m = Regex.Match(message, @"([가-힣A-Za-z0-9]{2,20}(상사|유통|마트|상회|물산|컴퍼니|코퍼레이션|산업|식품|전자|무역))");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>기간 표현 추출. "3월~5월" / "2026-01부터 2026-03" / "올해" / "이번 달" / 기본 최근 6개월.</summary>
    private static (DateTime? from, DateTime? to, string label) ExtractPeriod(string message)
    {
        // "N월 ~ M월" (올해 기준)
        var mm = Regex.Match(message, @"(\d{1,2})\s*월\s*[~\-부터to ]*\s*(\d{1,2})\s*월");
        if (mm.Success)
        {
            // 연도는 메시지에 4자리 있으면 사용, 없으면 작년~올해 데이터가 섞여있을 수 있어 넓게 안 잡고 최근 연도 추정 불가 → null 처리하되 라벨만.
            var y = ExtractYear(message);
            if (y.HasValue
                && int.TryParse(mm.Groups[1].Value, out var m1)
                && int.TryParse(mm.Groups[2].Value, out var m2)
                && m1 is >= 1 and <= 12 && m2 is >= 1 and <= 12)
            {
                var f = new DateTime(y.Value, m1, 1);
                var t = new DateTime(y.Value, m2, 1).AddMonths(1).AddDays(-1);
                return (f, t, $"{y.Value}-{m1:00} ~ {y.Value}-{m2:00}");
            }
            // 연도 불명 → 전체 기간으로 두되 라벨에 월 표기.
            return (null, null, $"{mm.Groups[1].Value}월 ~ {mm.Groups[2].Value}월(전체 연도)");
        }

        // "YYYY-MM" 두 개
        var dr = Regex.Matches(message, @"(\d{4})[-.](\d{1,2})");
        if (dr.Count >= 2)
        {
            var f = ParseYm(dr[0]);
            var t = ParseYm(dr[1]);
            if (f.HasValue && t.HasValue)
            {
                var tEnd = t.Value.AddMonths(1).AddDays(-1);
                return (f, tEnd, $"{f.Value:yyyy-MM} ~ {t.Value:yyyy-MM}");
            }
        }

        // 전체/누적 명시면 제한 없음
        if (message.Contains("전체") || message.Contains("누적") || message.Contains("전부"))
        {
            return (null, null, "전체 기간");
        }

        // 기본: 제한 없음(전체). 데이터가 과거~현재 폭넓게 분포할 수 있어 안전.
        return (null, null, "전체 기간");
    }

    private static int? ExtractYear(string message)
    {
        var y = Regex.Match(message, @"(20\d{2})\s*년?");
        if (y.Success && int.TryParse(y.Groups[1].Value, out var year))
        {
            return year;
        }
        if (message.Contains("올해") || message.Contains("금년"))
        {
            // 서버 로컬 연도 — 마이그 데이터가 과거라면 비어있을 수 있으나 사용자 의도 존중.
            return DateTime.Now.Year;
        }
        return null;
    }

    private static DateTime? ParseYm(Match m)
    {
        if (int.TryParse(m.Groups[1].Value, out var y) && int.TryParse(m.Groups[2].Value, out var mo)
            && mo is >= 1 and <= 12)
        {
            return new DateTime(y, mo, 1);
        }
        return null;
    }
}
