namespace HitPan.Application.Interfaces;

/// <summary>
/// DB 스키마 마이그(src/HitPan.API/Migrations/SQL/DB-*.sql) 적용 주체 — 고리4 ①단계.
///
/// 배경(설계서 §1-4·R5): 업데이트 시 ALTER 를 실행할 자동 주체가 시스템에 전무했다.
/// Program.cs 는 자동 마이그를 돌리지 않고, install.bat 은 신규설치 clean DDL 1회 import 만 한다.
/// 즉 지금까지는 사람이 손으로 DB-*.sql 을 돌렸다(헌법 #39 운영 손수술의 근원).
/// MigrationRunner 는 이 손수술을 schema_migrations 기준의 검증된 멱등 적용으로 대체한다.
///
/// 책임(① 범위):
///  1) Migrations/SQL/ 폴더의 DB-NN_*.sql 을 번호순 정렬.
///  2) schema_migrations 에서 이미 success=1 로 적용된 것은 제외(멱등).
///  3) 미적용분만 순서대로 실행 + schema_migrations 에 INSERT(성공/실패 모두 기록).
///  4) 실패 시: 해당 마이그 success=0 기록 + 즉시 중단(다음 마이그 진행 금지) + 명확한 로그.
///
/// ★ 범위 밖(②③, 절대 손대지 않음):
///  - 자동 롤백 호출 0건. MariaDB DDL 은 암묵 커밋이라 트랜잭션 롤백 불가(설계서 R1) →
///    실패 시 "기록 + 중단"까지만. 전체 덤프 복원 롤백은 ③단계(상태머신) 범위.
///  - 앱 시작 시 자동 실행 배선 0건. Program.cs / 워치독 / 업데이트 결선은 ②단계(Velopack 적용 시점).
///    ① 단계는 "클래스 존재 + 호출하면 동작"까지만. 운영 영향 0.
///
/// 헌법: #1 추가만 / #13 기존 형식 준수 / #15 빈 catch 금지 / #16 단일 순차(Task.WhenAll 금지) /
///       #26 timeout = 물리 한계(86400초), 목표시간(10분) 아님 / #36 clean DDL 단일 진실원.
/// </summary>
public interface IMigrationRunner
{
    /// <summary>
    /// 미적용 DB-*.sql 마이그를 번호순·멱등·순차로 적용한다.
    /// 실패하면 해당 지점에서 중단하고 결과에 실패 정보를 담아 반환한다(예외를 삼키지 않고 결과로 보고).
    /// </summary>
    /// <param name="appVersion">이 적용을 유발한 앱 버전(SemVer). 수동 호출 시 null 허용 — schema_migrations.app_version 에 기록.</param>
    /// <param name="ct">취소 토큰.</param>
    Task<MigrationRunResult> ApplyPendingAsync(string? appVersion = null, CancellationToken ct = default);
}

/// <summary>
/// MigrationRunner 실행 결과 요약. (① 단계 — 호출자가 적용 현황을 판단할 수 있게 한다)
/// </summary>
public sealed class MigrationRunResult
{
    /// <summary>전체 성공(미적용분을 모두 적용했거나, 적용할 것이 없었음) 여부.</summary>
    public bool Success { get; init; }

    /// <summary>이번 실행에서 새로 성공 적용한 마이그 식별자 목록(예: "DB-84").</summary>
    public IReadOnlyList<string> AppliedMigrationIds { get; init; } = Array.Empty<string>();

    /// <summary>이미 적용돼 건너뛴 마이그 수(멱등 skip).</summary>
    public int SkippedCount { get; init; }

    /// <summary>실패한 마이그 식별자(성공 시 null). 이 지점에서 중단됐음을 의미.</summary>
    public string? FailedMigrationId { get; init; }

    /// <summary>실패 사유(성공 시 null).</summary>
    public string? FailureMessage { get; init; }
}
