using System.Collections;
using System.Data.Common;
using HitPan.Application.Interfaces;
using HitPan.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HitPan.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private Hashtable? _repositories;
    private bool _disposed;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IRepository<T> Repository<T>() where T : BaseEntity
    {
        _repositories ??= new Hashtable();

        var type = typeof(T).Name;
        if (!_repositories.ContainsKey(type))
        {
            var repository = new Repository<T>(_context);
            _repositories.Add(type, repository);
        }

        return (IRepository<T>)_repositories[type]!;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }

    public async Task<ISharedTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        var efTx = await _context.Database.BeginTransactionAsync(ct);
        return new SharedTransaction(efTx);
    }

    public DbConnection GetDbConnection()
    {
        return _context.Database.GetDbConnection();
    }

    private sealed class SharedTransaction : ISharedTransaction
    {
        private readonly IDbContextTransaction _tx;
        public SharedTransaction(IDbContextTransaction tx) { _tx = tx; }
        public DbTransaction DbTransaction => _tx.GetDbTransaction();
        public Task CommitAsync(CancellationToken ct = default) => _tx.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => _tx.RollbackAsync(ct);
        public ValueTask DisposeAsync() => _tx.DisposeAsync();
        public void Dispose() => _tx.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _context.Dispose();
        }

        _disposed = true;
    }
}
