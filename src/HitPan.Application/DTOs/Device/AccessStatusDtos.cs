namespace HitPan.Application.DTOs.Device;

// 🔴 2026-10-05 작4 B-2 — 「접속기기 확인」 화면이 받는 모양(설계 §3 · 9/28 §4). 읽기만.
//   시각은 전부 **한국 시각**으로 서버 한 곳에서 맞춰 보낸다 — 화면은 받은 그대로 쓴다.
//   브라우저 이름은 표시용이다 — 어떤 판정에도 쓰지 않는다.

/// <summary>지금 접속 중 — 요약 + 사람별 줄.</summary>
public sealed class AccessStatusDto
{
    /// <summary>지금 컴퓨터로 들어와 있는 사람 수.</summary>
    public int PcUsersNow { get; set; }

    /// <summary>지금 들어와 있는 휴대폰 수(접속 줄 수).</summary>
    public int MobileSessionsNow { get; set; }

    /// <summary>사람별 — 접속 있는 사람 먼저, 그다음 이름 순.</summary>
    public List<AccessPersonDto> People { get; set; } = new();
}

/// <summary>한 사람의 접속.</summary>
public sealed class AccessPersonDto
{
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    /// <summary>로그인 아이디(<c>users.email</c> — 부모계정 아이디 방식 포함).</summary>
    public string LoginId { get; set; } = "";
    /// <summary>컴퓨터 접속 — 없으면 null(「사용 안 함」).</summary>
    public AccessSessionDto? Pc { get; set; }
    /// <summary>휴대폰 접속들(몇 대든 함께 쓸 수 있다).</summary>
    public List<AccessSessionDto> Mobiles { get; set; } = new();
    /// <summary>가장 최근 사용(한국 시각) — 접속 없으면 null.</summary>
    public DateTime? LastUsedAtKst { get; set; }
}

/// <summary>접속 한 줄.</summary>
public sealed class AccessSessionDto
{
    public DateTime LoginAtKst { get; set; }
    public DateTime LastActiveAtKst { get; set; }
    /// <summary>예: 「크롬 · 윈도우」 · 못 뽑으면 「알 수 없음」.</summary>
    public string Browser { get; set; } = "알 수 없음";
}

/// <summary>막힌 로그인 기록 한 줄 — 「누가 · 언제」만(어느 컴퓨터에서는 재료가 없다 · F-1·F-2).</summary>
public sealed class LoginConflictAlertDto
{
    public DateTime AtKst { get; set; }
    public string UserName { get; set; } = "";
    public string LoginId { get; set; } = "";
    /// <summary><c>pc_login_blocked</c> · <c>pc_session_forced_out</c> 중 하나(화면은 문장만 쓴다).</summary>
    public string Kind { get; set; } = "";
    /// <summary>서버가 만든 고객 문장(설계 §5-2). 원문 description 은 내보내지 않는다.</summary>
    public string Message { get; set; } = "";
}
