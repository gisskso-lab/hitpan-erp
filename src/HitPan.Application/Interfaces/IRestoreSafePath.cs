using HitPan.Application.DTOs.Backup;

namespace HitPan.Application.Interfaces;

// ══════════════════════════════════════════════════════════════════════
// 🔴 W-G1-52 갈래 A-2② — 안전 경로 복원 걸음 ① ~ ③ <b>계약</b>(작업지시서 20261128작_W-G1-52_갈래A2 §4-1 · 설계 §4 · §15-3).
//   A-2③(걸음 ④ · ④-끝 · ⑤)이 이 계약을 부른다 — 바꾸려면 PM 결재(A-2③ 와 같이).
//   깃발 BackupService.RestoreTemporarilyClosed 닫힌 채 = 제품 길에서 부르는 곳 0 · 시험은 DB 게이트에서 직접 부름(§9 ②).
//   점검 상태(A-2① IRestoreMaintenance)는 이 계약이 만들지 않는다 — 부르는 쪽이 켠 뒤 그 사실을 넘긴다(걸음 ② 인자).
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// 안전 경로 복원 걸음 ① ~ ③ — 문(대표 · 비번 · 회사명 · 확인) · ① 고르기 · ② 사전 백업 · ③ 임시 표 붓기 · 임시 표 지움.
/// 모든 메서드의 회사 = JWT 에서 온 <c>tenantId</c> 하나(#2). 걸음 ① ~ ③ 은 라이브 「백업 값」 표 손 0.
/// </summary>
public interface IRestoreSafePath
{
    /// <summary>
    /// 문(걸음 전) — 순서: 대표 판정 → 「사라지는 거래」 확인 → 회사명 → 잠김 → 비번.
    /// 바뀌는 것 = 비번 틀림의 실패 칸(+1 · 5회 15분 잠금 = 로그인과 같은 셈) · 맞으면 실패 칸 0.
    /// </summary>
    Task<RestoreStepResult> CheckDoorAsync(string tenantId, string userId, SafeRestoreRequest request, CancellationToken ct = default);

    /// <summary>화면 1장 목록 — 이 회사 성공 백업마다 고를 수 있나(목록 · 폴더 · 지문 칸 · 판). 파일 지문 다시 셈은 ① 에서만.</summary>
    Task<IReadOnlyList<SafeRestoreCandidateDto>> ListCandidatesAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// ① 고르기 — 이 회사 목록 행 · 백업 폴더 안 · 지문 칸 있음 · 판 같음 · 라이브 표 분류 전수 · 파일을 쓰기 막고 열어 SHA-256 대조.
    /// 성공이면 <see cref="RestoreSelection"/> 이 연 파일 손잡이를 쥔다(③-1 같은 손잡이 · 부르는 쪽이 Dispose).
    /// </summary>
    Task<RestoreStepResult<RestoreSelection>> SelectAsync(string tenantId, string backupId, CancellationToken ct = default);

    /// <summary>
    /// ② 사전 백업 — <paramref name="maintenanceActive"/> = 점검 상태가 켜졌음(부르는 쪽 확인 · false 면 덤프 0 · 멈춤).
    /// 지금 DB 전체 덤프 → 목록(<c>backup_history</c>)에 <c>pre_restore</c> 로 등록(지문 · 판 칸 포함 · 보관정책 밖 파일 이름).
    /// </summary>
    Task<RestoreStepResult<RestorePreBackup>> PreBackupAsync(string tenantId, string restoreId, bool maintenanceActive, CancellationToken ct = default);

    /// <summary>
    /// ③ 임시 자리 — <see cref="RestoreSelection"/> 의 같은 손잡이로 덤프를 읽어 변환기(바이트 단위 · 허용 목록 · 출력 2차 검사)를 거쳐
    /// 같은 DB 안 <c>rs_new__&lt;표&gt;</c> 에 붓는다(앱 연결 직접 실행 = 붓기 꼴 (ㄴ)). 붓기 뒤 임시 표의 회사 표지 셈(다른 회사 줄 0).
    /// 실패면 임시 표를 지우고 멈춤 · 라이브 손 0.
    /// </summary>
    Task<RestoreStepResult<RestoreStaging>> StageAsync(string tenantId, RestoreSelection selection, CancellationToken ct = default);

