namespace HitPan.API.Services.Cooltime;

// =============================================================
// 쿨타임(설정 시각 자동 로그아웃 · PC 만) — 갈래 ②(화면) · ③(게이트)가 붙는 계약
// 근거: 작업지시서 docs/운영기록/20260928작4_쿨타임_자동로그아웃_작업지시서.md
//       설계문서   docs/설계/erp/쿨타임_자동로그아웃_설계_20260928.md §3 · §4 · §5-2 · §D
// 🔴 이 파일의 이름·값을 바꾸면 화면(NotificationClient 이벤트 이름)과 게이트가 같이 깨진다.
// =============================================================

/// <summary>누가 끝내기를 불렀나 — 접속기록 사유 · <c>CooltimeEnded</c> 이벤트의 <c>cause</c> 로만 쓴다.</summary>
public enum CooltimeEndCause
{
    /// <summary>§3 ① — 물은 뒤 65초 안에 [계속]이 없었다(서버 작업).</summary>
    NoAnswer,

    /// <summary>§3 ② — 설정 시각 + 5분이 이미 지났는데 한 번도 안 물었다(서버가 꺼져 있다 켜진 경우 등 · 서버 작업).</summary>
    Missed,

    /// <summary>§4 ② — 팝업 [아니요 — 지금 로그아웃](API).</summary>
    UserChoice,

    /// <summary>§4 ③ — 팝업 카운트다운 0(API).</summary>
    Countdown
}

/// <summary>끝내기 결과의 갈래.</summary>
public enum CooltimeEndOutcome
{
    /// <summary>세션 행을 지웠다 — refresh 폐기 · 퇴근 기록(있으면) · 접속기록 · 푸시까지 했다.</summary>
    Ended,

    /// <summary>1단계 삭제 0행 — 이미 끝났거나 [계속]이 이겼거나 다른 자리(정지·비번 바꿈·퇴사)가 먼저 지웠다. 아무것도 안 했다(§D-6 (가)).</summary>
    NotEnded,

    /// <summary>InnoDB 교착(1213) — 트랜잭션 전체를 되돌렸다. 서버 작업은 다음 바퀴가 다시 판정한다(§D-6).</summary>
    Deadlock
}

/// <summary>퇴근 기록 단계(§6)가 무엇을 했나.</summary>
public enum CooltimeAttendanceOutcome
{
    /// <summary>끝내기 자체가 안 됐다(<see cref="CooltimeEndOutcome.NotEnded"/> · <see cref="CooltimeEndOutcome.Deadlock"/>).</summary>
    NotAttempted,

    /// <summary>계정에 연결된 사원이 없다(§D-3 · 0행) — 건너뜀.</summary>
    NoEmployee,

    /// <summary>회사 정시 퇴근 시각이 비어 있다 — 건너뜀.</summary>
    NoRegularLeaveTime,

    /// <summary>붙일 행 없음(출근 기록 없음 · 24시간 밖 · 이미 퇴근 · C-3) — 건너뜀.</summary>
    NoOpenRow,

    /// <summary><c>check_out</c> = 정시 퇴근 시각 · <c>check_out_source='cooltime'</c>.</summary>
    WroteRegularLeave,

    /// <summary>정시 퇴근 시각이 아직 안 왔다(C-2 (다)) — <c>check_out</c> NULL 그대로 · <c>check_out_source='cooltime_blank'</c>.</summary>
    LeftBlank
}

/// <summary><see cref="CooltimeEngine.CooltimeLogoutAsync"/> 의 결과. 게이트가 그대로 단언할 수 있게 값만 담는다.</summary>
public sealed record CooltimeEndResult(
    CooltimeEndOutcome Outcome,
    string? UserId,
    string? EmployeeId,
    string? AttendanceId,
    CooltimeAttendanceOutcome Attendance,
    DateTime? CheckOutWritten);

/// <summary><see cref="CooltimeEngine.RunOnceAsync"/> 한 바퀴 결과(게이트·로그용).</summary>
public sealed record CooltimeTickResult(int EnabledTenants, int Asked, int EndedNoAnswer, int EndedMissed, int Deadlocks);

/// <summary><c>GET /api/auth/cooltime/state</c> 응답 본문.</summary>
public sealed record CooltimeStateResponse(bool Asked, int SecondsLeft, DateTime? SnoozeUntilUtc);

/// <summary><c>POST /api/auth/cooltime/logout</c> 요청 본문(선택). 테넌트·세션 번호는 받지 않는다(#2 · JWT 에서만).</summary>
public sealed class CooltimeLogoutRequest
{
    /// <summary><c>"button"</c>(기본 · [아니요 — 지금 로그아웃]) 또는 <c>"countdown"</c>(0초). 접속기록 사유 구분에만 쓴다.</summary>
    public string? Trigger { get; set; }
}

/// <summary>푸시 이벤트 이름·사유 문구 — 한 곳에만 둔다.</summary>
public static class CooltimeWire
{
    /// <summary>그룹 <c>s:{tenant}:{sid}</c> 로 보내는 물음. 본문 <c>{ "secondsLeft": 60 }</c>.</summary>
    public const string PromptEvent = "CooltimePrompt";

    /// <summary>그룹 <c>s:{tenant}:{sid}</c> 로 보내는 끝남. 본문 <c>{ "cause": "no_answer" | "missed" | "user_choice" | "countdown" }</c>.</summary>
    public const string EndedEvent = "CooltimeEnded";

    /// <summary>팝업 카운트다운 초(오더 「= 60초」).</summary>
    public const int PromptSeconds = 60;

    /// <summary>서버가 끝내기까지 기다리는 초(60초 + 여유 5초 · 설계 §3 ①).</summary>
    public const int ServerGraceSeconds = 65;

    /// <summary><c>check_out_source</c> 값(DB-134).</summary>
    public const string SourceCooltime = "cooltime";

    /// <inheritdoc cref="SourceCooltime"/>
    public const string SourceCooltimeBlank = "cooltime_blank";

    /// <summary><c>CooltimeEnded.cause</c> 문자열.</summary>
    public static string CauseWire(CooltimeEndCause cause) => cause switch
    {
        CooltimeEndCause.NoAnswer => "no_answer",
        CooltimeEndCause.Missed => "missed",
        CooltimeEndCause.UserChoice => "user_choice",
        CooltimeEndCause.Countdown => "countdown",
        _ => "unknown"
    };

    /// <summary>접속기록 <c>audit_trail.reason</c> 문구(§D-4 (가)) — 화면에 그대로 보일 수 있어 개발용어를 쓰지 않는다(#25).</summary>
    public static string AuditReason(CooltimeEndCause cause) => cause switch
    {
        CooltimeEndCause.NoAnswer => "자동 로그아웃(설정 시각 · 응답 없음)",
        CooltimeEndCause.Missed => "자동 로그아웃(설정 시각 지남)",
        CooltimeEndCause.UserChoice => "자동 로그아웃(설정 시각 · 직접 로그아웃)",
        CooltimeEndCause.Countdown => "자동 로그아웃(설정 시각 · 시간 초과)",
        _ => "자동 로그아웃(설정 시각)"
    };
}
