using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitPan.API.Services.ManualUpdate;

/// <summary>usage.jsonl 한 줄(계약 문서 §7 칸 이름 그대로 · snake_case).</summary>
public sealed record ManualUsageEntry(
    DateTime At,
    string Entry,
    string Mode,
    string From,
    string? To,
    string Result,
    string Reason,
    string? RequestedBy,
    string? AutoState,
    string? Ticket);

/// <summary>
/// 2026-09-30 작1 갈래 U — 수동 사용 기록(설계 §13-5 · 계약 §7). 「수동이 쓰였다 = 자동 결함 신호」라서 CS 가 여기서 시작한다.
///
/// ■ 무엇을 적나 — 계약 §7: U 는 <b>일꾼에 넘기기 전에 끝난 사용</b>(거부·실패)만 적는다. 넘긴 뒤의 끝 상태는 일꾼이 같은 파일에
///   한 줄 적는다 ⇒ 한 번의 사용 = 한 줄(두 줄로 적지 않는다). 이벤트로그도 한 건.
/// ■ 어디에 — <c>{app}\manual\usage.jsonl</c>(덧붙이기만 · <see cref="ManualFolders"/> 쓰기 전 검사) + 이벤트로그
///   (원천 <c>HitPanWatchdog</c> · 새 원천 등록 0 · 번호 <see cref="EventIdRefusedBeforeHandOff"/>).
/// ■ 무엇을 안 하나 — DB 쓰기 0 · 본사 전송 0(#18·#22 · L-3 결재) · 민감정보 0(비밀번호·토큰·경로 값·거래 자료 없음).
/// </summary>
public sealed class ManualUsageLog
{
    /// <summary>이벤트로그 원천(워치독 설치가 등록한 이름 · 새로 등록하지 않는다).</summary>
    public const string EventSource = "HitPanWatchdog";

    /// <summary>
    /// 수동 업데이트 대역 28060~28079(PM P-4 결재) 중 API 가 일꾼에 넘기기 전에 끝낸 사용.
    /// 28060~28064 는 일꾼(시작·성공·거부·원위치·망가짐 · 계약 §7) 몫이라 겹치지 않게 28065 를 쓴다.
    /// </summary>
    public const int EventIdRefusedBeforeHandOff = 28065;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly object FileGate = new();

    private readonly ManualFolders _folders;
    private readonly ILogger<ManualUsageLog> _logger;
    private readonly Action<int, string, bool> _eventSink;

    /// <summary>운영: 이벤트로그에 적는다.</summary>
    public ManualUsageLog(ManualFolders folders, ILogger<ManualUsageLog> logger)
        : this(folders, logger, eventSink: null)
    {
    }

    /// <summary>시험: 이벤트 대역(<paramref name="eventSink"/> = (번호, 본문, 경고여부)).</summary>
    public ManualUsageLog(ManualFolders folders, ILogger<ManualUsageLog> logger, Action<int, string, bool>? eventSink)
    {
        _folders = folders;
        _logger = logger;
        _eventSink = eventSink ?? WriteEventLog;
    }

    /// <summary>일꾼에 넘기기 전에 끝난 수동 사용 한 번을 적는다(파일 한 줄 + 이벤트 한 건). 기록 실패는 로그만 — 사용자 응답을 막지 않는다.</summary>
    public void RecordBeforeHandOff(ManualUsageEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonOptions);
        try
        {
            ManualFolders.EnsureRestricted(_folders.ManualDir);
            lock (FileGate)
            {
                using var fs = new FileStream(_folders.UsageFile, FileMode.Append, FileAccess.Write, FileShare.Read);
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(line + "\n");
                fs.Write(bytes, 0, bytes.Length);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "[ManualUpdate] 수동 사용 기록(usage.jsonl)을 남기지 못했다 — 사유 {Reason}", entry.Reason);
        }

        var message = $"[수동 업데이트] 요청 전 종료 · 입구 {entry.Entry} · {entry.From} → {entry.To ?? "-"} · 결과 {entry.Result} · 사유 {entry.Reason}";
        try
        {
            _eventSink(EventIdRefusedBeforeHandOff, message, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ManualUpdate] 이벤트로그를 남기지 못했다 — 사유 {Reason}", entry.Reason);
        }
    }

    private void WriteEventLog(int eventId, string message, bool warning)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("[ManualUpdate] (윈도 아님 — 이벤트로그 대신) {EventId} {Message}", eventId, message);
            return;
        }
        WriteEventLogWindows(eventId, message, warning);
    }

    [SupportedOSPlatform("windows")]
    private static void WriteEventLogWindows(int eventId, string message, bool warning)
        => EventLog.WriteEntry(EventSource, message,
            warning ? EventLogEntryType.Warning : EventLogEntryType.Information, eventId);
}
