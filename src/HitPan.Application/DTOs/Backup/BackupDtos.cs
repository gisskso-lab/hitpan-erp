namespace HitPan.Application.DTOs.Backup;

/// <summary>자료 백업 운영 설정 DTO.</summary>
public sealed class BackupSettingsDto
{
    public string PrimaryPath { get; set; } = "";
    public string? MirrorPath { get; set; }
    public string ScheduleMode { get; set; } = "manual"; // manual / hourly / every_6h / daily_03
    public int RetentionCount { get; set; } = 30;
    public DateTime? LastRunAt { get; set; }
    public string? LastStatus { get; set; }
    public string? LastError { get; set; }
}

/// <summary>백업 설정 변경 요청.</summary>
public sealed class UpdateBackupSettingsRequest
{
    public string PrimaryPath { get; set; } = "";
    public string? MirrorPath { get; set; }
    public string ScheduleMode { get; set; } = "manual";
    public int RetentionCount { get; set; } = 30;
}

/// <summary>백업 이력 1행.</summary>
public sealed class BackupHistoryDto
{
    public string BackupId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? PrimaryFile { get; set; }
    public string? MirrorFile { get; set; }
    public long? FileSizeBytes { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string TriggeredBy { get; set; } = "manual";
}

/// <summary>백업 실행 응답.</summary>
public sealed class RunBackupResponse
{
    public bool Success { get; set; }
    public string BackupId { get; set; } = "";
    public string? PrimaryFile { get; set; }
    public string? MirrorFile { get; set; }
    public long FileSizeBytes { get; set; }
    public string? Error { get; set; }
}

/// <summary>복원 요청 (백업 이력에서 선택 또는 직접 경로).</summary>
public sealed class RestoreRequest
{
    /// <summary>백업 이력 ID (선택). 우선순위 1.</summary>
    public string? BackupId { get; set; }
    /// <summary>외부 .sql 파일 경로 (선택). BackupId 없을 때 사용.</summary>
    public string? ExternalFilePath { get; set; }
    /// <summary>안전 확인 — 사용자가 입력한 회사명 (DB 회사명과 일치해야 진행).</summary>
    public string ConfirmCompanyName { get; set; } = "";
}

/// <summary>복원 응답.</summary>
public sealed class RestoreResponse
{
    public bool Success { get; set; }
    public string RestoreId { get; set; } = "";
    public string? PreRestoreBackup { get; set; }
    public string? Error { get; set; }
}

/// <summary>복원 이력.</summary>
public sealed class RestoreHistoryDto
{
    public string RestoreId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string SourceFile { get; set; } = "";
    public string? PreRestoreBackup { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? TriggeredByUser { get; set; }
}

// ══════════════════════════════════════════════════════════════════════
// 🔴 W-G1-52 갈래 A-1 — 안전 경로 복원 계약(설계 §3 · §4 · §11 · 덧 지시서 §3).
//   B(복원 화면) 착수 조건 = 이 계약 고정. 바꾸려면 PM 결재(B 와 같이).
//   위 옛 RestoreRequest · RestoreResponse 는 손 0(#1 · 옛 복원 길 = 닫힘 깃발 그대로).
//   이 묶음엔 부르는 곳 0 — 서비스 · 컨트롤러 연결은 A-2 이후.
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// 안전 경로 복원 요청(부모계정 · 메인PC · 한 화면 4장 중 3장 「확인」 에서 보냄).
/// 외부 파일 경로 칸은 <b>없다</b> — 이 회사 백업 목록에 있는 것만(조건 4 · K9).
/// </summary>
public sealed class SafeRestoreRequest
{
    /// <summary>백업 목록(<c>backup_history</c>)의 ID — 이 회사 목록 밖이면 거절(<see cref="SafeRestoreDenyCodes.BackupNotInList"/>).</summary>
    public string BackupId { get; set; } = "";

    /// <summary>화면 2장 「위 내용을 확인했습니다」 체크(조건 6 · 「이 날짜 뒤에 적은 거래는 사라집니다」). false 면 거절.</summary>
    public bool AcknowledgedLaterDataLoss { get; set; }

