namespace HitPan.Application.Interfaces;

/// <summary>
/// 🔴 W-G1-52 갈래 C — 대표(부모) 비밀번호 되찾기 · 윈도우 관리자 승인 C안(설계 §8-4 · 덧 지시서 §2).
/// </summary>
/// <remarks>
/// 흐름: 메인PC 바로가기 → 오프라인 명령 <c>parent-reset</c>(관리자 승인 · 1회용 표 행만 DB 에) → 이 서비스(API 프로세스)가 표를 대조해 대표 비번을 바꾼다.
/// <list type="bullet">
/// <item>🔴 H1 — 감사 파일 「시작」 줄을 못 쓰면 <b>DB 를 하나도 바꾸지 않고</b> 멈춘다(<see cref="ParentResetStatus.AuditUnavailable"/>).</item>
/// <item>🔴 H3 — 감사 줄은 이 서비스(API 프로세스)만 쓴다. 오프라인 명령은 감사 파일 손 0.</item>
/// <item>🔴 #40 — 로컬 BCrypt 만 · 비번 · 1회용 표 값은 로그 · 감사 덧말 · 응답에 싣지 않는다.</item>
/// <item>🔴 #2 — 회사 · 대표 id 는 요청에서 받지 않는다. 표 행(서버 DB)에서만 꺼낸다.</item>
/// </list>
/// </remarks>
public interface IParentPasswordResetService
{
    /// <summary>재설정 화면이 대표 아이디를 보여 주려고 부른다. 표를 소비하지 않는다(읽기만 · 감사 0).</summary>
    Task<ParentResetPeekResult> PeekAsync(string? token, CancellationToken ct = default);

    /// <summary>표 대조 → 대표 비번 교체 · 실패 칸 0 · 잠금 비움 · 갱신 토큰 폐기 · 세션 끊기 · 감사 2벌(로컬 파일 + DB <c>audit_logs</c>).</summary>
    /// <param name="request">화면 입력(표 · 새 비번 두 칸).</param>
    /// <param name="ipAddress">요청 주소(DB 감사 줄용 · 서버가 소켓에서 꺼낸 값).</param>
    Task<ParentResetResult> ResetAsync(ParentResetRequest request, string? ipAddress, CancellationToken ct = default);
}

/// <summary>재설정 요청(화면 → API). 회사 · 사용자 id 칸은 일부러 없다(#2).</summary>
public sealed record ParentResetRequest(string? Token, string? NewPassword, string? ConfirmPassword);

/// <summary>대표 아이디 보기 결과. 표가 못 쓰는 것이면 <see cref="LoginId"/> = null.</summary>
public sealed record ParentResetPeekResult(bool Usable, string? LoginId);

/// <summary>재설정 결과 꼴.</summary>
public enum ParentResetStatus
{
    /// <summary>바뀜.</summary>
    Succeeded = 1,
    /// <summary>표 없음 · 꼴 틀림 · 만료 · 이미 씀 · 대표 아닌 행 · 동시 요청에서 짐 — 화면 문구는 하나(어느 까닭인지 알려 주지 않음).</summary>
    TokenRejected = 2,
    /// <summary>새 비번 두 칸이 다름(DB · 감사 손 0).</summary>
    PasswordMismatch = 3,
    /// <summary>비번 규칙 밖(DB · 감사 손 0) — <see cref="ParentResetResult.Message"/> 에 규칙 문구.</summary>
    PasswordRule = 4,
    /// <summary>🔴 H1 — 감사 「시작」 줄을 못 씀 → 아무것도 안 바꿈.</summary>
    AuditUnavailable = 5,
}

/// <summary>재설정 결과.</summary>
/// <param name="Status">꼴.</param>
/// <param name="Message">화면에 그대로 보이는 쉬운 문구(개발 용어 0).</param>
/// <param name="ReasonCode">감사 · 시험용 짧은 까닭 코드(화면에 안 보임 · 고정 낱말).</param>
public sealed record ParentResetResult(ParentResetStatus Status, string Message, string ReasonCode)
{
    /// <summary>성공 뒤 감사 「끝」 줄만 못 씀(비번은 이미 바뀜 · DB 감사 줄은 같은 트랜잭션에 있음).</summary>
    public bool AuditTailMissing { get; init; }
}
