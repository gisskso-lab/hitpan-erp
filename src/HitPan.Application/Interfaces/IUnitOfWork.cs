using System.Data;
using System.Data.Common;
using HitPan.Domain.Common;

namespace HitPan.Application.Interfaces;

/// <summary>
/// EF + Dapper 공유 트랜잭션 핸들. SaveChanges + Dapper 쿼리를 원자적으로 묶기 위함.
/// </summary>
public interface ISharedTransaction : IAsyncDisposable, IDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
    /// <summary>Dapper CommandDefinition.transaction 파라미터에 넘길 DbTransaction.</summary>
    DbTransaction DbTransaction { get; }
}

public interface IUnitOfWork : IDisposable
{
    IRepository<T> Repository<T>() where T : BaseEntity;
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// EF와 Dapper가 동일 트랜잭션을 공유하도록 한다.
    /// 사용 패턴:
    ///   using var tx = await _unitOfWork.BeginTransactionAsync(ct);
    ///   // EF 쓰기 — await _unitOfWork.SaveChangesAsync(ct);
    ///   // Dapper 쓰기 — await conn.ExecuteAsync(..., transaction: tx.DbTransaction, ct);
    ///   await tx.CommitAsync(ct);
    /// </summary>
    Task<ISharedTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>EF가 사용하는 실연결을 반환한다. (Dapper와 공유)</summary>
    DbConnection GetDbConnection();
}