    /// <summary>회사명 입력(지금 있는 확인 그대로 · 비번 다시 입력을 대신하지 못함 · 설계 §3 겹 4).</summary>
    public string ConfirmCompanyName { get; set; } = "";

    /// <summary>대표(부모계정) 비밀번호 다시 입력(ⓑ · 설계 §3 겹 3 · 틀리면 로그인 실패와 같은 셈). 로그 · 응답에 남기지 않는다.</summary>
    public string Password { get; set; } = "";
}

/// <summary>안전 경로 5걸음(설계 §4). 0 = 걸음 시작 전(문 · 판정에서 막힘).</summary>
public enum SafeRestoreStep
{
    None = 0,
    /// <summary>① 고르기 — 목록 · 지문 · 판 · 표 분류 전수.</summary>
    Select = 1,
    /// <summary>② 사전 백업 — 점검 상태 켠 뒤 지금 DB 덤프 · 목록 등록.</summary>
    PreBackup = 2,
    /// <summary>③ 임시 자리 — 같은 DB 안 <c>rs_new__</c> 표에 부음.</summary>
    Stage = 3,
    /// <summary>④ 대조 — 회사 표지 · 판 · 칸 · 참조 · 계정↔사원 양방향.</summary>
    Verify = 4,
    /// <summary>⑤ 교체 — 「백업 값」 표만 한 트랜잭션 지우고 붓기.</summary>
    Swap = 5,
}

/// <summary>
/// 막힘 · 멈춤 까닭 글자(화면 ↔ 서버 계약 · 화면은 이 글자로 쉬운 말 1줄을 고른다 · 개발 용어를 화면에 그대로 내지 않음).
/// </summary>
public static class SafeRestoreDenyCodes
{
    // 문 · 판정(걸음 전) — 바뀐 것 0
    /// <summary>대표(부모계정) 아님 · 대표 0명 · 2명 이상(설계 §3 겹 2 · G1).</summary>
    public const string NotParent = "not_parent";
    /// <summary>비밀번호 틀림(실패 칸 +1 · G2).</summary>
    public const string WrongPassword = "wrong_password";
    /// <summary>비밀번호 실패가 쌓여 잠김(5회 15분 · 로그인과 같은 셈).</summary>
    public const string LockedOut = "locked_out";
    /// <summary>회사명이 다름.</summary>
    public const string CompanyMismatch = "company_mismatch";
    /// <summary>「사라지는 거래」 확인 체크 안 함(조건 6).</summary>
    public const string NotAcknowledged = "not_acknowledged";
    /// <summary>다른 복원이 이미 도는 중.</summary>
    public const string AlreadyRunning = "already_running";

    // ① 고르기
    /// <summary>이 회사 백업 목록에 없는 백업 · 목록 행 파일이 백업 폴더 밖(조건 4 · K9 · G3).</summary>
    public const string BackupNotInList = "backup_not_in_list";
    /// <summary>파일 지문이 목록과 다름 · 지문 없는 옛 백업(K6 · G3 · G19).</summary>
    public const string FingerprintMismatch = "fingerprint_mismatch";
    /// <summary>판이 다른 백업(K5 「같은 판만」 · G19).</summary>
    public const string VersionMismatch = "version_mismatch";
    /// <summary>분류 안 된 표가 있음(<c>RestoreTableClassification</c> · G13).</summary>
    public const string UnclassifiedTable = "unclassified_table";

    // ② 사전 백업 · 점검 상태
    /// <summary>사전 백업 실패(반쪽 파일은 목록에 안 올림).</summary>
    public const string PreBackupFailed = "pre_backup_failed";
    /// <summary>도는 요청이 한도 안에 0 이 되지 않음(설계 §15-5 ① ⓑ).</summary>
    public const string MaintenanceDrainTimeout = "maintenance_drain_timeout";

