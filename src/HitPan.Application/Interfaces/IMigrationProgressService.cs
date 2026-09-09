namespace HitPan.Application.Interfaces;

/// <summary>
/// 마이그레이션 진행률 push 추상화 (정공법 CODE-01, 2026-05-14).
///
/// MdbMigrationService(Application)는 SignalR을 알면 안 된다 (역방향 의존 차단).
/// 이 인터페이스는 Application에 두고, 구현(MigrationProgressService)은 API에 둔다.
///
/// 책임:
///   1) jobId별 테이블 진행 상태 in-memory 보관 (재접속 시 현재 상태 즉시 push 가능).
///   2) SignalR Hub를 통해 jobId 그룹에 즉시 push.
///
/// 헌법 #16 (MySqlConnection thread-safe 아님): IDbConnection 의존 0 — DI captive 차단.
/// </summary>
public interface IMigrationProgressService
{
    /// <summary>
    /// 테이블 진행 상태 업데이트 + SignalR push.
    /// </summary>
    /// <param name="jobId">잡 ID</param>
    /// <param name="tableName">테이블 키 (snake_case)</param>
    /// <param name="status">pending | running | completed | failed</param>
    /// <param name="rows">처리 행 수</param>
    /// <param name="elapsedMs">경과(ms)</param>
    /// <param name="errorMessage">실패 시 메시지</param>
    Task UpdateAsync(string jobId, string tableName, string status, int rows, long elapsedMs, string? errorMessage);

    /// <summary>
    /// 잡 종료 시 in-memory 정리 + 최종 상태 push.
    /// </summary>
    Task CompleteJobAsync(string jobId, string finalStatus, string? errorMessage = null);

    /// <summary>
    /// 작22 (2026-09-09) A3: 단계 완료 신호 — 1단계(마스터)가 끝나면 <c>phase=1</c> 로 push 하고 잡은 <c>paused</c> 로 남는다.
    /// 화면은 이 신호로 5/16 사장님 문안 다이얼로그("1단계자료 이관완료. 2단계 자료 이관 계속 하시겠습니까?")를 띄운다.
    /// 잡을 끝내는 것이 아니므로 in-memory 스냅샷은 지우지 않는다(2단계가 같은 jobId 로 이어진다).
    /// 헌법 #12: 구현체는 MigrationProgressService 하나(grep 2026-09-09 — 테스트 mock 0건).
    /// </summary>
    Task PhaseCompletedAsync(string jobId, int phase);

    /// <summary>
    /// 클라이언트 재접속 시 현재 진행 상태 스냅샷 반환 (선택).
    /// </summary>
    IReadOnlyDictionary<string, MigrationProgressSnapshot> GetSnapshot(string jobId);
}

/// <summary>테이블별 진행 스냅샷 DTO (in-memory + SignalR payload 겸용).</summary>
public sealed class MigrationProgressSnapshot
{
    public string Status { get; set; } = "pending";
    public int Rows { get; set; }
    public long ElapsedMs { get; set; }
    public string? ErrorMessage { get; set; }
}