    /// <summary>임시 표(<c>rs_new__</c> 로 시작 · <c>LEFT(TABLE_NAME, 8)</c> 로 정확히)만 지운다 — 지우기 한 곳(G9 · 설계 §15-5 ④). 지운 수.</summary>
    Task<int> DropStagingTablesAsync(CancellationToken ct = default);
}

/// <summary>걸음 결과(값 없음). <b>기본값 = 실패 쪽</b>(<see cref="Ok"/> false · <see cref="DataUnchanged"/> false).</summary>
public class RestoreStepResult
{
    /// <summary>이 걸음이 통과했나.</summary>
    public bool Ok { get; init; }

    /// <summary>이 결과를 낸 걸음(문 = <see cref="SafeRestoreStep.None"/>).</summary>
    public SafeRestoreStep Step { get; init; }

    /// <summary>막힘 · 멈춤 까닭(<see cref="SafeRestoreDenyCodes"/>). 통과면 null.</summary>
    public string? DenyCode { get; init; }

    /// <summary>라이브 「백업 값」 표가 바뀌지 않았음을 이 걸음이 확인했나(걸음 ① ~ ③ 은 늘 true · 기본 false).</summary>
    public bool DataUnchanged { get; init; }

    /// <summary>잠김 남은 초(<see cref="SafeRestoreDenyCodes.LockedOut"/> 일 때만).</summary>
    public int? LockoutRemainingSeconds { get; init; }

    /// <summary>운영 로그용 짧은 까닭(경로 · 비번 · 자료 값 0 · 화면 · 응답에 옮기지 않음 — P3-9).</summary>
    public string? LogDetail { get; init; }
}

/// <summary>값을 실은 걸음 결과.</summary>
public sealed class RestoreStepResult<T> : RestoreStepResult where T : class
{
    /// <summary>통과면 값 · 아니면 null.</summary>
    public T? Value { get; init; }
}

/// <summary>① 고른 백업 — 지문을 확인한 <b>그 파일 손잡이</b>를 쥔다(쓰기 막힘 · ③-1). Dispose 로 닫는다.</summary>
public sealed class RestoreSelection : IDisposable
{
    public RestoreSelection(string backupId, DateTime startedAt, string fileSha256, string schemaFingerprint, Stream dumpStream)
    {
        BackupId = backupId;
        StartedAt = startedAt;
        FileSha256 = fileSha256;
        SchemaFingerprint = schemaFingerprint;
        DumpStream = dumpStream;
    }

    /// <summary>백업 목록 ID.</summary>
    public string BackupId { get; }

    /// <summary>백업 시각(「이 날짜 뒤 거래는 사라짐」).</summary>
    public DateTime StartedAt { get; }

    /// <summary>목록과 대조해 맞은 파일 SHA-256(소문자 16진 64자).</summary>
    public string FileSha256 { get; }

    /// <summary>목록의 판 표지(= 라이브 판 표지와 같음을 확인함).</summary>
    public string SchemaFingerprint { get; }

    /// <summary>지문을 확인한 그 손잡이(읽기 · 다른 쓰기 막힘). 걸음 ③ 이 처음으로 되감아 읽는다.</summary>
    public Stream DumpStream { get; }

    public void Dispose() => DumpStream.Dispose();
}

/// <summary>② 사전 백업 결과.</summary>
public sealed class RestorePreBackup
{
    /// <summary>백업 목록에 오른 「복원 전 자동 백업」 ID — 되돌릴 때 고를 백업.</summary>
    public string BackupId { get; init; } = "";
}

/// <summary>③ 임시 자리 결과 — A-2③ 의 ④ 대조 · ⑤ 교체가 이 목록 위에서 돈다.</summary>
public sealed class RestoreStaging
{
    /// <summary>
    /// 부은 원래 표 이름 → 임시 표 이름(<c>rs_new__&lt;원래&gt;</c>). 「백업 값」 표 + 판 대조용 2(<c>schema_migrations</c> · <c>__efmigrationshistory</c>).
    /// 대소문자 무시 사전.
    /// </summary>
    public IReadOnlyDictionary<string, string> StagedTables { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>판 대조용으로만 부은 표(교체 안 함).</summary>
    public IReadOnlyList<string> VersionOnlyTables { get; init; } = Array.Empty<string>();

    /// <summary>덤프에서 실행한 문장 수(만든 DROP · CREATE · INSERT).</summary>
    public int StatementsExecuted { get; init; }
}