    // ③ · ④
    /// <summary>임시 자리에 붓기 실패 · 허용 목록 밖 문장(G14).</summary>
    public const string StageFailed = "stage_failed";
    /// <summary>다른 회사 표지 백업(조건 4 · G3).</summary>
    public const string OtherCompanyBackup = "other_company_backup";
    /// <summary>대조 실패(칸 · 참조 · 복호화 표본 등).</summary>
    public const string VerifyFailed = "verify_failed";
    /// <summary>백업 뒤에 만든 직원 계정이 그 백업에 사원 정보가 없음(설계 §15-5 ③ · G18).</summary>
    public const string NewerAccountsMissing = "newer_accounts_missing";

    // ⑤ · 공통
    /// <summary>교체 실패 — 트랜잭션 되돌림(라이브 그대로).</summary>
    public const string SwapFailed = "swap_failed";
    /// <summary>감사 쓰기 실패 = 멈춤(덧 지시서 H1 · DB 를 바꾸기 전에 멈춤).</summary>
    public const string AuditWriteFailed = "audit_write_failed";
    /// <summary>감사 파일을 믿을 수 없음 · 끝나지 않은 교체 기록 → 자동 정리 0 · 「확인 필요」(덧 지시서 H2 · H4 · H5).</summary>
    public const string CheckNeeded = "check_needed";
}

/// <summary>안전 경로 복원 결과(화면 4장 「진행 · 끝」).</summary>
public sealed class SafeRestoreResponse
{
    public bool Success { get; set; }

    /// <summary><c>restore_history</c> 의 ID(걸음 시작 전에 막히면 빈 값).</summary>
    public string RestoreId { get; set; } = "";

    /// <summary>「복원 전 자동 백업」 의 백업 목록 ID(걸음 ② 를 넘었을 때만) — 마음에 안 들면 이것으로 되돌림.</summary>
    public string? PreRestoreBackupId { get; set; }

    /// <summary>멈춘 걸음(성공 · 걸음 전 막힘 = <see cref="SafeRestoreStep.None"/>).</summary>
    public SafeRestoreStep FailedStep { get; set; }

    /// <summary>막힘 · 멈춤 까닭 글자(<see cref="SafeRestoreDenyCodes"/>). 성공이면 null.</summary>
    public string? DenyCode { get; set; }

    /// <summary>쉬운 말 1줄(개발 용어 0 · 비밀번호 · 경로 · 내부 오류 글을 담지 않음).</summary>
    public string? Message { get; set; }

    /// <summary>
    /// 「자료는 바뀌지 않았습니다」 를 말해도 되는가(교체 트랜잭션이 커밋되지 않았음).
    /// <b>기본값 false = 실패 쪽</b> — 서버가 「교체 커밋 전」 임을 확인한 자리에서만 true 로 둔다.
    /// 응답을 만드는 쪽이 깜빡하면 화면은 「확인 필요」 쪽으로 말한다(거짓 안심 0 · 고침 1 Y4 · [3-V] P2-2 · [4] P2-③).
    /// </summary>
    // 고침 1 Y4 — 옛 줄: public bool DataUnchanged { get; set; } = true;
    public bool DataUnchanged { get; set; }

    /// <summary>성공 뒤 모든 로그인이 끊김 — 화면은 [로그인으로] 를 보인다(K8).</summary>
    public bool ReloginRequired { get; set; }

    /// <summary>
    /// 잠김 남은 시간(초) — <see cref="SafeRestoreDenyCodes.LockedOut"/> 일 때만 값 · 그 밖엔 null.
    /// 화면은 이것으로 「약 n분 뒤 다시 해 주세요」 를 말한다(고침 1 Y5 · [3-V] P2-3).
    /// </summary>
    public int? LockoutRemainingSeconds { get; set; }
}

// ══ 고침 1 Y5 ([3-V] P2-3) — 화면 B 계약 칸 ══

/// <summary>
/// 화면 1장 「백업 고르기」 목록 1행 — 백업마다 고를 수 있나 · 못 고르는 까닭을 서버가 미리 알려 준다
/// (지문 없는 옛 백업 · 판 다름 · 목록 밖 파일을 누르기 전에 회색 처리). <b>파일 경로 칸 없음</b>(K9).
/// </summary>
public sealed class SafeRestoreCandidateDto
{
    /// <summary>백업 목록(<c>backup_history</c>)의 ID — <see cref="SafeRestoreRequest.BackupId"/> 로 보낼 값.</summary>
    public string BackupId { get; set; } = "";

