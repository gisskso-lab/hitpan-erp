using System.Data.Common;

namespace HitPan.Application.Interfaces;

/// <summary>
/// MDB 마이그레이션 전용 DB 커넥션 팩토리.
/// (정공법 — 사장님 6축 명령 2026-05-14, SEC-04 정공법 + 헌법 #16 thread-safe)
///
/// 기존 봉합(00c8deb WS-07 RestoreSessionAsync catch + Dispose)은 단일 IDbConnection 공유 가정의
/// 반쪽 처방이었다. 정공법은 마이그 잡 자체가 일반 컨트롤러와 0 공유 connection을 사용하는 것이다.
///
/// 책임:
///  1) 일반 컨트롤러 풀(InfrastructureExtensions의 AddScoped&lt;IDbConnection&gt;)과 물리 분리된 신규 connection을 발급.
///  2) 마이그 전용 연결 옵션(AllowLoadLocalInfile=true, DefaultCommandTimeout 더 길게, Pooling=true)을 적용.
///  3) 11개 PANDATA 테이블 Task.WhenAll 병렬 시 각 잡이 독립 connection을 받게 함 (헌법 #16).
///
/// 사용 패턴:
///   using var conn = await factory.CreateOpenAsync(ct);
///   using var tx = conn.BeginTransaction();
///   ...
/// </summary>
public interface IMigrationDbConnectionFactory
{
    /// <summary>
    /// 마이그 전용 풀에서 새 DbConnection을 발급하고 열어서 반환한다.
    /// 호출자가 using으로 Dispose 책임을 진다(반환되면 pool 복귀).
    /// SET SESSION 튜닝(innodb_flush=2, fk=0, unique=0)은 이 connection에만 적용되므로
    /// pool 반환 시 reset 옵션으로 자동 원복된다(MySqlConnector 기본 ConnectionReset=true).
    /// </summary>
    Task<DbConnection> CreateOpenAsync(CancellationToken ct = default);
}
