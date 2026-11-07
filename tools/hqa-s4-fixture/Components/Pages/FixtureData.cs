using HitPan.Backoffice.Components.Pages.Platform.Components;

namespace HqaS4Fixture.Components.Pages;

/// <summary>목록 한 줄(가짜). 백오피스 CsTicketRow 에서 상태 칸 · 목록 칸이 읽는 값만 둔다.</summary>
public sealed class FixtureRow
{
    public int Id { get; init; }
    public string CompanyName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Channel { get; init; } = "";
    public string? HandledVia { get; init; }
    /// <summary>null 도 넣는다(옛 줄 흉내) — 상태 칸 매개변수는 string 이라 화면에서 null! 로 넘긴다.</summary>
    public string? Status { get; set; }
    public DateTime? ReadAt { get; init; }
    public int? Rating { get; init; }
    public DateTime ReceivedAt { get; init; }
    public int ReplyCount { get; init; }
    /// <summary>재기 편하라고 칸에 data-status-kind 로 싣는 분류(normal · legacy · blank · empty · null).</summary>
    public string StatusKind { get; set; } = "normal";
}

/// <summary>
/// 가짜 데이터 — 실데이터 · 실제 고객사 이름 0. 같은 rows · special 이면 언제나 같은 줄이 나온다(난수 0 · 시각만 오늘 기준).
/// </summary>
public static class FixtureData
{
    static readonly string[] ErpStatuses = { "접수", "처리중", "보류", "고객답대기", "완료" };
    static readonly string[] Categories = { "use", "set", "net", "dat", "upd", "bug", "ins", "etc" };

    public static List<FixtureRow> Build(int rows, bool special)
    {
        var today = DateTime.Today;
        var list = new List<FixtureRow>(rows);
        for (var i = 1; i <= rows; i++)
        {
            // 3줄에 1줄은 손으로 적은 건(전화) · 나머지는 고객사 쪽지(erp_message)
            var manual = i % 3 == 2;
            var channel = manual ? "phone" : CsVocab.ErpMessageChannel;
            string? status = manual ? (i % 2 == 0 ? "완료" : "접수") : ErpStatuses[i % 5];

            if (special)
            {
                status = i switch
                {
                    2 => "처리중",   // 전화 건인데 목록 밖 값 → 「예전 기록」 줄
                    3 => "   ",      // 공백뿐(쪽지 건)
                    4 => "",         // 빈 값(쪽지 건)
                    6 => null,       // null(쪽지 건)
                    8 => "  ",       // 공백뿐(전화 건) → 목록 밖 값으로도 읽힌다
                    _ => status,
                };
            }

            list.Add(new FixtureRow
            {
                Id = i,
                CompanyName = $"가짜고객사{i:D3}",
                Category = Categories[i % Categories.Length],
                Channel = channel,
                HandledVia = manual ? (i % 4 == 0 ? "remote" : "phone") : null,
                Status = status,
                ReadAt = i % 4 == 0 ? null : today.AddHours(-i + 1),
                Rating = i % 6 == 0 ? 3 : i % 10 == 0 ? 1 : null,
                ReceivedAt = today.AddHours(-i),
                ReplyCount = i % 3,
                StatusKind = KindOf(channel, status),
            });
        }
        return list;
    }

    public static string KindOf(string channel, string? status)
    {
        if (status is null) return "null";
        if (status.Length == 0) return "empty";
        if (string.IsNullOrWhiteSpace(status)) return "blank";
        return CsVocab.IsLegacyStatus(channel, status) ? "legacy" : "normal";
    }
}