    /// <summary>백업 시각(화면 「이 날짜 뒤에 적은 거래는 사라집니다」 의 날짜).</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>파일 크기(바이트 · 모르면 null).</summary>
    public long? FileSizeBytes { get; set; }

    /// <summary>누가 만든 백업인가(manual · auto · pre_restore 등 — 옛 <see cref="BackupHistoryDto.TriggeredBy"/> 와 같은 글자).</summary>
    public string TriggeredBy { get; set; } = "manual";

    /// <summary>고를 수 있나. <b>기본값 false = 실패 쪽</b> — 서버가 목록 · 지문 · 판을 확인한 것만 true.</summary>
    public bool Selectable { get; set; }

    /// <summary>
    /// 못 고르는 까닭(쉬운 말 코드 · <see cref="SafeRestoreDenyCodes"/> 글자 — 주로 <c>fingerprint_mismatch</c> · <c>version_mismatch</c> · <c>backup_not_in_list</c>).
    /// <see cref="Selectable"/> = true 면 null.
    /// </summary>
    public string? DenyCode { get; set; }
}

/// <summary>
/// 마지막 복원 결과 — 성공 뒤 모든 로그인이 끊겨(K8) 4장 「끝」 응답을 못 받았을 때,
/// 대표가 <b>다시 로그인한 뒤</b> 이것으로 결과를 본다(<see cref="SafeRestoreStatusDto.LastResult"/>).
/// 비번 · 경로 · 내부 오류 글을 담지 않는다.
/// </summary>
public sealed class SafeRestoreLastResultDto
{
    /// <summary><c>restore_history</c> 의 ID.</summary>
    public string RestoreId { get; set; } = "";

    /// <summary>복원 시작 시각.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>복원 끝 시각(도는 중 · 끝나지 않은 기록이면 null).</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>성공했나(기본 false).</summary>
    public bool Success { get; set; }

    /// <summary>멈춘 걸음(성공 · 걸음 전 막힘 = <see cref="SafeRestoreStep.None"/>).</summary>
    public SafeRestoreStep FailedStep { get; set; }

    /// <summary>막힘 · 멈춤 까닭 글자(<see cref="SafeRestoreDenyCodes"/>). 성공이면 null.</summary>
    public string? DenyCode { get; set; }

    /// <summary>「복원 전 자동 백업」 의 백업 목록 ID(걸음 ② 를 넘었을 때만) — 되돌릴 때 고를 백업.</summary>
    public string? PreRestoreBackupId { get; set; }

    /// <summary>「자료는 바뀌지 않았습니다」 를 말해도 되는가. <b>기본값 false = 실패 쪽</b>(<see cref="SafeRestoreResponse.DataUnchanged"/> 와 같은 뜻).</summary>
    public bool DataUnchanged { get; set; }

    /// <summary>쉬운 말 1줄(개발 용어 0).</summary>
    public string? Message { get; set; }
}

/// <summary>복원 진행 상태(점검 상태 중 통과 허용 목록의 「상태 확인」 · 설계 §15-5 ① ⓑ).</summary>
public sealed class SafeRestoreStatusDto
{
    /// <summary>복원이 도는 중(점검 상태 켜짐).</summary>
    public bool InProgress { get; set; }

    /// <summary>지금 걸음(도는 중이 아니면 <see cref="SafeRestoreStep.None"/>).</summary>
    public SafeRestoreStep CurrentStep { get; set; }

    /// <summary>도는 복원의 ID(없으면 null).</summary>
    public string? RestoreId { get; set; }

    /// <summary>
    /// 마지막으로 끝난 안전 복원의 결과(없으면 null · 고침 1 Y5 · [3-V] P2-3) — 다시 로그인한 대표에게만 채운다(부모계정 문 뒤).
    /// </summary>
    public SafeRestoreLastResultDto? LastResult { get; set; }
}
