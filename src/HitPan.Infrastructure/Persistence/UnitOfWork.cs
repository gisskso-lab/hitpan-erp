using System.Collections;
using HitPan.Application.Interfaces;
using HitPan.Domain.Common;

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
