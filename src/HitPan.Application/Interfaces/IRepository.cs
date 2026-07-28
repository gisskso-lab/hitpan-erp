using System.Linq.Expressions;
using HitPan.Domain.Common;

namespace HitPan.Application.Interfaces;

public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(string id);
    Task<IReadOnlyList<T>> GetAllAsync();
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task AddAsync(T entity);
    void Update(T entity);
    void Remove(T entity);

    /// <summary>
    /// 엔티티를 ChangeTracker 에서 추적 해제(Detached)한다. SaveChanges 실패로 Added 상태가 남은
    /// 엔티티를 떼어내, 이후 같은 DbContext 의 SaveChanges 가 그 좀비 엔티티를 재flush 하지 않게 한다.
    /// (Remove 는 소프트삭제라 추적이 유지되므로 이 용도에 쓸 수 없음 — 13차 emp_no 백필 교차검증 교훈.)
    /// </summary>
    void Detach(T entity);
}
